using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum CardSupportEffectSource { TypedHost, LegacyHost, PostPlaySeal }

    /// <summary>Detached condition values. Payment conditions use stable Kind identifiers.</summary>
    public sealed class CardSupportCondition
    {
        internal CardSupportCondition(string kind, int intValue = 0,
            float floatValue = 0f, string stringValue = null)
        {
            Kind = kind; IntValue = intValue; FloatValue = floatValue;
            StringValue = stringValue ?? string.Empty;
        }
        public string Kind { get; }
        public int IntValue { get; }
        public float FloatValue { get; }
        public string StringValue { get; }
    }

    /// <summary>
    /// One planned support effect. Scaled values are requests if its conditions
    /// hold, before healing, stat caps, deck limits and other settlement rules.
    /// Null ConditionsSatisfied means the prerequisite has not been evaluated.
    /// </summary>
    public sealed class CardSupportEffectEntry
    {
        internal CardSupportEffectEntry(CardEffectSpec effect, int multiplier,
            CardSupportEffectSource source, int sourceIndex,
            IReadOnlyList<CardSupportCondition> conditions = null,
            bool? conditionsSatisfied = null, int paidManaMultiplier = 0)
        {
            EffectType = effect.effectType; StatType = effect.statType;
            Target = effect.target; Source = source; SourceIndex = sourceIndex;
            BaseAmount = effect.amount; BaseCount = effect.count;
            ComboScalable = effect.comboScalable;
            ScaledAmount = ComboScalable ? effect.amount * multiplier : effect.amount;
            ScaledCount = ComboScalable ? effect.count * multiplier : effect.count;
            PaidManaMultiplier = paidManaMultiplier;
            var copied = new List<CardSupportCondition>();
            if (conditions != null) copied.AddRange(conditions);
            else if (effect.conditions != null)
                foreach (ConditionRule condition in effect.conditions)
                    if (condition != null && condition.type != ConditionTypeId.None)
                        copied.Add(new CardSupportCondition(condition.type.ToString(),
                            condition.intValue, condition.floatValue, condition.stringValue));
            Conditions = copied.AsReadOnly();
            ConditionsSatisfied = copied.Count == 0 ? true : conditionsSatisfied;
        }
        public EffectType EffectType { get; }
        public StatType StatType { get; }
        public TargetRule Target { get; }
        public CardSupportEffectSource Source { get; }
        public int SourceIndex { get; }
        public int BaseAmount { get; }
        public int BaseCount { get; }
        public int ScaledAmount { get; }
        public int ScaledCount { get; }
        public bool ComboScalable { get; }
        public int PaidManaMultiplier { get; }
        public bool? ConditionsSatisfied { get; }
        public IReadOnlyList<CardSupportCondition> Conditions { get; }
    }

    /// <summary>
    /// Host resource amounts exclude conditional effects, post-play seals and
    /// color/crawler reactions. Effects preserves those conditional requests
    /// separately. All collections and values are frozen for this observation.
    /// </summary>
    public sealed class CardSupportEffectPreview
    {
        internal CardSupportEffectPreview(int multiplier, List<CardSupportEffectEntry> effects)
        {
            ResultingMultiplier = multiplier;
            Effects = effects.AsReadOnly();
            foreach (CardSupportEffectEntry effect in effects)
            {
                if (effect.Source == CardSupportEffectSource.PostPlaySeal || effect.Conditions.Count > 0)
                    continue;
                switch (effect.EffectType)
                {
                    case EffectType.GainArmor:
                        BaseArmor += effect.BaseAmount; ScaledArmor += effect.ScaledAmount; break;
                    case EffectType.Heal:
                        BaseHeal += effect.BaseAmount; ScaledHeal += effect.ScaledAmount; break;
                    case EffectType.GainMana:
                        BaseManaGain += effect.BaseAmount; ScaledManaGain += effect.ScaledAmount; break;
                }
            }
        }
        public int ResultingMultiplier { get; }
        public int BaseArmor { get; }
        public int BaseHeal { get; }
        public int BaseManaGain { get; }
        public int ScaledArmor { get; }
        public int ScaledHeal { get; }
        public int ScaledManaGain { get; }
        public string Coverage => "HostUnconditionalRequestedAmounts";
        public string Limitations => "Scaled values are requested amounts, before healing modifiers, health/stat caps, " +
            "deck limits and settlement. Host totals exclude conditional effects, seals, color reactions and crawlers.";
        public IReadOnlyList<CardSupportEffectEntry> Effects { get; }
    }

    /// <summary>
    /// Owns typed-first support observation and scaling knowledge. It never
    /// mutates a card, pays mana, evaluates random conditions or predicts draws.
    /// Core remains the only owner of actual settlement and payment rules.
    /// </summary>
    public static class CardSupportEffectProjection
    {
        public static CardSupportEffectPreview Project(CardConfig card,
            int resultingMultiplier = 1, ManaSystem mana = null)
        {
            int multiplier = Math.Max(1, resultingMultiplier);
            var entries = new List<CardSupportEffectEntry>();
            if (card == null) return new CardSupportEffectPreview(multiplier, entries);
            IReadOnlyList<CardEffectSpec> host = card.HasSpecEffects
                ? card.effects : CardConfigAdapter.GenerateSpecs(card);
            host ??= Array.Empty<CardEffectSpec>();
            for (int i = 0; i < host.Count; i++)
                if (IsSupport(host[i]))
                    entries.Add(new CardSupportEffectEntry(host[i], multiplier,
                        card.HasSpecEffects ? CardSupportEffectSource.TypedHost : CardSupportEffectSource.LegacyHost, i));

            ManaPaymentReceipt payment = default;
            bool paymentKnown = mana != null && mana.PreviewPayment(card.ResolveCost().PaymentCost, out payment);
            if (card.seals != null)
                for (int i = 0; i < card.seals.Count; i++)
                {
                    CardSealSpec seal = card.seals[i];
                    if (CardSealRules.TryGetManaRefundRule(seal, out CardManaRefundRule refund))
                    {
                        var conditions = new List<CardSupportCondition>();
                        if (refund.RequiresPositivePayment)
                            conditions.Add(new CardSupportCondition("PositiveManaPayment"));
                        if (refund.RequiresExhaustedBalance)
                            conditions.Add(new CardSupportCondition("ExhaustedAvailableManaAfterPayment"));
                        var effect = CardEffectSpec.GainMana(paymentKnown ? refund.GetAmount(payment) : 0);
                        effect.comboScalable = false;
                        entries.Add(new CardSupportEffectEntry(effect, multiplier,
                            CardSupportEffectSource.PostPlaySeal, i, conditions,
                            paymentKnown ? (bool?)refund.IsSatisfied(payment) : null, refund.PaidManaMultiplier));
                        continue;
                    }
                    foreach (CardEffectSpec effect in CardSealRules.GetPostPlayEffects(new[] { seal }, payment))
                        if (IsSupport(effect))
                            entries.Add(new CardSupportEffectEntry(effect, multiplier,
                                CardSupportEffectSource.PostPlaySeal, i));
                }
            return new CardSupportEffectPreview(multiplier, entries);
        }

        private static bool IsSupport(CardEffectSpec effect) => effect != null &&
            effect.effectType != EffectType.None && effect.effectType != EffectType.Damage &&
            effect.effectType != EffectType.Bounce && effect.effectType != EffectType.Erase;
    }
}
