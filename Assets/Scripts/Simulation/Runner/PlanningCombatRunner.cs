using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Simulation.Protocol;

namespace WanjieScroll.Simulation
{
    /// <summary>Drives one planned encounter and collects only actual Core events.</summary>
    internal static class PlanningCombatRunner
    {
        internal static EncounterMetrics Run(CombatController combat, int maxTurns,
            ISimulationRewardPolicy rewardPolicy, int entryExperience, CombatPlanningOptions options)
        {
            var metrics = new EncounterMetrics { StartingHP = combat.Player.currentHealth };
            IReadOnlyList<LevelUpSystem.Reward> rewards = null;
            Action<List<LevelUpSystem.Reward>> captureRewards = values => rewards = values;
            Action<CombatCrawlerTriggeredEvent> crawler = _ => options.Statistics.CrawlerTriggers++;
            // Constructor validation may fail. Attach metric subscriptions only
            // after the session exists so every subscription reaches finally.
            using var session = new CombatPlanningSession(combat, options);
            using IDisposable capture = metrics.Capture(combat);
            combat.OnLevelUpRewardsGenerated += captureRewards;
            combat.OnCrawlerTriggered += crawler;
            try
            {
                session.Start(entryExperience);
                var planner = new PlanningCombatAgent(options);
                // IsCombatOver() reports enemy exhaustion before Core has
                // finished level-up rewards.  The runner must wait for the
                // formal BattleVictory/BattleDefeat phase instead.
                for (int commands = 0; commands < 1000 && !combat.State.IsBattleOver; commands++)
                {
                    BattleObservation observation = session.Observe();
                    if (observation.AwaitingRewardSelection)
                    {
                        IReadOnlyList<LevelUpSystem.Reward> candidates = rewards;
                        if (rewardPolicy == null || candidates == null || candidates.Count == 0)
                            throw new InvalidOperationException("Complete Run planning requires an explicit build reward policy.");
                        int index = rewardPolicy.ChooseReward(combat, candidates);
                        if (index < 0 || index >= candidates.Count || candidates[index] == null)
                            throw new InvalidOperationException("Build policy returned an invalid reward.");
                        rewards = null;
                        session.Step(LegalAction.SelectReward(index), $"{rewardPolicy.Id}@{rewardPolicy.Revision}");
                        if (combat.HasPendingLevelUpSealAssignment)
                        {
                            // D first policy: lowest legal runtime ID, independent
                            // of zone/list order and without consuming game RNG.
                            // Both requests go through Step so branches and exported
                            // traces reproduce the exact host, not a guessed effect.
                            LegalAction host = null;
                            foreach (LegalAction hostAction in session.GetLegalActions())
                                if (hostAction.Type == LegalActionType.AssignRewardSeal &&
                                    (host == null || hostAction.CardRuntimeInstanceId < host.CardRuntimeInstanceId))
                                    host = hostAction;
                            if (host == null)
                                throw new InvalidOperationException("Selected seal has no legal permanent host.");
                            session.Step(host, "seal-host.lowest-runtime-id@1");
                        }
                        metrics.RecordRewardSelection(candidates, index);
                        continue;
                    }
                    if (observation.Turn > maxTurns)
                    {
                        metrics.Termination = SimulationTerminationReason.MaxTurns;
                        break;
                    }
                    LegalAction action = planner.ChooseAction(session, out string explanation);
                    session.Step(action, explanation);
                }
                if (!combat.State.IsBattleOver && metrics.Termination == default)
                    metrics.Termination = SimulationTerminationReason.CommandLimit;
                else if (combat.State.IsBattleOver)
                {
                    metrics.Win = combat.State.CurrentPhase == BattlePhase.BattleVictory;
                    metrics.Termination = metrics.Win ? SimulationTerminationReason.Victory : SimulationTerminationReason.Defeat;
                }
                metrics.EndingHP = combat.Player.currentHealth;
                metrics.HpLost = metrics.StartingHP - metrics.EndingHP;
                metrics.Turns = combat.State.CurrentTurn;
                return metrics;
            }
            finally
            {
                options.Trace = session.ExportTrace();
                combat.OnLevelUpRewardsGenerated -= captureRewards;
                combat.OnCrawlerTriggered -= crawler;
            }
        }
    }
}
