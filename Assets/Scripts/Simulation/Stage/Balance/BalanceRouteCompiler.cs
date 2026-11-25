using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Owns route-schema evolution. Schema v1 remains an encounter-only
    /// compatibility contract; schema v2 freezes ordered production steps and
    /// their explicit card/reward decisions.
    /// </summary>
    internal static class BalanceRouteCompiler
    {
        private static readonly Regex StableIdPattern = new Regex(
            "^[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)+$",
            RegexOptions.CultureInvariant);

        internal static IReadOnlyList<BalanceRouteDefinition> Compile(
            int schemaVersion,
            BalanceRouteDocument[] documents,
            ISet<string> targetIds,
            ISet<string> routeTargetIds,
            ICollection<string> errors)
        {
            var result = new List<BalanceRouteDefinition>();
            if (documents == null) return result.AsReadOnly();
            for (int i = 0; i < documents.Length; i++)
            {
                BalanceRouteDocument item = documents[i];
                if (item == null)
                {
                    errors.Add($"routes[{i}] is null.");
                    continue;
                }

                RequireText(item.id, $"routes[{i}].id", errors);
                if (item.revision <= 0)
                    errors.Add($"routes[{i}].revision must be positive.");
                if (!TryParseEnum(
                        item.purpose,
                        out BalanceRoutePurpose purpose))
                {
                    errors.Add(
                        $"Route {item.id} has invalid purpose {item.purpose}.");
                }
                RegisterTarget(item.id, targetIds, routeTargetIds, errors);

                IReadOnlyList<StageSimulationStep> steps =
                    schemaVersion <= 1
                        ? CompileLegacyRoute(item, i, errors)
                        : CompileTypedRoute(item, i, errors);
                result.Add(new BalanceRouteDefinition(
                    item.id,
                    item.revision,
                    purpose,
                    steps,
                    item.quick));
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<StageSimulationStep> CompileLegacyRoute(
            BalanceRouteDocument item,
            int routeIndex,
            ICollection<string> errors)
        {
            var result = new List<StageSimulationStep>();
            if (item.steps != null)
            {
                errors.Add(
                    $"Route {item.id} uses steps, but schemaVersion 1 requires " +
                    "nodeContentIds.");
            }
            if (item.nodeContentIds == null ||
                item.nodeContentIds.Length == 0)
            {
                errors.Add($"Route {item.id} must contain combat nodes.");
                return result.AsReadOnly();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < item.nodeContentIds.Length; i++)
            {
                string nodeId = item.nodeContentIds[i];
                RequireStableId(
                    nodeId,
                    $"routes[{routeIndex}].nodeContentIds[{i}]",
                    errors);
                RegisterNode(item.id, nodeId, seen, errors);
                TryAddStep(
                    result,
                    new StepValues
                    {
                        Kind = StageSimulationStepKind.Combat,
                        NodeContentId = nodeId
                    },
                    $"routes[{routeIndex}].nodeContentIds[{i}]",
                    errors);
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<StageSimulationStep> CompileTypedRoute(
            BalanceRouteDocument item,
            int routeIndex,
            ICollection<string> errors)
        {
            var result = new List<StageSimulationStep>();
            if (item.nodeContentIds != null)
            {
                errors.Add(
                    $"Route {item.id} uses nodeContentIds, but schemaVersion 2 " +
                    "requires steps.");
            }
            if (item.steps == null || item.steps.Length == 0)
            {
                errors.Add($"Route {item.id} must contain typed steps.");
                return result.AsReadOnly();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int combatCount = 0;
            for (int i = 0; i < item.steps.Length; i++)
            {
                string label = $"routes[{routeIndex}].steps[{i}]";
                BalanceRouteStepDocument document = item.steps[i];
                if (document == null)
                {
                    errors.Add(label + " is null.");
                    continue;
                }
                bool validKind = TryParseEnum(
                        document.kind,
                        out StageSimulationStepKind kind);
                if (!validKind)
                {
                    errors.Add(
                        label + ".kind is invalid: " + document.kind + ".");
                }
                if (validKind && kind == StageSimulationStepKind.Combat)
                    combatCount++;
                RequireStableId(
                    document.nodeContentId,
                    label + ".nodeContentId",
                    errors);
                RegisterNode(
                    item.id,
                    document.nodeContentId,
                    seen,
                    errors);

                StageSimulationSealDisposition disposition =
                    StageSimulationSealDisposition.None;
                if (!string.IsNullOrWhiteSpace(
                        document.victorySealDisposition) &&
                    !TryParseEnum(
                        document.victorySealDisposition,
                        out disposition))
                {
                    errors.Add(
                        label + ".victorySealDisposition is invalid: " +
                        document.victorySealDisposition + ".");
                }
                if (document.targetCardOccurrence < 0)
                {
                    errors.Add(
                        label + ".targetCardOccurrence cannot be negative.");
                }
                if (!string.IsNullOrWhiteSpace(document.targetCardContentId))
                {
                    RequireStableId(
                        document.targetCardContentId,
                        label + ".targetCardContentId",
                        errors);
                }
                if (!string.IsNullOrWhiteSpace(document.victorySealContentId))
                {
                    RequireStableId(document.victorySealContentId,
                        label + ".victorySealContentId", errors);
                }
                StageSimulationCardConversionDisposition
                    cardConversionDisposition =
                        StageSimulationCardConversionDisposition.None;
                if (!string.IsNullOrWhiteSpace(
                        document.cardConversionDisposition) &&
                    !TryParseEnum(
                        document.cardConversionDisposition,
                        out cardConversionDisposition))
                {
                    errors.Add(
                        label + ".cardConversionDisposition is invalid: " +
                        document.cardConversionDisposition + ".");
                }
                TryAddStep(
                    result,
                    new StepValues
                    {
                        Kind = kind,
                        NodeContentId = document.nodeContentId,
                        TargetCardContentId = document.targetCardContentId,
                        TargetCardOccurrence =
                            document.targetCardOccurrence,
                        VictorySealDisposition = disposition,
                        VictorySealContentId = document.victorySealContentId,
                        CardConversionDisposition =
                            cardConversionDisposition
                    },
                    label,
                    errors);
            }
            if (combatCount == 0)
            {
                errors.Add(
                    $"Route {item.id} must contain at least one Combat step.");
            }
            return result.AsReadOnly();
        }

        private static void TryAddStep(
            ICollection<StageSimulationStep> result,
            StepValues values,
            string label,
            ICollection<string> errors)
        {
            try
            {
                result.Add(new StageSimulationStep(
                    values.Kind,
                    values.NodeContentId,
                    values.TargetCardContentId,
                    values.TargetCardOccurrence,
                    values.VictorySealDisposition,
                    values.CardConversionDisposition,
                    values.VictorySealContentId));
            }
            catch (ArgumentException exception)
            {
                errors.Add(label + " is invalid: " + exception.Message);
            }
        }

        private static void RegisterTarget(
            string id,
            ISet<string> targetIds,
            ISet<string> routeTargetIds,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (!targetIds.Add(id))
            {
                errors.Add($"Duplicate simulation target id: {id}.");
                return;
            }
            routeTargetIds.Add(id);
        }

        private static void RegisterNode(
            string routeId,
            string nodeId,
            ISet<string> seen,
            ICollection<string> errors)
        {
            if (!string.IsNullOrWhiteSpace(nodeId) && !seen.Add(nodeId))
                errors.Add($"Route {routeId} repeats node {nodeId}.");
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

        private static void RequireText(
            string value,
            string label,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add(label + " is required.");
        }

        private static bool TryParseEnum<T>(string value, out T parsed)
            where T : struct
        {
            return Enum.TryParse(value, true, out parsed) &&
                Enum.IsDefined(typeof(T), parsed);
        }

        private struct StepValues
        {
            public StageSimulationStepKind Kind;
            public string NodeContentId;
            public string TargetCardContentId;
            public int TargetCardOccurrence;
            public StageSimulationSealDisposition VictorySealDisposition;
            public string VictorySealContentId;
            public StageSimulationCardConversionDisposition
                CardConversionDisposition;
        }
    }
}
