using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>卡牌的衔接规则值快照，不持有可能被销毁的运行时卡牌。</summary>
    public readonly struct CardSequenceSignature
    {
        public readonly ResolvedCardCost Cost;
        public readonly CardColor Color;

        public CardSequenceSignature(CardConfig card)
        {
            Cost = card.ResolveCost();
            Color = card.color;
        }
    }

    public readonly struct CardSequenceCounts
    {
        public readonly int ComboBefore, ComboAfter, WuxingBefore, WuxingAfter;

        public CardSequenceCounts(int comboBefore, int comboAfter,
            int wuxingBefore, int wuxingAfter)
        {
            ComboBefore = comboBefore;
            ComboAfter = comboAfter;
            WuxingBefore = wuxingBefore;
            WuxingAfter = wuxingAfter;
        }
    }

    /// <summary>
    /// 查询两张牌连续打出的规则兼容性，不模拟支付、目标或效果产生的中间牌。
    /// 叠势使用隔离的 ComboSystem；相生复用战斗的公开五行关系查询，
    /// 阳牌替代与战斗实例预览保持一致，并由非 UI 一致性测试约束。
    /// </summary>
    public static class CardSequenceCompatibility
    {
        public static bool CanCombo(CardSequenceSignature before, CardSequenceSignature after)
        {
            // 这两种牌由战斗在结算后清链，不能把下一张起链误计作续链。
            if (before.Cost.IsMild || before.Cost.IsComboBreaker) return false;
            var combo = new ComboSystem();
            combo.TryPlayCard(before.Cost);
            return combo.PreviewPlayCard(after.Cost).WouldCombo;
        }

        public static bool CanGenerate(CardColor before, CardColor after)
        {
            if (before == CardColor.Yang)
                return CombatController.TryGetWuxingPredecessor(after, out _);
            if (after == CardColor.Yang)
                return CombatController.TryGetWuxingSuccessor(before, out _);
            return CombatController.IsWuxingGenerating(before, after);
        }

        /// <summary>
        /// 每个牌组实例分别计数；候选尚未获得，调用方不可将其额外加入 owned。
        /// 四项独立计算，不要求费用和颜色同时匹配。
        /// </summary>
        public static CardSequenceCounts Count(CardSequenceSignature candidate,
            IReadOnlyList<CardSequenceSignature> owned, bool comboEnabled, bool wuxingEnabled)
        {
            int comboBefore = 0, comboAfter = 0, wuxingBefore = 0, wuxingAfter = 0;
            if (owned != null)
                foreach (CardSequenceSignature card in owned)
                {
                    if (comboEnabled)
                    {
                        if (CanCombo(card, candidate)) comboBefore++;
                        if (CanCombo(candidate, card)) comboAfter++;
                    }
                    if (wuxingEnabled)
                    {
                        if (CanGenerate(card.Color, candidate.Color)) wuxingBefore++;
                        if (CanGenerate(candidate.Color, card.Color)) wuxingAfter++;
                    }
                }
            return new CardSequenceCounts(comboBefore, comboAfter, wuxingBefore, wuxingAfter);
        }
    }
}
