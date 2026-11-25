using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns the first direct-critical healing opportunity for ONE attack
    /// execution. Construct again for a real re-execution, never per object,
    /// target or cached card. Critical decisions come from the attack owner;
    /// healing modifiers are pure and never include combo/amount scaling.
    /// Call before the triggering hit's damage/death effects when integrating.
    /// </summary>
    internal sealed class AttackCriticalHealing
    {
        private readonly PlayerStats _player;
        private readonly int _sourceCardInstanceId;
        private readonly int _baseHealing;
        private bool _consumed;
        private bool _applying;

        internal AttackCriticalHealing(PlayerStats player, int sourceCardInstanceId, int baseHealing)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            if (sourceCardInstanceId <= 0) throw new ArgumentOutOfRangeException(nameof(sourceCardInstanceId));
            if (baseHealing < 0) throw new ArgumentOutOfRangeException(nameof(baseHealing));
            _sourceCardInstanceId = sourceCardInstanceId;
            _baseHealing = baseHealing;
        }

        /// <summary>
        /// Consume an authoritative decision once, including full-health or
        /// zero-healing results. No positive-damage, kill or secondary-effect
        /// requirement is added. Null modifier means the unmodified base value.
        /// Preparation failure does not commit health or consume the opportunity.
        /// </summary>
        internal bool TryApply(CriticalHealingHit hit, Func<int, int> resolveHealing,
            out CriticalHealingReceipt receipt)
        {
            receipt = default;
            if (_applying) throw new InvalidOperationException("Critical healing cannot be re-entered.");
            if (_consumed || hit.SourceCardInstanceId != _sourceCardInstanceId ||
                hit.Kind != DamageKind.Direct || !hit.IsCritical) return false;
            _applying = true;
            try
            {
                int requested = resolveHealing == null ? _baseHealing : resolveHealing(_baseHealing);
                if (requested < 0) throw new InvalidOperationException("Resolved healing must be nonnegative.");
                int healthAfter = _player.currentHealth;
                if (requested > 0)
                    healthAfter = (int)Math.Min(Math.Max(0L, _player.maxHealth),
                        Math.Max(0L, (long)_player.currentHealth + requested));
                int actual = checked(healthAfter - _player.currentHealth);
                receipt = new CriticalHealingReceipt(_sourceCardInstanceId, requested, actual);
                _consumed = true;
                _player.currentHealth = healthAfter;
                return true;
            }
            finally { _applying = false; }
        }
    }

    /// <summary>
    /// Minimal immutable critical decision, available before damage is applied.
    /// Does not roll randomness or manufacture a completed damage receipt.
    /// </summary>
    internal readonly struct CriticalHealingHit
    {
        internal readonly int SourceCardInstanceId;
        internal readonly DamageKind Kind;
        internal readonly bool IsCritical;

        internal CriticalHealingHit(int sourceCardInstanceId, DamageKind kind, bool isCritical)
        { SourceCardInstanceId = sourceCardInstanceId; Kind = kind; IsCritical = isCritical; }

        internal CriticalHealingHit(DamageEvent resolved)
            : this(resolved.SourceCardInstanceId, resolved.Kind, resolved.IsCritical) { }
    }

    internal readonly struct CriticalHealingReceipt
    {
        internal readonly int SourceCardInstanceId;
        internal readonly int RequestedHealing;
        internal readonly int ActualHealthChange;

        internal CriticalHealingReceipt(int sourceCardInstanceId, int requestedHealing, int actualHealthChange)
        { SourceCardInstanceId = sourceCardInstanceId; RequestedHealing = requestedHealing; ActualHealthChange = actualHealthChange; }
    }
}
