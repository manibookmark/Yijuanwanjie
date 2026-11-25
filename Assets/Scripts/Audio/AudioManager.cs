using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Audio
{
    /// <summary>
    /// 音频管理器（单例）
    /// 管理音乐播放/切换、音效触发、连击鼓点系统
    /// 可挂载到场景，也可 headless 测试（无 AudioClip 时不报错）
    ///
    /// 规模审查：本存量音频门面已超过文件阈值。BOSS 号角/战曲的
    /// 请求、解码与就绪状态已下沉到 BossPresentationAudioWarmup；
    /// 本类只保留音源、混音策略和窄的 Begin/PlayPrepared 组合入口。
    /// 密集命中限频与专用音源生命周期由 ImpactBurstAudioPlayer 独占。
    /// 场景间监听空档由 SceneAudioListenerContinuity 独占，本类只在已有
    /// 单例创建时装配，不持有第二套监听或场景切换状态。
    /// 音乐音源淡变由 AudioSourceFadeOwner 按音源独占，本类保留选曲与混音规则。
    /// </summary>
    public class AudioManager : MonoBehaviour, IAudioSettingsRuntime
    {
        private const float VoiceWarmupTimeoutSeconds = 8f;
        private static readonly ProfilerMarker VoiceWarmupRequestMarker =
            new ProfilerMarker("Audio.VoiceWarmup.Request");
        private static readonly ProfilerMarker VoiceWarmupCompleteMarker =
            new ProfilerMarker("Audio.VoiceWarmup.Complete");
        public static AudioManager Instance { get; private set; }

        [Header("配置")]
        [Tooltip("音频配置资产")]
        public AudioConfig config;

        [Header("音乐 Source")]
        [Tooltip("主音乐 AudioSource（场景音乐）")]
        public AudioSource musicSourceA;
        [Tooltip("副音乐 AudioSource（交叉淡入淡出用）")]
        public AudioSource musicSourceB;

        [Header("音效 Source")]
        [Tooltip("音效 AudioSource 池大小")]
        public int sfxSourcePoolSize = 8;

        private AudioSource[] _sfxSources;
        private SfxType?[] _sfxSourceTypes;
        private float[] _sfxSourceContentTrims;
        private int _sfxIndex = 0;
        private ImpactBurstAudioPlayer _impactBurstAudio;
        private AudioSourceFadeOwner _musicFades;
        private AudioConfig _defaultConfig;
        private bool _defaultConfigLoaded;
        private AudioClip _buttonClickFallback;
        private bool _buttonClickFallbackLoaded;
        private AudioPlayerSettings _playerSettings =
            AudioPlayerSettings.Recommended;

        private AudioSource _activeMusicSource;
        private AudioSource _inactiveMusicSource;
        private AudioClip _requestedMusicClip;
        private Coroutine _musicLoadRoutine;

        private MusicScene _currentMusicScene = MusicScene.Hall;
        private bool _comboClimaxActive = false;
        private int _comboCount = 0;

        // 语音通道（角色演出台词）：独占单源，新台词自动顶掉旧台词
        private AudioSource _voiceSource;
        private string _currentVoiceCue;
        private readonly Dictionary<string, AudioClip> _voiceClipCache =
            new Dictionary<string, AudioClip>();
        private readonly HashSet<string> _voiceWarmupsInFlight =
            new HashSet<string>();
        private readonly HashSet<string> _voiceWarmupFailures =
            new HashSet<string>();
        private readonly BossPresentationAudioWarmup
            _bossPresentationAudioWarmup =
                new BossPresentationAudioWarmup();
        private bool _voiceEnabled = true;

        // 连击鼓点
        private float _comboDrumTimer = 0f;
        private float _comboDrumInterval = 0.5f;
        private bool _drumActive = false;

        // 日志（headless 测试用）
        public event Action<string> OnLog;

        // ====== 事件钩子状态 ======
        public MusicScene CurrentMusicScene => _currentMusicScene;
        public bool ComboClimaxActive => _comboClimaxActive;
        public int ComboCount => _comboCount;
        public bool IsHeadless => _sfxSources == null || _sfxSources.Length == 0;
        public AudioPlayerSettings PlayerSettings => _playerSettings;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeAudioSources();
            _musicFades = GetComponent<AudioSourceFadeOwner>();
            if (_musicFades == null) _musicFades = gameObject.AddComponent<AudioSourceFadeOwner>();
            ApplyVolumeConfig(ResolveAudioConfig());
            AudioSettingsRuntime.Register(this);
            SceneAudioListenerContinuity.Ensure(this);
        }

        private void InitializeAudioSources()
        {
            // 音乐 Source
            if (musicSourceA == null)
            {
                musicSourceA = gameObject.AddComponent<AudioSource>();
                musicSourceA.volume = 0f;
            }
            if (musicSourceB == null)
            {
                musicSourceB = gameObject.AddComponent<AudioSource>();
                musicSourceB.volume = 0f;
            }
            musicSourceA.playOnAwake = false;
            musicSourceA.loop = true;
            musicSourceA.spatialBlend = 0f;
            musicSourceA.ignoreListenerPause = true;
            musicSourceB.playOnAwake = false;
            musicSourceB.loop = true;
            musicSourceB.spatialBlend = 0f;
            musicSourceB.ignoreListenerPause = true;

            _activeMusicSource = musicSourceA;
            _inactiveMusicSource = musicSourceB;

            // 音效 Source 池
            _sfxSources = new AudioSource[sfxSourcePoolSize];
            _sfxSourceTypes = new SfxType?[sfxSourcePoolSize];
            _sfxSourceContentTrims = new float[sfxSourcePoolSize];
            for (int i = 0; i < sfxSourcePoolSize; i++)
            {
                _sfxSources[i] = gameObject.AddComponent<AudioSource>();
                _sfxSources[i].playOnAwake = false;
                _sfxSources[i].spatialBlend = 0f;
                _sfxSourceContentTrims[i] = 1f;
            }

            // 语音 Source（单通道独占；台词切换时直接 Stop 旧语音）
            _voiceSource = gameObject.AddComponent<AudioSource>();
            _voiceSource.playOnAwake = false;
            _voiceSource.loop = false;
            _voiceSource.spatialBlend = 0f;
            _voiceSource.ignoreListenerPause = true;
        }

        // ================================================================
        //  音乐系统
        // ================================================================

        /// <summary>
        /// 播放场景音乐（带交叉淡入淡出）
        /// </summary>
        public void PlayMusic(MusicScene scene)
        {
            AudioConfig resolvedConfig = ResolveAudioConfig();
            AudioClip clip = ResolveMusicClip(scene, resolvedConfig);

            // 首次播放同一场景也要播（初始状态恰好是 Hall 时不应该跳过）
            bool alreadyPlayingThisScene = scene == _currentMusicScene &&
                _activeMusicSource != null &&
                _activeMusicSource.clip == clip &&
                _activeMusicSource.isPlaying;
            bool alreadyLoadingThisScene = scene == _currentMusicScene &&
                                           _requestedMusicClip == clip &&
                                           _musicLoadRoutine != null;
            if ((alreadyPlayingThisScene || alreadyLoadingThisScene) &&
                _comboClimaxActive == false)
                return;

            Log($"♪ 切换音乐 → {scene}" + (clip != null ? $" ({clip.name})" : " (无素材)"));

            _currentMusicScene = scene;
            _comboClimaxActive = false;
            CancelPendingMusicLoad();
            _requestedMusicClip = clip;

            if (clip == null ||
                _activeMusicSource == null ||
                _inactiveMusicSource == null)
                return;

            float targetVolume = AudioMixPolicy.GetMusicGain(
                scene,
                resolvedConfig,
                _playerSettings.Music);
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                if (clip.loadState == AudioDataLoadState.Failed)
                {
                    Debug.LogWarning(
                        $"[AudioManager] Music clip '{clip.name}' failed to load.",
                        this);
                    return;
                }

                _musicLoadRoutine = StartCoroutine(
                    PlayMusicWhenLoaded(scene, clip, targetVolume));
                return;
            }

            BeginMusicCrossfade(clip, targetVolume);
        }

        private void BeginMusicCrossfade(AudioClip clip, float targetVolume)
        {
            _musicFades.Cancel(_activeMusicSource);
            _musicFades.Cancel(_inactiveMusicSource);
            _inactiveMusicSource.clip = clip;
            _inactiveMusicSource.volume = 0f;
            _inactiveMusicSource.mute = !_musicEnabled;
            _inactiveMusicSource.Play();

            _musicFades.FadeTo(_activeMusicSource, 0f, 1f, stopWhenSilent: true);
            _musicFades.FadeTo(_inactiveMusicSource, targetVolume, 1f);

            // 交换
            var temp = _activeMusicSource;
            _activeMusicSource = _inactiveMusicSource;
            _inactiveMusicSource = temp;
            if (_activeMusicSource != null)
                _activeMusicSource.mute = !_musicEnabled;
        }

        /// <summary>
        /// Ensures that the requested baseline track is either playing or has an
        /// accepted asynchronous load request. Scene lifecycle code calls this
        /// intent-level API and never inspects sources, clips, configuration
        /// loading, or crossfade state.
        /// </summary>
        public bool EnsureMusic(MusicScene scene)
        {
            AudioConfig resolvedConfig = ResolveAudioConfig();
            AudioClip clip = ResolveMusicClip(scene, resolvedConfig);
            if (clip == null ||
                _activeMusicSource == null ||
                _inactiveMusicSource == null)
            {
                return false;
            }

            bool alreadyPlaying = _currentMusicScene == scene &&
                                   _activeMusicSource.clip == clip &&
                                   _activeMusicSource.isPlaying;
            bool alreadyLoading = _currentMusicScene == scene &&
                                  _requestedMusicClip == clip &&
                                  _musicLoadRoutine != null;
            if (!alreadyPlaying && !alreadyLoading)
                PlayMusic(scene);

            if (_activeMusicSource != null)
                _activeMusicSource.mute = !_musicEnabled;
            return (_activeMusicSource != null &&
                    _activeMusicSource.clip == clip &&
                    _activeMusicSource.isPlaying) ||
                   (_currentMusicScene == scene &&
                    _requestedMusicClip == clip &&
                    _musicLoadRoutine != null);
        }

        /// <summary>
        /// 停止所有音乐
        /// </summary>
        public void StopMusic()
        {
            Log("♪ 停止音乐");
            CancelPendingMusicLoad();
            _musicFades?.CancelAll();
            _requestedMusicClip = null;
            if (_activeMusicSource != null) _activeMusicSource.Stop();
            if (_inactiveMusicSource != null) _inactiveMusicSource.Stop();
        }

        private IEnumerator PlayMusicWhenLoaded(
            MusicScene scene,
            AudioClip clip,
            float targetVolume)
        {
            const float timeoutSeconds = 10f;
            float startedAt = Time.realtimeSinceStartup;
            while (clip != null &&
                   clip.loadState != AudioDataLoadState.Loaded &&
                   clip.loadState != AudioDataLoadState.Failed &&
                   Time.realtimeSinceStartup - startedAt < timeoutSeconds)
            {
                if (_currentMusicScene != scene || _requestedMusicClip != clip)
                {
                    _musicLoadRoutine = null;
                    yield break;
                }

                if (clip.loadState == AudioDataLoadState.Unloaded)
                    clip.LoadAudioData();
                yield return null;
            }

            _musicLoadRoutine = null;
            if (clip != null &&
                clip.loadState == AudioDataLoadState.Loaded &&
                _currentMusicScene == scene &&
                _requestedMusicClip == clip)
            {
                BeginMusicCrossfade(clip, targetVolume);
                yield break;
            }

            Debug.LogWarning(
                $"[AudioManager] Music clip '{clip?.name ?? "<null>"}' " +
                "did not become ready for playback.",
                this);
        }

        private void CancelPendingMusicLoad()
        {
            if (_musicLoadRoutine == null)
                return;
            StopCoroutine(_musicLoadRoutine);
            _musicLoadRoutine = null;
        }

        // ================================================================
        //  音效系统
        // ================================================================

        /// <summary>
        /// 播放音效
        /// </summary>
        public void PlaySfx(SfxType type)
        {
            AudioClip clip = ResolveSfxClip(type);
            string clipLabel = clip != null ? $" ({clip.name})" : " (无素材)";

            if (clip == null || IsHeadless)
            {
                Log($"♫ 音效: {type}{clipLabel}");
                return;
            }

            float pitch = GetSfxPitch(type);

            bool played = PlayTypedOneShot(clip, type, pitch, 1f);
            Log(played
                ? $"♫ 音效: {type}{clipLabel}"
                : $"♫ 音效静默: {type}（出牌、命中或升级音效正在播放）");
        }

        private AudioClip ResolveSfxClip(SfxType type)
        {
            AudioConfig resolvedConfig = ResolveAudioConfig();
            AudioClip clip = resolvedConfig != null
                ? resolvedConfig.GetSfxClip(type)
                : null;
            if (clip != null)
                return clip;

            // 兼容仍引用旧内嵌 AudioConfig 的场景：保留场景音量与其它覆盖，
            // 仅从正式默认配置补上新增且缺失的音效槽位。
            AudioConfig defaultConfig = GetDefaultAudioConfig();
            clip = defaultConfig != null && defaultConfig != resolvedConfig
                ? defaultConfig.GetSfxClip(type)
                : null;
            if (clip != null || type != SfxType.ButtonClick)
                return clip;

            if (!_buttonClickFallbackLoaded)
            {
                _buttonClickFallback = Resources.Load<AudioClip>(
                    "Audio/SFX/ui-click-01");
                _buttonClickFallbackLoaded = true;
            }
            return _buttonClickFallback;
        }

        private AudioConfig ResolveAudioConfig()
        {
            if (config != null)
                return config;

            AudioConfig defaultConfig = GetDefaultAudioConfig();
            if (defaultConfig != null)
                config = defaultConfig;
            return config;
        }

        private AudioConfig GetDefaultAudioConfig()
        {
            if (!_defaultConfigLoaded)
            {
                _defaultConfig = Resources.Load<AudioConfig>(
                    AudioConfig.DefaultResourcePath);
                _defaultConfigLoaded = true;
            }

            return _defaultConfig;
        }

        private AudioClip ResolveMusicClip(
            MusicScene scene,
            AudioConfig resolvedConfig)
        {
            AudioClip clip = resolvedConfig != null
                ? resolvedConfig.GetMusicClip(scene)
                : null;
            if (clip != null)
                return clip;

            AudioConfig defaultConfig = GetDefaultAudioConfig();
            return defaultConfig != null && defaultConfig != resolvedConfig
                ? defaultConfig.GetMusicClip(scene)
                : null;
        }

        /// <summary>
        /// 使用常驻池播放任意一次性片段。解绑战斗事件不会停止已开始的片段，
        /// 因而战斗场景卸载后尾音仍可自然结束。
        /// </summary>
        public void PlayOneShot(
            AudioClip clip,
            SfxType type,
            float pitch = 1f,
            float contentTrim = 1f)
        {
            PlayTypedOneShot(clip, type, pitch, contentTrim);
        }

        /// <summary>Compatibility entry for resident attacks. Admission and
        /// source ownership remain with the shared dense-impact player.</summary>
        public void PlayPeriodicAttackHit(AudioClip clip, float pitch = 1f, float contentTrim = 1f)
            => PlayDenseImpactHit(clip, pitch, contentTrim);

        /// <summary>Dense projectile contacts use exclusive bounded voices;
        /// ordinary effects, music and voice playback retain their own pools.</summary>
        public void PlayDenseImpactHit(AudioClip clip, float pitch = 1f, float contentTrim = 1f)
        {
            if (clip == null || IsHeadless || !isActiveAndEnabled || AudioListener.pause) return;
            if (_impactBurstAudio == null) _impactBurstAudio = new ImpactBurstAudioPlayer(transform);
            if (_impactBurstAudio.TryPlay(clip, ResolveAudioConfig(), _playerSettings.Sfx, pitch, contentTrim))
                StopCardHoverSfx();
        }

        /// <summary>
        /// CardHover 仅让位于出牌、命中、升级与钤印反馈；其它 SFX 不参与该规则。
        /// </summary>
        private bool PlayTypedOneShot(
            AudioClip clip,
            SfxType type,
            float pitch,
            float contentTrim)
        {
            if (clip == null || IsHeadless)
                return false;

            if (type == SfxType.CardHover)
            {
                if (IsCardHoverBlocked())
                    return false;
            }
            else if (BlocksCardHover(type))
            {
                StopCardHoverSfx();
            }

            AudioConfig resolvedConfig = ResolveAudioConfig();
            float volume = AudioMixPolicy.GetSfxGain(
                type,
                resolvedConfig,
                _playerSettings.Sfx,
                contentTrim);
            return PlayOnNextSfxSource(
                clip,
                volume,
                pitch,
                type,
                contentTrim) != null;
        }

        private AudioSource PlayOnNextSfxSource(
            AudioClip clip,
            float volume,
            float pitch,
            SfxType? type,
            float contentTrim)
        {
            if (clip == null || IsHeadless) return null;

            int sourceIndex = _sfxIndex;
            AudioSource source = _sfxSources[sourceIndex];
            _sfxIndex = (_sfxIndex + 1) % _sfxSources.Length;
            _sfxSourceTypes[sourceIndex] = type;
            _sfxSourceContentTrims[sourceIndex] = Mathf.Max(
                0f,
                contentTrim);
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, -3f, 3f);
            source.loop = false;
            source.Play();
            return source;
        }

        private bool IsCardHoverBlocked()
        {
            if (_impactBurstAudio != null && _impactBurstAudio.IsPlaying) return true;
            if (_sfxSources == null || _sfxSourceTypes == null)
                return false;

            for (int i = 0; i < _sfxSources.Length; i++)
            {
                AudioSource source = _sfxSources[i];
                SfxType? activeType = _sfxSourceTypes[i];
                if (source != null &&
                    source.isPlaying &&
                    activeType.HasValue &&
                    BlocksCardHover(activeType.Value))
                {
                    return true;
                }
            }

            return false;
        }

        private void StopCardHoverSfx()
        {
            if (_sfxSources == null || _sfxSourceTypes == null)
                return;

            for (int i = 0; i < _sfxSources.Length; i++)
            {
                if (_sfxSourceTypes[i] != SfxType.CardHover)
                    continue;

                AudioSource source = _sfxSources[i];
                if (source != null)
                {
                    source.Stop();
                    source.clip = null;
                }

                _sfxSourceTypes[i] = null;
                if (_sfxSourceContentTrims != null &&
                    i < _sfxSourceContentTrims.Length)
                {
                    _sfxSourceContentTrims[i] = 1f;
                }
            }
        }

        private static bool BlocksCardHover(SfxType type)
        {
            return type == SfxType.CardPlay ||
                   type == SfxType.ProjectileHit ||
                   type == SfxType.LevelUp ||
                   type == SfxType.SealStamp;
        }

        // ================================================================
        //  语音系统（角色演出台词，独立音量总线）
        // ================================================================

        /// <summary>当前正在播放的语音 cue；无语音时为 null。</summary>
        public string CurrentVoiceCue => _currentVoiceCue;

        /// <summary>
        /// 播放语音 cue（按 VoiceCueResolver 命名约定解析素材）。
        /// 台词切换规则：新语音开始前立即停止上一条；cue 未注册或文件缺失时
        /// 仅记录日志并保持安静，不影响字幕。非 voice. 前缀的 cue 一律忽略。
        /// 返回是否解析到了可播放素材。
        /// </summary>
        public bool PlayVoiceCue(string cueId)
        {
            if (!TryResolveVoiceCueIdentity(
                    cueId,
                    out string normalized,
                    out _))
                return false;

            AudioClip clip = ResolveVoiceClip(normalized);
            if (clip == null)
            {
                Log($"🎙 语音 cue 未注册或文件缺失: {normalized}");
                return false;
            }

            return PlayResolvedVoiceCue(normalized, clip);
        }

        /// <summary>
        /// Starts a low-priority, idempotent voice preparation owned by the
        /// persistent audio cache. This is safe to call while another
        /// presentation is running; no AudioSource is touched until playback.
        /// </summary>
        public bool BeginVoiceCueWarmup(string cueId)
        {
            if (!TryResolveVoiceCueIdentity(
                    cueId,
                    out string normalized,
                    out string resourcePath))
            {
                return false;
            }

            if (_voiceClipCache.TryGetValue(
                    normalized,
                    out AudioClip cached) &&
                cached != null &&
                cached.loadState == AudioDataLoadState.Loaded)
            {
                return true;
            }
            if (_voiceWarmupsInFlight.Contains(normalized))
                return true;
            if (_voiceWarmupFailures.Contains(normalized) ||
                !isActiveAndEnabled)
            {
                return false;
            }

            _voiceWarmupsInFlight.Add(normalized);
            StartCoroutine(WarmVoiceCueAsync(
                normalized,
                resourcePath,
                cached));
            return true;
        }

        /// <summary>
        /// Plays only a fully prepared cache entry. Boss presentation uses this
        /// bounded path so a missed warmup becomes silence plus subtitles,
        /// never a synchronous Resources.Load on the reveal frame.
        /// </summary>
        public bool PlayPreparedVoiceCue(string cueId)
        {
            if (!TryResolveVoiceCueIdentity(
                    cueId,
                    out string normalized,
                    out _) ||
                !_voiceClipCache.TryGetValue(
                    normalized,
                    out AudioClip clip) ||
                clip == null ||
                clip.loadState != AudioDataLoadState.Loaded)
            {
                return false;
            }

            return PlayResolvedVoiceCue(normalized, clip);
        }

        public bool IsVoiceCuePrepared(string cueId)
        {
            return TryResolveVoiceCueIdentity(
                       cueId,
                       out string normalized,
                       out _) &&
                   _voiceClipCache.TryGetValue(
                       normalized,
                       out AudioClip clip) &&
                   clip != null &&
                   clip.loadState == AudioDataLoadState.Loaded;
        }

        /// <summary>
        /// Prepares every audio asset consumed by the Boss reveal: horn, music
        /// override and optional entrance voice. The two presentation calls
        /// below are cache-only and therefore cannot fall through to a
        /// synchronous Resources.Load at the reveal boundary.
        /// </summary>
        public bool BeginBossPresentationAudioWarmup(string voiceCueId)
        {
            if (!string.IsNullOrWhiteSpace(voiceCueId))
                BeginVoiceCueWarmup(voiceCueId);

            AudioConfig resolvedConfig = ResolveAudioConfig();
            AudioClip configuredMusic = resolvedConfig != null
                ? (resolvedConfig.bossMusic != null
                    ? resolvedConfig.bossMusic
                    : resolvedConfig.combatMusic)
                : null;
            return _bossPresentationAudioWarmup.Begin(
                this,
                ResolveSfxClip(SfxType.BossAppear),
                configuredMusic);
        }

        public bool IsBossPresentationAudioReady =>
            _bossPresentationAudioWarmup.IsReady;

        public bool PlayPreparedBossAppearSfx()
        {
            if (!_bossPresentationAudioWarmup.TryGetHorn(
                    out AudioClip clip))
            {
                return false;
            }
            if (IsHeadless)
            {
                Log($"♫ 音效: {SfxType.BossAppear} ({clip.name})");
                return true;
            }

            bool played = PlayTypedOneShot(
                clip,
                SfxType.BossAppear,
                GetSfxPitch(SfxType.BossAppear),
                1f);
            Log(played
                ? $"♫ 音效: {SfxType.BossAppear} ({clip.name})"
                : $"♫ 音效静默: {SfxType.BossAppear}");
            return played;
        }

        public bool PlayPreparedBossMusic()
        {
            if (!_bossPresentationAudioWarmup.TryGetMusic(
                    out AudioClip clip) ||
                _activeMusicSource == null ||
                _inactiveMusicSource == null)
            {
                return false;
            }

            if (_currentMusicScene == MusicScene.Boss &&
                _activeMusicSource.clip == clip &&
                _activeMusicSource.isPlaying)
            {
                return true;
            }

            AudioConfig resolvedConfig = ResolveAudioConfig();
            float targetVolume = AudioMixPolicy.GetMusicGain(
                MusicScene.Boss,
                resolvedConfig,
                _playerSettings.Music);
            Log($"♪ 切换音乐 → {MusicScene.Boss} ({clip.name})");
            _currentMusicScene = MusicScene.Boss;
            _comboClimaxActive = false;
            CancelPendingMusicLoad();
            _requestedMusicClip = clip;
            BeginMusicCrossfade(clip, targetVolume);
            return true;
        }

        private bool PlayResolvedVoiceCue(
            string normalized,
            AudioClip clip)
        {
            if (clip == null)
                return false;

            _currentVoiceCue = normalized;
            if (IsHeadless)
            {
                Log($"🎙 语音(headless): {normalized} ({clip.name})");
                return true;
            }

            if (_voiceSource == null)
                return false;

            _voiceSource.Stop();
            _voiceSource.clip = clip;
            _voiceSource.volume = _playerSettings.Voice;
            _voiceSource.mute = !_voiceEnabled;
            _voiceSource.Play();
            Log($"🎙 语音: {normalized} ({clip.name})");
            return true;
        }

        private IEnumerator WarmVoiceCueAsync(
            string normalized,
            string resourcePath,
            AudioClip cached)
        {
            AudioClip clip = cached;
            if (clip == null)
            {
                ResourceRequest request;
                using (VoiceWarmupRequestMarker.Auto())
                {
                    request = Resources.LoadAsync<AudioClip>(resourcePath);
                    request.priority = -1;
                }
                yield return request;
                clip = request.asset as AudioClip;
            }

            if (clip != null &&
                clip.loadState == AudioDataLoadState.Unloaded)
            {
                clip.LoadAudioData();
            }

            float deadline = Time.realtimeSinceStartup +
                             VoiceWarmupTimeoutSeconds;
            while (clip != null &&
                   clip.loadState != AudioDataLoadState.Loaded &&
                   clip.loadState != AudioDataLoadState.Failed &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            using (VoiceWarmupCompleteMarker.Auto())
            {
                _voiceWarmupsInFlight.Remove(normalized);
                if (clip != null &&
                    clip.loadState == AudioDataLoadState.Loaded)
                {
                    _voiceClipCache[normalized] = clip;
                    _voiceWarmupFailures.Remove(normalized);
                    yield break;
                }
                _voiceWarmupFailures.Add(normalized);
            }

            Debug.LogWarning(
                $"[AudioManager] Voice warmup did not become ready: " +
                $"{normalized} ({resourcePath}).",
                this);
        }

        private static bool TryResolveVoiceCueIdentity(
            string cueId,
            out string normalized,
            out string resourcePath)
        {
            normalized = null;
            resourcePath = null;
            if (string.IsNullOrWhiteSpace(cueId))
                return false;
            normalized = cueId.Trim().ToLowerInvariant();
            if (!VoiceCueResolver.TryGetResourcePath(
                    normalized,
                    out resourcePath))
            {
                normalized = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 指定语音 cue 是否仍在播放。cue 不是当前 cue、已自然播完、headless
        /// 或素材缺失时返回 false，调用方不得因此阻塞演出确认。
        /// </summary>
        public bool IsVoiceCuePlaying(string cueId)
        {
            if (string.IsNullOrWhiteSpace(cueId) ||
                string.IsNullOrEmpty(_currentVoiceCue) ||
                _voiceSource == null)
                return false;

            return string.Equals(
                       _currentVoiceCue,
                       cueId.Trim().ToLowerInvariant(),
                       StringComparison.Ordinal) &&
                   _voiceSource.isPlaying;
        }

        /// <summary>停止当前语音。演出跳过、关闭、切场景或被更高优先级演出替换时必须调用，
        /// 语音不得跨场景残留。</summary>
        public void StopVoiceCue()
        {
            if (string.IsNullOrEmpty(_currentVoiceCue))
                return;

            Log($"🎙 停止语音: {_currentVoiceCue}");
            _currentVoiceCue = null;
            if (_voiceSource != null)
            {
                _voiceSource.Stop();
                _voiceSource.clip = null;
            }
        }

        /// <summary>设置语音开关（设置界面用）。关闭时立即静音语音总线。</summary>
        public void SetVoiceEnabled(bool enabled)
        {
            _voiceEnabled = enabled;
            if (_voiceSource != null)
                _voiceSource.mute = !enabled;
            Log($"🎙 语音 {(enabled ? "开启" : "关闭")}");
        }

        /// <summary>设置语音音量（0~1，设置界面用）。</summary>
        public void SetVoiceVolume(float volume01)
        {
            ApplyPlayerSettings(new AudioPlayerSettings(
                _playerSettings.Master,
                _playerSettings.Music,
                _playerSettings.Sfx,
                volume01,
                _playerSettings.Muted));
            Log($"🎙 语音音量: {_playerSettings.Voice:F2}");
        }

        private AudioClip ResolveVoiceClip(string normalizedCueId)
        {
            if (_voiceClipCache.TryGetValue(normalizedCueId, out AudioClip cached))
                return cached;

            AudioClip clip = null;
            if (VoiceCueResolver.TryGetResourcePath(
                    normalizedCueId, out string resourcePath))
            {
                clip = Resources.Load<AudioClip>(resourcePath);
            }

            if (clip != null)
                _voiceClipCache[normalizedCueId] = clip;
            return clip;
        }

        private void OnDisable()
        {
            CancelPendingMusicLoad();
            _musicFades?.CancelAll();
            _impactBurstAudio?.StopAndClear();
        }

        private void OnDestroy()
        {
            _musicFades?.CancelAll();
            _impactBurstAudio?.Dispose();
            _impactBurstAudio = null;
            AudioSettingsRuntime.Unregister(this);
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 获取音效音调（连击递增时音调升高，GDD §13.8.2 上限 1.18）
        /// </summary>
        private float GetSfxPitch(SfxType type)
        {
            if (type == SfxType.ComboIncrement && _comboCount > 0)
            {
                return Mathf.Min(1.18f, 1.0f + _comboCount * 0.018f);
            }
            return 1.0f;
        }

        // ================================================================
        //  连击鼓点系统（GDD 13.1: 鼓点加密随连击数递增）
        // ================================================================

        /// <summary>
        /// 更新连击状态
        /// </summary>
        public void SetComboCount(int count)
        {
            _comboCount = count;

            // 连击 > 10 时激活高潮音乐
            if (count >= 10 && !_comboClimaxActive)
            {
                ActivateComboClimax();
            }
            else if (count < 10 && _comboClimaxActive)
            {
                DeactivateComboClimax();
            }

            // 更新鼓点间隔
            if (config != null)
            {
                float baseInterval = config.comboDrumBaseInterval;
                float minInterval = config.comboDrumMinInterval;
                _comboDrumInterval = Mathf.Max(minInterval, baseInterval - count * 0.04f);
            }
        }

        /// <summary>
        /// 激活连击高潮音乐层
        /// </summary>
        private void ActivateComboClimax()
        {
            _comboClimaxActive = true;
            _drumActive = true;
            _comboDrumTimer = 0f;
            Log($"♪ 连击高潮激活！(连击数={_comboCount})");

            if (IsHeadless) return;

            AudioClip climaxClip = config != null ? config.GetMusicClip(MusicScene.ComboClimax) : null;
            if (climaxClip != null)
            {
                // 在副 Source 上叠加高潮层
                _musicFades.Cancel(_inactiveMusicSource);
                _inactiveMusicSource.clip = climaxClip;
                _inactiveMusicSource.volume = 0f;
                _inactiveMusicSource.loop = true;
                _inactiveMusicSource.Play();
                _musicFades.FadeTo(
                    _inactiveMusicSource,
                    AudioMixPolicy.GetMusicGain(
                        MusicScene.ComboClimax,
                        ResolveAudioConfig(),
                        _playerSettings.Music),
                    0.5f,
                    stopWhenSilent: false);
            }
        }

        /// <summary>
        /// 取消连击高潮
        /// </summary>
        private void DeactivateComboClimax()
        {
            _comboClimaxActive = false;
            _drumActive = false;
            Log("♪ 连击高潮结束");

            if (IsHeadless) return;

            if (_inactiveMusicSource.isPlaying)
            {
                _musicFades.FadeTo(
                    _inactiveMusicSource,
                    0f,
                    0.5f,
                    stopWhenSilent: true,
                    clearClipWhenSilent: true);
            }
        }

        /// <summary>
        /// 重置连击状态（回合结束时调用）
        /// </summary>
        public void ResetCombo()
        {
            if (_comboClimaxActive)
                DeactivateComboClimax();
            _comboCount = 0;
        }

        private void Update()
        {
            // 连击鼓点
            if (_drumActive && !IsHeadless)
            {
                _comboDrumTimer += Time.deltaTime;
                if (_comboDrumTimer >= _comboDrumInterval)
                {
                    _comboDrumTimer = 0f;
                    if (config != null && config.comboDrumSound != null)
                    {
                        PlaySfx(SfxType.ComboIncrement);
                    }
                }
            }
        }

        // ================================================================
        //  事件钩子（订阅 CombatController / ExplorationController 事件）
        // ================================================================

        /// <summary>
        /// 绑定战斗事件
        /// </summary>
        public void BindCombatEvents(CombatController combat)
        {
            this.combat = combat;
            combat.OnCardPlayResolved -= OnCardPlayResolved;
            combat.OnCardPlayResolved += OnCardPlayResolved;
            combat.OnTurnEnd -= OnTurnEnd;
            combat.OnTurnEnd += OnTurnEnd;
            combat.OnEnemyKilled -= OnEnemyKilled;
            combat.OnEnemyKilled += OnEnemyKilled;
            combat.OnLevelUp -= OnLevelUp;
            combat.OnLevelUp += OnLevelUp;

            if (combat.BossTimer != null)
            {
                combat.BossTimer.OnEyesChanged -= OnBossEyesChanged;
                combat.BossTimer.OnEyesChanged += OnBossEyesChanged;
                combat.BossTimer.OnTianjieTriggered -= OnTianjieTriggered;
                combat.BossTimer.OnTianjieTriggered += OnTianjieTriggered;
            }

            Log("✓ 战斗音频事件已绑定");
        }

        /// <summary>
        /// 解绑战斗事件
        /// </summary>
        public void UnbindCombatEvents(CombatController combat)
        {
            combat.OnCardPlayResolved -= OnCardPlayResolved;
            combat.OnTurnEnd -= OnTurnEnd;
            combat.OnEnemyKilled -= OnEnemyKilled;
            combat.OnLevelUp -= OnLevelUp;

            if (combat.BossTimer != null)
            {
                combat.BossTimer.OnEyesChanged -= OnBossEyesChanged;
                combat.BossTimer.OnTianjieTriggered -= OnTianjieTriggered;
            }

            // A previous controller may finish teardown after a later battle
            // has already bound this persistent manager. Release only our owner.
            if (System.Object.ReferenceEquals(this.combat, combat))
                this.combat = null;

            Log("✓ 战斗音频事件已解绑");
        }

        private void OnCardPlayResolved(CombatCardResolvedEvent result)
        {
            // 通用出牌音已移除——专属卡牌音效（VfxProfile.playSfxClip / playSfxPath）已覆盖出牌反馈，
            // 叠加通用翻纸声会导致重复感。按 GDD §13.3.3 专属释放声存在时通用层降为不播。
            if (result.Comboed)
            {
                PlaySfx(SfxType.ComboIncrement);
                SetComboCount(result.ComboCount);
            }

            if (result.HealAmount > 0)
                    PlaySfx(SfxType.LingdanPickup);
                }

        // 抽牌音已移除——与出牌/命中音叠加时重复感强，暂不使用。
        // private void OnCardsDrawn(CombatCardsDrawnEvent result)
        // {
        //     PlaySfx(SfxType.CardDraw);
        // }

        private CombatController combat;

        private void OnTurnEnd()
        {
            ResetCombo();
        }

        private void OnEnemyKilled(EnemyStats enemy)
        {
            PlaySfx(SfxType.EnemyDeath);
            if (enemy.isBoss)
            {
                // Boss 陨落后立即回到地图曲；结算与返回场景不再重复切曲。
                PlayMusic(MusicScene.Dungeon);
            }
        }

        private void OnLevelUp(int level)
        {
            PlaySfx(SfxType.LevelUp);
        }

        private void OnBossEyesChanged()
        {
            // 天劫倒计时音效
            PlaySfx(SfxType.Tianjie);
        }

        private void OnTianjieTriggered()
        {
            PlaySfx(SfxType.Tianjie);
            Log("⚡ 天劫降临音效！");
        }

        /// <summary>
        /// 绑定探索事件
        /// </summary>
        public void BindExplorationEvents(ExplorationController exploration)
        {
            exploration.OnEvent -= OnExplorationEvent;
            exploration.OnEvent += OnExplorationEvent;

            Log("✓ 探索音频事件已绑定");
        }

        /// <summary>
        /// 解绑探索事件
        /// </summary>
        public void UnbindExplorationEvents(ExplorationController exploration)
        {
            exploration.OnEvent -= OnExplorationEvent;
        }

        private void OnExplorationEvent(ExplorationEvent explorationEvent)
        {
            // 普通战斗不切曲：地图遭遇直接沿用地图曲，战斗场景基线也不降级
            // 已在播放的音乐。专属战斗曲只由 Boss 入场演出（CombatSceneBootstrap）
            // 触发，避免快节奏遭遇反复交叉淡入造成听感混乱。
            if (explorationEvent.Type == ExplorationEventType.BossEncountered)
            {
                PlaySfx(SfxType.BossAppear);
            }
            else if (explorationEvent.Type == ExplorationEventType.RoomEntered)
            {
                PlaySfx(SfxType.RoomEnter);
            }
            else if (explorationEvent.Type == ExplorationEventType.MerchantEntered)
            {
                PlaySfx(SfxType.Merchant);
            }
            else if (explorationEvent.Type == ExplorationEventType.PlayerHealed)
            {
                PlaySfx(SfxType.LingdanPickup);
            }
            else if (explorationEvent.Type == ExplorationEventType.TreasureOpened)
            {
                PlaySfx(SfxType.TreasureOpen);
            }
        }

        /// <summary>
        /// 设置主音量
        /// </summary>
        public void SetMasterVolume(float vol)
        {
            ApplyPlayerSettings(new AudioPlayerSettings(
                vol,
                _playerSettings.Music,
                _playerSettings.Sfx,
                _playerSettings.Voice,
                _playerSettings.Muted));
        }

        /// <summary>只恢复玩家混音，不修改 AudioConfig 的产品校准。</summary>
        public void ResetToRecommended()
        {
            ApplyPlayerSettings(AudioPlayerSettings.Recommended);
        }

        // ================================================================
        //  静态便捷方法
        // ================================================================

        public static void PlaySfxSafe(SfxType type)
        {
            EnsureInstance();
            if (Instance != null)
                Instance.PlaySfx(type);
        }

        public static void PlayMusicSafe(MusicScene scene)
        {
            EnsureInstance();
            if (Instance != null)
                Instance.PlayMusic(scene);
        }

        /// <summary>
        /// 播放语音 cue（自动确保 AudioManager 存在）。角色演出协调器等
        /// Bootstrap 装配点使用；空 cue 与非语音 cue 均安全忽略。
        /// </summary>
        public static bool PlayVoiceCueSafe(string cueId)
        {
            if (string.IsNullOrWhiteSpace(cueId))
                return false;
            AudioManager manager = EnsureInstance();
            return manager != null && manager.PlayVoiceCue(cueId);
        }

        /// <summary>停止当前语音（演出结束或中断时由装配点调用）。</summary>
        public static void StopVoiceCueSafe()
        {
            if (Instance != null)
                Instance.StopVoiceCue();
        }

        /// <summary>查询语音 cue 是否仍在播放（演出确认门槛用）；无实例时视为不阻塞。</summary>
        public static bool IsVoiceCuePlayingSafe(string cueId)
        {
            return Instance != null && Instance.IsVoiceCuePlaying(cueId);
        }

        /// <summary>停止所有场景音乐（CG 等需要完全安静的场景调用）。</summary>
        public static void StopMusicSafe()
        {
            EnsureInstance();
            if (Instance != null)
                Instance.StopMusic();
        }

        /// <summary>
        /// 确保 AudioManager 单例存在（跨场景常驻）。菜单/仙域等没有
        /// CombatSceneBootstrap 的场景也能直接播放音乐。
        /// </summary>
        public static AudioManager EnsureInstance()
        {
            if (Instance != null)
                return Instance;

            var existing = FindObjectOfType<AudioManager>();
            if (existing != null)
                return existing;

            var go = new GameObject("AudioManager (Persistent)");
            return go.AddComponent<AudioManager>();
        }

        private void Log(string msg)
        {
            OnLog?.Invoke(msg);
        }

        /// <summary>
        /// 绑定战斗控制器（存储引用供事件回调使用）
        /// </summary>
        public void SetCombatController(CombatController controller)
        {
            combat = controller;
        }

        /// <summary>
        /// 设置音乐开关（设置界面用）。关闭时立即静音音乐总线。
        /// </summary>
        public void SetMusicEnabled(bool enabled)
        {
            _musicEnabled = enabled;
            if (_activeMusicSource != null)
                _activeMusicSource.mute = !enabled;
            if (_inactiveMusicSource != null)
                _inactiveMusicSource.mute = !enabled;
            Log($"♪ 音乐 {(enabled ? "开启" : "关闭")}");
        }

        /// <summary>
        /// 设置音乐音量（0~1，设置界面用）。
        /// </summary>
        public void SetMusicVolume(float volume01)
        {
            ApplyPlayerSettings(new AudioPlayerSettings(
                _playerSettings.Master,
                volume01,
                _playerSettings.Sfx,
                _playerSettings.Voice,
                _playerSettings.Muted));
            Log($"♪ 音乐音量: {_playerSettings.Music:F2}");
        }

        /// <summary>
        /// 设置玩家 SFX 总线音量（0~1）。
        /// </summary>
        public void SetSfxVolume(float volume01)
        {
            ApplyPlayerSettings(new AudioPlayerSettings(
                _playerSettings.Master,
                _playerSettings.Music,
                volume01,
                _playerSettings.Voice,
                _playerSettings.Muted));
            Log($"🔊 音效音量: {_playerSettings.Sfx:F2}");
        }

        /// <summary>
        /// 将一份 AudioConfig 的总线参数立即应用到正在运行的音频源。
        /// Inspector 与设置界面只提交配置，不直接访问各 AudioSource。
        /// </summary>
        public void ApplyVolumeConfig(AudioConfig source)
        {
            if (source == null)
                return;

            config = source;
            ApplyPlayerSettings(_playerSettings);
        }

        /// <summary>
        /// 一次提交完整玩家混音；专用命中播放器只写其独占音源，门面写其余音源与静音。
        /// </summary>
        public void ApplyPlayerSettings(AudioPlayerSettings settings)
        {
            _playerSettings = settings.Normalize();
            AudioConfig source = ResolveAudioConfig();
            AudioListener.volume = _playerSettings.Muted
                ? 0f
                : _playerSettings.Master;

            if (_activeMusicSource != null && _activeMusicSource.isPlaying)
            {
                _musicFades?.Cancel(_activeMusicSource);
                _activeMusicSource.volume = AudioMixPolicy.GetMusicGain(
                    _currentMusicScene,
                    source,
                    _playerSettings.Music);
            }

            if (_comboClimaxActive &&
                _inactiveMusicSource != null &&
                _inactiveMusicSource.isPlaying)
            {
                _musicFades?.Cancel(_inactiveMusicSource);
                _inactiveMusicSource.volume = AudioMixPolicy.GetMusicGain(
                    MusicScene.ComboClimax,
                    source,
                    _playerSettings.Music);
            }

            if (_voiceSource != null)
                _voiceSource.volume = _playerSettings.Voice;

            _impactBurstAudio?.ApplyMix(source, _playerSettings.Sfx);

            if (_sfxSources == null || _sfxSourceTypes == null ||
                _sfxSourceContentTrims == null)
                return;

            int count = Mathf.Min(
                _sfxSources.Length,
                Mathf.Min(
                    _sfxSourceTypes.Length,
                    _sfxSourceContentTrims.Length));
            for (int i = 0; i < count; i++)
            {
                AudioSource sfxSource = _sfxSources[i];
                SfxType? type = _sfxSourceTypes[i];
                if (sfxSource == null || !sfxSource.isPlaying ||
                    !type.HasValue)
                    continue;
                sfxSource.volume = AudioMixPolicy.GetSfxGain(
                    type.Value,
                    source,
                    _playerSettings.Sfx,
                    _sfxSourceContentTrims[i]);
            }
        }

        private bool _musicEnabled = true;
    }
}
