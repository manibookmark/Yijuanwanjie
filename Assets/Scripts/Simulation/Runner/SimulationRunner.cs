using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// 单场模拟驱动器。构造真实 CombatController，由 Agent 驱动出牌，
    /// 不创建任何 GameObject/UI/Audio/Effects。
    /// 绝对禁止重写战斗逻辑（规范 §3）。
    /// </summary>
    public static class SimulationRunner
    {
        private const int MaxCommandsPerCombat = 1000;
        private const int FirstSlotSmokeRewardIndex = 0;

        /// <summary>运行单场模拟，返回结构化指标。</summary>
        public static EncounterMetrics Run(
            EncounterScenarioSpec spec,
            ISimulationAgent agent,
            IRandomSource random,
            bool suppressDebugLogs = true,
            ISimulationRewardPolicy rewardPolicy = null)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            // 构造真实 CombatController（复用正式 Core）
            var combat = new CombatController(
                spec.Player,
                new List<CardConfig>(spec.Deck),
                new List<EnemyStats>(spec.Enemies),
                null,                              // bossTimer
                spec.UpgradePool != null
                    ? new List<CardConfig>(spec.UpgradePool)
                    : null,
                spec.Rules,
                new RunProgressionState(),
                new EconomySystem(),
                spec.EnemyWaveSizes,
                null,
                null,
                spec.EnemyFormationRows,
                spec.EnemyFormationPattern,
                null,                              // forcedOpeningHand
                ContentRuntimeFactory.CreateCrawlerByContentId,
                null,                              // levelUpRewardConfig
                new SeededInstanceIdGenerator(1));

            combat.SetRandomSource(random);
            return Run(
                combat,
                agent,
                spec.MaxTurns,
                suppressDebugLogs,
                rewardPolicy,
                spec.EntryExperienceGrant);
        }

        /// <summary>
        /// 驱动一个已经由正式流程组装好的 CombatController。
        /// 调用方继续拥有控制器及其跨遭遇状态，Runner 只执行行动循环并采集指标。
        /// </summary>
        public static EncounterMetrics Run(
            CombatController combat,
            ISimulationAgent agent,
            int maxTurns = 100,
            bool suppressDebugLogs = true,
            ISimulationRewardPolicy rewardPolicy = null,
            int entryExperienceGrant = 0,
            CombatPlanningOptions planning = null)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (maxTurns <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTurns));
            if (entryExperienceGrant < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(entryExperienceGrant));

            // 在模拟模式下抑制 DamageTrace 日志，避免 7MB+/场 的输出
            bool wasDeveloperBuild = UnityEngine.Debug.unityLogger.logEnabled;
            if (suppressDebugLogs)
                UnityEngine.Debug.unityLogger.logEnabled = false;

            try
            {
                if (planning != null)
                    return PlanningCombatRunner.Run(combat, maxTurns,
                        rewardPolicy, entryExperienceGrant, planning);
                return RunInternal(
                    combat,
                    agent,
                    maxTurns,
                    rewardPolicy,
                    entryExperienceGrant);
            }
            finally
            {
                if (suppressDebugLogs)
                    UnityEngine.Debug.unityLogger.logEnabled = wasDeveloperBuild;
            }
        }

        private static EncounterMetrics RunInternal(
            CombatController combat,
            ISimulationAgent agent,
            int maxTurns,
            ISimulationRewardPolicy rewardPolicy,
            int entryExperienceGrant)
        {

            var metrics = new EncounterMetrics
            {
                StartingHP = combat.Player.currentHealth,
            };
            // The caller owns combat, so all metric hooks must detach on
            // success, turn limits and exceptions before it can be reused.
            using IDisposable capture = metrics.Capture(combat);
            IReadOnlyList<LevelUpSystem.Reward> rewardCandidates = null;
            Action<List<LevelUpSystem.Reward>> captureRewards = rewards =>
                rewardCandidates = rewards;
            combat.OnLevelUpRewardsGenerated += captureRewards;

            try
            {

                // 运行战斗
                combat.StartTurn();
                if (entryExperienceGrant > 0)
                    combat.GainXP(entryExperienceGrant);
                int commands = 0;

                // CombatState.IsCombatOver() also becomes true as soon as every
                // enemy is dead.  That is an encounter-progress signal, not the
                // formal terminal phase: a kill may have just opened one or more
                // level-up rewards.  Keep driving Core until it publishes
                // BattleVictory/BattleDefeat so rewards cannot be skipped.
                while (!combat.State.IsBattleOver && commands < MaxCommandsPerCombat)
                {
                    if (TrySelectPendingReward(
                            combat,
                            rewardPolicy,
                            metrics,
                            ref rewardCandidates))
                    {
                        commands++;
                        continue;
                    }

                    // 玩家回合：Agent 逐张选牌，出牌后重新规划（抽牌/法力会改变状态）
                    if (combat.State.CanPlayCards && combat.HandCount > 0)
                    {
                        // 最多在一个玩家回合内连出，防爆（规范 §11）
                        int playsThisTurn = 0;
                        while (combat.State.CanPlayCards &&
                               combat.HandCount > 0 &&
                               !combat.State.IsBattleOver &&
                               playsThisTurn < 100)
                        {
                            var plays = agent.ChoosePlays(combat);
                            if (plays == null || plays.Count == 0)
                                break;

                            foreach (var card in plays)
                            {
                                if (!combat.State.CanPlayCards)
                                    break;
                                CommandResult result = combat.ExecuteCommand(
                                    new PlayCardCommand { Card = card });
                                playsThisTurn++;
                                commands++;
                                if (!result.Success)
                                {
                                    throw new InvalidOperationException(
                                        $"Agent {agent.Name} returned an illegal " +
                                        $"card action: {result.Error} " +
                                        $"{result.Message}");
                                }
                                if (combat.State.IsBattleOver) break;
                            }
                        }
                    }

                    // 出牌结算可能刚生成升级候选；必须先选完奖励，
                    // 再判断战斗结束或尝试结束回合。
                    if (TrySelectPendingReward(
                            combat,
                            rewardPolicy,
                            metrics,
                            ref rewardCandidates))
                    {
                        commands++;
                        continue;
                    }

                    if (combat.State.IsBattleOver) break;

                    // 结束回合 → 敌人回合 → 下一玩家回合
                    if (combat.State.CanEndTurn)
                    {
                        CommandResult result = combat.ExecuteCommand(
                            new EndTurnCommand());
                        commands++;
                        if (!result.Success)
                        {
                            throw new InvalidOperationException(
                                $"Core rejected a legal end-turn action: " +
                                $"{result.Error} {result.Message}");
                        }
                    }
                    else
                    {
                        // 无法结束回合也无法出牌 — 安全退出
                        commands++;
                        break;
                    }

                    // 回合数统计
                    metrics.Turns = combat.State.CurrentTurn;
                    if (!combat.State.IsBattleOver &&
                        metrics.Termination == default &&
                        metrics.Turns >= maxTurns)
                    {
                        metrics.Termination = SimulationTerminationReason.MaxTurns;
                        break;
                    }
                }

                // 收尾
                if (!combat.State.IsBattleOver && metrics.Termination == default)
                {
                    metrics.Termination = SimulationTerminationReason.CommandLimit;
                }
                else if (combat.State.IsBattleOver && metrics.Termination == default)
                {
                    // 战斗已结束但 OnCombatEnded 未触发（如构造后即结束）
                    metrics.Win = combat.State.CurrentPhase == BattlePhase.BattleVictory;
                    metrics.Termination = metrics.Win
                        ? SimulationTerminationReason.Victory
                        : SimulationTerminationReason.Defeat;
                }

                metrics.EndingHP = combat.Player.currentHealth;
                metrics.HpLost = metrics.StartingHP - metrics.EndingHP;
                metrics.Turns = combat.State.CurrentTurn;

                // 追踪最大连击
                metrics.MaxCombo = Math.Max(metrics.MaxCombo, combat.ComboCount);

                return metrics;
            }
            finally
            {
                combat.OnLevelUpRewardsGenerated -= captureRewards;
            }
        }

        private static bool TrySelectPendingReward(
            CombatController combat,
            ISimulationRewardPolicy rewardPolicy,
            EncounterMetrics metrics,
            ref IReadOnlyList<LevelUpSystem.Reward> rewardCandidates)
        {
            if (!combat.State.AwaitingRewardSelection)
                return false;

            // Snapshot the batch being selected. ExecuteCommand may synchronously
            // publish the next level-up batch, so clear the shared slot before
            // entering Core and let that event populate it for the next loop.
            IReadOnlyList<LevelUpSystem.Reward> selectedCandidates =
                rewardCandidates;

            // 未传策略时明确保留旧的 FirstSlotSmoke 行为。
            int rewardIndex = FirstSlotSmokeRewardIndex;
            if (rewardPolicy != null)
            {
                if (selectedCandidates == null ||
                    selectedCandidates.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"奖励策略 {rewardPolicy.Id}@{rewardPolicy.Revision} " +
                        "无法读取当前奖励候选；禁止静默退回首槽策略。");
                }
                rewardIndex = rewardPolicy.ChooseReward(
                    combat,
                    selectedCandidates);
                if (rewardIndex < 0 ||
                    rewardIndex >= selectedCandidates.Count)
                {
                    throw new InvalidOperationException(
                        $"奖励策略 {rewardPolicy.Id}@{rewardPolicy.Revision} " +
                        $"返回非法索引 {rewardIndex}，候选数为 " +
                        $"{selectedCandidates.Count}。");
                }
            }

            rewardCandidates = null;
            CommandResult result = combat.ExecuteCommand(
                new SelectRewardCommand(rewardIndex));
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Core rejected reward index {rewardIndex}: " +
                    $"{result.Error} {result.Message}");
            }
            if (selectedCandidates != null)
            {
                metrics.RecordRewardSelection(
                    selectedCandidates,
                    rewardIndex);
            }
            return true;
        }
    }
}
