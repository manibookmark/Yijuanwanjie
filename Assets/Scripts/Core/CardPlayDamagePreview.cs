using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Whole-preview contract remains available even when no target rows are returned.</summary>
    public sealed class CardDamagePreviewCoverage
    {
        internal CardDamagePreviewCoverage(bool isEstimate, bool hasUnresolvedRandomness,
            bool hasPotentialDirectDamage, bool hasCombatContext, bool hasUnresolvedConditions)
        {
            IsEstimate = isEstimate;
            HasUnresolvedRandomness = hasUnresolvedRandomness;
            HasPotentialDirectDamage = hasPotentialDirectDamage;
            HasCombatContext = hasCombatContext;
            HasUnresolvedConditions = hasUnresolvedConditions;
        }
        public bool IsEstimate { get; }
        public bool HasUnresolvedRandomness { get; }
        public bool HasPotentialDirectDamage { get; }
        public bool HasCombatContext { get; }
        public bool HasUnresolvedConditions { get; }
        public bool EmptyPreviewGuaranteesNoDamage => false;
        public string Coverage => HasCombatContext ? "OrdinaryDirectDamage" : "NoCombatContext";
        public string Limitations => (HasCombatContext ? string.Empty :
            "No combat context: target damage and current attack scaling have not been calculated. ") +
            "Ordinary direct-damage hypothesis: unresolved random targets, ricochet paths, " +
            "criticals and control can change the result. Authored effect conditions are not evaluated by this " +
            "observation; condition metadata is not a claim that the current executor has resolved them. " +
            "Draw order, delayed/status/terrain damage, crawler/color " +
            "reactions, kill rewards and external callbacks are not simulated. An empty target list does not " +
            "guarantee no damage or no later damage.";
    }

    /// <summary>
    /// RNG-free, whole-card direct-damage estimate. One invocation exclusively owns
    /// its hypothetical player, enemies and overkill session; no live state, random
    /// tape, deck zone or exact-preview cache is created or changed.
    /// Damage mathematics and target policies remain in AttackPlan/AttackResolver.
    /// </summary>
    public static class CardPlayDamagePreview
    {
        /// <summary>
        /// RNG-free whole-card classification. A combat caller supplies the same
        /// frozen stats as its main plan; without them only an authored hypothesis
        /// is available and Coverage explicitly reports NoCombatContext.
        /// </summary>
        public static CardDamagePreviewCoverage DescribeCoverage(CardConfig card,
            AttackPlan plan, CombatRuleSet rules = null, CombatStatSnapshot? frozenStats = null)
        {
            bool context = frozenStats.HasValue;
            CombatStatSnapshot stats = frozenStats ?? new CombatStatSnapshot(null, 0);
            ConditionEvaluationRequirements conditions = DescribeCardConditions(card);
            bool random = AttackResolver.HasUnresolvedPreviewDecisions(plan) ||
                HasUnresolvedCardAttackDecisions(card, stats, rules) || conditions.RequiresRandom;
            bool composite = NeedsEstimate(card, rules);
            bool potential = HasAuthoredDirectEffects(card) || composite || (plan != null &&
                (plan.SubHits.Count > 0 || plan.RicochetProjectile != null));
            return new CardDamagePreviewCoverage(!context || random || composite || conditions.RequiresEvaluation,
                random, potential, context, conditions.RequiresEvaluation);
        }

        private static ConditionEvaluationRequirements DescribeCardConditions(CardConfig card)
        {
            bool evaluation = false, random = false;
            if (card?.HasSpecEffects == true)
                foreach (CardEffectSpec effect in card.effects)
                {
                    ConditionEvaluationRequirements requirement = ConditionEvaluator.DescribeRequirements(effect?.conditions);
                    evaluation |= requirement.RequiresEvaluation;
                    random |= requirement.RequiresRandom;
                }
            foreach (CardSealPostPlayAction action in CardSealRules.GetPostPlayActions(card?.seals))
            {
                ConditionEvaluationRequirements requirement = ConditionEvaluator.DescribeRequirements(action.Effect?.conditions);
                evaluation |= requirement.RequiresEvaluation;
                random |= requirement.RequiresRandom;
            }
            return new ConditionEvaluationRequirements(evaluation, random);
        }

        /// <summary>Formal display may freeze RNG while authored prerequisites remain unresolved.</summary>
        internal static bool HasUnresolvedConditions(CardConfig card)
            => DescribeCardConditions(card).RequiresEvaluation;

        private static bool HasUnresolvedCardAttackDecisions(CardConfig card,
            CombatStatSnapshot stats, CombatRuleSet rules)
        {
            if (card == null) return false;
            CardAttackSnapshot source = CardAttackSnapshot.Capture(card);
            if (card.HasSpecEffects)
            {
                foreach (CardEffectSpec effect in card.effects)
                    if (AttackPlanBuilder.TryFromDamageEffect(card, effect, rules, out DamageSpec damage) &&
                        AttackResolver.HasUnresolvedPreviewDecisions(AttackPlanBuilder.Build(damage, stats, source)))
                        return true;
            }
            else if (AttackPlanBuilder.TryBuildFromCard(card, rules, stats, out AttackPlan legacy) &&
                AttackResolver.HasUnresolvedPreviewDecisions(legacy))
                return true;

            // Standard seal attacks use their authoritative preparation path.
            // ArmorStrike requires a real target and is a fixed ordinary hit;
            // with this empty roster it correctly supplies no hypothetical plan.
            // Standalone Bounce likewise selects deterministically, not by RNG.
            var player = new PlayerStats { fengRui = stats.Might, amount = stats.Amount,
                bengLie = stats.Area, tianYun = stats.Luck };
            foreach (CardSealPostPlayAction action in CardSealRules.GetPostPlayActions(card.seals))
                if (action.TryPrepareAttack(card, player, Array.Empty<EnemyStats>(), out AttackPlan post, out _) &&
                    AttackResolver.HasUnresolvedPreviewDecisions(post))
                    return true;
            return false;
        }

        /// <summary>Damage capability for a card face, excluding pure Erase defeats.
        /// No legal target still means a damage card with an immediate total of zero.</summary>
        internal static bool HasPotentialImmediateDamage(CardConfig card, CombatRuleSet rules)
        {
            if (HasAuthoredDirectEffects(card, includeNonDamageDefeats: false)) return true;
            if (card == null || rules?.shieldBashEnabled != true) return false;
            if (!card.HasSpecEffects) return card.armor > 0;
            foreach (CardEffectSpec effect in card.effects)
                if (effect?.effectType == EffectType.GainArmor && effect.amount > 0) return true;
            return false;
        }

        private static bool HasAuthoredDirectEffects(CardConfig card, bool includeNonDamageDefeats = true)
        {
            if (card == null) return false;
            if (!card.HasSpecEffects && card.baseDamage > 0) return true;
            if (card.HasSpecEffects)
                foreach (CardEffectSpec effect in card.effects)
                    if (effect != null && (effect.effectType == EffectType.Damage ||
                        effect.effectType == EffectType.Bounce ||
                        (includeNonDamageDefeats && effect.effectType == EffectType.Erase)))
                        return true;
            if (card.seals != null)
                foreach (CardSealSpec seal in card.seals)
                    if (seal != null && seal.value > 0 &&
                        (seal.effect == CardSealEffect.Nduja || seal.effect == CardSealEffect.ArmorStrike))
                        return true;
            return false;
        }

        /// <summary>
        /// Routes only cards whose damage cannot be represented by the single main
        /// AttackPlan. A multi-hit single Damage effect is still one exact plan.
        /// Pass the encounter rules to include the optional host-only ShieldBash.
        /// This query never inspects card zones or requests random decisions.
        /// </summary>
        public static bool NeedsEstimate(CardConfig card, CombatRuleSet rules = null)
        {
            if (card == null) return false;
            if (card.seals != null)
                foreach (CardSealSpec seal in card.seals)
                    if (seal != null && seal.value > 0 &&
                        (seal.effect == CardSealEffect.Nduja ||
                         seal.effect == CardSealEffect.ArmorStrike))
                        return true;

            if (!card.HasSpecEffects)
                return rules?.shieldBashEnabled == true && card.armor > 0;

            int damageEffects = 0;
            bool handMayChange = false;
            foreach (CardEffectSpec effect in card.effects)
            {
                if (effect == null) continue;
                switch (effect.effectType)
                {
                    case EffectType.DrawCards:
                    case EffectType.DiscardCards:
                    case EffectType.CreateCard:
                        handMayChange = true;
                        break;
                    case EffectType.Damage:
                        if (AttackPlanBuilder.TryFromDamageEffect(card, effect, rules, out _)
                            && (++damageEffects > 1 || handMayChange))
                            return true;
                        break;
                    case EffectType.GainArmor:
                        if (rules?.shieldBashEnabled == true && effect.amount > 0)
                            return true;
                        break;
                    case EffectType.Bounce:
                    case EffectType.Erase:
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Estimates all host Damage/GainArmor and deferred damage-relevant
        /// ModifyStat effects, then host ShieldBash and every ordered post-play seal.
        /// Legacy damage/armor, Bounce and deterministic Erase also use Core rules.
        /// resultingCombo is the caller's already-resolved play preview, not current
        /// Combo; this method neither pays mana nor advances combo.
        ///
        /// Every returned item has IsEstimate=true (including WillKill). Preview
        /// mode uses ordinary hits and deterministic target choices, not crit/control
        /// rolls. Held aura is frozen from the known hand excluding the departing
        /// instance, even if it has Return. Draws never predict order, reshuffles,
        /// newly drawn aura or a re-drawn host. No random legacy status/erase rolls,
        /// delayed status/terrain damage,
        /// crawler/color reactions, kill rewards or external event callbacks are
        /// simulated; these can change the eventual result. Non-damage Run/deck
        /// stat changes are not persisted or projected. An empty list means no
        /// direct damage in this hypothesis, not a promise of no eventual damage.
        /// includeNonDamageDefeats exposes zero-damage Erase outcomes for display;
        /// the default retains the existing observation/strategy target-list contract.
        /// </summary>
        public static List<EnemyDamagePreview> Estimate(
            CardConfig card, CombatState state, int resultingCombo,
            CombatRuleSet rules = null, bool includeNonDamageDefeats = false)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (card == null) return new List<EnemyDamagePreview>();
            return new Hypothesis(card, state, resultingCombo, rules,
                includeNonDamageDefeats).Resolve();
        }

        // All mutable fields below belong to this one short-lived hypothesis.
        // In particular, no CombatState/DeckManager/IRandomSource is retained.
        private sealed class Hypothesis
        {
            private readonly CardConfig _card;
            private readonly PlayerStats _player;
            private readonly List<EnemyStats> _enemies;
            private readonly BossOverkillSession _overkill;
            // Own a copied coverage state for every host plan in this hypothesis.
            // AttackResolver records hypothetical placements; live terrain is never written.
            private readonly TerrainAttackCoverage _terrainCoverage;
            private readonly IReadOnlyList<EnemyIntent> _intents;
            private readonly CombatRuleSet _rules;
            private readonly int _combo;
            private readonly float _heldMultiplier;
            private readonly long _manaBeforePayment;
            private readonly int[] _requested;
            private readonly int[] _applied;
            private readonly int[] _overkillDamage;
            private readonly bool[] _erased;
            private readonly bool[] _legacyEraseMarkers;
            private readonly bool _includeNonDamageDefeats;
            private int _lastTarget = -1;

            internal Hypothesis(CardConfig card, CombatState state, int combo,
                CombatRuleSet rules, bool includeNonDamageDefeats)
            {
                _card = card;
                _player = ClonePlayer(state.Player);
                _enemies = new List<EnemyStats>(state.Enemies.Count);
                foreach (EnemyStats enemy in state.Enemies)
                    _enemies.Add(enemy.CloneForSimulation());
                _overkill = state.BossOverkill?.CreatePreviewSession(_enemies);
                _terrainCoverage = new TerrainAttackCoverage(state.TerrainEffects?.Active);
                _intents = state.EnemyAi?.GetPlannedIntents();
                _rules = rules?.Clone() ?? new CombatRuleSet();
                _combo = Math.Max(0, combo);
                _heldMultiplier = CardSealRules.GetHeldAttackMultiplier(state.Hand, card);
                _manaBeforePayment = (long)state.Mana.CurrentMana + state.Mana.TemporaryMana;
                _requested = new int[_enemies.Count];
                _applied = new int[_enemies.Count];
                _overkillDamage = new int[_enemies.Count];
                _erased = new bool[_enemies.Count];
                _legacyEraseMarkers = new bool[_enemies.Count];
                _includeNonDamageDefeats = includeNonDamageDefeats;
                // Combat begins special effects with ResolveEffectTarget(-1),
                // even before any Damage. Bounce excludes that initial enemy.
                _lastTarget = ResolveEffectTarget(-1);
            }

            internal List<EnemyDamagePreview> Resolve()
            {
                int hostArmorGained = ResolveHost();
                if (_rules.shieldBashEnabled && hostArmorGained > 0 &&
                    _player.armor > 0 && AttackResolver.GetAttackableTargets(_enemies).Count > 0)
                    Execute(AttackPlanBuilder.BuildFixedDirect(
                        CardAttackSnapshot.Capture(_card), _player.armor,
                        TargetPolicy.SequentialRightToLeft));

                // Costs and subsequent slots observe only this hypothesis. The
                // shared action refuses ArmorStrike before charging if no target.
                foreach (CardSealPostPlayAction action in CardSealRules.GetPostPlayActions(_card.seals))
                {
                    if (action.TryPrepareAttack(_card, _player, _enemies,
                            out AttackPlan plan, out int armorCost))
                    {
                        _player.armor -= armorCost;
                        Execute(plan);
                    }
                    else if (action.Effect?.effectType == EffectType.GainArmor)
                        _player.armor += action.Effect.amount;
                    else if (action.Effect?.effectType == EffectType.ModifyStat)
                        ApplyDamageStat(action.Effect.statType, action.Effect.amount);
                }

                var result = new List<EnemyDamagePreview>();
                for (int i = 0; i < _enemies.Count; i++)
                    if (_requested[i] > 0 || _legacyEraseMarkers[i] ||
                        (_includeNonDamageDefeats && _erased[i]))
                        result.Add(new EnemyDamagePreview(_enemies[i].runtimeId, i,
                            _applied[i], _overkillDamage[i], _requested[i],
                            _enemies[i].IsDead, isEstimate: true, isErased: _erased[i]));
                return result;
            }

            private int ResolveHost()
            {
                int armorGained = 0;
                if (!_card.HasSpecEffects)
                {
                    if (AttackPlanBuilder.TryBuildFromCard(_card, _rules, Snapshot(), out AttackPlan plan))
                        Execute(plan);
                    if (_card.armor > 0)
                    {
                        armorGained = _card.armor * (1 + _combo);
                        _player.armor += armorGained;
                    }
                    return armorGained;
                }

                var deferredStats = new List<KeyValuePair<StatType, int>>();
                foreach (CardEffectSpec effect in _card.effects)
                {
                    if (effect == null) continue;
                    int amount = effect.comboScalable ? effect.amount * (1 + _combo) : effect.amount;
                    switch (effect.effectType)
                    {
                        case EffectType.Damage:
                            if (AttackPlanBuilder.TryFromDamageEffect(_card, effect, _rules, out DamageSpec damage))
                                Execute(AttackPlanBuilder.Build(damage, Snapshot(), CardAttackSnapshot.Capture(_card)));
                            break;
                        case EffectType.GainArmor:
                            _player.armor += amount;
                            armorGained += amount;
                            break;
                        case EffectType.ModifyStat:
                            deferredStats.Add(new KeyValuePair<StatType, int>(effect.statType, amount));
                            break;
                        case EffectType.Bounce:
                            ResolveBounce(effect);
                            break;
                        case EffectType.Erase:
                            ResolveErase(effect.target);
                            break;
                    }
                }
                // Matches CombatController: even an earlier ModifyStat cannot
                // amplify a later host Damage; it can affect post-play attacks.
                foreach (KeyValuePair<StatType, int> change in deferredStats)
                    ApplyDamageStat(change.Key, change.Value);
                return armorGained;
            }

            private CombatStatSnapshot Snapshot()
                => new CombatStatSnapshot(_player, _combo, _heldMultiplier, _manaBeforePayment);

            private void Execute(AttackPlan plan)
            {
                AttackResolutionResult result = AttackResolver.Execute(plan, _enemies,
                    ResolutionMode.Preview, upcomingIntents: _intents, bossOverkill: _overkill,
                    terrainCoverage: _terrainCoverage);
                Accumulate(result);
                foreach (DamageEvent hit in result.Events)
                    if (hit.Kind == DamageKind.Direct)
                    {
                        _lastTarget = hit.TargetIndex;
                        break;
                    }
            }

            private void Accumulate(AttackResolutionResult result)
            {
                foreach (DamageEvent hit in result.Events)
                {
                    _requested[hit.TargetIndex] += hit.RequestedDamage;
                    _applied[hit.TargetIndex] += hit.AppliedDamage;
                    _overkillDamage[hit.TargetIndex] += hit.OverkillDamage;
                }
            }

            private void ResolveBounce(CardEffectSpec effect)
            {
                int excludedId = _lastTarget >= 0 ? _enemies[_lastTarget].runtimeId : 0;
                AttackResolutionResult result = AttackResolver.ResolveBounce(
                    effect.amount, effect.count, new PrePlayStatsSnapshot(_player, _combo),
                    _enemies, _lastTarget, BounceTargetMode.UniqueThenStop, excludedId, _overkill);
                Accumulate(result);
                if (result.Events.Count > 0)
                    _lastTarget = result.Events[result.Events.Count - 1].TargetIndex;
            }

            private void ResolveErase(TargetRule rule)
            {
                int target = ResolveEffectTarget(_lastTarget);
                EraseResolutionResult result = EraseResolver.Execute(
                    _enemies, rule, target, _card.runtimeInstanceId);
                foreach (EnemyDefeatResult defeat in result.Defeats)
                {
                    // Keep the established row markers in the legacy observation.
                    // Display may additionally request all non-damage defeat outcomes.
                    _erased[defeat.TargetIndex] = true;
                    _legacyEraseMarkers[defeat.TargetIndex] = rule == TargetRule.NearestAttackableRow;
                    _lastTarget = defeat.TargetIndex;
                }
                // Erase is a defeat, never damage or a BossOverkill activation.
            }

            private int ResolveEffectTarget(int requestedIndex)
            {
                if (AttackResolver.GetAttackableTargets(_enemies).Contains(requestedIndex))
                    return requestedIndex;
                // A fresh right-to-left selector gives the same rightmost-nearest
                // fallback as CombatController.ResolveEffectTarget, including wide
                // footprints. No overkill session: legacy effect targets are alive.
                var selector = new AttackTargetSelector(ResolutionMode.Preview, null, null, null);
                selector.BeginSubHit(_enemies, TargetPolicy.SequentialRightToLeft, 1);
                return selector.TrySelectNext(_enemies, out int target) ? target : -1;
            }

            private void ApplyDamageStat(StatType stat, int amount)
            {
                // Only these scalar fields feed immediate damage/armor plans.
                // Run growth/luck/greed and hand-size/draw ownership stay outside
                // this module; none changes Preview-mode direct damage.
                switch (stat)
                {
                    case StatType.FengRui: _player.fengRui += amount; break;
                    case StatType.Amount: _player.amount += amount; break;
                    case StatType.BengLie: _player.bengLie += amount; break;
                    case StatType.HuJia: _player.armor += amount; break;
                }
            }

            private static PlayerStats ClonePlayer(PlayerStats source)
                => new PlayerStats
                {
                    maxHealth = source.maxHealth, currentHealth = source.currentHealth,
                    armor = source.armor, baseArmorPerTurn = source.baseArmorPerTurn,
                    fengRui = source.fengRui, amount = source.amount, bengLie = source.bengLie,
                    ningShen = source.ningShen, shouPai = source.shouPai,
                    yinJuan = source.yinJuan, chouPai = source.chouPai,
                    tianYun = source.tianYun, lianYu = source.lianYu, mingWu = source.mingWu,
                    hunHun = source.hunHun, faLi = source.faLi, zhaoSha = source.zhaoSha,
                    recovery = source.recovery
                };
        }
    }
}
