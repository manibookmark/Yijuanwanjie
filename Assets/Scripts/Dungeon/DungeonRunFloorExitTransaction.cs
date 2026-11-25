using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Frozen acknowledgement; presentation cannot re-award or advance.</summary>
    public sealed class DungeonRunFloorExitResult
    {
        public string FloorContentId { get; }
        public string NodeContentId { get; }
        public string NextFloorContentId { get; }
        public IReadOnlyList<LevelInteractionReceipt> Receipts { get; }
        public bool NewlyCommitted { get; }
        internal DungeonRunFloorExitResult(string floorId, string nodeId, string nextId,
            IEnumerable<LevelInteractionReceipt> receipts, bool newlyCommitted)
        {
            FloorContentId = floorId; NodeContentId = nodeId; NextFloorContentId = nextId;
            Receipts = new List<LevelInteractionReceipt>(receipts).AsReadOnly();
            NewlyCommitted = newlyCommitted;
        }
    }

    /// <summary>
    /// One Run-owned atomic Exit boundary, shared by map and headless clients.
    /// Plans the next floor before any payment, commits its fixed Exit reward,
    /// completion, history and bindings together, and restores all participants
    /// when persistence fails. No animation, scene load or account type is known.
    /// One unsettled fixed Jade payment and multiple Level-owned Run effects can
    /// be staged here. Wallets checkpoint each Credit, not an arbitrary batch.
    /// Already-settled receipts can be reused without paying again. Other
    /// external effects must already have a receipt from their authoritative owner;
    /// this boundary cannot invent a card/relic/choice resolver or skip a reward.
    /// </summary>
    public sealed class DungeonRunFloorExitTransaction
    {
        private readonly DungeonRunSession _run;
        private readonly DungeonRunFloorProgression _floors;
        private bool _committing;
        private IDungeonRunInteractionWallet _unrestoredWallet;
        private int _unrestoredBalance;
        internal bool IsCommitting => _committing || _unrestoredWallet != null;
        internal DungeonRunFloorExitTransaction(DungeonRunSession run, DungeonRunFloorProgression floors)
        { _run = run; _floors = floors; }

        /// <summary>
        /// expectedFloorId makes retries unambiguous even when different floors
        /// use the same local node ID. Persist must throw on failure; a logging
        /// callback that swallows save errors cannot provide atomic persistence.
        /// The wallet rolls back account fields; this owner rolls back Run income.
        /// Persist must be an atomic write (throw means nothing was committed).
        /// A broken wallet rollback is reported as false, never thrown from finally;
        /// subsequent commits are blocked until that same participant is restored.
        /// </summary>
        public bool TryCommit(string expectedFloorId, string nodeId,
            Func<string, LevelRunProfile> resolver, IDungeonRunInteractionWallet wallet,
            out DungeonRunFloorExitResult result, out string error, Action persist = null)
        {
            result = null; error = null;
            if (_committing || _run.Interactions.IsCommitting || _run.GemChests.IsCommitting || _run.DoubleKeyPayments.IsCommitting ||
                _run.VictorySealRewards.IsCommitting || _run.CardConversions.IsCommitting || _run.SideEvents.IsCommitting || _run.Crystals.IsCommitting || _run.Crystals.HasPendingEvent || (_run.Arcanas.IsCommitting || (_run.ChickenOfferings.IsCommitting || _run.ManaStatues.IsCommitting || _run.AbandonedCarts.IsCommitting) || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy)))
            { error = "Run reward transaction is already committing."; return false; }
            _committing = true;
            RunProgressionState run = _run.RunProgression;
            RunProgressionSnapshot counters = run.Capture();
            int balance = 0;
            bool creditAttempted = false;
            try
            {
                if (_unrestoredWallet != null)
                {
                    if (!TryRestoreWallet(_unrestoredWallet, _unrestoredBalance, out string recoveryError))
                        throw new InvalidOperationException("Previous Exit wallet rollback is incomplete: " + recoveryError);
                    _unrestoredWallet = null;
                }
                if (string.IsNullOrWhiteSpace(expectedFloorId) || string.IsNullOrWhiteSpace(nodeId))
                    throw new InvalidOperationException("Exit requires stable floor and node identities.");
                if (_floors.TryReadCompletedExit(expectedFloorId, nodeId, resolver, out result))
                    return true;
                if (!string.Equals(expectedFloorId, _run.CurrentFloorContentId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Exit request belongs to a different floor.");
                if (_run.TryGetConclusion(out _) || (_run.Interactions.HasPendingChoice || (_run.Arcanas.HasPendingChoice || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy))))
                    throw new InvalidOperationException("A concluded Run or pending choice cannot exit.");
                _run.CurrentFloor.RequireFloorExitReady(nodeId);
                LevelNode node = _run.CurrentFloor.FindNode(nodeId);
                if (node?.nodeType != LevelNodeType.Exit)
                    throw new InvalidOperationException("Requested node is not an Exit.");
                List<string> validation = _run.CurrentFloor.Profile.Validate();
                if (validation.Count > 0) throw new InvalidOperationException(string.Join(" | ", validation));
                var effects = node.completionEffects ?? new List<LevelInteractionEffect>();
                int unpaidJade = 0;
                foreach (LevelInteractionEffect effect in effects)
                    if (effect.kind == LevelInteractionEffectKind.GrantJade &&
                        !_run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out _)) unpaidJade++;
                if (unpaidJade > 1)
                    throw new InvalidOperationException("Exit permits at most one unsettled GrantJade payment per transaction.");
                var byEffect = new Dictionary<string, LevelInteractionReceipt>(StringComparer.Ordinal);
                bool alreadyCompleted = _run.CurrentFloor.GetNodeState(nodeId) == LevelNodeViewState.Completed;
                if (!_floors.TryPrepare(resolver, out var prepared, out error, draft =>
                {
                    if (!alreadyCompleted && !draft.TryBeginInteraction(nodeId, out string beginError))
                        throw new InvalidOperationException(beginError);
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (LevelInteractionEffect effect in effects)
                    {
                        if (effect == null || string.IsNullOrWhiteSpace(effect.effectId) || !ids.Add(effect.effectId))
                            throw new InvalidOperationException("Exit effects contain missing or duplicate identities.");
                        if (draft.TryGetInteractionReceipt(effect.effectId, out var prior))
                        {
                            if (prior.Kind != effect.kind)
                                throw new InvalidOperationException("Exit receipt kind no longer matches its contract.");
                            byEffect.Add(effect.effectId, prior);
                            continue;
                        }
                        if (alreadyCompleted)
                            throw new InvalidOperationException("Completed Exit is missing its reward receipt.");
                        if (effect.kind == LevelInteractionEffectKind.GrantJade)
                        {
                            if (effect.amount <= 0 || (effect.amountMax != 0 && effect.amountMax != effect.amount))
                                throw new InvalidOperationException("Unsettled random Exit Jade needs its own frozen reward contract.");
                            continue;
                        }
                        if (!draft.TryApplyRunEffect(nodeId, effect, out var receipt, out string effectError))
                            throw new InvalidOperationException("Exit effect requires its authoritative external settlement: " + effectError);
                        byEffect.Add(effect.effectId, receipt);
                    }
                })) return false;
                var receipts = new List<LevelInteractionReceipt>();
                foreach (LevelInteractionEffect effect in effects)
                {
                    if (byEffect.TryGetValue(effect.effectId, out var prior))
                    { receipts.Add(prior); continue; }
                    if (wallet == null) throw new InvalidOperationException("Exit requires its reward wallet.");
                    int beforeBalance = wallet.Balance, beforeJade = run.RunJadeEarned;
                    if (!creditAttempted) balance = beforeBalance;
                    JadeGainResult expected = wallet.Preview(effect.amount);
                    creditAttempted = true;
                    JadeGainResult actual = wallet.Credit(effect.amount);
                    if (expected.BaseJade != effect.amount || actual.BaseJade != effect.amount ||
                        actual.FinalJade != expected.FinalJade || actual.FinalJade < 0 ||
                        (long)wallet.Balance - beforeBalance != actual.FinalJade ||
                        (long)run.RunJadeEarned - beforeJade != actual.FinalJade)
                        throw new InvalidOperationException("Exit wallet did not commit the exact reward to account and Run.");
                    if (!prepared.Completed.TryRecordExternalEffect(nodeId, effect, beforeBalance, wallet.Balance,
                            actual.FinalJade, out var receipt, out error))
                        throw new InvalidOperationException(error);
                    receipts.Add(receipt);
                }
                if (!alreadyCompleted && !prepared.Completed.TryCompleteInteraction(nodeId, out error))
                    throw new InvalidOperationException(error);
                var acknowledgement = new DungeonRunFloorExitResult(expectedFloorId,
                    nodeId, prepared.Next.Profile.contentId, receipts, true);
                _floors.Commit(prepared, persist);
                result = acknowledgement;
                return true;
            }
            catch (Exception exception)
            {
                result = null;
                error = "离层提交失败：" + exception.Message;
                try
                {
                    if (creditAttempted && !TryRestoreWallet(wallet, balance, out string rollbackError))
                    {
                        _unrestoredWallet = wallet;
                        _unrestoredBalance = balance;
                        error += "；钱包回滚未完成，禁止再次支付：" + rollbackError;
                    }
                }
                finally { run.Restore(counters); }
                return false;
            }
            finally { _committing = false; }
        }

        private static bool TryRestoreWallet(IDungeonRunInteractionWallet wallet, int balance, out string error)
        {
            error = null;
            try { wallet.RestoreBalance(balance); }
            catch (Exception exception) { error = exception.Message; }
            // Some sinks restore successfully and then throw while notifying observers.
            // Verification distinguishes that case from an account still holding credit.
            try
            {
                if (wallet.Balance == balance) return true;
                error = (error == null ? string.Empty : error + "; ") + "Wallet balance was not restored.";
            }
            catch (Exception exception) { error = (error ?? string.Empty) + "; " + exception.Message; }
            return false;
        }
    }
}
