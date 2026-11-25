using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// 模拟玩家 AI。只负责"这一回合按什么顺序出哪些牌"。
    /// 不重写战斗规则，通过 ExecuteCommand 驱动真实 Core。
    /// </summary>
    public interface ISimulationAgent
    {
        string Name { get; }

        /// <summary>
        /// 在当前玩家回合选择要出的牌序列。
        /// Runner 会依次 ExecuteCommand(PlayCardCommand)。
        /// 返回空列表表示结束回合。
        /// </summary>
        IReadOnlyList<CardConfig> ChoosePlays(CombatController combat);
    }
}
