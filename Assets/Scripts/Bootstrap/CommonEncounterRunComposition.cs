using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Wires content lookup before hosted Run admission/restore. The
    /// delegate resolves lazily; composition generates no choices or actors.</summary>
    public static class CommonEncounterRunComposition
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void EnsureInstalled()
        {
            DungeonRunSessionStore.ConfigureCommonEncounterSources(
                id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id));
        }
    }
}
