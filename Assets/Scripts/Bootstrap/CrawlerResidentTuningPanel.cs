using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Components;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// F7 中的英灵显化专用调参面板。它唯一拥有共享布局的会话
    /// 快照、滑杆绑定、折叠状态与恢复/保存交互；每名英灵专属的灵气定位
    /// 会话由 CrawlerResidentPresentationTuningSession 隐藏。战斗规则和英灵
    /// 实例仍由 Core 与驻场 Presenter 所有。
    ///
    /// 模块审查：本类超过 800 行是因为 F7 没有静态 Prefab，十二项控件的
    /// 运行时 UGUI 层级、绑定和恢复/保存交互共同维护“当前 Play 会话
    /// 中的驻场参数所见即所得”。逐英灵资产切换与基线状态已提取到
    /// CrawlerResidentPresentationTuningSession，本类不持有碎片实现、英灵
    /// 规则或卷灵配置。颜色由全局 SpiritEnergyColorPalette 固定，不在
    /// 面板暴露。若以后增加预设或三槽位置编辑，必须再提取独立模块。
    /// </summary>
    internal sealed class CrawlerResidentTuningPanel : MonoBehaviour
    {
        public const string Revision = "F7-CRAWLER-TUNER-2026.08.18.7";
        private const string DefaultStatus =
            "点“预览灵气”后可边看边调粒子与当前英灵的 X/Y；" +
            "入退场回放不重复增益，" +
            "也不修改凝神。";

        private const float PanelWidth = 410f;
        private const float ExpandedHeight = 706f;
        private const float CollapsedHeight = 52f;
        private const float RowStartY = 154f;
        private const float RowHeight = 38f;

        private readonly List<SliderBinding> _bindings =
            new List<SliderBinding>();
        private readonly List<PresentationSliderBinding>
            _presentationBindings =
                new List<PresentationSliderBinding>();
        private readonly CrawlerResidentPresentationTuningSession
            _presentationTuning =
                new CrawlerResidentPresentationTuningSession();

        private CrawlerResidentLayoutProfile _layout;
        private LayoutSnapshot _baseline;
        private Action<string> _onReplayEntryRequested;
        private Func<string, bool> _onReplayExitRequested;
        private Action _onLayoutChanged;
        private Func<string, bool, bool> _onAuraPreviewChanged;
        private RectTransform _panelRect;
        private GameObject _bodyRoot;
        private Text _selectedLabel;
        private Text _statusLabel;
        private Text _collapseLabel;
        private Button _replayButton;
        private Button _replayExitButton;
        private Button _resetButton;
        private Button _saveButton;
        private Button _auraPreviewButton;
        private Text _auraPreviewButtonLabel;
        private string _selectedCardId;
        private string _crawlerContentId;
        private bool _auraPreviewEnabled;
        private bool _refreshing;
        private bool _expanded = true;

        public static CrawlerResidentTuningPanel Create(
            Transform parent,
            CrawlerResidentLayoutProfile layout,
            Action<string> onReplayEntryRequested,
            Func<string, bool> onReplayExitRequested,
            Action onLayoutChanged,
            Func<string, bool, bool> onAuraPreviewChanged)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            var panelObject = new GameObject(
                "CrawlerResidentTuningPanel",
                typeof(RectTransform),
                typeof(Image),
                typeof(CrawlerResidentTuningPanel));
            panelObject.transform.SetParent(parent, false);
            var panel = panelObject.GetComponent<
                CrawlerResidentTuningPanel>();
            panel._layout = layout;
            panel._baseline = LayoutSnapshot.Capture(layout);
            panel._onReplayEntryRequested = onReplayEntryRequested;
            panel._onReplayExitRequested = onReplayExitRequested;
            panel._onLayoutChanged = onLayoutChanged;
            panel._onAuraPreviewChanged = onAuraPreviewChanged;
            panel.BuildUi();
            panel.gameObject.SetActive(false);
            return panel;
        }

        public void SetSelectedCard(CardConfig card)
        {
            string selectedCrawlerContentId =
                FindSummonedCrawlerContentId(card);
            string cardId = card?.contentId ?? string.Empty;
            string effectiveCrawlerContentId =
                !string.IsNullOrWhiteSpace(selectedCrawlerContentId)
                    ? selectedCrawlerContentId
                    : _crawlerContentId;
            if (string.Equals(
                    _selectedCardId,
                    cardId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    _crawlerContentId,
                    effectiveCrawlerContentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedCardId = cardId;
            if (!string.IsNullOrWhiteSpace(selectedCrawlerContentId) &&
                !string.Equals(
                    _crawlerContentId,
                    selectedCrawlerContentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                SetAuraPreview(false, showStatus: false);
                _crawlerContentId = selectedCrawlerContentId;
                _presentationTuning.Select(_crawlerContentId);
            }
            bool visible = !string.IsNullOrWhiteSpace(_crawlerContentId);
            gameObject.SetActive(visible);
            if (!visible)
                return;

            _selectedLabel.text =
                !string.IsNullOrWhiteSpace(selectedCrawlerContentId)
                    ? $"{card.cardName}  →  {_crawlerContentId}"
                    : $"{card?.cardName ?? "未选卡"} · 当前英灵  →  " +
                        _crawlerContentId;
            SetStatus(DefaultStatus, new Color(0.72f, 0.82f, 0.9f));
            RefreshBindings();
            RefreshButtons();
            transform.SetAsLastSibling();
        }

        private void BuildUi()
        {
            _panelRect = GetComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(0.5f, 1f);
            _panelRect.anchorMax = _panelRect.anchorMin;
            _panelRect.pivot = new Vector2(0.5f, 1f);
            _panelRect.sizeDelta = new Vector2(
                PanelWidth,
                ExpandedHeight);
            _panelRect.anchoredPosition = new Vector2(-455f, -22f);

            Image image = GetComponent<Image>();
            image.color = new Color(0.045f, 0.035f, 0.028f, 0.94f);
            image.raycastTarget = false;

            RectTransform body = CreateRect(
                "Body",
                transform,
                0f,
                0f,
                PanelWidth,
                ExpandedHeight);
            _bodyRoot = body.gameObject;

            Text title = UIElementFactory.CreateText(
                "Title",
                transform,
                "英灵驻场实时调参",
                21,
                new Color(1f, 0.82f, 0.4f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(title.rectTransform, 14f, 7f, 330f, 28f);

            Button collapse = CreateTextButton(
                "Collapse",
                transform,
                "－",
                356f,
                9f,
                40f,
                34f,
                ToggleExpanded);
            _collapseLabel = collapse.GetComponentInChildren<Text>();

            _selectedLabel = UIElementFactory.CreateText(
                "SelectedCrawler",
                body,
                string.Empty,
                14,
                new Color(0.94f, 0.91f, 0.82f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(
                _selectedLabel.rectTransform,
                14f,
                36f,
                382f,
                26f);

            _replayButton = CreateTextButton(
                "ReplayEntry",
                body,
                "重播入场",
                14f,
                70f,
                88f,
                34f,
                ReplayEntry);
            _replayExitButton = CreateTextButton(
                "ReplayExit",
                body,
                "预览退场",
                107f,
                70f,
                88f,
                34f,
                ReplayExit);
            _resetButton = CreateTextButton(
                "ResetLayout",
                body,
                "恢复基线",
                200f,
                70f,
                88f,
                34f,
                ResetLayout);
            _saveButton = CreateTextButton(
                "SaveLayout",
                body,
                "保存参数",
                293f,
                70f,
                103f,
                34f,
                SaveLayout);

            _auraPreviewButton = CreateTextButton(
                "PreviewAura",
                body,
                "预览灵气",
                14f,
                108f,
                103f,
                34f,
                ToggleAuraPreview);
            _auraPreviewButtonLabel =
                _auraPreviewButton.GetComponentInChildren<Text>();
            Text auraHint = UIElementFactory.CreateText(
                "AuraHint",
                body,
                "开启后拖动灵气参数可实时观察",
                13,
                new Color(0.78f, 0.78f, 0.72f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(
                auraHint.rectTransform,
                126f,
                108f,
                270f,
                34f);

            AddSlider(
                "入场时长",
                0.65f,
                0.95f,
                false,
                profile => profile.entryDuration,
                (profile, value) => profile.entryDuration = value,
                "0.00s");
            AddSlider(
                "退场时长",
                0.45f,
                0.85f,
                false,
                profile => profile.exitDuration,
                (profile, value) => profile.exitDuration = value,
                "0.00s");
            AddSlider(
                "碎片数量",
                CrawlerResidentLayoutProfile.
                    MinMaterializationParticleCount,
                CrawlerResidentLayoutProfile.
                    MaxMaterializationParticleCount,
                true,
                profile => profile.materializationParticleCount,
                (profile, value) =>
                    profile.materializationParticleCount =
                        Mathf.RoundToInt(value),
                "0");
            AddSlider(
                "碎片尺寸",
                CrawlerResidentLayoutProfile.
                    MinMaterializationSizeScale,
                CrawlerResidentLayoutProfile.
                    MaxMaterializationSizeScale,
                false,
                profile => profile.materializationSizeScale,
                (profile, value) =>
                    profile.materializationSizeScale = value,
                "0.00×");
            AddSlider(
                "凝神位置 X",
                -100f,
                40f,
                true,
                profile => profile.durationOffset.x,
                (profile, value) => profile.durationOffset =
                    new Vector2(value, profile.durationOffset.y),
                "0px");
            AddSlider(
                "凝神位置 Y",
                -40f,
                100f,
                true,
                profile => profile.durationOffset.y,
                (profile, value) => profile.durationOffset =
                    new Vector2(profile.durationOffset.x, value),
                "0px");
            AddSlider(
                "灵气粒子",
                CrawlerResidentLayoutProfile.MinAuraParticleCount,
                CrawlerResidentLayoutProfile.MaxAuraParticleCount,
                true,
                profile => profile.hoverAuraParticleCount,
                (profile, value) => profile.hoverAuraParticleCount =
                    Mathf.RoundToInt(value),
                "0");
            AddSlider(
                "环绕范围",
                CrawlerResidentLayoutProfile.MinAuraRadius / 64f,
                CrawlerResidentLayoutProfile.MaxAuraRadius / 64f,
                false,
                profile => profile.hoverAuraRadius / 64f,
                (profile, value) => profile.hoverAuraRadius = value * 64f,
                "0.00倍");
            AddSlider(
                "环绕速度",
                CrawlerResidentLayoutProfile.MinAuraCyclesPerSecond,
                CrawlerResidentLayoutProfile.MaxAuraCyclesPerSecond,
                false,
                profile => profile.hoverAuraCyclesPerSecond,
                (profile, value) => profile.hoverAuraCyclesPerSecond = value,
                "0.00");
            AddSlider(
                "灵气尺寸",
                CrawlerResidentLayoutProfile.MinAuraParticleSize,
                CrawlerResidentLayoutProfile.MaxAuraParticleSize,
                false,
                profile => profile.hoverAuraParticleSize,
                (profile, value) => profile.hoverAuraParticleSize = value,
                "0.0px");
            AddPresentationSlider(
                "灵气位置 X",
                profile => profile.hoverAuraOffset.x,
                value => _presentationTuning.SetAuraOffsetX(value));
            AddPresentationSlider(
                "灵气位置 Y",
                profile => profile.hoverAuraOffset.y,
                value => _presentationTuning.SetAuraOffsetY(value));
            _statusLabel = UIElementFactory.CreateText(
                "Status",
                body,
                Revision,
                14,
                new Color(0.72f, 0.82f, 0.9f),
                TextAnchor.UpperLeft);
            PlaceTopLeft(
                _statusLabel.rectTransform,
                14f,
                620f,
                382f,
                72f);
            _statusLabel.text = DefaultStatus;
        }

        private void AddSlider(
            string label,
            float minimum,
            float maximum,
            bool wholeNumbers,
            Func<CrawlerResidentLayoutProfile, float> read,
            Action<CrawlerResidentLayoutProfile, float> write,
            string format)
        {
            float y = RowStartY + _bindings.Count * RowHeight;
            RectTransform row = CreateRect(
                $"{label}Row",
                _bodyRoot.transform,
                0f,
                y,
                PanelWidth,
                RowHeight);

            Text labelText = UIElementFactory.CreateText(
                "Label",
                row,
                label,
                16,
                new Color(0.91f, 0.88f, 0.79f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(labelText.rectTransform, 14f, 2f, 120f, 30f);

            Slider slider = CreateSlider(
                row,
                136f,
                7f,
                188f,
                24f,
                minimum,
                maximum,
                wholeNumbers);
            Text valueText = UIElementFactory.CreateText(
                "Value",
                row,
                "--",
                15,
                new Color(1f, 0.76f, 0.32f),
                TextAnchor.MiddleRight);
            PlaceTopLeft(valueText.rectTransform, 328f, 2f, 68f, 30f);

            var binding = new SliderBinding(
                slider,
                valueText,
                read,
                write,
                format,
                wholeNumbers);
            _bindings.Add(binding);
            slider.onValueChanged.AddListener(value =>
                OnSliderChanged(binding, value));
        }

        private void AddPresentationSlider(
            string label,
            Func<CrawlerResidentPresentationProfile, float> read,
            Action<float> write)
        {
            float y = RowStartY +
                (_bindings.Count + _presentationBindings.Count) *
                RowHeight;
            RectTransform row = CreateRect(
                $"{label}Row",
                _bodyRoot.transform,
                0f,
                y,
                PanelWidth,
                RowHeight);

            Text labelText = UIElementFactory.CreateText(
                "Label",
                row,
                label,
                16,
                new Color(0.91f, 0.88f, 0.79f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(labelText.rectTransform, 14f, 2f, 120f, 30f);

            Slider slider = CreateSlider(
                row,
                136f,
                7f,
                188f,
                24f,
                CrawlerResidentPresentationProfile.MinHoverAuraOffset,
                CrawlerResidentPresentationProfile.MaxHoverAuraOffset,
                true);
            Text valueText = UIElementFactory.CreateText(
                "Value",
                row,
                "--",
                15,
                new Color(0.48f, 1f, 0.9f),
                TextAnchor.MiddleRight);
            PlaceTopLeft(valueText.rectTransform, 328f, 2f, 68f, 30f);

            var binding = new PresentationSliderBinding(
                slider,
                valueText,
                read,
                write);
            _presentationBindings.Add(binding);
            slider.onValueChanged.AddListener(value =>
                OnPresentationSliderChanged(binding, value));
        }

        private void OnSliderChanged(
            SliderBinding binding,
            float value)
        {
            if (_refreshing || _layout == null)
                return;

            float normalized = binding.WholeNumbers
                ? Mathf.Round(value)
                : value;
            binding.Write(_layout, normalized);
            binding.ValueText.text =
                binding.Read(_layout).ToString(binding.Format);
            _onLayoutChanged?.Invoke();
            SetStatus(
                _auraPreviewEnabled
                    ? "有未保存修改；灵气预览正在使用当前值。"
                    : "有未保存修改；可点“预览灵气”边看边调。",
                new Color(1f, 0.72f, 0.3f));
        }

        private void OnPresentationSliderChanged(
            PresentationSliderBinding binding,
            float value)
        {
            if (_refreshing || !_presentationTuning.HasCurrent)
                return;

            float normalized = Mathf.Round(value);
            binding.Write(normalized);
            binding.ValueText.text = normalized.ToString("0px");
            _onLayoutChanged?.Invoke();
            SetStatus(
                _auraPreviewEnabled
                    ? "当前英灵的灵气位置已修改；预览已实时更新。"
                    : "当前英灵的灵气位置有未保存修改。",
                new Color(1f, 0.72f, 0.3f));
        }

        private void RefreshBindings()
        {
            _refreshing = true;
            for (int i = 0; i < _bindings.Count; i++)
            {
                SliderBinding binding = _bindings[i];
                float value = Mathf.Clamp(
                    binding.Read(_layout),
                    binding.Slider.minValue,
                    binding.Slider.maxValue);
                binding.Slider.value = value;
                binding.ValueText.text = value.ToString(binding.Format);
            }

            CrawlerResidentPresentationProfile presentation =
                _presentationTuning.CurrentProfile;
            for (int i = 0; i < _presentationBindings.Count; i++)
            {
                PresentationSliderBinding binding =
                    _presentationBindings[i];
                binding.Slider.interactable = presentation != null;
                if (presentation == null)
                {
                    binding.ValueText.text = "--";
                    continue;
                }

                float value = Mathf.Clamp(
                    binding.Read(presentation),
                    binding.Slider.minValue,
                    binding.Slider.maxValue);
                binding.Slider.value = value;
                binding.ValueText.text = value.ToString("0px");
            }
            _refreshing = false;
        }

        private void ReplayEntry()
        {
            if (string.IsNullOrWhiteSpace(_crawlerContentId))
                return;
            _onReplayEntryRequested?.Invoke(_crawlerContentId);
            SetStatus(
                "正在按当前参数重播入场。",
                new Color(0.72f, 0.82f, 0.9f));
        }

        private void ReplayExit()
        {
            if (string.IsNullOrWhiteSpace(_crawlerContentId))
                return;

            SetAuraPreview(false, showStatus: false);

            bool started = _onReplayExitRequested?.Invoke(
                _crawlerContentId) ?? false;
            SetStatus(
                started
                    ? "正在预览退场；完成后点“重播入场”重新显形。"
                    : "英灵尚未显形或已经隐藏，请先点“重播入场”。",
                started
                    ? new Color(0.72f, 0.82f, 0.9f)
                    : new Color(1f, 0.58f, 0.32f));
        }

        private void ToggleAuraPreview()
        {
            SetAuraPreview(!_auraPreviewEnabled, showStatus: true);
        }

        private void SetAuraPreview(bool visible, bool showStatus)
        {
            bool applied = _onAuraPreviewChanged?.Invoke(
                _crawlerContentId,
                visible) ?? false;
            _auraPreviewEnabled = visible && applied;
            UpdateAuraPreviewButton();
            if (!showStatus)
                return;

            SetStatus(
                _auraPreviewEnabled
                    ? "灵气预览已开启；粒子、半径、速度、尺寸和 X/Y 均会即时更新。"
                    : visible
                        ? "英灵尚未显形，请先打出英灵卡或点“重播入场”。"
                        : "灵气预览已关闭。",
                _auraPreviewEnabled
                    ? new Color(0.62f, 0.92f, 0.58f)
                    : new Color(0.72f, 0.82f, 0.9f));
        }

        private void UpdateAuraPreviewButton()
        {
            if (_auraPreviewButtonLabel != null)
            {
                _auraPreviewButtonLabel.text = _auraPreviewEnabled
                    ? "关闭灵气"
                    : "预览灵气";
            }
        }

        private void ResetLayout()
        {
            _baseline.Restore(_layout);
            _presentationTuning.RestoreCurrent();
            RefreshBindings();
            _onLayoutChanged?.Invoke();
            SetStatus(
                "已恢复共享布局与当前英灵灵气位置的保存基线。",
                new Color(0.72f, 0.82f, 0.9f));
        }

        private void SaveLayout()
        {
            VfxProfileSaveResult layoutResult =
                VfxTestProfileSavePort.SaveAsset(_layout);
            VfxProfileSaveResult presentationResult =
                _presentationTuning.SaveCurrent();
            if (layoutResult.Success)
                _baseline = LayoutSnapshot.Capture(_layout);

            bool success = layoutResult.Success &&
                presentationResult.Success;
            if (success)
            {
                SetStatus(
                    "已保存共享布局与当前英灵的灵气 X/Y。",
                    new Color(0.62f, 0.92f, 0.58f));
            }
            else
            {
                SetStatus(
                    $"共享布局：{layoutResult.Message}\n" +
                    $"英灵定位：{presentationResult.Message}",
                    new Color(1f, 0.48f, 0.38f));
            }
        }

        private void RefreshButtons()
        {
            bool selected = !string.IsNullOrWhiteSpace(_crawlerContentId);
            _replayButton.interactable = selected;
            _replayExitButton.interactable = selected;
            _resetButton.interactable = selected;
            _auraPreviewButton.interactable = selected;
            _saveButton.interactable = selected &&
                _presentationTuning.HasCurrent &&
                VfxTestProfileSavePort.IsAssetSaveAvailable;
            UpdateAuraPreviewButton();
        }

        private void SetStatus(string message, Color color)
        {
            if (_statusLabel == null)
                return;
            _statusLabel.text = message;
            _statusLabel.color = color;
        }

        private void ToggleExpanded()
        {
            _expanded = !_expanded;
            _bodyRoot.SetActive(_expanded);
            _panelRect.sizeDelta = new Vector2(
                PanelWidth,
                _expanded ? ExpandedHeight : CollapsedHeight);
            _collapseLabel.text = _expanded ? "－" : "＋";
        }

        private void OnDestroy()
        {
            if (_auraPreviewEnabled)
                _onAuraPreviewChanged?.Invoke(_crawlerContentId, false);
            if (_layout != null)
                _baseline.Restore(_layout);
            _presentationTuning.RestoreAll();
        }

        private static string FindSummonedCrawlerContentId(CardConfig card)
        {
            if (card?.effects == null)
                return null;
            for (int i = 0; i < card.effects.Count; i++)
            {
                CardEffectSpec effect = card.effects[i];
                if (effect != null &&
                    effect.effectType == EffectType.SummonCrawler &&
                    !string.IsNullOrWhiteSpace(effect.crawlerContentId))
                {
                    return effect.crawlerContentId;
                }
            }
            return null;
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent,
            float x,
            float y,
            float width,
            float height)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            PlaceTopLeft(rect, x, y, width, height);
            return rect;
        }

        private static Button CreateTextButton(
            string name,
            Transform parent,
            string label,
            float x,
            float y,
            float width,
            float height,
            Action onClick)
        {
            Button button = UIElementFactory.CreateButton(name, parent);
            PlaceTopLeft(
                button.GetComponent<RectTransform>(),
                x,
                y,
                width,
                height);
            button.image.color = new Color(0.24f, 0.16f, 0.09f, 0.98f);
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            Text text = UIElementFactory.CreateText(
                "Label",
                button.transform,
                label,
                15,
                new Color(1f, 0.91f, 0.73f),
                TextAnchor.MiddleCenter);
            UIElementFactory.Stretch(text.rectTransform);
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            return button;
        }

        private static Slider CreateSlider(
            Transform parent,
            float x,
            float y,
            float width,
            float height,
            float minimum,
            float maximum,
            bool wholeNumbers)
        {
            var sliderObject = new GameObject(
                "Slider",
                typeof(RectTransform),
                typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            PlaceTopLeft(
                sliderObject.GetComponent<RectTransform>(),
                x,
                y,
                width,
                height);

            Image background = UIElementFactory.CreateImage(
                "Background",
                sliderObject.transform);
            background.color = new Color(0.16f, 0.14f, 0.12f, 1f);
            background.raycastTarget = false;
            RectTransform backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(0f, 7f);
            backgroundRect.anchoredPosition = Vector2.zero;

            RectTransform fillArea = CreateStretchRect(
                "FillArea",
                sliderObject.transform,
                9f,
                9f,
                -9f,
                -9f);
            Image fill = UIElementFactory.CreateImage("Fill", fillArea);
            fill.color = new Color(0.94f, 0.55f, 0.13f, 1f);
            fill.raycastTarget = false;
            UIElementFactory.Stretch(fill.rectTransform);

            RectTransform handleArea = CreateStretchRect(
                "HandleArea",
                sliderObject.transform,
                9f,
                0f,
                -9f,
                0f);
            Image handle = UIElementFactory.CreateImage(
                "Handle",
                handleArea);
            handle.color = new Color(1f, 0.9f, 0.62f, 1f);
            RectTransform handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0.5f, 0.5f);
            handleRect.anchorMax = handleRect.anchorMin;
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(17f, 17f);
            handleRect.anchoredPosition = Vector2.zero;

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.wholeNumbers = wholeNumbers;
            Navigation navigation = slider.navigation;
            navigation.mode = Navigation.Mode.None;
            slider.navigation = navigation;
            return slider;
        }

        private static RectTransform CreateStretchRect(
            string name,
            Transform parent,
            float left,
            float bottom,
            float right,
            float top)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
            return rect;
        }

        private static void PlaceTopLeft(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private sealed class SliderBinding
        {
            public Slider Slider { get; }
            public Text ValueText { get; }
            public Func<CrawlerResidentLayoutProfile, float> Read { get; }
            public Action<CrawlerResidentLayoutProfile, float> Write { get; }
            public string Format { get; }
            public bool WholeNumbers { get; }

            public SliderBinding(
                Slider slider,
                Text valueText,
                Func<CrawlerResidentLayoutProfile, float> read,
                Action<CrawlerResidentLayoutProfile, float> write,
                string format,
                bool wholeNumbers)
            {
                Slider = slider;
                ValueText = valueText;
                Read = read;
                Write = write;
                Format = format;
                WholeNumbers = wholeNumbers;
            }
        }

        private sealed class PresentationSliderBinding
        {
            public Slider Slider { get; }
            public Text ValueText { get; }
            public Func<CrawlerResidentPresentationProfile, float> Read
            {
                get;
            }
            public Action<float> Write { get; }

            public PresentationSliderBinding(
                Slider slider,
                Text valueText,
                Func<CrawlerResidentPresentationProfile, float> read,
                Action<float> write)
            {
                Slider = slider;
                ValueText = valueText;
                Read = read;
                Write = write;
            }
        }

        private readonly struct LayoutSnapshot
        {
            private readonly float _entryDuration;
            private readonly float _exitDuration;
            private readonly int _particleCount;
            private readonly float _shardScale;
            private readonly Vector2 _durationOffset;
            private readonly int _auraParticleCount;
            private readonly float _auraRadius;
            private readonly float _auraCyclesPerSecond;
            private readonly float _auraParticleSize;

            private LayoutSnapshot(CrawlerResidentLayoutProfile profile)
            {
                _entryDuration = profile.entryDuration;
                _exitDuration = profile.exitDuration;
                _particleCount = profile.materializationParticleCount;
                _shardScale = profile.materializationSizeScale;
                _durationOffset = profile.durationOffset;
                _auraParticleCount = profile.hoverAuraParticleCount;
                _auraRadius = profile.hoverAuraRadius;
                _auraCyclesPerSecond = profile.hoverAuraCyclesPerSecond;
                _auraParticleSize = profile.hoverAuraParticleSize;
            }

            public static LayoutSnapshot Capture(
                CrawlerResidentLayoutProfile profile)
            {
                return new LayoutSnapshot(profile);
            }

            public void Restore(CrawlerResidentLayoutProfile profile)
            {
                profile.entryDuration = _entryDuration;
                profile.exitDuration = _exitDuration;
                profile.materializationParticleCount = _particleCount;
                profile.materializationSizeScale = _shardScale;
                profile.durationOffset = _durationOffset;
                profile.hoverAuraParticleCount = _auraParticleCount;
                profile.hoverAuraRadius = _auraRadius;
                profile.hoverAuraCyclesPerSecond = _auraCyclesPerSecond;
                profile.hoverAuraParticleSize = _auraParticleSize;
            }
        }
    }
}
