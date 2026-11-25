using System;
using UnityEngine;

namespace WanjieScroll.Effects
{
    public enum BossActionEffectUsage
    {
        Attack = 0,
        Defend = 1
    }

    public enum BossActionEffectVisualKind
    {
        HeavyPunchImpact = 0,
        HorizontalSweep = 1,
        ArmorCompress = 2
    }

    public enum BossScreenMotionKind
    {
        None = 0,
        ImpactJolt = 1,
        HorizontalSway = 2
    }

    /// <summary>
    /// 可被多个 BOSS 复用的一次性动作特效预设。它拥有视觉语言与屏幕反馈
    /// 参数，但不知道哪个敌人或招式会使用自己。文件虽已进入复杂度审查区间，
    /// 但下方采样器仍是同一份无状态、确定性的时间与几何合同；新增视觉种类时
    /// 再把几何计划抽成独立深模块，避免当前阶段制造透传层。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewBossActionEffectPreset",
        menuName = "万界长卷/特效/BOSS 动作特效预设")]
    public sealed class BossActionEffectPreset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [Header("身份")]
        public int schemaVersion = CurrentSchemaVersion;
        [Tooltip("稳定效果 ID；多个 BOSS 可引用同一预设")]
        public string effectId = "effect.boss.new.v1";
        public string displayName = "新 BOSS 特效";
        public BossActionEffectUsage usage = BossActionEffectUsage.Attack;
        public BossActionEffectVisualKind visualKind =
            BossActionEffectVisualKind.HeavyPunchImpact;

        [Header("视觉")]
        [Min(0.05f)] public float duration = 0.2f;
        [Range(0.25f, 2.5f)] public float visualScale = 1f;
        [Range(0, 160)] public int particleCount = 48;
        public Color primaryColor =
            new Color(0.95f, 0.82f, 0.55f, 0.95f);
        public Color coreColor =
            new Color(1f, 0.96f, 0.82f, 1f);
        public Color debrisColor =
            new Color(0.12f, 0.09f, 0.08f, 0.9f);

        [Header("屏幕反馈")]
        public BossScreenMotionKind screenMotion =
            BossScreenMotionKind.None;
        [Min(0.01f)] public float screenMotionDuration = 0.16f;
        [Tooltip("以屏幕高度为基准的振幅比例；0.005 约等于 1080p 下 5.4 像素")]
        [Range(0f, 0.08f)] public float screenMotionAmplitude = 0.008f;

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
            if (string.IsNullOrWhiteSpace(displayName))
            {
                error = $"{effectId} 缺少显示名称。";
                return false;
            }
            if (!Enum.IsDefined(typeof(BossActionEffectUsage), usage) ||
                !Enum.IsDefined(
                    typeof(BossActionEffectVisualKind), visualKind) ||
                !Enum.IsDefined(
                    typeof(BossScreenMotionKind), screenMotion))
            {
                error = $"{effectId} 包含无效枚举值。";
                return false;
            }
            if (duration <= 0f || visualScale <= 0f ||
                particleCount < 0 || particleCount > 160)
            {
                error = $"{effectId} 的视觉参数必须为有效正值。";
                return false;
            }
            if (screenMotion != BossScreenMotionKind.None &&
                (screenMotionDuration <= 0f ||
                 screenMotionAmplitude <= 0f))
            {
                error = $"{effectId} 的屏幕反馈时长与振幅必须大于 0。";
                return false;
            }
            if (usage == BossActionEffectUsage.Defend &&
                screenMotion != BossScreenMotionKind.None)
            {
                error = $"{effectId} 是防御预设，不应移动整个屏幕。";
                return false;
            }

            error = null;
            return true;
        }
    }

    /// <summary>
    /// 同一特效时间点的确定性采样结果。运行时和 F10 分别负责世界空间与
    /// IMGUI 绘制，但共同消费这里的时钟、显隐、缩放和屏幕运动曲线。
    /// </summary>
    public readonly struct BossActionEffectSample
    {
        public BossActionEffectSample(
            bool active,
            bool visualVisible,
            float progress,
            float reveal,
            float alpha,
            float scale,
            Vector2 screenOffsetUnit)
        {
            Active = active;
            VisualVisible = visualVisible;
            Progress = progress;
            Reveal = reveal;
            Alpha = alpha;
            Scale = scale;
            ScreenOffsetUnit = screenOffsetUnit;
        }

        public bool Active { get; }
        public bool VisualVisible { get; }
        public float Progress { get; }
        public float Reveal { get; }
        public float Alpha { get; }
        public float Scale { get; }
        /// <summary>单位振幅下的相机偏移，最终振幅由预设和视口换算。</summary>
        public Vector2 ScreenOffsetUnit { get; }
    }

    /// <summary>
    /// BOSS 一次性效果的纯采样器。它不持有 GameObject、相机或编辑器状态，
    /// 因而真实战斗和 F10 可以复用完全相同的反馈曲线与几何骨架。
    /// </summary>
    public static class BossActionEffectSampler
    {
        public const int CirclePointCount = 36;
        public const int SweepPointCount = 28;
        public const int ShieldPointCount = 72;
        public const int ShieldOrnamentPointCount = 28;
        public const int ImpactPointCount = 40;
        public const int ScreenBreakRayCount = 8;
        public const int ScreenBreakRayPointCount = 5;
        public const int ScreenBreakBranchPointCount = 4;
        public const int GlassShardCount = 10;
        public const int GlassShardPointCount = 6;

        public static BossActionEffectSample Sample(
            BossActionEffectPreset preset,
            float elapsedSeconds)
        {
            if (preset == null || elapsedSeconds < 0f)
                return default;

            float visualDuration = Mathf.Max(0.001f, preset.duration);
            float activeDuration = Mathf.Max(
                visualDuration,
                preset.screenMotion == BossScreenMotionKind.None
                    ? 0f
                    : Mathf.Max(
                        0.001f,
                        preset.screenMotionDuration));
            bool active = elapsedSeconds < activeDuration;
            float progress = Mathf.Clamp01(
                elapsedSeconds / visualDuration);
            bool visualVisible = elapsedSeconds < visualDuration;
            // 重拳的接触点需要立即出现，但裂纹与碎片应沿屏幕逐步长开；
            // 其它效果维持原先更利落的展开速度。
            float revealEndProgress =
                preset.visualKind ==
                BossActionEffectVisualKind.HeavyPunchImpact
                    ? 0.44f
                    : 0.24f;
            float reveal = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, revealEndProgress, progress));
            float alpha = visualVisible
                ? 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.55f, 1f, progress))
                : 0f;
            float scale = SampleVisualScale(
                preset.visualKind,
                progress) * preset.visualScale;
            Vector2 offset = SampleScreenOffset(
                preset.screenMotion,
                elapsedSeconds,
                preset.screenMotionDuration);
            return new BossActionEffectSample(
                active,
                visualVisible,
                progress,
                reveal,
                alpha,
                scale,
                offset);
        }

        public static Vector2 SampleScreenOffset(
            BossScreenMotionKind kind,
            float elapsedSeconds,
            float duration)
        {
            if (kind == BossScreenMotionKind.None ||
                elapsedSeconds < 0f || duration <= 0f ||
                elapsedSeconds >= duration)
            {
                return Vector2.zero;
            }

            float t = Mathf.Clamp01(elapsedSeconds / duration);
            switch (kind)
            {
                case BossScreenMotionKind.ImpactJolt:
                    return SampleImpactJolt(t);
                case BossScreenMotionKind.HorizontalSway:
                    return SampleHorizontalSway(t);
                default:
                    return Vector2.zero;
            }
        }

        public static Vector2 GetUnitCirclePoint(int index, int pointCount)
        {
            int count = Mathf.Max(3, pointCount);
            float angle = index / (count - 1f) * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>
        /// 闭合盾牌轮廓。四段三次 Bézier 复刻敌人防御图标的顶尖、宽肩、
        /// 内收腰线和底尖，不再使用会显得像六边形的直线顶点插值。
        /// </summary>
        public static Vector2 GetShieldUnitPoint(float normalized)
        {
            float wrapped = Mathf.Repeat(normalized, 1f);
            float scaled = wrapped * 4f;
            int segment = Mathf.FloorToInt(scaled);
            float t = scaled - segment;
            switch (segment)
            {
                case 0:
                    return CubicBezier(
                        new Vector2(0f, 1f),
                        new Vector2(-0.18f, 0.91f),
                        new Vector2(-0.58f, 0.70f),
                        new Vector2(-0.98f, 0.65f),
                        t);
                case 1:
                    return CubicBezier(
                        new Vector2(-0.98f, 0.65f),
                        new Vector2(-0.96f, 0.10f),
                        new Vector2(-0.62f, -0.72f),
                        new Vector2(0f, -1f),
                        t);
                case 2:
                    return CubicBezier(
                        new Vector2(0f, -1f),
                        new Vector2(0.62f, -0.72f),
                        new Vector2(0.96f, 0.10f),
                        new Vector2(0.98f, 0.65f),
                        t);
                default:
                    return CubicBezier(
                        new Vector2(0.98f, 0.65f),
                        new Vector2(0.58f, 0.70f),
                        new Vector2(0.18f, 0.91f),
                        new Vector2(0f, 1f),
                        t);
            }
        }

        /// <summary>沿轮廓法线向内偏移的包边，避免简单缩放造成肩部间距失真。</summary>
        public static Vector2 GetShieldInsetUnitPoint(
            float normalized,
            float inset)
        {
            const float SampleDelta = 0.002f;
            Vector2 point = GetShieldUnitPoint(normalized);
            Vector2 before = GetShieldUnitPoint(normalized - SampleDelta);
            Vector2 after = GetShieldUnitPoint(normalized + SampleDelta);
            Vector2 tangent = (after - before).normalized;
            Vector2 inward = new Vector2(-tangent.y, tangent.x);
            return point + inward * Mathf.Max(0f, inset);
        }

        /// <summary>敌人盾图标内部左右镜像的卷纹。</summary>
        public static Vector2 GetShieldScrollUnitPoint(
            bool left,
            float normalized)
        {
            float side = left ? -1f : 1f;
            float t = Mathf.Clamp01(normalized);
            if (t < 0.56f)
            {
                return MirrorX(CubicBezier(
                    new Vector2(0.44f, 0.38f),
                    new Vector2(0.61f, 0.18f),
                    new Vector2(0.47f, -0.02f),
                    new Vector2(0.22f, -0.07f),
                    t / 0.56f), side);
            }
            return MirrorX(CubicBezier(
                new Vector2(0.22f, -0.07f),
                new Vector2(0.035f, -0.14f),
                new Vector2(0.08f, -0.35f),
                new Vector2(0.30f, -0.41f),
                (t - 0.56f) / 0.44f), side);
        }

        /// <summary>盾心菱形的闭合轮廓。</summary>
        public static Vector2 GetShieldGemUnitPoint(int index)
        {
            switch ((index % 5 + 5) % 5)
            {
                case 0: return new Vector2(0f, 0.17f);
                case 1: return new Vector2(0.13f, 0.01f);
                case 2: return new Vector2(0f, -0.15f);
                case 3: return new Vector2(-0.13f, 0.01f);
                default: return new Vector2(0f, 0.17f);
            }
        }

        /// <summary>从屏幕中心向外生长的一条确定性折线裂纹。</summary>
        public static Vector2 GetScreenBreakRayUnitPoint(
            int rayIndex,
            int pointIndex)
        {
            int ray = Mathf.Abs(rayIndex) % ScreenBreakRayCount;
            int point = Mathf.Clamp(
                pointIndex, 0, ScreenBreakRayPointCount - 1);
            float baseAngle = ray / (float)ScreenBreakRayCount *
                              Mathf.PI * 2f;
            baseAngle += (Hash01(ray, 11) - 0.5f) * 0.30f;
            float progress = point /
                             (ScreenBreakRayPointCount - 1f);
            float bend = (Hash01(ray, point + 37) - 0.5f) *
                         Mathf.Lerp(0.05f, 0.28f, progress);
            float radius = GetScreenBreakRayRadius(point) *
                           Mathf.Lerp(0.90f, 1.10f,
                               Hash01(ray, point + 71));
            float angle = baseAngle + bend;
            return new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius);
        }

        /// <summary>从主裂纹中段岔出的次级裂纹。</summary>
        public static Vector2 GetScreenBreakBranchUnitPoint(
            int rayIndex,
            int pointIndex)
        {
            int point = Mathf.Clamp(
                pointIndex, 0, ScreenBreakBranchPointCount - 1);
            Vector2 start = GetScreenBreakRayUnitPoint(rayIndex, 2);
            if (point == 0)
                return start;
            float sign = Hash01(rayIndex, 101) < 0.5f ? -1f : 1f;
            float angle = Mathf.Atan2(start.y, start.x) +
                          sign * Mathf.Lerp(0.34f, 0.57f,
                              Hash01(rayIndex, 131));
            angle += (Hash01(rayIndex, point + 151) - 0.5f) * 0.18f;
            float radius = GetScreenBreakBranchRadius(point) *
                           Mathf.Lerp(0.93f, 1.07f,
                               Hash01(rayIndex, point + 181));
            return new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius);
        }

        /// <summary>裂纹之间的玻璃碎片轮廓。</summary>
        public static Vector2 GetGlassShardUnitPoint(
            int shardIndex,
            int pointIndex)
        {
            int shard = Mathf.Abs(shardIndex) % GlassShardCount;
            int point = Mathf.Clamp(
                pointIndex, 0, GlassShardPointCount - 1);
            float centerAngle = shard / (float)GlassShardCount *
                                Mathf.PI * 2f +
                                (Hash01(shard, 211) - 0.5f) * 0.20f;
            float halfAngle = Mathf.Lerp(
                0.10f, 0.19f, Hash01(shard, 223));
            float inner = Mathf.Lerp(
                0.27f, 0.38f, Hash01(shard, 227));
            float outer = Mathf.Lerp(
                0.58f, 0.91f, Hash01(shard, 229));
            float tip = outer * Mathf.Lerp(
                1.04f, 1.22f, Hash01(shard, 233));
            float angle;
            float radius;
            switch (point)
            {
                case 0:
                case 5:
                    angle = centerAngle - halfAngle * 0.58f;
                    radius = inner;
                    break;
                case 1:
                    angle = centerAngle - halfAngle;
                    radius = outer * 0.86f;
                    break;
                case 2:
                    angle = centerAngle - halfAngle * 0.18f;
                    radius = tip;
                    break;
                case 3:
                    angle = centerAngle + halfAngle;
                    radius = outer;
                    break;
                default:
                    angle = centerAngle + halfAngle * 0.54f;
                    radius = inner * 1.06f;
                    break;
            }
            return new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius);
        }

        /// <summary>轻微不规则的冲击轮廓，避免完美同心圆的人工感。</summary>
        public static Vector2 GetImpactContourPoint(
            float normalized,
            int layer)
        {
            float angle = Mathf.Repeat(normalized, 1f) * Mathf.PI * 2f;
            float radius = 1f +
                Mathf.Sin(angle * (5f + layer) + layer * 1.7f) * 0.045f +
                Mathf.Sin(angle * 3f - layer * 0.8f) * 0.025f;
            return new Vector2(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// 返回从屏幕右侧向左展开的横扫波骨架；坐标是左下为原点的视口值。
        /// </summary>
        public static Vector2 GetSweepViewportPoint(float normalized)
        {
            float t = Mathf.Clamp01(normalized);
            return new Vector2(
                Mathf.Lerp(1.06f, -0.06f, t),
                0.35f + Mathf.Sin(t * Mathf.PI) * 0.105f -
                Mathf.Sin(t * Mathf.PI * 2f) * 0.018f);
        }

        private static float SampleVisualScale(
            BossActionEffectVisualKind kind,
            float progress)
        {
            float rise = EaseOutCubic(Mathf.Clamp01(progress / 0.42f));
            switch (kind)
            {
                case BossActionEffectVisualKind.HeavyPunchImpact:
                    return Mathf.LerpUnclamped(0.46f, 1.12f, rise);
                case BossActionEffectVisualKind.HorizontalSweep:
                    return Mathf.Lerp(0.86f, 1.04f, rise);
                case BossActionEffectVisualKind.ArmorCompress:
                    return Mathf.LerpUnclamped(1.20f, 1f, rise);
                default:
                    return 1f;
            }
        }

        private static Vector2 SampleImpactJolt(float t)
        {
            if (t < 0.22f)
            {
                float strike = EaseOutCubic(t / 0.22f);
                return new Vector2(0.12f * strike, -strike);
            }
            if (t < 0.58f)
            {
                float rebound = Mathf.SmoothStep(
                    0f,
                    1f,
                    (t - 0.22f) / 0.36f);
                return Vector2.LerpUnclamped(
                    new Vector2(0.12f, -1f),
                    new Vector2(-0.05f, 0.28f),
                    rebound);
            }

            float settle = Mathf.SmoothStep(
                0f,
                1f,
                (t - 0.58f) / 0.42f);
            return Vector2.LerpUnclamped(
                new Vector2(-0.05f, 0.28f),
                Vector2.zero,
                settle);
        }

        private static Vector2 SampleHorizontalSway(float t)
        {
            float envelope = Mathf.Sin(t * Mathf.PI);
            float x = -Mathf.Sin(t * Mathf.PI * 2f) * envelope;
            return new Vector2(x, 0f);
        }

        private static Vector2 MirrorX(Vector2 point, float side)
        {
            point.x *= side;
            return point;
        }

        private static Vector2 CubicBezier(
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3,
            float value)
        {
            float t = Mathf.Clamp01(value);
            float inverse = 1f - t;
            return inverse * inverse * inverse * p0 +
                   3f * inverse * inverse * t * p1 +
                   3f * inverse * t * t * p2 +
                   t * t * t * p3;
        }

        private static float Hash01(int first, int second)
        {
            return Mathf.Repeat(
                Mathf.Sin(
                    (first + 1) * 12.9898f +
                    (second + 1) * 78.233f) * 43758.5453f,
                1f);
        }

        private static float GetScreenBreakRayRadius(int point)
        {
            switch (point)
            {
                case 0: return 0.10f;
                case 1: return 0.34f;
                case 2: return 0.68f;
                case 3: return 1.13f;
                default: return 1.78f;
            }
        }

        private static float GetScreenBreakBranchRadius(int point)
        {
            switch (point)
            {
                case 0: return 0.68f;
                case 1: return 0.91f;
                case 2: return 1.18f;
                default: return 1.43f;
            }
        }

        private static float EaseOutCubic(float value)
        {
            float inverse = 1f - Mathf.Clamp01(value);
            return 1f - inverse * inverse * inverse;
        }
    }
}
