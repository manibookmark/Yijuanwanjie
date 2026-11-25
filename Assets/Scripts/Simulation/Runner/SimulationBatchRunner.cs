using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// 批量模拟的汇总报告（规范 §14/§15/§16 精简版）。
    /// </summary>
    public sealed class SimulationBatchReport
    {
        public string ScenarioId;
        public string AgentName;
        public int Runs;
        public int Wins;
        public int Losses;
        public int MaxTurnsHits;

        // 分布
        public float WinRate => Runs > 0 ? (float)Wins / Runs : 0f;
        public float MedianTurns;
        public float P25Turns;
        public float P75Turns;
        public float MedianHpLost;
        public float P90HpLost;
        public float MedianMaxCombo;
        public float MedianCardsPlayed;
        public float MedianManaSpent;

        // 异常
        public int LoopDetected;

        public override string ToString()
        {
            return $"[{ScenarioId}] {AgentName}: {Runs} runs, " +
                   $"WinRate={WinRate:P1} ({Wins}/{Runs}), " +
                   $"Turns(med)={MedianTurns:F1}, " +
                   $"HpLost(med)={MedianHpLost:F1}, " +
                   $"HpLost(p90)={P90HpLost:F1}, " +
                   $"Combo(med)={MedianMaxCombo:F1}, " +
                   $"Cards(med)={MedianCardsPlayed:F1}, " +
                   $"MaxTurns={MaxTurnsHits}, Loops={LoopDetected}";
        }
    }

    /// <summary>
    /// 批量模拟驱动器。同一 scenario + agent，不同 seed 跑 N 场。
    /// 每场重新构建 spec（PlayerStats/EnemyStats 是可变对象，不能跨场复用）。
    /// </summary>
    public static class SimulationBatchRunner
    {
        /// <summary>从 BattleScenarioProfile 每场重新构建 spec，保证状态隔离。</summary>
        public static SimulationBatchReport RunBatch(
            BattleScenarioProfile profile,
            ISimulationAgent agent,
            int seedStart,
            int count,
            CombatRuleSet rules = null)
        {
            var metrics = new List<EncounterMetrics>(count);
            string scenarioId = profile.scenarioId;

            for (int i = 0; i < count; i++)
            {
                // 每场创建新的 spec（新的 PlayerStats/EnemyStats/CardConfig）
                var spec = ScenarioFactory.FromProfile(profile, rules);
                var random = new SeededRandomSource(seedStart + i);
                var m = SimulationRunner.Run(spec, agent, random);
                metrics.Add(m);
            }

            return Aggregate(scenarioId, agent.Name, metrics);
        }

        private static SimulationBatchReport Aggregate(
            string scenarioId,
            string agentName,
            List<EncounterMetrics> metrics)
        {
            var report = new SimulationBatchReport
            {
                ScenarioId = scenarioId,
                AgentName = agentName,
                Runs = metrics.Count,
            };

            var turns = new List<float>();
            var hpLost = new List<float>();
            var maxCombo = new List<float>();
            var cardsPlayed = new List<float>();
            var manaSpent = new List<float>();

            foreach (var m in metrics)
            {
                if (m.Win) report.Wins++;
                else report.Losses++;

                if (m.Termination == SimulationTerminationReason.MaxTurns ||
                    m.Termination == SimulationTerminationReason.CommandLimit)
                {
                    report.MaxTurnsHits++;
                    if (m.Termination == SimulationTerminationReason.CommandLimit)
                        report.LoopDetected++;
                }

                turns.Add(m.Turns);
                hpLost.Add(m.HpLost);
                maxCombo.Add(m.MaxCombo);
                cardsPlayed.Add(m.CardsPlayed);
                manaSpent.Add(m.ManaSpent);
            }

            turns.Sort();
            hpLost.Sort();
            maxCombo.Sort();
            cardsPlayed.Sort();
            manaSpent.Sort();

            report.MedianTurns = Percentile(turns, 0.50f);
            report.P25Turns = Percentile(turns, 0.25f);
            report.P75Turns = Percentile(turns, 0.75f);
            report.MedianHpLost = Percentile(hpLost, 0.50f);
            report.P90HpLost = Percentile(hpLost, 0.90f);
            report.MedianMaxCombo = Percentile(maxCombo, 0.50f);
            report.MedianCardsPlayed = Percentile(cardsPlayed, 0.50f);
            report.MedianManaSpent = Percentile(manaSpent, 0.50f);

            return report;
        }

        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return 0;
            int idx = (int)Math.Round(p * (sorted.Count - 1));
            return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
        }
    }
}
