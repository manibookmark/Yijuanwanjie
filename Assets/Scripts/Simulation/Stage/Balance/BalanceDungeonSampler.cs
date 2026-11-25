using System;
using System.Collections.Generic;
using System.Globalization;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Owns cross-floor sample populations and real entry selection. Every seed
    /// starts the same fresh recipe through Generator; combat and map rules
    /// remain in their production owners. Losses retain all earlier entries.
    /// Report DTOs describe observations, never become mutable gameplay state.
    /// </summary>
    public static class BalanceDungeonSampler
    {
        public static BalanceDungeonSamplingReport Run(
            BalanceCheckpointRecipe recipe, int seedStart, int count)
            => Run(recipe, seedStart, count, BalanceCheckpointGenerator.Replay);

        // Injection boundary exercises population/error semantics without
        // installing production-map topology in tests or duplicating its rules.
        internal static BalanceDungeonSamplingReport Run(
            BalanceCheckpointRecipe recipe, int seedStart, int count,
            Func<BalanceCheckpointRecipe, BalanceCheckpointGenerationResult> replay)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            ValidatePlan(recipe, seedStart, count);
            var report = new BalanceDungeonSamplingReport
            {
                runId = "dungeon-sampling." + DateTime.UtcNow.ToString(
                    "yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "." +
                    Guid.NewGuid().ToString("N"),
                seedStart = seedStart,
                seedEnd = checked(seedStart + count - 1),
                requestedCount = count,
                provenance = new BalanceDungeonSamplingProvenance
                {
                    recipeId = recipe.RecipeId,
                    recipeRevision = recipe.Revision,
                    recipeSha256 = recipe.SourceSha256,
                    sourceSuiteId = recipe.SourceSuite.SuiteId,
                    sourceSuiteRevision = recipe.SourceSuite.Revision,
                    sourceSuiteSha256 = recipe.SourceSuite.SourceSha256,
                    agentId = recipe.SourceSuite.Agent.Id,
                    agentRevision = recipe.SourceSuite.Agent.Revision,
                    rewardPolicyId = recipe.SourceSuite.RewardPolicy.Id,
                    rewardPolicyRevision = recipe.SourceSuite.RewardPolicy.Revision,
                    maxTurnsPerEncounter = recipe.MaxTurnsPerEncounter
                }
            };
            var terminalStats = new BalanceStatistics();
            for (int offset = 0; offset < count; offset++)
            {
                int seed = checked(seedStart + offset);
                // No catch here: only Generator's typed combat outcomes are
                // observations. Content, state and execution faults abort.
                BalanceCheckpointGenerationResult result = replay(recipe.WithSeed(seed));
                BalanceDungeonSeedSample sample = Observe(result, seed);
                report.samples.Add(sample);
                bool victory = result.CombatFailure == null;
                bool limit = result.CombatFailure?.IsSimulationLimit ?? false;
                terminalStats.Add(victory, limit, sample.terminal.health);
                if (limit) report.simulationLimitCount++;
            }
            report.terminalHealth = terminalStats.Summarize();
            for (int index = 1; index < recipe.Floors.Count; index++)
                report.floors.Add(DescribeFloor(
                    recipe.Floors[index].FloorContentId, report.samples));
            // Gameplay defeat is a valid sample. Limits make the batch
            // incomplete for calibration, without discarding its evidence.
            report.exitCode = report.simulationLimitCount > 0 ? 1 : 0;
            return report;
        }

        private static void ValidatePlan(
            BalanceCheckpointRecipe recipe, int seedStart, int count)
        {
            if (seedStart <= 0) throw new ArgumentOutOfRangeException(nameof(seedStart));
            if (count <= 0 || count > BalanceRunOptions.MaximumCountPerTarget)
                throw new ArgumentOutOfRangeException(nameof(count));
            checked { _ = seedStart + count - 1; }
            if (recipe.SourceSuite.EntryState.Kind != BalanceEntryStateKind.FreshRun)
                throw new BalanceCheckpointException("Dungeon sampling must start a fresh Run.");
            if (recipe.Floors.Count < 2)
                throw new BalanceCheckpointException("Dungeon sampling requires multiple floors.");
            long combatCount = 0;
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (BalanceCheckpointOutput output in recipe.Outputs)
                outputs.Add(output.FloorContentId);
            for (int i = 0; i < recipe.Floors.Count; i++)
            {
                BalanceCheckpointFloorPlan floor = recipe.Floors[i];
                if (floor.AdvanceAfterCompletion != (i < recipe.Floors.Count - 1))
                    throw new BalanceCheckpointException(
                        "Sampling recipe must advance continuously to its final floor.");
                if (i > 0 && !outputs.Remove(floor.FloorContentId))
                    throw new BalanceCheckpointException(
                        "Every carried floor requires an entry output: " + floor.FloorContentId);
                foreach (Stage.StageSimulationStep step in floor.Steps)
                    if (step.Kind == Stage.StageSimulationStepKind.Combat) combatCount++;
            }
            if (outputs.Count != 0)
                throw new BalanceCheckpointException(
                    "Sampling outputs must name exactly the carried floors.");
            if (combatCount <= 0 || combatCount * count > BalanceRunOptions.MaximumCombatRuns)
                throw new BalanceCheckpointException("Dungeon sampling exceeds the combat work budget.");
        }

        private static BalanceDungeonSeedSample Observe(
            BalanceCheckpointGenerationResult result, int expectedSeed)
        {
            if (result == null || result.Seed != expectedSeed || result.TerminalState == null ||
                result.Artifacts.Count != result.Entries.Count)
                throw new BalanceCheckpointException("Replay returned inconsistent sample evidence.");
            BalanceCheckpointCombatOutcomeException failure = result.CombatFailure;
            if (failure == null && !result.TerminalState.IsFinaleCompleted)
                throw new BalanceCheckpointException(
                    "Sampling recipe stopped without a completed production Finale.");
            if (failure != null && (failure.Seed != expectedSeed ||
                failure.EndingHealth != result.TerminalState.Health ||
                failure.FloorContentId != result.TerminalState.FloorContentId))
                throw new BalanceCheckpointException("Combat failure does not match terminal state.");
            var sample = new BalanceDungeonSeedSample
            {
                seed = expectedSeed,
                runId = result.TerminalState.RunId,
                outcome = failure?.Termination.ToString() ?? "Victory",
                failedFloorContentId = failure?.FloorContentId ?? string.Empty,
                failedNodeContentId = failure?.NodeContentId ?? string.Empty,
                terminal = ObserveState(result.TerminalState),
                terminalPayload = result.FinalRunPayload
            };
            for (int i = 0; i < result.Artifacts.Count; i++)
            {
                BalanceCheckpointArtifact artifact = result.Artifacts[i];
                BalanceCheckpointRunState state = result.Entries[i];
                if (artifact.FloorContentId != state.FloorContentId ||
                    state.RunId != sample.runId || state.Health <= 0 || state.CardCount <= 0)
                    throw new BalanceCheckpointException("Entry artifact does not match its observation.");
                sample.entries.Add(new BalanceDungeonEntrySample
                {
                    state = ObserveState(state),
                    snapshotId = artifact.SnapshotId,
                    checkpointSha256 = artifact.Sha256,
                    checkpointPath = "seeds/s" + expectedSeed.ToString(
                        CultureInfo.InvariantCulture) + "/entry-" +
                        (i + 1).ToString("D2", CultureInfo.InvariantCulture) + ".json",
                    payload = artifact.Payload
                });
            }
            return sample;
        }

        private static BalanceDungeonPlayerState ObserveState(BalanceCheckpointRunState state)
            => new BalanceDungeonPlayerState
            {
                floorContentId = state.FloorContentId,
                health = state.Health,
                maxHealth = state.MaxHealth,
                cardCount = state.CardCount,
                level = state.Level,
                experience = state.Experience,
                finaleCompleted = state.IsFinaleCompleted,
                victoryConclusionFrozen = state.IsDungeonVictory
            };

        private static BalanceDungeonFloorPopulation DescribeFloor(
            string floorId, List<BalanceDungeonSeedSample> samples)
        {
            var reached = new List<(BalanceDungeonSeedSample sample, BalanceDungeonEntrySample entry)>();
            var stats = new BalanceStatistics();
            foreach (BalanceDungeonSeedSample sample in samples)
            {
                foreach (BalanceDungeonEntrySample entry in sample.entries)
                {
                    if (entry.state.floorContentId != floorId) continue;
                    reached.Add((sample, entry));
                    stats.Add(sample.outcome == "Victory",
                        sample.outcome == "MaxTurns" || sample.outcome == "CommandLimit",
                        entry.state.health);
                }
            }
            reached.Sort((a, b) =>
            {
                int health = a.entry.state.health.CompareTo(b.entry.state.health);
                return health != 0 ? health : a.sample.seed.CompareTo(b.sample.seed);
            });
            var population = new BalanceDungeonFloorPopulation
            {
                floorContentId = floorId,
                reachRate95 = BalanceStatistics.Wilson95(reached.Count, samples.Count),
                entryHealthByFinalOutcome = stats.Summarize()
            };
            if (reached.Count == 0) return population;
            string[] names = { "low", "median", "high" };
            double[] quantiles = { 0.1d, 0.5d, 0.9d };
            for (int i = 0; i < quantiles.Length; i++)
            {
                var chosen = reached[(int)Math.Round((reached.Count - 1) * quantiles[i])];
                population.representatives.Add(new BalanceDungeonEntryRepresentative
                {
                    band = names[i],
                    quantile = (float)quantiles[i],
                    seed = chosen.sample.seed,
                    runId = chosen.sample.runId,
                    finalOutcome = chosen.sample.outcome,
                    entry = chosen.entry
                });
            }
            return population;
        }
    }

    [Serializable]
    public sealed class BalanceDungeonSamplingReport
    {
        public int schemaVersion = 1;
        public string runId;
        public BalanceDungeonSamplingProvenance provenance;
        public int seedStart, seedEnd, requestedCount, simulationLimitCount, exitCode;
        public BalanceOutcomeStatistics terminalHealth;
        public List<BalanceDungeonSeedSample> samples = new List<BalanceDungeonSeedSample>();
        public List<BalanceDungeonFloorPopulation> floors = new List<BalanceDungeonFloorPopulation>();
    }

    [Serializable]
    public sealed class BalanceDungeonSamplingProvenance
    {
        public string recipeId, recipeSha256, sourceSuiteId, sourceSuiteSha256, contentFingerprint;
        public int recipeRevision, sourceSuiteRevision;
        public string agentId, rewardPolicyId;
        public int agentRevision, rewardPolicyRevision, maxTurnsPerEncounter;
    }

    [Serializable]
    public sealed class BalanceDungeonSeedSample
    {
        public int seed;
        public string runId, outcome, failedFloorContentId, failedNodeContentId;
        public BalanceDungeonPlayerState terminal;
        public string terminalPath, terminalSha256;
        public List<BalanceDungeonEntrySample> entries = new List<BalanceDungeonEntrySample>();
        [NonSerialized] public string terminalPayload;
    }

    [Serializable]
    public sealed class BalanceDungeonPlayerState
    {
        public string floorContentId;
        public int health, maxHealth, cardCount, level, experience;
        public bool finaleCompleted, victoryConclusionFrozen;
    }

    [Serializable]
    public sealed class BalanceDungeonEntrySample
    {
        public BalanceDungeonPlayerState state;
        public string snapshotId, checkpointPath, checkpointSha256;
        [NonSerialized] public string payload;
    }

    [Serializable]
    public sealed class BalanceDungeonFloorPopulation
    {
        public string floorContentId;
        public BalanceRateInterval reachRate95;
        // Groups are by the seed's eventual Dungeon result, including later
        // defeats. The "all" distribution is the unbiased reached population.
        public BalanceOutcomeStatistics entryHealthByFinalOutcome;
        public List<BalanceDungeonEntryRepresentative> representatives =
            new List<BalanceDungeonEntryRepresentative>();
    }

    [Serializable]
    public sealed class BalanceDungeonEntryRepresentative
    {
        public string band, runId, finalOutcome;
        public float quantile;
        public int seed;
        public BalanceDungeonEntrySample entry;
    }
}
