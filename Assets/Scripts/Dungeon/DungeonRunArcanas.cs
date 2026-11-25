using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns only Arcanas acquired during this Run: frozen offers, active identities,
    /// durable decisions and their validation. Entry equipment remains an account
    /// projection; Baishu remains the separate frozen entry-growth rule.
    /// This module's state and save validation share one acquisition invariant.
    /// The Run aggregate restores floor/other ledgers; this owner restores its own
    /// snapshot. UI, content admission and rule projection are external inputs.
    /// Scale review: wire-shape and acquisition-proof checks keep this file above
    /// 500 lines within one owner boundary. Re-review before adding another rule
    /// lifecycle; do not place battle execution or presentation state here.
    /// </summary>
    public sealed class DungeonRunArcanas
    {
        private readonly DungeonRunSession _run;
        private DungeonRunArcanaSaveData _state;
        private bool _committing;
        public bool HasPendingChoice => _state.hasPendingChoice;
        public string PendingNodeContentId => HasPendingChoice ? _state.pending.nodeContentId : null;
        internal bool IsCommitting => _committing;
        public IReadOnlyList<DungeonRunArcanaDefinition> Active => _state.active
            .Select(item => new DungeonRunArcanaDefinition(item.contentId, item.rule)).ToList().AsReadOnly();

        internal DungeonRunArcanas(DungeonRunSession run, DungeonRunArcanaSaveData saved = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _state = saved == null ? Empty() : Clone(saved);
            ValidateLocal(_state);
        }

        public bool TryOpen(string nodeId, DungeonRunArcanaCatalog catalog,
            out DungeonRunArcanaOffer offer, out string error, Action persist)
        {
            offer = null;
            if (!TryGuard(nodeId, out error)) return false;
            try
            {
                LevelInteractionEffect effect = RequireEffect(_run.CurrentFloor, nodeId);
                var prior = FindCompleted(nodeId);
                if (prior != null)
                {
                    var receipt = RequireReceipt(_run.CurrentFloor, effect, prior);
                    offer = new DungeonRunArcanaOffer(false, Definitions(prior.candidates), true, receipt, prior.skipped);
                    return true;
                }
                if (_run.CurrentFloor.TryGetInteractionReceipt(effect.effectId, out _))
                    throw Invalid("Arcana receipt has no owner decision.");
                if (HasPendingChoice)
                {
                    RequirePendingContract(effect, nodeId);
                    RequireReadableNode(nodeId);
                    offer = PendingOffer();
                    return true;
                }
                RequireCommittingNode(nodeId);
                if (catalog == null || !catalog.AcquisitionUnlocked)
                    throw Invalid("Arcana acquisition is not unlocked.");
                if (catalog.PoolContentId != effect.arcanaChoice.poolContentId)
                    throw Invalid("Resolved Arcana pool does not match the configured contract.");
                var candidates = catalog.EligibleCandidates.Where(item =>
                    item.ContentId != catalog.EquippedContentId &&
                    !_state.active.Exists(active => active.contentId == item.ContentId)).ToList();
                // A local deterministic stream does not consume battle/map random state.
                new SeededRandomSource(OfferSeed(nodeId, effect.effectId)).Shuffle(candidates);
                var next = Clone(_state);
                next.hasPendingChoice = true;
                next.pending = new DungeonRunArcanaPendingSaveData
                {
                    floorContentId = _run.CurrentFloorContentId, nodeContentId = nodeId,
                    effectId = effect.effectId, poolContentId = catalog.PoolContentId,
                    candidateCount = effect.arcanaChoice.candidateCount,
                    equippedContentId = catalog.EquippedContentId,
                    candidates = candidates.Take(effect.arcanaChoice.candidateCount).Select(SaveCandidate).ToList()
                };
                Commit(nodeId, next, () => { }, persist);
                offer = PendingOffer();
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        public bool TryChoose(string nodeId, string contentId, out LevelInteractionReceipt receipt,
            out string error, Action persist)
            => TryResolve(nodeId, contentId, false, false, out receipt, out error, persist);

        public bool TrySkip(string nodeId, out LevelInteractionReceipt receipt,
            out string error, Action persist)
            => TryResolve(nodeId, string.Empty, true, false, out receipt, out error, persist);

        /// <summary>
        /// Durably acquires the frozen candidate and completes its map node in one save.
        /// An older decision still resolving is completed without acquiring twice;
        /// a completed replay returns the original receipt without another save.
        /// </summary>
        public bool TryChooseAndComplete(string nodeId, string contentId, out LevelInteractionReceipt receipt,
            out string error, Action persist)
            => TryResolve(nodeId, contentId, false, true, out receipt, out error, persist);

        /// <summary>Durably skips and completes the node, including an empty offer, without creating an active Arcana.</summary>
        public bool TrySkipAndComplete(string nodeId, out LevelInteractionReceipt receipt,
            out string error, Action persist)
            => TryResolve(nodeId, string.Empty, true, true, out receipt, out error, persist);

        private bool TryResolve(string nodeId, string contentId, bool skipped, bool completeInteraction,
            out LevelInteractionReceipt receipt, out string error, Action persist)
        {
            receipt = default;
            if (!TryGuard(nodeId, out error)) return false;
            try
            {
                LevelInteractionEffect effect = RequireEffect(_run.CurrentFloor, nodeId);
                if (!skipped && !IsStableId(contentId)) throw Invalid("Choose a frozen stable Arcana ID.");
                var prior = FindCompleted(nodeId);
                if (prior != null)
                {
                    if (prior.skipped != skipped || (!skipped && prior.selectedContentId != contentId))
                        throw Invalid("Arcana choice was already resolved differently.");
                    LevelInteractionReceipt replay = RequireReceipt(_run.CurrentFloor, effect, prior);
                    if (completeInteraction && _run.CurrentFloor.GetNodeState(nodeId) != LevelNodeViewState.Completed)
                    {
                        RequireCommittingNode(nodeId);
                        Commit(nodeId, Clone(_state), () => { }, persist, true);
                    }
                    else if (completeInteraction) RequireCompletedNode(nodeId);
                    receipt = WithNewlyApplied(replay, false);
                    return true;
                }
                if (!HasPendingChoice) throw Invalid("Open the Arcana offer before resolving it.");
                RequirePendingContract(effect, nodeId);
                RequireCommittingNode(nodeId);
                var selected = skipped ? null : _state.pending.candidates.Find(item => item.contentId == contentId);
                if (!skipped && (selected == null || _state.active.Exists(item => item.contentId == contentId)))
                    throw Invalid("Arcana is not a legal frozen candidate.");
                var next = Clone(_state);
                var pending = next.pending;
                int before = next.active.Count;
                if (!skipped) next.active.Add(new DungeonRunArcanaActiveSaveData
                {
                    contentId = selected.contentId, rule = selected.rule,
                    floorContentId = pending.floorContentId, nodeContentId = nodeId, effectId = effect.effectId
                });
                var decision = new DungeonRunArcanaCompletedSaveData
                {
                    floorContentId = pending.floorContentId, nodeContentId = nodeId, effectId = effect.effectId,
                    poolContentId = pending.poolContentId, candidateCount = pending.candidateCount,
                    equippedContentId = pending.equippedContentId, candidates = CopyCandidates(pending.candidates),
                    skipped = skipped, selectedContentId = skipped ? string.Empty : contentId,
                    valueBefore = before, valueAfter = next.active.Count
                };
                next.completed.Add(decision);
                next.hasPendingChoice = false;
                next.pending = EmptyPending();
                LevelInteractionReceipt applied = default;
                Commit(nodeId, next, () =>
                {
                    if (!_run.CurrentFloor.TryRecordExternalEffect(nodeId, effect, before, next.active.Count,
                            skipped ? 0 : 1, out applied, out string failure,
                            resolvedTargetContentId: skipped ? string.Empty : contentId))
                        throw Invalid(failure ?? "Arcana receipt could not be recorded.");
                    RequireReceipt(_run.CurrentFloor, effect, decision);
                }, persist, completeInteraction);
                // Skip is a newly durable decision even though its gain amount is zero.
                receipt = WithNewlyApplied(applied, true);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private void Commit(string nodeId, DungeonRunArcanaSaveData next, Action apply, Action persist,
            bool completeInteraction = false)
        {
            if (persist == null) throw Invalid("A durable Arcana action requires an explicit save callback.");
            ValidateLocal(next);
            var previous = _state;
            var floor = _run.CurrentFloor;
            _committing = true;
            try
            {
                _run.CommitRewardState(() =>
                {
                    apply();
                    _state = next;
                    if (completeInteraction && !floor.TryCompleteInteraction(nodeId, out string failure))
                        throw Invalid(failure ?? "Arcana interaction could not be completed.");
                }, () =>
                {
                    persist();
                    if (!ReferenceEquals(floor, _run.CurrentFloor)) throw Invalid("Floor changed during Arcana save.");
                    if (completeInteraction) RequireCompletedNode(nodeId);
                    else RequireCommittingNode(nodeId);
                });
            }
            catch { _state = previous; throw; }
            finally { _committing = false; }
        }

        private bool TryGuard(string nodeId, out string error)
        {
            error = null;
            if (_committing || _run.IsRewardCommitInProgress || _run.Crystals.HasPendingEvent)
            { error = "Run reward transaction is already committing."; return false; }
            if (_run.TryGetConclusion(out _) || _run.Interactions.HasPendingChoice || (_run.ChickenOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy) ||
                (HasPendingChoice && (_state.pending.floorContentId != _run.CurrentFloorContentId ||
                                      PendingNodeContentId != nodeId)) ||
                (!string.IsNullOrEmpty(_run.CurrentFloor.ActiveInteractionNodeId) &&
                 _run.CurrentFloor.ActiveInteractionNodeId != nodeId) ||
                _run.CurrentFloor.ActiveEncounter.HasValue || _run.CurrentFloor.TryGetPendingReturn(out _))
            { error = "Finish the current Run transaction before choosing Arcana."; return false; }
            return true;
        }

        private void RequireReadableNode(string nodeId)
        {
            var view = _run.CurrentFloor.GetNodeState(nodeId);
            if (view == LevelNodeViewState.Completing) { RequireCommittingNode(nodeId); return; }
            if (view != LevelNodeViewState.Available || !string.IsNullOrEmpty(_run.CurrentFloor.ActiveInteractionNodeId))
                throw Invalid("Pending Arcana no longer belongs to a resumable node.");
        }

        private void RequireCommittingNode(string nodeId)
        {
            if (_run.CurrentFloor.ActiveInteractionNodeId != nodeId ||
                _run.CurrentFloor.GetNodeState(nodeId) != LevelNodeViewState.Completing)
                throw Invalid("Begin the matching Arcana map interaction before committing.");
        }

        private void RequireCompletedNode(string nodeId)
        {
            if (_run.CurrentFloor.GetNodeState(nodeId) != LevelNodeViewState.Completed ||
                !string.IsNullOrEmpty(_run.CurrentFloor.ActiveInteractionNodeId) ||
                _run.CurrentFloor.ActiveEncounter.HasValue || _run.CurrentFloor.TryGetPendingReturn(out _))
                throw Invalid("Arcana map interaction did not remain completed during save.");
        }

        private void RequirePendingContract(LevelInteractionEffect effect, string nodeId)
        {
            if (_state.pending.floorContentId != _run.CurrentFloorContentId ||
                _state.pending.nodeContentId != nodeId || _state.pending.effectId != effect.effectId ||
                _state.pending.poolContentId != effect.arcanaChoice.poolContentId ||
                _state.pending.candidateCount != effect.arcanaChoice.candidateCount)
                throw Invalid("Frozen Arcana offer does not match the current contract.");
        }

        private DungeonRunArcanaCompletedSaveData FindCompleted(string nodeId)
            => _state.completed.Find(item => item.floorContentId == _run.CurrentFloorContentId && item.nodeContentId == nodeId);
        private DungeonRunArcanaOffer PendingOffer() => new DungeonRunArcanaOffer(true, Definitions(_state.pending.candidates));
        internal DungeonRunArcanaSaveData CaptureSaveData() => Clone(_state);

        internal static void ValidateEffect(LevelInteractionEffect effect, List<string> errors)
        {
            if (effect == null) { errors.Add("Arcana effect is missing."); return; }
            if (effect.kind != LevelInteractionEffectKind.ChooseArcana || !IsStableId(effect.effectId) ||
                effect.arcanaChoice == null || !IsStableId(effect.arcanaChoice.poolContentId) ||
                effect.arcanaChoice.candidateCount <= 0 || effect.amount != 0 || effect.amountMax != 0 ||
                !string.IsNullOrEmpty(effect.targetContentId) || effect.cashOutJade != 0 ||
                !string.IsNullOrEmpty(effect.sealRewardPoolId) || effect.cardChoices?.Count > 0 ||
                effect.sealChoices?.Count > 0 || effect.HasDoubleKeyPayment ||
                effect.HasCrawlerInvitationContract || effect.HasFixedEvolutionContract || effect.HasTargetLevelResultContract)
                errors.Add("ChooseArcana requires one explicit pool/count contract and no card, seal, amount or target contract.");
        }

        private static LevelInteractionEffect RequireEffect(LevelRunSession session, string nodeId)
            => RequireNodeEffect(session?.FindNode(nodeId));

        private static LevelInteractionEffect RequireNodeEffect(LevelNode node)
        {
            var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
            if (node?.nodeType != LevelNodeType.Interaction) throw Invalid("Node is not a single Arcana interaction.");
            var errors = new List<string>();
            ValidateEffect(effect, errors);
            if (errors.Count != 0) throw Invalid(string.Join(" ", errors));
            return effect;
        }

        internal static DungeonRunArcanaSaveData ValidateRestored(DungeonRunSaveData data,
            IReadOnlyList<LevelRunSession> visited, string rawPayload = null)
        {
            if (data == null) throw Invalid("Run save is missing.");
            if (data.schemaVersion < 23)
            {
                var oldFloors = new List<LevelRunSaveData>(data.completedFloorStates ?? new List<LevelRunSaveData>());
                if (data.currentFloor != null) oldFloors.Add(data.currentFloor);
                if (oldFloors.Any(floor => floor?.interactionReceipts?.Exists(
                        item => item?.kind == LevelInteractionEffectKind.ChooseArcana) == true))
                    throw Invalid("Legacy schema cannot contain an in-run Arcana receipt.");
                return Empty();
            }
            if (!data.hasArcanaState || data.arcana == null) throw Invalid("Schema 23 requires explicit Arcana state.");
            if (rawPayload != null) ValidateWire(data.arcana, rawPayload);
            var state = Clone(data.arcana);
            ValidateLocal(state);
            int acquired = 0;
            foreach (var decision in state.completed)
            {
                var profile = FindProfile(visited, decision.floorContentId);
                var effect = RequireEffect(profile, decision.nodeContentId);
                RequireSavedContract(effect, decision.effectId, decision.poolContentId, decision.candidateCount);
                var floor = FindFloor(data, decision.floorContentId);
                var receipt = floor.interactionReceipts?.Find(item => item.effectId == decision.effectId);
                if (receipt == null || receipt.kind != LevelInteractionEffectKind.ChooseArcana ||
                    receipt.targetContentId != decision.selectedContentId || receipt.amount != (decision.skipped ? 0 : 1) ||
                    receipt.valueBefore != decision.valueBefore || receipt.valueAfter != decision.valueAfter ||
                    receipt.targetRuntimeInstanceId != 0 || decision.valueBefore != acquired ||
                    decision.valueAfter != acquired + (decision.skipped ? 0 : 1))
                    throw Invalid("Arcana decision and floor receipt disagree.");
                var progress = floor.nodeStates?.Find(item => item.nodeContentId == decision.nodeContentId);
                if (progress == null || (progress.progress != LevelNodeProgress.Cleared &&
                    (decision.floorContentId != data.currentFloorContentId || progress.progress != LevelNodeProgress.Resolving ||
                     floor.activeInteractionNodeContentId != decision.nodeContentId)))
                    throw Invalid("Resolved Arcana node has no matching completed or active progress.");
                if (!decision.skipped)
                {
                    if (acquired >= state.active.Count) throw Invalid("Arcana decision lacks its active identity.");
                    var active = state.active[acquired++];
                    var selected = decision.candidates.Find(item => item.contentId == decision.selectedContentId);
                    if (active.contentId != decision.selectedContentId || active.rule != selected.rule ||
                        active.floorContentId != decision.floorContentId || active.nodeContentId != decision.nodeContentId ||
                        active.effectId != decision.effectId) throw Invalid("Active Arcana has no exact acquisition proof.");
                }
            }
            if (acquired != state.active.Count) throw Invalid("Active Arcana lacks a completed decision.");
            if (state.hasPendingChoice)
            {
                var pending = state.pending;
                var profile = FindProfile(visited, pending.floorContentId);
                var effect = RequireEffect(profile, pending.nodeContentId);
                RequireSavedContract(effect, pending.effectId, pending.poolContentId, pending.candidateCount);
                var floor = FindFloor(data, pending.floorContentId);
                var progress = floor.nodeStates?.Find(item => item.nodeContentId == pending.nodeContentId);
                if (pending.floorContentId != data.currentFloorContentId || floor.hasActiveEncounter || floor.hasPendingReturn ||
                    !string.IsNullOrWhiteSpace(data.interaction?.effectId) || progress == null ||
                    (progress.progress != LevelNodeProgress.Available && progress.progress != LevelNodeProgress.Resolving) ||
                    (progress.progress == LevelNodeProgress.Resolving ? floor.activeInteractionNodeContentId != pending.nodeContentId :
                        !string.IsNullOrEmpty(floor.activeInteractionNodeContentId)) ||
                    floor.interactionReceipts?.Exists(item => item.effectId == pending.effectId) == true ||
                    state.completed.Exists(item => item.floorContentId == pending.floorContentId && item.effectId == pending.effectId) ||
                    pending.candidates.Any(item => state.active.Exists(active => active.contentId == item.contentId)))
                    throw Invalid("Pending Arcana is not a unique resumable current-floor choice.");
            }
            // Detect missing owner proof as well as forged extra proof.
            foreach (var profile in visited ?? Array.Empty<LevelRunSession>())
            {
                var floor = FindFloor(data, profile.Profile.contentId);
                foreach (var node in profile.Nodes.Where(n => profile.IsNodeEnabled(n.contentId)))
                {
                    var effect = node?.completionEffects?.Count == 1 ? node.completionEffects[0] : null;
                    if (effect?.kind != LevelInteractionEffectKind.ChooseArcana) continue;
                    RequireEffect(profile, node.contentId);
                    bool resolved = state.completed.Exists(item => item.floorContentId == profile.Profile.contentId && item.nodeContentId == node.contentId);
                    bool hasReceipt = floor.interactionReceipts?.Exists(item => item.effectId == effect.effectId) == true;
                    bool cleared = floor.nodeStates?.Exists(item => item.nodeContentId == node.contentId && item.progress == LevelNodeProgress.Cleared) == true;
                    if (resolved != hasReceipt || (cleared && !resolved)) throw Invalid("Arcana node progress lacks its owner receipt.");
                }
            }
            return state;
        }

        private static void ValidateLocal(DungeonRunArcanaSaveData state)
        {
            if (state.version != 1 || state.active == null || state.completed == null || state.pending == null)
                throw Invalid("Arcana owner version or collections are missing.");
            var activeIds = new HashSet<string>(StringComparer.Ordinal);
            var definitions = new Dictionary<string, DungeonRunArcanaRule>(StringComparer.Ordinal);
            foreach (var item in state.active)
            {
                if (item == null || !IsStableId(item.contentId) || !ValidRule(item.rule) ||
                    !IsStableId(item.floorContentId) || !IsStableId(item.nodeContentId) || !IsStableId(item.effectId) ||
                    !activeIds.Add(item.contentId)) throw Invalid("Active Arcana contains invalid or duplicate identities.");
                BindDefinition(definitions, item.contentId, item.rule);
            }
            var keys = new HashSet<(string Floor, string Effect)>();
            var resolvedIds = new HashSet<string>(StringComparer.Ordinal);
            int acquired = 0;
            foreach (var item in state.completed)
            {
                if (item == null || !keys.Add((item.floorContentId, item.effectId))) throw Invalid("Arcana decisions are duplicated.");
                ValidateOffer(item.floorContentId, item.nodeContentId, item.effectId, item.poolContentId,
                    item.candidateCount, item.equippedContentId, item.candidates);
                foreach (var candidate in item.candidates) BindDefinition(definitions, candidate.contentId, candidate.rule);
                if (item.valueBefore != acquired || item.valueAfter != acquired + (item.skipped ? 0 : 1) ||
                    item.candidates.Any(candidate => resolvedIds.Contains(candidate.contentId)) ||
                    (item.skipped ? !string.IsNullOrEmpty(item.selectedContentId) :
                        !item.candidates.Exists(candidate => candidate.contentId == item.selectedContentId)))
                    throw Invalid("Arcana decision is not one frozen selection or skip.");
                if (!item.skipped)
                {
                    if (acquired >= state.active.Count) throw Invalid("Arcana decision lacks its active identity.");
                    var active = state.active[acquired++];
                    var selected = item.candidates.Find(candidate => candidate.contentId == item.selectedContentId);
                    if (!resolvedIds.Add(item.selectedContentId) || active.contentId != selected.contentId ||
                        active.rule != selected.rule || active.floorContentId != item.floorContentId ||
                        active.nodeContentId != item.nodeContentId || active.effectId != item.effectId)
                        throw Invalid("Arcana active identity lacks its exact completed-choice source.");
                }
            }
            if (acquired != state.active.Count) throw Invalid("Arcana active collection contains unearned identities.");
            var pending = state.pending;
            if (state.hasPendingChoice)
            {
                ValidateOffer(pending.floorContentId, pending.nodeContentId, pending.effectId, pending.poolContentId,
                    pending.candidateCount, pending.equippedContentId, pending.candidates);
                foreach (var candidate in pending.candidates) BindDefinition(definitions, candidate.contentId, candidate.rule);
                if (keys.Contains((pending.floorContentId, pending.effectId)) ||
                    pending.candidates.Any(candidate => activeIds.Contains(candidate.contentId)))
                    throw Invalid("Pending Arcana conflicts with active or completed choices.");
            }
            else if (!string.IsNullOrEmpty(pending.floorContentId) || !string.IsNullOrEmpty(pending.nodeContentId) ||
                !string.IsNullOrEmpty(pending.effectId) || !string.IsNullOrEmpty(pending.poolContentId) ||
                pending.candidateCount != 0 || !string.IsNullOrEmpty(pending.equippedContentId) ||
                pending.candidates == null || pending.candidates.Count != 0)
                throw Invalid("Inactive Arcana checkpoint contains hidden progress.");
        }

        private static void ValidateOffer(string floor, string node, string effect, string pool, int count,
            string equipped, List<DungeonRunArcanaCandidateSaveData> candidates)
        {
            if (!IsStableId(floor) || !IsStableId(node) || !IsStableId(effect) || !IsStableId(pool) || count <= 0 ||
                candidates == null || candidates.Count > count ||
                (!string.IsNullOrEmpty(equipped) && !IsStableId(equipped))) throw Invalid("Frozen Arcana offer is invalid.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in candidates)
                if (item == null || !IsStableId(item.contentId) || !ValidRule(item.rule) ||
                    item.contentId == equipped || !ids.Add(item.contentId)) throw Invalid("Frozen Arcana candidates are invalid.");
        }

        private static void BindDefinition(Dictionary<string, DungeonRunArcanaRule> definitions,
            string id, DungeonRunArcanaRule rule)
        {
            if (definitions.TryGetValue(id, out var previous) && previous != rule)
                throw Invalid("One Arcana identity has conflicting rule definitions.");
            definitions[id] = rule;
        }

        private static LevelInteractionReceipt RequireReceipt(LevelRunSession floor, LevelInteractionEffect effect,
            DungeonRunArcanaCompletedSaveData decision)
        {
            RequireSavedContract(effect, decision.effectId, decision.poolContentId, decision.candidateCount);
            if (!floor.TryGetInteractionReceipt(effect.effectId, out var receipt) ||
                receipt.Kind != LevelInteractionEffectKind.ChooseArcana || receipt.TargetContentId != decision.selectedContentId ||
                receipt.Amount != (decision.skipped ? 0 : 1) || receipt.ValueBefore != decision.valueBefore ||
                receipt.ValueAfter != decision.valueAfter || receipt.TargetRuntimeInstanceId != 0)
                throw Invalid("Stored Arcana receipt disagrees with its decision.");
            return receipt;
        }

        private static void RequireSavedContract(LevelInteractionEffect effect, string id, string pool, int count)
        {
            if (effect.effectId != id || effect.arcanaChoice.poolContentId != pool || effect.arcanaChoice.candidateCount != count)
                throw Invalid("Saved Arcana contract does not match the visited effective contract.");
        }
        private static LevelRunSession FindProfile(IReadOnlyList<LevelRunSession> profiles, string id)
            => profiles?.SingleOrDefault(item => item?.Profile.contentId == id) ?? throw Invalid("Arcana references an unvisited floor.");
        private static LevelRunSaveData FindFloor(DungeonRunSaveData data, string id)
            => (id == data.currentFloorContentId ? data.currentFloor : data.completedFloorStates?.Find(item => item?.profileContentId == id))
               ?? throw Invalid("Arcana floor save is missing.");

        private static void ValidateWire(DungeonRunArcanaSaveData state, string payload)
        {
            if (!JsonObjectShape.TryGetField(payload, "hasArcanaState", out string presence) || presence != "true" ||
                !JsonObjectShape.TryGetField(payload, "arcana", out string raw) || raw == "null")
                throw Invalid("Run payload lacks explicit Arcana wire state.");
            RequireInt(raw, "version", state.version);
            RequireBool(raw, "hasPendingChoice", state.hasPendingChoice);
            var active = RequireArray(raw, "active", state.active?.Count ?? -1);
            for (int i = 0; i < active.Count; i++)
            {
                RequireDefinition(active[i], state.active[i]?.rule ?? (DungeonRunArcanaRule)(-1));
                RequireFields(active[i], "floorContentId", "nodeContentId", "effectId");
            }
            var completed = RequireArray(raw, "completed", state.completed?.Count ?? -1);
            for (int i = 0; i < completed.Count; i++)
            {
                var item = state.completed[i];
                if (item == null) throw Invalid("Null Arcana decision.");
                RequireOfferWire(completed[i], item.candidateCount, item.candidates);
                RequireFields(completed[i], "selectedContentId");
                RequireBool(completed[i], "skipped", item.skipped);
                RequireInt(completed[i], "valueBefore", item.valueBefore);
                RequireInt(completed[i], "valueAfter", item.valueAfter);
            }
            if (!JsonObjectShape.TryGetField(raw, "pending", out string pending) || pending == "null" || state.pending == null)
                throw Invalid("Arcana pending envelope is missing.");
            RequireOfferWire(pending, state.pending.candidateCount, state.pending.candidates);
        }

        private static void RequireOfferWire(string raw, int count, List<DungeonRunArcanaCandidateSaveData> candidates)
        {
            RequireFields(raw, "floorContentId", "nodeContentId", "effectId", "poolContentId", "equippedContentId");
            RequireInt(raw, "candidateCount", count);
            var items = RequireArray(raw, "candidates", candidates?.Count ?? -1);
            for (int i = 0; i < items.Count; i++) RequireDefinition(items[i], candidates[i]?.rule ?? (DungeonRunArcanaRule)(-1));
        }
        private static void RequireDefinition(string raw, DungeonRunArcanaRule rule)
        { RequireFields(raw, "contentId"); RequireInt(raw, "rule", (int)rule); }
        private static void RequireFields(string raw, params string[] fields)
        {
            foreach (string field in fields)
                if (!JsonObjectShape.TryGetField(raw, field, out _)) throw Invalid("Arcana wire field is missing: " + field);
        }
        private static IReadOnlyList<string> RequireArray(string raw, string field, int expected)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out string array) ||
                !JsonObjectShape.TryGetArrayItems(array, out var items) || items.Count != expected)
                throw Invalid("Arcana wire array is missing or inconsistent: " + field);
            return items;
        }
        private static void RequireInt(string raw, string field, int expected)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out string value) ||
                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed != expected)
                throw Invalid("Arcana wire integer is missing or inconsistent: " + field);
        }
        private static void RequireBool(string raw, string field, bool expected)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out string value) || value != (expected ? "true" : "false"))
                throw Invalid("Arcana wire boolean is missing or inconsistent: " + field);
        }

        internal static bool IsStableId(string id) => !string.IsNullOrWhiteSpace(id) && id == id.Trim();
        private static bool ValidRule(DungeonRunArcanaRule rule) => Enum.IsDefined(typeof(DungeonRunArcanaRule), rule);
        private static InvalidOperationException Invalid(string message) => new InvalidOperationException("Run Arcana: " + message);
        private static List<DungeonRunArcanaDefinition> Definitions(IEnumerable<DungeonRunArcanaCandidateSaveData> candidates)
            => candidates.Select(item => new DungeonRunArcanaDefinition(item.contentId, item.rule)).ToList();
        private static DungeonRunArcanaCandidateSaveData SaveCandidate(DungeonRunArcanaDefinition item)
            => new DungeonRunArcanaCandidateSaveData { contentId = item.ContentId, rule = item.Rule };
        private static List<DungeonRunArcanaCandidateSaveData> CopyCandidates(List<DungeonRunArcanaCandidateSaveData> candidates)
            => candidates?.Select(item => item == null ? null : new DungeonRunArcanaCandidateSaveData
                { contentId = item.contentId, rule = item.rule }).ToList();
        private static DungeonRunArcanaPendingSaveData EmptyPending()
            => new DungeonRunArcanaPendingSaveData { candidates = new List<DungeonRunArcanaCandidateSaveData>() };
        private static DungeonRunArcanaSaveData Empty() => new DungeonRunArcanaSaveData
        {
            version = 1, active = new List<DungeonRunArcanaActiveSaveData>(), pending = EmptyPending(),
            completed = new List<DungeonRunArcanaCompletedSaveData>()
        };
        private static DungeonRunArcanaSaveData Clone(DungeonRunArcanaSaveData source)
        {
            var pending = source.pending;
            return new DungeonRunArcanaSaveData
            {
                version = source.version, hasPendingChoice = source.hasPendingChoice,
                active = source.active?.Select(item => item == null ? null : new DungeonRunArcanaActiveSaveData
                { contentId = item.contentId, rule = item.rule, floorContentId = item.floorContentId,
                    nodeContentId = item.nodeContentId, effectId = item.effectId }).ToList(),
                pending = pending == null ? null : new DungeonRunArcanaPendingSaveData
                { floorContentId = pending.floorContentId, nodeContentId = pending.nodeContentId, effectId = pending.effectId,
                    poolContentId = pending.poolContentId, candidateCount = pending.candidateCount,
                    equippedContentId = pending.equippedContentId, candidates = CopyCandidates(pending.candidates) },
                completed = source.completed?.Select(item => item == null ? null : new DungeonRunArcanaCompletedSaveData
                { floorContentId = item.floorContentId, nodeContentId = item.nodeContentId, effectId = item.effectId,
                    poolContentId = item.poolContentId, candidateCount = item.candidateCount,
                    equippedContentId = item.equippedContentId, candidates = CopyCandidates(item.candidates),
                    skipped = item.skipped, selectedContentId = item.selectedContentId,
                    valueBefore = item.valueBefore, valueAfter = item.valueAfter }).ToList()
            };
        }
        private int OfferSeed(string node, string effect)
        {
            unchecked
            {
                int hash = 17;
                foreach (char value in _run.RunId + "\n" + _run.CurrentFloorContentId + "\n" + node + "\n" + effect)
                    hash = hash * 31 + value;
                return hash;
            }
        }
        private static LevelInteractionReceipt WithNewlyApplied(LevelInteractionReceipt receipt, bool applied)
            => new LevelInteractionReceipt(receipt.EffectId, receipt.Kind, receipt.TargetContentId, receipt.PresentationId,
                receipt.Amount, receipt.ValueBefore, receipt.ValueAfter, applied, receipt.SummaryText,
                receipt.DetailText, receipt.Rarity, receipt.TargetRuntimeInstanceId);
    }
}
