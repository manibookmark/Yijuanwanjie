using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Runs a versioned typed route through one production floor while
    /// DungeonRunSession remains the sole owner of cross-encounter HP, deck,
    /// level, experience, and node lifecycle. Non-UI interaction settlement is
    /// shared with checkpoint generation; only combat steps emit metrics.
    /// </summary>
    public static class StageSimulationRunner
    {
        [Obsolete(
            "G1F1-only compatibility overload. Generic callers must supply " +
            "an explicit versioned StageSimulationRuleInput or use the " +
            "BalanceSimulationEngine Suite facade.")]
        public static StageSimulationReport Run(
            LevelRunProfile floor,
            StageSimulationRoute route,
            ISimulationAgent agent,
            int agentRevision,
            ISimulationRewardPolicy rewardPolicy,
            int masterSeed,
            int maxTurnsPerEncounter = 100,
            bool suppressDebugLogs = true)
        {
            return Run(
                floor,
                route,
                agent,
                agentRevision,
                rewardPolicy,
                masterSeed,
                StageSimulationRulePresets.CreateFirstClear(),
                maxTurnsPerEncounter,
                suppressDebugLogs);
        }

        /// <summary>
        /// entryAccount is read only for frozen Run-only admission on a fresh
        /// entry. Null retains the account-free diagnostic default. A supplied
        /// entrySessionFactory owns its captured configuration (including old
        /// checkpoint migrations), so that branch never rechecks this account.
        /// Combat rules/stat projections remain the existing explicit inputs.
        /// </summary>
        public static StageSimulationReport Run(
            LevelRunProfile floor,
            StageSimulationRoute route,
            ISimulationAgent agent,
            int agentRevision,
            ISimulationRewardPolicy rewardPolicy,
            int masterSeed,
            StageSimulationRuleInput ruleInput,
            int maxTurnsPerEncounter = 100,
            bool suppressDebugLogs = true,
            string snapshotId = null,
            Func<IInstanceIdGenerator, DungeonRunSession> entrySessionFactory = null,
            bool treatPrerequisitesAsInformational = false,
            MetaProgression entryAccount = null)
        {
            ValidateInputs(
                floor,
                route,
                agent,
                agentRevision,
                rewardPolicy,
                maxTurnsPerEncounter);

            var random = new SeededRandomSource(masterSeed);
            var instanceIds = new SeededInstanceIdGenerator(1);
            StageSimulationRuleInput resolvedRuleInput = ruleInput ??
                throw new ArgumentNullException(nameof(ruleInput));
            CombatRuleSet resolvedRules = resolvedRuleInput.CreateRules();
            string resolvedSnapshotId = string.IsNullOrWhiteSpace(snapshotId)
                ? floor.ResolveDefaultSnapshotId()
                : snapshotId;
            if (entrySessionFactory == null &&
                floor.FindSnapshot(resolvedSnapshotId) == null)
            {
                throw new InvalidOperationException(
                    $"Simulation floor {floor.contentId} has no snapshot " +
                    $"{resolvedSnapshotId}.");
            }
            DungeonRunSession dungeon = entrySessionFactory == null
                ? DungeonRunSession.Begin(
                    floor,
                    resolvedSnapshotId,
                    BuildRunId(route.RouteId, masterSeed),
                    instanceIds,
                    runSeed: masterSeed,
                    cardPlayGrowthEnabled: RunStartService.CaptureCardPlayGrowthAdmission(entryAccount),
                    commonEncounterSources: id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id))
                : entrySessionFactory(instanceIds);
            if (dungeon == null || !string.Equals(
                    dungeon.CurrentFloorContentId,
                    floor.contentId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Stage entry session must target the requested floor.");
            }
            if (treatPrerequisitesAsInformational)
                dungeon.CurrentFloor.PrepareForExplicitSequence();
            string actualSnapshotId = dungeon.CurrentFloor.SnapshotId;
            MetaProgression rewardAccount = StageEncounterAssembler.FreezeRewardAccount(entryAccount)
                ?? new MetaProgression();
            var assembler = new StageEncounterAssembler();
            var transactions = new StageSimulationTransactionExecutor(random, rewardPolicy, rewardAccount);
            var encounters = new List<StageEncounterResult>(
                route.EncounterNodeContentIds.Count);

            int startingHealth = 0;
            int endingHealth = 0;
            int endingLevel = dungeon.CombatProgression.CurrentLevel;
            int endingExperience =
                dungeon.CombatProgression.CurrentExperience;
            IReadOnlyList<StageCardSnapshot> finalDeck =
                Array.Empty<StageCardSnapshot>();

            for (int index = 0;
                 index < route.Steps.Count;
                 index++)
            {
                StageSimulationStep step = route.Steps[index];
                string nodeContentId = step.NodeContentId;
                LevelNode node = dungeon.CurrentFloor.FindNode(nodeContentId);
                bool hasLaterCombat = HasLaterCombat(route.Steps, index);
                if (StageSimulationTransactionExecutor.TryGetSupportBlocker(
                        node,
                        step,
                        hasLaterCombat,
                        out string blocker))
                {
                    throw new NotSupportedException(
                        $"Route {route.RouteId} cannot execute step {index} " +
                        $"({nodeContentId}): {blocker}");
                }
                if (step.Kind != StageSimulationStepKind.Combat)
                {
                    // Exit replaces CurrentFloor atomically. Its supported
                    // rewards do not change the deck, whose definitions belong
                    // to the departing floor rather than the next entry.
                    if (step.Kind == StageSimulationStepKind.Exit)
                        finalDeck = StageSimulationTransactionExecutor.CaptureRunDeck(dungeon);
                    DungeonRunFloorExitResult exit = transactions.ExecuteInteraction(
                        dungeon, step, floor.contentId);
                    endingHealth = dungeon.CurrentHealth;
                    endingLevel = dungeon.CombatProgression.CurrentLevel;
                    endingExperience = dungeon.CombatProgression.CurrentExperience;
                    if (exit == null)
                        finalDeck = StageSimulationTransactionExecutor.CaptureRunDeck(dungeon);
                    continue;
                }

                if (!string.Equals(dungeon.CurrentFloorContentId, floor.contentId, StringComparison.Ordinal))
                    throw new InvalidOperationException("A single-floor route cannot run combat after its Exit.");

                node = RequireEncounterNode(dungeon.CurrentFloor, nodeContentId);

                if (!dungeon.CurrentFloor.TryBeginEncounter(
                        nodeContentId,
                        out LevelEncounterLaunch launch,
                        out string beginError))
                {
                    throw new InvalidOperationException(
                        $"Route {route.RouteId} cannot begin " +
                        $"{nodeContentId}: {beginError}");
                }

                BattleScenarioProfile scenario =
                    BattleScenarioCatalog.Get(launch.ScenarioId);
                if (scenario == null)
                {
                    throw new InvalidOperationException(
                        $"Route {route.RouteId} references missing scenario " +
                        $"{launch.ScenarioId} at {nodeContentId}.");
                }
                if (!resolvedRuleInput.MatchesScenarioSelection(
                        scenario.crawlerContentId))
                {
                    throw new InvalidOperationException(
                        $"Scenario {scenario.scenarioId} selects hero " +
                        $"{scenario.crawlerContentId}, but entry input " +
                        $"{resolvedRuleInput.Id}@{resolvedRuleInput.Revision} " +
                        $"requires {resolvedRuleInput.SelectedHeroContentId}.");
                }

                using StageEncounterAssembly assembly = assembler.Create(
                    dungeon,
                    scenario,
                    random,
                    instanceIds,
                    resolvedRules,
                    rewardAccount: rewardAccount);
                CombatController combat = assembly.Combat;
                int enteringHealth = combat.Player.currentHealth;
                int enteringLevel = combat.CurrentLevel;
                int enteringExperience = combat.CurrentXP;
                IReadOnlyList<StageCardSnapshot> enteringDeck =
                    CaptureDeck(combat);
                if (encounters.Count == 0)
                    startingHealth = enteringHealth;

                EncounterMetrics metrics = SimulationRunner.Run(
                    combat,
                    agent,
                    maxTurnsPerEncounter,
                    suppressDebugLogs,
                    rewardPolicy,
                    scenario.entryExperienceGrant);
                endingHealth = combat.Player.currentHealth;
                endingLevel = combat.CurrentLevel;
                endingExperience = combat.CurrentXP;
                finalDeck = CaptureDeck(combat);
                encounters.Add(new StageEncounterResult(
                    nodeContentId,
                    launch.ScenarioId,
                    enteringHealth,
                    enteringLevel,
                    enteringExperience,
                    enteringDeck,
                    metrics,
                    endingLevel,
                    endingExperience,
                    finalDeck));

                if (!metrics.Win)
                {
                    if (metrics.Termination == SimulationTerminationReason.Defeat)
                    {
                        if (!dungeon.TryCompleteDefeat(combat, out _, out string defeatError))
                            throw new InvalidOperationException("Cannot commit simulation defeat: " + defeatError);
                        RunAcceptanceSession.CommitAcceptedUnlocks(rewardAccount, dungeon, assembly.UnlockRequirements);
                    }
                    else CompleteNonVictoryEncounter(dungeon, launch);
                    StageSimulationTermination termination =
                        metrics.Termination ==
                            SimulationTerminationReason.Defeat
                            ? StageSimulationTermination.CombatDefeat
                            : StageSimulationTermination.SimulationLimit;
                    return BuildReport(
                        floor,
                        actualSnapshotId,
                        route,
                        agent,
                        agentRevision,
                        rewardPolicy,
                        resolvedRuleInput,
                        masterSeed,
                        startingHealth,
                        encounters,
                        termination,
                        nodeContentId,
                        endingHealth,
                        endingLevel,
                        endingExperience,
                        finalDeck);
                }

                if (!dungeon.TryCompleteVictory(
                        combat,
                        out _,
                        out string victoryError))
                {
                    throw new InvalidOperationException(
                        $"Route {route.RouteId} cannot commit victory for " +
                        $"{nodeContentId}: {victoryError}");
                }
                RunAcceptanceSession.CommitAcceptedUnlocks(rewardAccount, dungeon, assembly.UnlockRequirements);
                transactions.ResolvePendingVictorySeal(
                    dungeon,
                    scenario,
                    step);
                if (!dungeon.CurrentFloor.TryAcknowledgeReturn(nodeContentId))
                {
                    throw new InvalidOperationException(
                        $"Route {route.RouteId} cannot acknowledge return " +
                        $"from {nodeContentId}.");
                }
            }

            return BuildReport(
                floor,
                actualSnapshotId,
                route,
                agent,
                agentRevision,
                rewardPolicy,
                resolvedRuleInput,
                masterSeed,
                startingHealth,
                encounters,
                StageSimulationTermination.RouteCompleted,
                null,
                endingHealth,
                endingLevel,
                endingExperience,
                finalDeck);
        }

        private static void ValidateInputs(
            LevelRunProfile floor,
            StageSimulationRoute route,
            ISimulationAgent agent,
            int agentRevision,
            ISimulationRewardPolicy rewardPolicy,
            int maxTurnsPerEncounter)
        {
            if (floor == null) throw new ArgumentNullException(nameof(floor));
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (string.IsNullOrWhiteSpace(agent.Name))
                throw new ArgumentException("Agent name cannot be empty.", nameof(agent));
            if (agentRevision <= 0)
                throw new ArgumentOutOfRangeException(nameof(agentRevision));
            if (rewardPolicy == null)
                throw new ArgumentNullException(nameof(rewardPolicy));
            if (string.IsNullOrWhiteSpace(rewardPolicy.Id) ||
                rewardPolicy.Revision <= 0)
            {
                throw new ArgumentException(
                    "Reward policy must have a stable id and positive revision.",
                    nameof(rewardPolicy));
            }
            if (maxTurnsPerEncounter <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTurnsPerEncounter));

            List<string> errors = floor.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Simulation floor {floor.contentId} is invalid:" +
                    Environment.NewLine + string.Join(Environment.NewLine, errors));
            }
        }

        private static LevelNode RequireEncounterNode(
            LevelRunSession floor,
            string nodeContentId)
        {
            LevelNode node = floor.FindNode(nodeContentId);
            if (node == null)
                throw new InvalidOperationException(
                    $"Simulation route references missing node {nodeContentId}.");
            if (node.nodeType != LevelNodeType.Encounter &&
                node.nodeType != LevelNodeType.Finale)
            {
                throw new InvalidOperationException(
                    $"Simulation route node {nodeContentId} is " +
                    $"{node.nodeType}, not a combat node.");
            }
            return node;
        }

        internal static void RequireEncounterOnlyRewards(
            LevelNode node,
            bool hasLaterCombat)
        {
            if (!TryGetVictoryRewardBlocker(
                node,
                hasLaterCombat,
                out string blocker))
            {
                return;
            }
            throw new NotSupportedException(
                $"Combat node {node?.contentId} cannot be simulated as a " +
                $"route step: {blocker}");
        }

        internal static bool TryGetVictoryRewardBlocker(
            LevelNode node,
            bool hasLaterCombat,
            out string blocker)
        {
            string nodeId = string.IsNullOrWhiteSpace(node?.contentId)
                ? "missing.node"
                : node.contentId;
            return StageSimulationTransactionExecutor.TryGetSupportBlocker(
                node,
                new StageSimulationStep(
                    StageSimulationStepKind.Combat,
                    nodeId),
                hasLaterCombat,
                out blocker);
        }

        private static bool HasLaterCombat(
            IReadOnlyList<StageSimulationStep> steps,
            int currentIndex)
        {
            for (int i = currentIndex + 1; i < steps.Count; i++)
            {
                if (steps[i].Kind == StageSimulationStepKind.Combat)
                    return true;
            }
            return false;
        }

        private static void CompleteNonVictoryEncounter(
            DungeonRunSession dungeon,
            LevelEncounterLaunch launch)
        {
            if (!dungeon.CurrentFloor.TryCompleteEncounter(
                    launch.RunId,
                    launch.NodeContentId,
                    CombatOutcome.Defeat,
                    out _,
                    out string completeError))
            {
                throw new InvalidOperationException(
                    $"Cannot close non-victory encounter " +
                    $"{launch.NodeContentId}: {completeError}");
            }
            if (!dungeon.CurrentFloor.TryAcknowledgeReturn(
                    launch.NodeContentId))
            {
                throw new InvalidOperationException(
                    $"Cannot acknowledge non-victory encounter " +
                    $"{launch.NodeContentId}.");
            }
        }

        private static IReadOnlyList<StageCardSnapshot> CaptureDeck(
            CombatController combat)
        {
            var cardsById = new Dictionary<int, StageCardSnapshot>();
            AddCards(cardsById, combat.Deck);
            AddCards(cardsById, combat.Hand);
            AddCards(cardsById, combat.Discard);

            var result = new List<StageCardSnapshot>(cardsById.Values);
            result.Sort((left, right) =>
                left.RuntimeInstanceId.CompareTo(right.RuntimeInstanceId));
            return result.AsReadOnly();
        }

        private static void AddCards(
            IDictionary<int, StageCardSnapshot> target,
            IReadOnlyList<CardConfig> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                CardConfig card = source[i];
                if (card == null || card.runtimeInstanceId <= 0)
                    continue;
                target[card.runtimeInstanceId] = new StageCardSnapshot(
                    card.runtimeInstanceId,
                    card.contentId ?? string.Empty);
            }
        }

        private static StageSimulationReport BuildReport(
            LevelRunProfile floor,
            string snapshotId,
            StageSimulationRoute route,
            ISimulationAgent agent,
            int agentRevision,
            ISimulationRewardPolicy rewardPolicy,
            StageSimulationRuleInput ruleInput,
            int masterSeed,
            int startingHealth,
            IReadOnlyList<StageEncounterResult> encounters,
            StageSimulationTermination termination,
            string failedNodeContentId,
            int endingHealth,
            int endingLevel,
            int endingExperience,
            IReadOnlyList<StageCardSnapshot> finalDeck)
        {
            return new StageSimulationReport(
                floor.contentId,
                snapshotId,
                route.RouteId,
                route.Revision,
                masterSeed,
                agent.Name,
                agentRevision,
                rewardPolicy.Id,
                rewardPolicy.Revision,
                ruleInput.Id,
                ruleInput.Revision,
                ruleInput.SelectedHeroContentId,
                startingHealth,
                encounters,
                termination,
                failedNodeContentId,
                endingHealth,
                endingLevel,
                endingExperience,
                finalDeck);
        }

        private static string BuildRunId(string routeId, int masterSeed)
        {
            return $"simulation.{routeId}.{masterSeed}";
        }
    }
}
