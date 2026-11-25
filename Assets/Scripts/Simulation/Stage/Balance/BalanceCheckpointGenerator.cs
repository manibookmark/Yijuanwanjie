using System;
using System.Collections.Generic;
using System.Globalization;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    public enum BalanceCheckpointGenerationScope
    {
        FullRecipe,
        RequestedEntries
    }

    /// <summary>
    /// Replays one reviewed recipe through production Dungeon state and emits
    /// restorable entries. This facade owns only the cross-floor lifecycle;
    /// steps and artifact validation remain hidden behind dedicated modules.
    /// </summary>
    public static class BalanceCheckpointGenerator
    {
        public static BalanceCheckpointGenerationResult Generate(
            BalanceCheckpointRecipe recipe)
        {
            BalanceCheckpointGenerationResult result = Replay(recipe);
            if (result.CombatFailure != null) throw result.CombatFailure;
            return result;
        }

        /// <summary>
        /// Replays only until every requested entry has been captured. A later
        /// combat is outside this generation scope; missing entries and any
        /// failure encountered before the capture boundary still fail.
        /// </summary>
        public static BalanceCheckpointGenerationResult GenerateEntries(
            BalanceCheckpointRecipe recipe)
        {
            BalanceCheckpointGenerationResult result = Replay(
                recipe, BalanceCheckpointGenerationScope.RequestedEntries);
            if (result.CombatFailure != null) throw result.CombatFailure;
            return result;
        }

        /// <summary>
        /// The same production replay used by Generate, retaining every entry
        /// reached before a combat loss. Structural failures still propagate.
        /// </summary>
        internal static BalanceCheckpointGenerationResult Replay(
            BalanceCheckpointRecipe recipe)
            => Replay(recipe, BalanceCheckpointGenerationScope.FullRecipe);

        private static BalanceCheckpointGenerationResult Replay(
            BalanceCheckpointRecipe recipe, BalanceCheckpointGenerationScope scope)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));

            GameSession previousGameSession = GameSession.Current;
            var random = new SeededRandomSource(recipe.Seed);
            var instanceIds = new SeededInstanceIdGenerator(1);
            try
            {
                GameSession.Current = new GameSession();
                DungeonRunSessionStore.Clear();
                BattleScenarioCatalog.ClearCache();
                LevelRunProfileCatalog.ClearCache();

                BalanceSimulationSuite sourceSuite = recipe.SourceSuite;
                LevelRunProfile firstFloor = RequireFloor(
                    sourceSuite.FloorContentId);
                DungeonRunSession dungeon = DungeonRunSessionStore.Begin(
                    firstFloor,
                    sourceSuite.SnapshotId,
                    BuildRunId(recipe),
                    instanceIds,
                    runSeed: recipe.Seed,
                    commonEncounterSources: id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id));
                var progress = new ProductionReplayProgress(recipe, dungeon,
                    new BalanceCheckpointStepExecutor(recipe, random, instanceIds));
                BalanceCheckpointCombatOutcomeException failure = null;
                try
                {
                    ExecutePlan(recipe, progress, scope);
                }
                catch (BalanceCheckpointCombatOutcomeException outcome)
                {
                    failure = outcome;
                }
                dungeon = progress.Dungeon;
                string finalRunPayload = dungeon.Serialize();
                return new BalanceCheckpointGenerationResult(
                    recipe.RecipeId,
                    recipe.Revision,
                    recipe.Seed,
                    sourceSuite.SourceSha256,
                    progress.Artifacts.AsReadOnly(),
                    finalRunPayload,
                    progress.Entries.AsReadOnly(),
                    BalanceCheckpointRunState.Capture(dungeon),
                    failure,
                    scope);
            }
            finally
            {
                DungeonRunSessionStore.Clear();
                GameSession.Current = previousGameSession;
            }
        }

        /// <summary>
        /// Owns the replay stopping boundary. The progress adapter supplies
        /// authoritative entries and executes commands; this loop does not
        /// infer combat outcomes or map transitions from recipe order.
        /// </summary>
        internal static void ExecutePlan(
            BalanceCheckpointRecipe recipe, IBalanceCheckpointReplayProgress progress,
            BalanceCheckpointGenerationScope scope)
        {
            if (recipe.Outputs.Count == 0)
                throw new BalanceCheckpointException("Entry generation requires at least one requested output.");
            bool allEntriesCaptured = progress.CaptureRequestedEntries();
            if (scope == BalanceCheckpointGenerationScope.RequestedEntries && allEntriesCaptured) return;
            foreach (BalanceCheckpointFloorPlan plan in recipe.Floors)
            {
                RequireCurrentFloor(progress.CurrentFloorContentId, plan.FloorContentId);
                foreach (StageSimulationStep step in plan.Steps) progress.Execute(step);
                if (!plan.AdvanceAfterCompletion) continue;
                progress.Advance();
                allEntriesCaptured = progress.CaptureRequestedEntries();
                if (scope == BalanceCheckpointGenerationScope.RequestedEntries && allEntriesCaptured) return;
            }
            if (!allEntriesCaptured)
                throw new BalanceCheckpointException("Recipe did not reach every requested output floor.");
        }

        private static LevelRunProfile RequireFloor(string contentId)
        {
            LevelRunProfile floor;
            try
            {
                floor = LevelRunProfileCatalog.Get(contentId);
            }
            catch (Exception exception)
            {
                throw new BalanceCheckpointException(
                    $"Cannot resolve checkpoint floor {contentId}: " +
                    exception.Message,
                    exception);
            }
            if (floor == null)
            {
                throw new BalanceCheckpointException(
                    "Cannot resolve checkpoint floor: " + contentId + ".");
            }
            List<string> errors = floor.Validate();
            if (errors.Count > 0)
            {
                throw new BalanceCheckpointException(
                    $"Checkpoint floor {contentId} is invalid: " +
                    string.Join(" | ", errors));
            }
            return floor;
        }

        private static void RequireCurrentFloor(
            string actualFloorContentId,
            string expectedFloorContentId)
        {
            if (string.Equals(
                    actualFloorContentId,
                    expectedFloorContentId,
                    StringComparison.Ordinal))
            {
                return;
            }
            throw new BalanceCheckpointException(
                $"Recipe expected floor {expectedFloorContentId}, but the " +
                $"Run is on {actualFloorContentId}.");
        }

        private static string BuildRunId(BalanceCheckpointRecipe recipe)
            => "balance.checkpoint." + recipe.RecipeId + ".r" +
               recipe.Revision.ToString(CultureInfo.InvariantCulture) + ".s" +
               recipe.Seed.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Owns observed entry evidence and the current production session
        /// reference. Dungeon remains the sole writer of game state.
        /// </summary>
        private sealed class ProductionReplayProgress : IBalanceCheckpointReplayProgress
        {
            private readonly BalanceCheckpointRecipe _recipe;
            private readonly BalanceCheckpointStepExecutor _steps;
            private readonly HashSet<string> _captured = new HashSet<string>(StringComparer.Ordinal);
            private DungeonRunFloorExitResult _exit;

            internal ProductionReplayProgress(BalanceCheckpointRecipe recipe,
                DungeonRunSession dungeon, BalanceCheckpointStepExecutor steps)
            { _recipe = recipe; Dungeon = dungeon; _steps = steps; }

            internal DungeonRunSession Dungeon { get; private set; }
            internal List<BalanceCheckpointArtifact> Artifacts { get; } = new List<BalanceCheckpointArtifact>();
            internal List<BalanceCheckpointRunState> Entries { get; } = new List<BalanceCheckpointRunState>();
            public string CurrentFloorContentId => Dungeon.CurrentFloorContentId;

            public bool CaptureRequestedEntries()
            {
                int before = Artifacts.Count;
                BalanceCheckpointArtifactCapture.CaptureRequestedEntries(
                    _recipe, Dungeon, Artifacts, _captured);
                for (int i = before; i < Artifacts.Count; i++)
                    Entries.Add(BalanceCheckpointRunState.Capture(Dungeon));
                return Artifacts.Count == _recipe.Outputs.Count;
            }

            public void Execute(StageSimulationStep step)
            {
                // Keep the request's old identity until the recipe accepts the
                // boundary. Repeating its Exit must read history, not exit again.
                DungeonRunFloorExitResult result = _steps.Execute(Dungeon, step,
                    _exit?.FloorContentId ?? CurrentFloorContentId);
                if (result != null) _exit = result;
            }

            public void Advance()
            {
                // Legacy recipe verb: acknowledge the already committed Exit;
                // the root transaction is the sole owner of floor advancement.
                if (_exit == null || !string.Equals(_exit.NextFloorContentId,
                        CurrentFloorContentId, StringComparison.Ordinal))
                    throw new BalanceCheckpointException("Recipe advance requires a committed Exit to the current entry.");
                _exit = null;
            }
        }
    }

    /// <summary>Test seam for replay control flow, without replacing combat or map rules.</summary>
    internal interface IBalanceCheckpointReplayProgress
    {
        string CurrentFloorContentId { get; }
        bool CaptureRequestedEntries();
        void Execute(StageSimulationStep step);
        void Advance();
    }

    public sealed class BalanceCheckpointGenerationResult
    {
        internal BalanceCheckpointGenerationResult(
            string recipeId,
            int recipeRevision,
            int seed,
            string sourceSuiteSha256,
            IReadOnlyList<BalanceCheckpointArtifact> artifacts,
            string finalRunPayload,
            IReadOnlyList<BalanceCheckpointRunState> entries,
            BalanceCheckpointRunState terminalState,
            BalanceCheckpointCombatOutcomeException combatFailure = null,
            BalanceCheckpointGenerationScope scope = BalanceCheckpointGenerationScope.FullRecipe)
        {
            RecipeId = recipeId;
            RecipeRevision = recipeRevision;
            Seed = seed;
            SourceSuiteSha256 = sourceSuiteSha256;
            Artifacts = artifacts;
            FinalRunPayload = finalRunPayload ?? string.Empty;
            Entries = entries;
            TerminalState = terminalState;
            CombatFailure = combatFailure;
            Scope = scope;
        }

        public string RecipeId { get; }
        public int RecipeRevision { get; }
        public int Seed { get; }
        public string SourceSuiteSha256 { get; }
        public IReadOnlyList<BalanceCheckpointArtifact> Artifacts { get; }
        public IReadOnlyList<BalanceCheckpointRunState> Entries { get; }
        public BalanceCheckpointRunState TerminalState { get; }
        public BalanceCheckpointCombatOutcomeException CombatFailure { get; }
        public BalanceCheckpointGenerationScope Scope { get; }
        /// <summary>
        /// Immutable evidence captured at the declared scope boundary. This is
        /// intentionally not written as an entry checkpoint: it lets tests and
        /// batch callers verify terminal Run state after the generator releases
        /// its live Dungeon session.
        /// </summary>
        public string FinalRunPayload { get; }
    }

    /// <summary>Read-only observations of authoritative Dungeon state.</summary>
    public sealed class BalanceCheckpointRunState
    {
        internal BalanceCheckpointRunState(
            string runId, string floorContentId, int health, int maxHealth,
            int cardCount, int level, int experience,
            bool isFinaleCompleted, bool isDungeonVictory)
        {
            RunId = runId;
            FloorContentId = floorContentId;
            Health = health;
            MaxHealth = maxHealth;
            CardCount = cardCount;
            Level = level;
            Experience = experience;
            IsFinaleCompleted = isFinaleCompleted;
            IsDungeonVictory = isDungeonVictory;
        }

        public string RunId { get; }
        public string FloorContentId { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public int CardCount { get; }
        public int Level { get; }
        public int Experience { get; }
        /// <summary>The production floor has committed a Finale victory.</summary>
        public bool IsFinaleCompleted { get; }
        /// <summary>A victory conclusion has also been frozen after settlement.</summary>
        public bool IsDungeonVictory { get; }

        internal static BalanceCheckpointRunState Capture(DungeonRunSession dungeon)
            => new BalanceCheckpointRunState(
                dungeon.RunId, dungeon.CurrentFloorContentId,
                dungeon.CurrentHealth, dungeon.MaxHealth, dungeon.RunCardCount,
                dungeon.CombatProgression.CurrentLevel,
                dungeon.CombatProgression.CurrentExperience,
                HasCompletedFinale(dungeon.CurrentFloor),
                dungeon.TryGetConclusion(out DungeonRunResultSnapshot result) &&
                result.Status == DungeonRunResultStatus.Victory);

        private static bool HasCompletedFinale(LevelRunSession floor)
        {
            // Observe typed production state, without knowing any map node ID
            // or requiring account-reward settlement in a headless battle run.
            foreach (LevelNode node in floor.Nodes)
                if (node != null && node.nodeType == LevelNodeType.Finale &&
                    floor.GetNodeState(node.contentId) == LevelNodeViewState.Completed)
                    return true;
            return false;
        }
    }

    public sealed class BalanceCheckpointArtifact
    {
        internal BalanceCheckpointArtifact(
            string floorContentId,
            string snapshotId,
            string checkpointFile,
            string payload,
            string sha256)
        {
            FloorContentId = floorContentId;
            SnapshotId = snapshotId;
            CheckpointFile = checkpointFile;
            Payload = payload;
            Sha256 = sha256;
        }

        public string FloorContentId { get; }
        public string SnapshotId { get; }
        public string CheckpointFile { get; }
        public string Payload { get; }
        public string Sha256 { get; }
    }
}
