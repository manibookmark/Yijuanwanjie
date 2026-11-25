using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 卡牌悬停交互组件
    /// hover 时：放大、上浮、置顶（SetAsLastSibling）
    /// 退出时：立即恢复 sibling index，平滑动画回归原位
    /// 快速划过时：Update 兜底检测，确保不卡在 hover 状态
    /// 模块审查：本组件只拥有单卡 hover 动画与当前 hover 引用。对象池拥有
    /// 激活状态；OnDisable 只清理本地状态，层级回归由后续布局或 Update 完成，
    /// 避免在激活事务中重挂父级。本次不加入独立生命周期职责。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CardHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("动画参数")]
        [SerializeField] private float _hoverScale = 1.20f;
        [SerializeField] private float _hoverLiftY = 60f;
        [SerializeField] private float _enterDuration = 0.18f;
        [SerializeField] private float _exitDuration = 0.22f;
        [SerializeField] private AnimationCurve _easeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("描述详情")]
        [SerializeField] private bool _enlargeDescriptionOnHover = true;
        [SerializeField] private int _hoverDescFontSize = 24;
        [SerializeField] private float _hoverDescHeightMultiplier = 2.5f;

        // ── 原始状态（由 HandView.RefreshHand 维护）──
        private RectTransform _rt;          // 根节点（交互层，不旋转）
        private RectTransform _visualRt;      // VisualRoot（视觉层，旋转缩放）
        private Vector2 _originalPos;
        private Quaternion _originalRot;
        private int _originalSiblingIndex;
        private float _originalScale;
        private Transform _originalParent;

        // ── 运行时状态 ──
        private bool _isHovering;
        private static CardHoverHandler _currentHover; // 全局唯一：同时只允许一张卡 hover
        private Coroutine _hoverCoroutine;
        private Coroutine _exitCheckCoroutine;
        private Coroutine _descFontCoroutine;
        private Coroutine _descSizeCoroutine;
        private bool _interactionEnabled = true;
        private bool _layoutRestorePending;
        private int _hoverGeneration;
        private EventSystem _hoverEventSystem;
        private PointerEventData _hoverPointerData;

        // ── 描述文字缓存 ──
        private Text _descText;
        private int _originalDescFontSize;
        private Vector2 _originalDescAnchorMin;
        private Vector2 _originalDescAnchorMax;

        // ── Update 检测用局部变量，不再缓存 ──

        #region 公开属性（HandView 刷新时写入）

        public Vector2 OriginalPos
        {
            get => _originalPos;
            set => _originalPos = value;
        }

        public Quaternion OriginalRot
        {
            get => _originalRot;
            set => _originalRot = value;
        }

        public int OriginalSiblingIndex
        {
            get => _originalSiblingIndex;
            set => _originalSiblingIndex = value;
        }

        public float OriginalScale
        {
            get => _originalScale;
            set => _originalScale = value;
        }

        public bool IsHovering => _isHovering;
        public bool InteractionEnabled => _interactionEnabled;

        /// <summary>hover 进入时触发（参数：卡牌 SortedIndex）</summary>
        public System.Action<int> OnHoverEnter;
        /// <summary>hover 退出时触发</summary>
        public System.Action OnHoverExit;
        /// <summary>hover 进入时请求连击预览（参数：CardView）</summary>
        public System.Action<WanjieScroll.UI.Views.CardView> OnRequestComboPreview;
        /// <summary>hover 退出时请求恢复</summary>
        public System.Action OnClearComboPreview;

        /// <summary>
        /// 是否正在执行 hover 进入/退出动画。RefreshHand 应跳过正在动画的卡牌，
        /// 避免覆盖动画目标位置导致视觉跳动。
        /// </summary>
        public bool IsAnimating => _isHovering || _hoverCoroutine != null;

        /// <summary>Layout has a new target; a returning animation must relinquish it.</summary>
        public void CancelReturnForLayout()
        {
            if (_isHovering) return;
            StopAllHoverCoroutines();
            RestorePendingLayout();
            RestoreLocalVisuals();
        }

        /// <summary>Rebinding a pooled slot invalidates all hover state of its old card.</summary>
        public void ResetForLayout() => ResetHoverImmediately();

        #endregion

        public void ApplyPresentation(CardPresentationProfile profile)
        {
            if (profile == null) return;
            _hoverScale = profile.hoverScale;
            _hoverLiftY = profile.hoverLiftY;
            _enterDuration = profile.hoverEnterDuration;
            _exitDuration = profile.hoverExitDuration;
            _hoverDescFontSize = profile.hoverDescriptionFontSize;
            _hoverDescHeightMultiplier = profile.descriptionFontSize > 0
                ? profile.hoverDescriptionFontSize / (float)profile.descriptionFontSize
                : 2.5f;
            if (_rt != null) FindDescriptionText();
        }

        private void Awake()
        {
            _rt = GetComponent<RectTransform>();
            // 获取 VisualRoot（视觉层，旋转缩放在这里）
            var cv = GetComponent<WanjieScroll.UI.Views.CardView>();
            _visualRt = cv?.visualRoot ?? _rt;
            _originalPos = _rt.anchoredPosition;
            _originalRot = _visualRt.localRotation;
            _originalScale = _visualRt.localScale.x;
            _originalParent = _rt.parent;
            FindDescriptionText();
        }

        private void FindDescriptionText()
        {
            var descTr = _visualRt.Find("DescriptionText");
            if (descTr == null) return;
            _descText = descTr.GetComponent<Text>();
            if (_descText == null) return;
            _originalDescFontSize = _descText.fontSize;
            _originalDescAnchorMin = _descText.rectTransform.anchorMin;
            _originalDescAnchorMax = _descText.rectTransform.anchorMax;
        }

        #region hover 入口

        /// <summary>
        /// 全局输入关闭时立即复位，避免卡牌继续上浮、发光或展示预览。
        /// </summary>
        public void SetInteractionEnabled(bool enabled)
        {
            _interactionEnabled = enabled;
            if (!enabled && (_isHovering || _hoverCoroutine != null))
                ResetHoverImmediately();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || _rt == null || _visualRt == null)
                return;
            RestorePendingLayout();
            if (_layoutRestorePending) return;
            if (!TopmostHoverGate.IsTopmost(eventData, gameObject))
                return;
            var diagnosticCard = GetComponent<
                WanjieScroll.UI.Views.CardView>();
            CanvasGroup diagnosticGroup = CombatLogger.EnableVerboseDiagnostics
                ? GetComponent<CanvasGroup>()
                : null;
            if (!_interactionEnabled || _isHovering)
            {
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[CombatHoverDiag] Card pointer enter rejected; " +
                        $"instance={diagnosticCard?.CardData.runtimeInstanceId ?? 0}, " +
                        $"sorted={diagnosticCard?.SortedIndex ?? -1}, " +
                        $"interaction={_interactionEnabled}, hovering=" +
                        $"{_isHovering}, groupInteractable=" +
                        $"{diagnosticGroup?.interactable}, groupRaycasts=" +
                        $"{diagnosticGroup?.blocksRaycasts}, eventSystem=" +
                        $"{EventSystem.current?.name ?? "null"}");
                return;
            }

            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[CombatHoverDiag] Card pointer enter accepted; " +
                    $"instance={diagnosticCard?.CardData.runtimeInstanceId ?? 0}, " +
                    $"sorted={diagnosticCard?.SortedIndex ?? -1}, " +
                    $"groupInteractable={diagnosticGroup?.interactable}, " +
                    $"groupRaycasts={diagnosticGroup?.blocksRaycasts}, " +
                    $"eventSystem={EventSystem.current?.name ?? "null"}");

            // 如果有其他卡牌正在 hover，强制把它踢出去
            if (_currentHover != null && _currentHover != this && _currentHover._isHovering)
            {
                _currentHover.ExitHover();
            }

            _isHovering = true;
            _currentHover = this;
            int generation = ++_hoverGeneration;

            // 触发 hover 进入事件
            var cv = diagnosticCard;
            OnHoverEnter?.Invoke(cv != null ? cv.SortedIndex : -1);
            if (!IsCurrentHover(generation)) return;

            // 不改 sibling index，通过 parent 切换实现置顶
            _originalParent = _rt.parent;
            // 找到同级的最后一个子物体作为 hoverLayer
            var hoverLayer = _originalParent != null
                ? _originalParent.parent?.Find("HoverLayer") : null;
            if (hoverLayer != null && hoverLayer.gameObject.activeInHierarchy)
            {
                _rt.SetParent(hoverLayer, false);
            }

            // 显示 hover 发光效果（边缘发光 + 外散光）
            if (cv != null)
                cv.SetHighlight(true);

            // 请求连击预览
            OnRequestComboPreview?.Invoke(cv);
            if (!IsCurrentHover(generation)) return;

            StopAllHoverCoroutines();
            _hoverCoroutine = StartCoroutine(AnimateHover());

            if (_enlargeDescriptionOnHover && _descText != null)
                StartDescAnimation(_hoverDescFontSize, _hoverDescHeightMultiplier, _enterDuration);
        }

        #endregion

        #region hover 退出（三条路径统一调 ExitHover）

        public void OnPointerExit(PointerEventData eventData)
        {
            // 不在这里退出！Hover 锁定模式：退出检测完全由 Update() 负责
        }

        private void Update()
        {
            RestorePendingLayout();
            if (!_isHovering) return;

            // A modal/pile panel can open while the pointer remains stationary.
            // In that case Unity may not send a fresh exit to the card, so clear
            // the stale hover as soon as the topmost raycast belongs elsewhere.
            EventSystem eventSystem = EventSystem.current;
            if (_hoverPointerData == null || _hoverEventSystem != eventSystem)
            {
                _hoverEventSystem = eventSystem;
                _hoverPointerData = new PointerEventData(eventSystem);
            }
            _hoverPointerData.Reset();
            _hoverPointerData.position = Input.mousePosition;
            if (!TopmostHoverGate.IsTopmost(_hoverPointerData, gameObject))
            {
                ExitHover();
                return;
            }

            // Hover 锁定模式：用 RectTransformUtility 判断鼠标是否还在根节点范围内
            // 根节点不旋转（VisualRoot架构），所以 ScreenPointToLocalPointInRectangle 稳定
            // 上浮 60px 不影响判断，因为 rect 高 300px，60px 偏移仍在范围内
            Camera cam = null;
            var canvas = _rt.GetComponentInParent<Canvas>();
            if (canvas != null) cam = canvas.worldCamera;
            if (cam == null) cam = Camera.main;

            Vector2 localPoint;
            bool isInside = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rt, Input.mousePosition, cam, out localPoint);

            if (!isInside || !_rt.rect.Contains(localPoint))
            {
                ExitHover();
            }
        }

        /// <summary>
        /// 统一退出逻辑：立即恢复 parent，平滑动画回归
        /// </summary>
        public void ExitHover()
        {
            if (!_isHovering) return;
            if (!isActiveAndEnabled)
            {
                ResetHoverImmediately(restoreHierarchy: false);
                return;
            }
            _isHovering = false;
            _hoverGeneration++;
            if (_currentHover == this) _currentHover = null;

            // 关闭 hover 发光效果
            var cv = GetComponent<WanjieScroll.UI.Views.CardView>();
            if (cv != null)
                cv.SetHighlight(false);

            // 移回原始 parent（追加到末尾，不设 SetSiblingIndex）
            if (_originalParent != null &&
                _originalParent.gameObject.activeInHierarchy &&
                _rt.parent != _originalParent)
            {
                _rt.SetParent(_originalParent, false);
            }

            // 触发 HandView 重排所有卡牌的 sibling index
            ReorderAllSiblings();

            // 平滑动画回归
            StopAllHoverCoroutines();
            if (_originalParent != null && !_originalParent.gameObject.activeInHierarchy)
            {
                _layoutRestorePending = true;
                RestoreLocalVisuals();
            }
            else
            {
                _hoverCoroutine = StartCoroutine(AnimateReturn());
                if (_enlargeDescriptionOnHover && _descText != null)
                    StartDescAnimation(_originalDescFontSize, 1f, _exitDuration);
            }

            // 所有本地写入先完成；回调可能隐藏或换绑当前对象池槽位。
            OnHoverExit?.Invoke();
            OnClearComboPreview?.Invoke();
        }

        /// <summary>
        /// 找到同级的所有 CardView，按 _cardViews 数组顺序重排 sibling index
        /// </summary>
        private void ReorderAllSiblings()
        {
            if (_originalParent == null) return;
            // 找到 HandView
            var handView = _originalParent.GetComponentInParent<WanjieScroll.UI.Views.HandView>();
            if (handView == null) return;
            // 调用 HandView 的公开重排方法
            handView.ReorderSiblings();
        }

        #endregion

        #region 动画

        private IEnumerator AnimateHover()
        {
            Vector2 startPos = _rt.anchoredPosition;
            float startScale = _visualRt.localScale.x;
            Quaternion startRot = _visualRt.localRotation;

            // 沿卡牌倾斜方向往外抽出，不旋转到正方向
            float angleDeg = _originalRot.eulerAngles.z;
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector2 liftDir = new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad)).normalized;
            Vector2 targetPos = _originalPos + liftDir * _hoverLiftY;
            float targetScale = _originalScale * _hoverScale;
            Quaternion targetRot = _originalRot;

            yield return AnimateTransform(startPos, targetPos, startScale, targetScale,
                                         startRot, targetRot, _enterDuration);
            _hoverCoroutine = null;
        }

        private IEnumerator AnimateReturn()
        {
            Vector2 startPos = _rt.anchoredPosition;
            float startScale = _visualRt.localScale.x;
            Quaternion startRot = _visualRt.localRotation;

            yield return AnimateTransform(startPos, _originalPos, startScale, _originalScale,
                                         startRot, _originalRot, _exitDuration);

            // 确保 parent 已恢复
            if (_originalParent != null &&
                _originalParent.gameObject.activeInHierarchy &&
                _rt.parent != _originalParent)
            {
                _rt.SetParent(_originalParent, false);
            }
            // 统一重排
            ReorderAllSiblings();
            _hoverCoroutine = null;
        }

        private IEnumerator AnimateTransform(
            Vector2 startPos, Vector2 targetPos,
            float startScale, float targetScale,
            Quaternion startRot, Quaternion targetRot,
            float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float curve = _easeCurve.Evaluate(Mathf.Clamp01(t / duration));
                // 位置在根节点（不旋转，射线稳定）
                _rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, curve);
                // 旋转和缩放在 VisualRoot（视觉层）
                float s = Mathf.Lerp(startScale, targetScale, curve);
                _visualRt.localScale = new Vector3(s, s, 1f);
                _visualRt.localRotation = Quaternion.Lerp(startRot, targetRot, curve);
                yield return null;
            }
            _rt.anchoredPosition = targetPos;
            _visualRt.localScale = new Vector3(targetScale, targetScale, 1f);
            _visualRt.localRotation = targetRot;
        }

        private void StartDescAnimation(int targetFontSize, float heightMultiplier, float duration)
        {
            if (_descFontCoroutine != null) StopCoroutine(_descFontCoroutine);
            if (_descSizeCoroutine != null) StopCoroutine(_descSizeCoroutine);
            _descFontCoroutine = StartCoroutine(AnimateDescFont(targetFontSize, duration));
            _descSizeCoroutine = StartCoroutine(AnimateDescSize(heightMultiplier, duration));
        }

        private IEnumerator AnimateDescFont(int targetFontSize, float duration)
        {
            int startFontSize = _descText.fontSize;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float curve = _easeCurve.Evaluate(Mathf.Clamp01(t / duration));
                _descText.fontSize = Mathf.RoundToInt(Mathf.Lerp(startFontSize, targetFontSize, curve));
                yield return null;
            }
            _descText.fontSize = targetFontSize;
        }

        private IEnumerator AnimateDescSize(float heightMultiplier, float duration)
        {
            var descRt = _descText.rectTransform;
            Vector2 startMin = descRt.anchorMin;
            Vector2 startMax = descRt.anchorMax;

            float originalHeight = _originalDescAnchorMax.y - _originalDescAnchorMin.y;
            float targetHeight = originalHeight * heightMultiplier;
            float centerY = (_originalDescAnchorMin.y + _originalDescAnchorMax.y) * 0.5f;
            Vector2 targetMin = new Vector2(_originalDescAnchorMin.x, centerY - targetHeight * 0.5f);
            Vector2 targetMax = new Vector2(_originalDescAnchorMax.x, centerY + targetHeight * 0.5f);

            if (heightMultiplier > 1f)
            {
                startMin = _originalDescAnchorMin;
                startMax = _originalDescAnchorMax;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float curve = _easeCurve.Evaluate(Mathf.Clamp01(t / duration));
                descRt.anchorMin = Vector2.Lerp(startMin, targetMin, curve);
                descRt.anchorMax = Vector2.Lerp(startMax, targetMax, curve);
                yield return null;
            }
            descRt.anchorMin = targetMin;
            descRt.anchorMax = targetMax;
        }

        #endregion

        private void StopAllHoverCoroutines()
        {
            if (_hoverCoroutine != null) { StopCoroutine(_hoverCoroutine); _hoverCoroutine = null; }
            if (_exitCheckCoroutine != null)
            {
                StopCoroutine(_exitCheckCoroutine);
                _exitCheckCoroutine = null;
            }
            if (_descFontCoroutine != null)
            {
                StopCoroutine(_descFontCoroutine);
                _descFontCoroutine = null;
            }
            if (_descSizeCoroutine != null)
            {
                StopCoroutine(_descSizeCoroutine);
                _descSizeCoroutine = null;
            }
        }

        private bool IsCurrentHover(int generation) =>
            this != null && isActiveAndEnabled && _isHovering && _currentHover == this &&
            generation == _hoverGeneration;

        private void ResetHoverImmediately(bool restoreHierarchy = true)
        {
            bool notifyExit = _isHovering || _currentHover == this;
            _isHovering = false;
            _hoverGeneration++;
            if (_currentHover == this) _currentHover = null;
            StopAllHoverCoroutines();
            _layoutRestorePending = true;
            RestoreLocalVisuals();

            if (restoreHierarchy && isActiveAndEnabled)
                RestorePendingLayout();

            if (notifyExit)
            {
                OnHoverExit?.Invoke();
                OnClearComboPreview?.Invoke();
            }
        }

        private void RestorePendingLayout()
        {
            if (!_layoutRestorePending || !isActiveAndEnabled || _rt == null)
                return;
            if (_originalParent != null && !_originalParent.gameObject.activeInHierarchy)
                return;

            _layoutRestorePending = false;

            var cv = GetComponent<WanjieScroll.UI.Views.CardView>();
            cv?.SetHighlight(false);

            if (_originalParent != null && _rt.parent != _originalParent)
                _rt.SetParent(_originalParent, false);
            ReorderAllSiblings();
            RestoreLocalVisuals();
        }

        private void RestoreLocalVisuals()
        {
            if (_rt != null) _rt.anchoredPosition = _originalPos;
            if (_visualRt != null)
            {
                _visualRt.localRotation = _originalRot;
                if (_originalScale > 0f)
                    _visualRt.localScale = new Vector3(
                        _originalScale, _originalScale, 1f);
            }
            if (_descText != null)
            {
                _descText.fontSize = _originalDescFontSize;
                _descText.rectTransform.anchorMin = _originalDescAnchorMin;
                _descText.rectTransform.anchorMax = _originalDescAnchorMax;
            }
        }

        private void OnDisable()
        {
            // SetActive(false) 尚未返回时，重挂父级会再次进入原生激活事务。
            // 不切换层级、排序或高亮节点；下一次池布局/Update 负责恢复。
            ResetHoverImmediately(restoreHierarchy: false);
        }
    }
}
