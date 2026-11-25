using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI.Battle
{
    /// <summary>一段 BOSS 序列帧动画的图集与播放参数。</summary>
    [Serializable]
    public sealed class BossAnimationClip
    {
        [Tooltip("行优先、左上起始、整除网格的序列图集")]
        public Texture2D frameAtlas;
        [Min(1)] public int frameCount = 48;
        [Min(1)] public int atlasColumns = 8;
        [Min(1)] public int atlasRows = 6;
        [Min(0.1f)] public float framesPerSecond = 12f;
        [Tooltip("与动画同源提取的视频音轨；待机为循环音，动作为一次性整段")]
        public AudioClip audioClip;
        [Range(0f, 1f)] public float audioVolume = 1f;

        public bool IsValid => frameAtlas != null && frameCount > 0 &&
                               atlasColumns > 0 && atlasRows > 0 &&
                               frameCount <= atlasColumns * atlasRows;
    }

    /// <summary>
    /// 一个配置化的 BOSS 攻击变体。actionId 来自 Core 的敌人行为配置，
    /// 因此表现层不需要读取显示名称或依赖攻击条目的排列顺序。
    /// </summary>
    [Serializable]
    public sealed class BossAttackVariant
    {
        [Tooltip("稳定招式 ID，例如 attack.yinjiangjun.spinning_sweep_kick")]
        public string actionId;
        public BossAnimationClip clip = new BossAnimationClip();
        [Range(0f, 1f)]
        [Tooltip("规则命中回调在该动作时长中的归一化位置")]
        public float hitNormalized = 0.46f;
    }

    /// <summary>
    /// BOSS 战场序列动画配置。以 contentId 为主键关联敌人；待机和守势
    /// 仍为固定段，Attack 可按稳定 actionId 选择独立的图集、音频和命中帧。
    /// 图集由 SourceAssets 抠像脚本 + build_character_sequence_atlas.py 生成。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewBossAnimationProfile",
        menuName = "万界长卷/战斗/BOSS序列动画配置")]
    public sealed class BossAnimationProfile : ScriptableObject
    {
        public const string DefaultResourceRoot = "BossAnimations";

        [Tooltip("稳定敌人 contentId；不得使用显示名称关联")]
        public string contentId;

        [Header("统一动作节奏")]
        [Tooltip("Attack 与 Defend 共用的播放倍率；待机保持素材源速度。")]
        [Range(
            BossAnimationTiming.MinimumActionPlaybackSpeed,
            BossAnimationTiming.MaximumActionPlaybackSpeed)]
        public float actionPlaybackSpeed =
            BossAnimationTiming.DefaultActionPlaybackSpeed;

        [Tooltip("Defend 规则结算与受力特效在源动作时长中的归一化峰值")]
        [Range(0f, 1f)]
        public float defendImpactNormalized =
            BossAnimationTiming.DefaultDefendImpactNormalized;

        [Header("动作段")]
        public BossAnimationClip idle = new BossAnimationClip();
        public BossAnimationClip attack = new BossAnimationClip();
        public BossAnimationClip defend = new BossAnimationClip();

        [Header("Attack 变体")]
        [Tooltip("按稳定 actionId 选择的攻击变体；空 ID 或未知 ID 回退 attack。")]
        public List<BossAttackVariant> attackVariants =
            new List<BossAttackVariant>();

        [Header("Utility variants (stable actionId)")]
        public List<BossUtilityVariant> utilityVariants = new List<BossUtilityVariant>();

        // 旧资产兼容回退。正式位置与大小统一由
        // EnemyAreaPresentationProfile/怪物素材工作台维护。
        [HideInInspector]
        [Tooltip("仅在敌人区域配置缺少该 contentId 时使用的旧版落点回退")]
        [Range(0f, 1f)] public Vector2 groundAnchor = new Vector2(0.5f, 0.05f);
        [HideInInspector]
        [Min(0.1f)] public float visualScale = 1f;

        public bool TryValidate(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(contentId))
            {
                error = "BOSS 动画配置缺少 contentId。";
                return false;
            }

            if (actionPlaybackSpeed < 0f ||
                actionPlaybackSpeed >
                BossAnimationTiming.MaximumActionPlaybackSpeed ||
                float.IsNaN(actionPlaybackSpeed) ||
                float.IsInfinity(actionPlaybackSpeed))
            {
                error = $"{contentId} 的统一动作倍率无效。";
                return false;
            }

            if (!BossAnimationTiming.IsValidNormalizedTime(
                    defendImpactNormalized))
            {
                error = $"{contentId} 的 Defend 受力峰值必须在 0 到 1 之间。";
                return false;
            }

            if (!idle.IsValid || !attack.IsValid || !defend.IsValid)
            {
                error = $"{contentId} 的动作段配置不完整（图集/网格/帧数）。";
                return false;
            }

            // A static neutral segment can hold the current body for Idle or
            // Defend without a fabricated audio track. Animated clips and all
            // attacks still require their own audio.
            if ((idle.audioClip == null && idle.frameCount != 1) || attack.audioClip == null ||
                (defend.audioClip == null && defend.frameCount != 1))
            {
                error = $"{contentId} 的动作段配置不完整（音频）。";
                return false;
            }

            if (!IsGridAligned(idle) || !IsGridAligned(attack) ||
                !IsGridAligned(defend))
            {
                error = $"{contentId} 存在图集尺寸不能被网格整除的动作段。";
                return false;
            }

            var variantIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (attackVariants != null)
            {
                for (int i = 0; i < attackVariants.Count; i++)
                {
                    BossAttackVariant variant = attackVariants[i];
                    if (variant == null ||
                        string.IsNullOrWhiteSpace(variant.actionId))
                    {
                        error = $"{contentId}.attackVariants[{i}] 缺少 actionId。";
                        return false;
                    }
                    if (!variantIds.Add(variant.actionId.Trim()))
                    {
                        error = $"{contentId} 的 Attack actionId 重复：" +
                            variant.actionId;
                        return false;
                    }
                    if (variant.clip == null || !variant.clip.IsValid ||
                        variant.clip.audioClip == null)
                    {
                        error = $"{contentId} 的 Attack 变体 " +
                            $"{variant.actionId} 配置不完整（图集/音频）。";
                        return false;
                    }
                    if (!IsGridAligned(variant.clip))
                    {
                        error = $"{contentId} 的 Attack 变体 " +
                            $"{variant.actionId} 图集尺寸不能被网格整除。";
                        return false;
                    }
                    if (variant.hitNormalized < 0f ||
                        variant.hitNormalized > 1f)
                    {
                        error = $"{contentId} 的 Attack 变体 " +
                            $"{variant.actionId} hitNormalized 必须在 0 到 1 之间。";
                        return false;
                    }
                }
            }

            var utilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (utilityVariants != null)
            {
                foreach (BossUtilityVariant variant in utilityVariants)
                {
                    if (variant == null || string.IsNullOrWhiteSpace(variant.actionId) ||
                        !utilityIds.Add(variant.actionId.Trim()) ||
                        !BossUtilitySequenceContract.TryValidate(variant.actionId, variant.steps, out error))
                    {
                        error = $"{contentId}: invalid or duplicate Utility variant: {error}";
                        return false;
                    }
                    foreach (BossUtilityStep step in variant.steps)
                    {
                        if (step.clip == null || !step.clip.IsValid || step.clip.audioClip == null ||
                            !IsGridAligned(step.clip))
                        {
                            error = $"{contentId}: incomplete Utility segment for {variant.actionId}.";
                            return false;
                        }
                    }
                }
            }
            error = null;
            return true;
        }

        public bool TryGetUtilityVariant(string actionId, out BossUtilityVariant variant)
        {
            variant = null;
            if (string.IsNullOrWhiteSpace(actionId) || utilityVariants == null) return false;
            string normalized = actionId.Trim();
            for (int i = 0; i < utilityVariants.Count; i++)
            {
                BossUtilityVariant candidate = utilityVariants[i];
                if (candidate != null && string.Equals(candidate.actionId?.Trim(), normalized,
                    StringComparison.OrdinalIgnoreCase))
                {
                    variant = candidate;
                    return candidate.steps != null && candidate.steps.Count > 0;
                }
            }
            return false;
        }

        /// <summary>按稳定 actionId 查找精确攻击变体，不执行回退。</summary>
        public bool TryGetAttackVariant(
            string actionId,
            out BossAnimationClip clip,
            out float hitNormalized)
        {
            clip = null;
            hitNormalized = 0.46f;
            if (string.IsNullOrWhiteSpace(actionId) ||
                attackVariants == null)
                return false;

            string normalized = actionId.Trim();
            for (int i = 0; i < attackVariants.Count; i++)
            {
                BossAttackVariant variant = attackVariants[i];
                if (variant == null ||
                    !string.Equals(
                        variant.actionId?.Trim(),
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                clip = variant.clip;
                hitNormalized = Mathf.Clamp01(variant.hitNormalized);
                return clip != null;
            }

            return false;
        }

        /// <summary>
        /// 解析攻击图集；空 ID 或未知 ID 使用旧 attack 段并保持旧命中点。
        /// </summary>
        public BossAnimationClip ResolveAttackClip(
            string actionId,
            out float hitNormalized)
        {
            if (TryGetAttackVariant(
                    actionId,
                    out BossAnimationClip variant,
                    out hitNormalized))
                return variant;

            hitNormalized = 0.46f;
            return attack;
        }

        private bool IsGridAligned(BossAnimationClip clip)
        {
            return clip.frameAtlas.width % clip.atlasColumns == 0 &&
                   clip.frameAtlas.height % clip.atlasRows == 0;
        }
    }

    /// <summary>按稳定敌人 ID 解析 BOSS 序列动画配置。</summary>
    public static class BossAnimationProfileCatalog
    {
        private const string ResourceRoot = BossAnimationProfile.DefaultResourceRoot;
        private static Dictionary<string, BossAnimationProfile> _profiles;

        public static bool TryGet(
            string contentId,
            out BossAnimationProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(contentId))
                return false;

            // 运行时绝不同步加载强引用了大图集的审核资产。
            // 战斗只能取已由轻量清单异步准备完成的运行时实例。
            if (Application.isPlaying)
            {
                return BossAnimationRuntimeCatalog.TryGetLoaded(
                    contentId,
                    out profile);
            }

            EnsureLoaded();
            return _profiles.TryGetValue(contentId, out profile);
        }

        /// <summary>
        /// 该敌人是否拥有关卡 BOSS 级序列帧素材。砸落入场、BOSS 音乐、
        /// 全屏斩击等关卡 BOSS 表现只对有素材的 BOSS 生效；层 BOSS
        /// （如盘蛇妖）没有素材，按普通怪物方式出场。
        /// </summary>
        public static bool HasBossAnimation(string contentId)
            => Application.isPlaying
                ? BossAnimationRuntimeCatalog.HasManifest(contentId)
                : TryGet(contentId, out _);

        public static void ClearCache()
        {
            _profiles = null;
        }

        private static void EnsureLoaded()
        {
            if (_profiles != null)
                return;

            _profiles = new Dictionary<string, BossAnimationProfile>(
                StringComparer.OrdinalIgnoreCase);
            foreach (BossAnimationProfile profile in
                     Resources.LoadAll<BossAnimationProfile>(ResourceRoot))
            {
                if (profile == null)
                    continue;
                if (!profile.TryValidate(out string error))
                    throw new InvalidOperationException(error);
                if (!_profiles.TryAdd(profile.contentId, profile))
                {
                    throw new InvalidOperationException(
                        $"BOSS 动画 contentId 重复：{profile.contentId}。");
                }
            }
        }
    }
}
