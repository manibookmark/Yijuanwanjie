using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    public enum BalanceRunPreset
    {
        Quick,
        Calibration
    }

    public enum BalanceCoverageMode
    {
        CombatOnly,
        ProductionRoute
    }

    public enum BalanceRoutePurpose
    {
        Probe,
        Branch,
        Full
    }

    public enum BalanceCheckMode
    {
        All,
        Any
    }

    public enum BalanceCheckSeverity
    {
        Warning,
        Blocking
    }

    public enum BalanceResultScope
    {
        Encounter,
        Route,
        RouteEncounter
    }

    public enum BalanceCheckKind
    {
        Value,
        Spread,
        Delta
    }

    public enum BalanceComparisonOperator
    {
        LessOrEqual,
        GreaterOrEqual,
        Between
    }

    public enum BalanceMetric
    {
        WinRate,
        PopulationWinRate,
        CompletionRate,
        MedianTurns,
        MedianHpLost,
        P90HpLost,
        MedianEndingHealth,
        P10EndingHealth,
        SimulationLimitRate,
        MedianCardsPlayed,
        MedianDamage,
        ReachRate
    }

    /// <summary>
    /// One invocation's sampling contract. Preset defaults live here so the CLI,
    /// tests, and future editor menu cannot silently disagree about run size.
    /// </summary>
    public sealed class BalanceRunOptions
    {
        public const int QuickDefaultCount = 100;
        public const int CalibrationDefaultCount = 1000;
        public const int DefaultMaxTurnsPerEncounter = 100;
        public const int MaximumCountPerTarget = 100000;
        public const long MaximumCombatRuns = 250000;

        public BalanceRunOptions(
            BalanceRunPreset preset,
            int seedStart,
            int count = 0,
            int maxTurnsPerEncounter = DefaultMaxTurnsPerEncounter,
            bool strictBalanceChecks = false)
        {
            Preset = preset;
            SeedStart = seedStart;
            Count = count > 0
                ? count
                : preset == BalanceRunPreset.Quick
                    ? QuickDefaultCount
                    : CalibrationDefaultCount;
            MaxTurnsPerEncounter = maxTurnsPerEncounter;
            StrictBalanceChecks = strictBalanceChecks ||
                preset == BalanceRunPreset.Calibration;

            if (Count <= 0 || Count > MaximumCountPerTarget)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (MaxTurnsPerEncounter <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTurnsPerEncounter));
            checked
            {
                _ = SeedStart + Count - 1;
            }
        }

        public BalanceRunPreset Preset { get; }
        public int SeedStart { get; }
        public int Count { get; }
        public int MaxTurnsPerEncounter { get; }
        public bool StrictBalanceChecks { get; }

        internal void EnsureWithinWorkBudget(long combatRunsPerSeed)
        {
            if (combatRunsPerSeed <= 0)
                throw new ArgumentOutOfRangeException(nameof(combatRunsPerSeed));
            long total;
            checked
            {
                total = combatRunsPerSeed * Count;
            }
            if (total > MaximumCombatRuns)
            {
                throw new BalanceSuiteException(
                    $"Run requests {total} combat samples, exceeding the " +
                    $"safety budget {MaximumCombatRuns}. Reduce -Count or " +
                    "split the suite into independently reviewed batches.");
            }
        }
    }

    public sealed class BalanceSimulationSuite
    {
        public const int CurrentSchemaVersion = 2;
        public const int MinimumSchemaVersion = 1;
        private static readonly Regex StableIdPattern = new Regex(
            "^[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)+$",
            RegexOptions.CultureInvariant);

        private BalanceSimulationSuite(
            string suiteId,
            int revision,
            string floorContentId,
            string floorResourcePath,
            string snapshotId,
            BalanceCoverageMode coverageMode,
            bool usesDesignSequence,
            BalanceEntryState entryState,
            BalanceEntryRule entryRule,
            BalanceVersionedInput agent,
            BalanceVersionedInput rewardPolicy,
            IReadOnlyList<BalanceEncounterDefinition> isolatedEncounters,
            IReadOnlyList<BalanceRouteDefinition> routes,
            IReadOnlyList<BalanceCoverageExclusion> coverageExclusions,
            IReadOnlyList<BalanceCheckGroup> checks,
            string sourceSha256)
        {
            SuiteId = suiteId;
            Revision = revision;
            FloorContentId = floorContentId;
            FloorResourcePath = floorResourcePath;
            SnapshotId = snapshotId;
            CoverageMode = coverageMode;
            UsesDesignSequence = usesDesignSequence;
            EntryState = entryState;
            EntryRule = entryRule;
            Agent = agent;
            RewardPolicy = rewardPolicy;
            IsolatedEncounters = isolatedEncounters;
            Routes = routes;
            CoverageExclusions = coverageExclusions;
            Checks = checks;
            SourceSha256 = sourceSha256;
        }

        public string SuiteId { get; }
        public int Revision { get; }
        public string FloorContentId { get; }
        public string FloorResourcePath { get; }
        public string SnapshotId { get; }
        public BalanceCoverageMode CoverageMode { get; }
        // Opt-in experiment semantics; production suites keep runtime prerequisites.
        public bool UsesDesignSequence { get; }
        public BalanceEntryState EntryState { get; }
        public BalanceEntryRule EntryRule { get; }
        public BalanceVersionedInput Agent { get; }
        public BalanceVersionedInput RewardPolicy { get; }
        public IReadOnlyList<BalanceEncounterDefinition> IsolatedEncounters { get; }
        public IReadOnlyList<BalanceRouteDefinition> Routes { get; }
        public IReadOnlyList<BalanceCoverageExclusion> CoverageExclusions { get; }
        public IReadOnlyList<BalanceCheckGroup> Checks { get; }
        public string SourceSha256 { get; }

        /// <summary>
        /// Compiles mutable JSON DTOs into a validated immutable experiment.
        /// Production values remain referenced only by stable IDs; the suite does
        /// not copy enemy, card, reward, or scenario numbers.
        /// </summary>
        public static BalanceSimulationSuite CompileJson(
            string json,
            string projectRoot = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new BalanceSuiteException("Suite JSON cannot be empty.");

            BalanceJsonContractValidator.Validate(json);

            BalanceSuiteDocument document;
            try
            {
                document = JsonUtility.FromJson<BalanceSuiteDocument>(json);
            }
            catch (Exception exception)
            {
                throw new BalanceSuiteException(
                    $"Cannot parse suite JSON: {exception.Message}");
            }
            if (document == null)
                throw new BalanceSuiteException("Suite JSON produced no document.");

            var errors = new List<string>();
            if (document.schemaVersion < MinimumSchemaVersion ||
                document.schemaVersion > CurrentSchemaVersion)
            {
                errors.Add(
                    $"schemaVersion must be between {MinimumSchemaVersion} " +
                    $"and {CurrentSchemaVersion}, got {document.schemaVersion}.");
            }
            RequireStableId(document.suiteId, "suiteId", errors);
            RequirePositive(document.revision, "revision", errors);
            RequireStableId(document.floorContentId, "floorContentId", errors);
            RequireText(document.floorResourcePath, "floorResourcePath", errors);
            ValidateResourcePath(
                document.floorResourcePath,
                "floorResourcePath",
                errors);
            RequireStableId(document.snapshotId, "snapshotId", errors);

            if (!TryParseEnum(
                    document.coverageMode,
                    out BalanceCoverageMode coverageMode))
            {
                errors.Add(
                    $"coverageMode is unsupported: {document.coverageMode}. " +
                    "Supported values are CombatOnly and ProductionRoute.");
            }
            if (document.schemaVersion == 1 &&
                coverageMode != BalanceCoverageMode.CombatOnly)
            {
                errors.Add("schemaVersion 1 supports CombatOnly only.");
            }
            if (document.schemaVersion == 2 &&
                coverageMode != BalanceCoverageMode.ProductionRoute)
            {
                errors.Add("schemaVersion 2 requires ProductionRoute.");
            }

            BalanceEntryState entryState = CompileEntryState(
                document.entryState,
                projectRoot,
                errors);
            bool usesDesignSequence = string.Equals(
                document.routeExecution, "DesignSequence", StringComparison.Ordinal);
            if (document.routeExecution != null &&
                !string.Equals(document.routeExecution, "Production", StringComparison.Ordinal) &&
                !usesDesignSequence)
                errors.Add("routeExecution must be Production or DesignSequence.");
            BalanceEntryRule entry = CompileEntry(document.entryRule, errors);
            BalanceVersionedInput agent = CompileVersionedInput(
                document.agent,
                "agent",
                errors);
            BalanceVersionedInput rewardPolicy = CompileVersionedInput(
                document.rewardPolicy,
                "rewardPolicy",
                errors);

            var targetIds = new HashSet<string>(StringComparer.Ordinal);
            var encounterTargetIds = new HashSet<string>(StringComparer.Ordinal);
            var routeTargetIds = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<BalanceEncounterDefinition> encounters =
                CompileEncounters(
                    document.isolatedEncounters,
                    targetIds,
                    encounterTargetIds,
                    errors);
            IReadOnlyList<BalanceRouteDefinition> routes =
                BalanceRouteCompiler.Compile(
                    document.schemaVersion,
                    document.routes,
                    targetIds,
                    routeTargetIds,
                    errors);
            if (encounters.Count == 0 && routes.Count == 0)
                errors.Add("Suite must define at least one encounter or route.");

            IReadOnlyList<BalanceCoverageExclusion> exclusions =
                CompileExclusions(document.coverageExclusions, errors);
            IReadOnlyList<BalanceCheckGroup> checks = CompileChecks(
                document.checks,
                encounterTargetIds,
                routeTargetIds,
                BuildRouteEncounterTargetIds(routes),
                errors);
            BalanceSuiteSemanticValidator.Validate(checks, errors);

            if (errors.Count > 0)
            {
                throw new BalanceSuiteException(
                    "Balance suite is invalid:" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors));
            }

            return new BalanceSimulationSuite(
                document.suiteId,
                document.revision,
                document.floorContentId,
                document.floorResourcePath,
                document.snapshotId,
                coverageMode,
                usesDesignSequence,
                entryState,
                entry,
                agent,
                rewardPolicy,
                encounters,
                routes,
                exclusions,
                checks,
                ComputeSha256(json));
        }

        private static BalanceEntryState CompileEntryState(
            BalanceEntryStateDocument document,
            string projectRoot,
            ICollection<string> errors)
        {
            if (document == null)
            {
                errors.Add("entryState is required.");
                return null;
            }
            if (!TryParseEnum(document.kind, out BalanceEntryStateKind kind))
            {
                errors.Add($"entryState.kind is invalid: {document.kind}.");
            }
            RequirePositive(document.revision, "entryState.revision", errors);

            string checkpointPayload = null;
            string checkpointSha256 = string.Empty;
            if (kind == BalanceEntryStateKind.Checkpoint)
            {
                ValidateProjectRelativePath(
                    document.checkpointFile,
                    "entryState.checkpointFile",
                    errors);
                if (string.IsNullOrWhiteSpace(projectRoot))
                {
                    errors.Add(
                        "projectRoot is required to compile a checkpoint suite.");
                }
                else if (!string.IsNullOrWhiteSpace(document.checkpointFile))
                {
                    try
                    {
                        string root = Path.GetFullPath(projectRoot);
                        string fullPath = Path.GetFullPath(Path.Combine(
                            root,
                            document.checkpointFile));
                        if (!IsWithin(fullPath, root))
                        {
                            errors.Add(
                                "entryState.checkpointFile escapes projectRoot.");
                        }
                        else if (!File.Exists(fullPath))
                        {
                            errors.Add(
                                "Entry checkpoint does not exist: " +
                                document.checkpointFile + ".");
                        }
                        else
                        {
                            checkpointPayload = File.ReadAllText(fullPath);
                            checkpointSha256 = ComputeSha256(checkpointPayload);
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Add(
                            "Cannot read entry checkpoint: " + exception.Message);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(document.checkpointFile))
            {
                errors.Add(
                    "entryState.checkpointFile is only valid for Checkpoint.");
            }

            return new BalanceEntryState(
                kind,
                document.revision,
                document.checkpointFile,
                checkpointSha256,
                checkpointPayload);
        }

        private static BalanceEntryRule CompileEntry(
            BalanceEntryRuleDocument document,
            ICollection<string> errors)
        {
            if (document == null)
            {
                errors.Add("entryRule is required.");
                return null;
            }
            RequireStableId(document.id, "entryRule.id", errors);
            RequirePositive(document.revision, "entryRule.revision", errors);
            RequireStableId(
                document.selectedHeroContentId,
                "entryRule.selectedHeroContentId",
                errors);
            if (document.rules == null)
                errors.Add("entryRule.rules is required.");

            CombatRuleSet rules = document.rules?.CreateRules() ??
                new CombatRuleSet();
            if (rules.knockbackBaseChance < 0f ||
                rules.knockbackBaseChance > 1f)
            {
                errors.Add("entryRule.rules.knockbackBaseChance must be in [0,1].");
            }
            if (rules.disarmBaseChance < 0f ||
                rules.disarmBaseChance > 1f)
            {
                errors.Add("entryRule.rules.disarmBaseChance must be in [0,1].");
            }
            return new BalanceEntryRule(
                document.id,
                document.revision,
                document.selectedHeroContentId,
                rules);
        }

        private static BalanceVersionedInput CompileVersionedInput(
            BalanceVersionedInputDocument document,
            string label,
            ICollection<string> errors)
        {
            if (document == null)
            {
                errors.Add($"{label} is required.");
                return null;
            }
            RequireText(document.id, $"{label}.id", errors);
            RequirePositive(document.revision, $"{label}.revision", errors);
            return new BalanceVersionedInput(document.id, document.revision);
        }

        private static IReadOnlyList<BalanceEncounterDefinition> CompileEncounters(
            BalanceEncounterDocument[] documents,
            ISet<string> targetIds,
            ISet<string> encounterTargetIds,
            ICollection<string> errors)
        {
            var result = new List<BalanceEncounterDefinition>();
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            if (documents == null) return result.AsReadOnly();
            for (int i = 0; i < documents.Length; i++)
            {
                BalanceEncounterDocument item = documents[i];
                if (item == null)
                {
                    errors.Add($"isolatedEncounters[{i}] is null.");
                    continue;
                }
                RequireText(item.id, $"isolatedEncounters[{i}].id", errors);
                RequireStableId(
                    item.nodeContentId,
                    $"isolatedEncounters[{i}].nodeContentId",
                    errors);
                if (!string.IsNullOrWhiteSpace(item.id) &&
                    !targetIds.Add(item.id))
                {
                    errors.Add($"Duplicate simulation target id: {item.id}.");
                }
                else if (!string.IsNullOrWhiteSpace(item.id))
                {
                    encounterTargetIds.Add(item.id);
                }
                if (!string.IsNullOrWhiteSpace(item.nodeContentId) &&
                    !nodeIds.Add(item.nodeContentId))
                {
                    errors.Add(
                        $"Duplicate isolated encounter node: {item.nodeContentId}.");
                }
                result.Add(new BalanceEncounterDefinition(
                    item.id,
                    item.nodeContentId,
                    item.quick));
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<BalanceCoverageExclusion> CompileExclusions(
            BalanceCoverageExclusionDocument[] documents,
            ICollection<string> errors)
        {
            var result = new List<BalanceCoverageExclusion>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (documents == null) return result.AsReadOnly();
            for (int i = 0; i < documents.Length; i++)
            {
                BalanceCoverageExclusionDocument item = documents[i];
                if (item == null)
                {
                    errors.Add($"coverageExclusions[{i}] is null.");
                    continue;
                }
                RequireStableId(
                    item.nodeContentId,
                    $"coverageExclusions[{i}].nodeContentId",
                    errors);
                RequireText(
                    item.reason,
                    $"coverageExclusions[{i}].reason",
                    errors);
                if (!string.IsNullOrWhiteSpace(item.nodeContentId) &&
                    !seen.Add(item.nodeContentId))
                {
                    errors.Add(
                        $"Duplicate coverage exclusion: {item.nodeContentId}.");
                }
                result.Add(new BalanceCoverageExclusion(
                    item.nodeContentId,
                    item.reason));
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<BalanceCheckGroup> CompileChecks(
            BalanceCheckGroupDocument[] documents,
            ISet<string> encounterTargetIds,
            ISet<string> routeTargetIds,
            ISet<string> routeEncounterTargetIds,
            ICollection<string> errors)
        {
            var result = new List<BalanceCheckGroup>();
            var checkIds = new HashSet<string>(StringComparer.Ordinal);
            if (documents == null) return result.AsReadOnly();
            for (int i = 0; i < documents.Length; i++)
            {
                BalanceCheckGroupDocument item = documents[i];
                if (item == null)
                {
                    errors.Add($"checks[{i}] is null.");
                    continue;
                }
                RequireText(item.id, $"checks[{i}].id", errors);
                if (!string.IsNullOrWhiteSpace(item.id) &&
                    !checkIds.Add(item.id))
                {
                    errors.Add($"Duplicate check id: {item.id}.");
                }
                if (!TryParseEnum(item.mode, out BalanceCheckMode mode))
                    errors.Add($"Check {item.id} has invalid mode {item.mode}.");
                if (!TryParseEnum(
                        item.severity,
                        out BalanceCheckSeverity severity))
                {
                    errors.Add(
                        $"Check {item.id} has invalid severity {item.severity}.");
                }

                var conditions = new List<BalanceCheckCondition>();
                if (item.conditions == null || item.conditions.Length == 0)
                {
                    errors.Add($"Check {item.id} must contain conditions.");
                }
                else
                {
                    for (int j = 0; j < item.conditions.Length; j++)
                    {
                        BalanceCheckCondition condition = CompileCondition(
                            item.id,
                            j,
                            item.conditions[j],
                            encounterTargetIds,
                            routeTargetIds,
                            routeEncounterTargetIds,
                            errors);
                        if (condition != null) conditions.Add(condition);
                    }
                }
                result.Add(new BalanceCheckGroup(
                    item.id,
                    mode,
                    severity,
                    item.description ?? string.Empty,
                    conditions.AsReadOnly()));
            }
            return result.AsReadOnly();
        }

        private static BalanceCheckCondition CompileCondition(
            string checkId,
            int index,
            BalanceCheckConditionDocument item,
            ISet<string> encounterTargetIds,
            ISet<string> routeTargetIds,
            ISet<string> routeEncounterTargetIds,
            ICollection<string> errors)
        {
            string label = $"checks[{checkId}].conditions[{index}]";
            if (item == null)
            {
                errors.Add($"{label} is null.");
                return null;
            }
            bool valid = true;
            if (!TryParseEnum(item.kind, out BalanceCheckKind kind))
            {
                errors.Add($"{label}.kind is invalid: {item.kind}.");
                valid = false;
            }
            if (!TryParseEnum(item.scope, out BalanceResultScope scope))
            {
                errors.Add($"{label}.scope is invalid: {item.scope}.");
                valid = false;
            }
            if (!TryParseEnum(item.metric, out BalanceMetric metric))
            {
                errors.Add($"{label}.metric is invalid: {item.metric}.");
                valid = false;
            }
            if (!TryParseEnum(
                    item.comparison,
                    out BalanceComparisonOperator comparison))
            {
                errors.Add(
                    $"{label}.comparison is invalid: {item.comparison}.");
                valid = false;
            }
            if (valid && !IsMetricCompatible(scope, metric))
            {
                errors.Add($"{label}: metric {metric} is not valid for {scope}.");
                valid = false;
            }
            if (valid && !BalanceThresholdDomainValidator.Validate(
                    label,
                    kind,
                    metric,
                    comparison,
                    item.value,
                    item.upperValue,
                    errors))
                valid = false;

            ISet<string> scopedTargets = scope switch
            {
                BalanceResultScope.Encounter => encounterTargetIds,
                BalanceResultScope.Route => routeTargetIds,
                BalanceResultScope.RouteEncounter => routeEncounterTargetIds,
                _ => new HashSet<string>(StringComparer.Ordinal)
            };
            var targets = new List<string>();
            var uniqueTargets = new HashSet<string>(StringComparer.Ordinal);
            if (item.targetIds != null)
            {
                for (int i = 0; i < item.targetIds.Length; i++)
                {
                    string target = item.targetIds[i];
                    if (!scopedTargets.Contains(target))
                    {
                        errors.Add(
                            $"{label} references {target}, which is not a " +
                            $"{scope} target.");
                        valid = false;
                    }
                    if (!uniqueTargets.Add(target))
                    {
                        errors.Add($"{label} repeats target {target}.");
                        valid = false;
                    }
                    targets.Add(target);
                }
            }

            if (kind == BalanceCheckKind.Value && targets.Count != 1)
            {
                errors.Add($"{label}: Value requires exactly one targetIds entry.");
                valid = false;
            }
            if (kind == BalanceCheckKind.Spread && targets.Count < 2)
            {
                errors.Add($"{label}: Spread requires at least two targets.");
                valid = false;
            }
            if (kind == BalanceCheckKind.Delta)
            {
                if (targets.Count > 0)
                {
                    errors.Add($"{label}: Delta must not define targetIds.");
                    valid = false;
                }
                if (!scopedTargets.Contains(item.baselineId) ||
                    !scopedTargets.Contains(item.candidateId))
                {
                    errors.Add(
                        $"{label}: Delta baseline/candidate must reference targets.");
                    valid = false;
                }
                if (string.Equals(
                        item.baselineId,
                        item.candidateId,
                        StringComparison.Ordinal))
                {
                    errors.Add($"{label}: Delta targets must differ.");
                    valid = false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.baselineId) ||
                     !string.IsNullOrWhiteSpace(item.candidateId))
            {
                errors.Add(
                    $"{label}: baselineId/candidateId are only valid for Delta.");
                valid = false;
            }

            return valid
                ? new BalanceCheckCondition(
                    kind,
                    scope,
                    metric,
                    comparison,
                    item.value,
                    item.upperValue,
                    targets.AsReadOnly(),
                    item.baselineId,
                    item.candidateId)
                : null;
        }

        internal static bool IsMetricCompatible(
            BalanceResultScope scope,
            BalanceMetric metric)
        {
            if (scope == BalanceResultScope.Encounter ||
                scope == BalanceResultScope.RouteEncounter)
            {
                return metric == BalanceMetric.WinRate ||
                    (scope == BalanceResultScope.RouteEncounter &&
                     (metric == BalanceMetric.PopulationWinRate ||
                      metric == BalanceMetric.ReachRate)) ||
                    metric == BalanceMetric.MedianTurns ||
                    metric == BalanceMetric.MedianHpLost ||
                    metric == BalanceMetric.P90HpLost ||
                    metric == BalanceMetric.MedianEndingHealth ||
                    metric == BalanceMetric.SimulationLimitRate ||
                    metric == BalanceMetric.MedianCardsPlayed ||
                    metric == BalanceMetric.MedianDamage;
            }
            return metric == BalanceMetric.CompletionRate ||
                metric == BalanceMetric.MedianTurns ||
                metric == BalanceMetric.MedianHpLost ||
                metric == BalanceMetric.MedianEndingHealth ||
                metric == BalanceMetric.P10EndingHealth ||
                metric == BalanceMetric.SimulationLimitRate ||
                metric == BalanceMetric.MedianCardsPlayed ||
                metric == BalanceMetric.MedianDamage;
        }

        private static ISet<string> BuildRouteEncounterTargetIds(
            IReadOnlyList<BalanceRouteDefinition> routes)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < routes.Count; i++)
            {
                BalanceRouteDefinition route = routes[i];
                for (int j = 0; j < route.NodeContentIds.Count; j++)
                {
                    result.Add(RouteEncounterTargetId(
                        route.Id,
                        route.NodeContentIds[j]));
                }
            }
            return result;
        }

        internal static string RouteEncounterTargetId(
            string routeId,
            string nodeContentId)
        {
            return routeId + "@" + nodeContentId;
        }

        private static bool TryParseEnum<T>(string value, out T parsed)
            where T : struct
        {
            return Enum.TryParse(value, true, out parsed) &&
                Enum.IsDefined(typeof(T), parsed);
        }

        private static void RequireStableId(
            string value,
            string label,
            ICollection<string> errors)
        {
            RequireText(value, label, errors);
            if (!string.IsNullOrWhiteSpace(value) &&
                !StableIdPattern.IsMatch(value))
            {
                errors.Add(
                    $"{label} must be a stable dotted id without empty/path " +
                    $"segments, got {value}.");
            }
        }

        private static void ValidateResourcePath(
            string value,
            string label,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (Path.IsPathRooted(value) || value.IndexOf('\\') >= 0)
            {
                errors.Add($"{label} must be a Resources-relative '/' path.");
                return;
            }
            string[] segments = value.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(segments[i]) ||
                    segments[i] == "." || segments[i] == "..")
                {
                    errors.Add($"{label} contains an unsafe path segment.");
                    return;
                }
            }
        }

        private static void ValidateProjectRelativePath(
            string value,
            string label,
            ICollection<string> errors)
        {
            RequireText(value, label, errors);
            if (string.IsNullOrWhiteSpace(value)) return;
            if (Path.IsPathRooted(value))
            {
                errors.Add($"{label} must be project-relative.");
                return;
            }
            string normalized = value.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(segments[i]) ||
                    segments[i] == "." || segments[i] == "..")
                {
                    errors.Add($"{label} contains an unsafe path segment.");
                    return;
                }
            }
        }

        private static bool IsWithin(string path, string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(path);
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256(string value)
        {
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(
                Encoding.UTF8.GetBytes(value ?? string.Empty));
            var text = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
                text.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static void RequireText(
            string value,
            string label,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add($"{label} cannot be empty.");
        }

        private static void RequirePositive(
            int value,
            string label,
            ICollection<string> errors)
        {
            if (value <= 0) errors.Add($"{label} must be positive.");
        }
    }

    public sealed class BalanceEntryRule
    {
        private readonly CombatRuleSet _rules;

        internal BalanceEntryRule(
            string id,
            int revision,
            string selectedHeroContentId,
            CombatRuleSet rules)
        {
            Id = id;
            Revision = revision;
            SelectedHeroContentId = selectedHeroContentId;
            _rules = rules.Clone();
        }

        public string Id { get; }
        public int Revision { get; }
        public string SelectedHeroContentId { get; }

        public StageSimulationRuleInput CreateStageInput()
        {
            return new StageSimulationRuleInput(
                Id,
                Revision,
                SelectedHeroContentId,
                _rules);
        }

        internal CombatRuleSet CreateRules() => _rules.Clone();
    }

    public sealed class BalanceVersionedInput
    {
        internal BalanceVersionedInput(string id, int revision)
        {
            Id = id;
            Revision = revision;
        }

        public string Id { get; }
        public int Revision { get; }
    }

    public sealed class BalanceEncounterDefinition
    {
        internal BalanceEncounterDefinition(
            string id,
            string nodeContentId,
            bool quick)
        {
            Id = id;
            NodeContentId = nodeContentId;
            Quick = quick;
        }

        public string Id { get; }
        public string NodeContentId { get; }
        public bool Quick { get; }
    }

    public sealed class BalanceRouteDefinition
    {
        internal BalanceRouteDefinition(
            string id,
            int revision,
            BalanceRoutePurpose purpose,
            IReadOnlyList<StageSimulationStep> steps,
            bool quick)
        {
            Id = id;
            Revision = revision;
            Purpose = purpose;
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            var combatNodeIds = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Kind == StageSimulationStepKind.Combat)
                    combatNodeIds.Add(steps[i].NodeContentId);
            }
            NodeContentIds = combatNodeIds.AsReadOnly();
            Quick = quick;
        }

        public string Id { get; }
        public int Revision { get; }
        public BalanceRoutePurpose Purpose { get; }
        public IReadOnlyList<StageSimulationStep> Steps { get; }
        /// <summary>Combat-only projection retained for metrics and v1 callers.</summary>
        public IReadOnlyList<string> NodeContentIds { get; }
        public bool Quick { get; }

        internal StageSimulationRoute CreateStageRoute()
        {
            return new StageSimulationRoute(Id, Revision, Steps);
        }
    }

    public sealed class BalanceCoverageExclusion
    {
        internal BalanceCoverageExclusion(string nodeContentId, string reason)
        {
            NodeContentId = nodeContentId;
            Reason = reason;
        }

        public string NodeContentId { get; }
        public string Reason { get; }
    }

    public sealed class BalanceCheckGroup
    {
        internal BalanceCheckGroup(
            string id,
            BalanceCheckMode mode,
            BalanceCheckSeverity severity,
            string description,
            IReadOnlyList<BalanceCheckCondition> conditions)
        {
            Id = id;
            Mode = mode;
            Severity = severity;
            Description = description;
            Conditions = conditions;
        }

        public string Id { get; }
        public BalanceCheckMode Mode { get; }
        public BalanceCheckSeverity Severity { get; }
        public string Description { get; }
        public IReadOnlyList<BalanceCheckCondition> Conditions { get; }
    }

    public sealed class BalanceCheckCondition
    {
        internal BalanceCheckCondition(
            BalanceCheckKind kind,
            BalanceResultScope scope,
            BalanceMetric metric,
            BalanceComparisonOperator comparison,
            float value,
            float upperValue,
            IReadOnlyList<string> targetIds,
            string baselineId,
            string candidateId)
        {
            Kind = kind;
            Scope = scope;
            Metric = metric;
            Comparison = comparison;
            Value = value;
            UpperValue = upperValue;
            TargetIds = targetIds;
            BaselineId = baselineId;
            CandidateId = candidateId;
        }

        public BalanceCheckKind Kind { get; }
        public BalanceResultScope Scope { get; }
        public BalanceMetric Metric { get; }
        public BalanceComparisonOperator Comparison { get; }
        public float Value { get; }
        public float UpperValue { get; }
        public IReadOnlyList<string> TargetIds { get; }
        public string BaselineId { get; }
        public string CandidateId { get; }
    }

    public sealed class BalanceSuiteException : Exception
    {
        public BalanceSuiteException(string message) : base(message) { }
    }

}
