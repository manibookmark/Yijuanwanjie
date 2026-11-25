using System;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Components
{
    /// <summary>首页设置面板的纯视图；不依赖 PlayerPrefs、Audio 或场景系统。</summary>
    public sealed class SettingsPanelView : MonoBehaviour
    {
        public readonly struct SettingsSnapshot
        {
            public SettingsSnapshot(
                bool fullscreen,
                bool vSync,
                float master,
                float music,
                float sfx,
                float voice,
                bool muted,
                KeyCode endTurnKey)
            {
                Fullscreen = fullscreen;
                VSync = vSync;
                Master = master;
                Music = music;
                Sfx = sfx;
                Voice = voice;
                Muted = muted;
                EndTurnKey = endTurnKey;
            }

            public bool Fullscreen { get; }
            public bool VSync { get; }
            public float Master { get; }
            public float Music { get; }
            public float Sfx { get; }
            public float Voice { get; }
            public bool Muted { get; }
            public KeyCode EndTurnKey { get; }
        }

        public sealed class Callbacks
        {
            public Action<bool> FullscreenChanged;
            public Action<bool> VSyncChanged;
            public Action<float> MasterChanged;
            public Action<float> MusicChanged;
            public Action<float> SfxChanged;
            public Action<float> VoiceChanged;
            public Action<bool> MuteChanged;
            public Func<ShortcutAction, KeyCode, string> ShortcutChanged;
            public Action ResetRequested;
            public Action CloseRequested;
        }

        private sealed class ToggleControl
        {
            public Toggle Toggle;
            public Image Track;
            public RectTransform Knob;
            public Text State;
        }

        private JadePanelView _shell;
        private ToggleControl _fullscreen;
        private ToggleControl _vSync;
        private ToggleControl _mute;
        private Slider _master;
        private Slider _music;
        private Slider _sfx;
        private Slider _voice;
        private Text _masterValue;
        private Text _musicValue;
        private Text _sfxValue;
        private Text _voiceValue;
        private Button _endTurnShortcut;
        private Text _endTurnShortcutValue;
        private Text _shortcutStatus;
        private Button _resetButton;
        private Button _closeButton;
        private Callbacks _callbacks;
        private bool _capturingEndTurn;

        public bool IsCapturingShortcut => _capturingEndTurn;

        public void Initialize(Action onClose)
        {
            _callbacks = new Callbacks { CloseRequested = onClose };
            _shell = gameObject.AddComponent<JadePanelView>();
            _shell.Initialize("设置", new Vector2(1320f, 820f));

            BuildDisplaySection();
            BuildAudioSection();
            BuildShortcutSection();
            BuildFooter();
            ConfigureNavigation();
        }

        public void Bind(SettingsSnapshot snapshot, Callbacks callbacks)
        {
            _callbacks = callbacks ?? new Callbacks();

            BindToggle(_fullscreen, _callbacks.FullscreenChanged);
            BindToggle(_vSync, _callbacks.VSyncChanged);
            BindToggle(_mute, _callbacks.MuteChanged);
            BindSlider(_master, _masterValue, _callbacks.MasterChanged);
            BindSlider(_music, _musicValue, _callbacks.MusicChanged);
            BindSlider(_sfx, _sfxValue, _callbacks.SfxChanged);
            BindSlider(_voice, _voiceValue, _callbacks.VoiceChanged);

            Refresh(snapshot);
        }

        public void Refresh(SettingsSnapshot snapshot)
        {
            SetToggleWithoutNotify(_fullscreen, snapshot.Fullscreen);
            SetToggleWithoutNotify(_vSync, snapshot.VSync);
            SetToggleWithoutNotify(_mute, snapshot.Muted);
            SetSliderWithoutNotify(_master, _masterValue, snapshot.Master);
            SetSliderWithoutNotify(_music, _musicValue, snapshot.Music);
            SetSliderWithoutNotify(_sfx, _sfxValue, snapshot.Sfx);
            SetSliderWithoutNotify(_voice, _voiceValue, snapshot.Voice);
            SetShortcutValue(snapshot.EndTurnKey);
        }

        public void Show()
        {
            Selectable initialSelection = _fullscreen != null
                ? (Selectable)_fullscreen.Toggle
                : _closeButton;
            _shell.Show(initialSelection);
        }

        public void Hide()
        {
            CancelShortcutCapture();
            _shell.Hide();
        }

        public void CancelShortcutCapture()
        {
            if (!_capturingEndTurn)
                return;
            _capturingEndTurn = false;
            SetShortcutValue(ShortcutBindingRuntime.GetBinding(ShortcutAction.EndTurn));
            SetShortcutStatus("已取消", false);
        }

        private void Update()
        {
            if (!_capturingEndTurn || !Input.anyKeyDown)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelShortcutCapture();
                return;
            }

            foreach (KeyCode candidate in Enum.GetValues(typeof(KeyCode)))
            {
                if (!Input.GetKeyDown(candidate))
                    continue;

                if (!ShortcutBindingRuntime.TryValidate(candidate, out string validation))
                {
                    SetShortcutStatus(validation, true);
                    return;
                }

                string error = _callbacks?.ShortcutChanged?.Invoke(
                    ShortcutAction.EndTurn, candidate);
                if (!string.IsNullOrEmpty(error))
                {
                    SetShortcutStatus(error, true);
                    return;
                }

                _capturingEndTurn = false;
                SetShortcutValue(candidate);
                SetShortcutStatus("已保存", false);
                return;
            }
        }

        private void BuildDisplaySection()
        {
            RectTransform section = CreateSectionCard(
                "DisplaySection",
                new Vector2(330f, 516f),
                new Vector2(-444f, -12f),
                "壹",
                "画面");

            _fullscreen = CreateToggleRow(
                section, "全屏", -100f, 274f);
            _vSync = CreateToggleRow(
                section, "垂直同步", -196f, 274f);
        }

        private void BuildAudioSection()
        {
            RectTransform section = CreateSectionCard(
                "AudioSection",
                new Vector2(440f, 516f),
                new Vector2(-48f, -12f),
                "贰",
                "声音");

            _master = CreateSliderRow(
                section, "总音量", -86f,
                out _masterValue, 384f);
            _music = CreateSliderRow(
                section, "音乐", -160f,
                out _musicValue, 384f);
            _sfx = CreateSliderRow(
                section, "音效", -234f,
                out _sfxValue, 384f);
            _voice = CreateSliderRow(
                section, "语音", -308f,
                out _voiceValue, 384f);
            _mute = CreateToggleRow(
                section, "静音", -390f, 384f);
        }

        private void BuildShortcutSection()
        {
            RectTransform section = CreateSectionCard(
                "ShortcutSection",
                new Vector2(400f, 516f),
                new Vector2(390f, -12f),
                "叁",
                "按键");

            Text intro = JadePanelView.CreateText(
                "Intro", section,
                "点击键位修改",
                15, JadePanelView.TextSecondary, TextAnchor.UpperLeft);
            PlaceTopLeft(
                intro.rectTransform,
                new Vector2(28f, -88f),
                new Vector2(344f, 58f));

            _endTurnShortcut = CreateShortcutRow(
                section,
                "结束回合",
                -164f,
                out _endTurnShortcutValue);
            _endTurnShortcut.onClick.AddListener(BeginEndTurnCapture);

            _shortcutStatus = JadePanelView.CreateText(
                "ShortcutStatus", section.transform,
                string.Empty,
                14, JadePanelView.JadeMuted, TextAnchor.MiddleCenter);
            RectTransform statusRect = _shortcutStatus.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(0f, 1f);
            statusRect.pivot = new Vector2(0f, 1f);
            statusRect.sizeDelta = new Vector2(344f, 68f);
            statusRect.anchoredPosition = new Vector2(28f, -282f);

        }

        private void BuildFooter()
        {
            var footer = new GameObject("SettingsActions", typeof(RectTransform));
            footer.transform.SetParent(_shell.ContentRoot, false);
            RectTransform footerRect = footer.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0f, 0f);
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.sizeDelta = new Vector2(0f, 74f);
            footerRect.anchoredPosition = Vector2.zero;

            _resetButton = JadePanelView.CreateTextButton(
                "ResetButton", footer.transform, "恢复默认",
                new Vector2(164f, 50f), false);
            RectTransform resetRect = _resetButton.GetComponent<RectTransform>();
            resetRect.anchorMin = new Vector2(1f, 0.5f);
            resetRect.anchorMax = new Vector2(1f, 0.5f);
            resetRect.pivot = new Vector2(1f, 0.5f);
            resetRect.anchoredPosition = new Vector2(-172f, 0f);

            _closeButton = JadePanelView.CreateTextButton(
                "DoneButton", footer.transform, "完成",
                new Vector2(152f, 50f), true);
            RectTransform closeRect = _closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(-8f, 0f);

            _resetButton.onClick.AddListener(OnResetClicked);
            _closeButton.onClick.AddListener(OnCloseClicked);
        }

        private void OnResetClicked()
        {
            _callbacks?.ResetRequested?.Invoke();
        }

        private void OnCloseClicked()
        {
            _callbacks?.CloseRequested?.Invoke();
        }

        private RectTransform CreateSectionCard(
            string name,
            Vector2 size,
            Vector2 position,
            string index,
            string title)
        {
            var section = new GameObject(name, typeof(Image), typeof(Outline));
            section.transform.SetParent(_shell.ContentRoot, false);
            RectTransform rect = section.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            section.GetComponent<Image>().color = new Color(0.013f, 0.047f, 0.057f, 0.96f);
            section.GetComponent<Outline>().effectColor = new Color(
                JadePanelView.Jade.r, JadePanelView.Jade.g, JadePanelView.Jade.b, 0.34f);
            section.GetComponent<Outline>().effectDistance = new Vector2(1f, -1f);

            Text indexText = JadePanelView.CreateText(
                "Index", section.transform, index, 30,
                JadePanelView.Gold, TextAnchor.MiddleCenter, true);
            PlaceTopLeft(indexText.rectTransform, new Vector2(24f, -20f), new Vector2(46f, 46f));

            Text titleText = JadePanelView.CreateText(
                "SectionTitle", section.transform, title, 24,
                JadePanelView.TextPrimary, TextAnchor.MiddleLeft, true);
            PlaceTopLeft(titleText.rectTransform, new Vector2(80f, -27f), new Vector2(180f, 32f));
            return rect;
        }

        private static ToggleControl CreateToggleRow(
            Transform parent,
            string label,
            float y,
            float width = 344f)
        {
            var row = new GameObject(label + "Row", typeof(Image));
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.sizeDelta = new Vector2(width, 76f);
            rowRect.anchoredPosition = new Vector2(28f, y);
            row.GetComponent<Image>().color = new Color(0.04f, 0.105f, 0.11f, 0.58f);
            row.GetComponent<Image>().raycastTarget = false;

            Text labelText = JadePanelView.CreateText(
                "Label", row.transform, label, 19,
                JadePanelView.TextPrimary, TextAnchor.MiddleLeft, true);
            PlaceTopLeft(labelText.rectTransform, new Vector2(18f, -24f), new Vector2(width - 132f, 28f));

            var toggleObject = new GameObject("Toggle", typeof(Image), typeof(Toggle), typeof(Outline));
            toggleObject.transform.SetParent(row.transform, false);
            RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(1f, 0.5f);
            toggleRect.anchorMax = new Vector2(1f, 0.5f);
            toggleRect.pivot = new Vector2(1f, 0.5f);
            toggleRect.sizeDelta = new Vector2(76f, 44f);
            toggleRect.anchoredPosition = new Vector2(-18f, 0f);

            Image track = toggleObject.GetComponent<Image>();
            track.color = new Color(0.06f, 0.13f, 0.14f, 1f);
            Outline outline = toggleObject.GetComponent<Outline>();
            outline.effectColor = new Color(
                JadePanelView.Jade.r, JadePanelView.Jade.g, JadePanelView.Jade.b, 0.56f);
            outline.effectDistance = new Vector2(1f, -1f);

            var knobObject = new GameObject("Knob", typeof(Image));
            knobObject.transform.SetParent(toggleObject.transform, false);
            RectTransform knob = knobObject.GetComponent<RectTransform>();
            knob.anchorMin = new Vector2(0.5f, 0.5f);
            knob.anchorMax = new Vector2(0.5f, 0.5f);
            knob.pivot = new Vector2(0.5f, 0.5f);
            knob.sizeDelta = new Vector2(28f, 28f);
            knob.anchoredPosition = new Vector2(-18f, 0f);
            knobObject.GetComponent<Image>().color = JadePanelView.TextSecondary;
            knobObject.GetComponent<Image>().raycastTarget = false;

            Toggle toggle = toggleObject.GetComponent<Toggle>();
            toggle.targetGraphic = track;
            toggle.graphic = null;
            toggle.transition = Selectable.Transition.ColorTint;

            Text state = JadePanelView.CreateText(
                "State", row.transform, "关", 13,
                JadePanelView.JadeMuted, TextAnchor.MiddleRight);
            RectTransform stateRect = state.rectTransform;
            stateRect.anchorMin = new Vector2(1f, 0f);
            stateRect.anchorMax = new Vector2(1f, 0f);
            stateRect.pivot = new Vector2(1f, 0f);
            stateRect.sizeDelta = new Vector2(88f, 16f);
            stateRect.anchoredPosition = new Vector2(-18f, 0f);

            return new ToggleControl { Toggle = toggle, Track = track, Knob = knob, State = state };
        }

        private static Slider CreateSliderRow(
            Transform parent,
            string label,
            float y,
            out Text valueText,
            float width = 556f)
        {
            var row = new GameObject(label + "Row", typeof(Image));
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.sizeDelta = new Vector2(width, 68f);
            rowRect.anchoredPosition = new Vector2(28f, y);
            row.GetComponent<Image>().color = new Color(0.04f, 0.105f, 0.11f, 0.58f);
            row.GetComponent<Image>().raycastTarget = false;

            Text labelText = JadePanelView.CreateText(
                "Label", row.transform, label, 19,
                JadePanelView.TextPrimary, TextAnchor.MiddleLeft, true);
            PlaceTopLeft(labelText.rectTransform, new Vector2(18f, -20f), new Vector2(86f, 28f));

            valueText = JadePanelView.CreateText(
                "Value", row.transform, "100%", 16,
                JadePanelView.Gold, TextAnchor.MiddleRight, true);
            RectTransform valueRect = valueText.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 1f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 1f);
            valueRect.sizeDelta = new Vector2(72f, 28f);
            valueRect.anchoredPosition = new Vector2(-18f, -20f);

            var sliderObject = new GameObject("Slider", typeof(Slider));
            sliderObject.transform.SetParent(row.transform, false);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0.5f);
            sliderRect.anchorMax = new Vector2(1f, 0.5f);
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.offsetMin = new Vector2(158f, -9f);
            sliderRect.offsetMax = new Vector2(-112f, 9f);

            var track = new GameObject("Track", typeof(Image));
            track.transform.SetParent(sliderObject.transform, false);
            RectTransform trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(0f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.sizeDelta = new Vector2(0f, 6f);
            track.GetComponent<Image>().color = new Color(0.035f, 0.08f, 0.09f, 1f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderObject.transform, false);
            JadePanelView.Stretch(fillArea.GetComponent<RectTransform>(), new Vector2(0f, 6f), new Vector2(0f, -6f));
            var fill = new GameObject("Fill", typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            JadePanelView.Stretch(fillRect);
            fill.GetComponent<Image>().color = JadePanelView.Jade;

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderObject.transform, false);
            JadePanelView.Stretch(handleArea.GetComponent<RectTransform>());
            var handle = new GameObject("Handle", typeof(Image), typeof(Outline));
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(20f, 26f);
            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = JadePanelView.TextPrimary;
            Outline handleOutline = handle.GetComponent<Outline>();
            handleOutline.effectColor = JadePanelView.Gold;
            handleOutline.effectDistance = new Vector2(1f, -1f);

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.transition = Selectable.Transition.ColorTint;
            return slider;
        }

        private static Button CreateShortcutRow(
            Transform parent,
            string label,
            float y,
            out Text keyText)
        {
            var row = new GameObject(label + "Row", typeof(Image));
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.sizeDelta = new Vector2(344f, 92f);
            rowRect.anchoredPosition = new Vector2(28f, y);
            row.GetComponent<Image>().color = new Color(0.04f, 0.105f, 0.11f, 0.58f);
            row.GetComponent<Image>().raycastTarget = false;

            Text labelText = JadePanelView.CreateText(
                "Label", row.transform, label, 19,
                JadePanelView.TextPrimary, TextAnchor.MiddleLeft, true);
            PlaceTopLeft(
                labelText.rectTransform,
                new Vector2(18f, -32f),
                new Vector2(174f, 28f));

            Button button = JadePanelView.CreateTextButton(
                "BindingButton", row.transform, "E",
                new Vector2(112f, 54f), false);
            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-16f, 0f);
            keyText = button.GetComponentInChildren<Text>();
            return button;
        }

        private void BeginEndTurnCapture()
        {
            _capturingEndTurn = true;
            _endTurnShortcutValue.text = "请按键";
            SetShortcutStatus("按退出键取消", false);
        }

        private void SetShortcutValue(KeyCode key)
        {
            if (_endTurnShortcutValue != null)
                _endTurnShortcutValue.text = ShortcutBindingRuntime.FormatKey(key);
        }

        private void SetShortcutStatus(string message, bool error)
        {
            if (_shortcutStatus == null)
                return;
            _shortcutStatus.text = message;
            _shortcutStatus.color = error
                ? JadePanelView.Danger
                : JadePanelView.JadeMuted;
        }

        private void ConfigureNavigation()
        {
            SetNavigation(_fullscreen.Toggle,
                _fullscreen.Toggle, _master, _fullscreen.Toggle, _vSync.Toggle);
            SetNavigation(_vSync.Toggle,
                _vSync.Toggle, _music, _fullscreen.Toggle, _resetButton);
            SetNavigation(_master,
                _fullscreen.Toggle, _endTurnShortcut, _master, _music);
            SetNavigation(_music,
                _vSync.Toggle, _endTurnShortcut, _master, _sfx);
            SetNavigation(_sfx,
                _vSync.Toggle, _endTurnShortcut, _music, _voice);
            SetNavigation(_voice,
                _vSync.Toggle, _endTurnShortcut, _sfx, _mute.Toggle);
            SetNavigation(_mute.Toggle,
                _vSync.Toggle, _endTurnShortcut, _voice, _closeButton);
            SetNavigation(_endTurnShortcut,
                _music, _endTurnShortcut, _endTurnShortcut, _closeButton);
            SetNavigation(_resetButton,
                _closeButton, _closeButton, _vSync.Toggle, _resetButton);
            SetNavigation(_closeButton,
                _resetButton, _resetButton, _endTurnShortcut, _closeButton);
        }

        private static void SetNavigation(
            Selectable selectable,
            Selectable left,
            Selectable right,
            Selectable up,
            Selectable down)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnLeft = left;
            navigation.selectOnRight = right;
            navigation.selectOnUp = up;
            navigation.selectOnDown = down;
            selectable.navigation = navigation;
        }

        private static void BindToggle(ToggleControl control, Action<bool> callback)
        {
            control.Toggle.onValueChanged.RemoveAllListeners();
            control.Toggle.onValueChanged.AddListener(value =>
            {
                UpdateToggleVisual(control, value);
                callback?.Invoke(value);
            });
        }

        private static void BindSlider(Slider slider, Text value, Action<float> callback)
        {
            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(current =>
            {
                UpdatePercentage(value, current);
                callback?.Invoke(current);
            });
        }

        private static void SetToggleWithoutNotify(ToggleControl control, bool value)
        {
            control.Toggle.SetIsOnWithoutNotify(value);
            UpdateToggleVisual(control, value);
        }

        private static void UpdateToggleVisual(ToggleControl control, bool enabled)
        {
            control.Track.color = enabled
                ? new Color(0.14f, 0.38f, 0.31f, 1f)
                : new Color(0.06f, 0.13f, 0.14f, 1f);
            control.Knob.anchoredPosition = new Vector2(enabled ? 18f : -18f, 0f);
            control.Knob.GetComponent<Image>().color = enabled
                ? JadePanelView.TextPrimary
                : JadePanelView.TextSecondary;
            control.State.text = enabled ? "开" : "关";
            control.State.color = enabled ? JadePanelView.Jade : JadePanelView.JadeMuted;
        }

        private static void SetSliderWithoutNotify(Slider slider, Text value, float current)
        {
            slider.SetValueWithoutNotify(current);
            UpdatePercentage(value, current);
        }

        private static void UpdatePercentage(Text value, float current)
        {
            value.text = $"{Mathf.RoundToInt(Mathf.Clamp01(current) * 100f)}%";
        }

        private static void PlaceTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}
