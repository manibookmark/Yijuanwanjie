using System;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>Semantic layers shared by every reward-grade spirit field.</summary>
    public enum RewardSpiritLayerKind
    {
        Ambient,
        ConvergingLeft,
        ConvergingRight,
        Returning,
        Ascending,
        Hover
    }

    /// <summary>
    /// One grade's abundance contract. Grades share colour and motion grammar;
    /// only the amount, footprint, echo and duration of qi increase.
    /// </summary>
    public readonly struct RewardSpiritGradeStyle
    {
        internal RewardSpiritGradeStyle(
            RewardRarity rarity,
            int tier,
            int ambientCount,
            int convergingCount,
            int returningCount,
            int ascendingCount,
            int hoverCount,
            float footprint,
            float expectedDuration)
        {
            Rarity = rarity;
            Tier = tier;
            AmbientCount = ambientCount;
            ConvergingCount = convergingCount;
            ReturningCount = returningCount;
            AscendingCount = ascendingCount;
            HoverCount = hoverCount;
            Footprint = footprint;
            ExpectedDuration = expectedDuration;
        }

        public RewardRarity Rarity { get; }
        public int Tier { get; }
        public int AmbientCount { get; }
        public int ConvergingCount { get; }
        public int ReturningCount { get; }
        public int AscendingCount { get; }
        public int HoverCount { get; }
        public float Footprint { get; }
        public float ExpectedDuration { get; }
        public bool HasReturningCurrent => ReturningCount > 0;
        public int ReturningWaveCount => Tier >= 4 ? 3 : Tier >= 3 ? 2 : 1;
        public int PeakBudget =>
            AmbientCount + ConvergingCount * 2 +
            ReturningCount * ReturningWaveCount + AscendingCount;
    }

    /// <summary>
    /// Shared particle-scale motion. The five grades keep this same substance;
    /// only their counts, echo rhythm and modest footprint differ.
    /// </summary>
    public readonly struct RewardSpiritParticleMotion
    {
        internal RewardSpiritParticleMotion(
            Vector2 lifetime,
            Vector2 size,
            Vector2 linearX,
            Vector2 linearY,
            Vector2 shapeScale,
            float shapeRotationZ)
        {
            Lifetime = lifetime;
            Size = size;
            LinearX = linearX;
            LinearY = linearY;
            ShapeScale = shapeScale;
            ShapeRotationZ = shapeRotationZ;
        }

        public Vector2 Lifetime { get; }
        public Vector2 Size { get; }
        public Vector2 LinearX { get; }
        public Vector2 LinearY { get; }
        public Vector2 ShapeScale { get; }
        public float ShapeRotationZ { get; }
    }

    /// <summary>Native-ready values for one layer of the grade choreography.</summary>
    public readonly struct RewardSpiritLayerStyle
    {
        // Infinity 1.5.1 validates this value even when CycleCount is one.
        private const float MinimumNativeRepeatInterval = 0.01f;

        internal RewardSpiritLayerStyle(
            int count,
            float burstTime,
            float radius,
            float radiusThickness,
            float arc,
            int cycleCount,
            float repeatInterval,
            float radialVelocity,
            float orbitalVelocity,
            RewardSpiritParticleMotion motion)
        {
            Count = count;
            BurstTime = burstTime;
            Radius = radius;
            RadiusThickness = radiusThickness;
            Arc = arc;
            CycleCount = cycleCount;
            RepeatInterval = repeatInterval > 0f
                ? repeatInterval
                : MinimumNativeRepeatInterval;
            RadialVelocity = radialVelocity;
            OrbitalVelocity = orbitalVelocity;
            Motion = motion;
        }

        public int Count { get; }
        public float BurstTime { get; }
        public float Radius { get; }
        public float RadiusThickness { get; }
        public float Arc { get; }
        public int CycleCount { get; }
        public float RepeatInterval { get; }
        public float RadialVelocity { get; }
        public float OrbitalVelocity { get; }
        public RewardSpiritParticleMotion Motion { get; }
        public int RequiredCapacity => Count * CycleCount + 4;
    }

    /// <summary>
    /// Single source of truth for the five reward grades. The rhythm is always
    /// 引气 → 凝聚 → 展脉 → 升散; higher grades add breadth and ordered echoes,
    /// never unrelated petals, paper scraps or random fireworks.
    /// </summary>
    public static class RewardRevealSpiritGradeLanguage
    {
        public const int RuntimeChoreographyVersion = 3;

        public static RewardSpiritGradeStyle Resolve(RewardRarity rarity)
        {
            return rarity switch
            {
                RewardRarity.Uncommon => new RewardSpiritGradeStyle(
                    rarity, 1, 3, 6, 0, 6, 5, 0.64f, 1.22f),
                RewardRarity.Rare => new RewardSpiritGradeStyle(
                    rarity, 2, 4, 7, 4, 8, 6, 0.70f, 1.34f),
                RewardRarity.VeryRare => new RewardSpiritGradeStyle(
                    rarity, 3, 5, 9, 6, 10, 8, 0.76f, 1.48f),
                RewardRarity.UltraRare => new RewardSpiritGradeStyle(
                    rarity, 4, 6, 12, 8, 14, 10, 0.82f, 1.62f),
                _ => new RewardSpiritGradeStyle(
                    RewardRarity.Common, 0, 2, 4, 0, 4, 4, 0.58f, 1.15f)
            };
        }

        public static RewardSpiritLayerStyle ResolveLayer(
            RewardSpiritGradeStyle grade,
            RewardSpiritLayerKind kind)
        {
            float footprint = grade.Footprint;
            int tier = grade.Tier;
            return kind switch
            {
                RewardSpiritLayerKind.Ambient => new RewardSpiritLayerStyle(
                    grade.AmbientCount,
                    0f,
                    0.58f * footprint,
                    0.10f,
                    260f + tier * 5f,
                    1,
                    0.28f,
                    -0.04f,
                    0.16f,
                    new RewardSpiritParticleMotion(
                        new Vector2(0.72f, 0.84f + tier * 0.03f),
                        new Vector2(0.032f, 0.050f),
                        new Vector2(-0.010f, 0.010f),
                        new Vector2(0.015f, 0.035f),
                        new Vector2(1f, 0.90f),
                        145f)),
                RewardSpiritLayerKind.ConvergingLeft or
                RewardSpiritLayerKind.ConvergingRight =>
                    new RewardSpiritLayerStyle(
                        grade.ConvergingCount,
                        0.08f,
                        footprint,
                        0.055f,
                        68f + tier * 4f,
                        1,
                        0.28f,
                        -0.52f,
                        kind == RewardSpiritLayerKind.ConvergingLeft
                            ? 0.14f
                            : -0.14f,
                        new RewardSpiritParticleMotion(
                            new Vector2(
                                0.72f,
                                0.86f + tier * 0.02f),
                            new Vector2(0.038f, 0.060f),
                            new Vector2(-0.012f, 0.012f),
                            new Vector2(0.025f, 0.055f),
                            new Vector2(1f, 0.92f),
                            kind == RewardSpiritLayerKind.ConvergingLeft
                                ? 118f
                                : -118f)),
                RewardSpiritLayerKind.Returning =>
                    new RewardSpiritLayerStyle(
                        grade.ReturningCount,
                        0.38f,
                        0.62f * footprint,
                        0.07f,
                        220f + tier * 8f,
                        grade.ReturningWaveCount,
                        0.26f,
                        -0.03f,
                        0.30f,
                        new RewardSpiritParticleMotion(
                            new Vector2(0.58f, 0.72f),
                            new Vector2(0.035f, 0.055f),
                            new Vector2(-0.008f, 0.008f),
                            new Vector2(0.010f, 0.030f),
                            new Vector2(1f, 0.92f),
                            180f)),
                RewardSpiritLayerKind.Ascending =>
                    new RewardSpiritLayerStyle(
                        grade.AscendingCount,
                        0.64f + tier * 0.03f,
                        0.22f * footprint,
                        0.75f,
                        110f,
                        1,
                        0.26f,
                        0.02f,
                        0.025f,
                        new RewardSpiritParticleMotion(
                            new Vector2(
                                0.72f,
                                0.86f + tier * 0.03f),
                            new Vector2(0.032f, 0.052f),
                            new Vector2(-0.012f, 0.012f),
                            new Vector2(
                                0.24f,
                                0.32f + tier * 0.02f),
                            new Vector2(0.82f, 1f),
                            205f)),
                _ => new RewardSpiritLayerStyle(
                    grade.HoverCount,
                    0f,
                    0.48f * footprint,
                    0.08f,
                    220f,
                    1,
                    0.26f,
                    0.025f,
                    0.22f,
                    new RewardSpiritParticleMotion(
                        new Vector2(
                            0.38f,
                            0.48f + tier * 0.02f),
                        new Vector2(0.038f, 0.060f),
                        new Vector2(-0.008f, 0.008f),
                        new Vector2(0.030f, 0.060f),
                        new Vector2(1f, 0.92f),
                        105f))
            };
        }

        public static bool TryResolveLayerKind(
            string systemName,
            out RewardSpiritLayerKind kind)
        {
            if (string.Equals(systemName, "AmbientQi", StringComparison.Ordinal))
                kind = RewardSpiritLayerKind.Ambient;
            else if (string.Equals(
                         systemName,
                         "ConvergingQiLeft",
                         StringComparison.Ordinal))
                kind = RewardSpiritLayerKind.ConvergingLeft;
            else if (string.Equals(
                         systemName,
                         "ConvergingQiRight",
                         StringComparison.Ordinal))
                kind = RewardSpiritLayerKind.ConvergingRight;
            else if (string.Equals(
                         systemName,
                         "ReturningQi",
                         StringComparison.Ordinal))
                kind = RewardSpiritLayerKind.Returning;
            else if (string.Equals(
                         systemName,
                         "AscendingQi",
                         StringComparison.Ordinal))
                kind = RewardSpiritLayerKind.Ascending;
            else if (string.Equals(
                         systemName,
                         "QiBreathPulse",
                         StringComparison.Ordinal))
                kind = RewardSpiritLayerKind.Hover;
            else
            {
                kind = default;
                return false;
            }

            return true;
        }
    }
}
