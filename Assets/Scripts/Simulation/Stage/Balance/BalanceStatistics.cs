using System;
using System.Collections.Generic;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Owns sample denominators, empty-population semantics and uncertainty.
    /// The runner supplies outcomes; neither exports nor thresholds reclassify
    /// deaths or simulation limits. Limits remain in the success denominator.
    /// </summary>
    public sealed class BalanceStatistics
    {
        private readonly List<int> _all = new List<int>();
        private readonly List<int> _victories = new List<int>();
        private readonly List<int> _defeats = new List<int>();
        private readonly List<int> _limits = new List<int>();

        public void Add(bool victory, bool simulationLimit, int endingHealth)
        {
            if (victory && simulationLimit)
                throw new ArgumentException("A simulation limit cannot be a victory.");
            if (endingHealth < 0)
                throw new ArgumentOutOfRangeException(nameof(endingHealth));
            _all.Add(endingHealth);
            (victory ? _victories : simulationLimit ? _limits : _defeats)
                .Add(endingHealth);
        }

        public BalanceOutcomeStatistics Summarize()
        {
            return new BalanceOutcomeStatistics
            {
                all = Describe(_all),
                victories = Describe(_victories),
                defeats = Describe(_defeats),
                limits = Describe(_limits),
                successRate95 = Wilson95(_victories.Count, _all.Count)
            };
        }

        /// <summary>
        /// Two-sided 95% Wilson score interval for independent Bernoulli samples.
        /// SampleCount=0 explicitly means unavailable, not evidence of a 0% rate.
        /// It is not a paired A/B interval or a model of human-player variation.
        /// </summary>
        public static BalanceRateInterval Wilson95(int successes, int samples)
        {
            if (samples < 0 || successes < 0 || successes > samples)
                throw new ArgumentOutOfRangeException(nameof(successes));
            if (samples == 0) return new BalanceRateInterval();
            const double z = 1.959963984540054;
            double p = (double)successes / samples;
            double z2 = z * z;
            double denominator = 1d + z2 / samples;
            double center = (p + z2 / (2d * samples)) / denominator;
            double margin = z * Math.Sqrt(
                (p * (1d - p) + z2 / (4d * samples)) / samples) / denominator;
            return new BalanceRateInterval
            {
                samples = samples,
                successes = successes,
                lower = (float)Math.Max(0d, center - margin),
                upper = (float)Math.Min(1d, center + margin)
            };
        }

        private static BalanceHealthDistribution Describe(List<int> source)
        {
            // Preserve observation order and make repeated summaries harmless.
            var ordered = new List<int>(source);
            ordered.Sort();
            return new BalanceHealthDistribution
            {
                samples = ordered.Count,
                median = Quantile(ordered, 0.5d),
                p10 = Quantile(ordered, 0.1d),
                p90 = Quantile(ordered, 0.9d)
            };
        }

        private static float Quantile(List<int> sorted, double percentile)
            => sorted.Count == 0 ? 0f : sorted[(int)Math.Round(
                percentile * (sorted.Count - 1))];
    }

    [Serializable]
    public sealed class BalanceOutcomeStatistics
    {
        public BalanceHealthDistribution all = new BalanceHealthDistribution();
        public BalanceHealthDistribution victories = new BalanceHealthDistribution();
        public BalanceHealthDistribution defeats = new BalanceHealthDistribution();
        public BalanceHealthDistribution limits = new BalanceHealthDistribution();
        public BalanceRateInterval successRate95 = new BalanceRateInterval();
    }

    [Serializable]
    public sealed class BalanceHealthDistribution
    {
        public int samples;
        public float median;
        public float p10;
        public float p90;
    }

    [Serializable]
    public sealed class BalanceRateInterval
    {
        public int samples;
        public int successes;
        public float lower;
        public float upper;
    }
}
