using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Effects;
using WanjieScroll.Effects.Attacks;
using WanjieScroll.Effects.Projectiles;
using WanjieScroll.UI.Components;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// F7 专用的运行时 VFX 调参面板。它只修改当前 Play 会话中已加载的
    /// VfxProfile 实例；只有显式保存命令才通过 Editor 端口持久化当前 Profile。
    /// 场景 Bootstrap 负责把选择和重播动作接回战斗。
    ///
    /// 模块审查：本类超过 800 行是因为 F7 场景没有静态 Prefab，运行时 UGUI
    /// 层级、控件绑定、会话快照与交互状态必须共同维护“所见值就是下次重播值”这一
    /// 单一不变量；调参控件和初始值均由本类唯一写入。可滚动卡牌
    /// 选择层级及验收状态标签已由 VfxTestCardSelectionDropdown 独立拥有，
    /// 资产持久化已隔离在 VfxTestProfileSavePort 后；若后续加入预设管理
    /// 或颜色编辑，应继续提取新模块。
    /// </summary>
    internal sealed class VfxTestTuningPanel : MonoBehaviour
    {
        public const string Revision = "F7-VFX-TUNER-2026.08.19.18";

        private const float PanelWidth = 480f;
        private const float ExpandedHeight = 942f;
        private const float CollapsedHeight = 52f;
        private const float RowStartY = 124f;
        private const float RowHeight = 42f;

        private readonly List<CardConfig> _cards = new List<CardConfig>();
        private readonly List<SliderBinding> _bindings =
            new List<SliderBinding>();
        private readonly Dictionary<string, ProfileSnapshot> _defaults =
            new Dictionary<string, ProfileSnapshot>(
                StringComparer.OrdinalIgnoreCase);

        private Action<int> _onSelectionChanged;
        private Action _onReplayRequested;
        private RectTransform _panelRect;
        private GameObject _bodyRoot;
        private VfxTestCardSelectionDropdown _cardSelector;
        private Text _profileLabel;
        private Text _collapseLabel;
        private Button _previousButton;
        private Button _nextButton;
        private Button _replayButton;
        private Button _resetButton;
        private Button _saveButton;
        private VfxProfile _profile;
        private ProfileSnapshot _snapshot;
        private int _selectedIndex = -1;
        private bool _refreshing;
        private bool _expanded = true;

        public static VfxTestTuningPanel Create(
            Transform parent,
            Action<int> onSelectionChanged,
            Action onReplayRequested)
        {
            var panelObject = new GameObject(
                "VfxRuntimeTuningPanel",
                typeof(RectTransform),
                typeof(Image),
                typeof(VfxTestTuningPanel));
            panelObject.transform.SetParent(parent, false);
            var panel = panelObject.GetComponent<VfxTestTuningPanel>();
            panel._onSelectionChanged = onSelectionChanged;
            panel._onReplayRequested = onReplayRequested;
            panel.BuildUi();
            return panel;
        }

        public void BindCards(
            IReadOnlyList<CardConfig> cards,
            int selectedIndex)
        {
            _cards.Clear();
            if (cards != null)
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i] != null)
                        _cards.Add(cards[i]);
                }
            }

            _selectedIndex = -1;
            int resolvedIndex = _cardSelector != null
                ? _cardSelector.BindCards(_cards, selectedIndex)
                : selectedIndex;
            SelectIndex(resolvedIndex, notify: false);
            if (resolvedIndex < 0) RefreshSelectedCard();
            if (resolvedIndex != selectedIndex)
                _onSelectionChanged?.Invoke(resolvedIndex);
        }

        public void SetSelectedIndex(int selectedIndex)
        {
            SelectIndex(selectedIndex, notify: false);
        }

        private void BuildUi()
        {
            _panelRect = GetComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(0.5f, 1f);
            _panelRect.anchorMax = new Vector2(0.5f, 1f);
            _panelRect.pivot = new Vector2(0.5f, 1f);
            _panelRect.sizeDelta = new Vector2(PanelWidth, ExpandedHeight);
            _panelRect.anchoredPosition = new Vector2(0f, -22f);

            Image panelImage = GetComponent<Image>();
            panelImage.color = new Color(0.045f, 0.035f, 0.028f, 0.94f);
            panelImage.raycastTarget = false;

            Text title = UIElementFactory.CreateText(
                "Title",
                transform,
                "F7 卡牌特效实时调参",
                21,
                new Color(1f, 0.82f, 0.4f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(
                title.rectTransform,
                16f,
                1f,
                390f,
                27f);

            Text revision = UIElementFactory.CreateText(
                "Revision",
                transform,
                Revision,
                12,
                new Color(0.72f, 0.69f, 0.62f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(
                revision.rectTransform,
                16f,
                27f,
                400f,
                20f);

            Button collapse = CreateTextButton(
                "Collapse",
                transform,
                "－",
                424f,
                9f,
                40f,
                34f,
                ToggleExpanded);
            _collapseLabel = collapse.GetComponentInChildren<Text>();

            RectTransform body = CreateRect(
                "Body",
                transform,
                0f,
                52f,
                PanelWidth,
                ExpandedHeight - CollapsedHeight);
            _bodyRoot = body.gameObject;

            _cardSelector = VfxTestCardSelectionDropdown.Create(
                body,
                16f,
                0f,
                448f,
                32f,
                index => SelectIndex(index, notify: true));

            _previousButton = CreateTextButton(
                "PreviousCard",
                body,
                "上一张",
                16f,
                34f,
                68f,
                36f,
                () => ChangeSelection(-1));
            _nextButton = CreateTextButton(
                "NextCard",
                body,
                "下一张",
                90f,
                34f,
                68f,
                36f,
                () => ChangeSelection(1));
            _replayButton = CreateTextButton(
                "ReplayCard",
                body,
                "重播",
                164f,
                34f,
                108f,
                36f,
                Replay);
            _resetButton = CreateTextButton(
                "ResetProfile",
                body,
                "恢复默认",
                278f,
                34f,
                106f,
                36f,
                ResetCurrentProfile);
            _saveButton = CreateTextButton(
                "SaveProfile",
                body,
                "保存",
                390f,
                34f,
                74f,
                36f,
                SaveCurrentProfile);

            _profileLabel = UIElementFactory.CreateText(
                "ProfileInfo",
                body,
                string.Empty,
                16,
                new Color(0.76f, 0.84f, 0.9f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(
                _profileLabel.rectTransform,
                16f,
                76f,
                448f,
                42f);

            AddProfileSlider(
                body,
                "主体尺寸",
                0.1f,
                2.5f,
                false,
                profile => profile.projectileScale,
                (profile, value) => profile.projectileScale = value,
                "0.00");
            AddProfileSlider(
                body,
                "飞行速度倍率",
                0.25f,
                3f,
                false,
                ReadSpeedMultiplier,
                WriteSpeedMultiplier,
                "0.00×");
            AddProfileSlider(
                body,
                "多发间隔",
                0f,
                0.3f,
                false,
                profile => profile.multiProjectileDelay,
                (profile, value) => profile.multiProjectileDelay = value,
                "0.000s");
            AddProfileSlider(
                body,
                "主粒子大小",
                0.01f,
                0.3f,
                false,
                profile => profile.hitParticleSize,
                (profile, value) => profile.hitParticleSize = value,
                "0.000");
            AddProfileSlider(
                body,
                "次粒子大小",
                0.01f,
                0.3f,
                false,
                profile => profile.secondaryParticleSize,
                (profile, value) => profile.secondaryParticleSize = value,
                "0.000");
            AddProfileSlider(
                body,
                "主命中粒子数",
                0f,
                40f,
                true,
                profile => profile.hitParticleCount,
                (profile, value) =>
                    profile.hitParticleCount = Mathf.RoundToInt(value),
                "0");
            AddProfileSlider(
                body,
                "次命中粒子数",
                0f,
                20f,
                true,
                profile => profile.secondaryParticleCount,
                (profile, value) =>
                    profile.secondaryParticleCount = Mathf.RoundToInt(value),
                "0");
            AddProfileSlider(
                body,
                "尾迹寿命",
                0.02f,
                0.6f,
                false,
                profile => profile.trailTime,
                (profile, value) => profile.trailTime = value,
                "0.000s");
            AddProfileSlider(
                body,
                "刀剑主体长度",
                0.5f,
                2.5f,
                false,
                profile => ReadPositive(
                    profile.proceduralBladeLengthScale),
                (profile, value) =>
                    profile.proceduralBladeLengthScale = value,
                "0.00×",
                requiredVisualType: "ProceduralBlade");
            AddProfileSlider(
                body,
                "刀剑主体粗细",
                0.08f,
                0.5f,
                false,
                profile => profile.proceduralBladeWidthRatio,
                (profile, value) =>
                    profile.proceduralBladeWidthRatio = value,
                "0.00",
                requiredVisualType: "ProceduralBlade");
            AddProfileSlider(
                body,
                "棍身长度",
                0.4f,
                2.5f,
                false,
                profile => ReadPositive(profile.ruyiStaffLengthScale),
                (profile, value) => profile.ruyiStaffLengthScale = value,
                "0.00×",
                requiredVisualType:
                    RuyiStaffParticleVisual.VisualTypeName);
            AddProfileSlider(
                body,
                "棍身粗细",
                0.4f,
                2.5f,
                false,
                profile => ReadPositive(profile.ruyiStaffThicknessScale),
                (profile, value) => profile.ruyiStaffThicknessScale = value,
                "0.00×",
                requiredVisualType:
                    RuyiStaffParticleVisual.VisualTypeName);
            AddProfileSlider(
                body,
                "尾迹粒子大小",
                0.2f,
                3f,
                false,
                profile => ReadPositive(profile.ruyiStaffWakeScale),
                (profile, value) => profile.ruyiStaffWakeScale = value,
                "0.00×",
                requiredVisualType:
                    RuyiStaffParticleVisual.VisualTypeName);
            AddProfileSlider(
                body,
                "挥鞭总时长",
                0.3f,
                1f,
                false,
                profile => profile.whipSweepDuration,
                (profile, value) => profile.whipSweepDuration = value,
                "0.000s",
                requiredVisualType:
                    ProceduralWhipSweepPlayer.VisualTypeName);
            AddProfileSlider(
                body,
                "挥鞭弧度",
                0.12f,
                1.2f,
                false,
                profile => profile.whipArcHeight,
                (profile, value) => profile.whipArcHeight = value,
                "0.00",
                requiredVisualType:
                    ProceduralWhipSweepPlayer.VisualTypeName);
            AddProfileSlider(
                body,
                "鞭身柔软度",
                0f,
                1f,
                false,
                profile => profile.whipFlexibility,
                (profile, value) => profile.whipFlexibility = value,
                "0.00",
                requiredVisualType:
                    ProceduralWhipSweepPlayer.VisualTypeName);
            AddProfileSlider(
                body,
                "鞭身粗细",
                0.05f,
                0.32f,
                false,
                profile => profile.whipSegmentThickness,
                (profile, value) =>
                    profile.whipSegmentThickness = value,
                "0.00",
                requiredVisualType:
                    ProceduralWhipSweepPlayer.VisualTypeName);
            AddProfileSlider(
                body,
                "气环强度",
                0f,
                2f,
                false,
                profile => profile.whipAirRingIntensity,
                (profile, value) =>
                    profile.whipAirRingIntensity = value,
                "0.00×",
                requiredVisualType:
                    ProceduralWhipSweepPlayer.VisualTypeName);
            AddProfileSlider(
                body,
                "空气尾流强度",
                0f,
                3f,
                false,
                profile => profile.whipAirWakeIntensity,
                (profile, value) =>
                    profile.whipAirWakeIntensity = value,
                "0.00×",
                requiredVisualType:
                    ProceduralWhipSweepPlayer.VisualTypeName);
            AddProfileSlider(
                body,
                "空气尾流长度",
                0.25f,
                3f,
                false,
                profile => profile.whipAirWakeLength,
                (profile, value) =>
                    profile.whipAirWakeLength = value,
                "0.00×",
                requiredVisualType:
                    ProceduralWhipSweepPlayer.VisualTypeName);

            Text hint = UIElementFactory.CreateText(
                "Hint",
                body,
                "拖动只改本次 Play；点“重播”预览，满意后点“保存”写回本卡 VfxProfile。\n“恢复默认”回到进入 Play 或最近一次保存值；粒子数量只调整命中火花。",
                15,
                new Color(0.73f, 0.67f, 0.57f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(
                hint.rectTransform,
                16f,
                766f,
                448f,
                48f);

            RefreshSelectedCard();
        }

        private void AddProfileSlider(
            Transform parent,
            string label,
            float minimum,
            float maximum,
            bool wholeNumbers,
            Func<VfxProfile, float> read,
            Action<VfxProfile, float> write,
            string format,
            string requiredVisualType = null)
        {
            int rowIndex = _bindings.Count;
            float y = RowStartY + rowIndex * RowHeight;
            RectTransform row = CreateRect(
                $"{label}Row",
                parent,
                0f,
                y,
                PanelWidth,
                RowHeight);

            Text labelText = UIElementFactory.CreateText(
                "Label",
                row,
                label,
                17,
                new Color(0.91f, 0.88f, 0.79f),
                TextAnchor.MiddleLeft);
            PlaceTopLeft(labelText.rectTransform, 16f, 3f, 136f, 32f);

            Slider slider = CreateSlider(
                "Slider",
                row,
                154f,
                8f,
                226f,
                25f,
                minimum,
                maximum,
                wholeNumbers);
            Text valueText = UIElementFactory.CreateText(
                "Value",
                row,
                "--",
                16,
                new Color(1f, 0.76f, 0.32f),
                TextAnchor.MiddleRight);
            PlaceTopLeft(valueText.rectTransform, 386f, 3f, 78f, 32f);

            var binding = new SliderBinding(
                row.gameObject,
                slider,
                valueText,
                read,
                write,
                format,
                wholeNumbers,
                requiredVisualType);
            _bindings.Add(binding);
            slider.onValueChanged.AddListener(value =>
                OnSliderChanged(binding, value));
        }

        private void OnSliderChanged(
            SliderBinding binding,
            float value)
        {
            if (_refreshing || _profile == null)
                return;

            float normalized = binding.WholeNumbers
                ? Mathf.Round(value)
                : value;
            binding.Write(_profile, normalized);
            binding.ValueText.text =
                binding.Read(_profile).ToString(binding.Format);
            _profileLabel.color = new Color(1f, 0.72f, 0.3f);
            _profileLabel.text =
                $"{_profile.contentId}  |  {_profile.name}  |  {_profile.visualType}\n" +
                "有未保存修改";
        }

        private void ChangeSelection(int delta)
        {
            _cardSelector?.MoveSelection(delta);
        }

        private void SelectIndex(int index, bool notify)
        {
            if (_cards.Count == 0)
            {
                if (_selectedIndex != -1 || _profile != null)
                {
                    _selectedIndex = -1;
                    RefreshSelectedCard();
                }
                return;
            }

            if (index < 0)
            {
                if (_selectedIndex == -1)
                    return;
                _selectedIndex = -1;
                RefreshSelectedCard();
                if (notify)
                    _onSelectionChanged?.Invoke(-1);
                return;
            }

            int clamped = Mathf.Clamp(index, 0, _cards.Count - 1);
            _cardSelector?.SetSelectedIndex(clamped);
            if (_selectedIndex == clamped)
                return;

            _selectedIndex = clamped;
            RefreshSelectedCard();
            if (notify)
                _onSelectionChanged?.Invoke(_selectedIndex);
        }

        private void RefreshSelectedCard()
        {
            _profile = null;
            _snapshot = null;
            if (_profileLabel != null)
                _profileLabel.color = new Color(0.76f, 0.84f, 0.9f);

            if (_cards.Count == 0 || _selectedIndex < 0)
            {
                if (_profileLabel != null)
                    _profileLabel.text =
                         "当前筛选没有可选卡牌。";
                RefreshBindings();
                RefreshButtonStates();
                return;
            }

            CardConfig card = _cards[_selectedIndex];
            _profile = VfxProfileRegistry.Get(card.contentId);
            if (_profile == null)
            {
                _profileLabel.text =
                    $"{card.contentId}\n无专属 VfxProfile；仍可重播共享表现。";
            }
            else
            {
                if (!_defaults.TryGetValue(
                        card.contentId,
                        out _snapshot))
                {
                    _snapshot = ProfileSnapshot.Capture(_profile);
                    _defaults.Add(card.contentId, _snapshot);
                }

                _profileLabel.text =
                    $"{card.contentId}  |  {_profile.name}  |  {_profile.visualType}";
            }

            RefreshBindings();
            RefreshButtonStates();
        }

        public void AdvanceToNextPending(int completedIndex)
        {
            if (_cardSelector == null)
                return;

            int nextIndex = _cardSelector.AdvanceToNextPending(completedIndex);
            SelectIndex(nextIndex, notify: false);
            // Inspector writes may precede this panel's next Update sync.
            // Commit even if the next card already matches the panel index.
            _onSelectionChanged?.Invoke(nextIndex);
        }

        private void RefreshBindings()
        {
            _refreshing = true;
            int visibleRowIndex = 0;
            for (int i = 0; i < _bindings.Count; i++)
            {
                SliderBinding binding = _bindings[i];
                bool visible = string.IsNullOrWhiteSpace(
                        binding.RequiredVisualType) ||
                    (_profile != null && string.Equals(
                        _profile.visualType,
                        binding.RequiredVisualType,
                        StringComparison.OrdinalIgnoreCase));
                binding.Root.SetActive(visible);
                if (!visible)
                    continue;

                PlaceTopLeft(
                    binding.Root.GetComponent<RectTransform>(),
                    0f,
                    RowStartY + visibleRowIndex * RowHeight,
                    PanelWidth,
                    RowHeight);
                visibleRowIndex++;

                binding.Slider.interactable = _profile != null;
                if (_profile == null)
                {
                    binding.ValueText.text = "--";
                    continue;
                }

                float value = Mathf.Clamp(
                    binding.Read(_profile),
                    binding.Slider.minValue,
                    binding.Slider.maxValue);
                binding.Slider.value = value;
                binding.ValueText.text = value.ToString(binding.Format);
            }
            _refreshing = false;
        }

        private void RefreshButtonStates()
        {
            bool hasCards = _cards.Count > 0 &&
                _selectedIndex >= 0;
            if (_previousButton != null)
                _previousButton.interactable = _cards.Count > 1;
            if (_nextButton != null)
                _nextButton.interactable = _cards.Count > 1;
            if (_replayButton != null)
                _replayButton.interactable = hasCards;
            if (_resetButton != null)
                _resetButton.interactable = _profile != null;
            if (_saveButton != null)
            {
                _saveButton.interactable = _profile != null &&
                    VfxTestProfileSavePort.IsAvailable;
            }
        }

        private void Replay()
        {
            if (_cards.Count == 0 || _selectedIndex < 0)
                return;

            CardConfig card = _cards[_selectedIndex];
            if (_profile != null)
            {
                Debug.Log(
                    $"[VFXTest][Tuning] replay={card.cardName} " +
                    $"scale={_profile.projectileScale:F2} " +
                    $"speed={ReadSpeedMultiplier(_profile):F2}x " +
                    $"delay={_profile.multiProjectileDelay:F3}s " +
                    $"hitSize={_profile.hitParticleSize:F3} " +
                    $"secondarySize={_profile.secondaryParticleSize:F3}");
            }

            _onReplayRequested?.Invoke();
        }

        private void ResetCurrentProfile()
        {
            if (_profile == null || _snapshot == null)
                return;

            _snapshot.Restore(_profile);
            RefreshBindings();
            _profileLabel.color = new Color(0.76f, 0.84f, 0.9f);
            _profileLabel.text =
                $"{_profile.contentId}  |  {_profile.name}  |  {_profile.visualType}\n" +
                "已恢复当前保存基线";
            Debug.Log(
                $"[VFXTest][Tuning] 已恢复 {_profile.contentId} 的当前保存基线。");
        }

        private void SaveCurrentProfile()
        {
            if (_profile == null)
                return;

            VfxProfileSaveResult result =
                VfxTestProfileSavePort.Save(_profile);
            if (result.Success)
            {
                _snapshot = ProfileSnapshot.Capture(_profile);
                _defaults[_profile.contentId] = _snapshot;
                RefreshBindings();
                _profileLabel.color = new Color(0.62f, 0.92f, 0.58f);
                Debug.Log(
                    $"[VFXTest][Tuning] {_profile.contentId} {result.Message}");
            }
            else
            {
                _profileLabel.color = new Color(1f, 0.48f, 0.38f);
                Debug.LogError(
                    $"[VFXTest][Tuning] {_profile.contentId} {result.Message}");
            }

            _profileLabel.text =
                $"{_profile.contentId}  |  {_profile.name}  |  {_profile.visualType}\n" +
                result.Message;
        }

        private float ReadSpeedMultiplier(VfxProfile profile)
        {
            float baseline = _snapshot != null
                ? Mathf.Max(0.04f, _snapshot.FlightDuration)
                : Mathf.Max(0.04f, profile.flightDuration);
            return baseline / Mathf.Max(0.04f, profile.flightDuration);
        }

        private void WriteSpeedMultiplier(
            VfxProfile profile,
            float multiplier)
        {
            float baseline = _snapshot != null
                ? Mathf.Max(0.04f, _snapshot.FlightDuration)
                : Mathf.Max(0.04f, profile.flightDuration);
            profile.flightDuration = Mathf.Max(
                0.04f,
                baseline / Mathf.Max(0.25f, multiplier));
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
            foreach (ProfileSnapshot snapshot in _defaults.Values)
                snapshot.RestoreCapturedProfile();
        }

        private static float ReadPositive(float value)
        {
            return value > 0f ? value : 1f;
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
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.86f, 0.62f, 1f);
            colors.pressedColor = new Color(0.78f, 0.62f, 0.4f, 1f);
            colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.55f);
            button.colors = colors;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            Text text = UIElementFactory.CreateText(
                "Label",
                button.transform,
                label,
                16,
                new Color(1f, 0.91f, 0.73f),
                TextAnchor.MiddleCenter);
            UIElementFactory.Stretch(text.rectTransform);
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            return button;
        }

        private static Slider CreateSlider(
            string name,
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
                name,
                typeof(RectTransform),
                typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            RectTransform sliderRect =
                sliderObject.GetComponent<RectTransform>();
            PlaceTopLeft(sliderRect, x, y, width, height);

            Image background = UIElementFactory.CreateImage(
                "Background",
                sliderObject.transform);
            background.color = new Color(0.16f, 0.14f, 0.12f, 1f);
            background.raycastTarget = false;
            RectTransform backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(0f, 7f);
            backgroundRect.anchoredPosition = Vector2.zero;

            RectTransform fillArea = CreateStretchRect(
                "Fill Area",
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
                "Handle Slide Area",
                sliderObject.transform,
                9f,
                0f,
                -9f,
                0f);
            Image handle = UIElementFactory.CreateImage("Handle", handleArea);
            handle.color = new Color(1f, 0.9f, 0.62f, 1f);
            RectTransform handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0.5f, 0.5f);
            handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(18f, 18f);
            handleRect.anchoredPosition = Vector2.zero;

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
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
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private sealed class SliderBinding
        {
            public readonly GameObject Root;
            public readonly Slider Slider;
            public readonly Text ValueText;
            public readonly Func<VfxProfile, float> Read;
            public readonly Action<VfxProfile, float> Write;
            public readonly string Format;
            public readonly bool WholeNumbers;
            public readonly string RequiredVisualType;

            public SliderBinding(
                GameObject root,
                Slider slider,
                Text valueText,
                Func<VfxProfile, float> read,
                Action<VfxProfile, float> write,
                string format,
                bool wholeNumbers,
                string requiredVisualType)
            {
                Root = root;
                Slider = slider;
                ValueText = valueText;
                Read = read;
                Write = write;
                Format = format;
                WholeNumbers = wholeNumbers;
                RequiredVisualType = requiredVisualType;
            }
        }

        private sealed class ProfileSnapshot
        {
            private VfxProfile _capturedProfile;

            public float ProjectileScale;
            public float FlightDuration;
            public float MultiProjectileDelay;
            public float HitParticleSize;
            public float SecondaryParticleSize;
            public int HitParticleCount;
            public int SecondaryParticleCount;
            public float TrailTime;
            public float ProceduralBladeLengthScale;
            public float ProceduralBladeWidthRatio;
            public float RuyiStaffLengthScale;
            public float RuyiStaffThicknessScale;
            public float RuyiStaffWakeScale;
            public float WhipSweepDuration;
            public float WhipArcHeight;
            public float WhipFlexibility;
            public float WhipSegmentThickness;
            public float WhipAirRingIntensity;
            public float WhipAirWakeIntensity;
            public float WhipAirWakeLength;

            public static ProfileSnapshot Capture(VfxProfile profile)
            {
                return new ProfileSnapshot
                {
                    _capturedProfile = profile,
                    ProjectileScale = profile.projectileScale,
                    FlightDuration = profile.flightDuration,
                    MultiProjectileDelay = profile.multiProjectileDelay,
                    HitParticleSize = profile.hitParticleSize,
                    SecondaryParticleSize = profile.secondaryParticleSize,
                    HitParticleCount = profile.hitParticleCount,
                    SecondaryParticleCount = profile.secondaryParticleCount,
                    TrailTime = profile.trailTime,
                    ProceduralBladeLengthScale =
                        profile.proceduralBladeLengthScale,
                    ProceduralBladeWidthRatio =
                        profile.proceduralBladeWidthRatio,
                    RuyiStaffLengthScale = profile.ruyiStaffLengthScale,
                    RuyiStaffThicknessScale = profile.ruyiStaffThicknessScale,
                    RuyiStaffWakeScale = profile.ruyiStaffWakeScale,
                    WhipSweepDuration = profile.whipSweepDuration,
                    WhipArcHeight = profile.whipArcHeight,
                    WhipFlexibility = profile.whipFlexibility,
                    WhipSegmentThickness =
                        profile.whipSegmentThickness,
                    WhipAirRingIntensity =
                        profile.whipAirRingIntensity,
                    WhipAirWakeIntensity =
                        profile.whipAirWakeIntensity,
                    WhipAirWakeLength = profile.whipAirWakeLength
                };
            }

            public void Restore(VfxProfile profile)
            {
                profile.projectileScale = ProjectileScale;
                profile.flightDuration = FlightDuration;
                profile.multiProjectileDelay = MultiProjectileDelay;
                profile.hitParticleSize = HitParticleSize;
                profile.secondaryParticleSize = SecondaryParticleSize;
                profile.hitParticleCount = HitParticleCount;
                profile.secondaryParticleCount = SecondaryParticleCount;
                profile.trailTime = TrailTime;
                profile.proceduralBladeLengthScale =
                    ProceduralBladeLengthScale;
                profile.proceduralBladeWidthRatio =
                    ProceduralBladeWidthRatio;
                profile.ruyiStaffLengthScale = RuyiStaffLengthScale;
                profile.ruyiStaffThicknessScale = RuyiStaffThicknessScale;
                profile.ruyiStaffWakeScale = RuyiStaffWakeScale;
                profile.whipSweepDuration = WhipSweepDuration;
                profile.whipArcHeight = WhipArcHeight;
                profile.whipFlexibility = WhipFlexibility;
                profile.whipSegmentThickness =
                    WhipSegmentThickness;
                profile.whipAirRingIntensity =
                    WhipAirRingIntensity;
                profile.whipAirWakeIntensity =
                    WhipAirWakeIntensity;
                profile.whipAirWakeLength = WhipAirWakeLength;
            }

            public void RestoreCapturedProfile()
            {
                if (_capturedProfile != null)
                    Restore(_capturedProfile);
            }
        }
    }
}
