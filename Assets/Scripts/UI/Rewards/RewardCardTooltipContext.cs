using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Cards;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>奖励卡牌复用统一的关键词说明，不附加牌组分析。</summary>
    public sealed class RewardCardTooltipContext
    {
        private RewardCardTooltipContext() { }

        public static RewardCardTooltipContext FromCombat(
            CombatState state, CombatRuleSet rules = null)
        {
            return state == null ? null : new RewardCardTooltipContext();
        }

        public static RewardCardTooltipContext FromRun(
            DungeonRunSession run, IReadOnlyList<CardConfig> cardPool,
            CombatRuleSet rules = null)
        {
            return run == null ? null : new RewardCardTooltipContext();
        }

        public string GetText(CardConfig card, bool includeAnalysis = true)
        {
            return CardTextRenderer.GetTooltipText(card);
        }
    }
}
