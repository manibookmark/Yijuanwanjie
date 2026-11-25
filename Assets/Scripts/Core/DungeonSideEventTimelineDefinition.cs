using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WanjieScroll.Core
{
    public enum DungeonSideEventTimelineStatus { Candidate, Enabled, PartialEnabled }
    public enum DungeonSideEventTimelineTrigger { FloorEntry, AfterEncounter }

    [Serializable]
    public sealed class DungeonSideEventTimelineContentData
    {
        public int schemaVersion;
        public string contentId;
        public string sourceDocumentContentId;
        public string status;
        public int approvedSourceLayerCount;
        public List<DungeonSideEventTimelinePhaseData> phases;
    }

    [Serializable]
    public sealed class DungeonSideEventTimelinePhaseData
    {
        public string contentId;
        public string floorContentId;
        public string sourceLayerContentId;
        public string trigger;
        public string triggerNodeContentId;
        public List<DungeonSideEventTimelineSlotData> slots;
    }

    [Serializable]
    public sealed class DungeonSideEventTimelineSlotData
    {
        public string sourceSlotContentId;
        public string nodeContentId;
        public string effectContentId;
        public bool placementApproved;
    }

    public sealed class DungeonSideEventTimelineSlot
    {
        public string SourceSlotContentId { get; }
        public string NodeContentId { get; }
        public string EffectContentId { get; }
        public bool PlacementApproved { get; }
        public DungeonSideEventTimelineSlot(string sourceSlotContentId, string nodeContentId,
            string effectContentId, bool placementApproved)
        {
            SourceSlotContentId = SideEventSourceGuard.ContentId(sourceSlotContentId, "sideslot");
            NodeContentId = SideEventSourceGuard.ContentId(nodeContentId, "node");
            EffectContentId = SideEventSourceGuard.ContentId(effectContentId, "effect");
            PlacementApproved = placementApproved;
        }
    }

    public sealed class DungeonSideEventTimelinePhase
    {
        public string ContentId { get; }
        public string FloorContentId { get; }
        public string SourceLayerContentId { get; }
        public DungeonSideEventTimelineTrigger Trigger { get; }
        public string TriggerNodeContentId { get; }
        public IReadOnlyList<DungeonSideEventTimelineSlot> Slots { get; }
        public DungeonSideEventTimelinePhase(string contentId, string floorContentId,
            string sourceLayerContentId, DungeonSideEventTimelineTrigger trigger,
            string triggerNodeContentId, IReadOnlyList<DungeonSideEventTimelineSlot> slots)
        {
            ContentId = SideEventSourceGuard.ContentId(contentId, "sidephase");
            FloorContentId = SideEventSourceGuard.ContentId(floorContentId, "level");
            SourceLayerContentId = SideEventSourceGuard.ContentId(sourceLayerContentId, "sidelayer");
            if (!Enum.IsDefined(typeof(DungeonSideEventTimelineTrigger), trigger))
                throw new ArgumentException("A declared timeline trigger is required.", nameof(trigger));
            Trigger = trigger;
            if (trigger == DungeonSideEventTimelineTrigger.FloorEntry)
            {
                if (!string.IsNullOrEmpty(triggerNodeContentId))
                    throw new ArgumentException("Floor entry cannot invent an encounter trigger.");
                TriggerNodeContentId = null;
            }
            else TriggerNodeContentId = SideEventSourceGuard.ContentId(triggerNodeContentId, "node");
            if (slots == null || slots.Count == 0)
                throw new ArgumentException("A phase must bind its complete source slots.", nameof(slots));
            var copy = new List<DungeonSideEventTimelineSlot>(slots.Count);
            foreach (var slot in slots)
                copy.Add(slot ?? throw new ArgumentException("Timeline slots cannot contain null."));
            Slots = new ReadOnlyCollection<DungeonSideEventTimelineSlot>(copy);
        }
    }

    /// <summary>
    /// Owns the immutable author timeline and approval boundary, never node progress,
    /// qualification or placement. Candidate carries complete reviewable identities;
    /// it grants no execution and cannot claim a capacity approval. PartialEnabled
    /// approves a complete-floor prefix while retaining every later declaration.
    /// Approval concerns logical slot capacity, never UI/functional acceptance. Content compilation
    /// separately binds the exact source document layer/slot sequence. Floor topology,
    /// trigger node existence and actual mechanism readiness belong to Run admission.
    /// All collections are copied, and no mutable author DTO escapes this definition.
    /// </summary>
    public sealed class DungeonSideEventTimelineDefinition
    {
        public const int CurrentSchemaVersion = 2;
        public const int LegacySchemaVersion = 1;
        public string ContentId { get; }
        public string SourceDocumentContentId { get; }
        public DungeonSideEventTimelineStatus Status { get; }
        public int ApprovedSourceLayerCount { get; }
        public IReadOnlyList<DungeonSideEventTimelinePhase> Phases { get; }

        public static DungeonSideEventTimelineDefinition Create(DungeonSideEventTimelineContentData data)
        {
            if (data == null || (data.schemaVersion != CurrentSchemaVersion &&
                    data.schemaVersion != LegacySchemaVersion) || data.phases == null)
                throw new ArgumentException("A complete supported timeline schema is required.");
            var status = Parse<DungeonSideEventTimelineStatus>(data.status);
            if (data.schemaVersion == LegacySchemaVersion &&
                (status == DungeonSideEventTimelineStatus.PartialEnabled || data.approvedSourceLayerCount != 0))
                throw new ArgumentException("Legacy timelines cannot claim a partial approval scope.");
            var phases = new List<DungeonSideEventTimelinePhase>();
            foreach (var phase in data.phases)
            {
                if (phase?.slots == null) throw new ArgumentException("Complete phase and slot arrays are required.");
                var slots = new List<DungeonSideEventTimelineSlot>();
                foreach (var slot in phase.slots)
                {
                    if (slot == null) throw new ArgumentException("A complete slot object is required.");
                    slots.Add(new DungeonSideEventTimelineSlot(slot.sourceSlotContentId,
                        slot.nodeContentId, slot.effectContentId, slot.placementApproved));
                }
                phases.Add(new DungeonSideEventTimelinePhase(phase.contentId, phase.floorContentId,
                    phase.sourceLayerContentId, Parse<DungeonSideEventTimelineTrigger>(phase.trigger),
                    phase.triggerNodeContentId, slots));
            }
            return new DungeonSideEventTimelineDefinition(data.contentId, data.sourceDocumentContentId,
                status, phases, data.schemaVersion == LegacySchemaVersion
                    ? -1 : data.approvedSourceLayerCount);
        }

        private static T Parse<T>(string value) where T : struct
        {
            if (!Enum.TryParse(value, false, out T result) || !Enum.IsDefined(typeof(T), result) ||
                value != result.ToString()) throw new ArgumentException("An exact declared " + typeof(T).Name + " is required.");
            return result;
        }

        public DungeonSideEventTimelineDefinition(string contentId, string sourceDocumentContentId,
            DungeonSideEventTimelineStatus status, IReadOnlyList<DungeonSideEventTimelinePhase> phases,
            int approvedSourceLayerCount = -1)
        {
            ContentId = SideEventSourceGuard.ContentId(contentId, "sidetimeline");
            SourceDocumentContentId = SideEventSourceGuard.ContentId(sourceDocumentContentId, "sidesource");
            if (!Enum.IsDefined(typeof(DungeonSideEventTimelineStatus), status))
                throw new ArgumentException("A declared timeline status is required.", nameof(status));
            Status = status;
            if (phases == null || phases.Count == 0)
                throw new ArgumentException("A timeline must declare its complete phases.", nameof(phases));
            int approved = approvedSourceLayerCount == -1
                ? (status == DungeonSideEventTimelineStatus.Enabled ? phases.Count : 0)
                : approvedSourceLayerCount;
            if ((status == DungeonSideEventTimelineStatus.Candidate && approved != 0) ||
                (status == DungeonSideEventTimelineStatus.Enabled && approved != phases.Count) ||
                (status == DungeonSideEventTimelineStatus.PartialEnabled && (approved <= 0 || approved >= phases.Count)))
                throw new ArgumentException("Candidate approves none, Enabled approves all, and PartialEnabled requires an explicit proper prefix.");
            ApprovedSourceLayerCount = approved;
            var copy = new List<DungeonSideEventTimelinePhase>(phases.Count);
            var phaseIds = new HashSet<string>(StringComparer.Ordinal);
            var layerIds = new HashSet<string>(StringComparer.Ordinal);
            var sourceSlots = new HashSet<string>(StringComparer.Ordinal);
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            var effectIds = new HashSet<string>(StringComparer.Ordinal);
            var closedFloors = new HashSet<string>(StringComparer.Ordinal);
            string previousFloor = null;
            for (int phaseIndex = 0; phaseIndex < phases.Count; phaseIndex++)
            {
                var phase = phases[phaseIndex];
                if (phase == null || !phaseIds.Add(phase.ContentId) || !layerIds.Add(phase.SourceLayerContentId))
                    throw new ArgumentException("Phase and source layer identities must be unique.");
                if (phase.FloorContentId != previousFloor)
                {
                    if (closedFloors.Contains(phase.FloorContentId) || phase.Trigger != DungeonSideEventTimelineTrigger.FloorEntry)
                        throw new ArgumentException("Each contiguous floor starts with its own FloorEntry phase.");
                    if (previousFloor != null) closedFloors.Add(previousFloor);
                }
                else if (phase.Trigger != DungeonSideEventTimelineTrigger.AfterEncounter)
                    throw new ArgumentException("Later phases on one floor require independent encounter triggers.");
                foreach (var slot in phase.Slots)
                {
                    if (!sourceSlots.Add(slot.SourceSlotContentId) || !nodeIds.Add(slot.NodeContentId) || !effectIds.Add(slot.EffectContentId))
                        throw new ArgumentException("Source slots, logical nodes and effects must each bind uniquely.");
                    if (slot.PlacementApproved != (phaseIndex < approved))
                        throw new ArgumentException("Every approved prefix slot is required; later slots remain explicitly unapproved.");
                }
                copy.Add(phase); previousFloor = phase.FloorContentId;
            }
            if (approved > 0 && approved < copy.Count &&
                copy[approved - 1].FloorContentId == copy[approved].FloorContentId)
                throw new ArgumentException("An approval prefix cannot split source phases on the same project floor.");
            foreach (var phase in copy)
                if (phase.TriggerNodeContentId != null && nodeIds.Contains(phase.TriggerNodeContentId))
                    throw new ArgumentException("A source interaction slot cannot also be its encounter trigger.");
            Phases = new ReadOnlyCollection<DungeonSideEventTimelinePhase>(copy);
        }
    }
}
