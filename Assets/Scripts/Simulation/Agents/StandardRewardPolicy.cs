using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Agents
{
    /// <summary>
    /// 可复现的构筑奖励策略。根据当前生命、费用曲线、职能缺口和重复数
    /// 做连续评分；没有卡名白名单或必选分支，平分时保留候选原顺序。
    /// </summary>
    public sealed class StandardRewardPolicy : ISimulationRewardPolicy
    {
        public string Id => "standard";
        public int Revision => 2;

        public int ChooseReward(
            CombatController combat,
            IReadOnlyList<LevelUpSystem.Reward> rewards)
        {
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (rewards == null || rewards.Count == 0)
                throw new ArgumentException("奖励候选不能为空。", nameof(rewards));

            DeckProfile deck = DeckProfile.Capture(combat);
            int bestIndex = 0;
            float bestScore = Score(combat, deck, rewards[0]);
            for (int i = 1; i < rewards.Count; i++)
            {
                float score = Score(combat, deck, rewards[i]);
                if (score <= bestScore) continue;
                bestScore = score;
                bestIndex = i;
            }
            return bestIndex;
        }

        private static float Score(
            CombatController combat,
            DeckProfile deck,
            LevelUpSystem.Reward reward)
        {
            if (reward == null) return float.MinValue;
            if (reward.type == LevelUpSystem.RewardType.NewCard)
                return ScoreCard(combat, deck, reward.card);
            if (reward.type == LevelUpSystem.RewardType.StatBoost)
                return 900f + StandardCardHeuristics.StatGrowthValue(
                    reward.statType,
                    reward.statAmount) * 10f;
            return 0f;
        }

        private static float ScoreCard(
            CombatController combat,
            DeckProfile deck,
            CardConfig card)
        {
            if (card == null) return float.MinValue;

            StandardCardProfile profile = StandardCardHeuristics.Describe(card);
            float missingRatio = Math.Max(
                0f,
                1f - combat.Player.currentHealth /
                (float)Math.Max(1, combat.Player.maxHealth));
            int cost = card.ResolveCost().PaymentCost;
            int sameCount = deck.Count(card.contentId);
            int costCount = deck.CostCount(cost);

            float offense =
                StandardCardHeuristics.EstimateDamage(combat, card) * 5f;
            float sustain =
                profile.Armor * (10f + missingRatio * 24f) +
                profile.Healing * (12f + missingRatio * 30f);
            float utility = profile.Mana * 35f + profile.Draw * 40f +
                profile.Growth;

            float defenseScarcity = profile.Armor + profile.Healing > 0f
                ? Math.Max(0, deck.Total / 5 - deck.SustainCards) * 45f
                : 0f;
            float curve = 55f / (1f + costCount);
            if (deck.CostCount(cost - 1) > 0) curve += 12f;
            if (deck.CostCount(cost + 1) > 0) curve += 12f;

            float unaffordable = Math.Max(
                0,
                cost - combat.Mana.BaseMana) * 120f;
            float duplicate = Math.Min(240f, sameCount * 65f);
            float oneUseFactor = card.destroyOnUse ? 0.55f : 1f;

            return 1000f +
                (offense + sustain + utility + defenseScarcity + curve) *
                oneUseFactor -
                unaffordable - duplicate;
        }

        private sealed class DeckProfile
        {
            private readonly Dictionary<string, int> _contentCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<int, int> _costCounts =
                new Dictionary<int, int>();

            internal int Total { get; private set; }
            internal int SustainCards { get; private set; }

            internal static DeckProfile Capture(CombatController combat)
            {
                var result = new DeckProfile();
                result.Add(combat.Deck);
                result.Add(combat.Hand);
                result.Add(combat.Discard);
                return result;
            }

            internal int Count(string contentId)
            {
                if (string.IsNullOrWhiteSpace(contentId)) return 0;
                return _contentCounts.TryGetValue(contentId, out int count)
                    ? count
                    : 0;
            }

            internal int CostCount(int cost)
            {
                if (cost < 0) return 0;
                return _costCounts.TryGetValue(cost, out int count)
                    ? count
                    : 0;
            }

            private void Add(IReadOnlyList<CardConfig> cards)
            {
                if (cards == null) return;
                for (int i = 0; i < cards.Count; i++)
                {
                    CardConfig card = cards[i];
                    if (card == null) continue;
                    Total++;
                    string id = card.contentId ?? string.Empty;
                    _contentCounts[id] = Count(id) + 1;
                    int cost = card.ResolveCost().PaymentCost;
                    _costCounts[cost] = CostCount(cost) + 1;
                    StandardCardProfile profile =
                        StandardCardHeuristics.Describe(card);
                    if (profile.Armor + profile.Healing > 0f)
                        SustainCards++;
                }
            }
        }
    }

    /// <summary>
    /// 奖励策略对卡牌机制的统一视图；只解析类型化效果和兼容字段。
    /// </summary>
    internal static class StandardCardHeuristics
    {
        internal static StandardCardProfile Describe(CardConfig card)
        {
            if (card == null) return default;

            var result = new StandardCardProfile();
            if (!card.HasSpecEffects)
            {
                result.Armor = Math.Max(0, card.armor);
                result.Healing = Math.Max(0, card.heal);
                result.Mana = Math.Max(0, card.manaGain);
                return result;
            }

            for (int i = 0; i < card.effects.Count; i++)
            {
                CardEffectSpec effect = card.effects[i];
                if (effect == null) continue;
                float chance = effect.chance > 0f ? effect.chance : 1f;
                switch (effect.effectType)
                {
                    case EffectType.GainArmor:
                        result.Armor += Math.Max(0, effect.amount) * chance;
                        break;
                    case EffectType.Heal:
                        result.Healing += Math.Max(0, effect.amount) * chance;
                        break;
                    case EffectType.GainMana:
                        result.Mana += Math.Max(0, effect.amount) * chance;
                        break;
                    case EffectType.DrawCards:
                        result.Draw += Math.Max(0, effect.count) * chance;
                        break;
                    case EffectType.ModifyStat:
                        result.Growth += StatGrowthValue(
                            effect.statType,
                            effect.amount) * chance;
                        break;
                    case EffectType.IncreaseMaxHealth:
                        result.Growth += Math.Max(0, effect.amount) * 5f * chance;
                        break;
                }
            }
            return result;
        }

        internal static float EstimateDamage(
            CombatController combat,
            CardConfig card)
        {
            if (combat == null || card == null) return 0f;
            if (!AttackPlanBuilder.TryBuildFromCard(
                    card,
                    new CombatRuleSet(),
                    new CombatStatSnapshot(combat.Player, 0),
                    out AttackPlan plan))
            {
                return 0f;
            }

            float damage = 0f;
            foreach (AttackSubHit sub in plan.SubHits)
                damage += sub.NormalHitDamage;
            return damage;
        }

        internal static float StatGrowthValue(StatType stat, int amount)
        {
            int value = Math.Max(0, amount);
            float weight = stat switch
            {
                StatType.FaLi => 8f,
                StatType.ShouPai => 6f,
                StatType.ChouPai => 6f,
                StatType.Amount => 5f,
                StatType.FengRui => 4f,
                StatType.BengLie => 3f,
                StatType.HuJia => 2.5f,
                StatType.HunHun => 2f,
                StatType.MingWu => 1.5f,
                StatType.TianYun => 1f,
                StatType.NingShen => 1f,
                StatType.LianYu => 0.5f,
                StatType.ZhaoSha => -2f,
                _ => 0f
            };
            return value * weight;
        }
    }

    internal struct StandardCardProfile
    {
        internal float Armor;
        internal float Healing;
        internal float Mana;
        internal float Draw;
        internal float Growth;
    }
}
