using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.UI.Components;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Controllers
{
    /// <summary>
    /// Owns the opening media timeline, its pause/resume boundary and completion.
    /// The shared menu owns input/occlusion; only this owner controls video/audio.
    /// Disabling retains the paused segment; scene exit releases it permanently.
    /// </summary>
    public class OpeningCGController : SceneBootstrapBase
    {
        private const string VideoPath = "Video/opening-cg";
        private const string VoicePath = "Audio/Voice/ScrollSpirit/scroll-spirit-opening-cg-end-v1";
        private static readonly Color InkColor = new Color(0.035f, 0.105f, 0.13f, 0.98f);

        private VideoPlayer _videoPlayer;
        private RenderTexture _videoTarget;
        private RawImage _videoImage;
        private AudioSource _voiceSource;
        private Button _readyButton;
        private bool _videoEnded;
        private CombatPauseMenu _pauseMenu;
        private Coroutine _voiceWait;
        private float _voiceWaitRemaining;
        private bool _voiceRunning;
        private bool _voicePending;
        private bool _entered;
        private bool _active;
        private bool _exiting;
        private bool _paused;

        [SerializeField] private VideoClip _videoClip;
        [SerializeField] private AudioClip _voiceClip;

        protected override void OnSceneEnter()
        {
            _entered = _active = true;
            EnsureCamera(transform);
            CreateVideoSurface();
            _pauseMenu = CombatPauseMenu.CreateForCinematic(transform);
            _pauseMenu.OpenChanged += SetPresentationPaused;
            _pauseMenu.SkipRequested += SkipToButton;
            _pauseMenu.QuitRequested += QuitOpening;
            PrepareVideo();
        }

        protected override void OnSceneExit()
        {
            _exiting = true;
            _active = false;
            if (_pauseMenu != null)
            {
                _pauseMenu.OpenChanged -= SetPresentationPaused;
                _pauseMenu.SkipRequested -= SkipToButton;
                _pauseMenu.QuitRequested -= QuitOpening;
                _pauseMenu.Close();
            }
            StopVoiceWait();
            _voicePending = _voiceRunning = false;
            if (_voiceSource != null) _voiceSource.Stop();
            if (_videoPlayer != null)
            {
                UnsubscribeVideo();
                _videoPlayer.Stop();
                _videoPlayer.targetTexture = null;
            }

            if (_videoImage != null) _videoImage.texture = null;
            if (_videoTarget != null)
            {
                _videoTarget.Release();
                Destroy(_videoTarget);
                _videoTarget = null;
            }
        }

        private void CreateVideoSurface()
        {
            _videoImage = new GameObject("CGVideo", typeof(RawImage), typeof(AspectRatioFitter))
                .GetComponent<RawImage>();
            _videoImage.transform.SetParent(Canvas.transform, false);
            _videoImage.color = Color.black;
            _videoImage.raycastTarget = false;

            RectTransform rt = _videoImage.rectTransform;
            UIElementFactory.Stretch(rt);

            var fitter = _videoImage.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;
        }

        private void PrepareVideo()
        {
            VideoClip clip = _videoClip != null
                ? _videoClip
                : Resources.Load<VideoClip>(VideoPath);

            if (clip == null)
            {
                Debug.LogError($"[OpeningCG] 未找到 CG 视频 Resources/{VideoPath}，跳过直接显示按钮。");
                OnVideoFinished(null);
                return;
            }

            _videoTarget = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
            {
                name = "OpeningCG_RT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            _videoTarget.Create();

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.isLooping = false;
            _videoPlayer.skipOnDrop = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoTarget;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = clip;
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.loopPointReached += OnVideoFinished;
            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.Prepare();
        }

        private void OnVideoPrepared(VideoPlayer source)
        {
            if (!_active || _exiting || _videoEnded || source != _videoPlayer) return;
            if (_videoImage != null && _videoTarget != null)
            {
                _videoImage.texture = _videoTarget;
                _videoImage.color = Color.white;
            }
            if (!_paused) source.Play();
        }

        private void OnVideoFinished(VideoPlayer source)
        {
            if (!_active || _exiting || _videoEnded ||
                (source != null && source != _videoPlayer)) return;
            _videoEnded = true;
            _voicePending = true;
            if (!_paused) BeginClosingVoice();
        }

        private void BeginClosingVoice()
        {
            if (!_active || _exiting || !_voicePending) return;
            _voicePending = false;
            AudioClip clip = _voiceClip != null
                ? _voiceClip
                : Resources.Load<AudioClip>(VoicePath);

            if (clip != null)
            {
                if (_voiceSource == null)
                    _voiceSource = gameObject.AddComponent<AudioSource>();
                _voiceSource.clip = clip;
                _voiceSource.playOnAwake = false;
                _voiceWaitRemaining = clip.length + 0.5f;
                _voiceRunning = true;
                _voiceSource.Play();
                _voiceWait = StartCoroutine(WaitForClosingVoice());
                return;
            }

            ShowReadyButton();
        }

        private IEnumerator WaitForClosingVoice()
        {
            while (_voiceWaitRemaining > 0f)
            {
                yield return null;
                if (!_paused) _voiceWaitRemaining -= Time.unscaledDeltaTime;
            }
            _voiceWait = null;
            _voiceRunning = false;
            if (_active && !_exiting) ShowReadyButton();
        }

        private void StopVoiceWait()
        {
            if (_voiceWait == null) return;
            StopCoroutine(_voiceWait);
            _voiceWait = null;
        }

        private void ShowReadyButton()
        {
            if (_readyButton != null) return;

            Sprite buttonSprite = Resources.Load<Sprite>(
                "Sprites/UI/Menu/Buttons/JadeGlass/JadeGlass_Normal");

            _readyButton = UIElementFactory.CreateButton("ReadyButton", Canvas.transform, buttonSprite);
            RectTransform rt = _readyButton.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.12f);
            rt.anchorMax = new Vector2(0.5f, 0.12f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(338f, 120f);
            rt.anchoredPosition = Vector2.zero;

            if (buttonSprite != null)
            {
                _readyButton.image.preserveAspect = true;
                _readyButton.transition = Selectable.Transition.SpriteSwap;
                SpriteState state = _readyButton.spriteState;
                state.highlightedSprite = Resources.Load<Sprite>(
                    "Sprites/UI/Menu/Buttons/JadeGlass/JadeGlass_Highlighted");
                state.pressedSprite = Resources.Load<Sprite>(
                    "Sprites/UI/Menu/Buttons/JadeGlass/JadeGlass_Pressed");
                state.selectedSprite = state.highlightedSprite;
                state.disabledSprite = Resources.Load<Sprite>(
                    "Sprites/UI/Menu/Buttons/JadeGlass/JadeGlass_Disabled");
                _readyButton.spriteState = state;
            }
            else
            {
                _readyButton.image.color = new Color(0.67f, 0.82f, 0.78f, 0.92f);
            }

            Text label = UIElementFactory.CreateText(
                "Label", _readyButton.transform,
                "我准备好了", 30, InkColor);
            UIElementFactory.Stretch(label.rectTransform);
            label.fontStyle = FontStyle.Bold;

            _readyButton.onClick.AddListener(OnReadyClicked);
            _readyButton.Select();

            var hover = _readyButton.gameObject.AddComponent<HoverScaleEffect>();
            hover.HoverScale = 1.025f;
        }

        private void OnReadyClicked()
        {
            if (!_active || _exiting || (_pauseMenu != null && _pauseMenu.IsOpen)) return;
            _exiting = true;
            if (_readyButton != null) _readyButton.interactable = false;
            GameSession.Current.StartNewRun();
            // 旧 Run 的会话是内存静态数据，删存档不会清掉；新开局必须重建。
            LevelRunSessionStore.BeginConfigured(
                TutorialProgressionGate.LevelContentId);
            var meta = new MetaProgression
            {
                lingyu = GameSession.Current.PlayerLingyu
            };
            GameSaveService.CreateDefault().Save(
                meta,
                DungeonRunSessionStore.Current.Serialize());
            SceneLoader.GoToLevelMap();
        }

        private void Update()
        {
            if (!_active || _exiting || (_pauseMenu != null && _pauseMenu.IsOpen)) return;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                SkipToButton();
        }

        private void SkipToButton()
        {
            if (!_active || _exiting) return;
            // Skip is a single terminal choice for this presentation, not a
            // fabricated video callback. It never starts a Run or writes a save.
            _videoEnded = true;
            _voicePending = _voiceRunning = false;
            UnsubscribeVideo();
            if (_videoPlayer != null) _videoPlayer.Stop();
            StopVoiceWait();
            if (_voiceSource != null) _voiceSource.Stop();
            if (_pauseMenu != null) _pauseMenu.Close();
            ShowReadyButton();
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            if (!_active || _exiting || _videoEnded || source != _videoPlayer) return;
            Debug.LogError($"[OpeningCG] 视频播放失败：{message}");
            OnVideoFinished(source);
        }

        private void SetPresentationPaused(bool paused)
        {
            _paused = paused;
            if (!_active || _exiting) return;
            if (paused)
            {
                if (_videoPlayer != null && _videoPlayer.isPlaying) _videoPlayer.Pause();
                if (_voiceRunning && _voiceSource != null) _voiceSource.Pause();
                return;
            }
            if (!_videoEnded && _videoPlayer != null && _videoPlayer.isPrepared)
                _videoPlayer.Play();
            if (_voicePending) BeginClosingVoice();
            else if (_voiceRunning && _voiceSource != null) _voiceSource.UnPause();
        }

        private void QuitOpening()
        {
            if (_exiting) return;
            OnSceneExit();
            CombatPauseMenu.RequestApplicationQuit();
        }

        private void UnsubscribeVideo()
        {
            if (_videoPlayer == null) return;
            _videoPlayer.prepareCompleted -= OnVideoPrepared;
            _videoPlayer.loopPointReached -= OnVideoFinished;
            _videoPlayer.errorReceived -= OnVideoError;
        }

        private void OnDisable()
        {
            if (!_entered) return;
            _active = false;
            UnsubscribeVideo();
            if (_videoPlayer != null)
            {
                if (_videoPlayer.isPrepared) _videoPlayer.Pause();
                else _videoPlayer.Stop();
            }
            if (_voiceRunning && _voiceSource != null) _voiceSource.Pause();
            StopVoiceWait();
            if (_pauseMenu != null)
            {
                _pauseMenu.Close();
                _pauseMenu.enabled = false;
            }
            if (_videoImage != null) _videoImage.enabled = false;
            if (_readyButton != null) _readyButton.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (!_entered || _exiting) return;
            _active = true;
            _paused = false;
            if (_pauseMenu != null) _pauseMenu.enabled = true;
            if (_videoImage != null) _videoImage.enabled = true;
            if (_readyButton != null) _readyButton.gameObject.SetActive(true);
            if (!_videoEnded && _videoPlayer != null)
            {
                _videoPlayer.prepareCompleted += OnVideoPrepared;
                _videoPlayer.loopPointReached += OnVideoFinished;
                _videoPlayer.errorReceived += OnVideoError;
                if (_videoPlayer.isPrepared) OnVideoPrepared(_videoPlayer);
                else _videoPlayer.Prepare();
            }
            if (_voicePending) BeginClosingVoice();
            else if (_voiceRunning)
            {
                if (_voiceSource != null) _voiceSource.UnPause();
                _voiceWait = StartCoroutine(WaitForClosingVoice());
            }
        }
    }
}
