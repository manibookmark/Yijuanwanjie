using System;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>One ordered strand in the mirrored ruyi-cloud mother shape.</summary>
    public readonly struct RewardWindowBloomChannelStyle
    {
        internal RewardWindowBloomChannelStyle(
            Color deepColor,
            Color coreColor,
            Color highlightColor,
            float rateMultiplier,
            float progressLag,
            float outwardOffset,
            float widthMultiplier,
            float wispShape,
            bool stretched,
            float lengthScale,
            float velocityScale)
        {
            DeepColor = deepColor;
            CoreColor = coreColor;
            HighlightColor = highlightColor;
            RateMultiplier = rateMultiplier;
            ProgressLag = progressLag;
            OutwardOffset = outwardOffset;
            WidthMultiplier = widthMultiplier;
            WispShape = wispShape;
            Stretched = stretched;
            LengthScale = lengthScale;
            VelocityScale = velocityScale;
        }

        public Color DeepColor { get; }
        public Color CoreColor { get; }
        public Color HighlightColor { get; }
        public float RateMultiplier { get; }
        public float ProgressLag { get; }
        public float OutwardOffset { get; }
        public float WidthMultiplier { get; }
        public float WispShape { get; }
        public bool Stretched { get; }
        public float LengthScale { get; }
        public float VelocityScale { get; }
    }

    /// <summary>
    /// Immutable abundance contract for one grade. Geometry is always complete;
    /// grades change scale, density, strand count, persistence and colour only.
    /// </summary>
    public readonly struct RewardWindowBloomGradeStyle
    {
        private readonly RewardWindowBloomChannelStyle[] _channels;

        internal RewardWindowBloomGradeStyle(
            RewardRarity rarity,
            int tier,
            float patternScale,
            int channelCount,
            float particleRate,
            float strokeDuration,
            Vector2 particleLifetime,
            Vector2 particleSize,
            float filamentTime,
            float filamentWidth,
            float opacity,
            int colorFamilyCount,
            RewardWindowBloomChannelStyle[] channels)
        {
            Rarity = rarity;
            Tier = tier;
            PatternScale = patternScale;
            _channels = channels ?? Array.Empty<RewardWindowBloomChannelStyle>();
            ChannelCount = Mathf.Clamp(channelCount, 1, _channels.Length);
            ParticleRate = particleRate;
            StrokeDuration = strokeDuration;
            ParticleLifetime = particleLifetime;
            ParticleSize = particleSize;
            FilamentTime = filamentTime;
            FilamentWidth = filamentWidth;
            Opacity = opacity;
            ColorFamilyCount = colorFamilyCount;

            int budget = 0;
            for (int index = 0; index < ChannelCount; index++)
            {
                int perSide = Mathf.CeilToInt(
                    particleRate * strokeDuration *
                    _channels[index].RateMultiplier);
                budget += perSide * RewardWindowBloomLanguage.SideCount;
            }
            ParticleBudget = budget;
        }

        public RewardRarity Rarity { get; }
        public int Tier { get; }
        public float PatternScale { get; }
        public int ChannelCount { get; }
        public float ParticleRate { get; }
        public float StrokeDuration { get; }
        public Vector2 ParticleLifetime { get; }
        public Vector2 ParticleSize { get; }
        public float FilamentTime { get; }
        public float FilamentWidth { get; }
        public float Opacity { get; }
        public int ColorFamilyCount { get; }
        public int ParticleBudget { get; }
        public float PathCompletion => 1f;
        public float ExpectedVisibleDuration =>
            StrokeDuration + ParticleLifetime.y;
        public float EmitterRadius => 0.008f + Tier * 0.001f;
        public float ConvergencePearlDiameter => 0.075f + Tier * 0.0175f;
        public float ConvergencePearlDuration => 0.18f + Tier * 0.025f;
        public float ConvergencePearlOpacity => 0.30f + Tier * 0.095f;
        public float EstimatedRenderedHalfHeight =>
            RewardWindowBloomLanguage.PathHalfHeight * PatternScale +
            GetChannel(ChannelCount - 1).OutwardOffset * PatternScale +
            ParticleSize.y * 0.5f + FilamentWidth;

        public RewardWindowBloomChannelStyle GetChannel(int index)
        {
            if (_channels == null || index < 0 || index >= _channels.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _channels[index];
        }

        public int ResolveParticleCapacity(int channelIndex)
        {
            int expected = Mathf.CeilToInt(
                ParticleRate * StrokeDuration *
                GetChannel(channelIndex).RateMultiplier) + 4;
            return Mathf.Clamp(
                expected,
                8,
                RewardWindowBloomLanguage.MaxParticlesPerChannel);
        }
    }

    /// <summary>
    /// Owns the complete twelve-segment ruyi-cloud path. Six Infinity systems map
    /// to left/right sides by three ordered strands; they must never be treated
    /// as six circular contours. This 500-line immutable language module remains
    /// one knowledge boundary: path, grade grammar and their safety budgets vary
    /// together and own no runtime state. Re-review before adding a second mother
    /// shape, a fourth strand or another resource family.
    /// </summary>
    public static class RewardWindowBloomLanguage
    {
        private readonly struct CubicSegment
        {
            public CubicSegment(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
            {
                P0 = p0;
                P1 = p1;
                P2 = p2;
                P3 = p3;
            }

            public Vector2 P0 { get; }
            public Vector2 P1 { get; }
            public Vector2 P2 { get; }
            public Vector2 P3 { get; }

            public Vector2 Sample(float t)
            {
                float inverse = 1f - t;
                return inverse * inverse * inverse * P0 +
                       3f * inverse * inverse * t * P1 +
                       3f * inverse * t * t * P2 +
                       t * t * t * P3;
            }

            public Vector2 Tangent(float t)
            {
                float inverse = 1f - t;
                return 3f * inverse * inverse * (P1 - P0) +
                       6f * inverse * t * (P2 - P1) +
                       3f * t * t * (P3 - P2);
            }
        }

        public const int RuntimeLanguageVersion = 7;
        public const int SideCount = 2;
        public const int MaxChannelCount = 3;
        public const int PathSegmentCount = 12;
        public const int MaxParticleBudget = 720;
        public const int MaxParticlesPerChannel = 160;
        public const float StageHalfWidth = 2.133f;
        public const float StageHalfHeight = 1.600f;
        public const float PaperWindowHalfWidth = 1.126f;
        public const float PaperWindowHalfHeight = 1.067f;
        public const float PathHalfHeight = 1.350f;
        public const float ConvergenceStartProgress = 10f / PathSegmentCount;
        public const float ConvergenceGlowProgress = 0.93f;

        private static readonly CubicSegment[] RightStroke =
        {
            Segment(0.22f, -1.20f, 0.55f, -1.32f,
                0.98f, -1.28f, 1.20f, -1.13f),
            Segment(1.20f, -1.13f, 1.38f, -1.04f,
                1.47f, -0.88f, 1.37f, -0.75f),
            Segment(1.37f, -0.75f, 1.29f, -0.62f,
                1.18f, -0.65f, 1.20f, -0.80f),
            Segment(1.20f, -0.80f, 1.22f, -0.96f,
                1.44f, -0.84f, 1.40f, -0.48f),
            Segment(1.40f, -0.48f, 1.37f, -0.20f,
                1.20f, -0.02f, 1.24f, 0.18f),
            Segment(1.24f, 0.18f, 1.28f, 0.39f,
                1.50f, 0.52f, 1.41f, 0.78f),
            Segment(1.41f, 0.78f, 1.34f, 1.00f,
                1.34f, 1.14f, 1.20f, 1.18f),
            Segment(1.20f, 1.18f, 1.03f, 1.23f,
                1.04f, 1.13f, 1.18f, 1.12f),
            Segment(1.18f, 1.12f, 1.32f, 1.11f,
                1.24f, 1.30f, 1.07f, 1.30f),
            Segment(1.07f, 1.30f, 0.86f, 1.30f,
                0.64f, 1.35f, 0.48f, 1.28f),
            // The two final segments preserve the incoming tangent, draw
            // inward across the top and then dip just inside the paper edge.
            Segment(0.48f, 1.28f, 0.36f, 1.2275f,
                0.33f, 1.205f, 0.24f, 1.16f),
            Segment(0.24f, 1.16f, 0.15f, 1.115f,
                0.06f, 1.085f, 0.06f, 1.05f)
        };

        public static RewardWindowBloomGradeStyle Resolve(RewardRarity rarity)
        {
            return rarity switch
            {
                RewardRarity.Uncommon => Grade(
                    rarity, 1, 0.965f, 2, 96f, 1.05f,
                    new Vector2(0.38f, 0.52f),
                    new Vector2(0.043f, 0.066f),
                    0.29f, 0.0105f, 0.80f, 2, JadeChannels()),
                RewardRarity.Rare => Grade(
                    rarity, 2, 0.990f, 2, 104f, 1.15f,
                    new Vector2(0.40f, 0.55f),
                    new Vector2(0.046f, 0.071f),
                    0.34f, 0.012f, 0.86f, 3, JadeGoldChannels()),
                RewardRarity.VeryRare => Grade(
                    rarity, 3, 1.020f, 3, 108f, 1.25f,
                    new Vector2(0.42f, 0.58f),
                    new Vector2(0.050f, 0.078f),
                    0.39f, 0.0145f, 0.93f, 4,
                    JadeGoldVioletChannels()),
                RewardRarity.UltraRare => Grade(
                    rarity, 4, 1.055f, 3, 114f, 1.35f,
                    new Vector2(0.44f, 0.62f),
                    new Vector2(0.054f, 0.086f),
                    0.44f, 0.0175f, 1.00f, 5,
                    ImmortalChromaticChannels()),
                _ => Grade(
                    RewardRarity.Common, 0, 0.940f, 1, 90f, 0.95f,
                    new Vector2(0.36f, 0.50f),
                    new Vector2(0.040f, 0.062f),
                    0.24f, 0.0095f, 0.74f, 1, ManaBlueChannels())
            };
        }

        public static void SamplePath(
            int sideIndex,
            float normalizedProgress,
            float outwardOffset,
            out Vector2 position,
            out Vector2 tangent)
        {
            if (sideIndex < 0 || sideIndex >= SideCount)
                throw new ArgumentOutOfRangeException(nameof(sideIndex));

            float progress = Mathf.Clamp01(normalizedProgress);
            float scaled = progress * RightStroke.Length;
            int segmentIndex = Mathf.Min(
                RightStroke.Length - 1,
                Mathf.FloorToInt(scaled));
            float segmentProgress = segmentIndex == RightStroke.Length - 1 &&
                                    scaled >= RightStroke.Length
                ? 1f
                : scaled - segmentIndex;
            CubicSegment segment = RightStroke[segmentIndex];
            position = segment.Sample(segmentProgress);
            tangent = segment.Tangent(segmentProgress);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector2.up;
            else
                tangent.Normalize();

            if (sideIndex == 0)
            {
                position.x = -position.x;
                tangent.x = -tangent.x;
            }

            Vector2 outward = position.sqrMagnitude > 0.0001f
                ? position.normalized
                : Vector2.up;
            float convergence = ResolveConvergence(progress);
            float effectiveOffset = Mathf.Max(0f, outwardOffset) *
                                    (1f - convergence);
            position += outward * effectiveOffset;
        }

        public static float ResolvePatternScale(
            RewardWindowBloomGradeStyle grade,
            float normalizedProgress)
        {
            return Mathf.Lerp(
                grade.PatternScale,
                1f,
                ResolveConvergence(Mathf.Clamp01(normalizedProgress)));
        }

        public static float ResolveChannelProgress(
            RewardWindowBloomGradeStyle grade,
            int channelIndex,
            float entranceProgress)
        {
            RewardWindowBloomChannelStyle channel =
                grade.GetChannel(channelIndex);
            float localProgress = Mathf.Clamp01(
                (entranceProgress - channel.ProgressLag) /
                Mathf.Max(0.01f, 1f - channel.ProgressLag));
            return grade.PathCompletion * localProgress;
        }

        public static Vector2 GetHoverAnchor(
            RewardWindowBloomGradeStyle grade,
            int sideIndex)
        {
            SamplePath(
                sideIndex,
                Mathf.Min(grade.PathCompletion, 0.20f),
                0.015f,
                out Vector2 position,
                out _);
            return position * grade.PatternScale;
        }

        public static Vector2 GetConvergenceAnchor(int sideIndex)
        {
            SamplePath(
                sideIndex,
                1f,
                0f,
                out Vector2 position,
                out _);
            return position;
        }

        private static float ResolveConvergence(float normalizedProgress)
        {
            float tailProgress = Mathf.InverseLerp(
                ConvergenceStartProgress,
                1f,
                normalizedProgress);
            return Mathf.SmoothStep(0f, 1f, tailProgress);
        }

        private static RewardWindowBloomGradeStyle Grade(
            RewardRarity rarity,
            int tier,
            float patternScale,
            int channelCount,
            float particleRate,
            float strokeDuration,
            Vector2 particleLifetime,
            Vector2 particleSize,
            float filamentTime,
            float filamentWidth,
            float opacity,
            int colorFamilyCount,
            RewardWindowBloomChannelStyle[] channels)
        {
            return new RewardWindowBloomGradeStyle(
                rarity, tier, patternScale, channelCount, particleRate,
                strokeDuration, particleLifetime, particleSize,
                filamentTime, filamentWidth, opacity, colorFamilyCount,
                channels);
        }

        private static RewardWindowBloomChannelStyle[] ManaBlueChannels()
        {
            Color deep = new Color(0.035f, 0.11f, 0.28f, 1f);
            Color core = new Color(0.24f, 0.55f, 0.96f, 1f);
            Color light = new Color(0.64f, 0.83f, 1.15f, 1f);
            return Channels(
                Palette(deep, core, light),
                Palette(deep, core, light),
                Palette(deep, core, light),
                0.38f,
                0.30f);
        }

        private static RewardWindowBloomChannelStyle[] JadeChannels()
        {
            Color deep = new Color(0.015f, 0.15f, 0.18f, 1f);
            Color core = new Color(0.20f, 0.86f, 0.76f, 1f);
            Color light = new Color(0.72f, 1.05f, 0.96f, 1f);
            return Channels(
                Palette(deep, core, light),
                Palette(
                    new Color(0.025f, 0.11f, 0.22f, 1f),
                    new Color(0.28f, 0.72f, 1.00f, 1f),
                    new Color(0.78f, 1.10f, 1.16f, 1f)),
                Palette(deep, core, light),
                0.38f,
                0.30f);
        }

        private static RewardWindowBloomChannelStyle[] JadeGoldChannels()
        {
            return BuildJadeGoldVioletChannels(0.60f, 0.40f);
        }

        private static RewardWindowBloomChannelStyle[]
            JadeGoldVioletChannels()
        {
            return BuildJadeGoldVioletChannels(0.60f, 0.44f);
        }

        private static RewardWindowBloomChannelStyle[]
            BuildJadeGoldVioletChannels(
                float echoRateMultiplier,
                float accentRateMultiplier)
        {
            return Channels(
                Palette(
                    new Color(0.015f, 0.15f, 0.18f, 1f),
                    new Color(0.20f, 0.86f, 0.76f, 1f),
                    new Color(0.72f, 1.05f, 0.96f, 1f)),
                Palette(
                    new Color(0.18f, 0.10f, 0.025f, 1f),
                    new Color(0.92f, 0.65f, 0.20f, 1f),
                    new Color(1.24f, 1.00f, 0.58f, 1f)),
                Palette(
                    new Color(0.035f, 0.055f, 0.24f, 1f),
                    new Color(0.42f, 0.50f, 0.98f, 1f),
                    new Color(0.88f, 0.72f, 1.18f, 1f)),
                echoRateMultiplier,
                accentRateMultiplier);
        }

        private static RewardWindowBloomChannelStyle[]
            ImmortalChromaticChannels()
        {
            return Channels(
                Palette(
                    new Color(0.005f, 0.16f, 0.18f, 1f),
                    new Color(0.18f, 1.00f, 0.82f, 1f),
                    new Color(0.70f, 1.20f, 1.05f, 1f)),
                Palette(
                    new Color(0.20f, 0.10f, 0.02f, 1f),
                    new Color(1.02f, 0.68f, 0.18f, 1f),
                    new Color(1.35f, 1.08f, 0.56f, 1f)),
                Palette(
                    new Color(0.035f, 0.045f, 0.25f, 1f),
                    new Color(0.38f, 0.62f, 1.12f, 1f),
                    new Color(1.00f, 0.68f, 1.22f, 1f)),
                0.70f,
                0.54f);
        }

        private static RewardWindowBloomChannelStyle[] Channels(
            Color[] primary,
            Color[] echo,
            Color[] accent,
            float echoRateMultiplier,
            float accentRateMultiplier)
        {
            return new[]
            {
                Channel(primary, 1.00f, 0f, 0f, 1.00f,
                    0.08f, true, 0.70f, 0.12f),
                Channel(echo, echoRateMultiplier, 0.014f, 0.030f, 0.70f,
                    0.24f, false, 1f, 0f),
                Channel(accent, accentRateMultiplier, 0.028f, 0.060f, 0.52f,
                    0.64f, true, 0.34f, 0.06f)
            };
        }

        private static CubicSegment Segment(
            float x0, float y0,
            float x1, float y1,
            float x2, float y2,
            float x3, float y3)
        {
            return new CubicSegment(
                new Vector2(x0, y0),
                new Vector2(x1, y1),
                new Vector2(x2, y2),
                new Vector2(x3, y3));
        }

        private static Color[] Palette(Color deep, Color core, Color light)
        {
            return new[] { deep, core, light };
        }

        private static RewardWindowBloomChannelStyle Channel(
            Color[] palette,
            float rateMultiplier,
            float progressLag,
            float outwardOffset,
            float widthMultiplier,
            float wispShape,
            bool stretched,
            float lengthScale,
            float velocityScale)
        {
            return new RewardWindowBloomChannelStyle(
                palette[0], palette[1], palette[2],
                rateMultiplier, progressLag, outwardOffset,
                widthMultiplier, wispShape, stretched,
                lengthScale, velocityScale);
        }
    }
}
