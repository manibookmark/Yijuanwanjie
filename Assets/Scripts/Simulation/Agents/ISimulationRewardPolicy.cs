using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// 模拟器的升级奖励选择策略。Core 继续拥有候选生成和奖励结算，
    /// 策略只从已发布的只读候选中返回一个合法索引。
    /// </summary>
    public interface ISimulationRewardPolicy
    {
        string Id { get; }
        int Revision { get; }

        int ChooseReward(
            CombatController combat,
            IReadOnlyList<LevelUpSystem.Reward> rewards);
    }
}
