using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>Owns compilation of explicit author DTOs into the complete,
    /// immutable Core source registry. Core constructors own source invariants;
    /// this author boundary registers declarations within their containing volume.
    /// The shared Core wire compiler supplies the one canonical conversion.
    /// It never infers a mapping, removes unsupported events or evaluates eligibility.</summary>
    public static class SideEventSourceContentCompiler
    {
        public static bool TryCompile(SideEventSourceContentData data,
            out SideEventSourceDefinition definition, out IReadOnlyList<string> errors)
            => SideEventSourceWireCompiler.TryCompile(data, out definition, out errors);

        internal static void ValidateVolume(VolumeContent volume, IReadOnlyList<VolumeContent> volumes,
            HashSet<string> allIds, ContentValidationReport report)
        {
            if (volume.sideEventSources == null) return; // Optional schema-6 extension.
            for (int i = 0; i < volume.sideEventSources.Count; i++)
            {
                SideEventSourceContentData data = volume.sideEventSources[i];
                string path = "volume[" + volume.volumeId + "].sideEventSources[" + i + "]";
                if (!TryCompile(data, out _, out IReadOnlyList<string> errors))
                    foreach (string error in errors) report.Error(path, error);
                if (data == null) continue;
                // Declarations are unique, references and weighted occurrences are not.
                Register(data.contentId, volume.volumeId, allIds, path, report);
                foreach (SideEventSourceRootData root in data.roots ?? new List<SideEventSourceRootData>())
                    if (root != null) Register(root.contentId, volume.volumeId, allIds, path, report);
                foreach (SideEventSourceGroupData group in data.groups ?? new List<SideEventSourceGroupData>())
                    if (group != null) Register(group.contentId, volume.volumeId, allIds, path, report);
                foreach (SideEventSourceLayerData layer in data.layers ?? new List<SideEventSourceLayerData>())
                {
                    if (layer == null) continue;
                    Register(layer.contentId, volume.volumeId, allIds, path, report);
                    foreach (SideEventSourceSlotData slot in layer.slots ?? new List<SideEventSourceSlotData>())
                        if (slot != null) Register(slot.contentId, volume.volumeId, allIds, path, report);
                }
                var supportingIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (SideEventSourceRootData root in data.roots ?? new List<SideEventSourceRootData>())
                {
                    SideEventMechanicSpecificationData mechanic = root?.mechanic;
                    if (mechanic == null) continue;
                    if (mechanic.abandonedCart != null)
                    {
                        if (supportingIds.Add(mechanic.abandonedCart.dropConfigContentId ?? ""))
                            Register(mechanic.abandonedCart.dropConfigContentId, volume.volumeId, allIds, path, report);
                        if (supportingIds.Add(mechanic.abandonedCart.dropTagContentId ?? ""))
                            Register(mechanic.abandonedCart.dropTagContentId, volume.volumeId, allIds, path, report);
                    }
                    foreach (SideEventGemOfferData offer in new[] { mechanic.evoStatue?.offer,
                        mechanic.gemChest?.offer, mechanic.silverGemChest?.offer })
                        foreach (SideEventGemFrequencyData frequency in offer?.frequencies ?? new List<SideEventGemFrequencyData>())
                            if (frequency != null && supportingIds.Add(frequency.contentId ?? ""))
                                Register(frequency.contentId, volume.volumeId, allIds, path, report);
                }
            }
        }

        private static void Register(string id, string volumeId, HashSet<string> allIds, string path, ContentValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(id)) return; // The Core constructor already reports invalid declarations.
            string[] parts = id.Split('.');
            if (parts.Length < 3 || !string.Equals(parts[1], volumeId, StringComparison.Ordinal))
                report.Error(path, "Source declaration must belong to its containing volume: " + id + ".");
            if (!allIds.Add(id)) report.Error(path, "contentId 重复：" + id + "。");
        }

    }
}
