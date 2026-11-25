using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;
using static WanjieScroll.Dungeon.DungeonRunCardStatOfferingSaveCodec;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the CardStat decision, exact physical payment and durable idempotence.
    /// Existing Run owners retain all card, stat, floor and income state. The codec
    /// owns closed wire/local mathematics; StatApplications owns the sole eleven
    /// channel mapping. This transaction binds both proofs to the actual Run and
    /// commits them together. No account query, pool expansion or RNG occurs here.
    /// </summary>
    public sealed class DungeonRunCardStatOfferings
    {
        public const string SourceEventId = "event.xiyou.card-stat-offering.v1";
        private readonly DungeonRunSession _run;
        private DungeonRunCardStatOfferingSaveData _state;
        private bool _committing;
        private IDungeonRunFlatCashOutWallet _unrestoredWallet;
        private int _unrestoredBalance;
        public bool IsCommitting => _committing || _unrestoredWallet != null;
        internal long ProvenJadeIncome => (long)_state.entries.Count(entry => entry.finished && entry.disposition == 2) * 200;
        public bool HasPendingChoice => _state.entries.Any(entry => entry.active);
        public string PendingNodeContentId => _state.entries.FirstOrDefault(entry => entry.active)?.nodeId;
        public bool HasConsumed(int id) => id > 0 && _state.entries.Any(entry => entry.finished && entry.disposition == 1 && entry.selectedRuntimeInstanceId == id);

        internal DungeonRunCardStatOfferings(DungeonRunSession run, string savedCanonicalJson = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            try { _state = savedCanonicalJson == null ? CreateEmpty(run.RunId) : Read(savedCanonicalJson); }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException || exception is FormatException)
            { throw Bad("Invalid CardStat offering journal.", exception); }
            if (_state.runId != run.RunId)
            {
                if (_state.entries.Count != 0) throw Bad("CardStat journal belongs to another Run.");
                _state.runId = run.RunId; // Empty legacy/modern state has no identity-bound decision to replay.
            }
        }
        public string CaptureCanonicalJson()
        {
            if (_unrestoredWallet != null) throw Bad("CardStat economy recovery is incomplete.");
            return Write(_state);
        }
        public bool TryInspect(string nodeId, out CardStatOfferingOffer offer, out string error)
        {
            offer = null;
            try
            {
                Guard(nodeId); var effect = RequireEffect(_run.CurrentFloor, nodeId); var entry = Find(nodeId);
                if (entry == null) throw Bad("Open with an explicit prepared subset first.");
                RequireEntry(effect, entry); RequireLiveCheckpoint(entry);
                offer = Offer(entry, effect); error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        public bool TryOpen(string nodeId, CardStatOfferingPreparedContext context,
            out CardStatOfferingOffer offer, out string error, Action persist)
        {
            offer = null;
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var entry = Find(nodeId);
                if (entry != null)
                {
                    RequireEntry(effect, entry); RequireLiveCheckpoint(entry);
                    offer = Offer(entry, effect); error = null; return true;
                }
                RequirePersistence(persist); RequireAvailable(floor, nodeId);
                if (floor.TryGetInteractionReceipt(effect.effectId, out _)) throw Bad("CardStat receipt lacks its unique owner proof.");
                if (context == null) throw Bad("An explicit prepared source context is required.");
                var contract = effect.cardStatOffering;
                if (context.RootContentId != contract.rootContentId || context.Document.Fingerprint != contract.sourceFingerprint ||
                    context.PoolPolicyId != contract.poolPolicyId) throw Bad("Prepared source context differs from the canonical node.");
                RequirePlanDocument(context.Document.CanonicalJson);
                var draft = _run.CaptureInteractionDraft(); var roster = draft.Cards.Select(CaptureCard).ToList();
                var candidates = context.CaptureCandidates().ToList();
                foreach (var candidate in candidates)
                {
                    var live = roster.SingleOrDefault(card => card.runtimeInstanceId == candidate.card.runtimeInstanceId);
                    if (live == null || CardFingerprint(live) != CardFingerprint(candidate.card))
                        throw Bad("Prepared candidate is not the exact current Run instance.");
                }
                entry = new CardStatOfferingEntryData { floorId = _run.CurrentFloorContentId, nodeId = nodeId,
                    effectId = effect.effectId, effectFingerprint = EffectFingerprint(effect), active = true,
                    physicalProofVersion = 2,
                    sourceDocumentJson = context.Document.CanonicalJson, sourceFingerprint = context.Document.Fingerprint,
                    sourceRootId = context.RootContentId, poolPolicyId = context.PoolPolicyId,
                    poolPolicyJson = context.PoolPolicyCanonicalJson, poolPolicyFingerprint = Hash(context.PoolPolicyCanonicalJson),
                    openingRoster = roster, candidates = candidates, rosterAfter = new List<CardStatOfferingCardData>(),
                    jadeBefore = _run.RunProgression.RunJadeEarned, forcedOpeningBefore = draft.ForcedOpeningCardId };
                var next = Clone(_state); next.entries.Add(entry);
                Commit(next, floor, effect, () => Begin(floor, nodeId), persist);
                offer = Offer(entry, effect); error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        public bool TrySubmitAndComplete(string nodeId, int runtimeInstanceId,
            out CardStatOfferingReceipt receipt, out string error, Action persist)
        {
            receipt = null;
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = RequireOpened(effect, nodeId);
                if (old.finished)
                {
                    if (old.disposition != 1 || old.selectedRuntimeInstanceId != runtimeInstanceId) throw Bad("The durable decision used a different payment.");
                    receipt = ReadReceipt(floor, effect, old); error = null; return true;
                }
                RequirePersistence(persist); RequireLiveCheckpoint(old);
                var candidate = old.candidates.SingleOrDefault(value => value.card.runtimeInstanceId == runtimeInstanceId);
                if (candidate == null) throw Bad("Selected instance is outside the explicit frozen subset.");
                if (ConsumedByOtherOwner(runtimeInstanceId) || HasConsumed(runtimeInstanceId)) throw Bad("This physical instance was already consumed.");
                var projection = SideEventCardStatProjectionProof.Parse(candidate.projectionProofJson).Projection;
                var specification = CardStatOfferingPreparedContext.RequireSpecification(SideEventSourceWireDocument.Parse(old.sourceDocumentJson), old.sourceRootId);
                var result = SideEventCardStatOfferingRules.Prepare(specification, projection.IsFcc,
                    projection.RawAllowInOfferingTable == 1, projection.ModifiedStats);
                var prepared = _run.StatApplications.Prepare(result); // Full batch overflow/CAS preflight, no mutation.
                var draft = _run.CaptureInteractionDraft(); draft.Cards.RemoveAll(card => card.runtimeInstanceId == runtimeInstanceId);
                if (draft.ForcedOpeningCardId == runtimeInstanceId) draft.ForcedOpeningCardId = 0;
                var next = Clone(_state); var entry = next.entries.Single(value => value.floorId == old.floorId && value.nodeId == old.nodeId);
                entry.active = false; entry.finished = true; entry.disposition = 1;
                entry.selectedRuntimeInstanceId = runtimeInstanceId; entry.selectedContentId = candidate.card.contentId;
                entry.statApplicationProofJson = prepared.CaptureProof().Serialize();
                entry.rosterAfter = draft.Cards.Select(CaptureCard).ToList(); entry.forcedOpeningAfter = draft.ForcedOpeningCardId;
                entry.jadeAfter = entry.jadeBefore;
                LevelInteractionReceipt applied = default;
                Commit(next, floor, effect, () =>
                {
                    _run.CommitInteractionDraft(draft); _run.StatApplications.Apply(prepared);
                    applied = Complete(floor, entry, effect);
                }, persist);
                receipt = new CardStatOfferingReceipt(entry, applied); error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        public bool TryCashOutAndComplete(string nodeId, IDungeonRunFlatCashOutWallet wallet,
            out CardStatOfferingReceipt receipt, out string error, Action persist)
        {
            receipt = null; bool touched = false; int beforeBalance = 0; bool succeeded = false;
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = RequireOpened(effect, nodeId);
                if (old.finished)
                {
                    if (old.disposition != 2) throw Bad("The durable decision was a stat payment.");
                    receipt = ReadReceipt(floor, effect, old); error = null; return true;
                }
                RequirePersistence(persist); RequireLiveCheckpoint(old);
                if (wallet == null) throw Bad("An explicit flat cash-out wallet is required.");
                var next = Clone(_state); var entry = next.entries.Single(value => value.floorId == old.floorId && value.nodeId == old.nodeId);
                entry.active = false; entry.finished = true; entry.disposition = 2;
                entry.rosterAfter = entry.openingRoster.Select(card => CaptureCard(RestoreCard(card))).ToList();
                entry.forcedOpeningAfter = entry.forcedOpeningBefore;
                entry.cashGain = new CardStatOfferingCashData { source = (int)JadeSource.CashOut, baseJade = 200, finalJade = 200, affectedByGreed = false };
                LevelInteractionReceipt applied = default;
                Commit(next, floor, effect, () =>
                {
                    beforeBalance = wallet.Balance;
                    int afterBalance = checked(beforeBalance + 200), afterJade = checked(_run.RunProgression.RunJadeEarned + 200);
                    entry.walletBefore = beforeBalance; entry.walletAfter = afterBalance; entry.jadeAfter = afterJade;
                    string before = _run.SerializeForDeterministicComparison();
                    touched = true; // Preview is an external participant and may fail after changing its ledger.
                    RequireFlat(wallet.PreviewFlatCashOut(200));
                    if (wallet.Balance != beforeBalance || _run.SerializeForDeterministicComparison() != before)
                        throw Bad("Flat preview changed the Run or balance.");
                    RequireFlat(wallet.CreditFlatCashOut(200));
                    if (wallet.Balance != afterBalance || _run.RunProgression.RunJadeEarned != afterJade)
                        throw Bad("Flat wallet must credit and record exactly 200 once.");
                    applied = Complete(floor, entry, effect);
                }, persist, wallet);
                succeeded = true; receipt = new CardStatOfferingReceipt(entry, applied); error = null; return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (touched && !succeeded)
                {
                    _committing = true;
                    try
                    {
                        if (!RestoreWallet(wallet, beforeBalance, out string recovery))
                        { _unrestoredWallet = wallet; _unrestoredBalance = beforeBalance; error += " Economy recovery is incomplete: " + recovery; }
                    }
                    finally { _committing = false; }
                }
                return false;
            }
        }
        public bool TryBack(string nodeId, out string error, Action persist) => TryLeave(nodeId, out error, persist);
        public bool TryLeave(string nodeId, out string error, Action persist)
        {
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = Find(nodeId);
                if (old != null && old.finished) throw Bad("A completed decision cannot be canceled.");
                if (old == null && string.IsNullOrEmpty(floor.ActiveInteractionNodeId))
                { RequireAvailable(floor, nodeId); error = null; return true; }
                RequirePersistence(persist); if (old != null) { RequireEntry(effect, old); RequireLiveCheckpoint(old); }
                var next = Clone(_state); next.entries.RemoveAll(value => value.floorId == _run.CurrentFloorContentId && value.nodeId == nodeId);
                Commit(next, floor, effect, () =>
                {
                    if (!floor.TryCancelUncommittedInteraction(nodeId, out string failure)) throw Bad(failure);
                }, persist);
                error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        public bool TryRetryRecovery(out string error)
        {
            error = null;
            if (_committing) { error = "CardStat transaction is committing."; return false; }
            if (_unrestoredWallet == null) return true;
            _committing = true;
            try
            {
                if (!RestoreWallet(_unrestoredWallet, _unrestoredBalance, out error)) return false;
                _unrestoredWallet = null; return true;
            }
            finally { _committing = false; }
        }
        private void Commit(DungeonRunCardStatOfferingSaveData next, LevelRunSession floor,
            LevelInteractionEffect effect, Action apply, Action persist, IDungeonRunFlatCashOutWallet wallet = null)
        {
            var previous = _state; _committing = true;
            try
            {
                _run.CommitRewardState(() => { apply(); ValidateLocal(next); _state = next; RequireCheckpoint(floor, effect); }, () =>
                {
                    string checkpoint = _run.SerializeForDeterministicComparison(); int balance = wallet?.Balance ?? 0;
                    persist();
                    if (_run.SerializeForDeterministicComparison() != checkpoint || wallet != null && wallet.Balance != balance)
                        throw Bad("Strict persistence changed the prepared Run/economy checkpoint.");
                    RequireCheckpoint(floor, effect);
                });
            }
            catch { _state = previous; throw; }
            finally { _committing = false; }
        }
        private static LevelInteractionReceipt Complete(LevelRunSession floor, CardStatOfferingEntryData entry, LevelInteractionEffect effect)
        {
            int amount = entry.disposition == 1 ? 1 : 200;
            if (!floor.TryRecordExternalEffect(entry.nodeId, effect, 0, amount, amount, out var receipt, out string error,
                    entry.selectedRuntimeInstanceId, entry.selectedContentId) || !receipt.NewlyApplied) throw Bad(error ?? "CardStat receipt was already applied.");
            if (!floor.TryCompleteInteraction(entry.nodeId, out error)) throw Bad(error);
            return receipt;
        }
        private void Guard(string nodeId)
        {
            RequireId(nodeId);
            if (IsCommitting || _run.IsRewardCommitInProgress || _run.TryGetConclusion(out _) || _run.IsDungeonBonusGranted ||
                _run.IsLevelBonusGranted || _run.Interactions.HasOwnPendingChoice || _run.Arcanas.HasPendingChoice ||
                _run.Invitations.HasPendingChoice || _run.DuplicateOfferings.HasPendingChoice || _run.GunForHire.HasPendingChoice ||
                _run.CardOfferings.HasPendingChoice || _run.GemChests.HasPendingChoice || _run.Crystals.HasPendingEvent ||
                HasPendingChoice && PendingNodeContentId != nodeId) throw Bad("Resolve the active Run reward transaction first.");
            var floor = _run.CurrentFloor;
            if (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) ||
                !string.IsNullOrEmpty(floor.ActiveInteractionNodeId) && floor.ActiveInteractionNodeId != nodeId)
                throw Bad("CardStat offering requires its matching idle map node.");
        }
        private CardStatOfferingEntryData Find(string nodeId)
            => _state.entries.SingleOrDefault(entry => entry.floorId == _run.CurrentFloorContentId && entry.nodeId == nodeId);
        private CardStatOfferingEntryData RequireOpened(LevelInteractionEffect effect, string nodeId)
        {
            var entry = Find(nodeId) ?? throw Bad("Open and persist the explicit offering before choosing a branch.");
            RequireEntry(effect, entry); RequireLiveCheckpoint(entry); return entry;
        }
        private CardStatOfferingOffer Offer(CardStatOfferingEntryData entry, LevelInteractionEffect effect)
            => new CardStatOfferingOffer(entry, entry.finished ? ReadReceipt(_run.CurrentFloor, effect, entry) : null);
        private void RequireLiveCheckpoint(CardStatOfferingEntryData entry)
        {
            var floor = _run.CurrentFloor;
            if (entry.finished) { ReadReceipt(floor, RequireEffect(floor, entry.nodeId), entry); return; }
            if (!entry.active || floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) ||
                floor.GetNodeState(entry.nodeId) != LevelNodeViewState.Completing || floor.ActiveInteractionNodeId != entry.nodeId)
                throw Bad("Pending CardStat node checkpoint changed.");
            var draft = _run.CaptureInteractionDraft();
            if (!RosterEquals(entry.openingRoster, draft.Cards.Select(CaptureCard).ToList()) ||
                draft.ForcedOpeningCardId != entry.forcedOpeningBefore || _run.RunProgression.RunJadeEarned != entry.jadeBefore)
                throw Bad("The frozen physical roster or opening-hand/income state changed.");
        }
        private void RequireCheckpoint(LevelRunSession floor, LevelInteractionEffect effect)
        {
            if (!ReferenceEquals(floor, _run.CurrentFloor) || EffectFingerprint(RequireEffect(floor, FindNodeId(effect))) != EffectFingerprint(effect))
                throw Bad("Canonical CardStat floor/effect changed during save.");
            var entry = _state.entries.SingleOrDefault(value => value.floorId == floor.Profile.contentId && value.effectId == effect.effectId);
            if (entry != null) RequireLiveCheckpoint(entry);
            else if (!string.IsNullOrEmpty(floor.ActiveInteractionNodeId) || floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _))
                throw Bad("Canceled CardStat checkpoint changed during save.");
        }
        private string FindNodeId(LevelInteractionEffect effect)
        {
            foreach (var node in _run.CurrentFloor.Nodes)
                if (node?.completionEffects?.Any(value => value != null && value.effectId == effect.effectId) == true) return node.contentId;
            throw Bad("Canonical CardStat effect disappeared.");
        }
        private void RequirePlanDocument(string document)
        {
            if (_run.SideEvents.HasSourcePlan && _run.SideEvents.Plan.Document.CanonicalJson != document)
                throw Bad("CardStat source differs from this Run's frozen source document.");
        }
        private static void Begin(LevelRunSession floor, string nodeId)
        { if (!floor.TryBeginInteraction(nodeId, out string error)) throw Bad(error); }
        private static void RequireAvailable(LevelRunSession floor, string nodeId)
        { if (floor.GetNodeState(nodeId) != LevelNodeViewState.Available || !string.IsNullOrEmpty(floor.ActiveInteractionNodeId)) throw Bad("CardStat node is not available."); }
        private static void RequirePersistence(Action persist)
        { if (persist == null) throw Bad("An explicit strict persistence callback is required."); }
        private static void RequireFlat(JadeGainResult gain)
        { if (gain.Source != JadeSource.CashOut || gain.BaseJade != 200 || gain.FinalJade != 200 || gain.AffectedByGreed) throw Bad("CardStat cash-out must be exactly 200 without Greed."); }
        private static bool RestoreWallet(IDungeonRunFlatCashOutWallet wallet, int balance, out string error)
        {
            try { wallet.RestoreBalance(balance); if (wallet.Balance != balance) throw Bad("Wallet rollback did not restore its checkpoint."); error = null; return true; }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        private bool ConsumedByOtherOwner(int id)
            => _run.DoubleKeyPayments.WasConsumed(id) || _run.CardOfferings.WasConsumed(id) || _run.GemChests.HasConsumed(id);
        private static bool RosterEquals(IReadOnlyList<CardStatOfferingCardData> left, IReadOnlyList<CardStatOfferingCardData> right)
            => left.Count == right.Count && left.Select(CardFingerprint).SequenceEqual(right.Select(CardFingerprint));
        private static void RequireEntry(LevelInteractionEffect effect, CardStatOfferingEntryData entry)
        {
            var contract = effect.cardStatOffering;
            if (entry.effectId != effect.effectId || entry.effectFingerprint != EffectFingerprint(effect) ||
                entry.sourceFingerprint != contract.sourceFingerprint || entry.sourceRootId != contract.rootContentId || entry.poolPolicyId != contract.poolPolicyId)
                throw Bad("CardStat journal differs from the canonical configured effect.");
        }
        private static CardStatOfferingReceipt ReadReceipt(LevelRunSession floor, LevelInteractionEffect effect, CardStatOfferingEntryData entry)
        {
            int amount = entry.disposition == 1 ? 1 : 200;
            if (!entry.finished || floor.GetNodeState(entry.nodeId) != LevelNodeViewState.Completed ||
                floor.ActiveInteractionNodeId == entry.nodeId || !floor.TryGetInteractionReceipt(effect.effectId, out var receipt) ||
                receipt.Kind != LevelInteractionEffectKind.CardStatOffering || receipt.ValueBefore != 0 || receipt.ValueAfter != amount ||
                receipt.Amount != amount || receipt.TargetRuntimeInstanceId != entry.selectedRuntimeInstanceId || receipt.Rarity != effect.rarity ||
                (receipt.PresentationId ?? "") != (effect.presentationId ?? "") || (receipt.SummaryText ?? "") != (effect.summaryText ?? "") ||
                (receipt.DetailText ?? "") != (effect.detailText ?? "") ||
                (receipt.TargetContentId ?? "") != (entry.selectedContentId ?? "")) throw Bad("CardStat completion receipt differs from its durable decision.");
            return new CardStatOfferingReceipt(entry, receipt);
        }
        internal void RequireVisitedFloors(IReadOnlyList<LevelRunSession> floors)
        {
            ValidateLocal(_state); var consumed = new HashSet<int>();
            foreach (var entry in _state.entries)
            {
                var floor = floors.SingleOrDefault(value => value.Profile.contentId == entry.floorId) ?? throw Bad("CardStat proof refers to an unvisited floor.");
                var effect = RequireEffect(floor, entry.nodeId); RequireEntry(effect, entry); RequirePlanDocument(entry.sourceDocumentJson);
                if (entry.finished)
                {
                    ReadReceipt(floor, effect, entry);
                    if (entry.disposition == 1)
                    {
                        if (!consumed.Add(entry.selectedRuntimeInstanceId) || ConsumedByOtherOwner(entry.selectedRuntimeInstanceId) ||
                            _run.CaptureInteractionDraft().Cards.Any(card => card.runtimeInstanceId == entry.selectedRuntimeInstanceId))
                            throw Bad("Consumed CardStat instance conflicts with another owner or the current roster.");
                        var proof = DungeonRunStatApplications.ParseAndValidateProof(entry.statApplicationProofJson);
                        var baseline = _run.CaptureCardStatEntryBaseline();
                        if (baseline == null || DungeonRunStatApplicationProof.WriteBaseline(baseline) !=
                            DungeonRunStatApplicationProof.WriteBaseline(proof.Baseline.ToSaveData())) throw Bad("CardStat proof does not use the actual immutable Run entry baseline.");
                        RequireHistoricalState(proof.After);
                    }
                    if (_run.RunProgression.RunJadeEarned < entry.jadeAfter) throw Bad("CardStat income proof exceeds current Run income.");
                }
                else
                {
                    if (!ReferenceEquals(floor, _run.CurrentFloor) || _run.TryGetConclusion(out _) || _run.IsDungeonBonusGranted || _run.IsLevelBonusGranted)
                        throw Bad("Pending CardStat proof conflicts with the current/terminal floor.");
                    RequireLiveCheckpoint(entry);
                    if (floor.TryGetInteractionReceipt(effect.effectId, out _)) throw Bad("Unfinished CardStat already has a floor receipt.");
                }
            }
            foreach (var floor in floors)
            {
                var nodes = floor.Nodes;
                foreach (var node in nodes)
                {
                    foreach (var effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                        if (effect?.kind == LevelInteractionEffectKind.CardStatOffering &&
                            (floor.GetNodeState(node.contentId) == LevelNodeViewState.Completed || floor.TryGetInteractionReceipt(effect.effectId, out _)) &&
                            !_state.entries.Any(entry => entry.finished && entry.floorId == floor.Profile.contentId && entry.nodeId == node.contentId && entry.effectId == effect.effectId))
                            throw Bad("Completed CardStat node/receipt has no unique journal proof.");
                }
                foreach (var receipt in floor.CaptureSaveData().interactionReceipts)
                    if (receipt.kind == LevelInteractionEffectKind.CardStatOffering && !_state.entries.Any(entry => entry.finished && entry.floorId == floor.Profile.contentId && entry.effectId == receipt.effectId))
                        throw Bad("Orphan CardStat floor receipt.");
            }
            if (HasPendingChoice && (_run.Invitations.HasPendingChoice || _run.DuplicateOfferings.HasPendingChoice || _run.GunForHire.HasPendingChoice || _run.Interactions.HasOwnPendingChoice || _run.Arcanas.HasPendingChoice || _run.CardOfferings.HasPendingChoice || _run.GemChests.HasPendingChoice || _run.Crystals.HasPendingEvent))
                throw Bad("Pending CardStat conflicts with another owner.");
            RequireStatLowerBounds();
        }
        private void RequireHistoricalState(DungeonRunStatApplicationState after)
        {
            var current = _run.CaptureCardStatApplicationState(); var a = after.Modifiers; var c = current.Modifiers;
            if (a.BaseManaBonus > c.BaseManaBonus || a.BaseArmorPerTurnBonus > c.BaseArmorPerTurnBonus ||
                a.MightBonus > c.MightBonus || a.HandLimitBonus > c.HandLimitBonus || a.AreaBonus > c.AreaBonus)
                throw Bad("Historical shared modifiers exceed the current Run owners.");
            var p = after.Progression; var q = current.Progression;
            if (p.LuckBonus > q.LuckBonus || p.GreedBonus > q.GreedBonus || p.GrowthBonus > q.GrowthBonus || p.JadeEarned > q.JadeEarned)
                throw Bad("Historical raw counters exceed the current Run owners.");
            if (after.Additional.AmountBonus > current.Additional.AmountBonus || after.Additional.MagnetBonus > current.Additional.MagnetBonus ||
                after.Additional.CurseBonus > current.Additional.CurseBonus) throw Bad("Historical additional stats exceed the current Run owner.");
            var r = after.Revival; var s = current.Revival;
            if (r.Initialized != s.Initialized || r.CapacityPercent != s.CapacityPercent || r.InitialPercent != s.InitialPercent ||
                r.AwardedPercent > s.AwardedPercent || r.SpentLives > s.SpentLives)
                throw Bad("Historical Revival proof differs from the current reserve.");
            // Remaining Revival and reward rerolls may be consumed after the grant.
        }
        private void RequireStatLowerBounds()
        {
            var sums = new Dictionary<int, long>(); long cash = 0;
            foreach (var entry in _state.entries.Where(value => value.finished))
            {
                if (entry.disposition == 2) { cash += 200; continue; }
                foreach (var receipt in DungeonRunStatApplications.ParseAndValidateProof(entry.statApplicationProofJson).Receipts)
                { sums.TryGetValue(receipt.SourceStat, out long total); sums[receipt.SourceStat] = total + receipt.ActualOwnedDelta; }
            }
            var state = _run.CaptureCardStatApplicationState();
            foreach (var pair in sums)
            {
                // These are lower bounds on existing owners, not another mutable stat ledger.
                long current;
                switch (pair.Key)
                {
                    case 0: current = state.Modifiers.AreaBonus; break;
                    case 1: current = state.Additional.AmountBonus; break;
                    case 2: current = state.Modifiers.BaseArmorPerTurnBonus; break;
                    case 4: current = state.Modifiers.MightBonus; break;
                    case 5: current = state.Progression.GrowthBonus; break;
                    case 6: current = state.Progression.GreedBonus; break;
                    case 7: current = state.Progression.LuckBonus; break;
                    case 10: current = state.Modifiers.HandLimitBonus; break;
                    case 11: current = state.Additional.MagnetBonus; break;
                    case 13: current = state.Revival.AwardedPercent; break;
                    case 17: current = state.Additional.CurseBonus; break;
                    default: throw Bad("Unknown stat receipt channel.");
                }
                if (current < pair.Value) throw Bad("Run stat owner cannot account for its CardStat durable grants.");
            }
            if (_run.RunProgression.RunJadeEarned < cash) throw Bad("Run income cannot account for its CardStat cash-out grants.");
        }
        internal static LevelInteractionEffect RequireEffect(LevelRunSession floor, string nodeId)
        {
            var node = floor.FindNode(nodeId);
            if (node == null || !floor.IsNodeEnabled(nodeId) || node.nodeType != LevelNodeType.Interaction || node.completionEffects == null || node.completionEffects.Count != 1)
                throw Bad("CardStat requires one canonical enabled interaction effect.");
            var effect = node.completionEffects[0]; var errors = new List<string>(); ValidateEffect(effect, errors);
            if (errors.Count != 0) throw Bad(string.Join("; ", errors)); return effect;
        }
        public static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (effect == null || effect.kind != LevelInteractionEffectKind.CardStatOffering || effect.cardStatOffering == null || !effect.cardStatOffering.IsSpecified)
            { errors.Add("CardStat requires its typed source contract."); return; }
            var contract = effect.cardStatOffering;
            try { RequireId(effect.effectId); RequireId(contract.rootContentId); RequireHash(contract.sourceFingerprint); }
            catch (Exception exception) { errors.Add(exception.Message); }
            if (contract.sourceEventId != SourceEventId || contract.poolPolicyId != CardStatOfferingPreparedContext.ExplicitSubsetPolicyId ||
                effect.amount != 0 || effect.amountMax != 0 || !string.IsNullOrEmpty(effect.targetContentId) || !string.IsNullOrEmpty(effect.sealRewardPoolId) ||
                effect.cashOutJade != 0 || effect.cardChoices?.Count > 0 || effect.sealChoices?.Count > 0 || effect.HasArcanaChoiceContract ||
                effect.HasChickenOfferingContract || effect.HasManaOfferingContract || effect.HasManaStatueContract || effect.HasAbandonedCartContract ||
                effect.HasDuplicateOfferingContract || effect.HasGunForHireContract ||
                effect.HasCrystalThrowingContract || effect.HasCrawlerInvitationContract || effect.HasFixedEvolutionContract || effect.HasTargetLevelResultContract || effect.doubleKeyPayment?.IsSpecified == true)
                errors.Add("CardStat cannot mix another reward payload or an unknown source/pool policy.");
        }
        internal static string EffectFingerprint(LevelInteractionEffect effect)
            => Hash(new ExactJsonWire(typeof(EffectIdentityData), typeof(LevelInteractionCardStatOfferingContract)).Write(
                new EffectIdentityData { effectId = effect.effectId, kind = (int)effect.kind, amount = effect.amount, amountMax = effect.amountMax,
                    targetContentId = effect.targetContentId, presentationId = effect.presentationId, rarity = (int)effect.rarity,
                    summaryText = effect.summaryText, detailText = effect.detailText, sealRewardPoolId = effect.sealRewardPoolId,
                    cashOutJade = effect.cashOutJade, contract = effect.cardStatOffering }));
        private sealed class EffectIdentityData
        {
            public string effectId; public int kind, amount, amountMax; public string targetContentId, presentationId;
            public int rarity; public string summaryText, detailText, sealRewardPoolId; public int cashOutJade;
            public LevelInteractionCardStatOfferingContract contract;
        }
        internal static bool SameContract(LevelInteractionEffect left, LevelInteractionEffect right)
            => left != null && right != null && SameContract(left.cardStatOffering, right.cardStatOffering) && EffectFingerprint(left) == EffectFingerprint(right);
        internal static bool SameContract(LevelInteractionCardStatOfferingContract left, LevelInteractionCardStatOfferingContract right)
            => left != null && right != null && left.sourceEventId == right.sourceEventId && left.rootContentId == right.rootContentId &&
                left.sourceFingerprint == right.sourceFingerprint && left.poolPolicyId == right.poolPolicyId;
        internal static bool IsValidFloorSummary(int before, int after, int amount, int targetId, string targetContentId)
            => before == 0 && (amount == 1 && after == 1 && targetId > 0 && !string.IsNullOrEmpty(targetContentId) ||
                amount == 200 && after == 200 && targetId == 0 && string.IsNullOrEmpty(targetContentId));
        private static InvalidOperationException Bad(string message, Exception inner = null) => new InvalidOperationException(message, inner);
    }
}
