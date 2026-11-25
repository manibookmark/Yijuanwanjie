using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// One frozen seal instruction, evaluated at its own ordered resolution boundary.
    /// Hides special-attack stat isolation and resource cost; never mutates the player,
    /// deck or enemies. Combat commits the returned cost and executes the standard plan.
    /// Ordinary effects remain standard card effects, not a second effect executor.
    /// </summary>
    public readonly struct CardSealPostPlayAction
    {
        public CardEffectSpec Effect { get; }
        public int CopyCount { get; }
        public int SealSlotIndex { get; }
        public string SealContentId { get; }
        private readonly bool _spendCurrentArmor;

        internal CardSealPostPlayAction(CardEffectSpec effect, bool spendCurrentArmor = false)
        {
            Effect = effect;
            _spendCurrentArmor = spendCurrentArmor;
            CopyCount = 0;
            SealSlotIndex = -1;
            SealContentId = null;
        }

        internal CardSealPostPlayAction(int copyCount, int sealSlotIndex, string sealContentId)
        {
            Effect = null;
            _spendCurrentArmor = false;
            CopyCount = Math.Max(0, copyCount);
            SealSlotIndex = sealSlotIndex;
            SealContentId = sealContentId;
        }

        /// <summary>
        /// Armor is sampled only now, after the host and all preceding seal slots.
        /// No legal living target means no plan and no cost. A committed plan has one
        /// deterministic ordinary front target; neither hit seals nor global/host
        /// multipliers can amplify it. Later slots observe the already spent armor.
        /// </summary>
        public bool TryPrepareAttack(CardConfig source, PlayerStats player,
            IList<EnemyStats> enemies, out AttackPlan plan, out int armorCost)
        {
            plan = null;
            armorCost = 0;
            if (source == null || player == null || enemies == null)
                return false;
            if (_spendCurrentArmor)
            {
                int armor = Math.Max(0, player.armor);
                if (armor == 0 || AttackResolver.GetAttackableTargets(enemies).Count == 0)
                    return false;
                plan = AttackPlanBuilder.BuildFixedDirect(
                    new CardAttackSnapshot(source.runtimeInstanceId, 0), armor,
                    TargetPolicy.FirstAttackable, canTriggerSecondaryEffects: false);
                armorCost = armor;
                return true;
            }
            if (Effect?.effectType != EffectType.Damage || Effect.damageSpec == null)
                return false;
            plan = AttackPlanBuilder.Build(Effect.damageSpec,
                new CombatStatSnapshot(player, 0),
                new CardAttackSnapshot(source.runtimeInstanceId, 0),
                canTriggerSecondaryEffects: false);
            return true;
        }
    }
}
