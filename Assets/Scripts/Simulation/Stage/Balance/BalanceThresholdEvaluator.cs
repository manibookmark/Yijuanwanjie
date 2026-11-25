using System;
using System.Collections.Generic;
using System.Globalization;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Evaluates design-owned target relationships and converts findings into a
    /// stable terminal status. Measurement and policy remain separate concerns.
    /// </summary>
    internal static class BalanceThresholdEvaluator
    {
        public static void Evaluate(
            BalanceSimulationSuite suite,
            BalanceSimulationReport report,
            BalanceRunOptions options)
        {
            EvaluateChecks(suite, report);
            AddSimulationLimitGate(report);
            FinalizeStatus(report, options);
        }

        internal static void EvaluateChecks(
            BalanceSimulationSuite suite,
            BalanceSimulationReport report)
        {
            if (!HasDesignTargets(suite.Checks))
            {
                report.checks.Add(new BalanceCheckResult
                {
                    checkId = "BALANCE_TARGETS_REQUIRED",
                    status = "Fail",
                    severity = BalanceCheckSeverity.Warning.ToString(),
                    description = "The suite has no design-owned balance targets.",
                    details =
                        "Technical checks such as simulation-limit checks do not " +
                        "establish numerical acceptance."
                });
            }

            if (suite.Checks.Count == 0)
                return;

            for (int i = 0; i < suite.Checks.Count; i++)
            {
                BalanceCheckGroup group = suite.Checks[i];
                var outcomes = new List<bool>();
                var details = new List<string>();
                bool skipped = false;
                for (int j = 0; j < group.Conditions.Count; j++)
                {
                    BalanceCheckCondition condition = group.Conditions[j];
                    if (!TryEvaluateCondition(
                            condition,
                            report,
                            out bool passed,
                            out string detail))
                    {
                        skipped = true;
                        details.Add(detail);
                        break;
                    }
                    outcomes.Add(passed);
                    details.Add(detail);
                }

                if (skipped)
                {
                    report.checks.Add(new BalanceCheckResult
                    {
                        checkId = group.Id,
                        status = "Skipped",
                        severity = group.Severity.ToString(),
                        description = group.Description,
                        details = string.Join("; ", details)
                    });
                    continue;
                }

                bool groupPassed = group.Mode == BalanceCheckMode.All;
                if (group.Mode == BalanceCheckMode.All)
                {
                    for (int j = 0; j < outcomes.Count; j++)
                        groupPassed &= outcomes[j];
                }
                else
                {
                    groupPassed = false;
                    for (int j = 0; j < outcomes.Count; j++)
                        groupPassed |= outcomes[j];
                }
                report.checks.Add(new BalanceCheckResult
                {
                    checkId = group.Id,
                    status = groupPassed ? "Pass" : "Fail",
                    severity = group.Severity.ToString(),
                    description = group.Description,
                    details = string.Join("; ", details)
                });
            }
        }

        internal static bool HasDesignTargets(
            IReadOnlyList<BalanceCheckGroup> checks)
        {
            if (checks == null) return false;
            for (int i = 0; i < checks.Count; i++)
            {
                if (checks[i] != null &&
                    !IsTechnicalCheckId(checks[i].Id))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsTechnicalCheckId(string checkId)
        {
            return string.Equals(
                       checkId,
                       "SIMULATION_LIMITS_ZERO",
                       StringComparison.Ordinal) ||
                string.Equals(
                    checkId,
                    "pilot-no-simulation-limits",
                    StringComparison.Ordinal);
        }

        public static void FinalizeStatus(
            BalanceSimulationReport report,
            BalanceRunOptions options)
        {
            bool warningFailure = false;
            bool blockingFailure = false;
            for (int i = 0; i < report.checks.Count; i++)
            {
                BalanceCheckResult check = report.checks[i];
                bool failed = string.Equals(
                    check.status,
                    "Fail",
                    StringComparison.Ordinal);
                bool skipped = string.Equals(
                        check.status,
                        "Skipped",
                        StringComparison.Ordinal);
                if (!failed && !skipped)
                    continue;
                if (skipped && options.Preset == BalanceRunPreset.Quick)
                {
                    warningFailure = true;
                    continue;
                }
                if (string.Equals(
                        check.severity,
                        BalanceCheckSeverity.Blocking.ToString(),
                        StringComparison.Ordinal))
                {
                    blockingFailure = true;
                }
                else
                {
                    warningFailure = true;
                }
            }

            if (blockingFailure ||
                (warningFailure && options.StrictBalanceChecks))
            {
                report.status = "failed";
                report.exitCode = 4;
            }
            else if (warningFailure)
            {
                report.status = "warning";
                report.exitCode = 0;
            }
            else
            {
                report.status = "passed";
                report.exitCode = 0;
            }
        }

        private static bool TryEvaluateCondition(
            BalanceCheckCondition condition,
            BalanceSimulationReport report,
            out bool passed,
            out string details)
        {
            var observed = new List<float>();
            if (condition.Kind == BalanceCheckKind.Delta)
            {
                if (!TryGetMetric(
                        condition.Scope,
                        condition.Metric,
                        condition.BaselineId,
                        report,
                        out float baseline) ||
                    !TryGetMetric(
                        condition.Scope,
                        condition.Metric,
                        condition.CandidateId,
                        report,
                        out float candidate))
                {
                    passed = false;
                    details =
                        $"Skipped {condition.Metric}: preset did not execute " +
                        $"{condition.BaselineId}/{condition.CandidateId}.";
                    return false;
                }
                observed.Add(candidate - baseline);
            }
            else
            {
                for (int i = 0; i < condition.TargetIds.Count; i++)
                {
                    string targetId = condition.TargetIds[i];
                    if (!TryGetMetric(
                            condition.Scope,
                            condition.Metric,
                            targetId,
                            report,
                            out float value))
                    {
                        passed = false;
                        details =
                            $"Skipped {condition.Metric}: preset did not execute " +
                            $"{targetId}.";
                        return false;
                    }
                    observed.Add(value);
                }
                if (condition.Kind == BalanceCheckKind.Spread)
                {
                    float minimum = observed[0];
                    float maximum = observed[0];
                    for (int i = 1; i < observed.Count; i++)
                    {
                        minimum = Math.Min(minimum, observed[i]);
                        maximum = Math.Max(maximum, observed[i]);
                    }
                    observed.Clear();
                    observed.Add(maximum - minimum);
                }
            }

            float actual = observed[0];
            passed = Compare(
                actual,
                condition.Comparison,
                condition.Value,
                condition.UpperValue);
            details = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}.{2} observed={3:0.###}, target={4}",
                passed ? "PASS" : "FAIL",
                condition.Scope,
                condition.Metric,
                actual,
                DescribeComparison(condition));
            return true;
        }

        private static bool TryGetMetric(
            BalanceResultScope scope,
            BalanceMetric metric,
            string targetId,
            BalanceSimulationReport report,
            out float value)
        {
            if (scope == BalanceResultScope.Encounter)
            {
                for (int i = 0; i < report.isolatedEncounters.Count; i++)
                {
                    BalanceEncounterAggregate item =
                        report.isolatedEncounters[i];
                    if (!string.Equals(
                            item.targetId,
                            targetId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    value = GetEncounterMetric(item, metric);
                    return true;
                }
            }
            else if (scope == BalanceResultScope.Route)
            {
                for (int i = 0; i < report.routes.Count; i++)
                {
                    BalanceRouteAggregate item = report.routes[i];
                    if (!string.Equals(
                            item.routeId,
                            targetId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    value = GetRouteMetric(item, metric);
                    return true;
                }
            }
            else
            {
                for (int i = 0; i < report.routeEncounters.Count; i++)
                {
                    BalanceEncounterAggregate item =
                        report.routeEncounters[i];
                    string itemTarget =
                        BalanceSimulationSuite.RouteEncounterTargetId(
                            item.scopeId,
                            item.targetId);
                    if (!string.Equals(
                            itemTarget,
                            targetId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    value = GetEncounterMetric(item, metric);
                    return true;
                }
                if (metric == BalanceMetric.ReachRate)
                {
                    value = 0f;
                    return true;
                }
            }
            value = 0f;
            return false;
        }

        private static float GetEncounterMetric(
            BalanceEncounterAggregate item,
            BalanceMetric metric)
        {
            return metric switch
            {
                BalanceMetric.WinRate => item.winRate,
                BalanceMetric.PopulationWinRate => item.populationWinRate,
                BalanceMetric.ReachRate => item.reachRate,
                BalanceMetric.MedianTurns => item.medianTurns,
                BalanceMetric.MedianHpLost => item.medianHpLost,
                BalanceMetric.P90HpLost => item.p90HpLost,
                BalanceMetric.MedianEndingHealth => item.medianEndingHealth,
                BalanceMetric.SimulationLimitRate => item.simulationLimitRate,
                BalanceMetric.MedianCardsPlayed => item.medianCardsPlayed,
                BalanceMetric.MedianDamage => item.medianDamage,
                _ => throw new InvalidOperationException(
                    $"Metric {metric} is not valid for encounter results.")
            };
        }

        private static float GetRouteMetric(
            BalanceRouteAggregate item,
            BalanceMetric metric)
        {
            return metric switch
            {
                BalanceMetric.CompletionRate => item.completionRate,
                BalanceMetric.MedianTurns => item.medianTurns,
                BalanceMetric.MedianHpLost => item.medianHpLost,
                BalanceMetric.MedianEndingHealth => item.medianEndingHealth,
                BalanceMetric.P10EndingHealth => item.p10EndingHealth,
                BalanceMetric.SimulationLimitRate => item.simulationLimitRate,
                BalanceMetric.MedianCardsPlayed => item.medianCardsPlayed,
                BalanceMetric.MedianDamage => item.medianDamage,
                _ => throw new InvalidOperationException(
                    $"Metric {metric} is not valid for route results.")
            };
        }

        private static bool Compare(
            float actual,
            BalanceComparisonOperator comparison,
            float value,
            float upperValue)
        {
            const float epsilon = 0.00001f;
            return comparison switch
            {
                BalanceComparisonOperator.LessOrEqual => actual <= value + epsilon,
                BalanceComparisonOperator.GreaterOrEqual =>
                    actual + epsilon >= value,
                BalanceComparisonOperator.Between =>
                    actual + epsilon >= value && actual <= upperValue + epsilon,
                _ => false
            };
        }

        private static string DescribeComparison(BalanceCheckCondition condition)
        {
            return condition.Comparison switch
            {
                BalanceComparisonOperator.LessOrEqual =>
                    "<= " + condition.Value.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                BalanceComparisonOperator.GreaterOrEqual =>
                    ">= " + condition.Value.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture),
                _ => string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0:0.###},{1:0.###}]",
                    condition.Value,
                    condition.UpperValue)
            };
        }

        private static void AddSimulationLimitGate(BalanceSimulationReport report)
        {
            int limits = 0;
            for (int i = 0; i < report.isolatedEncounters.Count; i++)
                limits += report.isolatedEncounters[i].simulationLimits;
            for (int i = 0; i < report.routes.Count; i++)
                limits += report.routes[i].simulationLimits;
            report.checks.Add(new BalanceCheckResult
            {
                checkId = "SIMULATION_LIMITS_ZERO",
                status = limits == 0 ? "Pass" : "Fail",
                severity = BalanceCheckSeverity.Blocking.ToString(),
                description = "No run may end at a turn or command limit.",
                details = $"simulationLimits={limits}"
            });
        }
    }
}
