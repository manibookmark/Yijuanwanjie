using System;
using UnityEngine;

namespace WanjieScroll.Effects
{
    public enum BossEntranceEffectMode
    {
        ImpactDrop = 0,
        SpiritConvergence = 1,
        WaterConvergence = 2
    }

    /// <summary>
    /// 所有关卡 BOSS 共用的砸落登场预设。角色身份和战斗时机由外部决定；
    /// 本资产只拥有坠落曲线与柔边尘粒的可保存参数。
    /// </summary>
    [CreateAssetMenu(
        fileName = "stage-drop",
        menuName = "万界长卷/特效/BOSS 登场预设")]
    public sealed class BossEntranceEffectPreset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 5;
        public const string DefaultResourcePath =
            "BossEntranceEffects/stage-drop";
        public const float RecommendedConvergenceDuration = 1.22f;
        public const int RecommendedConvergenceParticleCount = 256;
        public const float RecommendedConvergenceParticleSize = 1.52f;
        public const float RecommendedConvergenceRadius = 1.10f;
        public const float RecommendedConvergenceOpacity = 0.96f;
        public const float RecommendedConvergenceRevealNormalized = 0.76f;
        public const float RecommendedConvergenceSmokeStretch = 1.80f;
        public const float RecommendedConvergenceSmokeCurl = 0.50f;
        public const float RecommendedConvergenceSmokeSoftness = 0.75f;

        [Header("身份")]
        public int schemaVersion = CurrentSchemaVersion;
        public string effectId = "effect.boss.stage_drop.v1";

        [Header("坠落")]
        [Range(0.45f, 1.8f)] public float duration = 1.32f;
        [Range(0.45f, 0.9f)] public float landingNormalized = 0.58f;
        [Range(1f, 1.35f)] public float startScale = 1.12f;
        [Tooltip("起点高于落点的距离，以角色自身高度为单位。")]
        [Range(0.08f, 0.45f)] public float dropDistanceInBossHeights = 0.22f;
        [Tooltip("落地压缩姿态短暂停留，形成有重量的定帧。")]
        [Range(0f, 0.12f)] public float impactHoldDuration = 0.05f;

        [Header("落地柔边粒子")]
        [Range(0, 320)] public int particleCount = 176;
        [Tooltip("相对基础粒径的倍率；1 对应约 3–6 像素，取决于战斗分辨率。")]
        [Range(0.5f, 3f)] public float particleSize = 1.55f;
        [Range(0.5f, 2f)] public float particleSpread = 1.2f;
        [Range(0.5f, 2f)] public float particleSpeed = 0.92f;
        [Range(0.2f, 1f)] public float particleOpacity = 0.72f;
        [Range(0.25f, 1.5f)] public float particleLifetime = 0.95f;

        [Header("落地震散顶部云层")]
        [Range(0.08f, 0.6f)] public float cloudDisperseDuration = 0.18f;
        [Tooltip("两半云层分别向外移动的距离，相对云层自身宽度。")]
        [Range(0.15f, 1.2f)] public float cloudDisperseDistance = 0.62f;
        [Tooltip("灵气汇聚模式下，整片顶部云向上飘出的自身高度比例。")]
        [Range(0.1f, 0.8f)] public float cloudRiseDistance = 0.38f;

        [Header("落地镜头冲击")]
        [Range(0.05f, 0.5f)] public float impactShakeDuration = 0.24f;
        [Range(0.02f, 0.5f)] public float impactShakeIntensity = 0.22f;

        [Header("灵气汇聚")]
        [Range(0.45f, 1.5f)] public float convergenceDuration =
            RecommendedConvergenceDuration;
        [Range(32, 320)] public int convergenceParticleCount =
            RecommendedConvergenceParticleCount;
        [Range(0.5f, 3f)] public float convergenceParticleSize =
            RecommendedConvergenceParticleSize;
        [Range(0.8f, 2.5f)] public float convergenceRadius =
            RecommendedConvergenceRadius;
        [Range(0.2f, 1f)] public float convergenceOpacity =
            RecommendedConvergenceOpacity;
        [Range(0.5f, 0.92f)] public float convergenceRevealNormalized =
            RecommendedConvergenceRevealNormalized;
        [Tooltip("断续灵气丝自身的舒展比例；过大会重新形成鞭状长条。")]
        [Range(1.4f, 3.6f)] public float convergenceSmokeStretch =
            RecommendedConvergenceSmokeStretch;
        [Tooltip("松散雾域的螺旋卷入强度；主方向始终汇向中心灵核。")]
        [Range(0f, 1f)] public float convergenceSmokeCurl =
            RecommendedConvergenceSmokeCurl;
        [Range(0.35f, 0.88f)] public float convergenceSmokeSoftness =
            RecommendedConvergenceSmokeSoftness;

        public float LandingTime =>
            Mathf.Max(0.01f, duration) *
            Mathf.Clamp(landingNormalized, 0.05f, 0.95f);

        [Header("水流汇聚")]
        [Range(0.8f, 3f)] public float waterDuration = 1.8f;
        [Range(0.5f, 1.5f)] public float waterRadius = 1f;
        [Range(0.1f, 1f)] public float waterOpacity = 0.82f;

        public void RestoreRecommendedSpiritConvergence()
        {
            convergenceDuration = RecommendedConvergenceDuration;
            convergenceParticleCount = RecommendedConvergenceParticleCount;
            convergenceParticleSize = RecommendedConvergenceParticleSize;
            convergenceRadius = RecommendedConvergenceRadius;
            convergenceOpacity = RecommendedConvergenceOpacity;
            convergenceRevealNormalized =
                RecommendedConvergenceRevealNormalized;
            convergenceSmokeStretch = RecommendedConvergenceSmokeStretch;
            convergenceSmokeCurl = RecommendedConvergenceSmokeCurl;
            convergenceSmokeSoftness = RecommendedConvergenceSmokeSoftness;
        }

        public bool TryValidate(out string error)
        {
            if (schemaVersion != CurrentSchemaVersion)
            {
                error = $"{name} schemaVersion 必须为 " +
                        $"{CurrentSchemaVersion}。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(effectId))
            {
                error = $"{name} 缺少稳定 effectId。";
                return false;
            }
            if (duration <= 0f || landingNormalized <= 0f ||
                landingNormalized >= 1f || startScale < 1f ||
                dropDistanceInBossHeights <= 0f ||
                impactHoldDuration < 0f)
            {
                error = $"{effectId} 的坠落参数无效。";
                return false;
            }
            if (particleCount < 0 || particleCount > 320 ||
                particleSize <= 0f ||
                particleSpread <= 0f || particleSpeed <= 0f ||
                particleOpacity <= 0f || particleLifetime <= 0f ||
                cloudDisperseDuration <= 0f || cloudDisperseDistance <= 0f ||
                cloudRiseDistance <= 0f ||
                impactShakeDuration <= 0f || impactShakeIntensity <= 0f)
            {
                error = $"{effectId} 的落地粒子参数无效。";
                return false;
            }
            if (convergenceDuration <= 0f ||
                convergenceParticleCount < 32 ||
                convergenceParticleCount > 320 ||
                convergenceParticleSize <= 0f || convergenceRadius <= 0f ||
                convergenceOpacity <= 0f ||
                convergenceRevealNormalized <= 0f ||
                convergenceRevealNormalized >= 1f ||
                convergenceSmokeStretch < 1f ||
                convergenceSmokeCurl < 0f || convergenceSmokeCurl > 1f ||
                convergenceSmokeSoftness <= 0f ||
                convergenceSmokeSoftness >= 1f)
            {
                error = $"{effectId} 的灵气汇聚参数无效。";
                return false;
            }

            error = null;
            if (waterDuration <= 0f || waterRadius <= 0f ||
                waterOpacity <= 0f || waterOpacity > 1f)
            {
                error = $"{effectId} 的水流汇聚参数无效。";
                return false;
            }
            return true;
        }
    }

    /// <summary>同一登场时点的无状态采样结果。</summary>
    public readonly struct BossEntranceEffectSample
    {
        public BossEntranceEffectSample(
            bool active,
            bool landingReached,
            float positionProgress,
            Vector2 scale,
            float bodyAlpha,
            float inkEchoAlpha,
            float cinnabarEchoAlpha,
            float rotationDegrees,
            float dustProgress,
            float cloudDisperseProgress)
        {
            Active = active;
            LandingReached = landingReached;
            PositionProgress = positionProgress;
            Scale = scale;
            BodyAlpha = bodyAlpha;
            InkEchoAlpha = inkEchoAlpha;
            CinnabarEchoAlpha = cinnabarEchoAlpha;
            RotationDegrees = rotationDegrees;
            DustProgress = dustProgress;
            CloudDisperseProgress = cloudDisperseProgress;
        }

        public bool Active { get; }
        public bool LandingReached { get; }
        public float PositionProgress { get; }
        public Vector2 Scale { get; }
        public float BodyAlpha { get; }
        public float InkEchoAlpha { get; }
        public float CinnabarEchoAlpha { get; }
        public float RotationDegrees { get; }
        /// <summary>落地前为 -1，落地后为粒子生命周期中的 0–1。</summary>
        public float DustProgress { get; }
        /// <summary>落地前为 -1；落地后 0–1，达到 1 时顶部云层完全消失。</summary>
        public float CloudDisperseProgress { get; }
    }

    /// <summary>真实战斗与 F10 共用的纯登场曲线。</summary>
    public static class BossEntranceEffectSampler
    {
        public static BossEntranceEffectSample Sample(
            BossEntranceEffectPreset preset,
            float elapsedSeconds)
            => Sample(preset, elapsedSeconds, BossEntranceEffectMode.ImpactDrop);

        public static BossEntranceEffectSample Sample(
            BossEntranceEffectPreset preset,
            float elapsedSeconds,
            BossEntranceEffectMode mode)
        {
            if (preset == null || elapsedSeconds < 0f)
                return default;

            if (mode == BossEntranceEffectMode.SpiritConvergence)
                return SampleConvergence(preset, elapsedSeconds);
            if (mode == BossEntranceEffectMode.WaterConvergence)
            {
                float progress = Mathf.Clamp01(elapsedSeconds /
                    Mathf.Max(0.01f, preset.waterDuration));
                return new BossEntranceEffectSample(
                    elapsedSeconds < preset.waterDuration,
                    progress >= 0.68f,
                    1f, Vector2.one,
                    Smooth01(Mathf.InverseLerp(0.28f, 0.9f, progress)),
                    0f, 0f, 0f, -1f,
                    progress >= 0.68f
                        ? Mathf.InverseLerp(0.68f, 1f, progress) : -1f);
            }

            float duration = Mathf.Max(0.01f, preset.duration);
            float landingTime = Mathf.Clamp(
                preset.LandingTime,
                0.01f,
                duration - 0.01f);
            float elapsed = Mathf.Clamp(elapsedSeconds, 0f, duration);
            float landingProgress = Mathf.Clamp01(elapsed / landingTime);
            float positionProgress = Mathf.Pow(landingProgress, 1.8f);
            float scaleX;
            float scaleY;
            float settleDuration = Mathf.Max(0.01f, duration - landingTime);
            if (elapsed < landingTime)
            {
                float shrink = Smooth01(landingProgress);
                scaleX = Mathf.Lerp(preset.startScale, 1.02f, shrink);
                scaleY = scaleX;
            }
            else
            {
                float sinceLanding = elapsed - landingTime;
                float holdDuration = Mathf.Min(
                    preset.impactHoldDuration,
                    settleDuration * 0.65f);
                float recover = Smooth01(Mathf.InverseLerp(
                    holdDuration,
                    settleDuration,
                    sinceLanding));
                scaleX = Mathf.Lerp(1.035f, 1f, recover);
                scaleY = Mathf.Lerp(0.955f, 1f, recover);
            }

            float rotationDegrees = 0f;
            float bodyAlpha = Mathf.Lerp(
                0.12f,
                1f,
                Smooth01(Mathf.InverseLerp(
                    0f,
                    duration * 0.38f,
                    elapsed)));
            float inkAlpha = 0f;
            float cinnabarAlpha = 0f;
            bool landingReached = elapsedSeconds >= landingTime;
            float dustProgress = landingReached
                ? Mathf.Clamp01(
                    (elapsedSeconds - landingTime) /
                    Mathf.Max(0.01f, preset.particleLifetime))
                : -1f;
            float cloudDisperseProgress = landingReached
                ? Mathf.Clamp01(
                    (elapsedSeconds - landingTime) /
                    Mathf.Max(0.01f, preset.cloudDisperseDuration))
                : -1f;

            return new BossEntranceEffectSample(
                elapsedSeconds < duration,
                landingReached,
                positionProgress,
                new Vector2(scaleX, scaleY),
                bodyAlpha,
                inkAlpha,
                cinnabarAlpha,
                rotationDegrees,
                dustProgress,
                cloudDisperseProgress);
        }

        private static BossEntranceEffectSample SampleConvergence(
            BossEntranceEffectPreset preset,
            float elapsedSeconds)
        {
            float duration = Mathf.Max(0.01f, preset.convergenceDuration);
            float progress = Mathf.Clamp01(elapsedSeconds / duration);
            float revealStart = Mathf.Clamp(
                preset.convergenceRevealNormalized,
                0.5f,
                0.92f);
            float revealEnd = Mathf.Min(1f, revealStart + 0.18f);
            float reveal = Smooth01(Mathf.InverseLerp(
                revealStart,
                revealEnd,
                progress));
            bool revealed = progress >= preset.convergenceRevealNormalized;
            float revealTime = duration *
                               preset.convergenceRevealNormalized;
            float cloudProgress = revealed
                ? Mathf.Clamp01(
                    (elapsedSeconds - revealTime) /
                    Mathf.Max(0.38f, preset.cloudDisperseDuration))
                : -1f;
            return new BossEntranceEffectSample(
                elapsedSeconds < duration,
                revealed,
                1f,
                Vector2.one,
                reveal,
                0f,
                0f,
                0f,
                progress,
                cloudProgress);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }

    /// <summary>登场落地尘的确定性初始状态；运行时与 F10 共用。</summary>
    public readonly struct BossEntranceParticleSeed
    {
        public BossEntranceParticleSeed(
            Vector2 positionUnit,
            Vector2 velocityUnit,
            float lifetime,
            float sizeMultiplier,
            float rotation,
            Color color)
        {
            PositionUnit = positionUnit;
            VelocityUnit = velocityUnit;
            Lifetime = lifetime;
            SizeMultiplier = sizeMultiplier;
            Rotation = rotation;
            Color = color;
        }

        public Vector2 PositionUnit { get; }
        public Vector2 VelocityUnit { get; }
        public float Lifetime { get; }
        public float SizeMultiplier { get; }
        public float Rotation { get; }
        public Color Color { get; }
    }

    /// <summary>
    /// 用散列而非等距编号生成落地尘，避免“按顺序向两边排开”的机械感。
    /// Position/Velocity 单位由调用方的角色脚印宽度与高度换算。
    /// </summary>
    public static class BossEntranceParticlePattern
    {
        public static BossEntranceParticleSeed GetSeed(
            BossEntranceEffectPreset preset,
            int index)
        {
            float n1 = Hash01(index, 0.137f);
            float n2 = Hash01(index, 0.571f);
            float n3 = Hash01(index, 0.913f);
            float n4 = Hash01(index, 0.337f);
            float side = n1 < 0.5f ? -1f : 1f;
            Vector2 position = new Vector2(
                (n2 - 0.5f) * 0.92f,
                n3 * 0.018f);
            Vector2 velocity = new Vector2(
                side * Mathf.Lerp(0.68f, 1.48f, n2) *
                preset.particleSpread * preset.particleSpeed,
                Mathf.Lerp(0.035f, 0.22f, n3) *
                preset.particleSpeed);
            float lifetime = preset.particleLifetime *
                             Mathf.Lerp(0.72f, 1.18f, n1);
            bool goldAccent = n4 > 0.9f;
            float size = preset.particleSize *
                         Mathf.Lerp(0.62f, 1.62f, n2) *
                         (goldAccent ? 0.5f : 1f);
            Color color = goldAccent
                ? new Color(0.72f, 0.46f, 0.16f, 0.78f)
                : n4 > 0.62f
                    ? new Color(0.28f, 0.18f, 0.10f, 0.68f)
                    : new Color(0.095f, 0.072f, 0.055f, 0.74f);
            return new BossEntranceParticleSeed(
                position,
                velocity,
                lifetime,
                size,
                n3 * 360f,
                color);
        }

        private static float Hash01(int index, float salt)
        {
            return Mathf.Repeat(
                Mathf.Sin((index + 1) * (12.9898f + salt * 41.731f)) *
                43758.5453f,
                1f);
        }
    }

    /// <summary>关卡 BOSS 默认登场预设的单一资源入口。</summary>
    public static class BossEntranceEffectPresetCatalog
    {
        private static BossEntranceEffectPreset _default;

        public static bool TryGetDefault(
            out BossEntranceEffectPreset preset,
            out string error)
        {
            _default ??= Resources.Load<BossEntranceEffectPreset>(
                BossEntranceEffectPreset.DefaultResourcePath);
            preset = _default;
            if (preset == null)
            {
                error = "缺少关卡 BOSS 默认登场预设：" +
                        BossEntranceEffectPreset.DefaultResourcePath;
                return false;
            }
            if (!preset.TryValidate(out error))
            {
                preset = null;
                return false;
            }

            error = null;
            return true;
        }

        public static void ClearCache()
        {
            _default = null;
        }
    }
}
