using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Run-owned Duration gained while no crawler is active, including the
    /// account/leader bonus admitted once at Run entry. Encounters reuse this
    /// owner; the next successful summon consumes its entire reserve once.
    /// </summary>
    public sealed class CrawlerDurationReserve
    {
        public const int MaximumBonus = 999;
        public int PendingBonus { get; private set; }

        /// <summary>Nonnegative gains saturate at the shared crawler duration cap.</summary>
        public int Bank(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int before = PendingBonus;
            PendingBonus = (int)Math.Min(MaximumBonus, (long)before + amount);
            return PendingBonus - before;
        }

        public int Consume()
        {
            int consumed = PendingBonus;
            PendingBonus = 0;
            return consumed;
        }

        /// <summary>Restore replaces a validated checkpoint; it never grants duration again.</summary>
        public void Restore(int pendingBonus)
        {
            RequireValid(pendingBonus);
            PendingBonus = pendingBonus;
        }

        public static void RequireValid(int pendingBonus)
        {
            if (pendingBonus < 0 || pendingBonus > MaximumBonus)
                throw new ArgumentOutOfRangeException(nameof(pendingBonus),
                    "Crawler duration reserve must be between 0 and 999.");
        }
    }
}
