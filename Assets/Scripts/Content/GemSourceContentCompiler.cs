using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// The author boundary admits explicit Gem registry declarations into their
    /// containing volume. Core owns complete source invariants and immutable copies.
    /// Referenced project seals are not declarations here; no grade, tag or display
    /// name creates an identity and no account, eligibility or Run state is read.
    /// </summary>
    public static class GemSourceContentCompiler
    {
        public static bool TryCompile(GemSourceRegistryData data,
            out GemSourceRegistryDefinition definition, out IReadOnlyList<string> errors)
            => GemSourceRegistryCompiler.TryCompile(data, out definition, out errors);

        internal static void ValidateVolume(VolumeContent volume, HashSet<string> allIds,
            ContentValidationReport report)
        {
            if (volume.gemSources == null) return;
            for (int i = 0; i < volume.gemSources.Count; i++)
            {
                GemSourceRegistryData data = volume.gemSources[i];
                string path = "volume[" + volume.volumeId + "].gemSources[" + i + "]";
                if (!TryCompile(data, out _, out IReadOnlyList<string> errors))
                    foreach (string error in errors) report.Error(path, error);
                if (data == null || string.IsNullOrWhiteSpace(data.contentId)) continue;
                string[] parts = data.contentId.Split('.');
                if (parts.Length < 3 || !string.Equals(parts[1], volume.volumeId, StringComparison.Ordinal))
                    report.Error(path, "Gem registry must belong to its containing volume: " + data.contentId + ".");
                if (!allIds.Add(data.contentId)) report.Error(path, "Duplicate contentId: " + data.contentId + ".");
            }
        }
    }
}
