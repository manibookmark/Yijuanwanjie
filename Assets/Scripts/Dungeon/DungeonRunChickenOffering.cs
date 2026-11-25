using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;
using static WanjieScroll.Dungeon.DungeonRunCardOfferingSaveCodec;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns one Offering lifecycle: frozen exact Run-card candidates, unsubmitted
    /// cancellation, consumed-instance history and durable completion proofs.
    /// The aggregate retains deck/player/statistics/facts/floor ownership and
    /// rolls them back together; this module restores only its own state.
    /// Content and permanent account eligibility are explicit typed queries.
    /// Persist must only save the staged payload: success means durable publication,
    /// and a thrown failure means it was not published. Arbitrary external side
    /// effects cannot be undone by this synchronous transaction.
    /// Wire, DTO, clone, fingerprint and local payout proof knowledge belongs to
    /// DungeonRunCardOfferingSaveCodec; this owner retains lifecycle and live Run
    /// cross-checks. Historical class/field names preserve GUID and save compatibility.
    /// </summary>
    public sealed class DungeonRunChickenOffering
    {
        private readonly DungeonRunSession _run;
        private DungeonRunChickenOfferingSaveData _state;
        private bool _committing;
        public bool HasPendingChoice => _state.hasPendingChoice;
        public string PendingNodeContentId => HasPendingChoice ? _state.pending.nodeContentId : null;
        internal bool IsCommitting => _committing;

        internal DungeonRunChickenOffering(DungeonRunSession run, DungeonRunChickenOfferingSaveData saved = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _state = saved == null ? CreateEmpty() : Clone(saved);
            ValidateLocal(_state);
        }

        public bool TryOpen(string nodeId, IChickenOfferingContext context,
            out DungeonRunChickenOfferingOffer offer, out string error, Action persist)
            => TryOpenOffering(nodeId, new OfferingContext(context), out offer, out error, persist);

        public bool TryOpenMana(string nodeId, IManaOfferingContext context,
            out DungeonRunChickenOfferingOffer offer, out string error, Action persist)
            => TryOpenOffering(nodeId, new OfferingContext(context), out offer, out error, persist);

        private bool TryOpenOffering(string nodeId, OfferingContext context,
            out DungeonRunChickenOfferingOffer offer, out string error, Action persist)
        {
            offer = null;
            if (!TryGuard(nodeId, out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId, context.Kind);
                var prior = FindCompleted(nodeId);
                if (prior != null)
                {
                    offer = new DungeonRunChickenOfferingOffer(false, Candidates(prior.offer.candidates),
                        true, RequireReceipt(_run.CurrentFloor, effect, prior), prior.offer.kind);
                    return true;
                }
                context.RequireEligibility();
                if (_run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out _))
                    throw Invalid("Receipt has no Offering owner proof.");
                RequireReadableNode(nodeId);
                if (HasPendingChoice)
                {
                    RequirePending(effect, nodeId);
                    ValidateCurrentCandidates(context, _state.pending);
                    offer = PendingOffer();
                    return true;
                }
                if (!_run.HasPlayerState) throw Invalid("Player health has not been established.");
                if (context.Kind == CardOfferingKind.Mana && (!_run.HasManaState || !_run.TryGetMapStartingMana(out _)))
                    throw Invalid("Known Run Mana baseline/balance is required.");
                var candidates = new List<DungeonRunChickenOfferingCandidateSaveData>();
                foreach (var card in _run.CaptureInteractionDraft().Cards)
                {
                    var admission = context.ResolveAdmission(card.contentId);
                    if (admission == null || !admission.IsAllowed) continue;
                    candidates.Add(SaveCandidate(card, admission));
                }
                var next = Clone(_state);
                next.hasPendingChoice = true;
                next.pending = new DungeonRunChickenOfferingPendingSaveData
                {
                    floorContentId = _run.CurrentFloorContentId, nodeContentId = nodeId,
                    effectId = effect.effectId, sourceEventId = SourceEvent(effect), kind = context.Kind,
                    candidates = candidates, candidateFingerprint = CandidateFingerprint(candidates)
                };
                Commit(nodeId, next, () => BeginNode(nodeId), persist, Checkpoint.Pending);
                offer = PendingOffer();
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        /// <summary>Consumes exactly one frozen instance and completes the node in one strict save.</summary>
        public bool TrySubmitAndComplete(string nodeId, int runtimeInstanceId, IChickenOfferingContext context,
            out LevelInteractionReceipt receipt, out string error, Action persist)
            => TrySubmitOffering(nodeId, runtimeInstanceId, new OfferingContext(context), out receipt, out error, persist);

        public bool TrySubmitManaAndComplete(string nodeId, int runtimeInstanceId, IManaOfferingContext context,
            out LevelInteractionReceipt receipt, out string error, Action persist)
            => TrySubmitOffering(nodeId, runtimeInstanceId, new OfferingContext(context), out receipt, out error, persist);

        private bool TrySubmitOffering(string nodeId, int runtimeInstanceId, OfferingContext context,
            out LevelInteractionReceipt receipt, out string error, Action persist)
        {
            receipt = default;
            if (!TryGuard(nodeId, out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId, context.Kind);
                var prior = FindCompleted(nodeId);
                if (prior != null)
                {
                    if (runtimeInstanceId != prior.selectedRuntimeInstanceId)
                        throw Invalid("This Offering was committed with a different exact instance.");
                    receipt = RequireReceipt(_run.CurrentFloor, effect, prior);
                    RequireCheckpoint(nodeId, Checkpoint.Completed);
                    return true;
                }
                context.RequireEligibility();
                if (!HasPendingChoice) throw Invalid("Open and freeze the Offering before submitting.");
                RequirePending(effect, nodeId);
                RequireReadableNode(nodeId);
                ValidateCurrentCandidates(context, _state.pending);
                var selected = _state.pending.candidates.SingleOrDefault(item => item.runtimeInstanceId == runtimeInstanceId);
                if (selected == null) throw Invalid("Selected instance is not in the frozen Offering.");
                bool medicine = context.Kind == CardOfferingKind.Chicken && context.ExperimentalMedicineEnabled;
                var draft = _run.CaptureInteractionDraft();
                int index = draft.Cards.FindIndex(item => item.runtimeInstanceId == runtimeInstanceId);
                if (index < 0 || !MatchesCard(selected, draft.Cards[index]))
                    throw Invalid("Selected Run instance changed after the offer was frozen.");
                draft.Cards.RemoveAt(index);
                if (draft.ForcedOpeningCardId == runtimeInstanceId) draft.ForcedOpeningCardId = 0;
                var next = Clone(_state);
                var decision = new DungeonRunChickenOfferingCompletedSaveData
                {
                    offer = ClonePending(_state.pending), selectedRuntimeInstanceId = runtimeInstanceId,
                    selectedContentId = selected.contentId, experimentalMedicineEnabled = medicine,
                    healingPerFood = context.Kind == CardOfferingKind.Chicken ? ChickenOfferingContracts.HealingPerFood(medicine) : 0,
                    actualHealing = new List<int>()
                };
                next.hasPendingChoice = false;
                next.pending = CreateEmptyPending();
                next.completed.Add(decision);
                LevelInteractionReceipt applied = default;
                Commit(nodeId, next, () =>
                {
                    BeginNode(nodeId);
                    _run.CommitInteractionDraft(draft);
                    int total;
                    if (context.Kind == CardOfferingKind.Chicken)
                    {
                        decision.valueBefore = _run.CurrentHealth;
                        decision.maxHealthAtCommit = _run.MaxHealth;
                        decision.actualHealing = _run.ApplyChickenOfferingHealing(medicine);
                        decision.valueAfter = _run.CurrentHealth;
                        total = decision.actualHealing.Sum();
                    }
                    else
                    {
                        var reward = new DungeonRunManaOfferingRewardSaveData
                        { currentBefore = _run.MapCurrentMana, baseBonusBefore = _run.BaseManaBonus,
                          requestedManaAmount = ManaOfferingContracts.ManaAmount };
                        _run.ApplyPersistentManaPickup();
                        reward.currentAfter = _run.MapCurrentMana; reward.baseBonusAfter = _run.BaseManaBonus;
                        decision.manaReward = reward;
                        decision.valueBefore = reward.currentBefore; decision.valueAfter = reward.currentAfter;
                        total = ManaOfferingContracts.ManaAmount; // Pickup metric is requested amount, not balance delta.
                    }
                    if (!_run.CurrentFloor.TryRecordExternalEffect(nodeId, effect, decision.valueBefore,
                        decision.valueAfter, total, out applied, out string failure,
                        runtimeInstanceId, selected.contentId)) throw Invalid(failure);
                    if (!_run.CurrentFloor.TryCompleteInteraction(nodeId, out failure)) throw Invalid(failure);
                }, persist, Checkpoint.Completed);
                receipt = applied;
                return true;
            }
            catch (Exception exception) { receipt = default; error = exception.Message; return false; }
        }

        /// <summary>No reward, receipt or clearing is produced by an unsubmitted cancellation.</summary>
        public bool TryCancel(string nodeId, out string error, Action persist)
        {
            if (!TryGuard(nodeId, out error)) return false;
            try
            {
                var effect = RequireEffect(_run.CurrentFloor, nodeId);
                if (FindCompleted(nodeId) != null || _run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out _))
                    throw Invalid("A submitted Offering cannot be canceled.");
                if (HasPendingChoice) RequirePending(effect, nodeId);
                RequireReadableNode(nodeId);
                if (!HasPendingChoice && string.IsNullOrEmpty(_run.CurrentFloor.ActiveInteractionNodeId)) return true;
                var next = Clone(_state);
                next.hasPendingChoice = false; next.pending = CreateEmptyPending();
                Commit(nodeId, next, () =>
                {
                    if (!_run.CurrentFloor.TryCancelUncommittedInteraction(nodeId, out string failure)) throw Invalid(failure);
                }, persist, Checkpoint.Canceled);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private enum Checkpoint { Pending, Completed, Canceled }
        private void Commit(string nodeId, DungeonRunChickenOfferingSaveData next, Action apply,
            Action persist, Checkpoint checkpoint)
        {
            if (persist == null) throw Invalid("Mutation requires an explicit strict save callback.");
            var previous = _state; var floor = _run.CurrentFloor;
            _committing = true;
            try
            {
                _run.CommitRewardState(() =>
                {
                    apply(); ValidateLocal(next); _state = next;
                    RequireCheckpoint(nodeId, checkpoint);
                }, () =>
                {
                    persist();
                    if (!ReferenceEquals(floor, _run.CurrentFloor)) throw Invalid("Floor changed during save.");
                    RequireCheckpoint(nodeId, checkpoint);
                });
            }
            catch { _state = previous; throw; }
            finally { _committing = false; }
        }

        private bool TryGuard(string nodeId, out string error)
        {
            error = null;
            if (_committing || _run.IsRewardCommitInProgress || _run.IsSpecializedInteractionBusy || _run.Crystals.HasPendingEvent)
            { error = "Run reward transaction is already committing."; return false; }
            if (_run.TryGetConclusion(out _) || _run.Interactions.HasPendingChoice || _run.Arcanas.HasPendingChoice ||
                (HasPendingChoice && (_state.pending.floorContentId != _run.CurrentFloorContentId || PendingNodeContentId != nodeId)) ||
                (!string.IsNullOrEmpty(_run.CurrentFloor.ActiveInteractionNodeId) && _run.CurrentFloor.ActiveInteractionNodeId != nodeId) ||
                _run.CurrentFloor.ActiveEncounter.HasValue || _run.CurrentFloor.TryGetPendingReturn(out _))
            { error = "Finish the current Run transaction before using an Offering."; return false; }
            return true;
        }
        // Detached typed adapters share the lifecycle; neither owns ledger or Run state.
        private readonly struct OfferingContext
        {
            private readonly IChickenOfferingContext _chicken;
            private readonly IManaOfferingContext _mana;
            public CardOfferingKind Kind { get; }
            public OfferingContext(IChickenOfferingContext context) { _chicken = context; _mana = null; Kind = CardOfferingKind.Chicken; }
            public OfferingContext(IManaOfferingContext context) { _mana = context; _chicken = null; Kind = CardOfferingKind.Mana; }
            public bool ExperimentalMedicineEnabled => _chicken.ExperimentalMedicineEnabled;
            public CardOfferingAdmission ResolveAdmission(string id)
                => Kind == CardOfferingKind.Chicken ? _chicken.ResolveAdmission(id) : _mana.ResolveAdmission(id);
            public void RequireEligibility()
            {
                if (Kind == CardOfferingKind.Chicken)
                {
                    var gate = _chicken?.QueryEligibility();
                    if (gate == null || gate.SourceEventId != ChickenOfferingContracts.SourceEventId || !gate.IsUnlocked)
                        throw Invalid("ChickenShop permanent event eligibility is not unlocked.");
                }
                else
                {
                    var gate = _mana?.QueryEligibility();
                    if (gate == null || gate.SourceEventId != ManaOfferingContracts.SourceEventId || !gate.IsUnlocked)
                        throw Invalid("Mana Offering permanent event eligibility is not unlocked.");
                }
            }
        }
        private void BeginNode(string nodeId)
        {
            if (!_run.CurrentFloor.TryBeginInteraction(nodeId, out string error)) throw Invalid(error);
        }
        private void RequireReadableNode(string nodeId)
        {
            var state = _run.CurrentFloor.GetNodeState(nodeId);
            if (state == LevelNodeViewState.Completing && _run.CurrentFloor.ActiveInteractionNodeId == nodeId) return;
            if (state != LevelNodeViewState.Available || !string.IsNullOrEmpty(_run.CurrentFloor.ActiveInteractionNodeId))
                throw Invalid("Offering node is not resumable.");
        }
        private void RequireCheckpoint(string nodeId, Checkpoint checkpoint)
        {
            var floor = _run.CurrentFloor;
            if (floor.ActiveEncounter.HasValue || floor.TryGetPendingReturn(out _)) throw Invalid("Encounter changed during Offering save.");
            if (checkpoint == Checkpoint.Pending)
            {
                if (!HasPendingChoice || floor.ActiveInteractionNodeId != nodeId || floor.GetNodeState(nodeId) != LevelNodeViewState.Completing)
                    throw Invalid("Frozen Offering checkpoint changed during save.");
            }
            else if (!string.IsNullOrEmpty(floor.ActiveInteractionNodeId) || HasPendingChoice ||
                floor.GetNodeState(nodeId) != (checkpoint == Checkpoint.Completed ? LevelNodeViewState.Completed : LevelNodeViewState.Available))
                throw Invalid("Offering completion/cancellation checkpoint changed during save.");
        }
        private void RequirePending(LevelInteractionEffect effect, string nodeId)
        {
            var pending = _state.pending;
            if (pending.floorContentId != _run.CurrentFloorContentId || pending.nodeContentId != nodeId ||
                pending.effectId != effect.effectId || pending.sourceEventId != SourceEvent(effect) || pending.kind != KindOf(effect))
                throw Invalid("Frozen Offering differs from the current effect contract.");
        }
        private void ValidateCurrentCandidates(OfferingContext context, DungeonRunChickenOfferingPendingSaveData pending)
        {
            ValidateOffer(pending);
            var cards = _run.CaptureInteractionDraft().Cards;
            foreach (var candidate in pending.candidates)
            {
                var card = cards.SingleOrDefault(item => item.runtimeInstanceId == candidate.runtimeInstanceId);
                if (!MatchesCard(candidate, card))
                    throw Invalid("A frozen Offering instance changed or disappeared.");
                var admission = context.ResolveAdmission(candidate.contentId);
                if (admission == null || !admission.IsAllowed || admission.SourceConfigId != candidate.sourceConfigId ||
                    admission.SourceClass != candidate.sourceClass || admission.RawAllowInOfferingTable != candidate.rawAllowInOfferingTable)
                    throw Invalid("Frozen Offering source admission changed.");
            }
        }
        private DungeonRunChickenOfferingCompletedSaveData FindCompleted(string nodeId)
            => _state.completed.Find(item => item.offer.floorContentId == _run.CurrentFloorContentId && item.offer.nodeContentId == nodeId);
        private DungeonRunChickenOfferingOffer PendingOffer() => new DungeonRunChickenOfferingOffer(true, Candidates(_state.pending.candidates), kind: _state.pending.kind);
        internal bool WasConsumed(int id) => _state.completed.Exists(item => item.selectedRuntimeInstanceId == id);
        /// <summary>Only food granted by this typed Offering ledger, not all floor pickups or source collections.</summary>
        public int FoodGrantedOnFloor(string floorContentId)
            => (int)Math.Min(int.MaxValue, (long)_state.completed.Count(item => item.offer.floorContentId == floorContentId && item.offer.kind == CardOfferingKind.Chicken) * ChickenOfferingContracts.FoodCount);
        /// <summary>Requested Mana pickups from this ledger only; no duplicate mutable counter.</summary>
        public int ManaGrantedOnFloor(string floorContentId)
            => _state.completed.Count(item => item.offer.floorContentId == floorContentId && item.offer.kind == CardOfferingKind.Mana);
        internal DungeonRunChickenOfferingSaveData CaptureSaveData() => Clone(_state);

        internal static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            bool chicken = effect?.kind == LevelInteractionEffectKind.ChickenOffering;
            bool mana = effect?.kind == LevelInteractionEffectKind.ManaOffering;
            if ((!chicken && !mana) || !Stable(effect.effectId) ||
                (chicken ? effect.chickenOffering?.sourceEventId != ChickenOfferingContracts.SourceEventId || effect.HasManaOfferingContract
                         : effect.manaOffering?.sourceEventId != ManaOfferingContracts.SourceEventId || effect.HasChickenOfferingContract) ||
                effect.amount != 0 || effect.amountMax != 0 || !string.IsNullOrEmpty(effect.targetContentId) ||
                effect.cashOutJade != 0 || !string.IsNullOrEmpty(effect.sealRewardPoolId) || effect.cardChoices?.Count > 0 ||
                effect.sealChoices?.Count > 0 || effect.HasDoubleKeyPayment || effect.HasCrawlerInvitationContract || effect.HasFixedEvolutionContract ||
                effect.HasTargetLevelResultContract || effect.HasArcanaChoiceContract || effect.HasManaStatueContract || effect.HasAbandonedCartContract)
                errors.Add("Offering requires only its canonical typed event contract; no coin, amount, target or other selection contract.");
        }
        private static LevelInteractionEffect RequireEffect(LevelRunSession session, string nodeId, CardOfferingKind? expected = null)
            => RequireNodeEffect(session?.FindNode(nodeId), expected);

        private static LevelInteractionEffect RequireNodeEffect(LevelNode node, CardOfferingKind? expected = null)
        {
            var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
            var errors = new List<string>(); ValidateEffect(effect, errors);
            if (node?.nodeType != LevelNodeType.Interaction || errors.Count != 0 || (expected.HasValue && KindOf(effect) != expected.Value))
                throw Invalid("Node is not one canonical typed Offering interaction. " + string.Join(" ", errors));
            return effect;
        }
        private static CardOfferingKind KindOf(LevelInteractionEffect effect)
            => effect.kind == LevelInteractionEffectKind.ChickenOffering ? CardOfferingKind.Chicken : CardOfferingKind.Mana;
        private static string SourceEvent(LevelInteractionEffect effect)
            => effect.kind == LevelInteractionEffectKind.ChickenOffering ? effect.chickenOffering.sourceEventId : effect.manaOffering.sourceEventId;
        private static LevelInteractionEffectKind ReceiptKind(CardOfferingKind kind)
            => kind == CardOfferingKind.Chicken ? LevelInteractionEffectKind.ChickenOffering : LevelInteractionEffectKind.ManaOffering;
        private static int RewardAmount(DungeonRunChickenOfferingCompletedSaveData decision)
            => decision.offer.kind == CardOfferingKind.Chicken ? decision.actualHealing.Sum() : ManaOfferingContracts.ManaAmount;
        private static LevelInteractionReceipt RequireReceipt(LevelRunSession floor, LevelInteractionEffect effect,
            DungeonRunChickenOfferingCompletedSaveData decision)
        {
            if (decision.offer.effectId != effect.effectId || decision.offer.kind != KindOf(effect) || decision.offer.sourceEventId != SourceEvent(effect) ||
                !floor.TryGetInteractionReceipt(effect.effectId, out var receipt) ||
                receipt.Kind != ReceiptKind(decision.offer.kind) || receipt.TargetRuntimeInstanceId != decision.selectedRuntimeInstanceId ||
                receipt.TargetContentId != decision.selectedContentId || receipt.Amount != RewardAmount(decision) ||
                receipt.ValueBefore != decision.valueBefore || receipt.ValueAfter != decision.valueAfter ||
                (receipt.PresentationId ?? string.Empty) != (effect.presentationId ?? string.Empty))
                throw Invalid("Offering decision and floor receipt disagree.");
            return receipt;
        }

        internal static DungeonRunChickenOfferingSaveData ValidateRestored(DungeonRunSaveData data,
            IReadOnlyList<LevelRunSession> visited, string payload)
        {
            var floors = new List<LevelRunSaveData>(data.completedFloorStates ?? new List<LevelRunSaveData>());
            if (data.currentFloor != null) floors.Add(data.currentFloor);
            var state = Read(data, payload);
            if (data.schemaVersion < 25)
            {
                if (floors.Any(f => f.interactionReceipts?.Exists(r => r.kind == LevelInteractionEffectKind.ChickenOffering || r.kind == LevelInteractionEffectKind.ManaOffering) == true))
                    throw Invalid("Legacy schema cannot carry Offering proof.");
                return state;
            }
            long totalFood = (long)state.completed.Count(d => d.offer.kind == CardOfferingKind.Chicken) * ChickenOfferingContracts.FoodCount;
            long totalHealing = state.completed.Sum(d => (long)d.actualHealing.Sum());
            int food = DungeonRunCardUnlockFactsSaveData.Read(data, payload).Counts
                .Where(c => c.Metric == CardUnlockMetric.FoodCollected).Sum(c => c.Count);
            if (food < Math.Min(int.MaxValue, totalFood) ||
                (data.statistics?.totalHealing ?? 0) < Math.Min(int.MaxValue, totalHealing))
                throw Invalid("Run food/healing ledgers are below completed Offering proof.");
            foreach (var decision in state.completed)
            {
                var offer = decision.offer;
                var profile = visited.SingleOrDefault(p => p.Profile.contentId == offer.floorContentId) ?? throw Invalid("Offering references an unvisited floor.");
                var effect = RequireEffect(profile, offer.nodeContentId);
                if (offer.effectId != effect.effectId || offer.sourceEventId != SourceEvent(effect) || offer.kind != KindOf(effect))
                    throw Invalid("Saved Offering contract differs from current configuration.");
                var floor = floors.SingleOrDefault(f => f.profileContentId == offer.floorContentId) ?? throw Invalid("Offering floor is missing.");
                var receipt = floor.interactionReceipts?.SingleOrDefault(r => r.effectId == offer.effectId);
                if (receipt == null || receipt.kind != ReceiptKind(offer.kind) ||
                    receipt.targetRuntimeInstanceId != decision.selectedRuntimeInstanceId || receipt.targetContentId != decision.selectedContentId ||
                    receipt.amount != RewardAmount(decision) || receipt.valueBefore != decision.valueBefore || receipt.valueAfter != decision.valueAfter ||
                    (receipt.presentationId ?? string.Empty) != (effect.presentationId ?? string.Empty) ||
                    floor.nodeStates?.SingleOrDefault(n => n.nodeContentId == offer.nodeContentId)?.progress != LevelNodeProgress.Cleared ||
                    data.cards.Exists(card => card.runtimeInstanceId == decision.selectedRuntimeInstanceId))
                    throw Invalid("Saved Offering receipt, consumed instance or completed node disagrees.");
            }
            if (state.hasPendingChoice)
            {
                var pending = state.pending;
                var profile = visited.SingleOrDefault(p => p.Profile.contentId == pending.floorContentId) ?? throw Invalid("Pending Offering floor is missing.");
                var effect = RequireEffect(profile, pending.nodeContentId);
                var floor = data.currentFloor;
                var progress = floor.nodeStates?.SingleOrDefault(n => n.nodeContentId == pending.nodeContentId);
                if (pending.floorContentId != data.currentFloorContentId || pending.effectId != effect.effectId ||
                    pending.sourceEventId != SourceEvent(effect) || pending.kind != KindOf(effect) || floor.hasActiveEncounter || floor.hasPendingReturn ||
                    !string.IsNullOrEmpty(data.interaction?.effectId) || data.arcana?.hasPendingChoice == true ||
                    progress == null || (progress.progress != LevelNodeProgress.Available && progress.progress != LevelNodeProgress.Resolving) ||
                    (progress.progress == LevelNodeProgress.Resolving ? floor.activeInteractionNodeContentId != pending.nodeContentId :
                        !string.IsNullOrEmpty(floor.activeInteractionNodeContentId))) throw Invalid("Pending Offering is not uniquely resumable.");
                foreach (var candidate in pending.candidates)
                {
                    var card = data.cards.SingleOrDefault(c => c.runtimeInstanceId == candidate.runtimeInstanceId);
                    if (!MatchesCard(candidate, card))
                        throw Invalid("Frozen Offering instance differs from saved Run deck.");
                }
            }
            foreach (var profile in visited)
            {
                var floor = floors.Single(f => f.profileContentId == profile.Profile.contentId);
                foreach (var node in profile.Nodes.Where(n => profile.IsNodeEnabled(n.contentId)))
                {
                    foreach (var effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                    {
                        if (effect?.kind != LevelInteractionEffectKind.ChickenOffering && effect?.kind != LevelInteractionEffectKind.ManaOffering) continue;
                        RequireEffect(profile, node.contentId);
                        bool decided = state.completed.Exists(d => d.offer.floorContentId == profile.Profile.contentId && d.offer.nodeContentId == node.contentId);
                        bool receipt = floor.interactionReceipts?.Exists(r => r.effectId == effect.effectId) == true;
                        bool cleared = floor.nodeStates?.Exists(n => n.nodeContentId == node.contentId && n.progress == LevelNodeProgress.Cleared) == true;
                        if (decided != receipt || decided != cleared || (state.hasPendingChoice && state.pending.floorContentId == profile.Profile.contentId && state.pending.effectId == effect.effectId && decided))
                            throw Invalid("Offering floor has missing or extra owner proof.");
                    }
                }
            }
            foreach (var floor in floors)
                foreach (var receipt in floor.interactionReceipts ?? new List<LevelInteractionReceiptSaveData>())
                {
                    if (receipt.kind != LevelInteractionEffectKind.ChickenOffering && receipt.kind != LevelInteractionEffectKind.ManaOffering) continue;
                    var profile = visited.Single(p => p.Profile.contentId == floor.profileContentId);
                    var nodes = profile.Nodes.Where(n => profile.IsNodeEnabled(n.contentId)).Where(n => n.completionEffects?.Any(e => e?.effectId == receipt.effectId) == true).ToList();
                    if (nodes.Count != 1) throw Invalid("Orphan Offering receipt.");
                    var effect = RequireEffect(profile, nodes[0].contentId);
                    if (!state.completed.Any(d => d.offer.floorContentId == profile.Profile.contentId && d.offer.nodeContentId == nodes[0].contentId &&
                        d.offer.effectId == effect.effectId && ReceiptKind(d.offer.kind) == receipt.kind))
                        throw Invalid("Offering receipt has no matching typed owner proof.");
                }
            return state;
        }

        // A single Mana fact cannot pay for both independent pickup sources.
        // Other sources may increase these ledgers, so validation is a saturated lower bound.
        internal static void RequireCombinedManaFacts(DungeonRunSaveData data, DungeonRunChickenOfferingSaveData offerings,
            DungeonRunManaStatueSaveData statues, string payload)
        {
            long count = (long)offerings.completed.Count(d => d.offer.kind == CardOfferingKind.Mana) + statues.entries.Count(e => e.collected);
            long mana = DungeonRunCardUnlockFactsSaveData.Read(data, payload).Counts
                .Where(c => c.Metric == CardUnlockMetric.ManaCollected).Sum(c => (long)c.Count);
            long minimum = Math.Min(int.MaxValue, count);
            if (mana < minimum || data.baseManaBonus < minimum || (count > 0 && !data.hasManaState))
                throw Invalid("Combined statue/Offering Mana bonus or pickup facts are below completed proof.");
        }
        private static bool Stable(string id) => !string.IsNullOrWhiteSpace(id) && id == id.Trim();
        private static InvalidOperationException Invalid(string text) => new InvalidOperationException("Run ChickenOffering: " + text);
        private static IReadOnlyList<DungeonRunChickenOfferingCandidate> Candidates(IEnumerable<DungeonRunChickenOfferingCandidateSaveData> values)
            => values.Select(c => new DungeonRunChickenOfferingCandidate(c.runtimeInstanceId, c.contentId)).ToList().AsReadOnly();
    }

    public sealed class DungeonRunChickenOfferingCandidate
    {
        public int RuntimeInstanceId { get; }
        public string ContentId { get; }
        internal DungeonRunChickenOfferingCandidate(int id, string contentId) { RuntimeInstanceId = id; ContentId = contentId; }
    }
    public sealed class DungeonRunChickenOfferingOffer
    {
        public CardOfferingKind Kind { get; }
        public bool Pending { get; }
        public bool CanCancel => Pending;
        public IReadOnlyList<DungeonRunChickenOfferingCandidate> Candidates { get; }
        public bool HasReceipt { get; }
        public LevelInteractionReceipt Receipt { get; }
        internal DungeonRunChickenOfferingOffer(bool pending, IReadOnlyList<DungeonRunChickenOfferingCandidate> candidates,
            bool hasReceipt = false, LevelInteractionReceipt receipt = default, CardOfferingKind kind = CardOfferingKind.Chicken)
        { Pending = pending; Candidates = candidates; HasReceipt = hasReceipt; Receipt = receipt; Kind = kind; }
    }
}
