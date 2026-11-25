using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Chooses one currently playable card for the player's explicit play-all
    /// intent. Reads public rules and already prepared previews only; it neither
    /// plays cards, prepares random choices nor searches hypothetical battles.
    /// The execution session owns cancellation, pacing and repeated-use limits.
    /// Equal candidates retain their real hand order, independent of UI sorting.
    /// </summary>
    public static class CombatAutoPlayPolicy
    {
        public static CardConfig SelectNext(CombatController combat,
            ISet<CardConfig> excluded = null)
        {
            if (combat == null || !combat.State.CanPlayCards || combat.IsCombatOver())
                return null;

            Candidate best = default;
            bool found = false;
            foreach (CardConfig card in combat.Hand)
            {
                if (card == null || excluded?.Contains(card) == true) continue;
                ResolvedCardCost cost = card.ResolveCost();
                if (!combat.Mana.PreviewPayment(cost.PaymentCost, out ManaPaymentReceipt payment))
                    continue;

                var candidate = Evaluate(combat, card, cost, payment);
                // Strict comparison deliberately preserves the original order.
                if (!found || IsBetter(candidate, best))
                {
                    best = candidate;
                    found = true;
                }
            }
            return found ? best.Card : null;
        }

        private static Candidate Evaluate(CombatController combat, CardConfig card,
            ResolvedCardCost cost, ManaPaymentReceipt payment)
        {
            ComboPreview combo = combat.GetComboPreview(card);
            bool numeric = !cost.IsWild && !cost.IsMild && !cost.IsComboBreaker;
            int chainPriority = 0;
            if (combat.Combo.Enabled)
            {
                // Save Wild for a missing numeric continuation. Clearing cards
                // cash out after an ordinary continuation or a useful bridge.
                chainPriority = numeric && combo.WouldCombo && !combo.WouldClearCombo ? 4
                    : cost.IsWild ? 3
                    : combo.WouldCombo && combo.WouldClearCombo ? 2
                    : numeric ? 1 : 0;
            }

            CardImmediateDamageDisplaySummary damage =
                combat.GetPreparedCardImmediateDamageDisplay(card);
            int guaranteedDefeats = 0;
            if (!damage.IsEstimate)
                foreach (EnemyDamagePreview row in damage.DamagePreviews)
                    if (!row.IsEstimate && row.WillKill) guaranteedDefeats++;

            bool startsNumericChain = combat.Combo.Enabled && numeric &&
                (!combat.IsComboActive || combat.Combo.LastComboValue == null || combo.WouldBreak);
            return new Candidate
            {
                Card = card,
                GuaranteedDefeats = guaranteedDefeats,
                ChainPriority = chainPriority,
                StartsNumericChain = startsNumericChain,
                ComboValue = cost.ComboValue,
                SetupPriority = GetSetupPriority(combat, card, cost, payment,
                    combo.ResultingMultiplier),
                Wuxing = combat.WouldTriggerWuxing(card),
                CrawlerTriggers = combat.GetCrawlerTriggerPreviews(card).Count,
                Damage = damage.TotalDamage,
                PaymentCost = cost.PaymentCost
            };
        }

        private static int GetSetupPriority(CombatController combat, CardConfig card,
            ResolvedCardCost cost, ManaPaymentReceipt payment, int multiplier)
        {
            // A negative payment is already a real capped credit. A free card's
            // numeric combo value must never manufacture a mana credit.
            if (payment.CreditedAmount > 0) return 2;
            bool hasBuff = false;
            long temporaryAfterPayment = (long)combat.Mana.TemporaryMana -
                Math.Min(combat.Mana.TemporaryMana, Math.Max(0, cost.PaymentCost));
            CardSupportEffectPreview support =
                CardSupportEffectProjection.Project(card, multiplier, combat.Mana);
            foreach (CardSupportEffectEntry effect in support.Effects)
            {
                // Unresolved author conditions are not guaranteed setup value.
                if (effect.ConditionsSatisfied != true) continue;
                if (effect.EffectType == EffectType.GainMana && effect.ScaledAmount > 0 &&
                    temporaryAfterPayment < int.MaxValue)
                    return 2;
                if (effect.EffectType == EffectType.DrawCards && effect.ScaledCount > 0 &&
                    (combat.DeckCount > 0 || combat.DiscardCount > 0 ||
                     !card.MustDestroyOnUse && !combat.State.Deck.WillReturnOnPlay(card)))
                    return 2;
                if (effect.EffectType == EffectType.Heal && effect.ScaledAmount > 0 &&
                    combat.Player.currentHealth < combat.Player.maxHealth)
                    return 1;
                if ((effect.EffectType == EffectType.ModifyStat ||
                     effect.EffectType == EffectType.ExtendCrawlerDuration ||
                     effect.EffectType == EffectType.IncreaseCrawlerDuration) &&
                    effect.ScaledAmount > 0)
                    hasBuff = true;
            }
            if (hasBuff)
                foreach (BuffPreview buff in combat.GetBuffPreview(card))
                    if (buff.delta > 0 && buff.statType != StatType.ZhaoSha)
                        return 1;
            return 0;
        }

        private static bool IsBetter(Candidate candidate, Candidate current)
        {
            if (candidate.GuaranteedDefeats != current.GuaranteedDefeats)
                return candidate.GuaranteedDefeats > current.GuaranteedDefeats;
            if (candidate.ChainPriority != current.ChainPriority)
                return candidate.ChainPriority > current.ChainPriority;
            // With no usable continuation, start low rather than spending the
            // high numeric setup card before the low numeric attack card.
            if (candidate.StartsNumericChain && current.StartsNumericChain &&
                candidate.ComboValue != current.ComboValue)
                return candidate.ComboValue < current.ComboValue;
            if (candidate.SetupPriority != current.SetupPriority)
                return candidate.SetupPriority > current.SetupPriority;
            if (candidate.Wuxing != current.Wuxing) return candidate.Wuxing;
            if (candidate.CrawlerTriggers != current.CrawlerTriggers)
                return candidate.CrawlerTriggers > current.CrawlerTriggers;
            if (candidate.Damage != current.Damage) return candidate.Damage > current.Damage;
            if (candidate.PaymentCost != current.PaymentCost)
                return candidate.PaymentCost < current.PaymentCost;
            return false;
        }

        private struct Candidate
        {
            public CardConfig Card;
            public int GuaranteedDefeats, ChainPriority, ComboValue, SetupPriority;
            public int CrawlerTriggers, Damage, PaymentCost;
            public bool StartsNumericChain, Wuxing;
        }
    }
}
