using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns one encounter's resolved Recovery baseline, temporary gains and
    /// terminal consumption. Does not choose victory, reset or exit policy.
    /// The permanent stat remains untouched; callers resolve gain modifiers
    /// before adding them and explicitly authorize healing or discard.
    /// </summary>
    internal sealed class EncounterRecovery
    {
        private readonly PlayerStats _player;
        private readonly int _baseline;
        private int _temporary;
        private bool _closed;

        internal int CurrentValue => checked(_baseline + _temporary);

        internal EncounterRecovery(PlayerStats player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _baseline = player.recovery;
        }

        /// <summary>Already includes applicable combo/medicine modifiers; never heal here.</summary>
        internal void GainResolvedAmount(int amount)
        {
            if (_closed) throw new InvalidOperationException("Recovery encounter is already closed.");
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int nextTemporary = checked(_temporary + amount);
            checked { _ = _baseline + nextTemporary; }
            _temporary = nextTemporary;
        }

        /// <summary>
        /// Heal once using the current maximum health, then drop temporary
        /// gains. A zero actual heal still consumes this encounter's value.
        /// No outcome or player-life check substitutes for caller authorization.
        /// </summary>
        internal bool TryConsumeAfterEncounter(out EncounterRecoveryReceipt receipt)
        {
            receipt = default;
            if (_closed) return false;
            int requested = Math.Max(0, CurrentValue);
            int healthAfter = _player.currentHealth;
            if (requested > 0)
                healthAfter = (int)Math.Min(Math.Max(0L, _player.maxHealth),
                    Math.Max(0L, (long)_player.currentHealth + requested));
            int actual = checked(healthAfter - _player.currentHealth);
            receipt = new EncounterRecoveryReceipt(requested, actual);
            _closed = true;
            _temporary = 0;
            _player.currentHealth = healthAfter;
            return true;
        }

        /// <summary>Close without healing; it never changes the permanent baseline.</summary>
        internal void Discard()
        {
            _closed = true;
            _temporary = 0;
        }
    }

    internal readonly struct EncounterRecoveryReceipt
    {
        internal readonly int RequestedRecovery;
        internal readonly int ActualHealthChange;

        internal EncounterRecoveryReceipt(int requestedRecovery, int actualHealthChange)
        {
            RequestedRecovery = requestedRecovery;
            ActualHealthChange = actualHealthChange;
        }
    }
}
