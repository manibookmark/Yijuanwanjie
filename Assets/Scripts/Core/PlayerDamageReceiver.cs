using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// One combat's player damage boundary. Keeps actual shield/HP hit totals
    /// independent of healing, and consumes the same Run reserve before terminal
    /// checks. DamageSystem writes damage; RevivalReserve writes revived health.
    /// This owner never captures account entry or persists a second charge ledger.
    /// </summary>
    internal sealed class PlayerDamageReceiver
    {
        private readonly PlayerStats _player;
        private readonly RunRevivalReserve _reserve;
        private long _hpDamage, _shieldDamage;
        private bool _applying;
        internal PlayerDamageTotals Totals => new PlayerDamageTotals(_hpDamage, _shieldDamage);
        internal event Action<PlayerRevivedEvent> OnRevived;

        internal PlayerDamageReceiver(PlayerStats player, RunRevivalReserve reserve)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _reserve = reserve ?? throw new ArgumentNullException(nameof(reserve));
            if (!reserve.IsInitialized) throw new InvalidOperationException("Revival entry must be admitted before combat damage.");
        }

        internal DamageResolution Apply(int requested)
        {
            if (_applying) throw new InvalidOperationException("Player damage cannot re-enter its hit boundary.");
            _applying = true;
            try
            {
                DamageResolution damage = DamageSystem.ApplyToPlayer(_player, requested);
                _hpDamage = checked(_hpDamage + damage.HpDamage);
                _shieldDamage = checked(_shieldDamage + damage.ShieldDamage);
                if (!damage.BecameFatal || !_reserve.TryRevive(_player)) return damage;
                OnRevived?.Invoke(new PlayerRevivedEvent(_player.currentHealth, _reserve.RemainingPercent, _reserve.SpentLives));
                return new DamageResolution(damage.RequestedDamage, damage.AppliedDamage,
                    damage.ShieldDamage, damage.HpDamage, damage.OverkillDamage,
                    becameFatal: false, remainingHealth: _player.currentHealth);
            }
            finally { _applying = false; }
        }
    }

    internal readonly struct PlayerDamageTotals
    {
        internal long HpDamage { get; }
        internal long ShieldDamage { get; }
        internal PlayerDamageTotals(long hp, long shield) { HpDamage = hp; ShieldDamage = shield; }
    }
}
