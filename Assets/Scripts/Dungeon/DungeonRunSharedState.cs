using System;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Unique Run-owned floor modifiers and isolated floor-preparation snapshots.
    /// Scalar source grants stay here; Run progression, XP and child ledgers retain
    /// their own identities. No duplicate source Minimum state is introduced.</summary>
    internal sealed class DungeonRunSharedState
    {
        public DungeonRunSharedState(
            LevelExperienceRules rules,
            RunProgressionState runProgression,
            int currentLevel = 1,
            int currentExperience = 0,
            int baseManaBonus = 0,
            int baseArmorPerTurnBonus = 0,
            int mightBonus = 0,
            int xpFractionCarryUnits = 0,
            int handLimitBonus = 0,
            int areaBonus = 0,
            int rewardRerollsRemaining = 0)
        {
            CombatProgression = new CombatProgressionState(
                rules,
                currentLevel,
                currentExperience,
                xpFractionCarryUnits);
            RunProgression = runProgression ?? new RunProgressionState();
            BaseManaBonus = Math.Max(0, baseManaBonus);
            BaseArmorPerTurnBonus = Math.Max(0, baseArmorPerTurnBonus);
            MightBonus = Math.Max(0, mightBonus);
            HandLimitBonus = Math.Max(0, handLimitBonus);
            AreaBonus = Math.Max(0, areaBonus);
            RewardRerollsRemaining = Math.Max(
                0, rewardRerollsRemaining);
        }

        public CombatProgressionState CombatProgression { get; }
        public RunProgressionState RunProgression { get; }
        public int BaseManaBonus { get; private set; }
        public int BaseArmorPerTurnBonus { get; private set; }
        public int MightBonus { get; private set; }
        public int HandLimitBonus { get; private set; }
        public int AreaBonus { get; private set; }
        public int RewardRerollsRemaining { get; private set; }

        // Floor snapshot effects are prepared against an isolated copy. Only
        // these modifiers can be rebuilt by LevelRunSession; progression/XP
        // identities stay live and are never swapped during a floor transition.
        internal DungeonRunSharedState CloneForFloorPreparation()
        {
            var run = new RunProgressionState(RunProgression.CardPlayGrowth.Enabled);
            run.Restore(RunProgression.Capture());
            run.CardPlayGrowth.Restore(RunProgression.CardPlayGrowth.Capture());
            run.CardUnlockFacts.Restore(RunProgression.CardUnlockFacts.Capture());
            run.CrawlerDurationReserve.Restore(RunProgression.CrawlerDurationReserve.PendingBonus);
            run.AdditionalStats.Restore(RunProgression.AdditionalStats.Capture());
            run.RevivalReserve.Restore(RunProgression.RevivalReserve.Capture());
            return new DungeonRunSharedState(CombatProgression.Rules, run,
                CombatProgression.CurrentLevel, CombatProgression.CurrentExperience,
                BaseManaBonus, BaseArmorPerTurnBonus, MightBonus,
                CombatProgression.XpFractionCarryUnits, HandLimitBonus,
                AreaBonus, RewardRerollsRemaining);
        }

        internal void CopyFloorModifiersFrom(DungeonRunSharedState prepared)
        {
            // Copy snapshots into the existing owners; floor publication never
            // swaps their identities or replenishes a spent revival from entry.
            RunProgression.AdditionalStats.Restore(prepared.RunProgression.AdditionalStats.Capture());
            RunProgression.RevivalReserve.Restore(prepared.RunProgression.RevivalReserve.Capture());
            RestoreModifiers(prepared.CaptureModifiers());
        }

        /// <summary>Six shared scalar counters only; progression and its child owners stay installed.</summary>
        internal DungeonRunModifierSnapshot CaptureModifiers()
            => new DungeonRunModifierSnapshot(BaseManaBonus, BaseArmorPerTurnBonus,
                MightBonus, HandLimitBonus, AreaBonus, RewardRerollsRemaining);

        internal void RestoreModifiers(DungeonRunModifierSnapshot snapshot)
        {
            BaseManaBonus = snapshot.BaseManaBonus;
            BaseArmorPerTurnBonus = snapshot.BaseArmorPerTurnBonus;
            MightBonus = snapshot.MightBonus;
            HandLimitBonus = snapshot.HandLimitBonus;
            AreaBonus = snapshot.AreaBonus;
            RewardRerollsRemaining = snapshot.RewardRerollsRemaining;
        }

        /// <summary>Full source Base grants bypass ordinary saturation. Unrepresentable
        /// requests fail before the owned counter changes; explicit zero is lawful.</summary>
        public int AddSourceArea(int amount)
        {
            int next = AddExactSource(AreaBonus, amount);
            AreaBonus = next;
            return amount;
        }

        public int AddSourceMight(int amount)
        {
            int next = AddExactSource(MightBonus, amount);
            MightBonus = next;
            return amount;
        }

        public int AddSourceHandLimit(int amount)
        {
            int next = AddExactSource(HandLimitBonus, amount);
            HandLimitBonus = next;
            return amount;
        }

        public int AddSourceBaseArmorPerTurn(int amount)
        {
            int next = AddExactSource(BaseArmorPerTurnBonus, amount);
            BaseArmorPerTurnBonus = next;
            return amount;
        }

        private static int AddExactSource(int owned, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            return checked(owned + amount);
        }

        public int AddBaseMana(int amount)
        {
            int before = BaseManaBonus;
            BaseManaBonus = AddSaturated(BaseManaBonus, amount);
            return BaseManaBonus - before;
        }

        public int AddBaseArmorPerTurn(int amount)
        {
            int before = BaseArmorPerTurnBonus;
            BaseArmorPerTurnBonus = AddSaturated(BaseArmorPerTurnBonus, amount);
            return BaseArmorPerTurnBonus - before;
        }

        public int AddMight(int amount)
        {
            int before = MightBonus;
            MightBonus = AddSaturated(MightBonus, amount);
            return MightBonus - before;
        }

        public int AddHandLimit(int amount)
        {
            int before = HandLimitBonus;
            HandLimitBonus = AddSaturated(HandLimitBonus, amount);
            return HandLimitBonus - before;
        }

        public int AddArea(int amount)
        {
            int before = AreaBonus;
            AreaBonus = AddSaturated(AreaBonus, amount);
            return AreaBonus - before;
        }

        public int AddRewardRerolls(int amount)
        {
            int before = RewardRerollsRemaining;
            RewardRerollsRemaining = AddSaturated(
                RewardRerollsRemaining, amount);
            return RewardRerollsRemaining - before;
        }

        public bool TryConsumeRewardReroll()
        {
            if (RewardRerollsRemaining <= 0)
                return false;
            RewardRerollsRemaining--;
            return true;
        }

        private static int AddSaturated(int current, int amount)
            => (int)Math.Max(0L, Math.Min(int.MaxValue, (long)current + amount));
    }


    /// <summary>Exact existing shared modifiers; constructing invalid counters never mutates an owner.</summary>
    internal readonly struct DungeonRunModifierSnapshot
    {
        internal int BaseManaBonus { get; }
        internal int BaseArmorPerTurnBonus { get; }
        internal int MightBonus { get; }
        internal int HandLimitBonus { get; }
        internal int AreaBonus { get; }
        internal int RewardRerollsRemaining { get; }

        internal DungeonRunModifierSnapshot(int baseManaBonus, int baseArmorPerTurnBonus,
            int mightBonus, int handLimitBonus, int areaBonus, int rewardRerollsRemaining)
        {
            if (baseManaBonus < 0) throw new ArgumentOutOfRangeException(nameof(baseManaBonus));
            if (baseArmorPerTurnBonus < 0) throw new ArgumentOutOfRangeException(nameof(baseArmorPerTurnBonus));
            if (mightBonus < 0) throw new ArgumentOutOfRangeException(nameof(mightBonus));
            if (handLimitBonus < 0) throw new ArgumentOutOfRangeException(nameof(handLimitBonus));
            if (areaBonus < 0) throw new ArgumentOutOfRangeException(nameof(areaBonus));
            if (rewardRerollsRemaining < 0) throw new ArgumentOutOfRangeException(nameof(rewardRerollsRemaining));
            BaseManaBonus = baseManaBonus; BaseArmorPerTurnBonus = baseArmorPerTurnBonus;
            MightBonus = mightBonus; HandLimitBonus = handLimitBonus;
            AreaBonus = areaBonus; RewardRerollsRemaining = rewardRerollsRemaining;
        }
    }
}
