using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// 单场 Encounter 模拟输入数据。完全纯 C#，无 MonoBehaviour/UI 依赖。
    /// 从 BattleScenarioProfile 或手工构建。
    /// </summary>
    public sealed class EncounterScenarioSpec
    {
        public string ScenarioId;
        public PlayerStats Player;
        public IReadOnlyList<CardConfig> Deck;
        public IReadOnlyList<CardConfig> UpgradePool;
        /// <summary>
        /// Every transient CardConfig materialized while building this spec,
        /// including content filtered out of Deck/UpgradePool. The headless
        /// encounter assembly owns and releases this set.
        /// </summary>
        public IReadOnlyList<CardConfig> OwnedCardPool;
        public IReadOnlyList<EnemyStats> Enemies;
        public IReadOnlyList<int> EnemyWaveSizes;
        public IReadOnlyList<int> EnemyFormationRows;
        public string EnemyFormationPattern;
        public int EntryExperienceGrant;
        public CombatRuleSet Rules;
        public int MaxTurns = 100;
    }
}
