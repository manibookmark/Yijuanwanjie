using System;
using System.Collections.Generic;
using System.Globalization;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Rejects mathematically vacuous or non-finite Suite thresholds before
    /// any samples run. Result lookup and pass/fail evaluation remain owned by
    /// BalanceThresholdEvaluator.
    /// </summary>
    internal static class BalanceThresholdDomainValidator
    {
        public static bool Validate(
            string label,
            BalanceCheckKind kind,
            BalanceMetric metric,
            BalanceComparisonOperator comparison,
            float value,
            float upperValue,
            ICollection<string> errors)
        {
            bool valid = true;
            if (!IsFinite(value) ||
                (comparison == BalanceComparisonOperator.Between &&
                 !IsFinite(upperValue)))
            {
                errors.Add($"{label}: threshold values must be finite.");
                valid = false;
            }
            if (comparison == BalanceComparisonOperator.Between &&
                upperValue < value)
            {
                errors.Add($"{label}: upperValue must be >= value.");
                valid = false;
            }
            if (!IsRatioMetric(metric)) return valid;

            float minimum = kind == BalanceCheckKind.Delta ? -1f : 0f;
            const float Maximum = 1f;
            if (value < minimum || value > Maximum)
            {
                errors.Add(
                    $"{label}: {metric} {kind} value must be in " +
                    $"[{Number(minimum)},{Number(Maximum)}].");
                valid = false;
            }
            if (comparison == BalanceComparisonOperator.Between &&
                (upperValue < minimum || upperValue > Maximum))
            {
                errors.Add(
                    $"{label}: {metric} {kind} upperValue must be in " +
                    $"[{Number(minimum)},{Number(Maximum)}].");
                valid = false;
            }
            return valid;
        }

        private static bool IsRatioMetric(BalanceMetric metric)
        {
            return metric == BalanceMetric.WinRate ||
                metric == BalanceMetric.PopulationWinRate ||
                metric == BalanceMetric.CompletionRate ||
                metric == BalanceMetric.SimulationLimitRate ||
                metric == BalanceMetric.ReachRate;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string Number(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
