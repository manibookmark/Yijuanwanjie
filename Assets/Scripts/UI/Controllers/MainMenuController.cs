using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.UI.Components;

namespace WanjieScroll.UI.Controllers
{
    /// <summary>
    /// 首页场景装配与流程协调。主菜单、存档读写和设置应用保持原有合同；
    /// 存档选择、删除确认和控件表现分别下沉到独立 View。
    /// </summary>
    public class MainMenuController : SceneBootstrapBase
    {
        private const string BackgroundVideoPath = "Video/main-menu-bg-anim";
        private const string JadeButtonNormalPath =
            "Sprites/UI/Menu/Buttons/JadeGlass/JadeGlass_Normal";
        private const string JadeButtonHighlightedPath =
            "Sprites/UI/Menu/Buttons/JadeGlass/JadeGlass_Highlighted";
        private const string JadeButtonPressedPath =
            "Sprites/UI/Menu/Buttons/JadeGlass/JadeGlass_Pressed";
        private const string JadeButtonDisabledPath =
            "Sprites/UI/Menu/Buttons/JadeGlass/JadeGlass_Disabled";

        private static readonly Color MenuInk = new Color(0.035f, 0.105f, 0.13f, 0.98f);

        [Header("可选：Inspector 赋值（留空则从 Resources 自动加载）")]
        [SerializeField] private Sprite _backgroundSprite;
        [SerializeField] private VideoClip _backgroundVideo;
        [SerializeField] private Sprite _buttonSprite;

        private readonly List<Button> _menuButtons = new List<Button>();

        private RawImage _backgroundImage;
        private Texture _backgroundFallbackTexture;
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoTarget;
        private bool _backgroundVideoReady;
        private GameObject _menuObject;
        private Button _primaryButton;

        private GameObject _modalLayer;
        private CanvasGroup _modalCanvasGroup;
        private Coroutine _modalFadeRoutine;
        private SettingsPanelView _settingsPanel;
        private SaveArchivePanelView _savePanel;

        private bool _hasSavedProgress;
        private bool _isNavigating;

        protected override void OnSceneEnter()
        {
            _hasSavedProgress = SaveSlotStore.Current.HasAnySave();

            CreateBackground();
            CreateMenu();
            CreateModalLayer();
            _primaryButton?.Select();
        }

        protected override void OnSceneExit()
        {
            ReleaseBackgroundVideo();
            GameSettingsStore.Shared.Flush();
            if (_modalFadeRoutine != null)
                StopCoroutine(_modalFadeRoutine);

        }

        private void OnEnable()
        {
            // Initial OnEnable runs before Start creates the background.
            if (_backgroundImage != null && _videoPlayer == null)
                PrepareBackgroundVideo();
        }

        private void OnDisable()
        {
            ReleaseBackgroundVideo();
        }

        private void ReleaseBackgroundVideo()
        {
            // Invalidate the old player's callbacks before stopping native work.
            VideoPlayer player = _videoPlayer;
            _videoPlayer = null;
            _backgroundVideoReady = false;
            if (player != null)
            {
                player.prepareCompleted -= OnVideoPrepared;
                player.errorReceived -= OnVideoError;
                player.Stop();
                player.targetTexture = null;
                player.enabled = false;
                Destroy(player);
            }

            if (_backgroundImage != null)
            {
                _backgroundImage.texture = _backgroundFallbackTexture;
                _backgroundImage.color = _backgroundFallbackTexture != null
                    ? Color.white
                    : new Color(0.006f, 0.015f, 0.024f, 1f);
            }

            RenderTexture target = _videoTarget;
            _videoTarget = null;
            if (target != null)
            {
                target.Release();
                Destroy(target);
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape) ||
                _modalLayer == null || !_modalLayer.activeSelf)
                return;

            if (_settingsPanel != null &&
                _settingsPanel.gameObject.activeSelf &&
                _settingsPanel.IsCapturingShortcut)
            {
                _settingsPanel.CancelShortcutCapture();
                return;
            }

            if (_savePanel != null && _savePanel.gameObject.activeSelf &&
                _savePanel.CancelPendingDelete())
                return;
            CloseModal();
        }

        // ─────────────────────────────────────────────────────────────
        // 背景
        // ─────────────────────────────────────────────────────────────

        private void CreateBackground()
        {
            Sprite fallback = _backgroundSprite != null
                ? _backgroundSprite
                : RuntimeArtLoader.GetMainMenuBackground();

            var backgroundObject = new GameObject(
                "Background", typeof(RawImage), typeof(AspectRatioFitter));
            backgroundObject.transform.SetParent(Canvas.transform, false);
            backgroundObject.transform.SetAsFirstSibling();

            _backgroundImage = backgroundObject.GetComponent<RawImage>();
            _backgroundImage.raycastTarget = false;
            _backgroundImage.color = fallback != null
                ? Color.white
                : new Color(0.006f, 0.015f, 0.024f, 1f);
            _backgroundImage.texture = fallback != null ? fallback.texture : null;
            _backgroundFallbackTexture = _backgroundImage.texture;

            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            UIElementFactory.Stretch(backgroundRect);
            AspectRatioFitter aspect = backgroundObject.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspect.aspectRatio = 16f / 9f;
            PrepareBackgroundVideo();
        }

        private void PrepareBackgroundVideo()
        {
            ReleaseBackgroundVideo();
            VideoClip clip = _backgroundVideo != null
                ? _backgroundVideo
                : Resources.Load<VideoClip>(BackgroundVideoPath);
            if (clip == null)
            {
                Debug.LogWarning(
                    $"[MainMenu] 未找到首页背景视频 Resources/{BackgroundVideoPath}，使用静态背景。");
                return;
            }

            if (clip.width == 0 || clip.height == 0)
            {
                Debug.LogWarning("[MainMenu] 首页视频尺寸无效，使用静态背景。");
                return;
            }

            // Match the imported clip, keeping the 2K source out of a 720p buffer.
            _videoTarget = new RenderTexture((int)clip.width, (int)clip.height, 0, RenderTextureFormat.ARGB32)
            {
                name = "MainMenuBackgroundRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            if (!_videoTarget.Create())
            {
                ReleaseBackgroundVideo();
                Debug.LogWarning("[MainMenu] 无法创建首页视频纹理，使用静态背景。");
                return;
            }

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.isLooping = true;
            _videoPlayer.skipOnDrop = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoTarget;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = clip;
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.Prepare();
        }

        private void OnVideoPrepared(VideoPlayer source)
        {
            if (source != _videoPlayer || !isActiveAndEnabled || _backgroundVideoReady ||
                _backgroundImage == null || _videoTarget == null)
                return;
            _backgroundVideoReady = true;
            source.prepareCompleted -= OnVideoPrepared;
            _backgroundImage.texture = _videoTarget;
            _backgroundImage.color = Color.white;
            source.Play();
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            if (source != _videoPlayer)
                return;
            ReleaseBackgroundVideo();
            Debug.LogWarning($"[MainMenu] 首页背景视频播放失败，继续使用静态背景：{message}");
        }

        // ─────────────────────────────────────────────────────────────
        // 主菜单
        // ─────────────────────────────────────────────────────────────

        private void CreateMenu()
        {
            if (_menuObject != null)
                Destroy(_menuObject);
            _menuButtons.Clear();
            _primaryButton = null;

            _menuObject = new GameObject("Menu", typeof(RectTransform));
            _menuObject.transform.SetParent(Canvas.transform, false);
            RectTransform menuRect = _menuObject.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(0.835f, 0.5f);
            menuRect.anchorMax = new Vector2(0.835f, 0.5f);
            menuRect.pivot = new Vector2(0.5f, 0.5f);
            menuRect.sizeDelta = new Vector2(400f, 600f);
            menuRect.anchoredPosition = Vector2.zero;

            var entries = new List<MenuEntry>();
            if (_hasSavedProgress)
            {
                entries.Add(new MenuEntry("ContinueButton", "继续", OnContinueClicked));
                entries.Add(new MenuEntry("ArchiveButton", "存档", OpenSavePanel));
            }
            else
            {
                entries.Add(new MenuEntry("StartButton", "开始", OnStartClicked));
            }
            entries.Add(new MenuEntry("SettingsButton", "设置", OpenSettingsPanel));
            entries.Add(new MenuEntry("ExitButton", "退出", OnExitClicked));

            const float step = 126f;
            float firstY = (entries.Count - 1) * step * 0.5f;
            for (int i = 0; i < entries.Count; i++)
            {
                MenuEntry entry = entries[i];
                Button button = CreateMenuButton(
                    entry.ObjectName,
                    menuRect,
                    entry.Label,
                    new Vector2(0f, firstY - i * step),
                    entry.Callback);
                _menuButtons.Add(button);
                if (i == 0)
                    _primaryButton = button;
            }
        }

        private Button CreateMenuButton(
            string objectName,
            Transform parent,
            string labelText,
            Vector2 position,
            Action callback)
        {
            Sprite normal = _buttonSprite != null
                ? _buttonSprite
                : Resources.Load<Sprite>(JadeButtonNormalPath);
            Button button = UIElementFactory.CreateButton(objectName, parent, normal);
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(338f, 120f);
            rect.anchoredPosition = position;

            if (normal != null)
            {
                button.image.preserveAspect = true;
                button.transition = Selectable.Transition.SpriteSwap;
                SpriteState state = button.spriteState;
                state.highlightedSprite = Resources.Load<Sprite>(JadeButtonHighlightedPath);
                state.pressedSprite = Resources.Load<Sprite>(JadeButtonPressedPath);
                state.selectedSprite = state.highlightedSprite;
                state.disabledSprite = Resources.Load<Sprite>(JadeButtonDisabledPath);
                button.spriteState = state;
            }
            else
            {
                button.image.color = new Color(0.67f, 0.82f, 0.78f, 0.92f);
            }

            Text label = UIElementFactory.CreateText(
                "Label", button.transform, labelText, 30, MenuInk);
            UIElementFactory.Stretch(label.rectTransform);
            JadePanelView.ApplyFont(label, true);
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.75f, 0.93f, 0.88f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -1f);

            button.onClick.AddListener(() => callback());
            var hover = button.gameObject.AddComponent<HoverScaleEffect>();
            hover.HoverScale = 1.025f;
            return button;
        }

        // ─────────────────────────────────────────────────────────────
        // 模态面板装配
        // ─────────────────────────────────────────────────────────────

        private void CreateModalLayer()
        {
            _modalLayer = new GameObject("ModalLayer", typeof(Image), typeof(CanvasGroup));
            _modalLayer.transform.SetParent(Canvas.transform, false);
            UIElementFactory.Stretch(_modalLayer.GetComponent<RectTransform>());
            Image blocker = _modalLayer.GetComponent<Image>();
            blocker.color = new Color(0.002f, 0.008f, 0.012f, 0.92f);
            blocker.raycastTarget = true;
            _modalCanvasGroup = _modalLayer.GetComponent<CanvasGroup>();

            var settingsObject = new GameObject("SettingsPanel", typeof(RectTransform));
            settingsObject.transform.SetParent(_modalLayer.transform, false);
            _settingsPanel = settingsObject.AddComponent<SettingsPanelView>();
            _settingsPanel.Initialize(CloseModal);
            _settingsPanel.Hide();

            if (_hasSavedProgress)
                CreateSavePanel();

            _modalCanvasGroup.alpha = 0f;
            _modalLayer.SetActive(false);
        }

        private void CreateSavePanel()
        {
            if (_savePanel != null)
                Destroy(_savePanel.gameObject);

            var saveObject = new GameObject("SaveArchivePanel", typeof(RectTransform));
            saveObject.transform.SetParent(_modalLayer.transform, false);
            _savePanel = saveObject.AddComponent<SaveArchivePanelView>();
            _savePanel.Initialize(
                StartNewGameInSlot,
                LoadSlot,
                DeleteSlot,
                CloseModal);
            _savePanel.Hide();
        }

        private void OpenSettingsPanel()
        {
            if (_isNavigating || _modalLayer == null)
                return;
            BeginModal();
            _savePanel?.Hide();
            BindSettingsValues();
            _settingsPanel.Show();
        }

        private void OpenSavePanel()
        {
            if (_isNavigating || !_hasSavedProgress || _modalLayer == null)
                return;
            if (_savePanel == null)
                CreateSavePanel();

            BeginModal();
            _settingsPanel.Hide();
            RefreshSavePanel();
            _savePanel.Show();
        }

        private void BeginModal()
        {
            SetMenuInteractivity(false);
            _modalLayer.SetActive(true);
            if (_modalFadeRoutine != null)
                StopCoroutine(_modalFadeRoutine);
            _modalFadeRoutine = StartCoroutine(FadeModalIn());
        }

        private void CloseModal()
        {
            GameSettingsStore.Shared.Flush();
            if (_modalFadeRoutine != null)
            {
                StopCoroutine(_modalFadeRoutine);
                _modalFadeRoutine = null;
            }
            _settingsPanel?.Hide();
            _savePanel?.Hide();
            if (_modalLayer != null)
            {
                _modalCanvasGroup.alpha = 0f;
                _modalLayer.SetActive(false);
            }
            if (!_isNavigating)
                SetMenuInteractivity(true);
            _primaryButton?.Select();
        }

        private IEnumerator FadeModalIn()
        {
            const float duration = 0.16f;
            _modalCanvasGroup.alpha = 0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                _modalCanvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
                yield return null;
            }
            _modalCanvasGroup.alpha = 1f;
            _modalFadeRoutine = null;
        }

        private void RefreshSavePanel(int preferredSlot = 0)
        {
            if (_savePanel == null)
                return;
            var summaries = new List<SaveSlotSummary>(SaveSlotStore.SlotCount);
            for (int slot = 1; slot <= SaveSlotStore.SlotCount; slot++)
                summaries.Add(SaveSlotStore.Current.ReadSummary(slot));

            _savePanel.Refresh(
                summaries,
                preferredSlot,
                SaveSlotStore.Current.ActiveSlot);
        }

        // ─────────────────────────────────────────────────────────────
        // 设置协调
        // ─────────────────────────────────────────────────────────────

        private void BindSettingsValues()
        {
            GameSettingsStore store = GameSettingsStore.Shared;
            _settingsPanel.Bind(
                ReadSettingsSnapshot(store.Current),
                new SettingsPanelView.Callbacks
                {
                    FullscreenChanged = value => UpdateSettings(
                        current => current.WithDisplay(value, current.VSync)),
                    VSyncChanged = value => UpdateSettings(
                        current => current.WithDisplay(current.Fullscreen, value)),
                    MasterChanged = value => UpdateAudioSettings(
                        master: value),
                    MusicChanged = value => UpdateAudioSettings(
                        music: value),
                    SfxChanged = value => UpdateAudioSettings(
                        sfx: value),
                    VoiceChanged = value => UpdateAudioSettings(
                        voice: value),
                    MuteChanged = value => UpdateAudioSettings(
                        muted: value),
                    ShortcutChanged = OnShortcutChanged,
                    ResetRequested = OnResetSettings,
                    CloseRequested = CloseModal
                });
        }

        private static SettingsPanelView.SettingsSnapshot ReadSettingsSnapshot(
            GameSettingsData settings)
        {
            return new SettingsPanelView.SettingsSnapshot(
                settings.Fullscreen,
                settings.VSync,
                settings.MasterVolume,
                settings.MusicVolume,
                settings.SfxVolume,
                settings.VoiceVolume,
                settings.Muted,
                settings.EndTurnKey);
        }

        private static void UpdateSettings(
            Func<GameSettingsData, GameSettingsData> update)
        {
            GameSettingsStore store = GameSettingsStore.Shared;
            store.Update(update(store.Current));
        }

        private static void UpdateAudioSettings(
            float? master = null,
            float? music = null,
            float? sfx = null,
            float? voice = null,
            bool? muted = null)
        {
            UpdateSettings(current => current.WithAudio(
                master ?? current.MasterVolume,
                music ?? current.MusicVolume,
                sfx ?? current.SfxVolume,
                voice ?? current.VoiceVolume,
                muted ?? current.Muted));
        }

        private string OnShortcutChanged(ShortcutAction action, KeyCode key)
        {
            if (action != ShortcutAction.EndTurn)
                return "未知的快捷键动作";
            if (!ShortcutBindingRuntime.TryValidate(key, out string error))
                return error;

            UpdateSettings(current => current.WithEndTurnKey(key));
            return string.Empty;
        }

        private void OnResetSettings()
        {
            GameSettingsStore store = GameSettingsStore.Shared;
            store.ResetToRecommended(flush: true);
            _settingsPanel.Refresh(ReadSettingsSnapshot(store.Current));
        }

        // ─────────────────────────────────────────────────────────────
        // 存档与导航（业务顺序保持不变）
        // ─────────────────────────────────────────────────────────────

        private void OnStartClicked()
        {
            if (!_isNavigating)
                StartNewGameInSlot(FirstEmptySlot());
        }

        private void StartNewGameInSlot(int slot)
        {
            if (_isNavigating || slot == 0)
                return;
            _isNavigating = true;
            SaveSlotStore.Current.BeginNewGame(slot);
            CloseModal();
            SetBusyState(true);
            SceneLoadingIndicator.Show();
            SceneLoader.GoToOpeningCG();
        }

        private static int FirstEmptySlot()
        {
            for (int slot = 1; slot <= SaveSlotStore.SlotCount; slot++)
            {
                if (!File.Exists(SaveSlotStore.Current.GetSlotPath(slot)))
                    return slot;
            }
            return SaveSlotStore.Current.ActiveSlot;
        }

        private void OnContinueClicked()
        {
            if (_isNavigating)
                return;
            int slot = SaveSlotStore.Current.MostRecentFilledSlot();
            if (slot == 0)
            {
                StartNewGameInSlot(FirstEmptySlot());
                return;
            }
            LoadSlot(slot);
        }

        private void LoadSlot(int slot)
        {
            if (_isNavigating || slot == 0)
                return;
            _isNavigating = true;
            CloseModal();
            SetBusyState(true);
            SceneLoadingIndicator.Show();

            GameSaveService service = SaveSlotStore.Current.GetSlotService(slot);
            var meta = new MetaProgression();
            if (!service.TryLoad(meta, out SaveGameData saveData, out string error))
            {
                string message = error ?? "该存档位无可用存档。";
                Debug.LogWarning($"[MainMenu] {message}");
                _isNavigating = false;
                SceneLoadingIndicator.Hide();
                SetBusyState(false);
                OpenSavePanel();
                _savePanel?.ShowError(message);
                return;
            }

            // 只在读取成功后切换活动槽，避免损坏存档劫持后续“继续”与自动保存。
            SaveSlotStore.Current.ActiveSlot = slot;
            GameSession.Current.PlayerLingyu = meta.lingyu;
            if (!string.IsNullOrWhiteSpace(saveData.activeDungeonRunPayload))
            {
                if (DungeonRunSessionStore.TryRestore(
                        saveData.activeDungeonRunPayload, out string runError,
                        sideEventContexts: plan => WanjieScroll.Progression.DungeonSideEventProductionResolver.CreateRestoreContext(plan, meta)))
                {
                    LevelRunSession floor = LevelRunSessionStore.Current;
                    if (floor != null && floor.ActiveEncounter.HasValue)
                        SceneLoader.GoToCombat();
                    else
                        SceneLoader.GoToLevelMap();
                    return;
                }

                Debug.LogWarning(
                    $"[MainMenu] 活动 Run 无法恢复，将清除损坏的 Run 载荷：{runError}");
                DungeonRunSessionStore.Clear();
                service.Save(meta, string.Empty);
            }

            if (TutorialProgressionGate.IsCompleted(meta))
                SceneLoader.GoToHub();
            else
                SceneLoader.GoToOpeningCG();
        }

        private void DeleteSlot(int slot)
        {
            if (_isNavigating || slot == 0)
                return;

            SaveSlotStore.Current.GetSlotService(slot).Delete();
            _hasSavedProgress = SaveSlotStore.Current.HasAnySave();
            if (!_hasSavedProgress)
            {
                if (_savePanel != null)
                {
                    Destroy(_savePanel.gameObject);
                    _savePanel = null;
                }
                CreateMenu();
                CloseModal();
                _primaryButton?.Select();
                return;
            }

            RefreshSavePanel();
        }

        private void OnExitClicked()
        {
            if (!_isNavigating)
                Application.Quit();
        }

        private void SetBusyState(bool busy)
        {
            SetMenuInteractivity(!busy);
        }

        private void SetMenuInteractivity(bool interactable)
        {
            for (int i = 0; i < _menuButtons.Count; i++)
            {
                if (_menuButtons[i] != null)
                    _menuButtons[i].interactable = interactable;
            }
        }

        private readonly struct MenuEntry
        {
            public MenuEntry(string objectName, string label, Action callback)
            {
                ObjectName = objectName;
                Label = label;
                Callback = callback;
            }

            public string ObjectName { get; }
            public string Label { get; }
            public Action Callback { get; }
        }
    }
}
