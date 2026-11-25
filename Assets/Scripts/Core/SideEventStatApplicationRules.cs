using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum SideEventStatModifierMode { Minimum = 0, Base = 1, Current = 2 }

    /// <summary>A detached native SimpleStat value, not a second Run stat owner.
    /// Bounds are supplied by the owner after its starting-stat calculation.
    /// A raised Minimum can exceed Maximum; this is representable in the source.</summary>
    public readonly struct SideEventStatSnapshot
    {
        public float Current { get; }
        public float RunBase { get; }
        public float Minimum { get; }
        public float Maximum { get; }

        public SideEventStatSnapshot(float current, float runBase, float minimum, float maximum)
        {
            RequireFinite(current); RequireFinite(runBase);
            RequireFinite(minimum); RequireFinite(maximum);
            Current = current; RunBase = runBase; Minimum = minimum; Maximum = maximum;
        }

        private static void RequireFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Stat channels must be finite binary32 values.");
        }
    }

    public sealed class SideEventStatApplication
    {
        public SideEventCardStatOfferingReward Reward { get; }
        public SideEventStatModifierMode Mode { get; }
        public SideEventStatSnapshot Before { get; }
        public SideEventStatSnapshot After { get; }

        internal SideEventStatApplication(SideEventCardStatOfferingReward reward,
            SideEventStatModifierMode mode, SideEventStatSnapshot before, SideEventStatSnapshot after)
        { Reward = reward; Mode = mode; Before = before; After = after; }
    }

    /// <summary>
    /// Owns the captured eleven-stat default modes and ordered SimpleStat arithmetic.
    /// Nine Minimum stats retain full RunBase and Minimum gains even at current cap.
    /// Armor adds only RunBase; RevivalCharge adds only Current. GiveReward finally
    /// calls SetValue with the resulting current value, after the mode-specific write.
    /// SetValue checks the lower bound first and returns immediately when below it;
    /// it is not Math.Min(max, Math.Max(min, value)) when Minimum exceeds Maximum.
    /// Every addition and integer-to-float conversion is rounded to binary32.
    ///
    /// This pure preparation mutates neither Run owners nor the caller's snapshots.
    /// It does not execute native observer callbacks (including charge-to-life conversion),
    /// choose project caps, persist a payout, authorize a card source or grant account
    /// progress. A Run adapter must apply the prepared channels to their existing owners
    /// atomically and record its project revival/storage policies separately.
    /// </summary>
    public static class SideEventStatApplicationRules
    {
        public static SideEventStatModifierMode DefaultMode(int sourceStat)
        {
            switch (sourceStat)
            {
                case 2: return SideEventStatModifierMode.Base;
                case 13: return SideEventStatModifierMode.Current;
                case 0: case 1: case 4: case 5: case 6: case 7: case 10: case 11: case 17:
                    return SideEventStatModifierMode.Minimum;
                default: throw new ArgumentOutOfRangeException(nameof(sourceStat), "No captured offering default mode exists.");
            }
        }

        public static SideEventStatApplication Apply(SideEventCardStatOfferingReward reward,
            SideEventStatSnapshot before)
        {
            if (reward == null) throw new ArgumentNullException(nameof(reward));
            if (reward.RequestedAmount <= 0) throw new ArgumentOutOfRangeException(nameof(reward));
            var mode = DefaultMode(reward.SourceStat);
            float amount = Binary32(reward.RequestedAmount);
            float current = before.Current, runBase = before.RunBase, minimum = before.Minimum;
            if (mode == SideEventStatModifierMode.Minimum)
            {
                current = Add(current, amount);
                runBase = Add(runBase, amount);
                minimum = Add(minimum, amount);
                // AddRunValueModifier(Minimum) raises current to the new floor.
                current = Math.Max(current, minimum);
            }
            else if (mode == SideEventStatModifierMode.Base)
                runBase = Add(runBase, amount);
            else
                current = SetValue(Add(current, amount), minimum, before.Maximum);
            current = SetValue(current, minimum, before.Maximum); // GiveReward's final SetValue.
            return new SideEventStatApplication(reward, mode, before,
                new SideEventStatSnapshot(current, runBase, minimum, before.Maximum));
        }

        /// <summary>Repeated tuples apply in effect order, using the preceding result
        /// for that same stat. Missing input channels abort preparation without writes.</summary>
        public static IReadOnlyList<SideEventStatApplication> Prepare(
            SideEventCardStatOfferingResult rewards,
            IReadOnlyDictionary<int, SideEventStatSnapshot> initial)
        {
            if (rewards == null) throw new ArgumentNullException(nameof(rewards));
            if (initial == null) throw new ArgumentNullException(nameof(initial));
            var current = new Dictionary<int, SideEventStatSnapshot>();
            var applications = new List<SideEventStatApplication>();
            foreach (var reward in rewards.Rewards)
            {
                int stat = reward.SourceStat;
                if (!current.TryGetValue(stat, out var before) && !initial.TryGetValue(stat, out before))
                    throw new InvalidOperationException("Missing owned channels for native stat " + stat + ".");
                var applied = Apply(reward, before);
                current[stat] = applied.After;
                applications.Add(applied);
            }
            return Array.AsReadOnly(applications.ToArray());
        }

        private static float SetValue(float value, float minimum, float maximum)
            => value < minimum ? minimum : value > maximum ? maximum : value;

        private static float Add(float left, float right)
        {
            float result = Binary32(left + right);
            if (float.IsNaN(result) || float.IsInfinity(result))
                throw new OverflowException("Stat addition exceeded finite binary32 storage.");
            return result;
        }

        private static float Binary32(float value)
            => BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
    }
}
