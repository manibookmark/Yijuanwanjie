using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Cards;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 卡牌 hover 提示面板。只显示卡牌上没有的补充信息。
    /// 始终 Active，用 CanvasGroup 控制显隐，保证首次显示时布局已计算。
    /// 宽度受限、高度自动；显示前强制刷新布局再定位，做屏幕边缘修正。
    /// 文本生成委托给 CardTextRenderer。
    /// </summary>
    public class CardTooltipView : MonoBehaviour
    {
        private Text _tooltipText;
        private Image _background;
        private CanvasGroup _canvasGroup;
        private Canvas _parentCanvas;
        private RectTransform _canvasRect;
        private RectTransform _rectTransform;
        private VerticalLayoutGroup _layoutGroup;
        private LayoutElement _layoutElement;
        private LayoutElement _textLayoutElement;
        private float _lastCanvasWidth = -1f;
        private bool _followsCanvasRender;

        // 尺寸
        [Header("尺寸")]
        [SerializeField] private float paddingX = 24f;
        [SerializeField] private float paddingY = 16f;
        [SerializeField] private float mouseOffset = 16f;
        [SerializeField] private float screenMargin = 12f;
        [SerializeField] private float minimumWidth = 220f;
        [SerializeField] private float maximumWidth = 420f;

        // 文字颜色
        private static readonly Color ValueColor = new Color(0.92f, 0.88f, 0.75f);

        private void Awake()
        {
            _parentCanvas = GetComponentInParent<Canvas>();
            _canvasRect = _parentCanvas != null
                ? _parentCanvas.GetComponent<RectTransform>()
                : null;

            // ── 根节点：始终 Active，用 CanvasGroup 控制显隐 ──
            _rectTransform = GetComponent<RectTransform>();
            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0f, 1f);

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false; // 防止 Tooltip 挡住卡牌导致 hover 闪烁

            // VerticalLayoutGroup：控制子元素垂直排列，撑开内容高度
            _layoutGroup = gameObject.AddComponent<VerticalLayoutGroup>();
            _layoutGroup.padding = new RectOffset(
                Mathf.RoundToInt(paddingX / 2f), Mathf.RoundToInt(paddingX / 2f),
                Mathf.RoundToInt(paddingY / 2f), Mathf.RoundToInt(paddingY / 2f));
            _layoutGroup.spacing = 4f;
            _layoutGroup.childControlWidth = true;
            _layoutGroup.childControlHeight = true;
            _layoutGroup.childForceExpandWidth = false;
            _layoutGroup.childForceExpandHeight = false;
            _layoutGroup.childAlignment = TextAnchor.UpperLeft;

            // 横向由 ShowText 根据自然宽度和最大宽度显式确定，纵向随换行内容撑开。
            var fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _layoutElement = gameObject.AddComponent<LayoutElement>();
            _layoutElement.preferredWidth = minimumWidth;

            // ── 背景：铺满根节点，ignoreLayout 不参与 VLG 计算 ──
            var bgGo = new GameObject("TooltipBg", typeof(Image));
            bgGo.transform.SetParent(transform, false);
            _background = bgGo.GetComponent<Image>();
            _background.color = new Color(0.05f, 0.04f, 0.03f, 0.88f);
            _background.raycastTarget = false;
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgLe = bgGo.AddComponent<LayoutElement>();
            bgLe.ignoreLayout = true;

            // ── 文字：由 VLG 控制宽度（=根内容宽度），换行后撑高 ──
            var textGo = new GameObject("TooltipText", typeof(Text));
            textGo.transform.SetParent(transform, false);
            _tooltipText = textGo.GetComponent<Text>();
            _tooltipText.font = Resources.Load<Font>("Fonts/NotoSerifSC-Medium")
                ?? Resources.Load<Font>("Fonts/NotoSerifSC-Heavy")
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _tooltipText.fontSize = 18;
            _tooltipText.lineSpacing = 1.4f;
            _tooltipText.alignment = TextAnchor.UpperLeft;
            _tooltipText.color = ValueColor;
            _tooltipText.raycastTarget = false;
            _tooltipText.supportRichText = true;
            _tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap; // 在受限宽度内换行
            _tooltipText.verticalOverflow = VerticalWrapMode.Overflow;
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            _textLayoutElement = textGo.AddComponent<LayoutElement>();
            _textLayoutElement.flexibleWidth = 1f;
            _textLayoutElement.minWidth = 1f;

            // 保持 Active，仅透明
            gameObject.SetActive(true);
        }

        private void BeforeCanvasRender()
        {
            // CanvasScaler的新尺寸可能在LateUpdate后才落实。只在可见期间订阅
            // 渲染前回调，让局部布局与定位读取本帧最终画布，不强制刷新全场Canvas。
            FollowMouse();
        }

        private void StopFollowingCanvas()
        {
            if (!_followsCanvasRender) return;
            Canvas.willRenderCanvases -= BeforeCanvasRender;
            _followsCanvasRender = false;
        }

        private void OnDisable() => Hide();
        private void OnDestroy() => StopFollowingCanvas();

        private void FollowMouse()
        {
            if (_parentCanvas == null || _canvasRect == null) return;
            if (_canvasGroup == null || _canvasGroup.alpha <= 0.001f)
                return;

            if (!Mathf.Approximately(
                    _lastCanvasWidth,
                    _canvasRect.rect.width))
            {
                RefreshLayout();
            }
            SetPosition(PointerToCanvasLocal());
        }

        private Vector2 PointerToCanvasLocal()
        {
            Camera cam = _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _parentCanvas.worldCamera;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect,
                Input.mousePosition, cam, out Vector2 localPoint);
            return localPoint;
        }

        private void SetPosition(Vector2 canvasLocal)
        {
            if (_rectTransform == null || _canvasRect == null)
                return;

            float width = _rectTransform.rect.width;
            float height = _rectTransform.rect.height;
            Rect canvasBounds = _canvasRect.rect;
            float horizontalMargin = Mathf.Min(
                Mathf.Max(0f, screenMargin),
                canvasBounds.width * 0.5f);
            float verticalMargin = Mathf.Min(
                Mathf.Max(0f, screenMargin),
                canvasBounds.height * 0.5f);
            float minX = canvasBounds.xMin + horizontalMargin;
            float maxX = canvasBounds.xMax - horizontalMargin;
            float minY = canvasBounds.yMin + verticalMargin;
            float maxY = canvasBounds.yMax - verticalMargin;

            bool fitsRight =
                canvasLocal.x + mouseOffset + width <= maxX;
            bool fitsLeft =
                canvasLocal.x - mouseOffset - width >= minX;
            bool placeRight = fitsRight ||
                (!fitsLeft &&
                 maxX - canvasLocal.x >= canvasLocal.x - minX);

            bool fitsBelow =
                canvasLocal.y - mouseOffset - height >= minY;
            bool fitsAbove =
                canvasLocal.y + mouseOffset + height <= maxY;
            bool placeBelow = fitsBelow ||
                (!fitsAbove &&
                 canvasLocal.y - minY >= maxY - canvasLocal.y);

            Vector2 pivot = new Vector2(
                placeRight ? 0f : 1f,
                placeBelow ? 1f : 0f);
            Vector2 pivotPosition = canvasLocal + new Vector2(
                placeRight ? mouseOffset : -mouseOffset,
                placeBelow ? -mouseOffset : mouseOffset);

            pivotPosition.x = ClampPivotCoordinate(
                pivotPosition.x,
                minX,
                maxX,
                width,
                pivot.x);
            pivotPosition.y = ClampPivotCoordinate(
                pivotPosition.y,
                minY,
                maxY,
                height,
                pivot.y);

            _rectTransform.pivot = pivot;
            _rectTransform.anchoredPosition =
                CanvasLocalToAnchored(pivotPosition);
        }

        private void RefreshLayout()
        {
            if (_tooltipText == null ||
                _rectTransform == null ||
                _canvasRect == null)
            {
                return;
            }

            float margin = Mathf.Max(0f, screenMargin);
            float availableWidth = Mathf.Max(
                1f,
                _canvasRect.rect.width - margin * 2f);
            float configuredMaximum = Mathf.Max(
                1f,
                Mathf.Max(minimumWidth, maximumWidth));
            float effectiveMaximum = Mathf.Min(
                configuredMaximum,
                availableWidth);
            float effectiveMinimum = Mathf.Min(
                Mathf.Max(1f, minimumWidth),
                effectiveMaximum);
            float horizontalPadding = _layoutGroup != null
                ? _layoutGroup.padding.horizontal
                : Mathf.Max(0f, paddingX);
            float naturalWidth =
                _tooltipText.preferredWidth + horizontalPadding;
            float resolvedWidth = Mathf.Clamp(
                naturalWidth,
                effectiveMinimum,
                effectiveMaximum);

            _rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                resolvedWidth);
            _layoutElement.preferredWidth = resolvedWidth;
            if (_textLayoutElement != null)
            {
                float textWidth = Mathf.Max(1f,
                    resolvedWidth - horizontalPadding);
                _textLayoutElement.minWidth = textWidth;
                _textLayoutElement.preferredWidth = textWidth;
            }
            _lastCanvasWidth = _canvasRect.rect.width;

            // 宽度已明确设置，一次局部布局即可计算换行与最终高度。
            // Hover 不得同步刷新全场 Canvas，避免拖慢其它 HUD 和奖励面板。
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rectTransform);
        }

        private Vector2 CanvasLocalToAnchored(Vector2 canvasLocal)
        {
            Rect bounds = _canvasRect.rect;
            Vector2 anchorReference = new Vector2(
                Mathf.Lerp(
                    bounds.xMin,
                    bounds.xMax,
                    _rectTransform.anchorMin.x),
                Mathf.Lerp(
                    bounds.yMin,
                    bounds.yMax,
                    _rectTransform.anchorMin.y));
            return canvasLocal - anchorReference;
        }

        private static float ClampPivotCoordinate(
            float value,
            float minimumEdge,
            float maximumEdge,
            float size,
            float pivot)
        {
            float minimum = minimumEdge + pivot * size;
            float maximum = maximumEdge - (1f - pivot) * size;
            return minimum <= maximum
                ? Mathf.Clamp(value, minimum, maximum)
                : (minimumEdge + maximumEdge) * 0.5f;
        }

        /// <summary>
        /// 显示提示面板。
        /// </summary>
        public void Show(CardConfig card, CombatController combat)
        {
            if (card == null)
            {
                Hide();
                return;
            }

            string text = CardTextRenderer.GetTooltipText(card, combat);
            ShowText(text);
        }

        /// <summary>
        /// Generic text entry used by other hover surfaces that share the
        /// same typography, layout and screen-edge behavior.
        /// </summary>
        public void ShowText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Hide();
                return;
            }

            // 内容与画布宽度未变时复用已测量的布局，包括移出后再次进入。
            bool textChanged = !string.Equals(
                _tooltipText.text, text, System.StringComparison.Ordinal);
            if (textChanged)
                _tooltipText.text = text;
            if (textChanged || !Mathf.Approximately(
                    _lastCanvasWidth, _canvasRect.rect.width))
                RefreshLayout();

            // 3. 定位（此刻 rect 尺寸已可信）
            SetPosition(PointerToCanvasLocal());

            // 4. 最后显示
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            if (transform.GetSiblingIndex() != transform.parent.childCount - 1)
                transform.SetAsLastSibling();
            if (!_followsCanvasRender)
            {
                Canvas.willRenderCanvases += BeforeCanvasRender;
                _followsCanvasRender = true;
            }
        }

        public void Hide()
        {
            StopFollowingCanvas();
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
    }
}
