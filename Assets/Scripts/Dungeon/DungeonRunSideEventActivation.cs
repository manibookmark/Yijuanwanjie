using System;

namespace WanjieScroll.Dungeon
{
    public enum DungeonRunSideEventActivationResult { NothingDue, WaitingForSettlement, Activated, Failed }

    /// <summary>Consumes the sole source journal at a quiescent map boundary. No second
    /// phase ledger is kept; replay and retry always consult committed Run state.</summary>
    public static class DungeonRunSideEventActivation
    {
        public static DungeonRunSideEventActivationResult TryActivateReady(DungeonRunSession run,
            Action persist, out string error)
        {
            error = null;
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (!run.SideEvents.TryGetDuePhase(out var phase) || run.TryGetConclusion(out _))
                return DungeonRunSideEventActivationResult.NothingDue;
            if (run.IsRewardCommitInProgress || run.Crystals.HasPendingEvent || run.Interactions.HasPendingChoice ||
                run.Arcanas.HasPendingChoice || run.CardOfferings.HasPendingChoice)
                return DungeonRunSideEventActivationResult.WaitingForSettlement;
            try { run.CurrentFloor.RequireFloorExitReady(); }
            catch (InvalidOperationException) { return DungeonRunSideEventActivationResult.WaitingForSettlement; }
            if (run.SideEventContext == null)
            { error = "Source phase requires the admitted account and placement context."; return DungeonRunSideEventActivationResult.Failed; }
            return run.SideEvents.TryActivatePhase(phase.PhaseContentId, run.SideEventContext, out error, persist)
                ? DungeonRunSideEventActivationResult.Activated : DungeonRunSideEventActivationResult.Failed;
        }
    }
}
