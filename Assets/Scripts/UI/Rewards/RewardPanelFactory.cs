using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Rewards
{
    internal static class RewardPanelFactory
    {
        internal static readonly Color ScrimColor =
            new Color(0.025f, 0.022f, 0.018f, 0.72f);
        internal static readonly Color PaperColor =
            new Color(0.12f, 0.13f, 0.14f, 0.985f);
        internal static readonly Color PrimaryText =
            new Color(0.93f, 0.88f, 0.76f, 1f);
        internal static readonly Color SecondaryText =
            new Color(0.73f, 0.72f, 0.68f, 1f);
        internal static readonly Color JadeAccent =
            SpiritEnergyColorPalette.Energy;
        internal static readonly Color JadeHighlight =
            SpiritEnergyColorPalette.ParticleHighlight;
        internal static readonly Color WarmGold =
            new Color(0.86f, 0.70f, 0.39f, 1f);
        internal static readonly Color PrimaryAction =
            new Color(0.22f, 0.67f, 0.61f, 1f);
        internal static readonly Color WarmAction =
            new Color(0.62f, 0.50f, 0.24f, 1f);
        internal static readonly Color NeutralAction =
            new Color(0.36f, 0.35f, 0.31f, 1f);

        internal static readonly Vector2 CandidateAreaMin =
            new Vector2(0.035f, 0.06f);
        internal static readonly Vector2 CandidateAreaMax =
            new Vector2(0.775f, 0.855f);
        internal static readonly Vector2 ResultRailMin =
            new Vector2(0.792f, 0.06f);
        internal static readonly Vector2 ResultRailMax =
            new Vector2(0.965f, 0.855f);

        private static Font _titleFont;
        private static Font _bodyFont;
        private static TMP_FontAsset _richTextFont;

        internal static GameObject CreateRoot(
            Canvas canvas,
            string name,
            bool addDismissButton,
            out CanvasGroup canvasGroup,
            out Button dismissButton)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            root.transform.SetParent(canvas.transform, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            Stretch(rect);
            Image scrim = root.GetComponent<Image>();
            scrim.color = ScrimColor;
            scrim.raycastTarget = true;
            canvasGroup = root.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
            dismissButton = null;
            if (addDismissButton)
            {
                dismissButton = root.AddComponent<Button>();
                dismissButton.name = "DismissSurface";
                dismissButton.targetGraphic = scrim;
                dismissButton.transition = Selectable.Transition.None;
                dismissButton.interactable = false;
            }
            root.transform.SetAsLastSibling();
            return root;
        }

        internal static RectTransform CreatePanel(
            Transform parent,
            string name,
            Vector2 size,
            out CanvasGroup canvasGroup)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            Image image = go.GetComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = PaperColor;
            image.raycastTarget = false;
            canvasGroup = go.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            rect.localScale = Vector3.one * 0.92f;
            AddQuietFrameLines(rect);
            AddQuietEdgeSpecks(rect);
            return rect;
        }

        internal static RectTransform CreateMinimalPanel(
            Transform parent,
            string name,
            Vector2 size,
            out CanvasGroup canvasGroup)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * 0.92f;
            canvasGroup = go.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            return rect;
        }

        internal static Text CreateText(
            Transform parent,
            string name,
            string value,
            int size,
            TextAnchor alignment,
            Color color)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = size >= 28 ? TitleFont : BodyFont;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.text = value ?? string.Empty;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        internal static Text CreateHeader(Transform parent, string title)
        {
            Text text = CreateText(
                parent,
                "Title",
                title,
                42,
                TextAnchor.MiddleCenter,
                PrimaryText);
            Place(
                text.rectTransform,
                new Vector2(0.05f, 0.895f),
                new Vector2(0.95f, 0.985f));

            RectTransform leftFlourish = CreateAnchoredRect(
                parent,
                "HeaderFlourishLeft",
                new Vector2(0.36f, 0.872f),
                new Vector2(0.485f, 0.875f));
            Image leftLine = leftFlourish.gameObject.AddComponent<Image>();
            leftLine.color = new Color(
                WarmGold.r, WarmGold.g, WarmGold.b, 0.28f);
            leftLine.raycastTarget = false;

            RectTransform knot = CreateAnchoredRect(
                parent,
                "HeaderKnot",
                new Vector2(0.497f, 0.866f),
                new Vector2(0.503f, 0.881f));
            knot.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image knotImage = knot.gameObject.AddComponent<Image>();
            knotImage.color = new Color(
                JadeHighlight.r,
                JadeHighlight.g,
                JadeHighlight.b,
                0.62f);
            knotImage.raycastTarget = false;

            RectTransform rightFlourish = CreateAnchoredRect(
                parent,
                "HeaderFlourishRight",
                new Vector2(0.515f, 0.872f),
                new Vector2(0.64f, 0.875f));
            Image rightLine = rightFlourish.gameObject.AddComponent<Image>();
            rightLine.color = leftLine.color;
            rightLine.raycastTarget = false;
            return text;
        }

        internal static RectTransform CreateResultRail(
            Transform parent,
            string name,
            string sectionLabel)
        {
            RectTransform rail = CreateAnchoredRect(
                parent,
                name,
                ResultRailMin,
                ResultRailMax);
            Image background = rail.gameObject.AddComponent<Image>();
            background.color = new Color(0.095f, 0.11f, 0.11f, 0.76f);
            background.raycastTarget = false;

            RectTransform divider = CreateAnchoredRect(
                rail,
                "RailDivider",
                new Vector2(0f, 0.025f),
                new Vector2(0.006f, 0.975f));
            Image dividerImage = divider.gameObject.AddComponent<Image>();
            dividerImage.color = new Color(
                JadeAccent.r, JadeAccent.g, JadeAccent.b, 0.32f);
            dividerImage.raycastTarget = false;

            RectTransform topAccent = CreateAnchoredRect(
                rail,
                "RailTopAccent",
                new Vector2(0.08f, 0.984f),
                new Vector2(0.34f, 0.988f));
            Image topAccentImage =
                topAccent.gameObject.AddComponent<Image>();
            topAccentImage.color = new Color(
                WarmGold.r, WarmGold.g, WarmGold.b, 0.38f);
            topAccentImage.raycastTarget = false;

            if (!string.IsNullOrWhiteSpace(sectionLabel))
            {
                Text label = CreateText(
                    rail,
                    "SectionLabel",
                    sectionLabel,
                    17,
                    TextAnchor.MiddleLeft,
                    new Color(
                        SecondaryText.r,
                        SecondaryText.g,
                        SecondaryText.b,
                        0.82f));
                Place(
                    label.rectTransform,
                    new Vector2(0.09f, 0.91f),
                    new Vector2(0.91f, 0.972f));
            }
            return rail;
        }

        internal static void CreateRailActionDivider(
            Transform parent,
            float normalizedY)
        {
            RectTransform divider = CreateAnchoredRect(
                parent,
                "ActionDivider",
                new Vector2(0.09f, normalizedY),
                new Vector2(0.91f, normalizedY + 0.002f));
            Image image = divider.gameObject.AddComponent<Image>();
            image.color = new Color(
                JadeAccent.r, JadeAccent.g, JadeAccent.b, 0.18f);
            image.raycastTarget = false;
        }

        internal static TMP_Text CreateRichText(
            Transform parent,
            string name,
            string value,
            int size,
            TextAlignmentOptions alignment,
            Color color)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.font = RichTextFont;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.text = value ?? string.Empty;
            text.raycastTarget = false;
            text.richText = true;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

        internal static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Color accent)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(150f, 56f);
            Image background = go.GetComponent<Image>();
            background.color = new Color(
                accent.r * 0.42f,
                accent.g * 0.42f,
                accent.b * 0.42f,
                0.94f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            Text text = CreateText(
                go.transform,
                "Label",
                label,
                22,
                TextAnchor.MiddleCenter,
                PrimaryText);
            Stretch(text.rectTransform, 8f);
            return button;
        }

        internal static Color ResolveUtilityAccent(
            RewardUtilityActionKind kind)
        {
            return kind switch
            {
                RewardUtilityActionKind.Refresh =>
                    new Color(0.30f, 0.48f, 0.58f, 1f),
                RewardUtilityActionKind.Exclude =>
                    new Color(0.52f, 0.36f, 0.30f, 1f),
                RewardUtilityActionKind.CashOut =>
                    new Color(0.58f, 0.52f, 0.27f, 1f),
                _ => new Color(0.42f, 0.38f, 0.28f, 1f)
            };
        }

        internal static IEnumerator AnimateIn(
            CanvasGroup root,
            RectTransform panel,
            float duration = 0.18f)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                root.alpha = Mathf.Clamp01(t / 0.34f);
                float scale = t < 0.68f
                    ? Mathf.Lerp(0.92f, 1.025f, t / 0.68f)
                    : Mathf.Lerp(1.025f, 1f, (t - 0.68f) / 0.32f);
                panel.localScale = Vector3.one * scale;
                yield return null;
            }
            root.alpha = 1f;
            panel.localScale = Vector3.one;
        }

        internal static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static RectTransform CreateAnchoredRect(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            Place(rect, anchorMin, anchorMax);
            return rect;
        }

        private static void Place(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Font TitleFont => _titleFont != null
            ? _titleFont
            : (_titleFont = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy")
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        private static Font BodyFont => _bodyFont != null
            ? _bodyFont
            : (_bodyFont = Resources.Load<Font>("Fonts/NotoSerifSC-Medium")
                ?? TitleFont);

        private static TMP_FontAsset RichTextFont
        {
            get
            {
                if (_richTextFont != null) return _richTextFont;
                Font source = BodyFont;
                _richTextFont = TMP_FontAsset.CreateFontAsset(source);
                if (_richTextFont != null)
                {
                    _richTextFont.name = "RewardTooltipRuntimeFont";
                    _richTextFont.hideFlags = HideFlags.HideAndDontSave;
                }
                return _richTextFont;
            }
        }

        private static void AddQuietEdgeSpecks(RectTransform panel)
        {
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject(
                    "AmbientInkSpeck",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                go.transform.SetParent(panel, false);
                RectTransform rect = go.GetComponent<RectTransform>();
                float side = i < 4 ? -1f : 1f;
                rect.anchorMin = rect.anchorMax = new Vector2(
                    side < 0f ? 0.03f : 0.97f,
                    0.18f + (i % 4) * 0.21f);
                rect.sizeDelta = Vector2.one * (3f + (i % 3));
                Image image = go.GetComponent<Image>();
                image.color = new Color(0.78f, 0.62f, 0.35f, 0.11f);
                image.raycastTarget = false;
            }
        }

        private static void AddQuietFrameLines(RectTransform panel)
        {
            Color lineColor = new Color(0.68f, 0.54f, 0.30f, 0.22f);
            CreateFrameLine(
                panel, "FrameLineTop",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(18f, -16f), new Vector2(-18f, -14f),
                lineColor);
            CreateFrameLine(
                panel, "FrameLineBottom",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(18f, 14f), new Vector2(-18f, 16f),
                lineColor);
            CreateFrameLine(
                panel, "FrameLineLeft",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(14f, 18f), new Vector2(16f, -18f),
                lineColor);
            CreateFrameLine(
                panel, "FrameLineRight",
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-16f, 18f), new Vector2(-14f, -18f),
                lineColor);
        }

        private static void CreateFrameLine(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            Color color)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }
    }
}
