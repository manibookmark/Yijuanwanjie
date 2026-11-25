using System;
using System.Collections.Generic;
using System.Globalization;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Compiles a suite's declared coverage against production Floor/Scenario
    /// content. Unsupported state changes are evidence, never silently skipped.
    /// </summary>
    internal static class BalanceSuitePreflight
    {
        public static void Validate(
            BalanceSimulationSuite suite,
            LevelRunProfile floor,
            ICollection<BalanceCoverageGap> gaps)
        {
            var errors = new List<string>();
            List<string> floorErrors = floor.Validate();
            if (floorErrors.Count > 0)
            {
                errors.Add(
                    $"Floor {floor.contentId} is invalid:" + Environment.NewLine +
                    string.Join(Environment.NewLine, floorErrors));
            }
            if (!string.Equals(
                    floor.contentId,
                    suite.FloorContentId,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Suite floorContentId {suite.FloorContentId} does not " +
                    $"match loaded floor {floor.contentId}.");
            }
            if (suite.EntryState.Kind == BalanceEntryStateKind.FreshRun &&
                floor.FindSnapshot(suite.SnapshotId) == null)
                errors.Add($"Floor has no snapshot {suite.SnapshotId}.");
            if (suite.EntryState.Kind == BalanceEntryStateKind.FreshRun &&
                floor.floorIndex > 1)
            {
                errors.Add(
                    $"Floor {floor.contentId} is floorIndex={floor.floorIndex}. " +
                    "Higher floors require entryState.kind=Checkpoint so " +
                    "production HP/deck/level/relic carry-over is not replaced " +
                    "by scenario defaults.");
            }
            if (!ContentCatalog.Current.TryGetCrawler(
                    suite.EntryRule.SelectedHeroContentId,
                    out _))
            {
                errors.Add(
                    $"Entry hero does not exist: " +
                    $"{suite.EntryRule.SelectedHeroContentId}.");
            }

            ValidateBuiltinInputs(suite, errors);
            ValidateEntryState(suite, floor, errors);

            var exclusions = new Dictionary<string, BalanceCoverageExclusion>(
                StringComparer.Ordinal);
            for (int i = 0; i < suite.CoverageExclusions.Count; i++)
            {
                BalanceCoverageExclusion exclusion =
                    suite.CoverageExclusions[i];
                exclusions[exclusion.NodeContentId] = exclusion;
            }

            var combatNodeIds = new HashSet<string>(StringComparer.Ordinal);
            var interactionNodeIds = new HashSet<string>(StringComparer.Ordinal);
            ISet<string> fullRouteStepNodeIds =
                BuildFullRouteStepNodeIds(suite);
            for (int i = 0; i < floor.nodes.Count; i++)
            {
                LevelNode node = floor.nodes[i];
                if (node == null) continue;
                if (IsCombat(node))
                {
                    combatNodeIds.Add(node.contentId);
                    if (node.victoryRewards != null &&
                        node.victoryRewards.Count > 0)
                    {
                        bool hasDownstream = HasDownstreamCombat(
                            suite,
                            node.contentId);
                        bool blocksRoute = TryGetVictoryRewardBlocker(
                            suite,
                            node,
                            hasDownstream,
                            out string rewardBlocker);
                        if (blocksRoute ||
                            suite.CoverageMode ==
                                BalanceCoverageMode.CombatOnly)
                        {
                            gaps.Add(new BalanceCoverageGap
                            {
                                nodeContentId = node.contentId,
                                nodeType = "VictoryReward",
                                affectsCombat = blocksRoute,
                                blocksCalibration = blocksRoute,
                                configuredEffects = DescribeVictoryRewards(
                                    node.victoryRewards),
                                declaredReason = blocksRoute
                                    ? rewardBlocker
                                    : "Terminal CrawlerUnlock occurs after " +
                                      "the measured combat and does not mutate " +
                                      "this Run."
                            });
                        }
                    }
                    ValidateScenario(suite, node, errors);
                    continue;
                }

                if (node.nodeType != LevelNodeType.Interaction &&
                    node.nodeType != LevelNodeType.Exit)
                {
                    continue;
                }
                interactionNodeIds.Add(node.contentId);
                bool coveredByProductionRoute =
                    suite.CoverageMode ==
                        BalanceCoverageMode.ProductionRoute &&
                    fullRouteStepNodeIds.Contains(node.contentId);
                if (coveredByProductionRoute)
                    continue;
                if (!exclusions.TryGetValue(
                        node.contentId,
                        out BalanceCoverageExclusion declared))
                {
                    errors.Add(
                        $"Unsupported node {node.contentId} must be explicitly " +
                        "listed in coverageExclusions with a reason.");
                    continue;
                }

                bool affectsCombat = EffectsAffectCombat(node.completionEffects);
                gaps.Add(new BalanceCoverageGap
                {
                    nodeContentId = node.contentId,
                    nodeType = node.nodeType.ToString(),
                    affectsCombat = affectsCombat,
                    blocksCalibration = affectsCombat,
                    configuredEffects = DescribeEffects(node.completionEffects),
                    declaredReason = declared.Reason
                });
            }

            foreach (string excludedNodeId in exclusions.Keys)
            {
                if (!interactionNodeIds.Contains(excludedNodeId))
                {
                    errors.Add(
                        $"coverageExclusions references a node that is not an " +
                        $"Interaction/Exit: {excludedNodeId}.");
                }
            }

            var isolatedNodes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < suite.IsolatedEncounters.Count; i++)
            {
                BalanceEncounterDefinition definition =
                    suite.IsolatedEncounters[i];
                LevelNode node = floor.FindNode(definition.NodeContentId);
                if (node == null || !IsCombat(node))
                {
                    errors.Add(
                        $"Isolated target {definition.Id} references missing " +
                        $"combat node {definition.NodeContentId}.");
                }
                isolatedNodes.Add(definition.NodeContentId);
            }
            foreach (string combatNodeId in combatNodeIds)
            {
                if (!isolatedNodes.Contains(combatNodeId))
                {
                    errors.Add(
                        $"Combat node {combatNodeId} is not covered by " +
                        "isolatedEncounters.");
                }
            }

            var fullRouteNodeIds = new HashSet<string>(StringComparer.Ordinal);
            int fullRouteCount = 0;
            for (int i = 0; i < suite.Routes.Count; i++)
            {
                BalanceRouteDefinition route = suite.Routes[i];
                if (route.Purpose == BalanceRoutePurpose.Full)
                {
                    fullRouteCount++;
                    for (int nodeIndex = 0;
                         nodeIndex < route.NodeContentIds.Count;
                         nodeIndex++)
                    {
                        fullRouteNodeIds.Add(route.NodeContentIds[nodeIndex]);
                    }
                }
                for (int j = 0; j < route.Steps.Count; j++)
                {
                    StageSimulationStep step = route.Steps[j];
                    string nodeId = step.NodeContentId;
                    LevelNode node = floor.FindNode(nodeId);
                    bool hasLaterCombat = HasLaterCombat(route.Steps, j);
                    if (StageSimulationTransactionExecutor.TryGetSupportBlocker(
                            node,
                            step,
                            hasLaterCombat,
                            out string blocker))
                    {
                        errors.Add(
                            $"Route {route.Id} step {j} ({nodeId}) is not " +
                            $"executable: {blocker}");
                    }
                }
            }
            if (fullRouteCount > 0)
            {
                foreach (string combatNodeId in combatNodeIds)
                {
                    if (!fullRouteNodeIds.Contains(combatNodeId))
                    {
                        errors.Add(
                            "The union of purpose=Full routes omits combat " +
                            $"node {combatNodeId}. Whole-floor Calibration " +
                            "requires every combat node to belong to at " +
                            "least one complete exit-reaching route.");
                    }
                }
            }

            if (errors.Count == 0 && !HasCalibrationBlock(gaps))
                ValidateRouteReachability(suite, floor, errors);

            if (suite.EntryState.Kind == BalanceEntryStateKind.FreshRun)
                ValidateEntryFingerprints(suite, floor, errors);
            if (errors.Count > 0)
            {
                throw new BalanceSuiteException(
                    "Suite does not match production content:" +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, errors));
            }
        }

        private static void ValidateEntryState(
            BalanceSimulationSuite suite,
            LevelRunProfile floor,
            ICollection<string> errors)
        {
            try
            {
                DungeonRunSession entry = BalanceEntrySessionFactory.Create(
                    suite,
                    floor,
                    "balance.preflight",
                    new SeededInstanceIdGenerator(1),
                    runSeed: 1);
                if (suite.EntryState.Kind == BalanceEntryStateKind.Checkpoint &&
                    suite.IsolatedEncounters.Count > 0)
                {
                    LevelNode firstNode = entry.CurrentFloor.FindNode(
                        suite.IsolatedEncounters[0].NodeContentId);
                    BattleScenarioProfile scenario = firstNode == null
                        ? null
                        : BattleScenarioCatalog.Get(firstNode.scenarioId);
                    if (scenario != null)
                    {
                        using StageEncounterAssembly assembly =
                            new StageEncounterAssembler().Create(
                                entry,
                                scenario,
                                new SeededRandomSource(1),
                                new SeededInstanceIdGenerator(1),
                                suite.EntryRule.CreateRules(), nodeContentId: firstNode.contentId);
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }
        }

        internal static void ValidateRouteReachability(
            BalanceSimulationSuite suite,
            LevelRunProfile floor,
            ICollection<string> errors,
            MetaProgression rewardAccount = null)
        {
            for (int i = 0; i < suite.Routes.Count; i++)
            {
                BalanceRouteDefinition route = suite.Routes[i];
                DungeonRunSession dungeon;
                var instanceIds = new SeededInstanceIdGenerator(1);
                var transactions = new StageSimulationTransactionExecutor(
                    new SeededRandomSource(1), rewardAccount: rewardAccount);
                try
                {
                    dungeon = BalanceEntrySessionFactory.Create(
                        suite,
                        floor,
                        "balance.preflight.route." + route.Id,
                        instanceIds,
                        runSeed: 1);
                }
                catch (Exception exception)
                {
                    errors.Add(exception.Message);
                    return;
                }

                // Only explicitly declared design experiments disregard the map
                // prerequisites. Production routes retain runtime reachability.
                if (suite.UsesDesignSequence)
                    dungeon.CurrentFloor.PrepareForExplicitSequence();

                LevelRunSession routeFloor = dungeon.CurrentFloor;

                bool entryPrepared = suite.EntryState.Kind == BalanceEntryStateKind.Checkpoint;
                bool routeExecutionFailed = false;
                for (int j = 0; j < route.Steps.Count; j++)
                {
                    StageSimulationStep step = route.Steps[j];
                    string nodeId = step.NodeContentId;
                    try
                    {
                        if (step.Kind != StageSimulationStepKind.Combat)
                        {
                            transactions.ExecuteInteraction(dungeon, step, floor.contentId);
                            continue;
                        }
                        if (!string.Equals(dungeon.CurrentFloorContentId, floor.contentId, StringComparison.Ordinal))
                            throw new InvalidOperationException("A single-floor route cannot run combat after its Exit.");
                        if (!dungeon.CurrentFloor.TryBeginEncounter(
                                nodeId,
                                out LevelEncounterLaunch launch,
                                out string beginError))
                        {
                            throw new InvalidOperationException(beginError);
                        }
                        LevelNode node = routeFloor.FindNode(nodeId);
                        BattleScenarioProfile scenario = node == null
                            ? null
                            : BattleScenarioCatalog.Get(node.scenarioId);
                        if (scenario == null)
                        {
                            throw new InvalidOperationException(
                                "missing combat scenario.");
                        }
                        if (!entryPrepared)
                        {
                            // StageSimulationRunner creates the first deck only after this
                            // encounter begins. Earlier map nodes must see the same empty
                            // FreshRun state; preflight cannot grant their host cards early.
                            PrepareRouteEntry(dungeon, suite.EntryState.Kind, () =>
                                ScenarioFactory.FromProfile(scenario, suite.EntryRule.CreateRules(),
                                    dungeon.ResolveEnemyRoster(scenario.scenarioId, nodeId)?.GeneratedRoster));
                            entryPrepared = true;
                        }
                        if (!dungeon.CurrentFloor.TryCompleteEncounter(
                                launch.RunId,
                                nodeId,
                                CombatOutcome.Victory,
                                out _,
                                out string completeError))
                        {
                            throw new InvalidOperationException(completeError);
                        }
                        transactions.ResolvePendingVictorySeal(
                            dungeon,
                            scenario,
                            step);
                        if (!dungeon.CurrentFloor.TryAcknowledgeReturn(nodeId))
                        {
                            throw new InvalidOperationException(
                                "return acknowledgement failed.");
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Add(
                            $"Route {route.Id} cannot execute design-sequence " +
                            $"step {j} ({nodeId}): {exception.Message}");
                        routeExecutionFailed = true;
                        break;
                    }
                }

                // An Exit that was never reached is not a second route
                // declaration error. Keep the first execution failure as the
                // actionable diagnostic instead of reporting a false Exit gap.
                if (routeExecutionFailed)
                    continue;

                if (route.Purpose != BalanceRoutePurpose.Full)
                    continue;
                bool definesExit = false;
                bool canExit = false;
                var routeNodes = routeFloor.Nodes;
                for (int nodeIndex = 0;
                     nodeIndex < routeNodes.Count;
                     nodeIndex++)
                {
                    LevelNode floorNode = routeNodes[nodeIndex];
                    if (floorNode == null ||
                        floorNode.nodeType != LevelNodeType.Exit)
                        continue;
                    definesExit = true;
                    if (routeFloor.GetNodeState(
                            floorNode.contentId) ==
                        LevelNodeViewState.Available)
                    {
                        canExit = true;
                    }
                }
                if (definesExit &&
                    suite.CoverageMode ==
                        BalanceCoverageMode.ProductionRoute)
                {
                    StageSimulationStep lastStep = route.Steps.Count > 0
                        ? route.Steps[route.Steps.Count - 1]
                        : null;
                    if (lastStep == null ||
                        lastStep.Kind != StageSimulationStepKind.Exit ||
                        routeFloor.GetNodeState(
                            lastStep.NodeContentId) !=
                            LevelNodeViewState.Completed)
                    {
                        errors.Add(
                            $"Full production route {route.Id} must end with " +
                            "an explicitly completed Exit step.");
                    }
                    continue;
                }
                if (definesExit && !canExit)
                {
                    errors.Add(
                        $"Full route {route.Id} does not leave any Exit node " +
                        "available after its declared combat sequence.");
                }
                if (definesExit) continue;

                string lastNodeId = route.Steps.Count > 0
                    ? route.Steps[route.Steps.Count - 1].NodeContentId
                    : string.Empty;
                LevelNode terminal = routeFloor.FindNode(lastNodeId);
                if (terminal == null ||
                    terminal.nodeType != LevelNodeType.Finale ||
                    routeFloor.GetNodeState(lastNodeId) !=
                        LevelNodeViewState.Completed)
                {
                    errors.Add(
                        $"Full route {route.Id} targets a floor without an " +
                        "Exit, but its last declared step is not a completed " +
                        "Finale. A partial combat branch cannot establish a " +
                        "whole-floor Calibration result.");
                }
            }
        }

        /// <summary>
        /// Establishes entry state when the route reaches its first started encounter.
        /// Checkpoints already own that state. Resolving content does not start a battle,
        /// apply entry XP, predict combat growth, or manufacture a victory receipt.
        /// The specification and the temporary prepared deck are owned by this call.
        /// </summary>
        internal static void PrepareRouteEntry(DungeonRunSession dungeon,
            BalanceEntryStateKind entryKind, Func<EncounterScenarioSpec> resolveSpecification)
        {
            if (dungeon == null) throw new ArgumentNullException(nameof(dungeon));
            if (entryKind == BalanceEntryStateKind.Checkpoint) return;
            if (entryKind != BalanceEntryStateKind.FreshRun)
                throw new ArgumentOutOfRangeException(nameof(entryKind));
            if (resolveSpecification == null) throw new ArgumentNullException(nameof(resolveSpecification));
            EncounterScenarioSpec spec = resolveSpecification() ??
                throw new InvalidOperationException("Entry specification is missing.");
            using (var owned = new StageEncounterAssembly(spec))
            {
                List<CardConfig> prepared = null;
                try
                {
                    if (!dungeon.TryPrepareCombat(spec.Player, spec.Deck, spec.OwnedCardPool,
                            out prepared, out string error))
                        throw new InvalidOperationException("Cannot prepare route entry: " + error);
                }
                finally
                {
                    // The transferred graph includes all bound enemy templates,
                    // including health-group compound secondary cards. It follows
                    // the same resource owner as an assembled encounter.
                    owned.TrackCards(prepared);
                }
            }
        }

        public static bool HasCalibrationBlock(
            IEnumerable<BalanceCoverageGap> gaps)
        {
            foreach (BalanceCoverageGap gap in gaps)
                if (gap.blocksCalibration) return true;
            return false;
        }

        public static string DescribeCalibrationBlockers(
            IReadOnlyList<BalanceCoverageGap> gaps)
        {
            var values = new List<string>();
            for (int i = 0; i < gaps.Count; i++)
            {
                if (!gaps[i].blocksCalibration) continue;
                values.Add(
                    gaps[i].nodeContentId + "[" + gaps[i].configuredEffects + "]");
            }
            return string.Join("; ", values);
        }

        private static void ValidateBuiltinInputs(
            BalanceSimulationSuite suite,
            ICollection<string> errors)
        {
            try
            {
                _ = BalanceSimulationEngine.CreateAgent(suite.Agent);
                ISimulationRewardPolicy rewardPolicy =
                    BalanceSimulationEngine.CreateRewardPolicy(
                        suite.RewardPolicy);
                if (!string.Equals(
                        rewardPolicy.Id,
                        suite.RewardPolicy.Id,
                        StringComparison.OrdinalIgnoreCase) ||
                    rewardPolicy.Revision != suite.RewardPolicy.Revision)
                {
                    errors.Add(
                        "Reward policy implementation version does not match suite.");
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }
        }

        private static void ValidateScenario(
            BalanceSimulationSuite suite,
            LevelNode node,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(node.scenarioId))
            {
                errors.Add($"Combat node {node.contentId} has no scenarioId.");
                return;
            }
            BattleScenarioProfile scenario = BattleScenarioCatalog.Get(
                node.scenarioId);
            if (scenario == null)
            {
                errors.Add(
                    $"Combat node {node.contentId} references missing scenario " +
                    $"{node.scenarioId}.");
                return;
            }
            List<string> scenarioErrors = scenario.ValidateProfile();
            if (scenarioErrors.Count > 0)
            {
                errors.Add(
                    $"Scenario {scenario.scenarioId} is invalid: " +
                    string.Join(" | ", scenarioErrors));
            }
            StageSimulationRuleInput input = suite.EntryRule.CreateStageInput();
            if (!input.MatchesScenarioSelection(scenario.crawlerContentId))
            {
                errors.Add(
                    $"Scenario {scenario.scenarioId} selects hero " +
                    $"{scenario.crawlerContentId}; suite entry selects " +
                    $"{input.SelectedHeroContentId}.");
            }
        }

        private static void ValidateEntryFingerprints(
            BalanceSimulationSuite suite,
            LevelRunProfile floor,
            ICollection<string> errors)
        {
            string expected = null;
            string expectedScenario = null;
            for (int i = 0; i < suite.IsolatedEncounters.Count; i++)
            {
                LevelNode node = floor.FindNode(
                    suite.IsolatedEncounters[i].NodeContentId);
                if (node == null || string.IsNullOrWhiteSpace(node.scenarioId))
                    continue;
                BattleScenarioProfile scenario = BattleScenarioCatalog.Get(
                    node.scenarioId);
                if (scenario == null) continue;
                string fingerprint = BuildEntryFingerprint(suite, scenario);
                if (expected == null)
                {
                    expected = fingerprint;
                    expectedScenario = scenario.scenarioId;
                }
                else if (!string.Equals(
                             expected,
                             fingerprint,
                             StringComparison.Ordinal))
                {
                    errors.Add(
                        $"Entry build drift: {scenario.scenarioId} differs from " +
                        $"{expectedScenario}. Player base stats, ordered deck, " +
                        "volume, and selected hero must agree.");
                }
            }
        }

        private static string BuildEntryFingerprint(
            BalanceSimulationSuite suite,
            BattleScenarioProfile scenario)
        {
            string hero = string.IsNullOrWhiteSpace(scenario.crawlerContentId)
                ? suite.EntryRule.SelectedHeroContentId
                : scenario.crawlerContentId;
            return string.Join("|", new[]
            {
                scenario.volumeId ?? string.Empty,
                scenario.maxHealth.ToString(CultureInfo.InvariantCulture),
                scenario.baseMana.ToString(CultureInfo.InvariantCulture),
                scenario.startingArmor.ToString(CultureInfo.InvariantCulture),
                scenario.handLimit.ToString(CultureInfo.InvariantCulture),
                scenario.turnDrawCount.ToString(CultureInfo.InvariantCulture),
                hero ?? string.Empty,
                scenario.deckContentIds == null
                    ? string.Empty
                    : string.Join(",", scenario.deckContentIds)
            });
        }

        private static bool IsCombat(LevelNode node)
        {
            return node.nodeType == LevelNodeType.Encounter ||
                node.nodeType == LevelNodeType.Finale;
        }

        private static ISet<string> BuildFullRouteStepNodeIds(
            BalanceSimulationSuite suite)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < suite.Routes.Count; i++)
            {
                BalanceRouteDefinition route = suite.Routes[i];
                if (route.Purpose != BalanceRoutePurpose.Full) continue;
                for (int j = 0; j < route.Steps.Count; j++)
                    result.Add(route.Steps[j].NodeContentId);
            }
            return result;
        }

        private static bool TryGetVictoryRewardBlocker(
            BalanceSimulationSuite suite,
            LevelNode node,
            bool hasDownstreamCombat,
            out string blocker)
        {
            if (suite.CoverageMode == BalanceCoverageMode.ProductionRoute)
            {
                string typedBlocker = null;
                for (int i = 0; i < suite.Routes.Count; i++)
                {
                    BalanceRouteDefinition route = suite.Routes[i];
                    if (route.Purpose != BalanceRoutePurpose.Full) continue;
                    for (int j = 0; j < route.Steps.Count; j++)
                    {
                        StageSimulationStep step = route.Steps[j];
                        if (step.Kind != StageSimulationStepKind.Combat ||
                            !string.Equals(
                                step.NodeContentId,
                                node.contentId,
                                StringComparison.Ordinal))
                        {
                            continue;
                        }
                        if (!StageSimulationTransactionExecutor
                                .TryGetSupportBlocker(
                                    node,
                                    step,
                                    HasLaterCombat(route.Steps, j),
                                    out typedBlocker))
                        {
                            blocker = null;
                            return false;
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(typedBlocker))
                {
                    blocker = typedBlocker;
                    return true;
                }
            }
            return StageSimulationRunner.TryGetVictoryRewardBlocker(
                node,
                hasDownstreamCombat,
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

        private static bool HasDownstreamCombat(
            BalanceSimulationSuite suite,
            string nodeContentId)
        {
            for (int i = 0; i < suite.Routes.Count; i++)
            {
                IReadOnlyList<StageSimulationStep> steps = suite.Routes[i].Steps;
                for (int j = 0; j < steps.Count; j++)
                {
                    if (string.Equals(
                            steps[j].NodeContentId,
                            nodeContentId,
                            StringComparison.Ordinal) &&
                        HasLaterCombat(steps, j))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string DescribeVictoryRewards(
            IReadOnlyList<LevelRewardGrant> rewards)
        {
            var values = new List<string>();
            for (int i = 0; i < rewards.Count; i++)
            {
                LevelRewardGrant reward = rewards[i];
                if (reward == null) continue;
                values.Add(
                    reward.kind + ":" + reward.contentId + "@" +
                    reward.targetContentId);
            }
            return string.Join(";", values);
        }

        private static bool EffectsAffectCombat(
            IReadOnlyList<LevelInteractionEffect> effects)
        {
            if (effects == null) return false;
            for (int i = 0; i < effects.Count; i++)
            {
                LevelInteractionEffect effect = effects[i];
                if (effect == null) continue;
                if (effect.kind != LevelInteractionEffectKind.GrantJade)
                    return true;
            }
            return false;
        }

        private static string DescribeEffects(
            IReadOnlyList<LevelInteractionEffect> effects)
        {
            if (effects == null || effects.Count == 0) return "none";
            var values = new List<string>(effects.Count);
            for (int i = 0; i < effects.Count; i++)
            {
                LevelInteractionEffect effect = effects[i];
                if (effect == null) continue;
                string value = effect.kind + ":" + effect.amount;
                if (effect.amountMax > effect.amount)
                    value += "-" + effect.amountMax;
                if (!string.IsNullOrWhiteSpace(effect.targetContentId))
                    value += "@" + effect.targetContentId;
                values.Add(value);
            }
            return string.Join(";", values);
        }
    }
}
