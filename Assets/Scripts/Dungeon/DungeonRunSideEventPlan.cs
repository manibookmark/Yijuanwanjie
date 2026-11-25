using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WanjieScroll.Core;
using UnityEngine;

namespace WanjieScroll.Dungeon
{
    public enum DungeonRunSideEventPhaseTrigger { FloorEntry, AfterEncounter }

    /// <summary>An explicit logical placement, never an inferred native tile or a new graph edge.</summary>
    public sealed class DungeonRunSideEventSlotBinding
    {
        public string SourceSlotContentId { get; }
        public string NodeContentId { get; }
        public string EffectId { get; }
        public DungeonRunSideEventSlotBinding(string sourceSlotContentId, string nodeContentId, string effectId)
        {
            SourceSlotContentId = RequireId(sourceSlotContentId);
            NodeContentId = RequireId(nodeContentId); EffectId = RequireId(effectId);
        }
        internal static string RequireId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new ArgumentException("Side-event bindings require exact stable identities.");
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsWhiteSpace(current) || char.IsControl(current) ||
                    char.IsLowSurrogate(current) || (char.IsHighSurrogate(current) &&
                        (i + 1 == value.Length || !char.IsLowSurrogate(value[++i]))))
                    throw new ArgumentException("Side-event identities must have valid Unicode and no whitespace or control characters.");
            }
            return value;
        }
    }

    public sealed class DungeonRunSideEventPhaseBinding
    {
        public string PhaseContentId { get; }
        public string FloorContentId { get; }
        public string SourceLayerContentId { get; }
        public DungeonRunSideEventPhaseTrigger Trigger { get; }
        public string TriggerNodeContentId { get; }
        public IReadOnlyList<DungeonRunSideEventSlotBinding> Slots { get; }
        public DungeonRunSideEventPhaseBinding(string phaseContentId, string floorContentId,
            string sourceLayerContentId, DungeonRunSideEventPhaseTrigger trigger,
            string triggerNodeContentId, IReadOnlyList<DungeonRunSideEventSlotBinding> slots)
        {
            PhaseContentId = DungeonRunSideEventSlotBinding.RequireId(phaseContentId);
            FloorContentId = DungeonRunSideEventSlotBinding.RequireId(floorContentId);
            SourceLayerContentId = DungeonRunSideEventSlotBinding.RequireId(sourceLayerContentId);
            if (!Enum.IsDefined(typeof(DungeonRunSideEventPhaseTrigger), trigger))
                throw new ArgumentException("Unknown side-event phase trigger.");
            Trigger = trigger;
            TriggerNodeContentId = trigger == DungeonRunSideEventPhaseTrigger.FloorEntry
                ? string.Empty : DungeonRunSideEventSlotBinding.RequireId(triggerNodeContentId);
            if (trigger == DungeonRunSideEventPhaseTrigger.FloorEntry && !string.IsNullOrEmpty(triggerNodeContentId))
                throw new ArgumentException("FloorEntry must not invent an encounter trigger.");
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            var copy = new List<DungeonRunSideEventSlotBinding>(slots.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in slots)
            {
                if (slot == null || !seen.Add(slot.SourceSlotContentId))
                    throw new ArgumentException("A phase requires unique explicit source-slot occurrences.");
                copy.Add(slot);
            }
            Slots = new ReadOnlyCollection<DungeonRunSideEventSlotBinding>(copy);
        }
    }

    /// <summary>
    /// Immutable source-to-floor bindings. The source document retains all twelve
    /// mechanisms; bindings identify logical slots and explicit qualification times.
    /// They grant neither account eligibility nor execution permission. The first
    /// phase of each floor is FloorEntry; subsequent phases are separately triggered.
    /// In particular merged floors do not capture two native layers at one time.
    /// </summary>
    public sealed class DungeonRunSideEventPlan
    {
        public SideEventSourceWireDocument Document { get; }
        public IReadOnlyList<DungeonRunSideEventPhaseBinding> Phases { get; }
        public int ApprovedSourceLayerCount { get; }
        public DungeonRunSideEventPlan(SideEventSourceWireDocument document,
            IReadOnlyList<DungeonRunSideEventPhaseBinding> phases, int approvedSourceLayerCount = -1)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            if (phases == null || phases.Count == 0) throw new ArgumentException("An explicit source plan needs phases.");
            ApprovedSourceLayerCount = approvedSourceLayerCount == -1 ? phases.Count : approvedSourceLayerCount;
            if (ApprovedSourceLayerCount < 0 || ApprovedSourceLayerCount > phases.Count)
                throw new ArgumentException("Source approval must be a declared contiguous prefix.");
            bool partialScope = ApprovedSourceLayerCount < phases.Count;
            if (partialScope && phases.Count != document.Definition.Layers.Count)
                throw new ArgumentException("A prefix scope must retain every native source layer.");
            var copy = new List<DungeonRunSideEventPhaseBinding>(phases.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var sourceLayers = new HashSet<string>(StringComparer.Ordinal);
            var closedFloors = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            var effects = new HashSet<string>(StringComparer.Ordinal);
            string previousFloor = null;
            int previousSourceIndex = -1;
            foreach (var phase in phases)
            {
                if (phase == null || !ids.Add(phase.PhaseContentId) || !sourceLayers.Add(phase.SourceLayerContentId) ||
                    !document.Definition.TryGetLayer(phase.SourceLayerContentId, out var layer))
                    throw new ArgumentException("Source phases must uniquely bind registered native layers.");
                int sourceIndex = -1;
                for (int i = 0; i < document.Definition.Layers.Count; i++)
                    if (document.Definition.Layers[i].ContentId == phase.SourceLayerContentId) sourceIndex = i;
                if (sourceIndex <= previousSourceIndex)
                    throw new ArgumentException("Source phases must retain the declared native layer order.");
                if (partialScope && sourceIndex != copy.Count)
                    throw new ArgumentException("A prefix scope cannot omit an original source layer.");
                previousSourceIndex = sourceIndex;
                bool first = previousFloor != phase.FloorContentId;
                if (first)
                {
                    if (previousFloor != null) closedFloors.Add(previousFloor);
                    if (closedFloors.Contains(phase.FloorContentId) || phase.Trigger != DungeonRunSideEventPhaseTrigger.FloorEntry)
                        throw new ArgumentException("Floor phases must be contiguous and begin at FloorEntry.");
                }
                else if (phase.Trigger == DungeonRunSideEventPhaseTrigger.FloorEntry)
                    throw new ArgumentException("A later source phase must capture at its own explicit trigger.");
                if (phase.Slots.Count != layer.Slots.Count)
                    throw new ArgumentException("A phase must bind every original source slot, including empty slots.");
                for (int i = 0; i < phase.Slots.Count; i++)
                {
                    var slot = phase.Slots[i];
                    if (slot.SourceSlotContentId != layer.Slots[i].ContentId ||
                        !nodes.Add(phase.FloorContentId + "\n" + slot.NodeContentId) ||
                        !effects.Add(phase.FloorContentId + "\n" + slot.EffectId))
                        throw new ArgumentException("Source order and independent floor node/effect identities are required.");
                }
                copy.Add(phase); previousFloor = phase.FloorContentId;
            }
            if (ApprovedSourceLayerCount > 0 && ApprovedSourceLayerCount < copy.Count &&
                copy[ApprovedSourceLayerCount - 1].FloorContentId == copy[ApprovedSourceLayerCount].FloorContentId)
                throw new ArgumentException("An approved prefix cannot end inside a project floor.");
            Phases = new ReadOnlyCollection<DungeonRunSideEventPhaseBinding>(copy);
        }

        public bool IsSourceLayerApproved(string sourceLayerId)
        {
            for (int i = 0; i < ApprovedSourceLayerCount; i++)
                if (Phases[i].SourceLayerContentId == sourceLayerId) return true;
            return false;
        }

        internal void RequirePhaseApproved(DungeonRunSideEventPhaseBinding phase)
        {
            if (phase == null || !IsSourceLayerApproved(phase.SourceLayerContentId))
                throw new InvalidOperationException("当前来源层尚未批准接入：" + phase?.FloorContentId);
        }

        internal void RequireFloor(LevelRunProfile profile)
        {
            bool approvedFloor = false;
            for (int i = 0; i < ApprovedSourceLayerCount; i++)
                approvedFloor |= Phases[i].FloorContentId == profile.contentId;
            if (ApprovedSourceLayerCount < Phases.Count && !approvedFloor)
                throw new InvalidOperationException("下一层内容尚未批准接入：" + profile.contentId + "。当前楼层检查点保留。");
            foreach (var phase in Phases)
            {
                if (phase.FloorContentId != profile.contentId) continue;
                if (phase.Trigger == DungeonRunSideEventPhaseTrigger.AfterEncounter)
                {
                    var trigger = profile.FindNode(phase.TriggerNodeContentId);
                    if (trigger == null || (trigger.nodeType != LevelNodeType.Encounter && trigger.nodeType != LevelNodeType.Finale))
                        throw new InvalidOperationException("A source phase requires its explicit existing encounter trigger.");
                }
                foreach (var slot in phase.Slots)
                    if (profile.FindNode(slot.NodeContentId)?.nodeType != LevelNodeType.Interaction)
                        throw new InvalidOperationException("Logical source placement requires an existing Interaction; topology is never invented.");
            }
        }
        internal bool HasFloor(string floorId)
        {
            foreach (var phase in Phases) if (phase.FloorContentId == floorId) return true;
            return false;
        }
    }

    /// <summary>
    /// The composition boundary supplies real qualification and pure logical
    /// placement. Unknown services throw; false is a known placement failure.
    /// No implementation may mutate Run/account state or the unpublished node.
    /// </summary>
    public interface IDungeonRunSideEventContext
    {
        SideEventQualificationSnapshot CaptureQualification(SideEventSourceWireDocument document,
            string sourceLayerContentId, string runId, IReadOnlyList<DungeonRunArcanaDefinition> activeArcanas);
        bool CanPlace(DungeonRunSideEventPlacementRequest request);
    }

    /// <summary>Optional production qualification for roster-dependent events.
    /// The Run supplies its real held roster, including the explicit initial
    /// leader before its first floor exists. Recovery never queries this input.</summary>
    public interface IDungeonRunSideEventRosterContext
    {
        SideEventQualificationSnapshot CaptureQualificationForRoster(SideEventSourceWireDocument document,
            string layerId, string runId, IReadOnlyList<DungeonRunArcanaDefinition> activeArcanas,
            IReadOnlyList<string> ownedCrawlerIds);
    }

    public sealed class DungeonRunSideEventPlacementRequest
    {
        public string RunId { get; }
        public DungeonRunSideEventPhaseBinding Phase { get; }
        public DungeonRunSideEventSlotBinding Slot { get; }
        public SideEventSourceAttempt Attempt { get; }
        private readonly string _nodeJson;
        // Each query is detached; callbacks cannot rewrite the node later published.
        public LevelNode Node => JsonUtility.FromJson<LevelNode>(_nodeJson);
        internal DungeonRunSideEventPlacementRequest(string runId, DungeonRunSideEventPhaseBinding phase,
            DungeonRunSideEventSlotBinding slot, SideEventSourceAttempt attempt, LevelNode node)
        { RunId = runId; Phase = phase; Slot = slot; Attempt = attempt; _nodeJson = JsonUtility.ToJson(node); }
    }
}
