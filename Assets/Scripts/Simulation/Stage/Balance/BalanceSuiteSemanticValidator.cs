using System;
using System.Collections.Generic;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Enforces relationships that span multiple otherwise-valid Suite checks.
    /// In particular, conditional RouteEncounter evidence is never accepted
    /// without an independently enforced population reach-rate floor.
    /// </summary>
    internal static class BalanceSuiteSemanticValidator
    {
        public static void Validate(
            IReadOnlyList<BalanceCheckGroup> checks,
            ICollection<string> errors)
        {
            var conditionalTargets = new HashSet<string>(StringComparer.Ordinal);
            var guardedTargets = new HashSet<string>(StringComparer.Ordinal);

            for (int groupIndex = 0;
                 groupIndex < checks.Count;
                 groupIndex++)
            {
                BalanceCheckGroup group = checks[groupIndex];
                for (int conditionIndex = 0;
                     conditionIndex < group.Conditions.Count;
                     conditionIndex++)
                {
                    BalanceCheckCondition condition =
                        group.Conditions[conditionIndex];
                    if (condition.Scope != BalanceResultScope.RouteEncounter)
                        continue;

                    if (condition.Metric != BalanceMetric.ReachRate)
                    {
                        AddTargets(condition, conditionalTargets);
                        continue;
                    }

                    bool unconditional =
                        group.Mode == BalanceCheckMode.All ||
                        group.Conditions.Count == 1;
                    bool validGate =
                        condition.Kind == BalanceCheckKind.Value &&
                        condition.Comparison ==
                            BalanceComparisonOperator.GreaterOrEqual &&
                        condition.TargetIds.Count == 1 &&
                        condition.Value > 0f &&
                        condition.Value <= 1f &&
                        unconditional;
                    if (!validGate)
                    {
                        errors.Add(
                            $"RouteEncounter ReachRate check {group.Id} must " +
                            "be an unconditional Value/GreaterOrEqual gate " +
                            "with exactly one target and value in (0,1].");
                        continue;
                    }
                    guardedTargets.Add(condition.TargetIds[0]);
                }
            }

            foreach (string target in conditionalTargets)
            {
                if (!guardedTargets.Contains(target))
                {
                    errors.Add(
                        $"RouteEncounter target {target} requires an explicit " +
                        "unconditional ReachRate minimum before its conditional " +
                        "combat metrics can be used.");
                }
            }
        }

        private static void AddTargets(
            BalanceCheckCondition condition,
            ISet<string> targets)
        {
            if (condition.Kind == BalanceCheckKind.Delta)
            {
                if (!string.IsNullOrWhiteSpace(condition.BaselineId))
                    targets.Add(condition.BaselineId);
                if (!string.IsNullOrWhiteSpace(condition.CandidateId))
                    targets.Add(condition.CandidateId);
                return;
            }

            for (int i = 0; i < condition.TargetIds.Count; i++)
                targets.Add(condition.TargetIds[i]);
        }
    }
}
