#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Content;
using WanjieScroll.Core;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 编辑器专用的交互节点表现审核宿主。页面只提供单一类型选择器和全局
    /// 播放控制；具体获得、后续面板与成果顺序由 RewardInteractionReviewController
    /// 统一编排，不创建 Run、地图事务或存档写入。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RewardUiReviewBootstrap : MonoBehaviour
    {
        private readonly List<CardConfig> _reviewCards =
            new List<CardConfig>();

        private Canvas _overlayCanvas;
        private RewardInteractionReviewController _interactionReview;
        private Dropdown _scenarioDropdown;
        private Text _rarityLabel;
        private Text _statusLabel;

        private void Start()
        {
            EnsureCamera();
            EnsureEventSystem();
            _overlayCanvas = CreateCanvas("RewardUiReviewOverlay", 32100);
            BuildToolbar();

            _interactionReview = gameObject.AddComponent<
                RewardInteractionReviewController>();
            _interactionReview.Initialize(
                _overlayCanvas,
                GetReviewCards,
                RefreshToolbar);
            _scenarioDropdown.ClearOptions();
            _scenarioDropdown.AddOptions(
                RewardInteractionReviewController.GetScenarioLabels());
            _scenarioDropdown.SetValueWithoutNotify(0);
            _scenarioDropdown.onValueChanged.AddListener(
                _interactionReview.SelectScenario);
            _interactionReview.SelectScenario(0);
            RefreshToolbar();
        }

        private void EnsureCamera()
        {
            if (Camera.main != null)
                return;
            var cameraObject = new GameObject(
                "RewardUiReviewCamera",
                typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.045f, 0.035f);
        }

        private IReadOnlyList<CardConfig> GetReviewCards()
        {
            EnsureReviewCards();
            return _reviewCards;
        }

        private void EnsureReviewCards()
        {
            if (_reviewCards.Count > 0)
                return;
            foreach (VolumeContent volume in ContentCatalog.Current.Volumes)
            {
                if (volume?.cards == null)
                    continue;
                foreach (CardData data in volume.cards)
                {
                    if (data == null ||
                        string.IsNullOrWhiteSpace(data.contentId))
                    {
                        continue;
                    }
                    CardConfig definition =
                        ContentRuntimeFactory.CreateCard(data);
                    CardConfig card = CardInstanceFactory.CreateNew(
                        definition,
                        _reviewCards.Count + 1);
                    Destroy(definition);
                    _reviewCards.Add(card);
                    if (_reviewCards.Count >= 8)
                        break;
                }
                if (_reviewCards.Count >= 8)
                    break;
            }
        }

        private void BuildToolbar()
        {
            Canvas canvas = CreateCanvas("RewardUiReviewToolbar", 32500);
            var barObject = new GameObject(
                "InteractionReviewToolbar",
                typeof(RectTransform),
                typeof(Image),
                typeof(HorizontalLayoutGroup));
            barObject.transform.SetParent(canvas.transform, false);
            RectTransform bar = barObject.GetComponent<RectTransform>();
            bar.anchorMin = new Vector2(0.5f, 1f);
            bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = new Vector2(0f, -12f);
            bar.sizeDelta = new Vector2(1500f, 62f);
            barObject.GetComponent<Image>().color =
                new Color(0.04f, 0.05f, 0.045f, 0.96f);

            HorizontalLayoutGroup layout =
                barObject.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 9, 9);
            layout.spacing = 9f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            CreateToolbarText(bar, "交互节点", 92f, 18,
                TextAnchor.MiddleCenter, Color.white);
            _scenarioDropdown = CreateScenarioDropdown(bar, 260f);
            CreateToolbarButton(
                bar, "◀ 品阶", 96f,
                () => _interactionReview?.CycleRarityOverride(-1));
            _rarityLabel = CreateToolbarText(
                bar, "按配置", 96f, 18,
                TextAnchor.MiddleCenter,
                new Color(0.91f, 0.78f, 0.48f));
            CreateToolbarButton(
                bar, "品阶 ▶", 96f,
                () => _interactionReview?.CycleRarityOverride(1));
            CreateToolbarButton(
                bar, "重播流程", 112f,
                () => _interactionReview?.Replay());
            CreateToolbarButton(
                bar, "五档连播", 112f,
                () => _interactionReview?.PlayRaritySequence());
            _statusLabel = CreateToolbarText(
                bar,
                "等待播放",
                520f,
                17,
                TextAnchor.MiddleLeft,
                new Color(0.78f, 0.86f, 0.82f));
        }

        private void RefreshToolbar()
        {
            if (_interactionReview == null)
                return;
            if (_rarityLabel != null)
                _rarityLabel.text = _interactionReview.RarityOverrideLabel;
            if (_statusLabel != null)
            {
                _statusLabel.text =
                    $"{_interactionReview.SelectedRouteLabel}    " +
                    $"当前：{_interactionReview.StageLabel}";
            }
        }

        private static Dropdown CreateScenarioDropdown(
            Transform parent,
            float width)
        {
            var rootObject = new GameObject(
                "InteractionTypeDropdown",
                typeof(RectTransform),
                typeof(Image),
                typeof(Dropdown),
                typeof(LayoutElement));
            rootObject.transform.SetParent(parent, false);
            LayoutElement rootLayout = rootObject.GetComponent<LayoutElement>();
            rootLayout.preferredWidth = width;
            rootLayout.minWidth = width;
            Image rootImage = rootObject.GetComponent<Image>();
            rootImage.color = new Color(0.16f, 0.31f, 0.28f, 1f);

            Text caption = CreateStretchText(
                rootObject.transform,
                "Caption",
                "选择交互节点",
                18,
                TextAnchor.MiddleLeft,
                Color.white,
                14f,
                36f);
            Text arrow = CreateStretchText(
                rootObject.transform,
                "Arrow",
                "▼",
                15,
                TextAnchor.MiddleCenter,
                Color.white,
                width - 36f,
                8f);

            var templateObject = new GameObject(
                "Template",
                typeof(RectTransform),
                typeof(Image),
                typeof(ScrollRect));
            templateObject.transform.SetParent(rootObject.transform, false);
            RectTransform template = templateObject.GetComponent<RectTransform>();
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, -4f);
            template.sizeDelta = new Vector2(0f, 246f);
            templateObject.GetComponent<Image>().color =
                new Color(0.035f, 0.055f, 0.048f, 0.99f);

            var viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(Image),
                typeof(RectMask2D));
            viewportObject.transform.SetParent(template, false);
            RectTransform viewport =
                viewportObject.GetComponent<RectTransform>();
            Stretch(viewport, 4f);
            viewportObject.GetComponent<Image>().color =
                new Color(0f, 0f, 0f, 0.01f);

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewport, false);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup contentLayout =
                contentObject.GetComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(3, 3, 3, 3);
            contentLayout.spacing = 2f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            ContentSizeFitter fitter =
                contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var itemObject = new GameObject(
                "Item",
                typeof(RectTransform),
                typeof(Image),
                typeof(Toggle),
                typeof(LayoutElement));
            itemObject.transform.SetParent(content, false);
            itemObject.GetComponent<LayoutElement>().preferredHeight = 37f;
            Image itemBackground = itemObject.GetComponent<Image>();
            itemBackground.color = new Color(0.12f, 0.24f, 0.21f, 1f);
            Toggle toggle = itemObject.GetComponent<Toggle>();
            toggle.targetGraphic = itemBackground;

            Text checkmark = CreateStretchText(
                itemObject.transform,
                "Item Checkmark",
                "◆",
                12,
                TextAnchor.MiddleCenter,
                new Color(0.64f, 0.90f, 0.75f),
                5f,
                width - 29f);
            toggle.graphic = checkmark;
            Text itemLabel = CreateStretchText(
                itemObject.transform,
                "Item Label",
                "选项",
                17,
                TextAnchor.MiddleLeft,
                Color.white,
                31f,
                8f);

            ScrollRect scroll = templateObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            Dropdown dropdown = rootObject.GetComponent<Dropdown>();
            dropdown.targetGraphic = rootImage;
            dropdown.captionText = caption;
            dropdown.template = template;
            dropdown.itemText = itemLabel;
            templateObject.SetActive(false);
            return dropdown;
        }

        private static Button CreateToolbarButton(
            Transform parent,
            string label,
            float width,
            UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = new GameObject(
                label,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<LayoutElement>().preferredWidth = width;
            buttonObject.GetComponent<Image>().color =
                new Color(0.20f, 0.43f, 0.37f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            CreateStretchText(
                buttonObject.transform,
                "Label",
                label,
                17,
                TextAnchor.MiddleCenter,
                Color.white,
                4f,
                4f);
            return button;
        }

        private static Text CreateToolbarText(
            Transform parent,
            string value,
            float width,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            var textObject = new GameObject(
                value,
                typeof(RectTransform),
                typeof(Text),
                typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);
            textObject.GetComponent<LayoutElement>().preferredWidth = width;
            Text text = textObject.GetComponent<Text>();
            ConfigureText(text, value, fontSize, alignment, color);
            return text;
        }

        private static Text CreateStretchText(
            Transform parent,
            string name,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color,
            float left,
            float right)
        {
            var textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
            Text text = textObject.GetComponent<Text>();
            ConfigureText(text, value, fontSize, alignment, color);
            return text;
        }

        private static void ConfigureText(
            Text text,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
        }

        private Canvas CreateCanvas(string name, int sortingOrder)
        {
            var canvasObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return canvas;
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
                return;
            var eventSystem = new GameObject(
                "RewardUiReviewEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(transform, false);
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private void OnDestroy()
        {
            _scenarioDropdown?.onValueChanged.RemoveAllListeners();
            _interactionReview?.Close();
            for (int i = 0; i < _reviewCards.Count; i++)
            {
                if (_reviewCards[i] != null)
                    Destroy(_reviewCards[i]);
            }
            _reviewCards.Clear();
        }
    }
}
#endif
