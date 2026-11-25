using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// BOSS 动作的轻量运行时描述。只保存 Resources 路径和切帧
    /// 参数，不持有大图集 PPtr，因此隐藏战斗装配时不会解码 BOSS 素材。
    /// </summary>
    [Serializable]
    public sealed class BossAnimationRuntimeClipDescriptor
    {
        public string frameAtlasResourcePath;
        public string audioResourcePath;
        [Min(1)] public int frameCount = 48;
        [Min(1)] public int atlasColumns = 8;
        [Min(1)] public int atlasRows = 6;
        [Min(0.1f)] public float framesPerSecond = 12f;
        [Range(0f, 1f)] public float audioVolume = 1f;

        public bool TryValidate(out string error, bool allowSilentStatic = false)
        {
            if (string.IsNullOrWhiteSpace(frameAtlasResourcePath) ||
                (string.IsNullOrWhiteSpace(audioResourcePath) &&
                 !(allowSilentStatic && frameCount == 1)))
            {
                error = "图集或音频 Resources 路径为空。";
                return false;
            }
            if (frameCount <= 0 || atlasColumns <= 0 || atlasRows <= 0 ||
                frameCount > atlasColumns * atlasRows || framesPerSecond <= 0f)
            {
                error = "切帧参数无效。";
                return false;
            }
            error = null;
            return true;
        }

        public BossAnimationClip CreateClip()
        {
            return new BossAnimationClip
            {
                frameCount = frameCount,
                atlasColumns = atlasColumns,
                atlasRows = atlasRows,
                framesPerSecond = framesPerSecond,
                audioVolume = audioVolume
            };
        }
    }

    [Serializable]
    public sealed class BossAnimationRuntimeAttackDescriptor
    {
        public string actionId;
        public BossAnimationRuntimeClipDescriptor clip =
            new BossAnimationRuntimeClipDescriptor();
        [Range(0f, 1f)] public float hitNormalized = 0.46f;
    }

    /// <summary>
    /// 关卡 BOSS 的轻量登场描述。只保存入口图集 Resources 路径与字幕，
    /// 不强引用地图或战斗大图，允许战斗显示后单独异步准备。
    /// </summary>
    [Serializable]
    public sealed class BossEntranceRuntimeDescriptor
    {
        public const string ImpactDropStyleId = "impact_drop";
        public const string SpiritConvergenceStyleId = "spirit_convergence";
        public const string WaterConvergenceStyleId = "water_convergence";

        public string styleId = ImpactDropStyleId;
        public string frameAtlasResourcePath;
        [Min(1)] public int atlasFrameCount = 32;
        [Min(1)] public int atlasColumns = 8;
        [Min(1)] public int atlasRows = 4;
        [Min(0)] public int firstFrame = 4;
        [Min(1)] public int playbackFrameCount = 24;
        [Min(0.1f)] public float framesPerSecond = 12f;
        [Min(0)] public int speechFrame = 8;
        [Min(0f)] public float speechDuration = 2.1f;
        public string speakerName;
        [TextArea(1, 2)] public string speechText;
        public string voiceCueId;
        [Tooltip("按透明主体高度相对待机帧自动换算的比例；避免不同图集留白造成跳变。")]
        [Range(0.4f, 1.5f)] public float visualScaleRelativeToIdle = 1f;
        [Tooltip("由登场末帧与待机首帧的透明主体中心预计算，保证切回待机不横跳。")]
        [Range(0f, 1f)] public float entranceGroundAnchorX = 0.5f;
        [Tooltip("由透明主体脚底边界预计算的登场图集纵向落点锚。")]
        [Range(0f, 1f)] public float entranceGroundAnchorY = 0.05f;

        public bool UsesSpiritConvergence => string.Equals(
            styleId?.Trim(),
            SpiritConvergenceStyleId,
            StringComparison.OrdinalIgnoreCase);

        // 水汇聚只使用战斗待机主体，不需要挑衅图集或台词。
        public bool UsesWaterConvergence => string.Equals(
            styleId?.Trim(), WaterConvergenceStyleId,
            StringComparison.OrdinalIgnoreCase);

        public bool TryValidate(out string error)
        {
            error = null;
            if (!UsesSpiritConvergence)
                return true;
            if (string.IsNullOrWhiteSpace(frameAtlasResourcePath) ||
                atlasFrameCount <= 0 || atlasColumns <= 0 || atlasRows <= 0 ||
                atlasFrameCount > atlasColumns * atlasRows ||
                firstFrame < 0 || playbackFrameCount <= 0 ||
                firstFrame + playbackFrameCount > atlasFrameCount ||
                framesPerSecond <= 0f || speechFrame < firstFrame ||
                speechFrame >= firstFrame + playbackFrameCount ||
                speechDuration < 0f || string.IsNullOrWhiteSpace(speechText) ||
                visualScaleRelativeToIdle <= 0f ||
                entranceGroundAnchorX < 0f || entranceGroundAnchorX > 1f ||
                entranceGroundAnchorY < 0f || entranceGroundAnchorY > 1f)
            {
                error = "灵气汇聚登场的图集、播放段或台词配置无效。";
                return false;
            }
            return true;
        }
    }

    public sealed class BossEntranceRuntimePresentation
    {
        public BossEntranceRuntimePresentation(
            BossEntranceRuntimeDescriptor descriptor,
            Texture2D frameAtlas)
        {
            Descriptor = descriptor;
            FrameAtlas = frameAtlas;
        }

        public BossEntranceRuntimeDescriptor Descriptor { get; }
        public Texture2D FrameAtlas { get; }
        public bool IsReady => Descriptor != null && FrameAtlas != null;
    }

    [CreateAssetMenu(
        fileName = "NewBossAnimationRuntimeManifest",
        menuName = "万界长卷/战斗/BOSS运行时动作清单")]
    public sealed class BossAnimationRuntimeManifest : ScriptableObject
    {
        public const string DefaultResourceRoot = "BossAnimationManifests";

        public string contentId;
        [Range(
            BossAnimationTiming.MinimumActionPlaybackSpeed,
            BossAnimationTiming.MaximumActionPlaybackSpeed)]
        public float actionPlaybackSpeed =
            BossAnimationTiming.DefaultActionPlaybackSpeed;
        [Range(0f, 1f)]
        public float defendImpactNormalized =
            BossAnimationTiming.DefaultDefendImpactNormalized;
        public BossAnimationRuntimeClipDescriptor idle =
            new BossAnimationRuntimeClipDescriptor();
        public BossAnimationRuntimeClipDescriptor attack =
            new BossAnimationRuntimeClipDescriptor();
        public BossAnimationRuntimeClipDescriptor defend =
            new BossAnimationRuntimeClipDescriptor();
        public List<BossAnimationRuntimeAttackDescriptor> attackVariants =
            new List<BossAnimationRuntimeAttackDescriptor>();
        public List<BossAnimationRuntimeUtilityDescriptor> utilityVariants =
            new List<BossAnimationRuntimeUtilityDescriptor>();
        public BossEntranceRuntimeDescriptor entrance =
            new BossEntranceRuntimeDescriptor();
        public Vector2 groundAnchor = new Vector2(0.5f, 0.05f);
        [Min(0.1f)] public float visualScale = 1f;

        public bool TryValidate(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(contentId))
            {
                error = "BOSS 运行时清单缺少 contentId。";
                return false;
            }
            if (!BossAnimationTiming.IsValidNormalizedTime(
                    defendImpactNormalized))
            {
                error = $"{contentId} 的 Defend 受力峰值必须在 0 到 1 之间。";
                return false;
            }
            if (idle == null || !idle.TryValidate(out error, allowSilentStatic: true) ||
                attack == null || !attack.TryValidate(out error) ||
                defend == null || !defend.TryValidate(out error, allowSilentStatic: true))
            {
                error = $"{contentId} 的基础动作描述无效：{error}";
                return false;
            }
            if (entrance != null && !entrance.TryValidate(out error))
            {
                error = $"{contentId} 的登场描述无效：{error}";
                return false;
            }
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (attackVariants != null)
            {
                for (int i = 0; i < attackVariants.Count; i++)
                {
                    BossAnimationRuntimeAttackDescriptor variant =
                        attackVariants[i];
                    if (variant == null ||
                        string.IsNullOrWhiteSpace(variant.actionId) ||
                        !ids.Add(variant.actionId.Trim()) ||
                        variant.clip == null ||
                        !variant.clip.TryValidate(out error))
                    {
                        error = $"{contentId} 的攻击变体[{i}]描述无效：{error}";
                        return false;
                    }
                }
            }
            var utilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (utilityVariants != null)
            {
                foreach (BossAnimationRuntimeUtilityDescriptor variant in utilityVariants)
                {
                    if (variant == null || string.IsNullOrWhiteSpace(variant.actionId) ||
                        !utilityIds.Add(variant.actionId.Trim()) ||
                        !BossUtilitySequenceContract.TryValidate(variant.actionId, variant.steps, out error))
                    {
                        error = $"{contentId}: invalid or duplicate Utility descriptor: {error}";
                        return false;
                    }
                    foreach (BossAnimationRuntimeUtilityStepDescriptor step in variant.steps)
                    {
                        if (step.clip == null || !step.clip.TryValidate(out error))
                        {
                            error = $"{contentId}: invalid Utility clip for {variant.actionId}: {error}";
                            return false;
                        }
                    }
                }
            }
            error = null;
            return true;
        }
    }

    /// <summary>
    /// BOSS 轻量清单索引与旧调用兼容缓存。生产战斗的分阶段加载、
    /// 切片与释放由 BossPresentationWarmupSession 唯一拥有；本类只向
    /// BossAnimationProfileCatalog 发布已完整准备的兼容读模型。
    /// </summary>
    public static class BossAnimationRuntimeCatalog
    {
        private static Dictionary<string, BossAnimationRuntimeManifest>
            _manifests;
        private static readonly Dictionary<string, BossAnimationProfile>
            LoadedProfiles = new Dictionary<string, BossAnimationProfile>(
                StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Loading =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Failures =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, BossEntranceRuntimePresentation>
            LoadedEntrances =
                new Dictionary<string, BossEntranceRuntimePresentation>(
                    StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> EntranceLoading =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> EntranceFailures =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetForPlaySession()
        {
            _manifests = null;
            LoadedProfiles.Clear();
            Loading.Clear();
            Failures.Clear();
            LoadedEntrances.Clear();
            EntranceLoading.Clear();
            EntranceFailures.Clear();
        }

        public static bool HasManifest(string contentId)
        {
            EnsureManifests();
            return !string.IsNullOrWhiteSpace(contentId) &&
                _manifests.ContainsKey(contentId);
        }

        public static bool TryGetManifest(
            string contentId,
            out BossAnimationRuntimeManifest manifest)
        {
            EnsureManifests();
            manifest = null;
            return !string.IsNullOrWhiteSpace(contentId) &&
                _manifests.TryGetValue(contentId, out manifest);
        }

        public static bool TryGetLoaded(
            string contentId,
            out BossAnimationProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(contentId) ||
                !LoadedProfiles.TryGetValue(contentId, out profile))
                return false;
            if (profile != null)
                return true;
            LoadedProfiles.Remove(contentId);
            return false;
        }

        /// <summary>
        /// Creates the lightweight runtime profile shell shared by staged and
        /// compatibility loaders. Resource references remain empty until the
        /// caller has completed the corresponding asynchronous stage.
        /// </summary>
        internal static BossAnimationProfile CreateRuntimeProfile(
            BossAnimationRuntimeManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            var profile = ScriptableObject.CreateInstance<
                BossAnimationProfile>();
            profile.hideFlags = HideFlags.DontSave;
            profile.name = manifest.contentId + " (Runtime)";
            profile.contentId = manifest.contentId;
            profile.actionPlaybackSpeed = manifest.actionPlaybackSpeed;
            profile.defendImpactNormalized =
                manifest.defendImpactNormalized;
            profile.groundAnchor = manifest.groundAnchor;
            profile.visualScale = manifest.visualScale;
            profile.idle = manifest.idle.CreateClip();
            profile.attack = manifest.attack.CreateClip();
            profile.defend = manifest.defend.CreateClip();
            profile.attackVariants = new List<BossAttackVariant>();
            for (int i = 0; i < (manifest.attackVariants?.Count ?? 0); i++)
            {
                BossAnimationRuntimeAttackDescriptor descriptor =
                    manifest.attackVariants[i];
                profile.attackVariants.Add(new BossAttackVariant
                {
                    actionId = descriptor.actionId,
                    clip = descriptor.clip.CreateClip(),
                    hitNormalized = descriptor.hitNormalized
                });
            }
            profile.utilityVariants = new List<BossUtilityVariant>();
            if (manifest.utilityVariants != null)
            {
                foreach (BossAnimationRuntimeUtilityDescriptor descriptor in manifest.utilityVariants)
                {
                    var variant = new BossUtilityVariant { actionId = descriptor.actionId };
                    foreach (BossAnimationRuntimeUtilityStepDescriptor step in descriptor.steps)
                        variant.steps.Add(step.CreateStep());
                    profile.utilityVariants.Add(variant);
                }
            }
            return profile;
        }

        /// <summary>
        /// Publishes a fully prepared profile for read-only compatibility
        /// consumers. The warmup session remains its lifetime owner and must
        /// release the exact same instance at encounter teardown.
        /// </summary>
        internal static bool PublishPreparedProfile(
            string contentId,
            BossAnimationProfile profile)
        {
            if (string.IsNullOrWhiteSpace(contentId) || profile == null)
                return false;
            if (LoadedProfiles.TryGetValue(
                    contentId,
                    out BossAnimationProfile existing) &&
                existing != null && !ReferenceEquals(existing, profile))
            {
                return false;
            }
            LoadedProfiles[contentId] = profile;
            return true;
        }

        internal static void ReleasePreparedProfile(
            string contentId,
            BossAnimationProfile profile)
        {
            if (string.IsNullOrWhiteSpace(contentId) || profile == null)
                return;
            if (LoadedProfiles.TryGetValue(
                    contentId,
                    out BossAnimationProfile existing) &&
                ReferenceEquals(existing, profile))
            {
                LoadedProfiles.Remove(contentId);
            }
        }

        /// <summary>
        /// 战斗场景在异步请求完成前被卸载时释放合并门禁，
        /// 避免下一次进入永久等待一条已停止的协程。
        /// </summary>
        public static void CancelPending(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                return;
            Loading.Remove(contentId);
            Failures.Remove(contentId);
            EntranceLoading.Remove(contentId);
            EntranceFailures.Remove(contentId);
        }

        public static bool TryGetLoadedEntrance(
            string contentId,
            out BossEntranceRuntimePresentation presentation)
        {
            presentation = null;
            return !string.IsNullOrWhiteSpace(contentId) &&
                LoadedEntrances.TryGetValue(contentId, out presentation) &&
                presentation != null && presentation.IsReady;
        }

        public static IEnumerator LoadEntranceAsync(
            string contentId,
            Action<BossEntranceRuntimePresentation> completed,
            Action<string> failed = null)
        {
            EnsureManifests();
            if (TryGetLoadedEntrance(contentId, out var cached))
            {
                completed?.Invoke(cached);
                yield break;
            }
            if (!_manifests.TryGetValue(
                    contentId ?? string.Empty,
                    out BossAnimationRuntimeManifest manifest) ||
                manifest.entrance == null ||
                !manifest.entrance.UsesSpiritConvergence)
            {
                completed?.Invoke(null);
                yield break;
            }
            if (EntranceLoading.Contains(contentId))
            {
                while (EntranceLoading.Contains(contentId))
                    yield return null;
                if (TryGetLoadedEntrance(contentId, out cached))
                    completed?.Invoke(cached);
                else
                    failed?.Invoke(EntranceFailures.TryGetValue(
                        contentId,
                        out string existingError)
                            ? existingError
                            : $"BOSS 登场素材加载失败：{contentId}。");
                yield break;
            }

            EntranceLoading.Add(contentId);
            EntranceFailures.Remove(contentId);
            BossEntranceRuntimeDescriptor descriptor = manifest.entrance;
            ResourceRequest request = Resources.LoadAsync<Texture2D>(
                descriptor.frameAtlasResourcePath);
            yield return request;
            Texture2D atlas = request.asset as Texture2D;
            EntranceLoading.Remove(contentId);
            if (atlas == null)
            {
                string error = "无法加载 BOSS 登场图集：" +
                               descriptor.frameAtlasResourcePath + "。";
                EntranceFailures[contentId] = error;
                failed?.Invoke(error);
                yield break;
            }

            var presentation = new BossEntranceRuntimePresentation(
                descriptor,
                atlas);
            LoadedEntrances[contentId] = presentation;
            completed?.Invoke(presentation);
        }

        public static IEnumerator LoadAsync(
            string contentId,
            Action<BossAnimationProfile> completed,
            Action<string> failed = null)
        {
            EnsureManifests();
            if (TryGetLoaded(contentId, out BossAnimationProfile cached))
            {
                completed?.Invoke(cached);
                yield break;
            }
            if (!_manifests.TryGetValue(
                    contentId ?? string.Empty,
                    out BossAnimationRuntimeManifest manifest))
            {
                failed?.Invoke($"找不到 BOSS 运行时清单：{contentId}。");
                yield break;
            }

            if (Loading.Contains(contentId))
            {
                while (Loading.Contains(contentId))
                    yield return null;
                if (TryGetLoaded(contentId, out cached))
                    completed?.Invoke(cached);
                else
                    failed?.Invoke(Failures.TryGetValue(contentId, out string e)
                        ? e
                        : $"BOSS 素材加载失败：{contentId}。");
                yield break;
            }

            Loading.Add(contentId);
            Failures.Remove(contentId);
            BossAnimationProfile profile = CreateRuntimeProfile(manifest);

            string error = null;
            yield return LoadClipAsync(manifest.idle, profile.idle,
                value => error = value);
            yield return null;
            if (error == null)
                yield return LoadClipAsync(manifest.attack, profile.attack,
                    value => error = value);
            yield return null;
            if (error == null)
                yield return LoadClipAsync(manifest.defend, profile.defend,
                    value => error = value);
            if (error == null && manifest.attackVariants != null)
            {
                for (int i = 0; i < manifest.attackVariants.Count; i++)
                {
                    yield return null;
                    int index = i;
                    yield return LoadClipAsync(
                        manifest.attackVariants[index].clip,
                        profile.attackVariants[index].clip,
                        value => error = value);
                    if (error != null)
                        break;
                }
            }

            if (error == null && manifest.utilityVariants != null)
            {
                for (int i = 0; i < manifest.utilityVariants.Count && error == null; i++)
                {
                    BossAnimationRuntimeUtilityDescriptor variant = manifest.utilityVariants[i];
                    for (int j = 0; j < variant.steps.Count && error == null; j++)
                    {
                        yield return null;
                        yield return LoadClipAsync(variant.steps[j].clip,
                            profile.utilityVariants[i].steps[j].clip,
                            value => error = value);
                    }
                }
            }

            if (error == null && !profile.TryValidate(out error))
                error = $"BOSS 运行时素材无效：{error}";

            Loading.Remove(contentId);
            if (error != null)
            {
                Failures[contentId] = error;
                UnityEngine.Object.Destroy(profile);
                failed?.Invoke(error);
                yield break;
            }

            LoadedProfiles[contentId] = profile;
            completed?.Invoke(profile);
        }

        private static IEnumerator LoadClipAsync(
            BossAnimationRuntimeClipDescriptor descriptor,
            BossAnimationClip target,
            Action<string> setError)
        {
            ResourceRequest atlasRequest = Resources.LoadAsync<Texture2D>(
                descriptor.frameAtlasResourcePath);
            yield return atlasRequest;
            target.frameAtlas = atlasRequest.asset as Texture2D;
            if (target.frameAtlas == null)
            {
                setError?.Invoke(
                    $"无法加载 BOSS 图集：{descriptor.frameAtlasResourcePath}。");
                yield break;
            }

            // A validated single-frame neutral segment (Idle/Defend) may omit audio.
            if (descriptor.frameCount == 1 && string.IsNullOrWhiteSpace(descriptor.audioResourcePath))
                yield break;
            ResourceRequest audioRequest = Resources.LoadAsync<AudioClip>(
                descriptor.audioResourcePath);
            yield return audioRequest;
            target.audioClip = audioRequest.asset as AudioClip;
            if (target.audioClip == null)
            {
                setError?.Invoke(
                    $"无法加载 BOSS 音频：{descriptor.audioResourcePath}。");
            }
        }

        private static void EnsureManifests()
        {
            if (_manifests != null)
                return;
            _manifests = new Dictionary<string, BossAnimationRuntimeManifest>(
                StringComparer.OrdinalIgnoreCase);
            BossAnimationRuntimeManifest[] manifests =
                Resources.LoadAll<BossAnimationRuntimeManifest>(
                    BossAnimationRuntimeManifest.DefaultResourceRoot);
            for (int i = 0; i < manifests.Length; i++)
            {
                BossAnimationRuntimeManifest manifest = manifests[i];
                if (manifest == null)
                    throw new InvalidOperationException(
                        "BOSS 运行时清单资产为空。");
                if (!manifest.TryValidate(out string error))
                    throw new InvalidOperationException(error);
                if (!_manifests.TryAdd(manifest.contentId, manifest))
                {
                    throw new InvalidOperationException(
                        $"BOSS 运行时清单 contentId 重复：{manifest.contentId}。");
                }
            }
        }
    }
}
