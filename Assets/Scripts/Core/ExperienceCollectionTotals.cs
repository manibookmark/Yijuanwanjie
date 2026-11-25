using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Actual applied experience, independent of the spendable XP balance and
    /// reached level. PickupCrystal is the adjusted XP obtained from crystal
    /// hits, not the number of crystals; it is a subset of PlayerXPGathered.
    /// Zero awards are valid. Arithmetic is checked and never saturates.
    /// </summary>
    public readonly struct ExperienceCollectionTotals : IEquatable<ExperienceCollectionTotals>
    {
        public static ExperienceCollectionTotals Zero => default;
        public long PlayerXPGathered { get; }
        public long PickupCrystal { get; }

        public ExperienceCollectionTotals(long playerXPGathered, long pickupCrystal)
        {
            if (playerXPGathered < 0) throw new ArgumentOutOfRangeException(nameof(playerXPGathered));
            if (pickupCrystal < 0 || pickupCrystal > playerXPGathered)
                throw new ArgumentOutOfRangeException(nameof(pickupCrystal),
                    "Crystal experience must be a nonnegative subset of actual player experience.");
            PlayerXPGathered = playerXPGathered;
            PickupCrystal = pickupCrystal;
        }

        public ExperienceCollectionTotals Add(ExperienceCollectionTotals delta)
            => new ExperienceCollectionTotals(checked(PlayerXPGathered + delta.PlayerXPGathered),
                checked(PickupCrystal + delta.PickupCrystal));

        /// <summary>New credits cannot relabel previously accepted ordinary XP as crystal XP.</summary>
        public ExperienceCollectionTotals Subtract(ExperienceCollectionTotals previous)
        {
            if (PlayerXPGathered < previous.PlayerXPGathered || PickupCrystal < previous.PickupCrystal)
                throw new InvalidOperationException("Accepted experience cannot decrease.");
            long player = PlayerXPGathered - previous.PlayerXPGathered;
            long crystal = PickupCrystal - previous.PickupCrystal;
            if (crystal > player)
                throw new InvalidOperationException("New crystal credits cannot exceed newly gathered player XP.");
            return new ExperienceCollectionTotals(player, crystal);
        }

        public bool Equals(ExperienceCollectionTotals other)
            => PlayerXPGathered == other.PlayerXPGathered && PickupCrystal == other.PickupCrystal;
        public override bool Equals(object obj) => obj is ExperienceCollectionTotals other && Equals(other);
        public override int GetHashCode()
            => unchecked((PlayerXPGathered.GetHashCode() * 397) ^ PickupCrystal.GetHashCode());
        public static bool operator ==(ExperienceCollectionTotals left, ExperienceCollectionTotals right)
            => left.Equals(right);
        public static bool operator !=(ExperienceCollectionTotals left, ExperienceCollectionTotals right)
            => !left.Equals(right);
    }
}
