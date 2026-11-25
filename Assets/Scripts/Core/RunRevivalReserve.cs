using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// One Run owns its charge and consumed lives. 100 percent is one full-health
    /// revival; a new encounter never reconstructs a consumed life from entry stats.
    /// Default capacity 300 is the project's GDD contract; native asset limits and
    /// explicit leader overrides are separate admission decisions.
    /// </summary>
    public sealed class RunRevivalReserve
    {
        public const int PercentPerLife = 100;
        public bool IsInitialized { get; private set; }
        public int CapacityPercent { get; private set; }
        public int InitialPercent { get; private set; }
        public int AwardedPercent { get; private set; }
        public int SpentLives { get; private set; }
        public int RemainingPercent { get; private set; }

        public void InitializeOnce(int entryPercent, int capacityPercent = 300)
        {
            if (IsInitialized) return;
            if (entryPercent < 0 || capacityPercent <= 0)
                throw new ArgumentOutOfRangeException(nameof(entryPercent));
            InitialPercent = Math.Min(entryPercent, capacityPercent);
            CapacityPercent = capacityPercent;
            RemainingPercent = InitialPercent;
            IsInitialized = true;
        }

        public int AddCharge(int positivePercent)
        {
            if (!IsInitialized) throw new InvalidOperationException("Revival entry has not been admitted.");
            if (positivePercent <= 0) return 0;
            int accepted = Math.Min(positivePercent, CapacityPercent - RemainingPercent);
            // The persistent evidence is exact. Overflow rejects without changing
            // any part of the reserve rather than inventing earned/consumed proof.
            int awarded = checked(AwardedPercent + accepted);
            AwardedPercent = awarded;
            RemainingPercent += accepted;
            return accepted;
        }

        public bool TryRevive(PlayerStats player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (!IsInitialized || player.currentHealth > 0 || player.maxHealth <= 0 || RemainingPercent < PercentPerLife)
                return false;
            int spent = checked(SpentLives + 1);
            RemainingPercent -= PercentPerLife;
            SpentLives = spent;
            player.currentHealth = player.maxHealth;
            player.hunHun = RemainingPercent;
            return true;
        }

        public RunRevivalSnapshot Capture() => new RunRevivalSnapshot(IsInitialized, CapacityPercent,
            InitialPercent, AwardedPercent, SpentLives, RemainingPercent);

        public void Restore(RunRevivalSnapshot snapshot)
        {
            IsInitialized = snapshot.Initialized; CapacityPercent = snapshot.CapacityPercent;
            InitialPercent = snapshot.InitialPercent; AwardedPercent = snapshot.AwardedPercent;
            SpentLives = snapshot.SpentLives; RemainingPercent = snapshot.RemainingPercent;
        }
    }

    public readonly struct RunRevivalSnapshot
    {
        public bool Initialized { get; }
        public bool IsInitialized => Initialized;
        public int CapacityPercent { get; }
        public int InitialPercent { get; }
        public int AwardedPercent { get; }
        public int SpentLives { get; }
        public int RemainingPercent { get; }
        public RunRevivalSnapshot(bool initialized, int capacityPercent, int initialPercent,
            int awardedPercent, int spentLives, int remainingPercent)
        {
            if (initialPercent < 0 || awardedPercent < 0 || spentLives < 0 || remainingPercent < 0 ||
                (initialized ? capacityPercent <= 0 || initialPercent > capacityPercent || remainingPercent > capacityPercent
                    : capacityPercent != 0 || initialPercent != 0 || awardedPercent != 0 || spentLives != 0 || remainingPercent != 0) ||
                (long)initialPercent + awardedPercent - (long)spentLives * RunRevivalReserve.PercentPerLife != remainingPercent)
                throw new ArgumentException("Revival reserve has inconsistent earned/consumed proof.");
            Initialized = initialized; CapacityPercent = capacityPercent; InitialPercent = initialPercent;
            AwardedPercent = awardedPercent; SpentLives = spentLives; RemainingPercent = remainingPercent;
        }
    }
}
