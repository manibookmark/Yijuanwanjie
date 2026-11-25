using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// Owns admission of the complete author timeline against one immutable source
    /// document. The closed Core reader owns JSON shape; Core models own timeline
    /// identity/approval invariants. This compiler binds all original layers and slots
    /// in order, including empty native slots, without filtering mechanisms or reading
    /// accounts. Candidate is registered content, not an execution fallback. Actual
    /// node/trigger topology, capacity and adapter readiness are Run admission checks.
    /// </summary>
    public static class DungeonSideEventTimelineContentCompiler
    {
        public static List<DungeonSideEventTimelineContentData> ReadFromVolume(string json, string sourceLabel)
            => SideEventSourceWireCodec.ReadTimelineSourcesFromVolume(json, sourceLabel);

        public static bool TryCompile(DungeonSideEventTimelineContentData data,
            SideEventSourceWireDocument source, out DungeonSideEventTimelineDefinition definition,
            out IReadOnlyList<string> errors)
        {
            definition = null;
            var problems = new List<string>();
            try
            {
                if (data == null || (data.schemaVersion != DungeonSideEventTimelineDefinition.CurrentSchemaVersion &&
                    data.schemaVersion != DungeonSideEventTimelineDefinition.LegacySchemaVersion))
                    throw new ArgumentException("A complete supported timeline schema is required.");
                if (source == null || data.sourceDocumentContentId != source.Definition.ContentId)
                    throw new ArgumentException("The timeline must bind its exact registered source document.");
                var compiled = DungeonSideEventTimelineDefinition.Create(data);
                if (compiled.Phases.Count != source.Definition.Layers.Count)
                    throw new ArgumentException("Every original source layer must have one explicit ordered phase.");
                for (int i = 0; i < compiled.Phases.Count; i++)
                {
                    var phase = compiled.Phases[i]; var layer = source.Definition.Layers[i];
                    if (phase.SourceLayerContentId != layer.ContentId || phase.Slots.Count != layer.Slots.Count)
                        throw new ArgumentException("Phase order and complete slot count must match the source document.");
                    for (int j = 0; j < phase.Slots.Count; j++)
                        if (phase.Slots[j].SourceSlotContentId != layer.Slots[j].ContentId)
                            throw new ArgumentException("Every original source slot must bind in its original order.");
                }
                definition = compiled;
            }
            catch (ArgumentException exception) { problems.Add(exception.Message); }
            errors = problems.AsReadOnly();
            return definition != null;
        }

        internal static void ValidateVolume(VolumeContent volume, IReadOnlyList<VolumeContent> volumes,
            HashSet<string> allIds, ContentValidationReport report)
        {
            if (volume.sideEventTimelines == null) return;
            var documents = new Dictionary<string, SideEventSourceWireDocument>(StringComparer.Ordinal);
            foreach (SideEventSourceContentData source in volume.sideEventSources ?? new List<SideEventSourceContentData>())
            {
                if (!SideEventSourceWireCompiler.TryCompile(source, out _, out _)) continue;
                if (!documents.ContainsKey(source.contentId))
                    documents.Add(source.contentId, SideEventSourceWireDocument.Create(source));
            }
            for (int i = 0; i < volume.sideEventTimelines.Count; i++)
            {
                var data = volume.sideEventTimelines[i];
                string path = "volume[" + volume.volumeId + "].sideEventTimelines[" + i + "]";
                SideEventSourceWireDocument source = null;
                if (data?.sourceDocumentContentId != null) documents.TryGetValue(data.sourceDocumentContentId, out source);
                if (!TryCompile(data, source, out _, out IReadOnlyList<string> errors))
                    foreach (string error in errors) report.Error(path, error);
                if (data == null) continue;
                Register(data.contentId, volume.volumeId, allIds, path, report);
                foreach (var phase in data.phases ?? new List<DungeonSideEventTimelinePhaseData>())
                {
                    if (phase == null) continue;
                    Register(phase.contentId, volume.volumeId, allIds, path, report);
                    RequireVolume(phase.floorContentId, volume.volumeId, path, report);
                    if (!string.IsNullOrEmpty(phase.triggerNodeContentId))
                        RequireVolume(phase.triggerNodeContentId, volume.volumeId, path, report);
                    foreach (var slot in phase.slots ?? new List<DungeonSideEventTimelineSlotData>())
                        if (slot != null)
                        {
                            // Nodes/effects and source slots are references, never declarations here.
                            RequireVolume(slot.nodeContentId, volume.volumeId, path, report);
                            RequireVolume(slot.effectContentId, volume.volumeId, path, report);
                        }
                }
            }
        }

        private static void Register(string id, string volumeId, HashSet<string> ids, string path, ContentValidationReport report)
        {
            RequireVolume(id, volumeId, path, report);
            if (id != null && !ids.Add(id)) report.Error(path, "Duplicate timeline declaration: " + id + ".");
        }
        private static void RequireVolume(string id, string volumeId, string path, ContentValidationReport report)
        {
            string[] parts = id?.Split('.');
            if (parts == null || parts.Length < 3 || parts[1] != volumeId)
                report.Error(path, "Timeline declarations and project references must belong to the containing volume.");
        }
    }
}
