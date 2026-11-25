using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Fixed post-play refund rule evaluated against the payment receipt.</summary>
    public readonly struct CardManaRefundRule
    {
        internal CardManaRefundRule(int paidManaMultiplier, bool requiresExhaustedBalance)
        {
            PaidManaMultiplier = paidManaMultiplier;
            RequiresExhaustedBalance = requiresExhaustedBalance;
        }

        public int PaidManaMultiplier { get; }
        public bool RequiresPositivePayment => true;
        public bool RequiresExhaustedBalance { get; }
        public bool IsSatisfied(ManaPaymentReceipt payment) => payment.PaidAmount > 0 &&
            (!RequiresExhaustedBalance || payment.ExhaustedAvailableMana);
        public int GetAmount(ManaPaymentReceipt payment) => IsSatisfied(payment)
            ? (int)Math.Min(int.MaxValue, (long)payment.PaidAmount * PaidManaMultiplier) : 0;
    }

    /// <summary>
        /// 灵印的卡牌局部规则边界：统一判定印位、宿主合法性、赋予结果与战斗效果。
    /// 调用方只提交卡牌和灵印，不需要了解具体枚举或叠乘顺序。
    /// </summary>
    public static class CardSealRules
    {
        public const int MaximumSealSlots = 3;

        /// <summary>State seals are qualifications, not counters or additive copies.</summary>
        public static bool HasEffect(IReadOnlyList<CardSealSpec> seals, CardSealEffect effect)
        {
            if (seals != null)
                for (int i = 0; i < seals.Count; i++)
                    if (seals[i]?.effect == effect && seals[i].value > 0) return true;
            return false;
        }

        /// <summary>
        /// One physical source contributes its installed Mug total, not one layer per hit.
        /// Freeze at the base attack boundary; subsequent hits replace, never accumulate.
        /// </summary>
        public static int GetMugBaseReward(IReadOnlyList<CardSealSpec> seals)
        {
            long total = 0;
            if (seals != null)
                for (int i = 0; i < seals.Count; i++)
                    if (seals[i]?.effect == CardSealEffect.Mug)
                        total = Math.Min(int.MaxValue, total + Math.Max(0, seals[i].value));
            return (int)total;
        }

        /// <summary>
        /// Queries the authoritative hand; never caches an aura or writes player stats.
        /// Preview excludes the card about to leave. Actual attacks query the live hand,
        /// so a draw between two host effects can enable/disable the later attack's aura.
        /// Leader percentages add to one multiplier, independent of global Might.
        /// </summary>
        public static float GetHeldAttackMultiplier(IReadOnlyList<CardConfig> hand,
            CardConfig departingCard = null)
        {
            long percent = 0;
            foreach (CardSealSpec seal in HeldSeals(hand, departingCard))
                if (seal.effect == CardSealEffect.Leader)
                    percent = Math.Min(int.MaxValue, percent + seal.value);
            return 1f + percent / 100f;
        }

        /// <summary>
        /// Freeze independent base payments at a successful real play's departure.
        /// Economy rounds each source separately; summing first would change greed income.
        /// Calling this query does not count a play or award currency.
        /// </summary>
        public static IReadOnlyList<int> GetHeldPlayJadeRewards(IReadOnlyList<CardConfig> hand,
            CardConfig playedCard)
        {
            var rewards = new List<int>();
            foreach (CardSealSpec seal in HeldSeals(hand, playedCard))
                if (seal.effect == CardSealEffect.Midas) rewards.Add(seal.value);
            return rewards;
        }

        private static IEnumerable<CardSealSpec> HeldSeals(IReadOnlyList<CardConfig> hand,
            CardConfig excluded)
        {
            if (hand == null) yield break;
            var seen = new HashSet<int>();
            for (int i = 0; i < hand.Count; i++)
            {
                CardConfig host = hand[i];
                if (host == null || host.runtimeInstanceId <= 0 || !seen.Add(host.runtimeInstanceId) ||
                    ReferenceEquals(host, excluded) || (excluded != null &&
                    excluded.runtimeInstanceId > 0 && host.runtimeInstanceId == excluded.runtimeInstanceId))
                    continue;
                if (host.seals == null) continue;
                for (int s = 0; s < host.seals.Count; s++)
                    if (host.seals[s] != null && host.seals[s].value > 0)
                        yield return host.seals[s];
            }
        }

        /// <summary>
        /// Ordered post-play instructions. Dynamic armor cannot be converted into a
        /// numeric CardEffectSpec before preceding slots execute, so it stays an
        /// explicit deferred action. Existing ordinary effects keep their one resolver.
        /// </summary>
        public static IReadOnlyList<CardSealPostPlayAction> GetPostPlayActions(
            IReadOnlyList<CardSealSpec> seals, ManaPaymentReceipt payment = default)
        {
            var actions = new List<CardSealPostPlayAction>();
            if (seals == null) return actions;
            for (int i = 0; i < seals.Count; i++)
            {
                CardSealSpec seal = seals[i];
                if (seal == null || seal.value <= 0) continue;
                if (seal.effect == CardSealEffect.ArmorStrike)
                    actions.Add(new CardSealPostPlayAction(null, spendCurrentArmor: true));
                else if (seal.effect == CardSealEffect.Copy)
                    actions.Add(new CardSealPostPlayAction(seal.value, i, seal.contentId));
                else
                    foreach (CardEffectSpec effect in GetPostPlayEffects(new[] { seal }, payment))
                        actions.Add(new CardSealPostPlayAction(effect));
            }
            return actions;
        }

        /// <summary>Only a real draw may use this budget to reset the physical card.</summary>
        public static int GetReturnUsesPerDraw(IReadOnlyList<CardSealSpec> seals)
        {
            long uses = 0;
            if (seals != null)
                for (int i = 0; i < seals.Count; i++)
                    if (seals[i]?.effect == CardSealEffect.Return)
                        uses = Math.Min(int.MaxValue, uses + Math.Max(0, seals[i].value));
            return (int)uses;
        }

        /// <summary>Qualification union, not one dispatch per matching color/seal.</summary>
        public static bool CanTriggerColor(CardConfig card, CardColor triggerColor)
        {
            if (card == null) return false;
            if (card.costType == CostType.Wild || card.color == triggerColor)
                return true;
            if (triggerColor != CardColor.Jin || card.seals == null)
                return false;
            for (int i = 0; i < card.seals.Count; i++)
                if (card.seals[i]?.effect == CardSealEffect.YellowTrigger &&
                    card.seals[i].value > 0)
                    return true;
            return false;
        }

        public static int GetSlotCapacity(CardConfig card)
            => card == null
                ? 0
                : GetSlotCapacity(card.gemSlots);

        public static int GetOccupiedSlotCount(CardConfig card)
            => card?.seals?.Count ?? 0;

        /// <summary>The same slot rule for a complete frozen author without native allocation.</summary>
        public static int GetSlotCapacity(int authoredSlotCount)
            => Math.Max(0, Math.Min(MaximumSealSlots, authoredSlotCount));

        public static bool HasEmptySlot(CardConfig card)
            => GetOccupiedSlotCount(card) < GetSlotCapacity(card);

        public static bool CanAssign(
            CardConfig card,
            CardSealSpec seal,
            CombatRuleSet rules = null,
            Func<string, bool> isEvolutionResultEligible = null)
        {
            if (card == null || card.runtimeInstanceId <= 0 || seal == null ||
                string.IsNullOrWhiteSpace(seal.contentId) || !HasEmptySlot(card))
                return false;

            if (seal.effect == CardSealEffect.Evolution)
            {
                CardConfig result = card.evolutionTarget;
                return result != null && result != card &&
                    !string.IsNullOrWhiteSpace(result.contentId) &&
                    !string.Equals(card.contentId, result.contentId,
                        StringComparison.OrdinalIgnoreCase) &&
                    (isEvolutionResultEligible == null ||
                     isEvolutionResultEligible(result.contentId));
            }

            if (seal.effect == CardSealEffect.Retain || seal.effect == CardSealEffect.ReverseCombo)
                return seal.value > 0 && !HasEffect(card.seals, seal.effect);

            // Installation follows the confirmed soft-choice policy. Zero benefit
            // is lawful; actual hit/mark eligibility is owned by combat execution.
            // Keep Mug's positive-value guard, but never infer host legality from
            // current displayed damage, native attacks or incidental rule procs.
            return seal.effect != CardSealEffect.Mug || seal.value > 0;
        }

        /// <summary>
        /// 构造赋予灵印后的独立持久状态快照，不修改宿主、目标定义或输入灵印。
        /// 化形不需要副卡：消耗宿主一个空印位，保持实体身份与已有灵印，突破清零。
        /// 内容发布门禁由调用方注入，与候选筛选使用相同策略。非法宿主返回 null。
        /// 返回对象由调用方拥有；实际牌组状态仍只能由其事务 owner 提交。
        /// </summary>
        public static CardConfig BuildAssignedCard(
            CardConfig card,
            CardSealSpec seal,
            CombatRuleSet rules = null,
            Func<string, bool> isEvolutionResultEligible = null)
        {
            if (!CanAssign(card, seal, rules, isEvolutionResultEligible))
                return null;

            bool evolves = seal.effect == CardSealEffect.Evolution;
            CardConfig result = CardInstanceFactory.RestoreReplacement(
                card, evolves ? card.evolutionTarget : card,
                evolves ? 0 : card.LimitBreakCount, card.seals);
            if (card.RuntimeInstance.IsSealCopy)
                result.MarkSealCopy();
            else if (card.IsEncounterTemporary)
                result.MarkEncounterTemporary();
            if (evolves)
                result.gemSlots = GetSlotCapacity(card) - 1;
            else
                result.seals.Add(seal.Clone());
            return result;
        }

        /// <summary>
        /// 把宿主基础效果后触发的灵印投影为标准效果，保留印位顺序。
        /// 数值固定且不受 Combo 放大；伤害乘印与既有 Run 属性印仍由原路径处理。
        /// 返回新规格，调用方不能通过修改结果回写卡牌实例上的灵印。
        /// </summary>
        public static IReadOnlyList<CardEffectSpec> GetPostPlayEffects(
            IReadOnlyList<CardSealSpec> seals,
            ManaPaymentReceipt payment = default)
        {
            var effects = new List<CardEffectSpec>();
            if (seals == null)
                return effects;

            for (int i = 0; i < seals.Count; i++)
            {
                CardSealSpec seal = seals[i];
                if (seal == null || seal.value <= 0)
                    continue;

                CardEffectSpec effect;
                switch (seal.effect)
                {
                    case CardSealEffect.Draw:
                        effect = CardEffectSpec.DrawCards(seal.value);
                        break;
                    case CardSealEffect.Armor:
                        effect = CardEffectSpec.GainArmor(seal.value);
                        break;
                    case CardSealEffect.Amount:
                        effect = CardEffectSpec.ModifyStat(
                            StatType.Amount, seal.value);
                        break;
                    case CardSealEffect.BengLie:
                        effect = CardEffectSpec.ModifyStat(
                            StatType.BengLie, seal.value);
                        break;
                    case CardSealEffect.Might:
                        effect = CardEffectSpec.ModifyStat(
                            StatType.FengRui, seal.value);
                        break;
                    case CardSealEffect.Refund:
                    case CardSealEffect.ManaRebate:
                        if (!TryGetManaRefundRule(seal, out CardManaRefundRule refund) ||
                            !refund.IsSatisfied(payment))
                            continue;
                        effect = CardEffectSpec.GainMana(
                            refund.GetAmount(payment));
                        break;
                    case CardSealEffect.Nduja:
                        effect = CardEffectSpec.Damage(seal.value);
                        effect.damageSpec = new DamageSpec
                        {
                            baseHitDamage = seal.value,
                            comboScalable = false,
                            amountScalable = false,
                            pattern = new AttackPatternSpec
                            {
                                baseAttackInstanceCount = 1,
                                targetPolicy = TargetPolicy.FrozenNearestAttackableRow,
                                directTargetsPerSubHit = 0,
                                comboMode = ComboPatternMode.Disabled,
                                amountMode = AmountPatternMode.Disabled
                            },
                            crit = new CritSpec { enabled = false },
                            area = new AreaSpec
                            {
                                enabled = false,
                                triggerMode = AreaTriggerMode.Disabled
                            }
                        };
                        break;
                    default:
                        continue;
                }
                effect.comboScalable = false;
                effects.Add(effect);
            }
            return effects;
        }

        /// <summary>The same typed payment prerequisite used by execution and observation.</summary>
        public static bool TryGetManaRefundRule(CardSealSpec seal, out CardManaRefundRule rule)
        {
            rule = default;
            if (seal == null || seal.value <= 0 ||
                (seal.effect != CardSealEffect.Refund && seal.effect != CardSealEffect.ManaRebate))
                return false;
            rule = new CardManaRefundRule(seal.value, seal.effect == CardSealEffect.ManaRebate);
            return true;
        }

        public static float GetDamageMultiplier(
            IReadOnlyList<CardSealSpec> seals)
        {
            double multiplier = 1d;
            if (seals == null)
                return 1f;

            for (int i = 0; i < seals.Count; i++)
            {
                CardSealSpec seal = seals[i];
                if (seal == null)
                    continue;
                switch (seal.effect)
                {
                    case CardSealEffect.DoubleDamage:
                        multiplier *= seal.value > 1 ? seal.value : 2;
                        break;
                    case CardSealEffect.TripleDamage:
                        multiplier *= seal.value > 1 ? seal.value : 3;
                        break;
                }
            }

            return (float)Math.Min(float.MaxValue, multiplier);
        }

        public static DamageSpec ApplyDamageMultiplier(
            DamageSpec source,
            IReadOnlyList<CardSealSpec> seals)
            => ApplyDamageMultiplier(source, GetDamageMultiplier(seals));

        public static DamageSpec ApplyDamageMultiplier(
            DamageSpec source,
            float sealMultiplier)
        {
            if (source == null)
                return null;
            if (Math.Abs(sealMultiplier - 1f) < 0.0001f)
                return source;

            return new DamageSpec
            {
                baseHitDamage = source.baseHitDamage,
                manaDamageCoefficient = source.manaDamageCoefficient,
                limitBreakAttackInstancesPerLevel =
                    source.limitBreakAttackInstancesPerLevel,
                pattern = source.pattern,
                crit = source.crit,
                area = source.area,
                bounce = source.bounce,
                specials = source.specials,
                onHitEffects = source.onHitEffects,
                terrainEffect = source.terrainEffect,
                ricochetProjectile = source.ricochetProjectile,
                birdAttack = source.birdAttack?.Clone(),
                cardLocalDamageMultiplier =
                    Math.Max(0f, source.cardLocalDamageMultiplier) *
                    sealMultiplier,
                comboScalable = source.comboScalable,
                amountScalable = source.amountScalable
            };
        }
    }
}
