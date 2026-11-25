using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Detached source provenance and the currently captured native-stage
    /// position. Origin identifies placement; CompletedSourceFloors identifies the
    /// latest activated stage, not the physical five-floor ordinal. Crystal Begin
    /// freezes this value as a project durability guarantee; VC reads it on each hit.</summary>
    public sealed class DungeonRunCrystalSourceContext
    {
        public string SourceContentId { get; }
        public string SourceFingerprint { get; }
        public string OriginSourceLayerContentId { get; }
        public int OriginSourceLayerIndex { get; }
        public string CurrentSourceLayerContentId { get; }
        public int CompletedSourceFloors { get; }
        public int SourceFloorCount { get; }
        internal DungeonRunCrystalSourceContext(SideEventSourceWireDocument document,
            string origin, int originIndex, string current, int currentIndex)
        {
            SourceContentId = document.Definition.ContentId; SourceFingerprint = document.Fingerprint;
            OriginSourceLayerContentId = origin; OriginSourceLayerIndex = originIndex;
            CurrentSourceLayerContentId = current; CompletedSourceFloors = currentIndex;
            SourceFloorCount = document.Definition.Layers.Count;
        }
    }

    /// <summary>
    /// One Run-owned journal binds complete source, time-specific qualification,
    /// allocator outcomes and actual logical placement to the effective floor.
    /// Core remains the only qualification/selection algorithm owner; the adapter
    /// owns adopted mechanic parameters and the directory owns payload/topology.
    /// This owner never qualifies recovery with today's account or rerolls it.
    /// Future phases retain the source document but do not capture future gates.
    /// Scale review: journal wire and cross-proof validation belong to this one
    /// state boundary. No node progress, reward ledger, UI or Meta is duplicated.
    /// Reassess before adding an unrelated rule or a second mutable lifecycle.
    /// Scope review: the approved prefix is immutable plan knowledge captured in
    /// this same journal. Versioned canonical encoding and legacy byte preservation
    /// remain one checkpoint boundary; no new mutable progress or approval owner is added.
    /// </summary>
    public sealed class DungeonRunSideEventState
    {
        internal const int Version = 2;
        private readonly DungeonRunSession _run;
        private readonly DungeonRunSideEventPlan _plan;
        private readonly IReadOnlyList<string> _initialCrawlerIds;
        private Envelope _state;
        private bool _committing;
        internal bool IsCommitting => _committing;
        public bool HasSourcePlan => _plan != null;
        public string Fingerprint => _state.fingerprint;
        public DungeonRunSideEventPlan Plan => _plan;

        /// <summary>Read-only due-phase projection from the one committed journal.</summary>
        public bool TryGetDuePhase(out DungeonRunSideEventPhaseBinding phase)
        {
            phase = null;
            if (_plan == null || _run == null) return false;
            foreach (var binding in _plan.Phases)
            {
                if (!_plan.IsSourceLayerApproved(binding.SourceLayerContentId)) return false;
                var row = _state.phases.Find(p => p.phaseId == binding.PhaseContentId);
                if (row.activated) continue;
                if (binding.FloorContentId == _run.CurrentFloorContentId &&
                    binding.Trigger == DungeonRunSideEventPhaseTrigger.AfterEncounter &&
                    _run.CurrentFloor.GetNodeState(binding.TriggerNodeContentId) == LevelNodeViewState.Completed)
                { phase = binding; return true; }
                // A later phase never overtakes the first uncaptured source time.
                return false;
            }
            return false;
        }

        /// <summary>Reads only the committed current-floor placement and complete
        /// captured phase proof. No qualification, RNG, placement or save is performed.
        /// Unmapped, inactive and failed placements cannot supply an XP context.</summary>
        public bool TryGetCrystalSourceContext(string nodeId, string effectId,
            out DungeonRunCrystalSourceContext context, out string error)
        {
            context = null; error = null;
            try
            {
                DungeonRunSideEventSlotBinding.RequireId(nodeId);
                DungeonRunSideEventSlotBinding.RequireId(effectId);
                if (_run == null || _plan == null || !TracksFloor(_run.CurrentFloorContentId))
                    throw Bad("Crystal source context requires the current entered source floor.");
                ValidateJournal(); ValidateFloor(_run.CurrentFloor);
                PhaseData origin = null; PlacementData placed = null;
                int originIndex = -1, currentIndex = -1; string currentLayer = null;
                foreach (var phase in _state.phases)
                {
                    if (!phase.activated) continue;
                    int index = -1;
                    for (int i = 0; i < _plan.Document.Definition.Layers.Count; i++)
                        if (_plan.Document.Definition.Layers[i].ContentId == phase.sourceLayerId) index = i;
                    if (index < 0) throw Bad("Captured phase references an unknown native source layer.");
                    if (index > currentIndex) { currentIndex = index; currentLayer = phase.sourceLayerId; }
                    if (phase.floorId != _run.CurrentFloorContentId) continue;
                    foreach (var placement in phase.placements)
                    {
                        if (!placement.placed || placement.nodeId != nodeId) continue;
                        if (placed != null || placement.effectId != effectId)
                            throw Bad("Crystal source node and independent effect identity disagree.");
                        origin = phase; placed = placement; originIndex = index;
                    }
                }
                if (placed == null || currentIndex < originIndex)
                    throw Bad("Crystal has no activated successful source placement.");
                var generation = SideEventSourceGeneration.RestoreCanonicalJson(origin.generationJson);
                var attempt = generation.Steps[placed.stepIndex].Attempt;
                if (attempt.Selection.Member.Root.Kind != SideEventSourceKind.Crystal ||
                    !DungeonSideEventSourceAdapter.TryValidateNode(attempt,
                        _run.CurrentFloor.FindNode(nodeId), placed.adapterId, out _))
                    throw Bad("The placed source is not its exact adopted Crystal contract.");
                context = new DungeonRunCrystalSourceContext(_plan.Document, origin.sourceLayerId,
                    originIndex, currentLayer, currentIndex);
                return true;
            }
            catch (Exception exception) { context = null; error = exception.Message; return false; }
        }

        [Serializable] private sealed class Envelope
        {
            public int version;
            public int approvedSourceLayerCount;
            public string sourceDocumentJson;
            public List<PhaseData> phases;
            public List<FloorData> floors;
            public string fingerprint;
        }
        // Retains the exact v1 canonical field order and hash. Its implicit
        // scope was the full plan; reading it never invents partial approval.
        [Serializable] private sealed class LegacyEnvelope
        {
            public int version;
            public string sourceDocumentJson;
            public List<PhaseData> phases;
            public List<FloorData> floors;
            public string fingerprint;
        }
        [Serializable] private sealed class PhaseData
        {
            public string phaseId, floorId, sourceLayerId, triggerNodeId;
            public int trigger;
            public List<SlotData> slots;
            public bool activated;
            public string generationJson;
            public List<PlacementData> placements;
        }
        [Serializable] private sealed class SlotData { public string sourceSlotId, nodeId, effectId; }
        [Serializable] private sealed class PlacementData
        {
            public int stepIndex;
            public string sourceSlotId, rootId, nodeId, effectId, adapterId;
            public bool placed;
        }
        [Serializable] private sealed class FloorData { public string floorId, directoryFingerprint; }

        internal DungeonRunSideEventState(DungeonRunSession run, DungeonRunSideEventPlan plan = null,
            IReadOnlyList<string> initialCrawlerIds = null)
        {
            _run = run; _plan = plan; _initialCrawlerIds = initialCrawlerIds ?? Array.Empty<string>();
            _state = new Envelope { version = Version, approvedSourceLayerCount = plan?.ApprovedSourceLayerCount ?? 0,
                sourceDocumentJson = plan?.Document.CanonicalJson ?? string.Empty,
                phases = new List<PhaseData>(), floors = new List<FloorData>(), fingerprint = string.Empty };
            if (plan != null)
                foreach (var phase in plan.Phases)
                {
                    var row = new PhaseData { phaseId = phase.PhaseContentId, floorId = phase.FloorContentId,
                        sourceLayerId = phase.SourceLayerContentId, trigger = (int)phase.Trigger,
                        triggerNodeId = phase.TriggerNodeContentId, slots = new List<SlotData>(),
                        activated = false, generationJson = string.Empty, placements = new List<PlacementData>() };
                    foreach (var slot in phase.Slots) row.slots.Add(new SlotData
                    { sourceSlotId = slot.SourceSlotContentId, nodeId = slot.NodeContentId, effectId = slot.EffectId });
                    _state.phases.Add(row);
                }
            Seal();
        }

        private DungeonRunSideEventState(DungeonRunSession run, DungeonRunSideEventPlan plan, Envelope state)
        { _run = run; _plan = plan; _state = Clone(state); _initialCrawlerIds = Array.Empty<string>(); }
        internal DungeonRunSideEventState Rebind(DungeonRunSession run) => new DungeonRunSideEventState(run, _plan, _state);
        internal DungeonRunSideEventState Clone() => new DungeonRunSideEventState(_run, _plan, _state);
        internal void Adopt(DungeonRunSideEventState prepared)
        {
            if (prepared == null || prepared._plan != _plan)
                throw Bad("Prepared source journal belongs to another immutable plan.");
            _state = Clone(prepared._state);
        }
        internal string CaptureCanonicalJson() => SerializeEnvelope(_state);
        internal bool TracksFloor(string floorId) => _state.floors.Exists(f => f.floorId == floorId);

        /// <summary>Victory must not freeze away this floor's uncaptured source
        /// phases. This is a capture boundary, not a requirement to visit every
        /// generated event. Defeat and author-only/manual frozen Runs retain
        /// their existing lifecycle.</summary>
        public bool TryValidateVictoryReadiness(out string error)
        {
            error = null;
            try
            {
                if (_run == null) throw Bad("Source journal is not attached to a Run.");
                RequireVictoryReady(_run.CurrentFloor);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        internal void RequireVictoryReady(LevelRunSession floor)
        {
            if (_plan == null || !TracksFloor(floor.Profile.contentId)) return;
            RequireFloor(floor, requireCompletedPhases: true);
        }

        internal void ValidateFloorPreparation(LevelRunProfile profile)
        { _plan?.RequireFloor(profile); }

        internal LevelRunNodeDirectory PrepareFloor(LevelRunProfile profile, int seed,
            IDungeonRunSideEventContext context, string runId, IReadOnlyList<DungeonRunArcanaDefinition> active)
        {
            _plan?.RequireFloor(profile);
            if (_plan == null || !_plan.HasFloor(profile.contentId)) return null;
            if (TracksFloor(profile.contentId)) throw Bad("An entered source floor cannot be generated again.");
            var directory = LevelRunNodeDirectory.FreezeInteractions(profile, Array.Empty<LevelNode>(), Disabled(profile.contentId));
            _state.floors.Add(new FloorData { floorId = profile.contentId, directoryFingerprint = directory.Fingerprint });
            foreach (var phase in _plan.Phases)
                if (phase.FloorContentId == profile.contentId && phase.Trigger == DungeonRunSideEventPhaseTrigger.FloorEntry)
                    directory = PreparePhase(phase, profile, directory, seed, context, runId, active);
            Seal();
            return directory;
        }

        /// <summary>Explicit phase publication is durable or changes nothing. Replay never captures or saves again.</summary>
        public bool TryActivatePhase(string phaseId, IDungeonRunSideEventContext context,
            out string error, Action persist = null)
        {
            error = null;
            if (_run == null) { error = "Source journal is not attached to a Run."; return false; }
            if (_run.IsRewardCommitInProgress || _run.Crystals.HasPendingEvent || _run.Interactions.HasPendingChoice ||
                _run.Arcanas.HasPendingChoice || (_run.CardOfferings.HasPendingChoice || _run.IsSpecializedInteractionBusy) || _run.TryGetConclusion(out _))
            { error = "Run reward or source publication is outstanding."; return false; }
            try
            {
                var phase = FindPhase(phaseId);
                _plan.RequirePhaseApproved(phase);
                var row = _state.phases.Find(p => p.phaseId == phaseId);
                if (row.activated) return true;
                if (persist == null) throw Bad("Source phase publication requires strict persistence.");
                if (phase.FloorContentId != _run.CurrentFloorContentId || !TracksFloor(phase.FloorContentId))
                    throw Bad("Source phase is not part of the current entered floor.");
                _run.CurrentFloor.RequireFloorExitReady();
                if (phase.Trigger != DungeonRunSideEventPhaseTrigger.AfterEncounter ||
                    _run.CurrentFloor.GetNodeState(phase.TriggerNodeContentId) != LevelNodeViewState.Completed)
                    throw Bad("The explicit encounter phase trigger has not completed.");
                _committing = true;
                var prepared = Clone();
                var directory = prepared.PreparePhase(phase, _run.CurrentFloor.Profile,
                    _run.CurrentFloor.NodeDirectory, _run.RunSeed, context, _run.RunId, _run.Arcanas.Active);
                var floor = _run.CurrentFloor.PrepareDirectoryTransition(directory);
                _run.CommitSideEventPhase(floor, prepared, persist);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally { _committing = false; }
        }

        private LevelRunNodeDirectory PreparePhase(DungeonRunSideEventPhaseBinding phase, LevelRunProfile profile,
            LevelRunNodeDirectory previous, int seed, IDungeonRunSideEventContext context,
            string runId, IReadOnlyList<DungeonRunArcanaDefinition> active)
        {
            if (context == null) throw Bad("A real source qualification and placement context is required.");
            _plan.RequirePhaseApproved(phase);
            var row = _state.phases.Find(p => p.phaseId == phase.PhaseContentId);
            if (row == null || row.activated) throw Bad("This source phase was already captured.");
            foreach (var earlier in _state.phases)
            {
                if (earlier == row) break;
                if (!earlier.activated) throw Bad("Source phases must be captured in their explicit original order.");
            }
            var snapshot = context is IDungeonRunSideEventRosterContext rosterContext
                ? rosterContext.CaptureQualificationForRoster(_plan.Document, phase.SourceLayerContentId,
                    runId, active, _run?.Invitations.OwnedCrawlerContentIds ?? _initialCrawlerIds)
                : context.CaptureQualification(_plan.Document, phase.SourceLayerContentId, runId, active);
            if (snapshot == null || snapshot.LayerContentId != phase.SourceLayerContentId ||
                snapshot.SourceFingerprint != _plan.Document.Fingerprint)
                throw Bad("Qualification input belongs to another source document or layer.");
            var checkpoint = SideEventQualificationCheckpoint.Create(_plan.Document, snapshot);
            var generation = SideEventSourceGeneration.Begin(checkpoint);
            var replacements = new List<LevelNode>();
            var placements = new List<PlacementData>();
            var random = new PhaseRandom(seed, phase.PhaseContentId);
            while (generation.Status == SideEventSourceGenerationStatus.Ready)
            {
                int stepIndex = generation.Steps.Count;
                generation = generation.Advance(random, attempt =>
                {
                    if (attempt.Selection.Slot == null || attempt.Selection.Member == null)
                        throw Bad("A weighted result without a member has no adopted placement contract.");
                    var slot = FindSlot(phase, attempt.Selection.Slot.Source.ContentId);
                    if (!DungeonSideEventSourceAdapter.TryCreateNode(attempt, generation.Qualification.Document, profile, slot.NodeContentId, slot.EffectId,
                            out var node, out var adapterId, out var adapterError)) throw Bad(adapterError);
                    bool placed = context.CanPlace(new DungeonRunSideEventPlacementRequest(runId, phase, slot, attempt, node));
                    // Never publish the callback's detached copy or accept an adapter drift.
                    if (!DungeonSideEventSourceAdapter.TryValidateNode(attempt, generation.Qualification.Document, node, adapterId, out adapterError)) throw Bad(adapterError);
                    placements.Add(new PlacementData { stepIndex = stepIndex, sourceSlotId = slot.SourceSlotContentId,
                        rootId = attempt.Selection.Member.Root.ContentId, nodeId = slot.NodeContentId,
                        effectId = slot.EffectId, adapterId = adapterId, placed = placed });
                    if (placed) replacements.Add(node);
                    return placed;
                });
            }
            row.activated = true; row.generationJson = generation.CaptureCanonicalJson(); row.placements = placements;
            var directory = LevelRunNodeDirectory.RefreezeInteractions(profile, previous, replacements, Disabled(profile.contentId));
            _state.floors.Find(f => f.floorId == profile.contentId).directoryFingerprint = directory.Fingerprint;
            Seal(); return directory;
        }

        internal void RequireFloor(LevelRunSession floor, bool requireCompletedPhases = false)
        {
            if (requireCompletedPhases && _committing)
                throw Bad("Source publication cannot reenter floor advancement.");
            if (!TracksFloor(floor.Profile.contentId))
            {
                if (floor.NodeDirectory.HasFrozenInteractions)
                    throw Bad("A frozen floor has no complete Run source placement journal; advancing remains forbidden.");
                return;
            }
            ValidateFloor(floor);
            if (requireCompletedPhases && _state.phases.Exists(p => p.floorId == floor.Profile.contentId && !p.activated))
                throw Bad("The current floor still has an uncaptured explicit source phase.");
        }

        private List<string> Disabled(string floorId)
        {
            var placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var phase in _state.phases)
                if (phase.floorId == floorId)
                    foreach (var item in phase.placements) if (item.placed) placed.Add(item.nodeId);
            var disabled = new List<string>();
            foreach (var phase in _state.phases)
                if (phase.floorId == floorId)
                    foreach (var slot in phase.slots) if (!placed.Contains(slot.nodeId)) disabled.Add(slot.nodeId);
            return disabled;
        }

        private void ValidateFloor(LevelRunSession floor)
        {
            string floorId = floor.Profile.contentId;
            var proof = _state.floors.Find(f => f.floorId == floorId);
            if (proof == null || !floor.NodeDirectory.HasFrozenInteractions || proof.directoryFingerprint != floor.NodeDirectory.Fingerprint)
                throw Bad("Source journal and complete effective directory fingerprint disagree.");
            _plan.RequireFloor(floor.Profile);
            if (_state.phases.Exists(p => p.floorId == floorId &&
                    p.trigger == (int)DungeonRunSideEventPhaseTrigger.FloorEntry && !p.activated))
                throw Bad("An entered source floor must have captured its FloorEntry phase.");
            var expectedDisabled = new HashSet<string>(Disabled(floorId), StringComparer.Ordinal);
            if (!expectedDisabled.SetEquals(floor.NodeDirectory.DisabledInteractionNodeIds))
                throw Bad("Source placement and directory activation disagree.");
            foreach (var phase in _state.phases)
            {
                if (phase.floorId != floorId || !phase.activated) continue;
                if (phase.trigger == (int)DungeonRunSideEventPhaseTrigger.AfterEncounter)
                {
                    var trigger = floor.FindNode(phase.triggerNodeId);
                    if (trigger == null || (trigger.nodeType != LevelNodeType.Encounter && trigger.nodeType != LevelNodeType.Finale) ||
                        floor.GetNodeState(phase.triggerNodeId) != LevelNodeViewState.Completed)
                        throw Bad("Captured encounter phase has no completed trigger in its saved floor.");
                }
                var generation = SideEventSourceGeneration.RestoreCanonicalJson(phase.generationJson);
                if (generation.Qualification.Document.CanonicalJson != _plan.Document.CanonicalJson)
                    throw Bad("Placed source qualification differs from the complete frozen Run document.");
                foreach (var placement in phase.placements)
                {
                    if (!placement.placed) continue;
                    var node = floor.FindNode(placement.nodeId);
                    if (node == null || node.completionEffects == null || node.completionEffects.Count != 1 ||
                        node.completionEffects[0].effectId != placement.effectId ||
                        !DungeonSideEventSourceAdapter.TryValidateNode(generation.Steps[placement.stepIndex].Attempt, generation.Qualification.Document,
                            node, placement.adapterId, out _)) throw Bad("Saved logical placement differs from its adopted complete effect.");
                }
            }
        }

        internal static DungeonRunSideEventState Restore(DungeonRunSession run, DungeonRunSaveData data,
            string payload, IReadOnlyList<LevelRunSession> visited)
        {
            if (data.schemaVersion < 30)
            {
                if (data.hasSideEventSourceState || !string.IsNullOrEmpty(data.sideEventSourceStateJson))
                    throw Bad("Legacy Run cannot contain future source placement proof.");
                return new DungeonRunSideEventState(run);
            }
            if (!JsonObjectShape.TryGetField(payload, "hasSideEventSourceState", out var present) || present != "true" ||
                !data.hasSideEventSourceState || !JsonObjectShape.TryGetField(payload, "sideEventSourceStateJson", out var raw) ||
                raw.Length < 2 || raw[0] != '"' || string.IsNullOrEmpty(data.sideEventSourceStateJson))
                throw Bad("Run requires its explicit source-state checkpoint wire.");
            var saved = ReadCanonical(data.sideEventSourceStateJson);
            DungeonRunSideEventPlan plan = null;
            if (saved.sourceDocumentJson != string.Empty)
            {
                var document = SideEventSourceWireDocument.Parse(saved.sourceDocumentJson);
                var phases = new List<DungeonRunSideEventPhaseBinding>();
                foreach (var row in saved.phases)
                {
                    var slots = new List<DungeonRunSideEventSlotBinding>();
                    foreach (var slot in row.slots) slots.Add(new DungeonRunSideEventSlotBinding(slot.sourceSlotId, slot.nodeId, slot.effectId));
                    phases.Add(new DungeonRunSideEventPhaseBinding(row.phaseId, row.floorId, row.sourceLayerId,
                        (DungeonRunSideEventPhaseTrigger)row.trigger, row.triggerNodeId, slots));
                }
                plan = new DungeonRunSideEventPlan(document, phases, saved.approvedSourceLayerCount);
            }
            else if (saved.phases.Count != 0 || saved.floors.Count != 0 || saved.approvedSourceLayerCount != 0)
                throw Bad("An author-only Run cannot invent source history or approvals.");
            var result = new DungeonRunSideEventState(run, plan, saved);
            result.ValidateJournal();
            var floorIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < visited.Count; i++)
            {
                var floor = visited[i];
                plan?.RequireFloor(floor.Profile);
                floorIds.Add(floor.Profile.contentId);
                if (result.TracksFloor(floor.Profile.contentId))
                {
                    result.ValidateFloor(floor);
                    if (i < visited.Count - 1 && saved.phases.Exists(p => p.floorId == floor.Profile.contentId && !p.activated))
                        throw Bad("Historical source floor contains an uncaptured phase.");
                }
                else if (plan?.HasFloor(floor.Profile.contentId) == true) throw Bad("Entered planned source floor is missing its journal.");
            }
            foreach (var floor in saved.floors)
                if (!floorIds.Contains(floor.floorId)) throw Bad("Source journal contains an unentered floor.");
            // Old manually frozen directories remain readable but untracked: no invented origin.
            return result;
        }

        private void ValidateJournal()
        {
            if (_state.approvedSourceLayerCount != (_plan?.ApprovedSourceLayerCount ?? 0))
                throw Bad("Source journal and immutable approval scope differ.");
            bool pendingSeen = false;
            foreach (var phase in _state.phases)
            {
                if (!phase.activated)
                {
                    pendingSeen = true;
                    if (phase.generationJson != string.Empty || phase.placements.Count != 0) throw Bad("Pending phase invents captured inputs.");
                    continue;
                }
                _plan.RequirePhaseApproved(FindPhase(phase.phaseId));
                if (pendingSeen) throw Bad("Source phase order changed.");
                var generation = SideEventSourceGeneration.RestoreCanonicalJson(phase.generationJson);
                if (generation.Qualification.Document.CanonicalJson != _plan.Document.CanonicalJson ||
                    generation.Qualification.Snapshot.LayerContentId != phase.sourceLayerId ||
                    generation.Status == SideEventSourceGenerationStatus.Ready) throw Bad("Saved source phase is incomplete or cross-bound.");
                int index = 0;
                for (int i = 0; i < generation.Steps.Count; i++)
                {
                    var step = generation.Steps[i];
                    if (step.Outcome == SideEventSourceAttemptOutcome.EmptyPool) continue;
                    if (index >= phase.placements.Count || step.Attempt.Selection.Member == null) throw Bad("Saved placement is missing its selected member.");
                    var placement = phase.placements[index++];
                    var slot = FindSlot(FindPhase(phase.phaseId), step.Attempt.Selection.Slot.Source.ContentId);
                    if (!DungeonSideEventSourceAdapter.TryResolveAdapterIdentity(step.Attempt,
                            out var expectedAdapterId, out var adapterError)) throw Bad(adapterError);
                    if (placement.stepIndex != i || placement.sourceSlotId != slot.SourceSlotContentId ||
                        placement.rootId != step.Attempt.Selection.Member.Root.ContentId || placement.nodeId != slot.NodeContentId ||
                        placement.effectId != slot.EffectId || placement.adapterId != expectedAdapterId ||
                        placement.placed != (step.Outcome == SideEventSourceAttemptOutcome.Placed))
                        throw Bad("Actual placement and mathematical attempt proof disagree.");
                }
                if (index != phase.placements.Count) throw Bad("Source journal contains an orphan placement.");
            }
            var floors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var floor in _state.floors)
                if (!floors.Add(floor.floorId) || _plan == null || !_plan.HasFloor(floor.floorId) ||
                    string.IsNullOrWhiteSpace(floor.directoryFingerprint)) throw Bad("Source floor proofs are invalid or duplicate.");
            foreach (var phase in _state.phases)
                if (phase.activated && !floors.Contains(phase.floorId)) throw Bad("Captured phase belongs to an unentered floor.");
        }

        private DungeonRunSideEventPhaseBinding FindPhase(string id)
        {
            if (_plan != null) foreach (var phase in _plan.Phases) if (phase.PhaseContentId == id) return phase;
            throw Bad("Source phase identity is not declared.");
        }
        private static DungeonRunSideEventSlotBinding FindSlot(DungeonRunSideEventPhaseBinding phase, string id)
        {
            foreach (var slot in phase.Slots) if (slot.SourceSlotContentId == id) return slot;
            throw Bad("Selected source occurrence has no explicit logical placement.");
        }
        private void Seal() { _state.fingerprint = string.Empty; _state.fingerprint = Hash(SerializeEnvelope(_state)); }
        private static Envelope Clone(Envelope data) => JsonUtility.FromJson<Envelope>(JsonUtility.ToJson(data));
        private static string Hash(string text)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        private static InvalidOperationException Bad(string error) => new InvalidOperationException("Side-event source state: " + error);

        private static Envelope ReadCanonical(string json)
        {
            Envelope result;
            if (JsonObjectShape.TryGetField(json, "version", out var version) && version == "1")
            {
                var legacy = JsonUtility.FromJson<LegacyEnvelope>(json);
                if (legacy == null || JsonUtility.ToJson(legacy) != json)
                    throw Bad("Legacy source journal is not canonical complete wire.");
                result = new Envelope { version = 1, sourceDocumentJson = legacy.sourceDocumentJson,
                    phases = legacy.phases, floors = legacy.floors, fingerprint = legacy.fingerprint,
                    approvedSourceLayerCount = legacy.sourceDocumentJson == string.Empty ? 0 : legacy.phases?.Count ?? 0 };
            }
            else result = JsonUtility.FromJson<Envelope>(json);
            if (result == null || (result.version != Version && result.version != 1) ||
                result.approvedSourceLayerCount < 0 || result.phases == null || result.floors == null ||
                result.sourceDocumentJson == null || result.fingerprint == null) throw Bad("Invalid source journal envelope.");
            foreach (var phase in result.phases)
            {
                if (phase == null || phase.slots == null || phase.placements == null || phase.generationJson == null ||
                    phase.triggerNodeId == null) throw Bad("Source phase wire must be complete.");
                foreach (var slot in phase.slots) if (slot == null) throw Bad("Null source binding.");
                foreach (var placement in phase.placements) if (placement == null) throw Bad("Null placement proof.");
            }
            foreach (var floor in result.floors) if (floor == null) throw Bad("Null source floor proof.");
            // This checkpoint protocol is canonical and DTO-closed. A decode/encode
            // identity rejects missing/defaulted/duplicate/escaped/extra/wrong-type fields.
            if (SerializeEnvelope(result) != json) throw Bad("Source journal is not canonical complete wire.");
            string fingerprint = result.fingerprint; result.fingerprint = string.Empty;
            if (Hash(SerializeEnvelope(result)) != fingerprint) throw Bad("Source journal fingerprint changed.");
            result.fingerprint = fingerprint; return result;
        }

        private static string SerializeEnvelope(Envelope state)
            => state.version == 1 ? JsonUtility.ToJson(new LegacyEnvelope {
                version = 1, sourceDocumentJson = state.sourceDocumentJson, phases = state.phases,
                floors = state.floors, fingerprint = state.fingerprint }) : JsonUtility.ToJson(state);

        /// <summary>Private deterministic phase stream is rollback-local project policy,
        /// not the source global RNG or an additional live mutable Run owner.</summary>
        private sealed class PhaseRandom : IRandomSource
        {
            private readonly System.Random _random;
            internal PhaseRandom(int seed, string phaseId)
            { var bytes = new UTF8Encoding(false, true).GetBytes(seed.ToString(CultureInfo.InvariantCulture) + "/side-events/v1/" + phaseId);
                using var sha = SHA256.Create(); _random = new System.Random(BitConverter.ToInt32(sha.ComputeHash(bytes), 0)); }
            public float Value01() => (float)_random.NextDouble();
            public void Shuffle<T>(IList<T> values)
            { for (int i = values.Count - 1; i > 0; i--) { int j = _random.Next(i + 1); T value = values[i]; values[i] = values[j]; values[j] = value; } }
            public int Range(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
            public float Range(float minInclusive, float maxInclusive) => (float)(minInclusive + (maxInclusive - minInclusive) * _random.NextDouble());
        }
    }
}
