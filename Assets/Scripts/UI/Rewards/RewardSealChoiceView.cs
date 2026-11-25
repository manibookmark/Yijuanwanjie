using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 灵印奖励候选的纸面与共享 Tooltip 生命周期。只承载已确定的灵印主体，
    /// 不伪造 CardConfig、预选宿主或提交奖励；悬停运动交给共同候选动画组件。
    /// </summary>
    internal sealed class RewardSealChoiceView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private RewardFeedbackItem _item;
        private CardTooltipView _tooltip;
        private Button _button;
        private GameObject _highlight;
        private bool _hovered;
        private bool _tooltipVisible;

        internal static Button Create(
            Transform parent, RewardFeedbackItem item, CardTooltipView tooltip,
            CardPresentationProfile profile, float presentationScale)
        {
            var root = new GameObject("SealRewardChoice", typeof(RectTransform),
                typeof(CanvasGroup), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = profile.referenceSize;
            rect.localScale = Vector3.one * presentationScale;
            Image hitArea = root.GetComponent<Image>();
            hitArea.color = Color.clear;
            hitArea.raycastTarget = true;
            Button button = root.GetComponent<Button>();
            button.targetGraphic = hitArea;
            button.transition = Selectable.Transition.None;

            var visualObject = new GameObject("VisualRoot", typeof(RectTransform));
            visualObject.transform.SetParent(root.transform, false);
            RectTransform visual = visualObject.GetComponent<RectTransform>();
            RewardPanelFactory.Stretch(visual);
            Image paper = CreateImage(visual, "Paper");
            RewardPanelFactory.Stretch(paper.rectTransform);
            paper.color = profile.cardBaseColor;
            if (profile.paperFiberSprite != null)
            {
                Image fiber = CreateImage(visual, "PaperFiber");
                RewardPanelFactory.Stretch(fiber.rectTransform);
                fiber.sprite = profile.paperFiberSprite;
                fiber.color = new Color(1f, 1f, 1f, profile.paperFiberAlpha);
            }
            Color ink = profile.bodyTextColor;
            CreateOutline(visual, "ThinBorder", profile,
                Mathf.Max(1, Mathf.RoundToInt(profile.borderWidth)),
                new Color(ink.r, ink.g, ink.b, 0.32f));

            Text type = CreateText(visual, "Type",
                string.IsNullOrWhiteSpace(item.DisplayName) ? "灵印" : item.DisplayName,
                profile.nameFontSize, profile.titleFont, ink);
            profile.nameRect.Apply(type.rectTransform);
            if (item.Artwork != null)
            {
                Image artwork = CreateImage(visual, "SealArtwork");
                artwork.sprite = item.Artwork;
                artwork.preserveAspect = true;
                artwork.rectTransform.anchorMin = artwork.rectTransform.anchorMax =
                    new Vector2(0.5f, 0.53f);
                artwork.rectTransform.sizeDelta = Vector2.one *
                    Mathf.Min(profile.referenceSize.x * 0.84f,
                        profile.referenceSize.y * 0.56f);
            }
            else
            {
                // 专用印图尚未制作时，沿用同一纸面、字体和灵印色显示印字，
                // 不用其他灵印素材代替；完整名称仍由上方标题统一保留。
                int symbolSize = Mathf.Max(profile.nameFontSize,
                    Mathf.RoundToInt(Mathf.Min(profile.referenceSize.x * 0.58f,
                        profile.referenceSize.y * 0.42f)));
                Text fallback = CreateText(visual, "SealSymbol",
                    string.IsNullOrWhiteSpace(item.Symbol) ? "印" : item.Symbol,
                    symbolSize, profile.titleFont, item.Accent);
                profile.artworkRect.Apply(fallback.rectTransform);
                fallback.resizeTextForBestFit = true;
                fallback.resizeTextMinSize = profile.nameFontSize;
                fallback.resizeTextMaxSize = symbolSize;
            }
            Text purpose = CreateText(visual, "Purpose", "钤入已有卡牌",
                profile.descriptionFontSize, profile.bodyFont, ink);
            profile.descriptionRect.Apply(purpose.rectTransform);

            GameObject highlight = CreateOutline(visual, "HoverOutline", profile, 2,
                new Color(item.Accent.r, item.Accent.g, item.Accent.b,
                    Mathf.Clamp01(profile.glowRimAlpha)));
            highlight.SetActive(false);
            RewardSealChoiceView view = root.AddComponent<RewardSealChoiceView>();
            view._item = item;
            view._tooltip = tooltip;
            view._button = button;
            view._highlight = highlight;
            root.AddComponent<RewardCardHoverMotion>().Bind(visual, view.SetHighlight);
            return button;
        }

        private static Image CreateImage(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static GameObject CreateOutline(Transform parent, string name,
            CardPresentationProfile profile, int width, Color color)
        {
            int radius = Mathf.RoundToInt(profile.cornerRadius);
            if (radius > 0)
            {
                Image image = CreateImage(parent, name);
                RewardPanelFactory.Stretch(image.rectTransform);
                image.sprite = RoundedRectSpriteGenerator.GetOutline(radius, width);
                image.type = Image.Type.Sliced;
                image.fillCenter = false;
                image.color = color;
                return image.gameObject;
            }

            // A zero-radius sprite has no slice border, so Unity stretches its
            // whole outline texture. Square paper uses fixed UI line widths.
            var outline = new GameObject(name, typeof(RectTransform));
            outline.transform.SetParent(parent, false);
            RewardPanelFactory.Stretch(outline.GetComponent<RectTransform>());
            string[] edges = { "Left", "Right", "Bottom", "Top" };
            for (int i = 0; i < edges.Length; i++)
            {
                bool vertical = i < 2;
                float edge = i % 2;
                float inset = edge == 0f ? width * 0.5f : -width * 0.5f;
                Image line = CreateImage(outline.transform, edges[i]);
                line.color = color;
                RectTransform lineRect = line.rectTransform;
                lineRect.anchorMin = vertical
                    ? new Vector2(edge, 0f) : new Vector2(0f, edge);
                lineRect.anchorMax = vertical
                    ? new Vector2(edge, 1f) : new Vector2(1f, edge);
                lineRect.sizeDelta = vertical
                    ? new Vector2(width, -width * 2f) : new Vector2(0f, width);
                lineRect.anchoredPosition = vertical
                    ? new Vector2(inset, 0f) : new Vector2(0f, inset);
            }
            return outline;
        }

        private static Text CreateText(Transform parent, string name, string value,
            int size, Font font, Color color)
        {
            Text text = RewardPanelFactory.CreateText(parent, name, value, size,
                TextAnchor.MiddleCenter, color);
            if (font != null) text.font = font;
            return text;
        }

        private void SetHighlight(bool value)
        {
            if (_highlight == null) return;
            bool show = value && _button != null && _button.IsInteractable();
            if (_highlight.activeSelf != show) _highlight.SetActive(show);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            _hovered = true;
            TryShowTooltip();
        }

        private void Update()
        {
            if (_button == null || !_button.IsInteractable()) SetHighlight(false);
            if (!_hovered) return;
            if (_tooltipVisible && (_button == null || !_button.IsInteractable()))
                HideTooltip();
            else if (!_tooltipVisible)
                TryShowTooltip();
        }

        private void TryShowTooltip()
        {
            if (_tooltip == null || _button == null || !_button.IsInteractable()) return;
            _tooltip.transform.SetAsLastSibling();
            _tooltip.ShowText(_item.DisplayName + "\n" + _item.Description);
            _tooltipVisible = true;
        }

        public void OnPointerExit(PointerEventData eventData) => HideTooltip();
        public void OnPointerDown(PointerEventData eventData) => HideTooltip();
        private void OnDisable() => HideTooltip();

        private void HideTooltip()
        {
            _hovered = false;
            if (_tooltipVisible) _tooltip?.Hide();
            _tooltipVisible = false;
        }
    }
}
