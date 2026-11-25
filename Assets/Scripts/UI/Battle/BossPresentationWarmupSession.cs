using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// Describes one independently observable part of BOSS presentation
    /// preparation. Critical readiness covers entrance and idle presentation;
    /// optional readiness covers combat actions and variants.
    /// </summary>
    internal enum BossPresentationWarmupStatus
    {
        NotStarted,
        Preparing,
        Ready,
        Failed,
        Cancelled,
        Released
    }

    /// <summary>
    /// Immutable identity plus incrementally populated presentation data owned
    /// by a <see cref="BossPresentationWarmupSession"/>. Consumers borrow its
    /// Sprite arrays; only the session may destroy them.
    /// </summary>
    internal sealed class BossPreparedPresentation
    {
        private readonly Dictionary<string, Sprite[]> _attackVariants =
            new Dictionary<string, Sprite[]>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Sprite[][]> _utilityVariants =
            new Dictionary<string, Sprite[][]>(StringComparer.OrdinalIgnoreCase);

        public BossPreparedPresentation(
            string contentId,
            BossAnimationProfile profile,
            BossEntranceRuntimeDescriptor entranceDescriptor)
        {
            ContentId = contentId;
            Profile = profile;
            EntranceDescriptor = entranceDescriptor;
        }

        public string ContentId { get; }
        public BossAnimationProfile Profile { get; }
        public BossEntranceRuntimeDescriptor EntranceDescriptor { get; }
        public Sprite[] EntranceSprites { get; internal set; }
        public Sprite[] IdleSprites { get; internal set; }
        public Sprite[] AttackSprites { get; internal set; }
        public Sprite[] DefendSprites { get; internal set; }

        public bool HasEntrance => EntranceDescriptor != null &&
                                   EntranceDescriptor.UsesSpiritConvergence &&
                                   EntranceSprites != null &&
                                   EntranceSprites.Length > 0;

        public void SetAttackVariant(string actionId, Sprite[] sprites)
        {
            if (string.IsNullOrWhiteSpace(actionId) || sprites == null)
                return;
            _attackVariants[actionId.Trim()] = sprites;
        }

        public void SetUtilityVariant(string actionId, Sprite[][] sprites)
        {
            if (!string.IsNullOrWhiteSpace(actionId) && sprites != null)
                _utilityVariants[actionId.Trim()] = sprites;
        }

        public Sprite[] ResolveUtilitySprites(string actionId, int stepIndex)
        {
            return !string.IsNullOrWhiteSpace(actionId) && stepIndex >= 0 &&
                _utilityVariants.TryGetValue(actionId.Trim(), out Sprite[][] steps) &&
                stepIndex < steps.Length ? steps[stepIndex] : null;
        }

        public Sprite[] ResolveSprites(
            BossSequenceAnimator.BossAction action,
            string actionId)
        {
            switch (action)
            {
                case BossSequenceAnimator.BossAction.Attack:
                    if (!string.IsNullOrWhiteSpace(actionId) &&
                        _attackVariants.TryGetValue(
                            actionId.Trim(),
                            out Sprite[] variant))
                    {
                        return variant;
                    }
                    return AttackSprites;
                case BossSequenceAnimator.BossAction.Defend:
                    return DefendSprites;
                default:
                    return IdleSprites;
            }
        }
    }

    /// <summary>
    /// Owns the complete lifetime of one BOSS presentation warmup generation:
    /// staged resource requests, frame-budgeted Sprite creation, readiness,
    /// cancellation, cache deduplication and release. The public surface does
    /// not expose atlas paths, slicing order or cache ownership to callers.
    ///
    /// Design review: this deliberately remains one cohesive async state
    /// machine even above the 800-line review threshold. It uniquely owns the
    /// generation, request caches, borrowed Sprite lifetime and two readiness
    /// phases; splitting by load/slice/release time would leak those invariants.
    /// Re-review if a second resource family or concurrent generation is added.
    /// </summary>
    internal sealed class BossPresentationWarmupSession : IDisposable
    {
        private const int MaxSpritesPerFrame = 4;
        private const double MaxSliceMillisecondsPerFrame = 1.5d;
        private const float AudioPrepareTimeoutSeconds = 8f;

        private static readonly ProfilerMarker ManifestMarker =
            new ProfilerMarker("BossWarmup.Manifest");
        private static readonly ProfilerMarker ResourceRequestMarker =
            new ProfilerMarker("BossWarmup.ResourceRequest");
        private static readonly ProfilerMarker SliceBatchMarker =
            new ProfilerMarker("BossWarmup.SliceBatch");
        private static readonly ProfilerMarker AudioPrepareMarker =
            new ProfilerMarker("BossWarmup.AudioPrepare");
        private static readonly ProfilerMarker PublishMarker =
            new ProfilerMarker("BossWarmup.Publish");

        private readonly Dictionary<string, Texture2D> _textures =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<string, AudioClip> _audioClips =
            new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private readonly HashSet<int> _preparedAudioIds = new HashSet<int>();
        private readonly Dictionary<SpriteSliceKey, Sprite[]> _spriteCache =
            new Dictionary<SpriteSliceKey, Sprite[]>();

        private int _generation;
        private BossPreparedPresentation _prepared;
        private BossAnimationProfile _publishedProfile;

        public int Generation => _generation;
        public BossPresentationWarmupStatus CriticalStatus { get; private set; }
            = BossPresentationWarmupStatus.NotStarted;
        public BossPresentationWarmupStatus OptionalStatus { get; private set; }
            = BossPresentationWarmupStatus.NotStarted;
        public string CriticalError { get; private set; }
        public string OptionalError { get; private set; }

        public bool TryGetCritical(out BossPreparedPresentation presentation)
        {
            presentation = CriticalStatus == BossPresentationWarmupStatus.Ready
                ? _prepared
                : null;
            return presentation != null;
        }

        /// <summary>
        /// Starts a fresh generation. The callback fires exactly once after all
        /// entrance and idle resources, Sprites and idle audio data are ready;
        /// optional actions continue warming afterward without actor activity.
        /// </summary>
        public IEnumerator PrepareAsync(
            string contentId,
            Vector2 groundAnchor,
            Action<BossPreparedPresentation> criticalReady)
        {
            if (CriticalStatus != BossPresentationWarmupStatus.NotStarted)
                yield break;

            int generation = ++_generation;
            CriticalStatus = BossPresentationWarmupStatus.Preparing;
            OptionalStatus = BossPresentationWarmupStatus.NotStarted;
            CriticalError = null;
            OptionalError = null;

            BossAnimationRuntimeManifest manifest;
            using (ManifestMarker.Auto())
            {
                if (!BossAnimationRuntimeCatalog.TryGetManifest(
                        contentId,
                        out manifest))
                {
                    manifest = null;
                }
            }

            if (manifest == null)
            {
                FailCritical($"找不到 BOSS 运行时清单：{contentId}。");
                yield break;
            }

            BossAnimationProfile profile =
                BossAnimationRuntimeCatalog.CreateRuntimeProfile(manifest);
            _prepared = new BossPreparedPresentation(
                manifest.contentId,
                profile,
                manifest.entrance);
            Vector2 pivot = new Vector2(
                Mathf.Clamp01(groundAnchor.x),
                Mathf.Clamp01(groundAnchor.y));

            string error = null;
            if (manifest.entrance != null &&
                manifest.entrance.UsesSpiritConvergence)
            {
                Sprite[] entranceSprites = null;
                yield return PrepareEntranceAsync(
                    manifest.entrance,
                    generation,
                    value => entranceSprites = value,
                    value => error = value);
                if (!IsCurrent(generation))
                    yield break;
                if (error != null)
                {
                    FailCritical(error);
                    yield break;
                }
                _prepared.EntranceSprites = entranceSprites;
            }

            Sprite[] idleSprites = null;
            yield return PrepareClipAsync(
                manifest.idle,
                profile.idle,
                pivot,
                generation,
                value => idleSprites = value,
                value => error = value);
            if (!IsCurrent(generation))
                yield break;
            if (error != null)
            {
                FailCritical(error);
                yield break;
            }

            _prepared.IdleSprites = idleSprites;
            CriticalStatus = BossPresentationWarmupStatus.Ready;
            criticalReady?.Invoke(_prepared);

            // Optional action atlases deliberately start only after the complete
            // entrance + idle critical path has become consumable.
            OptionalStatus = BossPresentationWarmupStatus.Preparing;
            yield return null;
            yield return PrepareOptionalAsync(manifest, profile, pivot, generation);
            if (!IsCurrent(generation))
                yield break;

            if (OptionalError != null)
            {
                OptionalStatus = BossPresentationWarmupStatus.Failed;
                yield break;
            }

            if (!profile.TryValidate(out string validationError))
            {
                OptionalError = $"BOSS 运行时素材无效：{validationError}";
                OptionalStatus = BossPresentationWarmupStatus.Failed;
                yield break;
            }

            using (PublishMarker.Auto())
            {
                if (BossAnimationRuntimeCatalog.PublishPreparedProfile(
                        manifest.contentId,
                        profile))
                {
                    _publishedProfile = profile;
                }
            }
            OptionalStatus = BossPresentationWarmupStatus.Ready;
        }

        public void Cancel()
        {
            if (CriticalStatus == BossPresentationWarmupStatus.Released)
                return;

            _generation++;
            if (CriticalStatus == BossPresentationWarmupStatus.Preparing)
                CriticalStatus = BossPresentationWarmupStatus.Cancelled;
            if (OptionalStatus == BossPresentationWarmupStatus.Preparing)
                OptionalStatus = BossPresentationWarmupStatus.Cancelled;
        }

        public void Release()
        {
            if (CriticalStatus == BossPresentationWarmupStatus.Released)
                return;

            Cancel();
            DiscardOwnedPresentation();

            CriticalStatus = BossPresentationWarmupStatus.Released;
            OptionalStatus = BossPresentationWarmupStatus.Released;
        }

        public void Dispose()
        {
            Release();
        }

        private IEnumerator PrepareEntranceAsync(
            BossEntranceRuntimeDescriptor descriptor,
            int generation,
            Action<Sprite[]> completed,
            Action<string> failed)
        {
            Texture2D atlas = null;
            string error = null;
            yield return LoadTextureAsync(
                descriptor.frameAtlasResourcePath,
                generation,
                value => atlas = value,
                value => error = value);
            if (!IsCurrent(generation))
                yield break;
            if (error != null)
            {
                failed?.Invoke(error);
                yield break;
            }

            // Do not combine texture upload completion with the first Sprite
            // batch in one frame.
            yield return null;
            if (!IsCurrent(generation))
                yield break;
            Sprite[] sprites = null;
            Vector2 entrancePivot = new Vector2(
                Mathf.Clamp01(descriptor.entranceGroundAnchorX),
                Mathf.Clamp01(descriptor.entranceGroundAnchorY));
            yield return GetOrCreateSpritesAsync(
                atlas,
                descriptor.atlasColumns,
                descriptor.atlasRows,
                descriptor.firstFrame,
                descriptor.playbackFrameCount,
                entrancePivot,
                generation,
                value => sprites = value,
                value => error = value);
            if (!IsCurrent(generation))
                yield break;
            if (error != null)
                failed?.Invoke(error);
            else
                completed?.Invoke(sprites);
        }

        private IEnumerator PrepareOptionalAsync(
            BossAnimationRuntimeManifest manifest,
            BossAnimationProfile profile,
            Vector2 pivot,
            int generation)
        {
            string error = null;
            Sprite[] sprites = null;
            yield return PrepareClipAsync(
                manifest.attack,
                profile.attack,
                pivot,
                generation,
                value => sprites = value,
                value => error = value);
            if (!IsCurrent(generation))
                yield break;
            if (error != null)
            {
                OptionalError = error;
                yield break;
            }
            _prepared.AttackSprites = sprites;

            yield return null;
            sprites = null;
            yield return PrepareClipAsync(
                manifest.defend,
                profile.defend,
                pivot,
                generation,
                value => sprites = value,
                value => error = value);
            if (!IsCurrent(generation))
                yield break;
            if (error != null)
            {
                OptionalError = error;
                yield break;
            }
            _prepared.DefendSprites = sprites;

            for (int i = 0; i < (manifest.attackVariants?.Count ?? 0); i++)
            {
                if (!IsCurrent(generation))
                    yield break;
                yield return null;
                BossAnimationRuntimeAttackDescriptor descriptor =
                    manifest.attackVariants[i];
                sprites = null;
                error = null;
                yield return PrepareClipAsync(
                    descriptor.clip,
                    profile.attackVariants[i].clip,
                    pivot,
                    generation,
                    value => sprites = value,
                    value => error = value);
                if (!IsCurrent(generation))
                    yield break;
                if (error != null)
                {
                    OptionalError = error;
                    yield break;
                }
                _prepared.SetAttackVariant(descriptor.actionId, sprites);
            }

            for (int i = 0; i < (manifest.utilityVariants?.Count ?? 0); i++)
            {
                BossAnimationRuntimeUtilityDescriptor variant = manifest.utilityVariants[i];
                var steps = new Sprite[variant.steps.Count][];
                // Borrowed arrays are owned/deduplicated by this same warmup session.
                _prepared.SetUtilityVariant(variant.actionId, steps);
                for (int j = 0; j < variant.steps.Count; j++)
                {
                    if (!IsCurrent(generation)) yield break;
                    yield return null;
                    sprites = null;
                    error = null;
                    yield return PrepareClipAsync(variant.steps[j].clip,
                        profile.utilityVariants[i].steps[j].clip, pivot, generation,
                        value => sprites = value, value => error = value);
                    if (!IsCurrent(generation)) yield break;
                    if (error != null)
                    {
                        OptionalError = error;
                        yield break;
                    }
                    steps[j] = sprites;
                }
            }
        }

        private IEnumerator PrepareClipAsync(
            BossAnimationRuntimeClipDescriptor descriptor,
            BossAnimationClip target,
            Vector2 pivot,
            int generation,
            Action<Sprite[]> completed,
            Action<string> failed)
        {
            Texture2D atlas = null;
            string error = null;
            yield return LoadTextureAsync(
                descriptor.frameAtlasResourcePath,
                generation,
                value => atlas = value,
                value => error = value);
            if (!IsCurrent(generation))
                yield break;
            if (error != null)
            {
                failed?.Invoke(error);
                yield break;
            }

            target.frameAtlas = atlas;
            // Texture integration and Sprite.Create are separate frame-budget
            // consumers even though the asset request itself was asynchronous.
            yield return null;
            if (!IsCurrent(generation))
                yield break;
            Sprite[] sprites = null;
            yield return GetOrCreateSpritesAsync(
                atlas,
                descriptor.atlasColumns,
                descriptor.atlasRows,
                0,
                descriptor.frameCount,
                pivot,
                generation,
                value => sprites = value,
                value => error = value);
            if (!IsCurrent(generation))
                yield break;
            if (error != null)
            {
                failed?.Invoke(error);
                yield break;
            }

            // A validated neutral segment (Idle/Defend) needs no fabricated silent WAV.
            if (descriptor.frameCount == 1 && string.IsNullOrWhiteSpace(descriptor.audioResourcePath))
            {
                target.audioClip = null;
                completed?.Invoke(sprites);
                yield break;
            }
            AudioClip audio = null;
            yield return LoadAudioClipAsync(
                descriptor.audioResourcePath,
                generation,
                value => audio = value,
                value => error = value);
            if (!IsCurrent(generation))
                yield break;
            if (error != null)
            {
                failed?.Invoke(error);
                yield break;
            }

            target.audioClip = audio;
            // Audio data preparation can schedule decompression; keep it away
            // from the resource request completion frame.
            yield return null;
            if (!IsCurrent(generation))
                yield break;
            yield return PrepareAudioDataAsync(
                audio,
                descriptor.audioResourcePath,
                generation,
                value => error = value);
            if (!IsCurrent(generation))
                yield break;
            if (error != null)
                failed?.Invoke(error);
            else
                completed?.Invoke(sprites);
        }

        private IEnumerator LoadTextureAsync(
            string path,
            int generation,
            Action<Texture2D> completed,
            Action<string> failed)
        {
            if (_textures.TryGetValue(path, out Texture2D cached) &&
                cached != null)
            {
                completed?.Invoke(cached);
                yield break;
            }

            ResourceRequest request;
            using (ResourceRequestMarker.Auto())
            {
                request = Resources.LoadAsync<Texture2D>(path);
                request.priority = -1;
            }
            yield return request;
            if (!IsCurrent(generation))
                yield break;

            Texture2D texture = request.asset as Texture2D;
            if (texture == null)
            {
                failed?.Invoke($"无法加载 BOSS 图集：{path}。");
                yield break;
            }
            _textures[path] = texture;
            completed?.Invoke(texture);
        }

        private IEnumerator LoadAudioClipAsync(
            string path,
            int generation,
            Action<AudioClip> completed,
            Action<string> failed)
        {
            if (_audioClips.TryGetValue(path, out AudioClip cached) &&
                cached != null)
            {
                completed?.Invoke(cached);
                yield break;
            }

            ResourceRequest request;
            using (ResourceRequestMarker.Auto())
            {
                request = Resources.LoadAsync<AudioClip>(path);
                request.priority = -1;
            }
            yield return request;
            if (!IsCurrent(generation))
                yield break;

            AudioClip clip = request.asset as AudioClip;
            if (clip == null)
            {
                failed?.Invoke($"无法加载 BOSS 音频：{path}。");
                yield break;
            }
            _audioClips[path] = clip;
            completed?.Invoke(clip);
        }

        private IEnumerator PrepareAudioDataAsync(
            AudioClip clip,
            string path,
            int generation,
            Action<string> failed)
        {
            if (clip == null)
            {
                failed?.Invoke($"BOSS 音频为空：{path}。");
                yield break;
            }
            if (_preparedAudioIds.Contains(clip.GetInstanceID()) ||
                clip.loadState == AudioDataLoadState.Loaded)
            {
                _preparedAudioIds.Add(clip.GetInstanceID());
                yield break;
            }

            if (clip.loadState == AudioDataLoadState.Unloaded)
            {
                using (AudioPrepareMarker.Auto())
                    clip.LoadAudioData();
            }

            float deadline = Time.realtimeSinceStartup +
                             AudioPrepareTimeoutSeconds;
            while (IsCurrent(generation) &&
                   clip.loadState != AudioDataLoadState.Loaded &&
                   clip.loadState != AudioDataLoadState.Failed &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            if (!IsCurrent(generation))
                yield break;
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                failed?.Invoke(
                    $"BOSS 音频数据准备失败或超时：{path}，" +
                    $"状态 {clip.loadState}。");
                yield break;
            }
            _preparedAudioIds.Add(clip.GetInstanceID());
        }

        private IEnumerator GetOrCreateSpritesAsync(
            Texture2D atlas,
            int columns,
            int rows,
            int firstFrame,
            int frameCount,
            Vector2 pivot,
            int generation,
            Action<Sprite[]> completed,
            Action<string> failed)
        {
            if (atlas == null || columns <= 0 || rows <= 0 ||
                firstFrame < 0 || frameCount <= 0 ||
                firstFrame + frameCount > columns * rows ||
                atlas.width % columns != 0 || atlas.height % rows != 0)
            {
                failed?.Invoke("BOSS 图集切片参数或尺寸无效。");
                yield break;
            }

            var key = new SpriteSliceKey(
                atlas.GetInstanceID(),
                columns,
                rows,
                firstFrame,
                frameCount,
                pivot);
            if (_spriteCache.TryGetValue(key, out Sprite[] cached))
            {
                completed?.Invoke(cached);
                yield break;
            }

            int frameWidth = atlas.width / columns;
            int frameHeight = atlas.height / rows;
            var sprites = new Sprite[frameCount];
            // Register ownership before the first Sprite.Create so cancellation
            // also releases a partially completed batch.
            _spriteCache.Add(key, sprites);
            int next = 0;
            while (next < frameCount)
            {
                if (!IsCurrent(generation))
                    yield break;

                int createdThisFrame = 0;
                bool sliceFailed = false;
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                using (SliceBatchMarker.Auto())
                {
                    while (next < frameCount &&
                           createdThisFrame < MaxSpritesPerFrame &&
                           (createdThisFrame == 0 ||
                            ElapsedMilliseconds(started) <
                            MaxSliceMillisecondsPerFrame))
                    {
                        int sourceFrame = firstFrame + next;
                        int column = sourceFrame % columns;
                        int rowFromTop = sourceFrame / columns;
                        Rect rect = new Rect(
                            column * frameWidth,
                            atlas.height - (rowFromTop + 1) * frameHeight,
                            frameWidth,
                            frameHeight);
                        Exception creationError = null;
                        try
                        {
                            sprites[next] = Sprite.Create(
                                atlas,
                                rect,
                                pivot,
                                frameHeight);
                        }
                        catch (Exception exception)
                        {
                            creationError = exception;
                        }
                        if (creationError != null)
                        {
                            failed?.Invoke(
                                $"BOSS 图集切片失败：{atlas.name}，" +
                                creationError.Message);
                            sliceFailed = true;
                            break;
                        }
                        next++;
                        createdThisFrame++;
                    }
                }
                if (sliceFailed)
                    yield break;
                if (next < frameCount)
                    yield return null;
            }

            completed?.Invoke(sprites);
        }

        private void FailCritical(string error)
        {
            CriticalError = error;
            CriticalStatus = BossPresentationWarmupStatus.Failed;
            if (OptionalStatus == BossPresentationWarmupStatus.Preparing)
                OptionalStatus = BossPresentationWarmupStatus.Cancelled;
            DiscardOwnedPresentation();
        }

        private bool IsCurrent(int generation)
        {
            return generation == _generation &&
                   CriticalStatus != BossPresentationWarmupStatus.Cancelled &&
                   CriticalStatus != BossPresentationWarmupStatus.Released;
        }

        private static double ElapsedMilliseconds(long started)
        {
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            return elapsed * 1000d /
                   System.Diagnostics.Stopwatch.Frequency;
        }

        private static void DestroySprites(Sprite[] sprites)
        {
            if (sprites == null)
                return;
            for (int i = 0; i < sprites.Length; i++)
                DestroyObject(sprites[i]);
        }

        private void DiscardOwnedPresentation()
        {
            if (_publishedProfile != null && _prepared != null)
            {
                BossAnimationRuntimeCatalog.ReleasePreparedProfile(
                    _prepared.ContentId,
                    _publishedProfile);
            }
            _publishedProfile = null;
            foreach (Sprite[] sprites in _spriteCache.Values)
                DestroySprites(sprites);
            _spriteCache.Clear();
            _textures.Clear();
            _audioClips.Clear();
            _preparedAudioIds.Clear();
            BossAnimationProfile profile = _prepared?.Profile;
            _prepared = null;
            if (profile != null)
                DestroyObject(profile);
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }

        private readonly struct SpriteSliceKey : IEquatable<SpriteSliceKey>
        {
            private readonly int _atlasInstanceId;
            private readonly int _columns;
            private readonly int _rows;
            private readonly int _firstFrame;
            private readonly int _frameCount;
            private readonly Vector2 _pivot;

            public SpriteSliceKey(
                int atlasInstanceId,
                int columns,
                int rows,
                int firstFrame,
                int frameCount,
                Vector2 pivot)
            {
                _atlasInstanceId = atlasInstanceId;
                _columns = columns;
                _rows = rows;
                _firstFrame = firstFrame;
                _frameCount = frameCount;
                _pivot = pivot;
            }

            public bool Equals(SpriteSliceKey other)
            {
                return _atlasInstanceId == other._atlasInstanceId &&
                       _columns == other._columns && _rows == other._rows &&
                       _firstFrame == other._firstFrame &&
                       _frameCount == other._frameCount &&
                       _pivot == other._pivot;
            }

            public override bool Equals(object obj)
            {
                return obj is SpriteSliceKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _atlasInstanceId;
                    hash = hash * 397 ^ _columns;
                    hash = hash * 397 ^ _rows;
                    hash = hash * 397 ^ _firstFrame;
                    hash = hash * 397 ^ _frameCount;
                    hash = hash * 397 ^ _pivot.GetHashCode();
                    return hash;
                }
            }
        }
    }
}
