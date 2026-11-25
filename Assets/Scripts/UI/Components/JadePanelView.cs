using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Components
{
    /// <summary>
    /// 首页卷宗面板的视觉外壳。统一负责标题区、纯色玉墨背景与开场过渡，
    /// 具体内容由存档和设置面板分别拥有。
    /// </summary>
    public sealed class JadePanelView : MonoBehaviour
    {
        private const float RevealDuration = 0.24f;

        public static readonly Color Ink = new Color(0.008f, 0.026f, 0.034f, 0.99f);
        public static readonly Color Surface = new Color(0.016f, 0.052f, 0.063f, 0.99f);
        public static readonly Color SurfaceRaised = new Color(0.032f, 0.088f, 0.096f, 0.99f);
        public static readonly Color Jade = new Color(0.47f, 0.88f, 0.75f, 1f);
        public static readonly Color JadeMuted = new Color(0.58f, 0.76f, 0.70f, 1f);
        public static readonly Color Gold = new Color(0.86f, 0.72f, 0.43f, 1f);
        public static readonly Color TextPrimary = new Color(0.94f, 0.985f, 0.965f, 1f);
        public static readonly Color TextSecondary = new Color(0.72f, 0.82f, 0.79f, 1f);
        public static readonly Color Danger = new Color(0.94f, 0.43f, 0.36f, 1f);

        private RectTransform _root;
        private RectTransform _contentRoot;
        private CanvasGroup _canvasGroup;
        private Vector2 _restingPosition;
        private Coroutine _revealRoutine;

        public RectTransform ContentRoot => _contentRoot;

        public void Initialize(string title, Vector2 size)
        {
            _root = GetComponent<RectTransform>();
            if (_root == null)
                throw new InvalidOperationException("JadePanelView 必须挂在 RectTransform 上。");

            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = size;
            _root.anchoredPosition = Vector2.zero;
            _restingPosition = _root.anchoredPosition;

            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            BuildShadow();
            BuildBackground();
            BuildHeader(title);
            BuildContentRoot();
        }

        public void Show(Selectable initialSelection = null)
        {
            gameObject.SetActive(true);
            if (_revealRoutine != null)
                StopCoroutine(_revealRoutine);
            _revealRoutine = StartCoroutine(Reveal(initialSelection));
        }

        public void Hide()
        {
            if (_revealRoutine != null)
            {
                StopCoroutine(_revealRoutine);
                _revealRoutine = null;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
            gameObject.SetActive(false);
        }

        private void BuildShadow()
        {
            var shadow = new GameObject("PanelShadow", typeof(Image));
            shadow.transform.SetParent(transform, false);
            RectTransform rect = shadow.GetComponent<RectTransform>();
            Stretch(rect, new Vector2(-18f, -22f), new Vector2(18f, 14f));
            Image image = shadow.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.42f);
            image.raycastTarget = false;
        }

        private void BuildBackground()
        {
            var background = new GameObject("JadePanelBackground", typeof(Image), typeof(Outline));
            background.transform.SetParent(transform, false);
            RectTransform rect = background.GetComponent<RectTransform>();
            Stretch(rect);

            Image image = background.GetComponent<Image>();
            image.color = Ink;
            image.raycastTarget = true;

            Outline outline = background.GetComponent<Outline>();
            outline.effectColor = new Color(Jade.r, Jade.g, Jade.b, 0.62f);
            outline.effectDistance = new Vector2(1f, -1f);

            var surface = new GameObject("InnerSurface", typeof(Image), typeof(Outline));
            surface.transform.SetParent(background.transform, false);
            RectTransform surfaceRect = surface.GetComponent<RectTransform>();
            Stretch(surfaceRect, new Vector2(12f, 12f), new Vector2(-12f, -12f));
            Image surfaceImage = surface.GetComponent<Image>();
            surfaceImage.color = Surface;
            surfaceImage.raycastTarget = false;
            Outline surfaceOutline = surface.GetComponent<Outline>();
            surfaceOutline.effectColor = new Color(Jade.r, Jade.g, Jade.b, 0.18f);
            surfaceOutline.effectDistance = new Vector2(1f, -1f);

            var headerWash = new GameObject("HeaderWash", typeof(Image));
            headerWash.transform.SetParent(background.transform, false);
            RectTransform headerRect = headerWash.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(13f, -102f);
            headerRect.offsetMax = new Vector2(-13f, -13f);
            Image headerImage = headerWash.GetComponent<Image>();
            headerImage.color = new Color(
                SurfaceRaised.r, SurfaceRaised.g, SurfaceRaised.b, 0.42f);
            headerImage.raycastTarget = false;
        }

        private void BuildHeader(string title)
        {
            Text titleText = CreateText(
                "Title", transform, title, 42, TextPrimary, TextAnchor.MiddleCenter, true);
            RectTransform titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(720f, 58f);
            titleRect.anchoredPosition = new Vector2(0f, -26f);
            titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;

            CreateHeaderDivider();
        }

        private void BuildContentRoot()
        {
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(transform, false);
            _contentRoot = content.GetComponent<RectTransform>();
            _contentRoot.anchorMin = Vector2.zero;
            _contentRoot.anchorMax = Vector2.one;
            _contentRoot.offsetMin = new Vector2(48f, 56f);
            _contentRoot.offsetMax = new Vector2(-48f, -128f);
        }

        private void CreateHeaderDivider()
        {
            var divider = new GameObject("HeaderDivider", typeof(RectTransform));
            divider.transform.SetParent(transform, false);
            RectTransform rect = divider.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(52f, -102f);
            rect.offsetMax = new Vector2(-52f, -100f);

            Image line = divider.gameObject.AddComponent<Image>();
            line.color = new Color(Jade.r, Jade.g, Jade.b, 0.48f);
            line.raycastTarget = false;

            var seal = new GameObject("DividerSeal", typeof(Image));
            seal.transform.SetParent(divider.transform, false);
            RectTransform sealRect = seal.GetComponent<RectTransform>();
            sealRect.anchorMin = new Vector2(0f, 0.5f);
            sealRect.anchorMax = new Vector2(0f, 0.5f);
            sealRect.pivot = new Vector2(0.5f, 0.5f);
            sealRect.sizeDelta = new Vector2(10f, 10f);
            sealRect.anchoredPosition = new Vector2(0f, 0f);
            sealRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image sealImage = seal.GetComponent<Image>();
            sealImage.color = Gold;
            sealImage.raycastTarget = false;
        }

        private IEnumerator Reveal(Selectable initialSelection)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            _root.localScale = new Vector3(0.965f, 0.965f, 1f);
            _root.anchoredPosition = _restingPosition + new Vector2(0f, -18f);

            float elapsed = 0f;
            while (elapsed < RevealDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / RevealDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                _canvasGroup.alpha = eased;
                float scale = Mathf.Lerp(0.965f, 1f, eased);
                _root.localScale = new Vector3(scale, scale, 1f);
                _root.anchoredPosition = Vector2.Lerp(
                    _restingPosition + new Vector2(0f, -18f), _restingPosition, eased);
                yield return null;
            }

            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
            _root.localScale = Vector3.one;
            _root.anchoredPosition = _restingPosition;
            _revealRoutine = null;
            initialSelection?.Select();
        }

        public static Text CreateText(
            string name,
            Transform parent,
            string value,
            int fontSize,
            Color color,
            TextAnchor alignment,
            bool heavy = false)
        {
            var textObject = new GameObject(name, typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.supportRichText = true;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            ApplyFont(text, heavy);
            return text;
        }

        public static void ApplyFont(Text text, bool heavy)
        {
            if (text == null)
                return;
            Font font = Resources.Load<Font>(heavy
                ? "Fonts/NotoSerifSC-Heavy"
                : "Fonts/NotoSerifSC-Medium");
            if (font == null)
                font = Resources.Load<Font>("Fonts/NotoSerifSC-Heavy");
            if (font != null)
                text.font = font;
            // 字体文件本身已经包含真实字重；再次合成 Bold 会让小字号边缘发糊。
            text.fontStyle = FontStyle.Normal;
        }

        public static Button CreateTextButton(
            string name,
            Transform parent,
            string label,
            Vector2 size,
            bool primary,
            bool danger = false)
        {
            var buttonObject = new GameObject(name, typeof(Image), typeof(Button), typeof(Outline));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;

            Image image = buttonObject.GetComponent<Image>();
            image.color = danger
                ? new Color(0.22f, 0.065f, 0.06f, 0.96f)
                : primary
                    ? new Color(0.09f, 0.25f, 0.22f, 0.98f)
                    : new Color(0.025f, 0.075f, 0.086f, 0.96f);

            Outline outline = buttonObject.GetComponent<Outline>();
            outline.effectColor = danger
                ? Danger
                : primary ? Gold : new Color(Jade.r, Jade.g, Jade.b, 0.62f);
            outline.effectDistance = new Vector2(1f, -1f);

            Text text = CreateText(
                "Label", buttonObject.transform, label, 20,
                danger ? new Color(1f, 0.77f, 0.7f, 1f) : TextPrimary,
                TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.14f, 1f);
            colors.pressedColor = new Color(0.78f, 0.82f, 0.8f, 1f);
            colors.selectedColor = new Color(1.12f, 1.12f, 1.08f, 1f);
            colors.disabledColor = new Color(0.42f, 0.46f, 0.45f, 0.62f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var hover = buttonObject.AddComponent<HoverScaleEffect>();
            hover.HoverScale = 1.025f;
            return button;
        }

        public static void Stretch(
            RectTransform rect,
            Vector2 offsetMin = default,
            Vector2 offsetMax = default)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

    }
}
