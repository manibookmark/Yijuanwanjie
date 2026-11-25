using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Detached nominal host attacks for one display context. MainDamage counts
    /// each ordinary sub-hit once, before target replication or extra damage.
    /// Plans preserve object/sub-hit identity; actual landed-hit counts are not
    /// object counts. IsEstimate concerns unknown host scaling, not target rolls.
    /// </summary>
    public sealed class CardAttackDisplaySummary
    {
        internal CardAttackDisplaySummary(List<AttackPlan> plans, bool isEstimate)
        {
            Plans = plans.AsReadOnly();
            IsEstimate = isEstimate;
            foreach (AttackPlan plan in plans)
                MainDamage = checked(MainDamage + plan.DisplayedCardDamage);
        }

        public int MainDamage { get; }
        public bool IsEstimate { get; }
        public IReadOnlyList<AttackPlan> Plans { get; }
    }

    /// <summary>
    /// Owns typed/legacy nominal host display and coverage knowledge. Builds all
    /// host Damage effects with the supplied frozen stats, without targets, RNG,
    /// payments, post-play attacks or future damage. Deferred stat gains cannot
    /// amplify a later host Damage. Unknown hand/crawler changes remain marked
    /// as estimates instead of guessing the next snapshot. No state is retained.
    /// </summary>
    public static class CardAttackDisplayProjection
    {
        public static CardAttackDisplaySummary Project(CardConfig card,
            CombatStatSnapshot stats, CombatRuleSet rules = null)
        {
            var plans = new List<AttackPlan>();
            bool estimate = false;
            if (card == null) return new CardAttackDisplaySummary(plans, estimate);

            if (!card.HasSpecEffects)
            {
                if (AttackPlanBuilder.TryBuildFromCard(card, rules, stats, out AttackPlan legacy))
                    plans.Add(legacy);
                return new CardAttackDisplaySummary(plans, estimate);
            }

            CardAttackSnapshot source = CardAttackSnapshot.Capture(card);
            bool unknownScaling = false;
            foreach (CardEffectSpec effect in card.effects)
            {
                if (effect == null) continue;
                if (AttackPlanBuilder.TryFromDamageEffect(card, effect, rules, out DamageSpec damage))
                {
                    plans.Add(AttackPlanBuilder.Build(damage, stats, source));
                    estimate |= unknownScaling ||
                        ConditionEvaluator.DescribeRequirements(effect.conditions).RequiresEvaluation;
                }
                switch (effect.effectType)
                {
                    case EffectType.DrawCards:
                    case EffectType.DiscardCards:
                    case EffectType.CreateCard:
                    case EffectType.SummonCrawler:
                        unknownScaling = true;
                        break;
                }
            }
            return new CardAttackDisplaySummary(plans, estimate);
        }
    }
}
