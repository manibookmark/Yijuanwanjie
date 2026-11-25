using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 创建完整可用的卡牌组件层级。
    /// 视觉/交互分离架构：根节点负责交互（不旋转），VisualRoot 负责视觉（旋转缩放）。
    /// </summary>
    public static class CardViewFactory
    {
        public static CardView Create(
            Transform parent,
            CardPresentationProfile profile = null,
            bool addHoverHandler = true)
        {
            profile ??= CardPresentationProfile.LoadDefault();

            // ══ 根节点：交互层（不旋转、不缩放） ══
            var root = new GameObject(
                "CardView",
                typeof(RectTransform),
                typeof(CanvasGroup));
            root.transform.SetParent(parent, false);

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = profile.referenceSize;

            // 根节点全卡透明 Image — 射线检测区域（永远正交、不旋转）
            var hitArea = root.AddComponent<Image>();
            hitArea.color = Color.clear;
            hitArea.raycastTarget = true; // 全卡可点击

            var button = root.AddComponent<Button>();
            button.targetGraphic = hitArea;
            button.transition = Selectable.Transition.ColorTint;

            // ══ VisualRoot：视觉层（旋转、缩放、所有美术节点） ══
            var visualRoot = new GameObject("VisualRoot", typeof(RectTransform));
            visualRoot.transform.SetParent(root.transform, false);
            var visualRt = visualRoot.GetComponent<RectTransform>();
            visualRt.anchorMin = Vector2.zero;
            visualRt.anchorMax = Vector2.one;
            visualRt.offsetMin = Vector2.zero;
            visualRt.offsetMax = Vector2.zero;

            // 1. CardBackground
            Image background = CreateImage(visualRoot.transform, "CardBackground");
            Stretch(background.rectTransform);
            background.color = profile.cardBaseColor;

            // 2. Artwork
            Image art = CreateImage(visualRoot.transform, "Artwork");
            profile.artworkRect.Apply(art.rectTransform);
            art.preserveAspect = true;
            art.color = Color.clear;

            // 3. SealLayer
            var sealLayer = new GameObject("SealLayer", typeof(RectTransform));
            sealLayer.transform.SetParent(visualRoot.transform, false);
            profile.sealLayerRect.Apply(sealLayer.GetComponent<RectTransform>());

            // 4. TypeHeader
            Image typeHeader = CreateImage(visualRoot.transform, "TypeHeader");
            profile.typeHeaderRect.Apply(typeHeader.rectTransform);
            typeHeader.color = Color.clear;

            // 5. PaperFiber
            Image paperFiber = CreateImage(visualRoot.transform, "PaperFiber");
            Stretch(paperFiber.rectTransform);
            paperFiber.sprite = profile.paperFiberSprite;
            paperFiber.type = Image.Type.Simple;
            paperFiber.color = new Color(1f, 1f, 1f, profile.paperFiberAlpha);

            // 6. CostFrame + CostText
            Image costFrame = CreateImage(visualRoot.transform, "CostFrame");
            profile.costFrameRect.Apply(costFrame.rectTransform);
            costFrame.color = Color.clear;

            Text costText = CreateText(costFrame.transform, "CostText", TextAnchor.MiddleCenter);
            Stretch(costText.rectTransform);
            costText.horizontalOverflow = HorizontalWrapMode.Overflow;
            costText.verticalOverflow = VerticalWrapMode.Overflow;
            costText.resizeTextForBestFit = true;
            costText.resizeTextMinSize = profile.costMinFontSize;
            costText.resizeTextMaxSize = profile.costFontSize;

            // 7. NameText
            Text nameText = CreateText(visualRoot.transform, "NameText", TextAnchor.MiddleCenter);
            profile.nameRect.Apply(nameText.rectTransform);
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameText.verticalOverflow = VerticalWrapMode.Overflow;
            nameText.resizeTextForBestFit = true;
            nameText.resizeTextMinSize = profile.nameMinFontSize;
            nameText.resizeTextMaxSize = profile.nameFontSize;

            // 8. DescriptionText
            Text descriptionText = CreateText(visualRoot.transform, "DescriptionText", TextAnchor.MiddleCenter);
            profile.descriptionRect.Apply(descriptionText.rectTransform);
            descriptionText.supportRichText = true;
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow = VerticalWrapMode.Truncate;
            descriptionText.resizeTextForBestFit = true;
            descriptionText.resizeTextMinSize = profile.descriptionMinFontSize;
            descriptionText.resizeTextMaxSize = profile.descriptionFontSize;
            descriptionText.lineSpacing = 1f;

            // 9. ThinBorder
            Image thinBorder = CreateImage(visualRoot.transform, "ThinBorder");
            Stretch(thinBorder.rectTransform);
            int radius = Mathf.RoundToInt(profile.cornerRadius);
            int bw = Mathf.RoundToInt(profile.borderWidth);
            thinBorder.sprite = RoundedRectSpriteGenerator.GetOutline(radius, bw);
            thinBorder.type = Image.Type.Sliced;
            thinBorder.fillCenter = false;
            thinBorder.color = Color.clear;

            // 10. GlowScatter — 外散光（比卡牌大，类型色低alpha，hover时显示）
            float scatterSpread = profile.glowScatterSpread;
            Image glowScatter = CreateImage(visualRoot.transform, "GlowScatter");
            Stretch(glowScatter.rectTransform, new Vector2(-scatterSpread, -scatterSpread), new Vector2(scatterSpread, scatterSpread));
            glowScatter.color = Color.clear;
            glowScatter.raycastTarget = false;
            glowScatter.gameObject.SetActive(false);

            // 11. GlowProxy — 边缘发光（rim glow，fillCenter=false，仅边缘）
            Image glowProxy = CreateImage(visualRoot.transform, "GlowProxy");
            Stretch(glowProxy.rectTransform, new Vector2(-2f, -2f), new Vector2(2f, 2f));
            glowProxy.color = Color.clear;
            glowProxy.raycastTarget = false;
            glowProxy.gameObject.SetActive(false);

            // 12. HoverOrSelectedOutline
            Image hoverOutline = CreateImage(visualRoot.transform, "HoverOrSelectedOutline");
            Stretch(hoverOutline.rectTransform);
            hoverOutline.sprite = RoundedRectSpriteGenerator.GetOutline(radius, 2);
            hoverOutline.type = Image.Type.Sliced;
            hoverOutline.fillCenter = false;
            hoverOutline.color = Color.clear;
            hoverOutline.gameObject.SetActive(false);

            // 13. DisabledOverlay — 仅保留旧 Prefab/序列化引用兼容，运行时常关
            Image disabledOverlay = CreateImage(visualRoot.transform, "DisabledOverlay");
            Stretch(disabledOverlay.rectTransform);
            disabledOverlay.color = Color.clear;
            disabledOverlay.raycastTarget = false;
            disabledOverlay.gameObject.SetActive(false);

            // ══ 绑定 CardView ══
            var view = root.AddComponent<CardView>();
            view.visualRoot = visualRt; // 新增字段：视觉根节点
            view.cardArtImage = art;
            view.borderImage = thinBorder;
            view.nameText = nameText;
            view.costText = costText;
            view.descriptionText = descriptionText;
            view.cardButton = button;
            view.hoverHighlight = hoverOutline.gameObject;
            view.backgroundImage = background;
            view.paperFiberImage = paperFiber;
            view.typeHeaderImage = typeHeader;
            view.costFrameImage = costFrame;
            view.thinBorderImage = thinBorder;
            view.glowScatterImage = glowScatter;
            view.glowProxyImage = glowProxy;
            view.disabledOverlay = disabledOverlay;
            view.hoverOrSelectedOutline = hoverOutline;
            view.sealLayerTransform = sealLayer.transform;

            view.SetPresentationProfile(profile);
            if (addHoverHandler)
            {
                var hover = root.AddComponent<CardHoverHandler>();
                hover.ApplyPresentation(profile);
            }
            return view;
        }

        private static Image CreateImage(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(
            Transform parent,
            string name,
            TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(
            RectTransform rect,
            Vector2? offsetMin = null,
            Vector2? offsetMax = null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin ?? Vector2.zero;
            rect.offsetMax = offsetMax ?? Vector2.zero;
        }
    }
}
