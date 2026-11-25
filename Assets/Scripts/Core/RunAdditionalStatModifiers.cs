using System;

namespace WanjieScroll.Core
{
    /// <summary>Run-owned permanent deltas; encounter effects never become an entry baseline.</summary>
    public sealed class RunAdditionalStatModifiers
    {
        public int AmountBonus { get; private set; }
        public int MagnetBonus { get; private set; }
        public int CurseBonus { get; private set; }

        public int AddAmount(int amount, int? currentValue = null, int capacity = 50)
        {
            int gain = Accepted(amount, currentValue ?? AmountBonus, capacity, AmountBonus);
            AmountBonus += gain;
            return gain;
        }

        public int AddMagnet(int amount, int? currentValue = null, int capacity = 4)
        {
            int gain = Accepted(amount, currentValue ?? MagnetBonus, capacity, MagnetBonus);
            MagnetBonus += gain;
            return gain;
        }

        public int AddCurse(int amount)
        {
            int gain = Math.Max(0, (int)Math.Min(Math.Max(0L, amount), (long)int.MaxValue - CurseBonus));
            CurseBonus += gain;
            return gain;
        }

        /// <summary>Full source Base grants bypass ordinary pickup capacities.
        /// Exact integer overflow rejects before mutation; current encounter values
        /// and the captured native float channels are not additional Run ledgers.</summary>
        public int AddSourceAmount(int amount)
        {
            int next = AddExactSource(AmountBonus, amount);
            AmountBonus = next;
            return amount;
        }

        public int AddSourceMagnet(int amount)
        {
            int next = AddExactSource(MagnetBonus, amount);
            MagnetBonus = next;
            return amount;
        }

        public int AddSourceCurse(int amount)
        {
            int next = AddExactSource(CurseBonus, amount);
            CurseBonus = next;
            return amount;
        }

        private static int AddExactSource(int owned, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            return checked(owned + amount);
        }

        // currentValue is the authoritative current projection including existing
        // deltas; it is never an invitation to recapture temporary combat stats.
        private static int Accepted(int amount, int currentValue, int capacity, int owned)
        {
            if (currentValue < 0 || capacity < 0) throw new ArgumentOutOfRangeException(nameof(currentValue));
            return (int)Math.Min(Math.Max(0L, amount),
                Math.Min(Math.Max(0L, (long)capacity - currentValue), (long)int.MaxValue - owned));
        }

        public int ProjectAmount(int baseline) => Project(baseline, AmountBonus);
        public int ProjectMagnet(int baseline) => Project(baseline, MagnetBonus);
        public int ProjectCurse(int baseline) => Project(baseline, CurseBonus);
        private static int Project(int baseline, int delta)
            => (int)Math.Max(0L, Math.Min(int.MaxValue, (long)baseline + delta));

        public RunAdditionalStatSnapshot Capture() => new RunAdditionalStatSnapshot(AmountBonus, MagnetBonus, CurseBonus);

        public void Restore(RunAdditionalStatSnapshot snapshot)
        {
            AmountBonus = snapshot.AmountBonus;
            MagnetBonus = snapshot.MagnetBonus;
            CurseBonus = snapshot.CurseBonus;
        }
    }

    public readonly struct RunAdditionalStatSnapshot
    {
        public int AmountBonus { get; }
        public int MagnetBonus { get; }
        public int CurseBonus { get; }
        public RunAdditionalStatSnapshot(int amountBonus, int magnetBonus, int curseBonus)
        {
            if (amountBonus < 0 || magnetBonus < 0 || curseBonus < 0)
                throw new ArgumentOutOfRangeException(nameof(amountBonus), "Run stat deltas cannot be negative.");
            AmountBonus = amountBonus; MagnetBonus = magnetBonus; CurseBonus = curseBonus;
        }
    }
}
