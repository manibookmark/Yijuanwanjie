using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Completes a production Run against an explicitly supplied account. It
    /// commits automatic rewards, first-clear records, separate finale-floor and
    /// Dungeon bonuses and the frozen result. Collected Run jade is already earned and is
    /// never credited again here. No storage, scene or presentation is touched.
    /// </summary>
    public static class DungeonRunSettlement
    {
        /// <summary>Accept this real Run's facts once; only a frozen whole-Run
        /// conclusion promotes the pending permanent side-event achievement.</summary>
        public static bool TryCommitFactsAndPersist(DungeonRunSession run, MetaProgression account,
            Action persist, out string error)
        {
            if (run == null || account == null || persist == null)
            { error = "Run facts require a real Run, account and strict save callback."; return false; }
            if (account.ExperienceCollections.IsCommitting || account.EventUnlocks.IsCommitting)
            { error = "Account facts are already committing."; return false; }
            CardUnlockAcceptance accepted = CardUnlockAcceptance.FromRun(run);
            // XP is the outer account transaction; the existing event owner
            // still owns CardUnlock watermark/gate rollback. A rejected inner
            // commit must throw so the outer watermark is restored as well.
            Action commitEventsAndPersist = () =>
            {
                bool saved = accepted.Conclusion == EventUnlockRunConclusion.None
                    ? account.EventUnlocks.TryCommitRunFacts(account, accepted.RunId, accepted.Facts, persist, out string eventError)
                    : account.EventUnlocks.TryCommitRunConclusion(account, accepted.RunId, accepted.Facts,
                        accepted.Conclusion, persist, out eventError, accepted.EvoProof);
                if (!saved) throw new InvalidOperationException(eventError);
            };
            return accepted.Conclusion == EventUnlockRunConclusion.None
                ? account.ExperienceCollections.TryCommitRunFacts(accepted.RunId, run.ExperienceCollections.RunTotals,
                    commitEventsAndPersist, out error)
                : account.ExperienceCollections.TryCommitRunConclusion(accepted.RunId, run.ExperienceCollections.RunTotals,
                    accepted.Conclusion, commitEventsAndPersist, out error);
        }

        public static bool TrySettleVictory(DungeonRunSession run, MetaProgression account,
            out DungeonRunResultSnapshot result, out string error, Action persist = null)
            => TrySettleVictory(run, account, null, null, out result, out error, persist);

        public static bool TrySettleVictory(DungeonRunSession run, MetaProgression account,
            DungeonCompletionUnlockCatalog unlocks, Func<string, string> crawlerName,
            out DungeonRunResultSnapshot result, out string error, Action persist = null)
        {
            result = null;
            error = null;
            if (run == null || account == null) { error = "Settlement requires a Run and an explicit account."; return false; }
            if (account.ExperienceCollections.IsCommitting || account.EventUnlocks.IsCommitting)
            { error = "Account facts are already committing."; return false; }
            // Source qualification phases belong to the Run owner. Reject before
            // account rewards, first-clear records, bonus callbacks or strict save.
            if (!run.SideEvents.TryValidateVictoryReadiness(out error)) return false;
            if (run.TryGetConclusion(out result))
            {
                if (result.Status == DungeonRunResultStatus.Victory)
                {
                    try
                    {
                        if (persist == null) return true; // Preserve the explicit legacy memory-only API.
                        return TryCommitFactsAndPersist(run, account, persist, out error);
                    }
                    catch (Exception exception) { error = "Victory persistence failed: " + exception.Message; return false; }
                }
                error = "The Run already concluded as a defeat.";
                return false;
            }
            if (run.CurrentFloor.ActiveEncounter.HasValue ||
                !string.IsNullOrEmpty(run.CurrentFloor.ActiveInteractionNodeId) ||
                run.Interactions.HasPendingChoice || run.CurrentFloor.TryGetPendingVictorySealReward(out _))
            { error = "Settlement cannot bypass an active encounter or pending reward transaction."; return false; }
            LevelNode finale = null;
            foreach (LevelNode node in run.CurrentFloor.Profile.nodes)
                if (node != null && node.nodeType == LevelNodeType.Finale &&
                    run.CurrentFloor.GetNodeState(node.contentId) == LevelNodeViewState.Completed)
                {
                    if (finale != null) { error = "More than one Finale is completed."; return false; }
                    finale = node;
                }
            if (finale == null || !run.HasPlayerState || run.CurrentHealth <= 0)
            { error = "Victory settlement requires a completed Finale and the real surviving player state."; return false; }
            bool hasReturn = run.CurrentFloor.TryGetPendingReturn(out LevelEncounterReturn pending);
            if (hasReturn && (pending.Outcome != CombatOutcome.Victory || pending.NodeContentId != finale.contentId))
            { error = "The pending return does not belong to the completed Finale."; return false; }

            var before = new MetaCompletionAccountSnapshot(account);
            bool committed = false;
            try
            {
                unlocks ??= DungeonCompletionUnlockCatalog.LoadDefault();
                var grants = new LevelRewardGrantRules(account, crawlerName: crawlerName);
                IReadOnlyList<LevelRewardGrant> automatic = LevelRewardGrantRules.GetAutomaticGrants(finale);
                // Resolve every target and every unlock before changing either owner.
                IReadOnlyList<LevelRewardReceipt> direct = grants.Preview(automatic);
                bool newlyCleared = !account.IsLevelCleared(run.FirstFloorContentId);
                IReadOnlyList<DungeonRunRewardResult> rewards = unlocks.Resolve(run.FirstFloorContentId, newlyCleared, direct);
                // Check the combined credit before either bonus or any unlock
                // changes its owner. The two receipts remain separate in Run.
                checked
                {
                    int bonusCredit = (run.IsLevelBonusGranted ? 0 : run.LevelBonusBaseJade) +
                        (run.IsDungeonBonusGranted ? 0 : run.DungeonBonusBaseJade);
                    _ = account.lingyu + bonusCredit;
                    _ = account.totalLingyuEarned + bonusCredit;
                }
                grants.Apply(automatic);
                grants.RecordLevelCleared(run.FirstFloorContentId);
                var economy = new EconomySystem(runState: run.RunProgression, jadeSink: account.CreditLingyu);
                if (!run.TryConcludeVictoryWithBonus(amount => economy.Grant(amount, 0,
                        JadeSource.StageClear, affectedByGreed: false, recordInRun: false),
                        rewards, out result, out error, persist == null ? null : (Action)(() =>
                         {
                             if (!TryCommitFactsAndPersist(run, account, persist, out string eventError))
                                 throw new InvalidOperationException(eventError);
                         })))
                { before.Restore(account); return false; }
                committed = true;
                return true;
            }
            catch (Exception exception)
            {
                if (!committed) before.Restore(account);
                error = "Victory settlement failed: " + exception.Message;
                return false;
            }
        }

        public static bool TrySettleDefeat(DungeonRunSession run,
            out DungeonRunResultSnapshot result, out string error)
        {
            result = null;
            error = null;
            if (run == null) { error = "Missing Run."; return false; }
            if (run.TryGetConclusion(out result))
            {
                if (result.Status == DungeonRunResultStatus.Defeat) return true;
                error = "The Run already concluded as a victory.";
                return false;
            }
            if (run.CurrentHealth > 0 || !run.CurrentFloor.TryGetPendingReturn(out LevelEncounterReturn pending) ||
                pending.Outcome != CombatOutcome.Defeat)
            { error = "Only a committed gameplay defeat can be settled as defeat."; return false; }
            if (!run.TryConcludeDefeat(out result, out error)) return false;
            if (!run.CurrentFloor.TryAcknowledgeReturn(pending.NodeContentId))
            { error = "The settled defeat return could not be acknowledged."; return false; }
            return true;
        }
    }
}
