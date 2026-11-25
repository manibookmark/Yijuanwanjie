using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;
using static WanjieScroll.Dungeon.DungeonRunDuplicateOfferingSaveCodec;

namespace WanjieScroll.Dungeon
{
    /// <summary>Single owner of durable duplicate selection, dormant cache and receipt.
    /// Core constructs a fresh persistent copy; Session owns inventory and aggregate
    /// rollback. The journal freezes explicit policies, never approves production pools
    /// or breakthrough defaults. Reference bindings are required anew for actual submit,
    /// while restore/inspect/history perform only managed proof validation. Scale review:
    /// commands, receipt recovery and identity checks protect this same journal; wire and
    /// copy arithmetic are separated. Reassess before adding another reward branch.</summary>
    public sealed class DungeonRunDuplicateOfferings
    {
        public const string SourceEventId = DuplicateOfferingEligibility.CanonicalSourceEventId;
        private readonly DungeonRunSession _run;
        private DungeonRunDuplicateOfferingSaveData _state;
        private bool _committing;
        public bool IsCommitting => _committing;
        public bool HasPendingChoice => _state.entries.Any(value => value.active && !value.finished);
        public string PendingNodeContentId => _state.entries.SingleOrDefault(value => value.active && !value.finished)?.nodeId;
        public bool ReservesInstanceId(int id) => id > 0 && _state.entries.Any(entry =>
            ContainsId(entry.openingRoster, id) || ContainsId(entry.checkpointRoster, id) || ContainsId(entry.rosterAfter, id) || entry.createdRuntimeInstanceId == id);
        internal bool HasGeneratedInstanceId(int id) => id > 0 && _state.entries.Any(entry => entry.finished && entry.createdRuntimeInstanceId == id);
        internal DungeonRunDuplicateOfferings(DungeonRunSession run, string savedJson = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run)); _state = savedJson == null ? CreateEmpty(run.RunId) : Read(savedJson);
            if (_state.runId != run.RunId)
            { if (_state.entries.Count != 0) throw Bad("Duplicate journal belongs to another Run."); _state.runId = run.RunId; }
        }
        public string CaptureCanonicalJson() => Write(_state);
        public bool TryInspect(string nodeId, out DuplicateOfferingOffer offer, out string error)
        {
            offer = null;
            try { Guard(nodeId); var effect = RequireEffect(_run.CurrentFloor, nodeId); var entry = Find(nodeId) ?? throw Bad("Open an explicit duplicate context first.");
                RequireEntry(effect, entry); RequireLiveCheckpoint(entry); offer = Offer(entry, effect); error = null; return true; }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        public bool TryOpen(string nodeId, DuplicateOfferingPreparedContext context, out DuplicateOfferingOffer offer, out string error, Action persist)
        {
            offer = null;
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = Find(nodeId);
                if (old != null)
                {
                    RequireEntry(effect, old); RequireLiveCheckpoint(old);
                    if (old.active || old.finished) { offer = Offer(old, effect); error = null; return true; }
                    RequirePersistence(persist); RequireAvailable(floor, nodeId);
                    var resumed = Clone(_state); var entry = resumed.entries.Single(value => value.floorId == old.floorId && value.nodeId == old.nodeId);
                    // Explicitly leaving ends that visit. A production revisit supplies
                    // a fresh admitted subset; restoring an active pending visit above
                    // never consults today's provider and remains exactly frozen.
                    if (context != null)
                    {
                        RequireContext(effect, context);
                        if (context.PoolPolicyCanonicalJson != entry.poolPolicyJson)
                            throw Bad("A revisit cannot change the frozen duplicate construction policy.");
                        var refreshedRoster = CaptureCards(_run.CaptureInteractionDraft().Cards);
                        var refreshedCandidates = context.CaptureCandidates();
                        foreach (var candidate in refreshedCandidates)
                            if (!refreshedRoster.Any(value => value.canonicalJson == candidate.canonicalJson))
                                throw Bad("Refreshed candidate is not the exact current Run physical instance.");
                        entry.openingRoster = refreshedRoster; entry.candidates = refreshedCandidates;
                        entry.eligibilityUnlocked = context.Eligibility.IsUnlocked;
                        entry.qualifyingRunId = context.Eligibility.QualifyingRunId;
                    }
                    SetCheckpoint(entry, _run.CaptureInteractionDraft()); entry.active = true;
                    Commit(resumed, floor, effect, () => Begin(floor, nodeId), persist);
                    offer = Offer(entry, effect); error = null; return true;
                }
                RequirePersistence(persist); RequireAvailable(floor, nodeId);
                if (floor.TryGetInteractionReceipt(effect.effectId, out _)) throw Bad("Duplicate receipt has no owner journal.");
                if (context == null) throw Bad("An explicit admitted subset and breakthrough policy are required.");
                RequireContext(effect, context);
                var draft = _run.CaptureInteractionDraft(); var roster = CaptureCards(draft.Cards); var candidates = context.CaptureCandidates();
                foreach (var candidate in candidates)
                    if (!roster.Any(value => value.canonicalJson == candidate.canonicalJson)) throw Bad("Candidate is not the exact current Run physical instance.");
                var fresh = new DuplicateOfferingEntryData { floorId = _run.CurrentFloorContentId, nodeId = nodeId, effectId = effect.effectId,
                    effectFingerprint = EffectFingerprint(effect), active = true, sourceDocumentJson = context.Document.CanonicalJson,
                    sourceFingerprint = context.Document.Fingerprint, sourceRootId = context.RootContentId,
                    poolPolicyId = context.PoolPolicyId, poolPolicyJson = context.PoolPolicyCanonicalJson,
                    poolPolicyFingerprint = Hash(context.PoolPolicyCanonicalJson), eligibilityUnlocked = context.Eligibility.IsUnlocked,
                    qualifyingRunId = context.Eligibility.QualifyingRunId, openingRoster = roster, candidates = candidates,
                    rosterAfter = new List<DuplicateOfferingCardData>() };
                SetCheckpoint(fresh, draft); var next = Clone(_state); next.entries.Add(fresh);
                Commit(next, floor, effect, () => Begin(floor, nodeId), persist);
                offer = Offer(fresh, effect); error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        public bool TrySubmitAndComplete(string nodeId, int sourceRuntimeInstanceId, out DuplicateOfferingReceipt receipt,
            out string error, Action persist, ICardAuthorReferenceResolver references)
        {
            receipt = null;
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = Find(nodeId) ?? throw Bad("Open and save the duplicate offer first.");
                RequireEntry(effect, old); RequireLiveCheckpoint(old);
                if (old.finished)
                {
                    if (old.selectedRuntimeInstanceId != sourceRuntimeInstanceId) throw Bad("The completed duplicate used a different original instance.");
                    receipt = ReadReceipt(floor, effect, old); error = null; return true;
                }
                if (!old.active) throw Bad("Reopen the dormant cache before submitting.");
                RequirePersistence(persist); if (references == null) throw Bad("Submit requires explicit authoritative frozen-author reference bindings.");
                var candidate = old.candidates.SingleOrDefault(value => ParseCard(value).runtimeInstanceId == sourceRuntimeInstanceId);
                if (candidate == null || !old.checkpointRoster.Any(value => value.canonicalJson == candidate.canonicalJson) || Consumed(sourceRuntimeInstanceId))
                    throw Bad("The selected frozen original is unavailable or was consumed.");
                var draft = _run.CaptureInteractionDraft(); var next = Clone(_state);
                var entry = next.entries.Single(value => value.floorId == old.floorId && value.nodeId == old.nodeId);
                LevelInteractionReceipt applied = default;
                Commit(next, floor, effect, () =>
                {
                    string before = _run.SerializeForDeterministicComparison();
                    CardConfig original = null, duplicate = null;
                    try
                    {
                        original = DungeonRunCardCodec.Restore(ParseCard(candidate), references);
                        int id = _run.AllocateInteractionCardId();
                        if (id <= 0 || ReservesInstanceId(id) || draft.Cards.Any(card => card.runtimeInstanceId == id)) throw Bad("Allocated copy ID is already reserved.");
                        duplicate = CardInstanceFactory.CreatePersistentDuplicate(original, id, DuplicateOfferingPreparedContext.ReadPolicy(entry.poolPolicyId, entry.poolPolicyJson));
                        var copied = DungeonRunCardCodec.Capture(duplicate, references); ValidateCopy(ParseCard(candidate), copied,
                            DuplicateOfferingPreparedContext.ReadPolicy(entry.poolPolicyId, entry.poolPolicyJson));
                        if (_run.SerializeForDeterministicComparison() != before) throw Bad("Author resolution changed the live Run during copy preparation.");
                        draft.Cards.Add(copied); entry.active = false; entry.finished = true;
                        entry.selectedRuntimeInstanceId = sourceRuntimeInstanceId; entry.createdRuntimeInstanceId = id;
                        entry.createdCardJson = DungeonRunCardCodec.CanonicalJson(copied); entry.rosterAfter = CaptureCards(draft.Cards);
                        entry.forcedOpeningAfter = draft.ForcedOpeningCardId; entry.jadeAfter = entry.jadeBefore;
                        _run.CommitInteractionDraft(draft);
                        if (!floor.TryRecordExternalEffect(nodeId, effect, 0, 1, 1, out applied, out string failure, id, copied.contentId) || !applied.NewlyApplied)
                            throw Bad(failure ?? "Duplicate receipt was already granted.");
                        if (!floor.TryCompleteInteraction(nodeId, out failure)) throw Bad(failure);
                    }
                    finally { DungeonRunCardCodec.Release(duplicate); DungeonRunCardCodec.Release(original); }
                }, persist);
                receipt = new DuplicateOfferingReceipt(entry, applied); error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        public bool TryBack(string nodeId, out string error, Action persist) => TryLeave(nodeId, out error, persist);
        public bool TryLeave(string nodeId, out string error, Action persist)
        {
            try
            {
                Guard(nodeId); var floor = _run.CurrentFloor; var effect = RequireEffect(floor, nodeId); var old = Find(nodeId);
                if (old == null) { RequireAvailable(floor, nodeId); error = null; return true; }
                RequireEntry(effect, old); RequireLiveCheckpoint(old);
                if (old.finished) throw Bad("A completed duplicate cannot be canceled.");
                if (!old.active) { error = null; return true; }
                RequirePersistence(persist); var next = Clone(_state);
                next.entries.Single(value => value.floorId == old.floorId && value.nodeId == old.nodeId).active = false;
                Commit(next, floor, effect, () => { if (!floor.TryCancelUncommittedInteraction(nodeId, out string failure)) throw Bad(failure); }, persist);
                error = null; return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
        private void SetCheckpoint(DuplicateOfferingEntryData entry, DungeonRunInteractionDraft draft)
        { entry.checkpointRoster = CaptureCards(draft.Cards); entry.forcedOpeningBefore = draft.ForcedOpeningCardId; entry.jadeBefore = _run.RunProgression.RunJadeEarned; }
        private void Commit(DungeonRunDuplicateOfferingSaveData next, LevelRunSession floor, LevelInteractionEffect effect, Action apply, Action persist)
        {
            var previous = _state; string expectedEffect = EffectFingerprint(effect); _committing = true;
            try
            {
                _run.CommitRewardState(() => { apply(); ValidateLocal(next); _state = next; RequireCheckpoint(floor, effect.effectId, expectedEffect); }, () =>
                {
                    string checkpoint = _run.SerializeForDeterministicComparison(); persist();
                    if (_run.SerializeForDeterministicComparison() != checkpoint) throw Bad("Strict save changed the prepared duplicate Run checkpoint.");
                    RequireCheckpoint(floor, effect.effectId, expectedEffect);
                });
            }
            catch { _state = previous; throw; }
            finally { _committing = false; }
        }
        private void Guard(string nodeId)
        {
            RequireId(nodeId);
            if (IsCommitting || _run.IsRewardCommitInProgress || _run.TryGetConclusion(out _) || _run.IsDungeonBonusGranted || _run.IsLevelBonusGranted ||
                _run.Interactions.HasOwnPendingChoice || _run.Arcanas.HasPendingChoice || _run.CardOfferings.HasPendingChoice ||
                _run.CardStatOfferings.HasPendingChoice || _run.GemChests.HasPendingChoice || _run.Crystals.HasPendingEvent ||
                _run.GunForHire.HasPendingChoice || _run.Invitations.HasPendingChoice || HasPendingChoice && PendingNodeContentId != nodeId) throw Bad("Resolve the active Run reward transaction first.");
            var floor = _run.CurrentFloor;
            if (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) ||
                !string.IsNullOrEmpty(floor.ActiveInteractionNodeId) && floor.ActiveInteractionNodeId != nodeId) throw Bad("Duplicate requires its matching idle map node.");
        }
        private DuplicateOfferingEntryData Find(string nodeId) => _state.entries.SingleOrDefault(value => value.floorId == _run.CurrentFloorContentId && value.nodeId == nodeId);
        private DuplicateOfferingOffer Offer(DuplicateOfferingEntryData entry, LevelInteractionEffect effect)
            => new DuplicateOfferingOffer(entry, _run.CaptureInteractionDraft().Cards, entry.finished ? ReadReceipt(_run.CurrentFloor, effect, entry) : null);
        private void RequireLiveCheckpoint(DuplicateOfferingEntryData entry)
        {
            var floor = _run.CurrentFloor;
            if (entry.finished) { ReadReceipt(floor, RequireEffect(floor, entry.nodeId), entry); return; }
            if (!entry.active)
            { if (floor.GetNodeState(entry.nodeId) != LevelNodeViewState.Available || floor.TryGetInteractionReceipt(entry.effectId, out _)) throw Bad("Dormant duplicate node changed."); return; }
            var draft = _run.CaptureInteractionDraft();
            if (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _) || floor.ActiveInteractionNodeId != entry.nodeId ||
                floor.GetNodeState(entry.nodeId) != LevelNodeViewState.Completing || !RosterEquals(entry.checkpointRoster, CaptureCards(draft.Cards)) ||
                draft.ForcedOpeningCardId != entry.forcedOpeningBefore || _run.RunProgression.RunJadeEarned != entry.jadeBefore)
                throw Bad("Active duplicate physical roster/opening-hand/income checkpoint changed.");
        }
        private void RequireCheckpoint(LevelRunSession floor, string effectId, string expected)
        {
            var entry = _state.entries.Single(value => value.floorId == floor.Profile.contentId && value.effectId == effectId);
            if (!ReferenceEquals(floor, _run.CurrentFloor) || EffectFingerprint(RequireEffect(floor, entry.nodeId)) != expected) throw Bad("Canonical duplicate floor/effect changed during save.");
            RequireLiveCheckpoint(entry);
        }
        private void RequirePlanDocument(string document)
        { if (_run.SideEvents.HasSourcePlan && _run.SideEvents.Plan.Document.CanonicalJson != document) throw Bad("Duplicate source differs from the Run's frozen source plan."); }
        private void RequireContext(LevelInteractionEffect effect, DuplicateOfferingPreparedContext context)
        {
            var contract = effect.duplicateOffering;
            if (context.RootContentId != contract.rootContentId || context.Document.Fingerprint != contract.sourceFingerprint || context.PoolPolicyId != contract.poolPolicyId)
                throw Bad("Prepared context differs from the canonical duplicate contract.");
            RequirePlanDocument(context.Document.CanonicalJson);
        }
        private bool Consumed(int id) => _run.DoubleKeyPayments.WasConsumed(id) || _run.CardOfferings.WasConsumed(id) || _run.GemChests.HasConsumed(id) || _run.CardStatOfferings.HasConsumed(id);
        private static void Begin(LevelRunSession floor, string nodeId) { if (!floor.TryBeginInteraction(nodeId, out string error)) throw Bad(error); }
        private static void RequireAvailable(LevelRunSession floor, string nodeId)
        { if (floor.GetNodeState(nodeId) != LevelNodeViewState.Available || !string.IsNullOrEmpty(floor.ActiveInteractionNodeId)) throw Bad("Duplicate node is not available."); }
        private static void RequirePersistence(Action persist) { if (persist == null) throw Bad("An explicit strict persistence callback is required."); }
        private static void RequireEntry(LevelInteractionEffect effect, DuplicateOfferingEntryData entry)
        {
            var contract = effect.duplicateOffering;
            if (entry.effectId != effect.effectId || entry.effectFingerprint != EffectFingerprint(effect) || entry.sourceRootId != contract.rootContentId ||
                entry.sourceFingerprint != contract.sourceFingerprint || entry.poolPolicyId != contract.poolPolicyId) throw Bad("Duplicate journal differs from its canonical effect contract.");
        }
        private static DuplicateOfferingReceipt ReadReceipt(LevelRunSession floor, LevelInteractionEffect effect, DuplicateOfferingEntryData entry)
        {
            var copied = DungeonRunCardCodec.Parse(entry.createdCardJson);
            if (!entry.finished || floor.GetNodeState(entry.nodeId) != LevelNodeViewState.Completed || floor.ActiveInteractionNodeId == entry.nodeId ||
                !floor.TryGetInteractionReceipt(effect.effectId, out var receipt) || receipt.Kind != LevelInteractionEffectKind.DuplicateOffering ||
                !IsValidFloorSummary(receipt.ValueBefore, receipt.ValueAfter, receipt.Amount, receipt.TargetRuntimeInstanceId, receipt.TargetContentId) ||
                receipt.TargetRuntimeInstanceId != entry.createdRuntimeInstanceId || receipt.TargetContentId != copied.contentId || receipt.Rarity != effect.rarity ||
                (receipt.PresentationId ?? "") != (effect.presentationId ?? "") || (receipt.SummaryText ?? "") != (effect.summaryText ?? "") || (receipt.DetailText ?? "") != (effect.detailText ?? ""))
                throw Bad("Duplicate completion receipt differs from its unique durable grant.");
            return new DuplicateOfferingReceipt(entry, receipt);
        }
        internal void RequireVisitedFloors(IReadOnlyList<LevelRunSession> floors)
        {
            ValidateLocal(_state); var generated = new HashSet<int>(); var live = _run.CaptureInteractionDraft().Cards;
            foreach (var entry in _state.entries)
            {
                var floor = floors.SingleOrDefault(value => value.Profile.contentId == entry.floorId) ?? throw Bad("Duplicate proof refers to an unvisited floor.");
                var effect = RequireEffect(floor, entry.nodeId); RequireEntry(effect, entry); RequirePlanDocument(entry.sourceDocumentJson);
                if (entry.finished)
                {
                    ReadReceipt(floor, effect, entry);
                    if (!generated.Add(entry.createdRuntimeInstanceId) || _run.GunForHire.HasGeneratedInstanceId(entry.createdRuntimeInstanceId) ||
                        live.Any(value => value.runtimeInstanceId == entry.createdRuntimeInstanceId) && Consumed(entry.createdRuntimeInstanceId))
                        throw Bad("Created duplicate identity conflicts with a consumed/current physical instance.");
                    // Either original or created card may be legally consumed or evolved later.
                }
                else if (entry.active)
                {
                    if (!ReferenceEquals(floor, _run.CurrentFloor) || _run.TryGetConclusion(out _) || _run.IsDungeonBonusGranted || _run.IsLevelBonusGranted) throw Bad("Active duplicate conflicts with a terminal/current floor.");
                    RequireLiveCheckpoint(entry);
                    if (floor.TryGetInteractionReceipt(effect.effectId, out _)) throw Bad("Pending duplicate already has a receipt.");
                }
                else if (floor.GetNodeState(entry.nodeId) != LevelNodeViewState.Available || floor.TryGetInteractionReceipt(effect.effectId, out _)) throw Bad("Dormant duplicate has a completion proof.");
            }
            foreach (var floor in floors)
            {
                foreach (var node in floor.Nodes)
                    foreach (var effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                        if (effect?.kind == LevelInteractionEffectKind.DuplicateOffering &&
                            (floor.GetNodeState(node.contentId) == LevelNodeViewState.Completed || floor.TryGetInteractionReceipt(effect.effectId, out _)) &&
                            !_state.entries.Any(value => value.finished && value.floorId == floor.Profile.contentId && value.nodeId == node.contentId && value.effectId == effect.effectId)) throw Bad("Completed duplicate lacks a journal proof.");
                foreach (var receipt in floor.CaptureSaveData().interactionReceipts)
                    if (receipt.kind == LevelInteractionEffectKind.DuplicateOffering && !_state.entries.Any(value => value.finished && value.floorId == floor.Profile.contentId && value.effectId == receipt.effectId)) throw Bad("Orphan duplicate receipt.");
            }
            if (HasPendingChoice && (_run.Interactions.HasOwnPendingChoice || _run.Arcanas.HasPendingChoice || _run.CardOfferings.HasPendingChoice ||
                _run.CardStatOfferings.HasPendingChoice || _run.GemChests.HasPendingChoice || _run.Crystals.HasPendingEvent || _run.GunForHire.HasPendingChoice || _run.Invitations.HasPendingChoice)) throw Bad("Duplicate and another owner are both pending.");
        }
        internal static LevelInteractionEffect RequireEffect(LevelRunSession floor, string nodeId)
        {
            var node = floor.FindNode(nodeId);
            if (node == null || !floor.IsNodeEnabled(nodeId) || node.nodeType != LevelNodeType.Interaction || node.completionEffects?.Count != 1) throw Bad("Duplicate requires one canonical enabled interaction effect.");
            var effect = node.completionEffects[0]; var errors = new List<string>(); ValidateEffect(effect, errors);
            if (errors.Count != 0) throw Bad(string.Join("; ", errors)); return effect;
        }
        internal static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (effect == null || effect.kind != LevelInteractionEffectKind.DuplicateOffering || effect.duplicateOffering == null || !effect.duplicateOffering.IsSpecified)
            { errors.Add("Duplicate requires its typed source contract."); return; }
            var contract = effect.duplicateOffering;
            try { RequireId(effect.effectId); RequireId(contract.rootContentId); RequireHash(contract.sourceFingerprint); }
            catch (Exception exception) { errors.Add(exception.Message); }
            if (contract.sourceEventId != SourceEventId || contract.poolPolicyId != DuplicateOfferingPreparedContext.ExplicitSubsetPolicyId ||
                effect.amount != 1 || effect.amountMax != 0 || !string.IsNullOrEmpty(effect.targetContentId) || !string.IsNullOrEmpty(effect.sealRewardPoolId) ||
                effect.cashOutJade != 0 || effect.cardChoices?.Count > 0 || effect.sealChoices?.Count > 0 || effect.HasArcanaChoiceContract ||
                effect.HasChickenOfferingContract || effect.HasManaOfferingContract || effect.HasManaStatueContract || effect.HasAbandonedCartContract ||
                effect.HasCrystalThrowingContract || effect.HasCrawlerInvitationContract || effect.HasFixedEvolutionContract || effect.HasTargetLevelResultContract || effect.HasCardStatOfferingContract || effect.HasGunForHireContract ||
                effect.doubleKeyPayment?.IsSpecified == true) errors.Add("Duplicate cannot mix another payload or an unknown source/pool policy.");
        }
        internal static string EffectFingerprint(LevelInteractionEffect effect) => Hash(new ExactJsonWire(typeof(EffectData), typeof(LevelInteractionDuplicateOfferingContract)).Write(
            new EffectData { effectId = effect.effectId, kind = (int)effect.kind, amount = effect.amount, amountMax = effect.amountMax, targetContentId = effect.targetContentId,
                presentationId = effect.presentationId, rarity = (int)effect.rarity, summaryText = effect.summaryText, detailText = effect.detailText,
                sealRewardPoolId = effect.sealRewardPoolId, cashOutJade = effect.cashOutJade, contract = effect.duplicateOffering }));
        private sealed class EffectData
        { public string effectId; public int kind, amount, amountMax; public string targetContentId, presentationId; public int rarity;
          public string summaryText, detailText, sealRewardPoolId; public int cashOutJade; public LevelInteractionDuplicateOfferingContract contract; }
        internal static bool SameContract(LevelInteractionEffect left, LevelInteractionEffect right) => left != null && right != null && EffectFingerprint(left) == EffectFingerprint(right);
        internal static bool IsValidFloorSummary(int before, int after, int amount, int physicalTarget, string contentTarget)
            => before == 0 && after == 1 && amount == 1 && physicalTarget > 0 && !string.IsNullOrEmpty(contentTarget);
        private static InvalidOperationException Bad(string message) => new InvalidOperationException(message);
    }
}
