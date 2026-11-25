using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;
using static WanjieScroll.Dungeon.DungeonRunGunForHireSaveCodec;

namespace WanjieScroll.Dungeon
{
    /// <summary>Single owner of frozen Hire choice/quote, dormant cache and durable
    /// buy/pity completion. It grants one Run physical card, never a permanent
    /// crawler. Explicit FCC subset and source-floor context are required; no
    /// production eligibility is inferred. Versioned deterministic project RNG
    /// preserves candidate→buy→pity call order, not the native generator's seed.
    /// Inventory, income and floor remain the existing Run owners. The codec
    /// owns closed wire; reference resolution is explicit at purchase only.</summary>
    public sealed class DungeonRunGunForHire
    {
        public const string SourceEventId = "event.xiyou.gun-for-hire.v1";
        private readonly DungeonRunSession _run;
        private GunForHireSaveData _state;
        private bool _committing;
        private IDungeonRunFlatCashOutWallet _unrestoredWallet;
        private int _unrestoredBalance;
        public bool IsCommitting => _committing || _unrestoredWallet != null;
        public bool HasPendingChoice => _state.entries.Any(e => e.active);
        public string PendingNodeContentId => _state.entries.FirstOrDefault(e => e.active)?.nodeId;
        internal DungeonRunGunForHire(DungeonRunSession run, string savedJson = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _state = savedJson == null ? CreateEmpty(run.RunId) : Read(savedJson);
            if (_state.runId != run.RunId)
            { if (_state.entries.Count != 0) throw Bad("Hire journal belongs to another Run."); _state.runId = run.RunId; }
        }
        public string CaptureCanonicalJson()
        { if (_unrestoredWallet != null) throw Bad("Hire wallet recovery is incomplete."); return Write(_state); }
        internal bool ReservesInstanceId(int id) => id > 0 && _state.entries.Any(e => e.grantedId == id ||
            e.openingRoster.Concat(e.rosterAfter).Any(c => RestoreCard(c).runtimeInstanceId == id));
        internal bool HasGeneratedInstanceId(int id) => id > 0 && _state.entries.Any(e => e.finished && e.disposition == 1 && e.grantedId == id);
        internal long ProvenJadeIncome => _state.entries.Where(e => e.finished && e.disposition == 2).Sum(e => (long)e.pityPrice);
        public bool TryInspect(string nodeId, out GunForHireOffer offer, out string error)
        {
            offer = null;
            try { Guard(nodeId); var effect = RequireEffect(_run.CurrentFloor, nodeId); var e = Find(nodeId) ?? throw Bad("Open with an explicit FCC context first.");
                RequireEntry(e, effect); RequireLive(e); offer = new GunForHireOffer(e); error = null; return true; }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        public bool TryOpen(string nodeId, GunForHirePreparedContext context, out GunForHireOffer offer, out string error, Action persist)
        {
            offer = null;
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = Find(nodeId);
                if (old != null)
                {
                    RequireEntry(old, effect); RequireLive(old);
                    if (old.active || old.finished) { offer = new GunForHireOffer(old); error = null; return true; }
                }
                RequirePersistence(persist); RequireContext(context, effect); RequireAvailable(floor, nodeId);
                if (floor.TryGetInteractionReceipt(effect.effectId, out _)) throw Bad("Hire floor receipt lacks its journal proof.");
                var draft = _run.CaptureInteractionDraft(); var roster = CaptureRoster(draft.Cards);
                bool keep = old != null && old.selectedIndex >= 0 && !IsOwned(old.candidates[old.selectedIndex], roster) &&
                    context.Candidates.Any(c => c.SourceAssetId == old.candidates[old.selectedIndex].sourceAssetId &&
                        c.Author.ContentId == RestoreCandidate(old.candidates[old.selectedIndex]).Author.ContentId &&
                        SameObject(c.Provenance, old.candidates[old.selectedIndex].provenance));
                var next = Clone(_state); var e = old == null ? null : next.entries.Single(v => v.floorId == old.floorId && v.nodeId == old.nodeId);
                if (!keep)
                {
                    int generation = old == null ? 0 : checked(old.generation + 1);
                    e = CreateEntry(nodeId, effect, context, roster, generation);
                    next.entries.RemoveAll(v => v.floorId == e.floorId && v.nodeId == nodeId); next.entries.Add(e);
                }
                else
                {
                    // A cached source choice does not re-query today's unlock gate.
                    // Preserve its original candidate pool, context and quotation.
                    // Current roster is a new checkpoint for the later exact buy/pity.
                    e.openingRoster = roster; e.forcedOpeningBefore = draft.ForcedOpeningCardId;
                    e.jadeBefore = e.jadeAfter = _run.RunProgression.RunJadeEarned;
                }
                e.active = true;
                Commit(next, floor, effect, () => Begin(floor, nodeId), persist);
                offer = new GunForHireOffer(e); error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        private GunForHireEntryData CreateEntry(string nodeId, LevelInteractionEffect effect, GunForHirePreparedContext context,
            List<GunForHireCardData> roster, int generation)
        {
            var e = new GunForHireEntryData { floorId = _run.CurrentFloorContentId, nodeId = nodeId, effectId = effect.effectId,
                effectFingerprint = EffectFingerprint(effect), sourceDocumentJson = context.Document.CanonicalJson,
                sourceFingerprint = context.Document.Fingerprint, sourceRootId = context.RootContentId,
                poolPolicyId = GunForHirePreparedContext.ExplicitSubsetPolicyId, generation = generation,
                eventAdmitted = context.EventAdmitted,
                completedSourceFloors = context.CompletedSourceFloorCount, candidates = context.Candidates.Select(CaptureCandidate).ToList(),
                openingRoster = roster, rosterAfter = new List<GunForHireCardData>(), selectedIndex = -1,
                jadeBefore = _run.RunProgression.RunJadeEarned, jadeAfter = _run.RunProgression.RunJadeEarned,
                forcedOpeningBefore = _run.CaptureInteractionDraft().ForcedOpeningCardId };
            var random = new SeededRandomSource(Seed(_run.RunId + "\n" + e.floorId + "\n" + e.effectId + "\n" + e.sourceFingerprint + "\n" + generation));
            var pool = e.candidates.Select((c, i) => new { c, i }).Where(v => v.c.admittedUnlock && !IsOwned(v.c, roster)).Select(v => v.i).ToArray();
            if (pool.Length > 0) e.selectedIndex = pool[random.Range(0, pool.Length)];
            var quote = SideEventGunForHirePriceRules.DrawQuote(GunForHirePreparedContext.RequireSpecification(context.Document, context.RootContentId),
                context.CompletedSourceFloorCount, random);
            e.rawBuyPrice = quote.RawBuyPrice; e.rawPityPrice = quote.RawPityPrice; e.buyPrice = quote.BuyPrice; e.pityPrice = quote.PityPrice;
            if (quote.BuyPrice < 0 || quote.PityPrice < 0) throw Bad("Source quotation is outside the supported nonnegative wallet domain.");
            return e;
        }
        public bool TryBuyAndComplete(string nodeId, IDungeonRunHireWallet wallet, ICardAuthorReferenceResolver resolver,
            out GunForHireReceipt receipt, out string error, Action persist)
            => TryComplete(nodeId, false, wallet, resolver, out receipt, out error, persist);
        public bool TryPityAndComplete(string nodeId, IDungeonRunFlatCashOutWallet wallet,
            out GunForHireReceipt receipt, out string error, Action persist)
            => TryComplete(nodeId, true, wallet, null, out receipt, out error, persist);
        private bool TryComplete(string nodeId, bool pity, IDungeonRunFlatCashOutWallet wallet, ICardAuthorReferenceResolver resolver,
            out GunForHireReceipt receipt, out string error, Action persist)
        {
            receipt = null; bool touched = false; int balance = 0;
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = Find(nodeId) ?? throw Bad("Open and persist Hire first.");
                RequireEntry(old, effect); RequireLive(old);
                if (old.finished)
                { if (old.disposition != (pity ? 2 : 1)) throw Bad("Hire completed through another branch."); receipt = ReadReceipt(floor, effect, old); error = null; return true; }
                RequirePersistence(persist);
                if (!old.active || wallet == null) throw Bad("An active Hire and explicit wallet are required.");
                if (!pity && (old.selectedIndex < 0 || !(wallet is IDungeonRunHireWallet) || resolver == null)) throw Bad("A saved FCC, debit capability and explicit author resolver are required.");
                var draft = _run.CaptureInteractionDraft(); var next = Clone(_state); var e = next.entries.Single(v => v.floorId == old.floorId && v.nodeId == nodeId);
                e.active = false; e.finished = true; e.disposition = pity ? 2 : 1;
                e.forcedOpeningAfter = draft.ForcedOpeningCardId;
                LevelInteractionReceipt applied = default;
                Commit(next, floor, effect, () =>
                {
                    string checkpoint = _run.SerializeForDeterministicComparison(); balance = wallet.Balance; touched = true;
                    if (balance < 0 || _run.SerializeForDeterministicComparison() != checkpoint) throw Bad("Wallet inspection changed the Run checkpoint.");
                    e.walletBefore = balance;
                    if (pity)
                    {
                        if (e.selectedIndex >= 0 && balance >= e.buyPrice) throw Bad("Pity is available only when the frozen FCC is unaffordable or absent.");
                        int target = checked(balance + e.pityPrice); int income = checked(e.jadeBefore + e.pityPrice);
                        RequireFlat(wallet.PreviewFlatCashOut(e.pityPrice), e.pityPrice);
                        if (wallet.Balance != balance || _run.SerializeForDeterministicComparison() != checkpoint) throw Bad("Pity preview changed Run or wallet.");
                        var gain = wallet.CreditFlatCashOut(e.pityPrice); RequireFlat(gain, e.pityPrice);
                        e.pityGain = new GunForHireGainData { source = (int)gain.Source, baseJade = gain.BaseJade, finalJade = gain.FinalJade, affectedByGreed = gain.AffectedByGreed };
                        if (wallet.Balance != target || _run.RunProgression.RunJadeEarned != income)
                            throw Bad("Pity wallet did not credit exactly one flat Run-income grant.");
                        var counters = _run.RunProgression.Capture();
                        try
                        {
                            _run.RunProgression.Restore(new RunProgressionSnapshot(counters.LuckBonus, counters.GreedBonus, e.jadeBefore, counters.GrowthBonus));
                            if (_run.SerializeForDeterministicComparison() != checkpoint) throw Bad("Pity credit changed another Run owner.");
                        }
                        finally { _run.RunProgression.Restore(counters); }
                        e.jadeAfter = _run.RunProgression.RunJadeEarned;
                    }
                    else
                    {
                        if (balance < e.buyPrice) throw Bad("Insufficient Hire balance.");
                        if (IsOwned(e.candidates[e.selectedIndex], e.openingRoster)) throw Bad("The saved FCC is already owned.");
                        var author = RestoreCandidate(e.candidates[e.selectedIndex]).Author;
                        CardConfig resolved = null; DungeonRunCardSaveData reward;
                        try
                        {
                            resolved = author.Instantiate(resolver);
                            e.grantedId = _run.AllocateInteractionCardId(); e.grantedContentId = author.ContentId;
                            reward = DungeonRunCardCodec.CaptureNew(resolved, e.grantedId, resolver);
                        }
                        finally { DungeonRunCardCodec.Release(resolved); }
                        if (wallet.Balance != balance || _run.SerializeForDeterministicComparison() != checkpoint)
                            throw Bad("Author/instance allocator callback changed Run or wallet.");
                        if (reward.baseAuthorJson != author.CanonicalJson || reward.currentAuthorJson != author.CanonicalJson)
                            throw Bad("Birth no longer matches the complete frozen FCC author.");
                        draft.Cards.Add(reward);
                        draft.Cards.Sort((a, b) => a.runtimeInstanceId.CompareTo(b.runtimeInstanceId));
                        ((IDungeonRunHireWallet)wallet).DebitExact(e.buyPrice);
                        if (wallet.Balance != balance - e.buyPrice || _run.SerializeForDeterministicComparison() != checkpoint)
                            throw Bad("Hire debit changed Run income or another Run owner.");
                        _run.CommitInteractionDraft(draft);
                    }
                    e.rosterAfter = CaptureRoster(draft.Cards);
                    string afterGrant = _run.SerializeForDeterministicComparison(); e.walletAfter = wallet.Balance;
                    if (_run.SerializeForDeterministicComparison() != afterGrant) throw Bad("Wallet receipt inspection changed Run state.");
                    applied = Complete(floor, effect, e);
                }, persist, wallet);
                receipt = new GunForHireReceipt(e, applied); error = null; return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (touched && !RestoreWallet(wallet, balance, out string recovery))
                { _unrestoredWallet = wallet; _unrestoredBalance = balance; error += "; wallet recovery: " + recovery; }
                return false;
            }
        }
        public bool TryLeave(string nodeId, out string error, Action persist)
        {
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = Find(nodeId) ?? throw Bad("No frozen Hire cache.");
                RequireEntry(old, effect); RequireLive(old);
                if (!old.active) { error = null; return true; }
                RequirePersistence(persist); var next = Clone(_state); next.entries.Single(v => v.floorId == old.floorId && v.nodeId == nodeId).active = false;
                Commit(next, floor, effect, () => { if (!floor.TryCancelUncommittedInteraction(nodeId, out var failure)) throw Bad(failure); }, persist);
                error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        public bool TryBack(string nodeId, out string error, Action persist) => TryLeave(nodeId, out error, persist);
        public bool TryRetryRecovery(out string error)
        {
            error = null; if (_committing || OtherCommitting) { error = "Run is committing."; return false; }
            if (_unrestoredWallet == null) return true;
            _committing = true;
            try { if (!RestoreWallet(_unrestoredWallet, _unrestoredBalance, out error)) return false; _unrestoredWallet = null; return true; }
            finally { _committing = false; }
        }
        private void Commit(GunForHireSaveData next, LevelRunSession floor, LevelInteractionEffect effect, Action apply, Action persist,
            IDungeonRunFlatCashOutWallet wallet = null)
        {
            var previous = _state; _committing = true;
            try { _run.CommitRewardState(() => { apply(); ValidateLocal(next); _state = next; RequireCheckpoint(floor, effect); }, () =>
                { string checkpoint = _run.SerializeForDeterministicComparison(); int? balance = wallet?.Balance;
                    if (_run.SerializeForDeterministicComparison() != checkpoint) throw Bad("Wallet inspection changed the checkpoint before save.");
                    persist(); int? afterBalance = wallet?.Balance;
                    if (afterBalance != balance || _run.SerializeForDeterministicComparison() != checkpoint) throw Bad("Strict Hire save changed its checkpoint.");
                    RequireCheckpoint(floor, effect); }); }
            catch { _state = previous; throw; } finally { _committing = false; }
        }
        private void Guard(string nodeId)
        {
            RequireId(nodeId);
            if (IsCommitting || _run.IsRewardCommitInProgress || _run.TryGetConclusion(out _) || _run.IsDungeonBonusGranted || _run.IsLevelBonusGranted ||
                _run.Interactions.HasOwnPendingChoice || _run.Arcanas.HasPendingChoice || _run.CardOfferings.HasPendingChoice ||
                _run.GemChests.HasPendingChoice || _run.Crystals.HasPendingEvent || _run.CardStatOfferings.HasPendingChoice ||
                _run.Invitations.HasPendingChoice || _run.DuplicateOfferings.HasPendingChoice || HasPendingChoice && PendingNodeContentId != nodeId) throw Bad("Resolve the active Run reward transaction first.");
            var floor = _run.CurrentFloor;
            if (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) ||
                !string.IsNullOrEmpty(floor.ActiveInteractionNodeId) && floor.ActiveInteractionNodeId != nodeId) throw Bad("Hire requires its matching idle map node.");
        }
        private bool OtherCommitting => _run.Interactions.IsCommitting || _run.Arcanas.IsCommitting || _run.CardOfferings.IsCommitting ||
            _run.GemChests.IsCommitting || _run.Crystals.IsCommitting || _run.CardStatOfferings.IsCommitting || _run.DuplicateOfferings.IsCommitting ||
            _run.DoubleKeyPayments.IsCommitting || _run.VictorySealRewards.IsCommitting || _run.FloorExits.IsCommitting ||
            _run.CardConversions.IsCommitting || _run.ManaStatues.IsCommitting || _run.AbandonedCarts.IsCommitting || _run.SideEvents.IsCommitting;
        private GunForHireEntryData Find(string nodeId) => _state.entries.SingleOrDefault(e => e.floorId == _run.CurrentFloorContentId && e.nodeId == nodeId);
        internal bool HasJournalEntry(string floorId, string nodeId) => _state.entries.Any(e => e.floorId == floorId && e.nodeId == nodeId);
        private void RequireLive(GunForHireEntryData e)
        {
            var floor = _run.CurrentFloor;
            if (e.finished) { ReadReceipt(floor, RequireEffect(floor, e.nodeId), e); return; }
            if (floor.TryGetInteractionReceipt(e.effectId, out _) || floor.GetNodeState(e.nodeId) == LevelNodeViewState.Completed) throw Bad("Unfinished Hire has a terminal floor receipt.");
            if (!e.active) { if (floor.ActiveInteractionNodeId == e.nodeId) throw Bad("Dormant Hire cannot retain its node lock."); return; }
            if (floor.ActiveInteractionNodeId != e.nodeId || floor.GetNodeState(e.nodeId) != LevelNodeViewState.Completing ||
                floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) ||
                !SameRoster(e.openingRoster, CaptureRoster(_run.CaptureInteractionDraft().Cards)) ||
                e.forcedOpeningBefore != _run.CaptureInteractionDraft().ForcedOpeningCardId || e.jadeBefore != _run.RunProgression.RunJadeEarned)
                throw Bad("Active Hire roster/floor/income checkpoint changed.");
        }
        private void RequireCheckpoint(LevelRunSession floor, LevelInteractionEffect effect)
        { if (!ReferenceEquals(floor, _run.CurrentFloor)) throw Bad("Hire floor identity changed."); var e = _state.entries.Single(v => v.floorId == floor.Profile.contentId && v.effectId == effect.effectId);
            RequireEntry(e, RequireEffect(floor, e.nodeId)); RequireLive(e); }
        private void RequireContext(GunForHirePreparedContext context, LevelInteractionEffect effect)
        {
            var c = effect.gunForHire;
            if (context == null || context.RootContentId != c.rootContentId || context.Document.Fingerprint != c.sourceFingerprint) throw Bad("Explicit Hire source context differs from node.");
            if (_run.SideEvents.HasSourcePlan && _run.SideEvents.Plan.Document.CanonicalJson != context.Document.CanonicalJson) throw Bad("Hire source differs from the frozen Run source plan.");
        }
        internal void RequireVisitedFloors(IReadOnlyList<LevelRunSession> floors)
        {
            ValidateLocal(_state); long pity = 0;
            foreach (var e in _state.entries)
            {
                var floor = floors.SingleOrDefault(f => f.Profile.contentId == e.floorId) ?? throw Bad("Hire refers to an unvisited floor.");
                var effect = RequireEffect(floor, e.nodeId); RequireEntry(e, effect);
                if (_run.SideEvents.HasSourcePlan && _run.SideEvents.Plan.Document.CanonicalJson != e.sourceDocumentJson) throw Bad("Saved Hire source differs from Run plan.");
                if (e.finished) { ReadReceipt(floor, effect, e); pity += e.disposition == 2 ? e.pityPrice : 0;
                    if (e.disposition == 1 && _run.DuplicateOfferings.HasGeneratedInstanceId(e.grantedId)) throw Bad("Hire birth identity conflicts with Duplicate birth.");
                    if (_run.RunProgression.RunJadeEarned < e.jadeAfter) throw Bad("Hire historical income exceeds actual Run income."); }
                else if (e.active)
                { if (!ReferenceEquals(floor, _run.CurrentFloor) || _run.TryGetConclusion(out _) || _run.IsDungeonBonusGranted || _run.IsLevelBonusGranted) throw Bad("Pending Hire conflicts with terminal/current floor.");
                    RequireLive(e); }
                else if (floor.GetNodeState(e.nodeId) == LevelNodeViewState.Completed || floor.ActiveInteractionNodeId == e.nodeId || floor.TryGetInteractionReceipt(e.effectId, out _))
                    throw Bad("Dormant Hire holds a node or completion proof.");
            }
            if (_run.RunProgression.RunJadeEarned < pity) throw Bad("Run income cannot account for Hire pity grants.");
            if (HasPendingChoice && (_run.Interactions.HasOwnPendingChoice || _run.Arcanas.HasPendingChoice || _run.CardOfferings.HasPendingChoice ||
                _run.GemChests.HasPendingChoice || _run.Crystals.HasPendingEvent || _run.CardStatOfferings.HasPendingChoice || _run.DuplicateOfferings.HasPendingChoice)) throw Bad("Hire overlaps another active owner.");
            foreach (var floor in floors)
            {
                foreach (var node in floor.Nodes)
                    foreach (var effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                        if (effect?.kind == LevelInteractionEffectKind.GunForHire && (floor.GetNodeState(node.contentId) == LevelNodeViewState.Completed || floor.TryGetInteractionReceipt(effect.effectId, out _)) &&
                            !_state.entries.Any(e => e.finished && e.floorId == floor.Profile.contentId && e.nodeId == node.contentId && e.effectId == effect.effectId) &&
                            !_run.Invitations.HasLegacyCompletionProof(floor, effect.effectId)) throw Bad("Completed Hire lacks its unique journal.");
                foreach (var receipt in floor.CaptureSaveData().interactionReceipts)
                    if (receipt.kind == LevelInteractionEffectKind.GunForHire && !_state.entries.Any(e => e.finished && e.floorId == floor.Profile.contentId && e.effectId == receipt.effectId) &&
                        !_run.Invitations.HasLegacyCompletionProof(floor, receipt.effectId)) throw Bad("Orphan Hire receipt.");
            }
        }
        private static LevelInteractionReceipt Complete(LevelRunSession floor, LevelInteractionEffect effect, GunForHireEntryData e)
        { int amount = e.disposition == 1 ? 1 : e.pityPrice;
            if (!floor.TryRecordExternalEffect(e.nodeId, effect, 0, amount, amount, out var receipt, out var error, e.grantedId, e.grantedContentId) || !receipt.NewlyApplied) throw Bad(error ?? "Hire already recorded.");
            if (!floor.TryCompleteInteraction(e.nodeId, out error)) throw Bad(error); return receipt; }
        private static GunForHireReceipt ReadReceipt(LevelRunSession floor, LevelInteractionEffect effect, GunForHireEntryData e)
        { int amount = e.disposition == 1 ? 1 : e.pityPrice;
            if (!e.finished || floor.GetNodeState(e.nodeId) != LevelNodeViewState.Completed || floor.ActiveInteractionNodeId == e.nodeId ||
                !floor.TryGetInteractionReceipt(e.effectId, out var receipt) || receipt.Kind != LevelInteractionEffectKind.GunForHire ||
                receipt.ValueBefore != 0 || receipt.ValueAfter != amount || receipt.Amount != amount || receipt.TargetRuntimeInstanceId != e.grantedId ||
                (receipt.TargetContentId ?? "") != (e.grantedContentId ?? "") || receipt.Rarity != effect.rarity ||
                (receipt.PresentationId ?? "") != (effect.presentationId ?? "") || (receipt.SummaryText ?? "") != (effect.summaryText ?? "") ||
                (receipt.DetailText ?? "") != (effect.detailText ?? "")) throw Bad("Hire floor receipt differs from its decision.");
            return new GunForHireReceipt(e, receipt); }
        private static bool SameObject(SideEventSourceProvenance p, GunForHireProvenanceData q)
            => p.Alias == q.alias && p.SourceFile == q.sourceFile && p.PathId == q.pathId;
        private static void RequireEntry(GunForHireEntryData e, LevelInteractionEffect effect)
        { var c = effect.gunForHire;
            if (e.effectFingerprint != EffectFingerprint(effect) || e.effectId != effect.effectId || e.sourceRootId != c.rootContentId ||
                e.sourceFingerprint != c.sourceFingerprint || e.poolPolicyId != c.poolPolicyId) throw Bad("Hire journal differs from canonical node."); }
        private static int Seed(string text) { string hash = Hash("hire-project-rng/v1\n" + text); return unchecked((int)Convert.ToUInt32(hash.Substring(0, 8), 16)); }
        private static void Begin(LevelRunSession floor, string nodeId) { if (!floor.TryBeginInteraction(nodeId, out var error)) throw Bad(error); }
        private static void RequireAvailable(LevelRunSession floor, string nodeId)
        { if (floor.GetNodeState(nodeId) != LevelNodeViewState.Available || !string.IsNullOrEmpty(floor.ActiveInteractionNodeId)) throw Bad("Hire node is unavailable."); }
        private static void RequirePersistence(Action persist) { if (persist == null) throw Bad("Explicit strict persistence is required."); }
        private static void RequireFlat(JadeGainResult receipt, int amount)
        { if (receipt.Source != JadeSource.CashOut || receipt.BaseJade != amount || receipt.FinalJade != amount || receipt.AffectedByGreed) throw Bad("Hire pity must be exact and unaffected by Greed."); }
        private bool RestoreWallet(IDungeonRunFlatCashOutWallet wallet, int balance, out string error)
        { _committing = true; try { wallet.RestoreBalance(balance); if (wallet.Balance != balance) throw Bad("Hire wallet rollback differs."); error = null; return true; }
            catch (Exception exception) { error = exception.Message; return false; } finally { _committing = false; } }
        internal static LevelInteractionEffect RequireEffect(LevelRunSession floor, string nodeId)
        { var node = floor.FindNode(nodeId);
            if (node == null || node.contentId != nodeId || !floor.IsNodeEnabled(nodeId) || node.nodeType != LevelNodeType.Interaction || node.completionEffects == null || node.completionEffects.Count != 1) throw Bad("Hire requires one exact enabled interaction.");
            var effect = node.completionEffects[0]; var errors = new List<string>(); ValidateEffect(effect, errors); if (errors.Count > 0) throw Bad(string.Join("; ", errors)); return effect; }
        public static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        { if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (effect == null || effect.kind != LevelInteractionEffectKind.GunForHire || effect.gunForHire == null || !effect.gunForHire.IsSpecified) { errors.Add("Hire requires its typed source contract."); return; }
            try { RequireId(effect.effectId); RequireId(effect.gunForHire.rootContentId); RequireHash(effect.gunForHire.sourceFingerprint); } catch (Exception e) { errors.Add(e.Message); }
            if (effect.gunForHire.sourceEventId != SourceEventId || effect.gunForHire.poolPolicyId != GunForHirePreparedContext.ExplicitSubsetPolicyId ||
                effect.amount != 0 || effect.amountMax != 0 || effect.cashOutJade != 0 || !string.IsNullOrEmpty(effect.targetContentId) || !string.IsNullOrEmpty(effect.sealRewardPoolId) ||
                effect.cardChoices?.Count > 0 || effect.sealChoices?.Count > 0 || effect.HasArcanaChoiceContract || effect.HasChickenOfferingContract || effect.HasManaOfferingContract ||
                effect.HasManaStatueContract || effect.HasAbandonedCartContract || effect.HasCrystalThrowingContract || effect.HasCrawlerInvitationContract || effect.HasFixedEvolutionContract ||
                effect.HasTargetLevelResultContract || effect.HasCardStatOfferingContract || effect.HasDuplicateOfferingContract || effect.doubleKeyPayment?.IsSpecified == true)
                errors.Add("Hire cannot mix other effects or unknown source/pool policies."); }
        internal static string EffectFingerprint(LevelInteractionEffect e) => Hash(new ExactJsonWire(typeof(HireEffectData), typeof(LevelInteractionGunForHireContract)).Write(
            new HireEffectData { effectId = e.effectId, kind = (int)e.kind, amount = e.amount, amountMax = e.amountMax, targetContentId = e.targetContentId,
                presentationId = e.presentationId, rarity = (int)e.rarity, summaryText = e.summaryText, detailText = e.detailText,
                sealRewardPoolId = e.sealRewardPoolId, cashOutJade = e.cashOutJade, contract = e.gunForHire }));
        internal static bool SameContract(LevelInteractionEffect a, LevelInteractionEffect b) => a != null && b != null && EffectFingerprint(a) == EffectFingerprint(b);
        internal static bool IsValidFloorSummary(int before, int after, int amount, int target, string content)
            => before == 0 && after == amount && (amount == 1 && target > 0 && !string.IsNullOrEmpty(content) || amount >= 0 && target == 0 && string.IsNullOrEmpty(content));
        private sealed class HireEffectData
        { public string effectId; public int kind, amount, amountMax; public string targetContentId, presentationId; public int rarity;
            public string summaryText, detailText, sealRewardPoolId; public int cashOutJade; public LevelInteractionGunForHireContract contract; }
    }
}
