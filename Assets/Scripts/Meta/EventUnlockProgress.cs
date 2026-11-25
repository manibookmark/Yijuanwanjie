using System;
using WanjieScroll.Core;

namespace WanjieScroll.Meta
{
    public enum EventUnlockRunConclusion { None = 0, Victory = 1, Defeat = 2 }
    /// <summary>
    /// Single owner of permanent side-event gates. The existing accepted-Run
    /// ledger owns cumulative metric credits. This owner commits its gate and
    /// that ledger with one strict callback; callbacks cannot reenter this owner.
    /// Mana's Runtime metric is one accepted Run and is promoted only at a typed
    /// whole-Run conclusion; its immutable proof identifies that original Run.
    /// Hire uses accepted lifetime actual crawler reactions, explicitly replacing
    /// native attempted calls. Its terminal promotion retains the contributing Run.
    /// No card-unlock flag, display name or node completion is an event gate.
    /// </summary>
    public sealed class EventUnlockProgress
    {
        public const int ChickenShopRequiredFood = 5;
        public const int ManaOfferingRequiredPlays = 5;
        public const int DuplicateOfferingRequiredPlays = 25;
        public const int GunForHireRequiredTriggers = 200;
        private static readonly string[] DuplicatorIds =
        {
            "legacy.card.common.8b1aef56", "legacy.card.xiuxian.d9894e05"
        };
        private static readonly string[] EmptyTomeIds =
        {
            "legacy.card.common.c0191ee0", "legacy.card.common.5f916d2a",
            "legacy.card.common.72bd90af", "legacy.card.common.69a654a6"
        };
        public bool ChickenShopUnlocked { get; private set; }
        public bool ManaOfferingUnlocked { get; private set; }
        public string ManaOfferingQualifyingRunId { get; private set; } = string.Empty;
        public bool DuplicateOfferingUnlocked { get; private set; }
        public string DuplicateOfferingQualifyingRunId { get; private set; } = string.Empty;
        public bool EvoOfferingUnlocked { get; private set; }
        public EvoOfferingRunProof EvoOfferingProof { get; private set; }
        public bool GunForHireUnlocked { get; private set; }
        public string GunForHireQualifyingRunId { get; private set; } = string.Empty;
        public bool IsCommitting { get; private set; }

        public bool IsChickenShopEligible() => ChickenShopUnlocked;
        public bool IsChickenShopPending(CardUnlockFactsSnapshot projectedGlobalFacts)
            => !ChickenShopUnlocked && CountFood(projectedGlobalFacts) >= ChickenShopRequiredFood;
        public bool IsManaOfferingEligible() => ManaOfferingUnlocked;
        public bool IsManaOfferingPending(CardUnlockFactsSnapshot cumulativeRunFacts)
            => !ManaOfferingUnlocked && CountManaOfferingPlays(cumulativeRunFacts) >= ManaOfferingRequiredPlays;
        public ManaOfferingEligibility QueryManaOfferingEligibility()
            => new ManaOfferingEligibility(ManaOfferingUnlocked, ManaOfferingQualifyingRunId);
        public bool IsDuplicateOfferingPending(CardUnlockFactsSnapshot cumulativeRunFacts)
            => !DuplicateOfferingUnlocked && CountDuplicateOfferingPlays(cumulativeRunFacts) >= DuplicateOfferingRequiredPlays;
        public DuplicateOfferingEligibility QueryDuplicateOfferingEligibility()
            => new DuplicateOfferingEligibility(DuplicateOfferingUnlocked, DuplicateOfferingQualifyingRunId);
        public EvoOfferingEligibility QueryEvoOfferingEligibility()
            => new EvoOfferingEligibility(EvoOfferingUnlocked, EvoOfferingProof);
        public GunForHireEligibility QueryGunForHireEligibility()
            => new GunForHireEligibility(GunForHireUnlocked, GunForHireQualifyingRunId);
        public bool IsGunForHirePending(CardUnlockFactsSnapshot projectedGlobalFacts)
            => !GunForHireUnlocked && CountCrawlerActions(projectedGlobalFacts) >= GunForHireRequiredTriggers;

        public EventUnlockProgressSnapshot Capture()
            => new EventUnlockProgressSnapshot(ChickenShopUnlocked, ManaOfferingUnlocked, ManaOfferingQualifyingRunId,
                DuplicateOfferingUnlocked, DuplicateOfferingQualifyingRunId, EvoOfferingUnlocked, EvoOfferingProof,
                GunForHireUnlocked, GunForHireQualifyingRunId);

        public void Restore(EventUnlockProgressSnapshot snapshot)
        {
            if (IsCommitting) throw new InvalidOperationException("Event facts are already committing.");
            var validated = new ManaOfferingEligibility(snapshot.ManaOfferingUnlocked, snapshot.ManaOfferingQualifyingRunId);
            var duplicate = new DuplicateOfferingEligibility(snapshot.DuplicateOfferingUnlocked, snapshot.DuplicateOfferingQualifyingRunId);
            var evo = new EvoOfferingEligibility(snapshot.EvoOfferingUnlocked, snapshot.EvoOfferingProof);
            var hire = new GunForHireEligibility(snapshot.GunForHireUnlocked, snapshot.GunForHireQualifyingRunId);
            ChickenShopUnlocked = snapshot.ChickenShopUnlocked;
            ManaOfferingUnlocked = validated.IsUnlocked;
            ManaOfferingQualifyingRunId = validated.QualifyingRunId;
            DuplicateOfferingUnlocked = duplicate.IsUnlocked;
            DuplicateOfferingQualifyingRunId = duplicate.QualifyingRunId;
            EvoOfferingUnlocked = evo.IsUnlocked;
            EvoOfferingProof = evo.Proof;
            GunForHireUnlocked = hire.IsUnlocked;
            GunForHireQualifyingRunId = hire.QualifyingRunId;
        }

        public bool TryCommitRunFacts(MetaProgression account, string runId,
            CardUnlockFactsSnapshot cumulativeFacts, Action persist, out string error)
        {
            if (string.IsNullOrWhiteSpace(runId) || cumulativeFacts == null)
            { error = "An event fact commit requires a real cumulative Run and stable Run ID."; return false; }
            return TryCommit(account, runId, cumulativeFacts, false, persist, out error);
        }

        /// <summary>Only a typed whole-Run conclusion promotes a threshold to a permanent event gate.</summary>
        public bool TryCommitRunConclusion(MetaProgression account, string runId,
            CardUnlockFactsSnapshot cumulativeFacts, EventUnlockRunConclusion conclusion,
            Action persist, out string error)
            => TryCommitRunConclusion(account, runId, cumulativeFacts, conclusion, persist, out error, null);

        public bool TryCommitRunConclusion(MetaProgression account, string runId,
            CardUnlockFactsSnapshot cumulativeFacts, EventUnlockRunConclusion conclusion,
            Action persist, out string error, EvoOfferingRunProof evoProof)
        {
            if (conclusion != EventUnlockRunConclusion.Victory && conclusion != EventUnlockRunConclusion.Defeat)
            { error = "Permanent event unlock requires a verified whole-Run victory or defeat."; return false; }
            if (string.IsNullOrWhiteSpace(runId) || cumulativeFacts == null)
            { error = "Run conclusion requires cumulative facts and a stable Run ID."; return false; }
            if (evoProof != null && evoProof.RunId != runId)
            { error = "Evo floor proof belongs to a different Run."; return false; }
            return TryCommit(account, runId, cumulativeFacts, true, persist, out error, evoProof);
        }

        /// <summary>Level-only callers can commit their existing permanent ledger without inventing a Run.</summary>
        public bool TryCommitExistingFacts(MetaProgression account, Action persist, out string error)
            => TryCommit(account, null, null, false, persist, out error);

        private bool TryCommit(MetaProgression account, string runId,
            CardUnlockFactsSnapshot cumulativeFacts, bool promoteTerminalGate, Action persist, out string error,
            EvoOfferingRunProof evoProof = null)
        {
            error = null;
            if (IsCommitting) { error = "Event facts are already committing."; return false; }
            if (account == null || !ReferenceEquals(account.EventUnlocks, this) || persist == null)
            { error = "Event facts require their explicit account owner and strict persistence callback."; return false; }
            CardUnlockProgressSnapshot beforeFacts = account.CardUnlocks.Capture();
            EventUnlockProgressSnapshot beforeGates = Capture();
            IsCommitting = true;
            try
            {
                if (cumulativeFacts != null) account.CardUnlocks.RecordAcceptedRun(runId, cumulativeFacts);
                if (promoteTerminalGate)
                {
                    ChickenShopUnlocked |= CountFood(account.CardUnlocks.LifetimeFacts) >= ChickenShopRequiredFood;
                    // Runtime=1 is one cumulative Run, never lifetime 3+2.
                    // The source rewardType6 promotion is only the typed whole-Run boundary.
                    if (!ManaOfferingUnlocked && cumulativeFacts != null &&
                        CountManaOfferingPlays(cumulativeFacts) >= ManaOfferingRequiredPlays)
                    {
                        ManaOfferingUnlocked = true;
                        ManaOfferingQualifyingRunId = runId;
                    }
                    if (!DuplicateOfferingUnlocked && cumulativeFacts != null &&
                        CountDuplicateOfferingPlays(cumulativeFacts) >= DuplicateOfferingRequiredPlays)
                    {
                        var permission = new DuplicateOfferingEligibility(true, runId);
                        DuplicateOfferingUnlocked = true;
                        DuplicateOfferingQualifyingRunId = permission.QualifyingRunId;
                    }
                    if (!EvoOfferingUnlocked && evoProof != null)
                    {
                        EvoOfferingUnlocked = true;
                        EvoOfferingProof = evoProof;
                    }
                    if (!GunForHireUnlocked && cumulativeFacts != null && CountCrawlerActions(cumulativeFacts) > 0 &&
                        CountAcceptedCrawlerActions(account.CardUnlocks.Capture()) >= GunForHireRequiredTriggers)
                    {
                        var permission = new GunForHireEligibility(true, runId);
                        GunForHireUnlocked = true;
                        GunForHireQualifyingRunId = permission.QualifyingRunId;
                    }
                }
                persist();
                return true;
            }
            catch (Exception exception)
            {
                account.CardUnlocks.Restore(beforeFacts);
                ChickenShopUnlocked = beforeGates.ChickenShopUnlocked;
                ManaOfferingUnlocked = beforeGates.ManaOfferingUnlocked;
                ManaOfferingQualifyingRunId = beforeGates.ManaOfferingQualifyingRunId ?? string.Empty;
                DuplicateOfferingUnlocked = beforeGates.DuplicateOfferingUnlocked;
                DuplicateOfferingQualifyingRunId = beforeGates.DuplicateOfferingQualifyingRunId ?? string.Empty;
                EvoOfferingUnlocked = beforeGates.EvoOfferingUnlocked;
                EvoOfferingProof = beforeGates.EvoOfferingProof;
                GunForHireUnlocked = beforeGates.GunForHireUnlocked;
                GunForHireQualifyingRunId = beforeGates.GunForHireQualifyingRunId ?? string.Empty;
                error = "Event fact persistence failed: " + exception.Message;
                return false;
            }
            finally { IsCommitting = false; }
        }

        public static int CountFood(CardUnlockFactsSnapshot facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            foreach (CardUnlockCount count in facts.Counts)
                if (count.Metric == CardUnlockMetric.FoodCollected) return count.Count;
            return 0;
        }

        public static int CountCrawlerActions(CardUnlockFactsSnapshot facts)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            foreach (CardUnlockCount count in facts.Counts)
                if (count.Metric == CardUnlockMetric.CrawlerActionsTriggered) return count.Count;
            return 0;
        }

        /// <summary>Only this metric's accepted Run watermarks can prove the new cumulative gate.</summary>
        public static int CountAcceptedCrawlerActions(CardUnlockProgressSnapshot progress)
        {
            long total = 0;
            foreach (CardUnlockRunCheckpoint run in progress.AcceptedRuns) total += CountCrawlerActions(run.Facts);
            return (int)Math.Min(int.MaxValue, total);
        }

        /// <summary>Counts the confirmed four EmptyTome stable IDs in one authority snapshot.</summary>
        public static int CountManaOfferingPlays(CardUnlockFactsSnapshot facts)
            => CountGroupPlays(facts, EmptyTomeIds);

        /// <summary>Both explicitly confirmed Duplicator tiers count within this one Run, never across Runs.</summary>
        public static int CountDuplicateOfferingPlays(CardUnlockFactsSnapshot facts)
            => CountGroupPlays(facts, DuplicatorIds);

        private static int CountGroupPlays(CardUnlockFactsSnapshot facts, string[] ids)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            long total = 0;
            foreach (CardUnlockCount count in facts.Counts)
            {
                if (count.Metric != CardUnlockMetric.CardPlayed) continue;
                foreach (string id in ids)
                {
                    if (!string.Equals(id, count.SourceContentId, StringComparison.OrdinalIgnoreCase)) continue;
                    total += count.Count;
                    break;
                }
            }
            return (int)Math.Min(int.MaxValue, total);
        }
    }

    public readonly struct EventUnlockProgressSnapshot
    {
        public bool ChickenShopUnlocked { get; }
        public bool ManaOfferingUnlocked { get; }
        public string ManaOfferingQualifyingRunId { get; }
        public bool DuplicateOfferingUnlocked { get; }
        public string DuplicateOfferingQualifyingRunId { get; }
        public bool EvoOfferingUnlocked { get; }
        public EvoOfferingRunProof EvoOfferingProof { get; }
        public bool GunForHireUnlocked { get; }
        public string GunForHireQualifyingRunId { get; }
        public EventUnlockProgressSnapshot(bool chickenShopUnlocked, bool manaOfferingUnlocked = false,
            string manaOfferingQualifyingRunId = null, bool duplicateOfferingUnlocked = false,
            string duplicateOfferingQualifyingRunId = null, bool evoOfferingUnlocked = false,
            EvoOfferingRunProof evoOfferingProof = null, bool gunForHireUnlocked = false,
            string gunForHireQualifyingRunId = null)
        {
            var qualification = new ManaOfferingEligibility(manaOfferingUnlocked, manaOfferingQualifyingRunId);
            var duplicate = new DuplicateOfferingEligibility(duplicateOfferingUnlocked, duplicateOfferingQualifyingRunId);
            var evo = new EvoOfferingEligibility(evoOfferingUnlocked, evoOfferingProof);
            var hire = new GunForHireEligibility(gunForHireUnlocked, gunForHireQualifyingRunId);
            ChickenShopUnlocked = chickenShopUnlocked;
            ManaOfferingUnlocked = qualification.IsUnlocked;
            ManaOfferingQualifyingRunId = qualification.QualifyingRunId;
            DuplicateOfferingUnlocked = duplicate.IsUnlocked;
            DuplicateOfferingQualifyingRunId = duplicate.QualifyingRunId;
            EvoOfferingUnlocked = evo.IsUnlocked;
            EvoOfferingProof = evo.Proof;
            GunForHireUnlocked = hire.IsUnlocked;
            GunForHireQualifyingRunId = hire.QualifyingRunId;
        }
    }
}
