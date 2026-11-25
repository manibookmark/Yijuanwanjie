using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns the frozen Run rule, committed real-play count and the mana credited
    /// by that rule. Counts, paid cycles and this source's bonus change together;
    /// neither combat statistics nor a current account can reconstruct them.
    /// Call only at the successful card-transaction boundary. This owner does
    /// not mutate current mana, player baselines, progression XP or presentation.
    /// Capture/Restore also serve rollback and detached planning; restoring a
    /// checkpoint replaces state, it never adds the historical award again.
    /// </summary>
    public sealed class RunCardPlayGrowth
    {
        public const int RealPlaysPerCycle = 100;

        public bool Enabled { get; }
        public long TotalRealPlays { get; private set; }
        public int SettledCycles { get; private set; }
        public int BaseManaBonus { get; private set; }
        public int ProgressToNextCycle => (int)(TotalRealPlays % RealPlaysPerCycle);

        public RunCardPlayGrowth(bool enabled = false) { Enabled = enabled; }

        /// <summary>
        /// Projects only this source onto a caller-owned base value, without
        /// mutating the player or imposing a new minimum on existing consumers.
        /// A disabled/zero-bonus owner is identity even for a negative baseline.
        /// Consumers must supply a value not already containing this bonus.
        /// </summary>
        public int ProjectBaseMana(int baseMana)
            => (int)Math.Min(int.MaxValue, (long)baseMana + BaseManaBonus);

        /// <summary>
        /// Atomically credits an already committed batch. Negative counts and
        /// unrepresentable totals/bonuses throw without changing any field.
        /// Disabled Runs do not accumulate hidden progress. Persistence is the
        /// enclosing transaction's responsibility: Capture before staging and
        /// Restore if its atomic save fails; a thrown write is not proof that an
        /// arbitrary external sink did not already write data.
        /// </summary>
        public RunCardPlayGrowthReceipt CommitRealPlays(long count = 1)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (!Enabled || count == 0) return Receipt(0, 0, false);

            long total = checked(TotalRealPlays + count);
            int cycles = checked((int)(total / RealPlaysPerCycle));
            int delta = checked(cycles - SettledCycles);
            int bonus = checked(BaseManaBonus + delta);
            TotalRealPlays = total;
            SettledCycles = cycles;
            BaseManaBonus = bonus;
            return Receipt(count, delta, false);
        }

        public RunCardPlayGrowthSnapshot Capture()
            => new RunCardPlayGrowthSnapshot(Enabled, TotalRealPlays, SettledCycles, BaseManaBonus);

        /// <summary>
        /// Validates all fields before replacing state. An unpaid suffix is
        /// repaired only when independently saved cycles and bonus agree and
        /// neither exceeds earned cycles. Mismatched awards are corruption, not
        /// permission to infer payment from total plays. The frozen Enabled flag
        /// must match this owner. Restoration may rewind for a failed transaction
        /// or planner; its receipt reports only newly repaired cycles, never the
        /// historical total, and must not replay historical presentation.
        /// </summary>
        public RunCardPlayGrowthReceipt Restore(RunCardPlayGrowthSnapshot snapshot)
        {
            if (snapshot.Enabled != Enabled)
                throw new InvalidOperationException("Run card-play growth configuration is frozen.");
            if (snapshot.TotalRealPlays < 0 || snapshot.SettledCycles < 0 || snapshot.BaseManaBonus < 0)
                throw new InvalidOperationException("Run card-play growth contains a negative field.");
            if (!Enabled && (snapshot.TotalRealPlays != 0 || snapshot.SettledCycles != 0 ||
                             snapshot.BaseManaBonus != 0))
                throw new InvalidOperationException("Disabled Run card-play growth cannot contain hidden progress.");
            long earned = snapshot.TotalRealPlays / RealPlaysPerCycle;
            if (earned > int.MaxValue)
                throw new OverflowException("Run card-play growth mana bonus cannot be represented.");
            if (snapshot.SettledCycles > earned || snapshot.BaseManaBonus != snapshot.SettledCycles)
                throw new InvalidOperationException("Run card-play growth paid cycles and source bonus are inconsistent.");

            int cycles = (int)earned;
            // Re-reading the same pending checkpoint into an already normalized
            // owner is idempotent, including its repair receipt.
            int repaired = TotalRealPlays == snapshot.TotalRealPlays &&
                           SettledCycles == cycles && BaseManaBonus == cycles
                ? 0 : cycles - snapshot.SettledCycles;
            TotalRealPlays = snapshot.TotalRealPlays;
            SettledCycles = cycles;
            BaseManaBonus = cycles;
            return Receipt(0, repaired, true);
        }

        private RunCardPlayGrowthReceipt Receipt(long plays, int cycles, bool restoring)
            => new RunCardPlayGrowthReceipt(plays, cycles, restoring, Capture());
    }

    /// <summary>Detached immutable checkpoint; callers cannot mutate the live owner.</summary>
    public readonly struct RunCardPlayGrowthSnapshot
    {
        public bool Enabled { get; }
        public long TotalRealPlays { get; }
        public int SettledCycles { get; }
        public int BaseManaBonus { get; }

        public RunCardPlayGrowthSnapshot(bool enabled, long totalRealPlays, int settledCycles, int baseManaBonus)
        {
            Enabled = enabled;
            TotalRealPlays = totalRealPlays;
            SettledCycles = settledCycles;
            BaseManaBonus = baseManaBonus;
        }
    }

    /// <summary>
    /// One committed rule result. For a commit, BaseManaDelta is safe to apply
    /// once to an existing ManaSystem without refilling current mana. A restore
    /// is a state replacement: use State.BaseManaBonus when rebuilding consumers,
    /// not historical deltas; IsRestore forbids replaying historic achievement FX.
    /// </summary>
    public readonly struct RunCardPlayGrowthReceipt
    {
        public long CommittedRealPlays { get; }
        public int SettledCyclesDelta { get; }
        public int BaseManaDelta => SettledCyclesDelta;
        public bool IsRestore { get; }
        public RunCardPlayGrowthSnapshot State { get; }

        internal RunCardPlayGrowthReceipt(long plays, int cycles, bool isRestore, RunCardPlayGrowthSnapshot state)
        { CommittedRealPlays = plays; SettledCyclesDelta = cycles; IsRestore = isRestore; State = state; }
    }
}
