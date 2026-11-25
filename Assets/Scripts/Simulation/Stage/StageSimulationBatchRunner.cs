using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>Immutable aggregate for one route and one versioned policy set.</summary>
    public sealed class StageSimulationBatchReport
    {
        internal StageSimulationBatchReport(
            IReadOnlyList<StageSimulationReport> samples)
        {
            if (samples == null || samples.Count == 0)
                throw new ArgumentException("Batch samples cannot be empty.", nameof(samples));

            var frozenSamples = new List<StageSimulationReport>(samples);
            Samples = frozenSamples.AsReadOnly();
            StageSimulationReport first = frozenSamples[0];
            FloorContentId = first.FloorContentId;
            SnapshotId = first.SnapshotId;
            RouteId = first.RouteId;
            RouteRevision = first.RouteRevision;
            AgentName = first.AgentName;
            AgentRevision = first.AgentRevision;
            RewardPolicyId = first.RewardPolicyId;
            RewardPolicyRevision = first.RewardPolicyRevision;
            RuleInputId = first.RuleInputId;
            RuleInputRevision = first.RuleInputRevision;
            SelectedHeroContentId = first.SelectedHeroContentId;
            SeedStart = first.MasterSeed;
            Runs = frozenSamples.Count;

            var endingHealth = new List<int>(Runs);
            var encounterCounts = new List<int>(Runs);
            var failures = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < frozenSamples.Count; i++)
            {
                StageSimulationReport sample = frozenSamples[i];
                RequireSameInputs(first, sample);
                endingHealth.Add(sample.EndingHealth);
                encounterCounts.Add(sample.Encounters.Count);
                switch (sample.Termination)
                {
                    case StageSimulationTermination.RouteCompleted:
                        RouteCompletions++;
                        break;
                    case StageSimulationTermination.CombatDefeat:
                        CombatDefeats++;
                        AddFailure(failures, sample.FailedNodeContentId);
                        break;
                    case StageSimulationTermination.SimulationLimit:
                        SimulationLimits++;
                        AddFailure(failures, sample.FailedNodeContentId);
                        break;
                }
            }

            endingHealth.Sort();
            encounterCounts.Sort();
            MedianEndingHealth = Percentile(endingHealth, 0.50f);
            P10EndingHealth = Percentile(endingHealth, 0.10f);
            MedianEncounterCount = Percentile(encounterCounts, 0.50f);
            FailuresByNode = new ReadOnlyDictionary<string, int>(failures);
        }

        public const int SchemaVersion = 2;
        public string FloorContentId { get; }
        public string SnapshotId { get; }
        public string RouteId { get; }
        public int RouteRevision { get; }
        public string AgentName { get; }
        public int AgentRevision { get; }
        public string RewardPolicyId { get; }
        public int RewardPolicyRevision { get; }
        public string RuleInputId { get; }
        public int RuleInputRevision { get; }
        public string SelectedHeroContentId { get; }
        public int SeedStart { get; }
        public int Runs { get; }
        public int RouteCompletions { get; private set; }
        public int CombatDefeats { get; private set; }
        public int SimulationLimits { get; private set; }
        public float RouteCompletionRate =>
            Runs > 0 ? (float)RouteCompletions / Runs : 0f;
        public float MedianEndingHealth { get; }
        public float P10EndingHealth { get; }
        public float MedianEncounterCount { get; }
        public IReadOnlyDictionary<string, int> FailuresByNode { get; }
        public IReadOnlyList<StageSimulationReport> Samples { get; }

        public override string ToString()
        {
            return $"[{FloorContentId}/{RouteId}@{RouteRevision}] " +
                   $"{AgentName}@{AgentRevision}: " +
                   $"{Runs} runs, completion={RouteCompletionRate:P1}, " +
                   $"HP(p10/median)={P10EndingHealth:F1}/" +
                   $"{MedianEndingHealth:F1}, limits={SimulationLimits}";
        }

        private static void RequireSameInputs(
            StageSimulationReport expected,
            StageSimulationReport actual)
        {
            if (!string.Equals(expected.FloorContentId, actual.FloorContentId,
                    StringComparison.Ordinal) ||
                !string.Equals(expected.SnapshotId, actual.SnapshotId,
                    StringComparison.Ordinal) ||
                !string.Equals(expected.RouteId, actual.RouteId,
                    StringComparison.Ordinal) ||
                expected.RouteRevision != actual.RouteRevision ||
                !string.Equals(expected.AgentName, actual.AgentName,
                    StringComparison.Ordinal) ||
                expected.AgentRevision != actual.AgentRevision ||
                !string.Equals(expected.RewardPolicyId, actual.RewardPolicyId,
                    StringComparison.Ordinal) ||
                expected.RewardPolicyRevision != actual.RewardPolicyRevision ||
                !string.Equals(expected.RuleInputId, actual.RuleInputId,
                    StringComparison.Ordinal) ||
                expected.RuleInputRevision != actual.RuleInputRevision ||
                !string.Equals(
                    expected.SelectedHeroContentId,
                    actual.SelectedHeroContentId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A stage batch cannot mix floors, routes, agents, or policies.");
            }
        }

        private static void AddFailure(
            IDictionary<string, int> failures,
            string nodeContentId)
        {
            string key = string.IsNullOrWhiteSpace(nodeContentId)
                ? "<unknown>"
                : nodeContentId;
            failures.TryGetValue(key, out int count);
            failures[key] = count + 1;
        }

        private static float Percentile(IReadOnlyList<int> sorted, float p)
        {
            if (sorted.Count == 0) return 0f;
            int index = (int)Math.Round(p * (sorted.Count - 1));
            index = Math.Max(0, Math.Min(index, sorted.Count - 1));
            return sorted[index];
        }
    }

    /// <summary>
    /// Creates fresh agent and reward-policy instances for every seed so a
    /// stateful implementation cannot contaminate the next sample.
    /// </summary>
    public static class StageSimulationBatchRunner
    {
        [Obsolete(
            "G1F1-only compatibility overload. Generic callers must supply " +
            "an explicit versioned StageSimulationRuleInput or use the " +
            "BalanceSimulationEngine Suite facade.")]
        public static StageSimulationBatchReport RunBatch(
            LevelRunProfile floor,
            StageSimulationRoute route,
            Func<ISimulationAgent> agentFactory,
            int agentRevision,
            Func<ISimulationRewardPolicy> rewardPolicyFactory,
            int seedStart,
            int count,
            int maxTurnsPerEncounter = 100,
            bool suppressDebugLogs = true)
        {
            return RunBatch(
                floor,
                route,
                agentFactory,
                agentRevision,
                rewardPolicyFactory,
                seedStart,
                count,
                StageSimulationRulePresets.CreateFirstClear(),
                maxTurnsPerEncounter,
                suppressDebugLogs);
        }

        public static StageSimulationBatchReport RunBatch(
            LevelRunProfile floor,
            StageSimulationRoute route,
            Func<ISimulationAgent> agentFactory,
            int agentRevision,
            Func<ISimulationRewardPolicy> rewardPolicyFactory,
            int seedStart,
            int count,
            StageSimulationRuleInput ruleInput,
            int maxTurnsPerEncounter = 100,
            bool suppressDebugLogs = true,
            string snapshotId = null,
            Func<int, IInstanceIdGenerator, DungeonRunSession>
                entrySessionFactory = null,
            bool treatPrerequisitesAsInformational = false)
        {
            if (agentFactory == null)
                throw new ArgumentNullException(nameof(agentFactory));
            if (rewardPolicyFactory == null)
                throw new ArgumentNullException(nameof(rewardPolicyFactory));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            var samples = new List<StageSimulationReport>(count);
            for (int index = 0; index < count; index++)
            {
                ISimulationAgent agent = agentFactory();
                ISimulationRewardPolicy rewardPolicy = rewardPolicyFactory();
                if (agent == null)
                    throw new InvalidOperationException("Agent factory returned null.");
                if (rewardPolicy == null)
                {
                    throw new InvalidOperationException(
                        "Reward policy factory returned null.");
                }

                int seed = checked(seedStart + index);
                samples.Add(StageSimulationRunner.Run(
                    floor,
                    route,
                    agent,
                    agentRevision,
                    rewardPolicy,
                    seed,
                    ruleInput,
                    maxTurnsPerEncounter,
                    suppressDebugLogs,
                    snapshotId,
                    entrySessionFactory == null
                        ? (Func<IInstanceIdGenerator, DungeonRunSession>)null
                        : ids => entrySessionFactory(seed, ids),
                    treatPrerequisitesAsInformational));
            }
            return new StageSimulationBatchReport(samples);
        }
    }
}
