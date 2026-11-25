using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Immutable, versioned description of one representative production Run.
    /// Route choices are frozen here; combat numbers remain owned by production
    /// content referenced through the source Suite.
    /// </summary>
    public sealed class BalanceCheckpointRecipe
    {
        public const int CurrentSchemaVersion = 1;

        internal BalanceCheckpointRecipe(
            string recipeId,
            int revision,
            int seed,
            int maxTurnsPerEncounter,
            string sourceSuiteFile,
            BalanceSimulationSuite sourceSuite,
            IReadOnlyList<BalanceCheckpointFloorPlan> floors,
            IReadOnlyList<BalanceCheckpointOutput> outputs)
        {
            RecipeId = recipeId;
            Revision = revision;
            Seed = seed;
            MaxTurnsPerEncounter = maxTurnsPerEncounter;
            SourceSuiteFile = sourceSuiteFile;
            SourceSuite = sourceSuite;
            Floors = floors;
            Outputs = outputs;
        }

        public string RecipeId { get; }
        public int Revision { get; }
        public int Seed { get; }
        public int MaxTurnsPerEncounter { get; }
        public string SourceSuiteFile { get; }
        public BalanceSimulationSuite SourceSuite { get; }
        public IReadOnlyList<BalanceCheckpointFloorPlan> Floors { get; }
        public IReadOnlyList<BalanceCheckpointOutput> Outputs { get; }
        public string SourceSha256 { get; private set; } = string.Empty;

        public static BalanceCheckpointRecipe CompileJson(
            string json,
            string projectRoot)
        {
            BalanceCheckpointRecipe recipe =
                BalanceCheckpointRecipeCompiler.Compile(json, projectRoot);
            using SHA256 sha = SHA256.Create();
            recipe.SourceSha256 = BitConverter.ToString(sha.ComputeHash(
                    new UTF8Encoding(false).GetBytes(json)))
                .Replace("-", string.Empty).ToLowerInvariant();
            return recipe;
        }

        internal BalanceCheckpointRecipe WithSeed(int seed)
        {
            if (seed <= 0) throw new ArgumentOutOfRangeException(nameof(seed));
            return new BalanceCheckpointRecipe(
                RecipeId,
                Revision,
                seed,
                MaxTurnsPerEncounter,
                SourceSuiteFile,
                SourceSuite,
                Floors,
                Outputs) { SourceSha256 = SourceSha256 };
        }
    }

    public sealed class BalanceCheckpointFloorPlan
    {
        internal BalanceCheckpointFloorPlan(
            string floorContentId,
            IReadOnlyList<StageSimulationStep> steps,
            bool advanceAfterCompletion)
        {
            FloorContentId = floorContentId;
            Steps = steps;
            AdvanceAfterCompletion = advanceAfterCompletion;
        }

        public string FloorContentId { get; }
        public IReadOnlyList<StageSimulationStep> Steps { get; }
        public bool AdvanceAfterCompletion { get; }
    }

    public sealed class BalanceCheckpointOutput
    {
        internal BalanceCheckpointOutput(
            string floorContentId,
            string checkpointFile)
        {
            FloorContentId = floorContentId;
            CheckpointFile = checkpointFile;
        }

        public string FloorContentId { get; }
        public string CheckpointFile { get; }
    }

    public class BalanceCheckpointException : Exception
    {
        public BalanceCheckpointException(string message) : base(message) { }

        public BalanceCheckpointException(
            string message,
            Exception innerException) : base(message, innerException) { }
    }

    /// <summary>A completed combat sample, not a content or execution error.</summary>
    public sealed class BalanceCheckpointCombatOutcomeException : BalanceCheckpointException
    {
        public BalanceCheckpointCombatOutcomeException(
            int seed, string floorContentId, string nodeContentId,
            SimulationTerminationReason termination, int endingHealth)
            : base($"Checkpoint recipe failed combat {nodeContentId} at seed " +
                   $"{seed}: {termination}, endingHealth={endingHealth}.")
        {
            if (termination != SimulationTerminationReason.Defeat &&
                termination != SimulationTerminationReason.MaxTurns &&
                termination != SimulationTerminationReason.CommandLimit)
                throw new ArgumentOutOfRangeException(nameof(termination));
            if (endingHealth < 0)
                throw new ArgumentOutOfRangeException(nameof(endingHealth));
            Seed = seed;
            FloorContentId = floorContentId;
            NodeContentId = nodeContentId;
            Termination = termination;
            EndingHealth = endingHealth;
        }

        public int Seed { get; }
        public string FloorContentId { get; }
        public string NodeContentId { get; }
        public SimulationTerminationReason Termination { get; }
        public int EndingHealth { get; }
        public bool IsSimulationLimit => Termination != SimulationTerminationReason.Defeat;
    }
}
