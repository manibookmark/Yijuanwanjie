using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// BOSS 战场序列帧动画组件。把 BossAnimationClip 的图集切成 Sprite 数组，
    /// 逐帧设置同一 SpriteRenderer.sprite——位置、缩放、sortingOrder、伤害预览
    /// 全部沿用敌人渲染管线，BOSS 因此与小兵共用同一套阵型与格子规则。
    /// 动作音频与动画出自同一批视频，时间轴天然对齐，整段同步播放。
    /// Attack 段可由稳定 actionId 选择变体，空 ID 或未知 ID 回退旧 Attack。
    /// 规模复核：仍只拥有同一角色的序列与边界混合；混合代理和
    /// 回调代次随动作一起撤回，不把规则结算或遭遇流程纳入本组件。
    /// 2026-10-25规模复核：Utility多段仍属于同一角色动作状态机；唯一拥有
    /// 协程/自建Sprite/ghost/回调，预热Sprite只借用。音频只发意图，
    /// 不拥有声源或玩家音量。新增另一并行动作通道时必须重新审查。
    /// 动态Idle接入复核：双向淡接仍属本角色播放状态机；只淡出自有ghost，
    /// 不回写主Renderer颜色。音频重订阅按原始开始时间恢复同一段相位。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class BossSequenceAnimator : MonoBehaviour
    {
        private const float ReturnToIdleBlendDuration = 0.1f;
        private const float EnterActionBlendDuration = 0.12f;
        public enum BossAction
        {
            Idle,
            Attack,
            Defend
        }

        private SpriteRenderer _renderer;
        private long _audioOwnerKey;
        private long _audioGeneration;
        private Action<BossAudioRequest> _audioRequestSink;
        private BossAudioRequest? _audioIntent;
        private double _audioStartedAt;
        private bool _warnedMissingAudioSink;
        private BossAnimationProfile _profile;
        private Vector2 _sliceGroundAnchor = new Vector2(0.5f, 0.05f);
        private Sprite[][] _clipSprites;
        private readonly Dictionary<string, Sprite[]> _attackVariantSprites =
            new Dictionary<string, Sprite[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Sprite[][]> _utilityVariantSprites =
            new Dictionary<string, Sprite[][]>(StringComparer.OrdinalIgnoreCase);
        private Action _onActionCancelled;
        private BossAction _currentAction = BossAction.Idle;
        private BossAnimationClip _currentClip;
        private Sprite[] _currentSprites;
        private float _elapsed;
        private bool _playing;
        private bool _actionRunning;
        private Action _onActionComplete;
        private bool _spritesBuilt;
        private Coroutine _prewarmRoutine;
        private BossPreparedPresentation _preparedPresentation;
        private bool _ownsSprites;
        private bool _idlePlaybackArmed;
        private Coroutine _actionRoutine;
        private int _actionVersion;
        private SpriteRenderer _outgoingFrameGhost;

        public BossAction CurrentAction => _currentAction;
        public bool IsActionRunning => _actionRunning;

        /// <summary>旧调用兼容：没有显式对齐值时使用动画资产的回退落点。</summary>
        public void Bind(BossAnimationProfile profile)
        {
            Bind(
                profile,
                profile != null
                    ? profile.groundAnchor
                    : new Vector2(0.5f, 0.05f));
        }

        /// <summary>
        /// 绑定配置并使用敌人区域的统一落点切分序列帧。重复绑定会重建
        /// Sprite 缓存，保证待机、攻击和守势共享同一个 pivot。
        /// </summary>
        public void Bind(
            BossAnimationProfile profile,
            Vector2 groundAnchor)
        {
            StopAll();
            ReleaseSpriteBindings();
            _preparedPresentation = null;
            _profile = profile;
            _idlePlaybackArmed = profile != null;
            _sliceGroundAnchor = new Vector2(
                Mathf.Clamp01(groundAnchor.x),
                Mathf.Clamp01(groundAnchor.y));
            _spritesBuilt = false;
            if (profile == null)
            {
                StopAll();
                return;
            }

            EnsureRenderer();
            BuildSprites();
            EnterIdle();
        }

        /// <summary>
        /// Binds arrays borrowed from the encounter warmup owner. This method
        /// performs no Sprite.Create and remains safe while the actor is
        /// inactive; OnEnable starts idle playback when the formation reveals
        /// the actor.
        /// </summary>
        internal void BindPrepared(
            BossPreparedPresentation presentation,
            Vector2 groundAnchor,
            bool startIdlePlayback)
        {
            StopAll();
            ReleaseSpriteBindings();
            _preparedPresentation = presentation;
            _profile = presentation?.Profile;
            _idlePlaybackArmed = presentation != null && startIdlePlayback;
            _sliceGroundAnchor = new Vector2(
                Mathf.Clamp01(groundAnchor.x),
                Mathf.Clamp01(groundAnchor.y));
            _clipSprites = new Sprite[3][];
            _clipSprites[(int)BossAction.Idle] =
                presentation?.IdleSprites;
            _spritesBuilt = presentation?.IdleSprites != null &&
                            presentation.IdleSprites.Length > 0;
            _ownsSprites = false;
            if (_profile == null || !_spritesBuilt)
            {
                StopAudio();
                return;
            }

            EnsureRenderer();
            if (_idlePlaybackArmed && isActiveAndEnabled)
                EnterIdle();
        }

        /// <summary>
        /// 只写入已经预热的待机首帧，不启动协程、音效或 Infinity。特殊登场在
        /// 阵型布局前调用，使角色尺寸取自真正 Boss 素材而不是静态占位图。
        /// </summary>
        internal bool ShowPreparedIdleFrame()
        {
            Sprite[] idleSprites = _clipSprites != null
                ? _clipSprites[(int)BossAction.Idle]
                : null;
            if (idleSprites == null || idleSprites.Length == 0 ||
                idleSprites[0] == null)
            {
                return false;
            }

            EnsureRenderer();
            ApplyFrame(idleSprites, 0);
            return true;
        }

        /// <summary>
        /// Reports whether an action can play without creating or loading
        /// anything. Prepared production actors return false until the optional
        /// warmup stage has supplied the requested segment.
        /// </summary>
        public bool CanPlayAction(BossAction action, string actionId = null)
        {
            return _profile != null && action != BossAction.Idle &&
                BuildPlaybackSegments(action, actionId) != null;
        }

        public bool HasSpellReleaseMarker(BossAction action, string actionId)
        {
            if (action != BossAction.Defend || _profile == null ||
                !_profile.TryGetUtilityVariant(actionId, out BossUtilityVariant variant))
                return false;
            foreach (BossUtilityStep step in variant.steps)
                if (step != null && step.emitSpellRelease) return true;
            return false;
        }

        /// <summary>进入待机循环（呼吸动画 + 循环音）。</summary>
        public void EnterIdle()
        {
            if (_profile == null)
                return;
            if (_playing && _currentAction == BossAction.Idle &&
                !_actionRunning && _actionRoutine == null)
                return;
            int requestedVersion = unchecked(_actionVersion + 1);
            CancelActionPresentation();
            if (_actionVersion != requestedVersion)
                return;
            BeginIdlePlayback();
        }

        // Normal action completion keeps its cancellation waiter armed through
        // the return blend. Public EnterIdle is the separate interrupt boundary.
        private void BeginIdlePlayback()
        {
            _idlePlaybackArmed = true;
            EnsureSprites();

            _currentAction = BossAction.Idle;
            _currentClip = _profile.idle;
            _currentSprites = _clipSprites?[(int)BossAction.Idle];
            _actionRunning = false;
            _onActionComplete = null;
            _elapsed = 0f;
            _playing = isActiveAndEnabled;
            ApplyFrame(_currentSprites, 0);

            StopAudio();
            BossAnimationClip idleClip = _profile.idle;
            if (isActiveAndEnabled && idleClip.audioClip != null)
                RequestAudio(idleClip.audioClip, true, idleClip.audioVolume, 1f);
        }

        /// <summary>
        /// 播放一次动作段（攻击/守势），结束后自动回到待机。
        /// onImpact 在 Profile 登记的动作峰值回调，供规则结算与表现对齐；
        /// onComplete 在动作结束、即将回待机时回调。
        /// </summary>
        public void PlayAction(
            BossAction action,
            Action onImpact = null,
            Action onComplete = null)
        {
            PlayAction(action, null, onImpact, onComplete);
        }

        /// <summary>
        /// 播放一次带稳定 actionId 的动作段。Attack/Utility 均使用稳定 actionId；未知 Utility ID 回退旧 Defend。
        /// </summary>
        public void PlayAction(
            BossAction action,
            string actionId,
            Action onImpact = null,
            Action onComplete = null,
            Action onCancelled = null,
            Action<Vector3?> onSpellRelease = null)
        {
            if (!isActiveAndEnabled)
            {
                onCancelled?.Invoke();
                return;
            }
            if (_profile == null || action == BossAction.Idle)
            {
                onComplete?.Invoke();
                return;
            }
            List<PlaybackSegment> segments = BuildPlaybackSegments(action, actionId);
            if (segments == null)
            {
                onComplete?.Invoke();
                return;
            }

            int requestedVersion = unchecked(_actionVersion + 1);
            CancelActionPresentation();
            // An old cancellation subscriber can re-enter and start a newer action.
            if (_actionVersion != requestedVersion)
            {
                onCancelled?.Invoke();
                return;
            }
            int version = _actionVersion;
            _currentAction = action;
            _playing = true;
            _actionRunning = true;
            _onActionComplete = onComplete;
            _onActionCancelled = onCancelled;
            StopAudio();
            Coroutine started = StartCoroutine(ActionRoutine(
                segments, onImpact, onSpellRelease, version));
            if (version == _actionVersion && _actionRunning)
                _actionRoutine = started;
        }

        public void StopAll()
        {
            CancelActionPresentation(stopAllPlayback: true);
        }

        private void CancelActionPresentation(bool stopAllPlayback = false)
        {
            unchecked { _actionVersion++; }
            Coroutine previous = _actionRoutine;
            Action interrupted = _onActionCancelled;
            _actionRoutine = null;
            _onActionComplete = null;
            _onActionCancelled = null;
            _actionRunning = false;
            if (previous != null) StopCoroutine(previous);
            ClearFrameBlend();
            StopAudio();
            if (stopAllPlayback)
            {
                Coroutine prewarm = _prewarmRoutine;
                _prewarmRoutine = null;
                if (prewarm != null) StopCoroutine(prewarm);
                _playing = false;
            }
            // Cleanup precedes notification so a subscriber may safely start another round.
            if (interrupted != null)
            {
                try { interrupted(); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }

        private void ClearFrameBlend()
        {
            SpriteRenderer ghost = _outgoingFrameGhost;
            _outgoingFrameGhost = null;
            if (ghost != null)
            {
                ghost.gameObject.SetActive(false);
                Destroy(ghost.gameObject);
            }
        }

        private void FadeOutgoingFrame(float progress)
        {
            if (_outgoingFrameGhost == null || _renderer == null) return;
            // Keep the incoming frame opaque and read the current tint rather
            // than restoring a snapshot over newer hit/death/visibility state.
            Color color = _renderer.color;
            color.a *= 1f - Mathf.Clamp01(progress);
            _outgoingFrameGhost.color = color;
        }

        private sealed class PlaybackSegment
        {
            public BossAnimationClip Clip;
            public Sprite[] Sprites;
            public BossUtilityStepTiming Timing;
        }

        private List<PlaybackSegment> BuildPlaybackSegments(BossAction action, string actionId)
        {
            if (_profile == null) return null;
            if (action == BossAction.Defend &&
                _profile.TryGetUtilityVariant(actionId, out BossUtilityVariant variant))
            {
                var segments = new List<PlaybackSegment>(variant.steps.Count);
                for (int i = 0; i < variant.steps.Count; i++)
                {
                    BossUtilityStep step = variant.steps[i];
                    Sprite[] sprites = ResolveUtilitySprites(actionId, i, step.clip);
                    if (step.clip == null || sprites == null || sprites.Length == 0) return null;
                    segments.Add(new PlaybackSegment { Clip = step.clip, Sprites = sprites, Timing = step });
                }
                return segments;
            }
            EnsureSprites();
            BossAnimationClip clip = ResolveClip(action, actionId, out float impact);
            Sprite[] frames = ResolveSprites(action, actionId, clip);
            if (clip == null || frames == null || frames.Length == 0) return null;
            return new List<PlaybackSegment>
            {
                new PlaybackSegment
                {
                    Clip = clip, Sprites = frames,
                    Timing = new BossUtilityStepTiming { resolveImpact = true, impactNormalized = impact }
                }
            };
        }

        private System.Collections.IEnumerator ActionRoutine(
            List<PlaybackSegment> segments,
            Action onImpact,
            Action<Vector3?> onSpellRelease,
            int version)
        {
            bool firstSegment = true;
            foreach (PlaybackSegment segment in segments)
            {
                if (version != _actionVersion) yield break;
                BossAnimationClip clip = segment.Clip;
                BossUtilityStepTiming timing = segment.Timing;
                _currentClip = clip;
                _currentSprites = segment.Sprites;
                _elapsed = 0f;
                if (firstSegment) CreateOutgoingFrameGhost();
                firstSegment = false;
                ApplyFrame(_currentSprites, 0);
                PlayActionAudio(clip);
                float duration = BossAnimationTiming.GetDuration(_profile, clip, isAction: true);
                float fps = BossAnimationTiming.GetFramesPerSecond(_profile, clip, isAction: true);
                bool impactFired = false;
                bool releaseFired = false;
                float entryBlendElapsed = 0f;
                while (_elapsed < duration)
                {
                    if (version != _actionVersion) yield break;
                    _elapsed += Time.unscaledDeltaTime;
                    ApplyFrame(_currentSprites, Mathf.Min(
                        Mathf.FloorToInt(_elapsed * fps), clip.frameCount - 1));
                    if (_outgoingFrameGhost != null)
                    {
                        entryBlendElapsed += Time.unscaledDeltaTime;
                        FadeOutgoingFrame(entryBlendElapsed / EnterActionBlendDuration);
                        if (entryBlendElapsed >= EnterActionBlendDuration) ClearFrameBlend();
                    }
                    if (timing.resolveImpact && !impactFired &&
                        _elapsed >= duration * timing.impactNormalized)
                    {
                        impactFired = true;
                        if (!InvokeCurrentCallback(onImpact, version)) yield break;
                    }
                    if (timing.emitSpellRelease && !releaseFired &&
                        _elapsed >= duration * timing.releaseNormalized)
                    {
                        releaseFired = true;
                        Vector3? anchor = TryGetSourceAnchorWorld(timing.sourceAnchorNormalized,
                            out Vector3 world) ? world : (Vector3?)null;
                        if (!InvokeCurrentCallback(() => onSpellRelease?.Invoke(anchor), version))
                            yield break;
                    }
                    yield return null;
                }
            }

            if (version != _actionVersion) yield break;
            Action callback = _onActionComplete;
            SpriteRenderer outgoing = CreateOutgoingFrameGhost();
            BeginIdlePlayback();
            // Cancellation remains armed until the visual blend and whole sequence finish.
            if (outgoing != null && _renderer != null)
            {
                float blendElapsed = 0f;
                while (blendElapsed < ReturnToIdleBlendDuration)
                {
                    if (version != _actionVersion) yield break;
                    blendElapsed += Time.unscaledDeltaTime;
                    FadeOutgoingFrame(blendElapsed / ReturnToIdleBlendDuration);
                    yield return null;
                }
            }
            if (version != _actionVersion) yield break;
            ClearFrameBlend();
            _actionRoutine = null;
            _onActionCancelled = null;
            _onActionComplete = null;
            callback?.Invoke();
        }

        private bool InvokeCurrentCallback(Action callback, int version)
        {
            try { callback?.Invoke(); }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (version == _actionVersion) CancelActionPresentation(stopAllPlayback: true);
                return false;
            }
            return version == _actionVersion;
        }

        private bool TryGetSourceAnchorWorld(Vector2 finalFrameUv, out Vector3 world)
        {
            world = default;
            Sprite sprite = _renderer != null ? _renderer.sprite : null;
            if (sprite == null || sprite.pixelsPerUnit <= 0f) return false;
            Vector3 local = new Vector3(
                (finalFrameUv.x * sprite.rect.width - sprite.pivot.x) / sprite.pixelsPerUnit,
                (finalFrameUv.y * sprite.rect.height - sprite.pivot.y) / sprite.pixelsPerUnit, 0f);
            if (_renderer.flipX) local.x = -local.x;
            if (_renderer.flipY) local.y = -local.y;
            world = _renderer.transform.TransformPoint(local);
            return true;
        }

        private SpriteRenderer CreateOutgoingFrameGhost()
        {
            ClearFrameBlend();
            if (_renderer == null || _renderer.sprite == null)
                return null;

            var ghostObject = new GameObject("BossFrameBlend");
            ghostObject.transform.SetParent(_renderer.transform, false);
            var ghost = ghostObject.AddComponent<SpriteRenderer>();
            _outgoingFrameGhost = ghost;
            ghost.sprite = _renderer.sprite;
            ghost.color = _renderer.color;
            ghost.flipX = _renderer.flipX;
            ghost.flipY = _renderer.flipY;
            ghost.sharedMaterial = _renderer.sharedMaterial;
            ghost.sortingLayerID = _renderer.sortingLayerID;
            ghost.sortingOrder = _renderer.sortingOrder + 1;
            ghost.maskInteraction = _renderer.maskInteraction;
            return ghost;
        }

        private void Awake()
        {
            EnsureRenderer();
        }

        private void OnEnable()
        {
            if (_idlePlaybackArmed && _profile != null && !_playing)
                EnterIdle();
            StartDeferredPrewarm();
        }

        private void Update()
        {
            if (!_playing || _profile == null || _actionRunning)
                return;

            BossAnimationClip clip = _currentClip ?? ClipFor(_currentAction);
            Sprite[] sprites = _currentSprites;
            if (clip == null || sprites == null || sprites.Length == 0)
                return;
            _elapsed += Time.unscaledDeltaTime;
            bool isAction = _currentAction != BossAction.Idle;
            int index = BossAnimationTiming.GetFrameIndex(
                _profile,
                clip,
                isAction,
                _elapsed,
                loop: !isAction);

            ApplyFrame(sprites, index);
        }

        private void OnDisable()
        {
            StopAll();
        }

        private void OnDestroy()
        {
            StopAll();
            ReleaseSpriteBindings();
        }

        private BossAnimationClip ClipFor(BossAction action)
        {
            switch (action)
            {
                case BossAction.Attack: return _profile.attack;
                case BossAction.Defend: return _profile.defend;
                default: return _profile.idle;
            }
        }

        private void EnsureSprites()
        {
            if (_spritesBuilt)
                return;
            BuildSprites();
        }

        /// <summary>
        /// 首帧只切待机图集，保证 BOSS 能立即登场；攻击、防守与变体按帧
        /// 预热。动作若早于预热触发，则只同步补齐该一个动作。
        /// </summary>
        private void BuildSprites()
        {
            if (_profile == null)
                return;

            ReleaseSpriteBindings();
            _preparedPresentation = null;
            _ownsSprites = true;

            _clipSprites = new Sprite[3][];
            _clipSprites[(int)BossAction.Idle] = SliceAtlas(_profile.idle);
            _spritesBuilt = true;
            StartDeferredPrewarm();
        }

        private void StartDeferredPrewarm()
        {
            if (_preparedPresentation != null || !isActiveAndEnabled ||
                _profile == null ||
                _clipSprites == null || _prewarmRoutine != null)
                return;
            _prewarmRoutine = StartCoroutine(DeferredPrewarmRoutine());
        }

        private System.Collections.IEnumerator DeferredPrewarmRoutine()
        {
            yield return null;
            EnsureActionSprites(BossAction.Attack, null, _profile.attack);
            yield return null;
            EnsureActionSprites(BossAction.Defend, null, _profile.defend);

            if (_profile.attackVariants != null)
            {
                for (int i = 0; i < _profile.attackVariants.Count; i++)
                {
                    BossAttackVariant variant = _profile.attackVariants[i];
                    if (variant == null ||
                        string.IsNullOrWhiteSpace(variant.actionId) ||
                        variant.clip == null)
                        continue;
                    string actionId = variant.actionId.Trim();
                    if (_attackVariantSprites.ContainsKey(actionId))
                        continue;
                    _attackVariantSprites.Add(
                        actionId,
                        SliceAtlas(variant.clip));
                    yield return null;
                }
            }
            if (_profile.utilityVariants != null)
            {
                foreach (BossUtilityVariant variant in _profile.utilityVariants)
                {
                    if (variant == null || variant.steps == null ||
                        string.IsNullOrWhiteSpace(variant.actionId)) continue;
                    for (int i = 0; i < variant.steps.Count; i++)
                    {
                        ResolveUtilitySprites(variant.actionId, i, variant.steps[i].clip);
                        yield return null;
                    }
                }
            }
            _prewarmRoutine = null;
        }

        private Sprite[] ResolveUtilitySprites(string actionId, int stepIndex, BossAnimationClip clip)
        {
            if (_preparedPresentation != null)
                return _preparedPresentation.ResolveUtilitySprites(actionId, stepIndex);
            EnsureSprites();
            string key = actionId.Trim();
            if (!_utilityVariantSprites.TryGetValue(key, out Sprite[][] arrays))
            {
                if (!_profile.TryGetUtilityVariant(key, out BossUtilityVariant variant)) return null;
                arrays = new Sprite[variant.steps.Count][];
                _utilityVariantSprites.Add(key, arrays);
            }
            if (stepIndex < 0 || stepIndex >= arrays.Length) return null;
            if (arrays[stepIndex] == null) arrays[stepIndex] = SliceAtlas(clip);
            return arrays[stepIndex];
        }

        private Sprite[] SliceAtlas(BossAnimationClip clip)
        {
            if (clip == null || clip.frameAtlas == null)
                return Array.Empty<Sprite>();

            Texture2D atlas = clip.frameAtlas;
            int frameWidth = atlas.width / clip.atlasColumns;
            int frameHeight = atlas.height / clip.atlasRows;
            var sprites = new Sprite[clip.frameCount];
            Vector2 pivot = _sliceGroundAnchor;
            for (int i = 0; i < clip.frameCount; i++)
            {
                int column = i % clip.atlasColumns;
                int rowFromTop = i / clip.atlasColumns;
                Rect rect = new Rect(
                    column * frameWidth,
                    // Sprite rect 以纹理左下为原点，图集行序自上而下。
                    atlas.height - (rowFromTop + 1) * frameHeight,
                    frameWidth,
                    frameHeight);
                sprites[i] = Sprite.Create(
                    atlas,
                    rect,
                    pivot,
                    pixelsPerUnit: frameHeight);
            }
            return sprites;
        }

        private BossAnimationClip ResolveClip(
            BossAction action,
            string actionId,
            out float impactNormalized)
        {
            if (action == BossAction.Attack)
                return _profile.ResolveAttackClip(
                    actionId,
                    out impactNormalized);

            impactNormalized = action == BossAction.Defend
                ? BossAnimationTiming.ResolveDefendImpactNormalized(_profile)
                : BossAnimationTiming.DefaultDefendImpactNormalized;
            return ClipFor(action);
        }

        private Sprite[] ResolveSprites(
            BossAction action,
            string actionId,
            BossAnimationClip clip)
        {
            if (_preparedPresentation != null)
            {
                return _preparedPresentation.ResolveSprites(action, actionId);
            }

            EnsureActionSprites(action, actionId, clip);
            if (action == BossAction.Attack &&
                !string.IsNullOrWhiteSpace(actionId))
            {
                string normalized = actionId.Trim();
                if (_attackVariantSprites.TryGetValue(
                        normalized,
                        out Sprite[] variantSprites) &&
                    variantSprites != null && variantSprites.Length > 0 &&
                    _profile.TryGetAttackVariant(
                        normalized,
                        out BossAnimationClip exactClip,
                        out _))
                {
                    if (ReferenceEquals(exactClip, clip))
                        return variantSprites;
                }
            }

            return _clipSprites?[(int)action];
        }

        private void EnsureActionSprites(
            BossAction action,
            string actionId,
            BossAnimationClip clip)
        {
            if (_preparedPresentation != null)
                return;
            if (_clipSprites == null)
                _clipSprites = new Sprite[3][];

            if (action == BossAction.Attack &&
                !string.IsNullOrWhiteSpace(actionId) &&
                _profile.TryGetAttackVariant(
                    actionId,
                    out BossAnimationClip exactClip,
                    out _) &&
                ReferenceEquals(exactClip, clip))
            {
                string normalized = actionId.Trim();
                if (!_attackVariantSprites.ContainsKey(normalized))
                {
                    _attackVariantSprites.Add(
                        normalized,
                        SliceAtlas(exactClip));
                }
                return;
            }

            int index = (int)action;
            if (_clipSprites[index] == null)
                _clipSprites[index] = SliceAtlas(clip);
        }

        private void ApplyFrame(Sprite[] sprites, int frameIndex)
        {
            if (sprites == null || sprites.Length == 0)
                return;
            int index = Mathf.Clamp(frameIndex, 0, sprites.Length - 1);
            if (_renderer != null && sprites[index] != null)
                _renderer.sprite = sprites[index];
        }

        /// <summary>Bind before BindPrepared; late subscribers can replay the current intent.</summary>
        public void BindAudioRequests(Action<BossAudioRequest> sink)
        {
            if (_audioRequestSink == sink) return;
            BossAudioRequest? intent = _audioIntent;
            if (_audioRequestSink != null) SendAudioStop();
            _audioRequestSink = sink;
            _warnedMissingAudioSink = false;
            if (intent.HasValue && isActiveAndEnabled && sink != null)
                ReplayAudioRequestTo(sink);
        }

        public void ReplayAudioRequestTo(Action<BossAudioRequest> sink)
        {
            if (sink == null || !_audioIntent.HasValue || !isActiveAndEnabled) return;
            BossAudioRequest previous = _audioIntent.Value;
            if (previous.Clip == null) return;
            double offset = System.Math.Max(0d,
                (Time.unscaledTimeAsDouble - _audioStartedAt) * previous.Pitch);
            double length = (double)previous.Clip.samples / previous.Clip.frequency;
            if (previous.Loop && length > 0d) offset %= length;
            else if (offset >= length)
            {
                _audioIntent = null;
                try { sink(new BossAudioRequest(AudioOwnerKey, ++_audioGeneration,
                    BossAudioRequestKind.Stop)); }
                catch (Exception error) { Debug.LogException(error); }
                return;
            }
            var replay = new BossAudioRequest(AudioOwnerKey, ++_audioGeneration,
                BossAudioRequestKind.Play, previous.Clip, previous.Loop,
                previous.Volume, previous.Pitch, (float)offset);
            _audioIntent = replay;
            // Explicit re-subscription is a fresh request, sent only to that subscriber.
            // Existing listeners are not restarted; Audio can retain strict Stop tombstones.
            try { sink(replay); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private long AudioOwnerKey
        {
            get
            {
                if (_audioOwnerKey == 0)
                    _audioOwnerKey = BossAudioOwnerIdentity.Allocate();
                return _audioOwnerKey;
            }
        }

        private void PlayActionAudio(BossAnimationClip clip)
        {
            StopAudio();
            if (clip?.audioClip != null && isActiveAndEnabled)
                RequestAudio(clip.audioClip, false, clip.audioVolume,
                    BossAnimationTiming.GetActionAudioPitch(_profile));
        }

        private void RequestAudio(AudioClip clip, bool loop, float volume, float pitch)
        {
            if (clip == null) return;
            var request = new BossAudioRequest(AudioOwnerKey, ++_audioGeneration,
                BossAudioRequestKind.Play, clip, loop, volume, pitch);
            _audioStartedAt = Time.unscaledTimeAsDouble;
            _audioIntent = request;
            if (_audioRequestSink != null)
                DeliverAudioRequest(request);
            else if (!_warnedMissingAudioSink)
            {
                _warnedMissingAudioSink = true;
                Debug.LogWarning("Boss animation audio has no request sink; bind its Audio coordinator. Playback intent retained for late subscription.");
            }
        }

        private void StopAudio()
        {
            _audioIntent = null;
            SendAudioStop();
        }

        private void SendAudioStop()
        {
            if (_audioOwnerKey != 0)
                DeliverAudioRequest(new BossAudioRequest(_audioOwnerKey,
                    ++_audioGeneration, BossAudioRequestKind.Stop));
        }

        private void DeliverAudioRequest(BossAudioRequest request)
        {
            try { _audioRequestSink?.Invoke(request); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private void EnsureRenderer()
        {
            if (_renderer == null)
                _renderer = GetComponent<SpriteRenderer>();
        }

        private void ReleaseSpriteBindings()
        {
            if (_ownsSprites)
            {
                DestroySpriteArrays(_clipSprites);
                foreach (Sprite[] sprites in _attackVariantSprites.Values)
                    DestroySpriteArray(sprites);
                foreach (Sprite[][] arrays in _utilityVariantSprites.Values)
                    DestroySpriteArrays(arrays);
            }
            _clipSprites = null;
            _attackVariantSprites.Clear();
            _utilityVariantSprites.Clear();
            _currentClip = null;
            _currentSprites = null;
            _spritesBuilt = false;
            _ownsSprites = false;
        }

        private static void DestroySpriteArrays(Sprite[][] arrays)
        {
            if (arrays == null)
                return;
            for (int i = 0; i < arrays.Length; i++)
                DestroySpriteArray(arrays[i]);
        }

        private static void DestroySpriteArray(Sprite[] sprites)
        {
            if (sprites == null)
                return;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null)
                    Destroy(sprites[i]);
            }
        }
    }
}
