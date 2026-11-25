using System;

namespace WanjieScroll.Core
{
    /// <summary>Explicit source configuration; neither the maximum nor Luck factor is inferred from a chest grade.</summary>
    public sealed class SideEventGemChoiceCountConfiguration
    {
        public int MaximumChoices { get; }
        public float LuckChanceModifier { get; }

        public SideEventGemChoiceCountConfiguration(int maximumChoices, float luckChanceModifier)
        {
            if (maximumChoices < 0) throw new ArgumentOutOfRangeException(nameof(maximumChoices));
            if (!SideEventGemChoiceCountRules.IsFinite(luckChanceModifier) || luckChanceModifier < 0f)
                throw new ArgumentOutOfRangeException(nameof(luckChanceModifier));
            MaximumChoices = maximumChoices;
            LuckChanceModifier = luckChanceModifier;
        }
    }

    /// <summary>A detached observation of one count roll. A durable reward owner stores it rather than rerolling on reads or resume.</summary>
    public sealed class SideEventGemChoiceCountResult
    {
        public int BaseCount { get; }
        public int ResolvedCount { get; }
        public int MaximumChoices { get; }
        public float LuckMultiplier { get; }
        public float LuckChanceModifier { get; }
        public float Threshold { get; }
        public float Draw { get; }
        public bool LuckRollPassed { get; }
        public bool Increased => ResolvedCount > BaseCount;

        internal SideEventGemChoiceCountResult(int baseCount, int resolvedCount,
            SideEventGemChoiceCountConfiguration configuration, float luckMultiplier,
            float threshold, float draw, bool luckRollPassed)
        {
            BaseCount = baseCount;
            ResolvedCount = resolvedCount;
            MaximumChoices = configuration.MaximumChoices;
            LuckMultiplier = luckMultiplier;
            LuckChanceModifier = configuration.LuckChanceModifier;
            Threshold = threshold;
            Draw = draw;
            LuckRollPassed = luckRollPassed;
        }
    }

    /// <summary>
    /// Owns the scalar count roll from ChooseGemModal.GetGemChoices: binary32
    /// (LuckMultiplier - 1) * modifier, one inclusive float draw, and a strict
    /// draw &lt; threshold decision followed by Clamp(base + 1, base, maximum).
    /// Negative Luck is legal and the threshold is not clamped to a probability.
    /// A draw still occurs when Luck cannot help or the configured maximum is met.
    ///
    /// This module is stateless: fresh generation and an explicit reroll may call
    /// Resolve; displaying, returning to hosts, and restoring a saved offer must
    /// reuse that offer's checkpoint. It does not decide when a reroll is payable,
    /// filter Gem supply, extend frequencies, or manufacture a full offer.
    ///
    /// Unknown/nonfinite inputs, invalid bounds, and integer/arithmetic overflow
    /// are project guards checked before RNG. Native unchecked integer overflow
    /// is deliberately not reproduced. IRandomSource is required; its stream is
    /// not claimed equivalent to UnityEngine.Random for the same seed.
    /// </summary>
    public static class SideEventGemChoiceCountRules
    {
        public static SideEventGemChoiceCountResult Resolve(int baseChoiceCount,
            float? luckMultiplier, SideEventGemChoiceCountConfiguration configuration,
            IRandomSource random)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (!luckMultiplier.HasValue)
                throw new InvalidOperationException("Gem choice-count Luck qualification is unknown.");
            float luck = luckMultiplier.Value;
            if (!IsFinite(luck)) throw new ArgumentOutOfRangeException(nameof(luckMultiplier));
            if (baseChoiceCount < 0 || baseChoiceCount > configuration.MaximumChoices)
                throw new ArgumentOutOfRangeException(nameof(baseChoiceCount));
            if (random == null) throw new ArgumentNullException(nameof(random));

            int increased = checked(baseChoiceCount + 1);
            float excessLuck = Binary32(luck - 1f);
            float threshold = Binary32(excessLuck * configuration.LuckChanceModifier);
            if (!IsFinite(excessLuck) || !IsFinite(threshold))
                throw new OverflowException("Gem choice-count threshold exceeded finite binary32 arithmetic.");

            float draw = random.Range(0f, 1f);
            if (!IsFinite(draw) || draw < 0f || draw > 1f)
                throw new InvalidOperationException("Gem choice-count RNG returned a value outside [0, 1].");
            bool passed = draw < threshold;
            int count = passed ? Math.Min(increased, configuration.MaximumChoices) : baseChoiceCount;
            return new SideEventGemChoiceCountResult(baseChoiceCount, count, configuration,
                luck, threshold, draw, passed);
        }

        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Binary32(float value) => BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
    }
}
