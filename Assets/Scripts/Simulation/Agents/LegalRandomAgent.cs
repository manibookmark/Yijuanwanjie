using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Agents
{
    /// <summary>
    /// 从当前手牌中随机选一张可合法出的牌；用于 fuzz 测试和验证 Runner 基础设施。
    /// 不作为平衡基准（规范 §9.1）。
    /// </summary>
    public sealed class LegalRandomAgent : ISimulationAgent
    {
        public string Name => "LegalRandom";

        public IReadOnlyList<CardConfig> ChoosePlays(CombatController combat)
        {
            // 找出所有可出的牌（法力足够）
            var playable = new List<CardConfig>();
            int available = combat.Mana.CurrentMana + combat.Mana.TemporaryMana;

            foreach (var card in combat.Hand)
            {
                var cost = card.ResolveCost();
                if (cost.PaymentCost <= available)
                    playable.Add(card);
            }

            if (playable.Count == 0)
                return System.Array.Empty<CardConfig>();

            // 只出一张，交给 Runner 循环
            // 随机选一张
            IRandomSource random = combat.State.Random ??
                DefaultRandomSource.Instance;
            int index = random.Range(0, playable.Count);
            return new[] { playable[index] };
        }
    }
}
