using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Owns the recipe JSON contract, path confinement, and all cross-field
    /// semantic validation. Callers receive only an immutable compiled recipe.
    /// Scale review: this remains one stateless schema-compilation boundary;
    /// card-conversion intent extends the same step contract. Re-review at 800
    /// lines or before introducing a second recipe schema/lifecycle.
    /// </summary>
    internal static class BalanceCheckpointRecipeCompiler
    {
        private static readonly Regex StableIdPattern = new Regex(
            "^[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)+$",
            RegexOptions.CultureInvariant);

        internal static BalanceCheckpointRecipe Compile(
            string json,
            string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new BalanceCheckpointException(
                    "Checkpoint recipe JSON cannot be empty.");
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new BalanceCheckpointException(
                    "projectRoot is required to compile a checkpoint recipe.");

            BalanceCheckpointRecipeDocument document;
            try
            {
                document = JsonUtility.FromJson<BalanceCheckpointRecipeDocument>(
                    json);
            }
            catch (Exception exception)
            {
                throw new BalanceCheckpointException(
                    "Cannot parse checkpoint recipe JSON: " +
                    exception.Message,
                    exception);
            }
            if (document == null)
                throw new BalanceCheckpointException(
                    "Checkpoint recipe JSON produced no document.");

            var errors = new List<string>();
            if (document.schemaVersion !=
                BalanceCheckpointRecipe.CurrentSchemaVersion)
            {
                errors.Add(
                    $"schemaVersion must be " +
                    $"{BalanceCheckpointRecipe.CurrentSchemaVersion}, got " +
                    $"{document.schemaVersion}.");
            }
            RequireStableId(document.recipeId, "recipeId", errors);
            RequirePositive(document.revision, "revision", errors);
            if (document.seed <= 0) errors.Add("seed must be positive.");
            RequirePositive(
                document.maxTurnsPerEncounter,
                "maxTurnsPerEncounter",
                errors);

            string root = Path.GetFullPath(projectRoot);
            BalanceSimulationSuite sourceSuite = CompileSourceSuite(
                document.sourceSuiteFile,
                root,
                errors);
            IReadOnlyList<BalanceCheckpointFloorPlan> floors = CompileFloors(
                document.floors,
                errors);
            IReadOnlyList<BalanceCheckpointOutput> outputs = CompileOutputs(
                document.outputs,
                root,
                errors);
            if (sourceSuite != null && floors.Count > 0 &&
                !string.Equals(
                    floors[0].FloorContentId,
                    sourceSuite.FloorContentId,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "The first floor plan must match " +
                    $"sourceSuite.floorContentId ({sourceSuite.FloorContentId}).");
            }

            if (errors.Count > 0)
            {
                throw new BalanceCheckpointException(
                    "Checkpoint recipe is invalid:" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors));
            }
            return new BalanceCheckpointRecipe(
                document.recipeId,
                document.revision,
                document.seed,
                document.maxTurnsPerEncounter,
                NormalizeRelativePath(document.sourceSuiteFile),
                sourceSuite,
                floors,
                outputs);
        }

        private static BalanceSimulationSuite CompileSourceSuite(
            string relativePath,
            string projectRoot,
            ICollection<string> errors)
        {
            string sourceSuitePath = ResolveProjectFile(
                projectRoot,
                relativePath,
                "sourceSuiteFile",
                errors);
            BalanceSimulationSuite sourceSuite = null;
            if (sourceSuitePath != null)
            {
                try
                {
                    sourceSuite = BalanceSimulationSuite.CompileJson(
                        File.ReadAllText(sourceSuitePath),
                        projectRoot);
                }
                catch (Exception exception)
                {
                    errors.Add(
                        "sourceSuiteFile is invalid: " + exception.Message);
                }
            }
            if (sourceSuite != null &&
                sourceSuite.EntryState.Kind != BalanceEntryStateKind.FreshRun)
            {
                errors.Add(
                    "sourceSuiteFile must declare a FreshRun entry; checkpoint " +
                    "recipes own the progression chain from its first floor.");
            }
            return sourceSuite;
        }

        private static IReadOnlyList<BalanceCheckpointFloorPlan> CompileFloors(
            BalanceCheckpointFloorPlanDocument[] documents,
            ICollection<string> errors)
        {
            var result = new List<BalanceCheckpointFloorPlan>();
            if (documents == null || documents.Length == 0)
            {
                errors.Add("floors must contain at least one floor plan.");
                return result.AsReadOnly();
            }

            var floorIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < documents.Length; i++)
            {
                BalanceCheckpointFloorPlanDocument document = documents[i];
                if (document == null)
                {
                    errors.Add($"floors[{i}] cannot be null.");
                    continue;
                }
                RequireStableId(
                    document.floorContentId,
                    $"floors[{i}].floorContentId",
                    errors);
                if (!string.IsNullOrWhiteSpace(document.floorContentId) &&
                    !floorIds.Add(document.floorContentId))
                {
                    errors.Add(
                        "floors repeats floorContentId " +
                        $"{document.floorContentId}.");
                }
                IReadOnlyList<StageSimulationStep> steps = CompileSteps(
                    document,
                    i,
                    errors);
                result.Add(new BalanceCheckpointFloorPlan(
                    document.floorContentId,
                    steps,
                    document.advanceAfterCompletion));
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<StageSimulationStep> CompileSteps(
            BalanceCheckpointFloorPlanDocument floor,
            int floorIndex,
            ICollection<string> errors)
        {
            var result = new List<StageSimulationStep>();
            if (floor.steps == null || floor.steps.Length == 0)
            {
                errors.Add($"floors[{floorIndex}].steps cannot be empty.");
                return result.AsReadOnly();
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int stepIndex = 0;
                 stepIndex < floor.steps.Length;
                 stepIndex++)
            {
                BalanceCheckpointStepDocument step = floor.steps[stepIndex];
                if (step == null)
                {
                    errors.Add(
                        $"floors[{floorIndex}].steps[{stepIndex}] cannot be null.");
                    continue;
                }
                StageSimulationStepKind kind = CompileStepKind(
                    step,
                    floorIndex,
                    stepIndex,
                    errors);
                RequireStableId(
                    step.nodeContentId,
                    $"floors[{floorIndex}].steps[{stepIndex}].nodeContentId",
                    errors);
                if (!string.IsNullOrWhiteSpace(step.nodeContentId) &&
                    !nodeIds.Add(step.nodeContentId))
                {
                    errors.Add(
                        $"Floor plan {floor.floorContentId} repeats node " +
                        $"{step.nodeContentId}.");
                }

                StageSimulationSealDisposition disposition =
                    CompileSealDisposition(
                        step,
                        floorIndex,
                        stepIndex,
                        errors);
                StageSimulationCardConversionDisposition
                    cardConversionDisposition =
                        CompileCardConversionDisposition(
                            step,
                            floorIndex,
                            stepIndex,
                            errors);
                ValidateCardTarget(
                    step,
                    disposition,
                    floorIndex,
                    stepIndex,
                    errors);
                try
                {
                    result.Add(new StageSimulationStep(
                        kind,
                        step.nodeContentId,
                        step.targetCardContentId,
                        step.targetCardOccurrence,
                        disposition,
                        cardConversionDisposition,
                        step.victorySealContentId));
                }
                catch (ArgumentException exception)
                {
                    errors.Add(
                        $"floors[{floorIndex}].steps[{stepIndex}] is invalid: " +
                        exception.Message);
                }
            }
            return result.AsReadOnly();
        }

        private static StageSimulationStepKind CompileStepKind(
            BalanceCheckpointStepDocument step,
            int floorIndex,
            int stepIndex,
            ICollection<string> errors)
        {
            if (Enum.TryParse(
                    step.kind,
                    false,
                    out StageSimulationStepKind kind) &&
                Enum.IsDefined(typeof(StageSimulationStepKind), kind))
            {
                return kind;
            }
            errors.Add(
                $"floors[{floorIndex}].steps[{stepIndex}].kind is invalid: " +
                $"{step.kind}.");
            return default;
        }

        private static StageSimulationSealDisposition CompileSealDisposition(
            BalanceCheckpointStepDocument step,
            int floorIndex,
            int stepIndex,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(step.victorySealDisposition))
                return StageSimulationSealDisposition.None;
            if (Enum.TryParse(
                    step.victorySealDisposition,
                    false,
                    out StageSimulationSealDisposition disposition) &&
                Enum.IsDefined(
                    typeof(StageSimulationSealDisposition),
                    disposition))
            {
                return disposition;
            }
            errors.Add(
                $"floors[{floorIndex}].steps[{stepIndex}]." +
                "victorySealDisposition is invalid: " +
                step.victorySealDisposition + ".");
            return StageSimulationSealDisposition.None;
        }

        private static StageSimulationCardConversionDisposition
            CompileCardConversionDisposition(
                BalanceCheckpointStepDocument step,
                int floorIndex,
                int stepIndex,
                ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(
                    step.cardConversionDisposition))
            {
                return StageSimulationCardConversionDisposition.None;
            }
            if (Enum.TryParse(
                    step.cardConversionDisposition,
                    false,
                    out StageSimulationCardConversionDisposition disposition) &&
                Enum.IsDefined(
                    typeof(StageSimulationCardConversionDisposition),
                    disposition))
            {
                return disposition;
            }
            errors.Add(
                $"floors[{floorIndex}].steps[{stepIndex}]." +
                "cardConversionDisposition is invalid: " +
                step.cardConversionDisposition + ".");
            return StageSimulationCardConversionDisposition.None;
        }

        private static void ValidateCardTarget(
            BalanceCheckpointStepDocument step,
            StageSimulationSealDisposition disposition,
            int floorIndex,
            int stepIndex,
            ICollection<string> errors)
        {
            string label = $"floors[{floorIndex}].steps[{stepIndex}]";
            if (!string.IsNullOrWhiteSpace(step.victorySealContentId))
                RequireStableId(step.victorySealContentId, label + ".victorySealContentId", errors);
            if (step.targetCardOccurrence < 0)
            {
                errors.Add(
                    $"{label}.targetCardOccurrence cannot be negative.");
            }
            if (!string.IsNullOrWhiteSpace(step.targetCardContentId))
            {
                RequireStableId(
                    step.targetCardContentId,
                    label + ".targetCardContentId",
                    errors);
            }
            else if (step.targetCardOccurrence != 0)
            {
                errors.Add(
                    $"{label} declares a card occurrence without " +
                    "targetCardContentId.");
            }
            if (disposition == StageSimulationSealDisposition.Assign &&
                string.IsNullOrWhiteSpace(step.targetCardContentId))
            {
                errors.Add(
                    $"{label} assigns a victory seal without " +
                    "targetCardContentId.");
            }
            if (disposition == StageSimulationSealDisposition.CashOut &&
                !string.IsNullOrWhiteSpace(step.targetCardContentId))
            {
                errors.Add(
                    $"{label} cashes out a victory seal but also declares " +
                    "a card target.");
            }
        }

        private static IReadOnlyList<BalanceCheckpointOutput> CompileOutputs(
            BalanceCheckpointOutputDocument[] documents,
            string projectRoot,
            ICollection<string> errors)
        {
            var result = new List<BalanceCheckpointOutput>();
            if (documents == null || documents.Length == 0)
            {
                errors.Add("outputs must contain at least one checkpoint.");
                return result.AsReadOnly();
            }
            var floorIds = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string checkpointRoot = Path.GetFullPath(Path.Combine(
                projectRoot,
                "tools",
                "simulation",
                "checkpoints"));
            for (int i = 0; i < documents.Length; i++)
            {
                BalanceCheckpointOutputDocument document = documents[i];
                if (document == null)
                {
                    errors.Add($"outputs[{i}] cannot be null.");
                    continue;
                }
                RequireStableId(
                    document.floorContentId,
                    $"outputs[{i}].floorContentId",
                    errors);
                if (!string.IsNullOrWhiteSpace(document.floorContentId) &&
                    !floorIds.Add(document.floorContentId))
                {
                    errors.Add(
                        "outputs repeats floorContentId " +
                        $"{document.floorContentId}.");
                }

                string fullPath = ResolveProjectPath(
                    projectRoot,
                    document.checkpointFile,
                    $"outputs[{i}].checkpointFile",
                    errors);
                if (fullPath != null)
                {
                    if (!IsWithin(fullPath, checkpointRoot))
                    {
                        errors.Add(
                            $"outputs[{i}].checkpointFile must stay under " +
                            "tools/simulation/checkpoints.");
                    }
                    if (!string.Equals(
                            Path.GetExtension(fullPath),
                            ".json",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(
                            $"outputs[{i}].checkpointFile must end in .json.");
                    }
                    if (!paths.Add(fullPath))
                    {
                        errors.Add(
                            "outputs repeats checkpointFile " +
                            $"{document.checkpointFile}.");
                    }
                }
                result.Add(new BalanceCheckpointOutput(
                    document.floorContentId,
                    NormalizeRelativePath(document.checkpointFile)));
            }
            return result.AsReadOnly();
        }

        private static string ResolveProjectFile(
            string projectRoot,
            string relativePath,
            string label,
            ICollection<string> errors)
        {
            string fullPath = ResolveProjectPath(
                projectRoot,
                relativePath,
                label,
                errors);
            if (fullPath != null && !File.Exists(fullPath))
            {
                errors.Add($"{label} does not exist: {relativePath}.");
                return null;
            }
            return fullPath;
        }

        private static string ResolveProjectPath(
            string projectRoot,
            string relativePath,
            string label,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                errors.Add($"{label} cannot be empty.");
                return null;
            }
            if (Path.IsPathRooted(relativePath))
            {
                errors.Add($"{label} must be project-relative.");
                return null;
            }
            try
            {
                string fullPath = Path.GetFullPath(Path.Combine(
                    projectRoot,
                    relativePath));
                if (!IsWithin(fullPath, projectRoot))
                {
                    errors.Add($"{label} escapes projectRoot.");
                    return null;
                }
                return fullPath;
            }
            catch (Exception exception)
            {
                errors.Add($"{label} is invalid: {exception.Message}");
                return null;
            }
        }

        private static bool IsWithin(string path, string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(path);
            return candidate.StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRelativePath(string path)
            => string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/');

        private static void RequireStableId(
            string value,
            string label,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !StableIdPattern.IsMatch(value))
            {
                errors.Add($"{label} is not a stable content ID: {value}.");
            }
        }

        private static void RequirePositive(
            int value,
            string label,
            ICollection<string> errors)
        {
            if (value <= 0) errors.Add($"{label} must be positive.");
        }
    }

    [Serializable]
    internal sealed class BalanceCheckpointRecipeDocument
    {
        public int schemaVersion;
        public string recipeId;
        public int revision;
        public int seed;
        public int maxTurnsPerEncounter;
        public string sourceSuiteFile;
        public BalanceCheckpointFloorPlanDocument[] floors;
        public BalanceCheckpointOutputDocument[] outputs;
    }

    [Serializable]
    internal sealed class BalanceCheckpointFloorPlanDocument
    {
        public string floorContentId;
        public BalanceCheckpointStepDocument[] steps;
        public bool advanceAfterCompletion;
    }

    [Serializable]
    internal sealed class BalanceCheckpointStepDocument
    {
        public string kind;
        public string nodeContentId;
        public string targetCardContentId;
        public int targetCardOccurrence;
        public string victorySealDisposition;
        public string victorySealContentId;
        public string cardConversionDisposition;
    }

    [Serializable]
    internal sealed class BalanceCheckpointOutputDocument
    {
        public string floorContentId;
        public string checkpointFile;
    }
}
