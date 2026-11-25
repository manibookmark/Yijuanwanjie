using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 连击的轻量 HUD 投影。这里只拥有节奏和可见性状态；数字本身由
    /// ComboInkGraphic 用毛笔轮廓点云与灵光网格绘制，不加载图片、字体或外部特效。
    /// 根 RectTransform 的位置、尺寸和棋盘缩放仍由 BattleBoardHudLayout 持有。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup), typeof(Canvas))]
    public sealed class BrushComboView : MonoBehaviour
    {
        private const float StampDuration = 0.68f;
        private const float BreakDuration = 0.34f;
        private const float RetractDuration = 0.32f;

        private enum Motion
        {
            None,
            Stamp,
            Break,
            Retract
        }

        private CanvasGroup _group;
        private Canvas _canvas;
        private RectTransform _animationRoot;
        private ComboInkGraphic _ink;
        private Motion _motion;
        private float _elapsed;
        private float _duration;
        private float _startAlpha;
        private Vector3 _startScale;
        private Vector2 _startPosition;
        private float _viewportHeight = 180f;

        // _currentComboLength is the latest semantic value from the controller.
        // _displayComboLength is kept separate so a broken chain can disperse the
        // old glyph before the new chain stamps in.
        private int _currentComboLength;
        private int _displayComboLength;
        private int _pendingComboLength;
        private int _displayTier;
        private int _pendingTier;

        private void Awake()
        {
            // The board layout is created before the regular battle HUD and is
            // therefore behind it in the parent canvas hierarchy.  The combo is
            // a transient overlay, so give it its own sorted canvas; otherwise
            // the generated mesh can be fully covered even while its state and
            // alpha are correct.
            _canvas = GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = gameObject.AddComponent<Canvas>();
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 1000;
            _canvas.pixelPerfect = false;

            _group = GetComponent<CanvasGroup>();
            if (_group == null)
                _group = gameObject.AddComponent<CanvasGroup>();

            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _animationRoot = CreateRect("Animation", transform);
            _animationRoot.anchorMin = Vector2.zero;
            _animationRoot.anchorMax = Vector2.one;
            _animationRoot.pivot = new Vector2(0.5f, 0.5f);
            _animationRoot.offsetMin = _animationRoot.offsetMax = Vector2.zero;
            _animationRoot.anchoredPosition = Vector2.zero;

            _ink = _animationRoot.GetComponentInChildren<ComboInkGraphic>(true);
            if (_ink == null)
            {
                var inkObject = new GameObject(
                    "InkParticles",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(ComboInkGraphic));
                inkObject.transform.SetParent(_animationRoot, false);
                _ink = inkObject.GetComponent<ComboInkGraphic>();
            }

            RectTransform inkRect = _ink.rectTransform;
            inkRect.anchorMin = Vector2.zero;
            inkRect.anchorMax = Vector2.one;
            inkRect.pivot = new Vector2(0.5f, 0.5f);
            inkRect.offsetMin = inkRect.offsetMax = Vector2.zero;
            _ink.raycastTarget = false;
            _ink.SetReferenceHeight(_viewportHeight);
            _ink.ClearGlyph();
        }

        private void OnEnable()
        {
            TraceCombo("Enable");
            if (_group == null || _ink == null)
                return;

            if (_currentComboLength > 0)
            {
                _displayComboLength = _currentComboLength;
                _displayTier = TierFor(_displayComboLength);
                _motion = Motion.None;
                SetStable();
            }
            else
            {
                _motion = Motion.None;
                _group.alpha = 0f;
                _ink.ClearGlyph();
            }
        }

        private void OnDisable()
        {
            TraceCombo("Disable");
            if (_group != null)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }

            _motion = Motion.None;
            _ink?.ClearGlyph();
        }

        /// <summary>
        /// 让外部布局继续以视口高度驱动连击占用空间；实际墨点会按当前
        /// RectTransform 的宽高自适应，不再依赖数字贴图的宽高比。
        /// </summary>
        public void SetViewportHeight(float viewportHeight)
        {
            _viewportHeight = Mathf.Max(1f, viewportHeight);
            _ink?.SetReferenceHeight(_viewportHeight);
        }

        /// <summary>Clears the previous encounter without playing a chain break.</summary>
        public void ResetEncounterPresentation()
        {
            _currentComboLength = 0;
            _displayComboLength = 0;
            _pendingComboLength = 0;
            _displayTier = 0;
            _pendingTier = 0;
            _motion = Motion.None;
            _elapsed = 0f;
            _duration = 0f;
            if (_group != null)
                _group.alpha = 0f;
            _ink?.ClearGlyph();
        }

        /// <summary>
        /// comboLength 为 0 时只播放一次散墨；restartChain 表示旧链先断开，
        /// 再把新数字聚拢成形。时间全部走 unscaledDeltaTime，暂停菜单不会卡住退场。
        /// </summary>
        public void SetComboLength(int comboLength, bool restartChain = false)
        {
            comboLength = Mathf.Max(0, comboLength);
            int previousSemantic = _currentComboLength;
            int previousVisible = _displayComboLength;
            _currentComboLength = comboLength;

            if (previousSemantic == comboLength && !restartChain)
                return;

            if (CombatLogger.EnableVerboseDiagnostics)
                TraceCombo($"Receive previous={previousSemantic} requested={comboLength} restart={restartChain}");

            if (restartChain && previousVisible > 0)
            {
                _pendingComboLength = comboLength;
                _pendingTier = TierFor(comboLength);
                _displayTier = TierFor(previousVisible);
                BeginMotion(Motion.Break, BreakDuration);
                return;
            }

            if (comboLength <= 0)
            {
                _pendingComboLength = 0;
                if (previousVisible <= 0)
                {
                    _displayComboLength = 0;
                    _motion = Motion.None;
                    _group.alpha = 0f;
                    _ink.ClearGlyph();
                    return;
                }

                _displayTier = TierFor(previousVisible);
                BeginMotion(Motion.Retract, RetractDuration);
                return;
            }

            _pendingComboLength = comboLength;
            _pendingTier = TierFor(comboLength);
            _displayComboLength = comboLength;
            _displayTier = _pendingTier;
            BeginMotion(Motion.Stamp, StampDuration);
        }

        private void Update()
        {
            if (_motion == Motion.None)
                return;

            _elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.001f, _duration));
            ApplyMotion(progress);

            if (progress < 1f)
                return;

            if (_motion == Motion.Break)
            {
                if (_pendingComboLength > 0)
                {
                    _displayComboLength = _pendingComboLength;
                    _displayTier = _pendingTier;
                    BeginMotion(Motion.Stamp, StampDuration);
                }
                else
                {
                    _displayComboLength = 0;
                    _motion = Motion.None;
                    _group.alpha = 0f;
                    _ink.ClearGlyph();
                }
            }
            else if (_motion == Motion.Retract)
            {
                _displayComboLength = 0;
                _motion = Motion.None;
                _group.alpha = 0f;
                _ink.ClearGlyph();
            }
            else
            {
                SetStable();
            }
        }

        private void BeginMotion(Motion motion, float duration)
        {
            if (CombatLogger.EnableVerboseDiagnostics)
                TraceCombo($"Begin next={motion}");
            _motion = motion;
            _elapsed = 0f;
            _duration = Mathf.Max(0.001f, duration);
            _startAlpha = Mathf.Max(_group.alpha, motion == Motion.Stamp ? 0f : _group.alpha);
            _startScale = _animationRoot.localScale;
            _startPosition = _animationRoot.anchoredPosition;

            if (motion == Motion.Stamp && _displayComboLength > 0)
                _ink.SetGlyph(_displayComboLength.ToString(), 0f, _displayTier,
                    dispersing: false, broken: false);
            else if (motion == Motion.Break && _displayComboLength > 0)
                _ink.SetGlyph(_displayComboLength.ToString(), 0f, _displayTier,
                    dispersing: true, broken: true);
            else if (motion == Motion.Retract && _displayComboLength > 0)
                _ink.SetGlyph(_displayComboLength.ToString(), 0f, _displayTier,
                    dispersing: true, broken: false);

            ApplyMotion(0f);
        }

        private void ApplyMotion(float progress)
        {
            if (_ink == null)
                return;

            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            switch (_motion)
            {
                case Motion.Stamp:
                    // The brush lands sharply, then the energy trails finish;
                    // only one small recoil follows the hit, never a looping bounce.
                    float landing = Mathf.Clamp01(progress / 0.12f);
                    float recoil = Mathf.Clamp01((progress - 0.12f) / 0.88f);
                    float impulse = Mathf.Sin(recoil * Mathf.PI) *
                        Mathf.Pow(1f - recoil, 3f);
                    _group.alpha = Mathf.Lerp(_startAlpha, 1f, Mathf.Clamp01(progress * 9f));
                    _animationRoot.localScale = Vector3.Lerp(_startScale,
                        Vector3.one, landing) + Vector3.one *
                        (impulse * (0.08f + _displayTier * 0.015f));
                    _animationRoot.anchoredPosition = Vector2.Lerp(
                        _startPosition, Vector2.zero, landing) + new Vector2(4f, 1.5f) * impulse;
                    _ink.SetGlyph(_displayComboLength.ToString(), progress,
                        _displayTier, dispersing: false, broken: false);
                    break;

                case Motion.Break:
                    _group.alpha = Mathf.Lerp(_startAlpha, 0.08f, progress * progress);
                    _animationRoot.localScale = Vector3.Lerp(
                        _startScale, Vector3.one * 1.04f, eased);
                    _ink.SetGlyph(_displayComboLength.ToString(), progress,
                        _displayTier, dispersing: true, broken: true);
                    break;

                case Motion.Retract:
                    _group.alpha = Mathf.Lerp(_startAlpha, 0f, progress * progress);
                    _animationRoot.localScale = Vector3.Lerp(
                        _startScale, Vector3.one * 0.94f, eased);
                    _ink.SetGlyph(_displayComboLength.ToString(), progress,
                        _displayTier, dispersing: true, broken: false);
                    break;
            }
        }

        private void SetStable()
        {
            TraceCombo("Stable");
            _motion = Motion.None;
            _group.alpha = _displayComboLength > 0 ? 1f : 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _animationRoot.localScale = Vector3.one;
            _animationRoot.anchoredPosition = Vector2.zero;
            if (_displayComboLength > 0)
            {
                _ink.SetGlyph(_displayComboLength.ToString(), 1f, _displayTier,
                    dispersing: false, broken: false);
            }
            else
            {
                _ink.ClearGlyph();
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void TraceCombo(string action)
        {
            if (!CombatLogger.EnableVerboseDiagnostics) return;
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log($"[ComboTrace][View] frame={Time.frameCount} t={Time.unscaledTime:F3} " +
                    $"view={GetInstanceID()} ink={(_ink != null ? _ink.GetInstanceID() : 0)} " +
                    $"scene={gameObject.scene.name} active={isActiveAndEnabled} action={action} " +
                    $"semantic={_currentComboLength} display={_displayComboLength} pending={_pendingComboLength} " +
                    $"motion={_motion} elapsed={_elapsed:F3}/{_duration:F3} " +
                    $"alpha={(_group != null ? _group.alpha : 0f):F3}", this);
        }

        private static int TierFor(int comboLength)
        {
            if (comboLength >= 20) return 3;
            if (comboLength >= 10) return 2;
            if (comboLength >= 5) return 1;
            return 0;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform))
                .GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
