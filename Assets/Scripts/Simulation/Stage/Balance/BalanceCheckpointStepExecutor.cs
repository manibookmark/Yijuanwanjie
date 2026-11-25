using System;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Adapts checkpoint recipes to combat sampling. All interaction and
    /// pending-reward settlement is delegated to the shared typed-route
    /// transaction executor so checkpoint and batch routes cannot drift.
    /// </summary>
    internal sealed class BalanceCheckpointStepExecutor
    {
        private readonly BalanceCheckpointRecipe _recipe;
        private readonly ISimulationAgent _agent;
        private readonly ISimulationRewardPolicy _rewardPolicy;
        private readonly CombatRuleSet _rules;
        private readonly IRandomSource _random;
        private readonly IInstanceIdGenerator _instanceIds;
        private readonly StageEncounterAssembler _assembler;
        private readonly StageSimulationTransactionExecutor _transactions;

        internal BalanceCheckpointStepExecutor(
            BalanceCheckpointRecipe recipe,
            IRandomSource random,
            IInstanceIdGenerator instanceIds)
        {
            _recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _instanceIds = instanceIds ??
                throw new ArgumentNullException(nameof(instanceIds));
            _agent = BalanceSimulationEngine.CreateAgent(recipe.SourceSuite.Agent);
            _rewardPolicy = BalanceSimulationEngine.CreateRewardPolicy(
                recipe.SourceSuite.RewardPolicy);
            _rules = recipe.SourceSuite.EntryRule.CreateRules();
            _assembler = new StageEncounterAssembler();
            _transactions = new StageSimulationTransactionExecutor(random, _rewardPolicy);
        }

        internal DungeonRunFloorExitResult Execute(
            DungeonRunSession dungeon,
            StageSimulationStep step,
            string expectedFloorId = null)
        {
            try
            {
                switch (step.Kind)
                {
                    case StageSimulationStepKind.Combat:
                        if (expectedFloorId != null && !string.Equals(expectedFloorId,
                                dungeon.CurrentFloorContentId, StringComparison.Ordinal))
                            throw new InvalidOperationException("Checkpoint combat belongs to a departed floor.");
                        ExecuteCombat(dungeon, step);
                        return null;
                    case StageSimulationStepKind.Interaction:
                    case StageSimulationStepKind.Exit:
                        return _transactions.ExecuteInteraction(dungeon, step, expectedFloorId);
                    default:
                        throw new InvalidOperationException(
                            "Unsupported checkpoint step kind: " +
                            step.Kind + ".");
                }
            }
            catch (BalanceCheckpointException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BalanceCheckpointException(
                    $"Checkpoint step {step?.NodeContentId} failed: " +
                    exception.Message,
                    exception);
            }
        }

        private void ExecuteCombat(
            DungeonRunSession dungeon,
            StageSimulationStep step)
        {
            LevelNode node = dungeon.CurrentFloor.FindNode(
                step.NodeContentId);
            if (node == null ||
                (node.nodeType != LevelNodeType.Encounter &&
                 node.nodeType != LevelNodeType.Finale))
            {
                throw new BalanceCheckpointException(
                    "Checkpoint combat step is not a combat node: " +
                    step.NodeContentId + ".");
            }
            if (!dungeon.CurrentFloor.TryBeginEncounter(
                    step.NodeContentId,
                    out LevelEncounterLaunch launch,
                    out string beginError))
            {
                throw new BalanceCheckpointException(
                    $"Cannot begin checkpoint combat {step.NodeContentId}: " +
                    beginError);
            }
            BattleScenarioProfile scenario = BattleScenarioCatalog.Get(
                launch.ScenarioId);
            if (scenario == null)
            {
                throw new BalanceCheckpointException(
                    "Checkpoint combat references missing scenario " +
                    launch.ScenarioId + ".");
            }
            if (!_recipe.SourceSuite.EntryRule.CreateStageInput()
                    .MatchesScenarioSelection(scenario.crawlerContentId))
            {
                throw new BalanceCheckpointException(
                    $"Scenario {scenario.scenarioId} selects hero " +
                    $"{scenario.crawlerContentId}, but recipe source Suite " +
                    $"selects {_recipe.SourceSuite.EntryRule.SelectedHeroContentId}.");
            }

            using StageEncounterAssembly assembly = _assembler.Create(
                dungeon,
                scenario,
                _random,
                _instanceIds,
                _rules);
            CombatController combat = assembly.Combat;
            EncounterMetrics metrics = SimulationRunner.Run(
                combat,
                _agent,
                _recipe.MaxTurnsPerEncounter,
                suppressDebugLogs: true,
                rewardPolicy: _rewardPolicy,
                entryExperienceGrant: scenario.entryExperienceGrant);
            if (!metrics.Win)
            {
                CloseFailedCombat(dungeon, combat, launch, metrics.Termination);
                throw new BalanceCheckpointCombatOutcomeException(
                    _recipe.Seed, dungeon.CurrentFloorContentId,
                    step.NodeContentId, metrics.Termination, metrics.EndingHP);
            }
            if (!dungeon.TryCompleteVictory(
                    combat,
                    out _,
                    out string victoryError))
            {
                throw new BalanceCheckpointException(
                    $"Cannot commit checkpoint victory {step.NodeContentId}: " +
                    victoryError);
            }

            _transactions.ResolvePendingVictorySeal(dungeon, scenario, step);
            if (!dungeon.CurrentFloor.TryAcknowledgeReturn(step.NodeContentId))
            {
                throw new BalanceCheckpointException(
                    "Cannot acknowledge checkpoint combat return from " +
                    step.NodeContentId + ".");
            }
        }

        private static void CloseFailedCombat(
            DungeonRunSession dungeon,
            CombatController combat,
            LevelEncounterLaunch launch,
            SimulationTerminationReason termination)
        {
            // Keep the actual terminal HP/deck/XP. Closing only the floor node
            // would leave the Dungeon snapshot at the previous victory.
            if (!dungeon.TryCompleteDefeat(
                    combat,
                    out _,
                    out string error))
            {
                throw new BalanceCheckpointException(
                    $"Cannot close failed checkpoint combat " +
                    $"{launch.NodeContentId}: {error}");
            }
            // A timeout is a censored simulation, not a production defeat.
            if (termination == SimulationTerminationReason.Defeat &&
                !dungeon.TryConcludeDefeat(out _, out error))
                throw new BalanceCheckpointException(
                    "Cannot conclude checkpoint defeat: " + error);
            if (!dungeon.CurrentFloor.TryAcknowledgeReturn(launch.NodeContentId))
                throw new BalanceCheckpointException(
                    "Cannot acknowledge failed checkpoint combat: " + launch.NodeContentId);
        }
    }
}
