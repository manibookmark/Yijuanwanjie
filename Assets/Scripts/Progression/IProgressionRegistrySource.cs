using System;
using System.Collections.Generic;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Authoring catalogs expose their existing permanent-progress projection
    /// without giving a headless host a dependency on presentation assemblies.
    /// Reading this contract never grants an entry or changes account progress.
    /// </summary>
    public interface IProgressionRegistrySource
    {
        IReadOnlyList<ProgressionRegistryItemSnapshot> ReadProgression(MetaProgression account);
    }

    [Serializable]
    public sealed class ProgressionRegistryItemSnapshot
    {
        public string contentId, kind, displayName;
        public bool unlocked;
    }
}
