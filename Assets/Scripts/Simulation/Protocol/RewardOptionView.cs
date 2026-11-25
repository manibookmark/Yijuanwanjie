using System;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Protocol
{
    /// <summary>
    /// Detached reward information that lets an external agent make a real
    /// choice without receiving LevelUpSystem.Reward or CardConfig references.
    /// RewardIndex remains the only executable identity.
    /// </summary>
    public sealed class RewardOptionView
    {
        internal RewardOptionView(
            int rewardIndex,
            LevelUpSystem.Reward reward)
        {
            if (reward == null)
                throw new ArgumentNullException(nameof(reward));

            RewardIndex = rewardIndex;
            Type = reward.type;
            Name = reward.name ?? string.Empty;
            Description = reward.description ?? string.Empty;
            StatType = reward.statType;
            StatAmount = reward.statAmount;

            CardConfig card = reward.card;
            if (card == null) return;

            ResolvedCardCost cost = card.ResolveCost();
            CardContentId = card.contentId ?? string.Empty;
            CardName = card.cardName ?? string.Empty;
            CardDescription = card.description ?? string.Empty;
            CardDetailDescription = card.detailDescription ?? string.Empty;
            CardColor = card.color;
            CardCostType = card.costType;
            CardBaseManaValue = cost.BaseManaValue;
            CardComboValue = cost.ComboValue;
            CardPaymentCost = cost.PaymentCost;
            CardIsFreeToPlay = cost.IsFreeToPlay;
            CardBaseDamage = card.baseDamage;
            CardProjectileCount = card.projectileCount;
            CardDirectTargetsPerHit = card.directTargetsPerHit;
            CardArmor = card.armor;
            CardHeal = card.heal;
            CardManaGain = card.manaGain;
            CardDestroyOnUse = card.destroyOnUse;
        }

        public int RewardIndex { get; }
        public LevelUpSystem.RewardType Type { get; }
        public string Name { get; }
        public string Description { get; }
        public StatType StatType { get; }
        public int StatAmount { get; }
        public string CardContentId { get; } = string.Empty;
        public string CardName { get; } = string.Empty;
        public string CardDescription { get; } = string.Empty;
        public string CardDetailDescription { get; } = string.Empty;
        public CardColor CardColor { get; }
        public CostType CardCostType { get; }
        public int CardBaseManaValue { get; }
        public int CardComboValue { get; }
        public int CardPaymentCost { get; }
        public bool CardIsFreeToPlay { get; }
        public int CardBaseDamage { get; }
        public int CardProjectileCount { get; }
        public int CardDirectTargetsPerHit { get; }
        public int CardArmor { get; }
        public int CardHeal { get; }
        public int CardManaGain { get; }
        public bool CardDestroyOnUse { get; }
    }
}
