using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>Owns full authored timeline admission, complete dependency preflight and
    /// saved-plan rebinding. Full source structure is always retained; PartialEnabled
    /// preflights only its explicitly approved complete-floor prefix. A Candidate remains reviewable author content. A referenced
    /// Candidate, missing dependency or changed saved plan fails explicitly, never as an empty plan.</summary>
    public static class DungeonSideEventProductionResolver
    {
        public static DungeonRunSideEventSetup ResolveEntry(RunEntryLoadoutProfile entry, MetaProgression account)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrEmpty(entry.sideEventTimelineContentId)) return null;
            var content = ContentCatalog.Current;
            if (!content.TryGetSideEventTimeline(entry.sideEventTimelineContentId, out var definition))
                throw new InvalidOperationException("Missing authored side-event timeline: " + entry.sideEventTimelineContentId);
            var setup = ResolveEnabled(definition, content, account, LevelRunProfileCatalog.Get, ProductionArcanas);
            if (setup.Plan.Phases[0].FloorContentId != entry.firstFloorContentId)
                throw new InvalidOperationException("The side-event timeline must start at the formal entry floor.");
            return setup;
        }

        public static DungeonRunSideEventSetup ResolveEnabled(DungeonSideEventTimelineDefinition definition,
            ContentCatalog content, MetaProgression account, Func<string, LevelRunProfile> floors,
            Func<MetaProgression, DungeonRunArcanaCatalog> arcanas, Func<string, bool> invitationEligible = null)
        {
            var errors = Inspect(definition, content, floors, out var plan);
            if (errors.Count != 0) throw new InvalidOperationException("Side-event timeline cannot be enabled:\n" + string.Join("\n", errors));
            return new DungeonRunSideEventSetup(plan,
                new DungeonSideEventProductionContext(plan, account, content, arcanas, invitationEligible));
        }

        public static IReadOnlyList<string> Inspect(DungeonSideEventTimelineDefinition definition,
            ContentCatalog content, Func<string, LevelRunProfile> floors, out DungeonRunSideEventPlan plan)
        {
            plan = null;
            if (definition == null || content == null || floors == null)
                throw new ArgumentNullException("Complete timeline, content and floor authority are required.");
            var errors = new List<string>();
            if (definition.Status == DungeonSideEventTimelineStatus.Candidate)
                errors.Add(definition.ContentId + ": Candidate; placement has not been approved for production.");
            if (!content.TryGetSideEventSourceDocument(definition.SourceDocumentContentId, out var document))
            { errors.Add("Missing complete source document: " + definition.SourceDocumentContentId); return errors.AsReadOnly(); }
            try { plan = CompilePlan(definition, document); }
            catch (Exception exception) { errors.Add(exception.Message); return errors.AsReadOnly(); }
            var profiles = new Dictionary<string, LevelRunProfile>(StringComparer.Ordinal);
            for (int phaseIndex = 0; phaseIndex < definition.Phases.Count; phaseIndex++)
            {
                var phase = definition.Phases[phaseIndex];
                if (definition.Status == DungeonSideEventTimelineStatus.PartialEnabled &&
                    phaseIndex >= definition.ApprovedSourceLayerCount) continue;
                if (!profiles.TryGetValue(phase.FloorContentId, out var profile))
                {
                    try
                    {
                        profile = floors(phase.FloorContentId);
                        if (profile == null || profile.contentId != phase.FloorContentId)
                            throw new InvalidOperationException("Missing or mismatched authored floor.");
                        foreach (string error in profile.Validate()) errors.Add(profile.contentId + ": " + error);
                    }
                    catch (Exception exception) { errors.Add(phase.FloorContentId + ": " + exception.Message); }
                    profiles.Add(phase.FloorContentId, profile);
                }
                if (profile == null) continue;
                if (phase.Trigger == DungeonSideEventTimelineTrigger.AfterEncounter)
                {
                    var trigger = profile.FindNode(phase.TriggerNodeContentId);
                    if (trigger?.nodeType != LevelNodeType.Encounter)
                        errors.Add(phase.ContentId + ": requires an existing ordinary/elite encounter trigger; finale publication is not admitted: " + phase.TriggerNodeContentId);
                }
                foreach (var slot in phase.Slots)
                {
                    if (!slot.PlacementApproved) errors.Add(slot.SourceSlotContentId + ": placement approval pending.");
                    if (profile.FindNode(slot.NodeContentId)?.nodeType != LevelNodeType.Interaction)
                        errors.Add(slot.SourceSlotContentId + ": missing authored Interaction " + slot.NodeContentId);
                }
            }
            errors.AddRange(DungeonRunSideEventSupport.Inspect(plan,
                includeUnapproved: definition.Status == DungeonSideEventTimelineStatus.Candidate));
            errors.AddRange(DungeonSideEventQualificationResolver.InspectRequirementContracts(document.Definition, content,
                definition.Status == DungeonSideEventTimelineStatus.Candidate ? document.Definition.Layers.Count :
                definition.ApprovedSourceLayerCount));
            return errors.AsReadOnly();
        }

        public static IDungeonRunSideEventContext CreateRestoreContext(DungeonRunSideEventPlan saved, MetaProgression account)
            => CreateRestoreContext(saved, account, ContentCatalog.Current, LevelRunProfileCatalog.Get, ProductionArcanas);

        public static IDungeonRunSideEventContext CreateRestoreContext(DungeonRunSideEventPlan saved, MetaProgression account,
            ContentCatalog content, Func<string, LevelRunProfile> floors,
            Func<MetaProgression, DungeonRunArcanaCatalog> arcanas)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            DungeonRunSideEventSetup match = null;
            foreach (var timeline in content.SideEventTimelines)
            {
                if (timeline.SourceDocumentContentId != saved.Document.Definition.ContentId) continue;
                if (!content.TryGetSideEventSourceDocument(timeline.SourceDocumentContentId, out var document)) continue;
                var authored = CompilePlan(timeline, document);
                if (!PlansEqual(authored, saved)) continue;
                if (match != null) throw new InvalidOperationException("Saved source plan matches ambiguous authored timelines.");
                match = ResolveEnabled(timeline, content, account, floors, arcanas);
            }
            if (match == null) throw new InvalidOperationException("Saved source plan has no exact complete enabled author timeline.");
            return match.Context;
        }

        private static DungeonRunSideEventPlan CompilePlan(DungeonSideEventTimelineDefinition definition,
            SideEventSourceWireDocument document)
        {
            var phases = new List<DungeonRunSideEventPhaseBinding>();
            foreach (var phase in definition.Phases)
            {
                var slots = new List<DungeonRunSideEventSlotBinding>();
                foreach (var slot in phase.Slots)
                    slots.Add(new DungeonRunSideEventSlotBinding(slot.SourceSlotContentId, slot.NodeContentId, slot.EffectContentId));
                phases.Add(new DungeonRunSideEventPhaseBinding(phase.ContentId, phase.FloorContentId, phase.SourceLayerContentId,
                    phase.Trigger == DungeonSideEventTimelineTrigger.FloorEntry ? DungeonRunSideEventPhaseTrigger.FloorEntry :
                    DungeonRunSideEventPhaseTrigger.AfterEncounter, phase.TriggerNodeContentId, slots));
            }
            if (phases.Count != document.Definition.Layers.Count)
                throw new InvalidOperationException("The full source timeline cannot omit native layers.");
            return new DungeonRunSideEventPlan(document, phases, definition.ApprovedSourceLayerCount);
        }

        private static bool PlansEqual(DungeonRunSideEventPlan a, DungeonRunSideEventPlan b)
        {
            if (a.Document.Fingerprint != b.Document.Fingerprint || a.Phases.Count != b.Phases.Count ||
                a.ApprovedSourceLayerCount != b.ApprovedSourceLayerCount) return false;
            for (int i = 0; i < a.Phases.Count; i++)
            {
                var x = a.Phases[i]; var y = b.Phases[i];
                if (x.PhaseContentId != y.PhaseContentId || x.FloorContentId != y.FloorContentId ||
                    x.SourceLayerContentId != y.SourceLayerContentId || x.Trigger != y.Trigger ||
                    x.TriggerNodeContentId != y.TriggerNodeContentId || x.Slots.Count != y.Slots.Count) return false;
                for (int j = 0; j < x.Slots.Count; j++)
                    if (x.Slots[j].SourceSlotContentId != y.Slots[j].SourceSlotContentId ||
                        x.Slots[j].NodeContentId != y.Slots[j].NodeContentId || x.Slots[j].EffectId != y.Slots[j].EffectId) return false;
            }
            return true;
        }

        private static DungeonRunArcanaCatalog ProductionArcanas(MetaProgression account)
        {
            ContentCatalog.Current.TryGetRelic(DungeonRunArcanaCatalogResolver.LegacyAcquisitionRelicContentId, out var relic);
            return DungeonRunArcanaCatalogResolver.ResolveFromContent(account,
                Resources.LoadAll<ArcanaConfig>("Content/Arcanas"), relic);
        }
    }
}
