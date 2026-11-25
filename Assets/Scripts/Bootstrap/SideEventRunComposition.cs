using UnityEngine;
using WanjieScroll.Dungeon;
using WanjieScroll.Progression;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Prevents legacy hosted admission from bypassing an explicitly bound
    /// formal source timeline. Formal preparation supplies the actual account context.</summary>
    internal static class SideEventRunComposition
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            DungeonRunSessionStore.ConfigureSideEventStartGuard(profile =>
            {
                bool found = RunEntryLoadoutProfileCatalog.TryFindForFirstFloor(profile.contentId,
                    out var entry, out string error);
                if (!found && error != null) throw new System.InvalidOperationException(error);
                return found && !string.IsNullOrEmpty(entry.sideEventTimelineContentId);
            });
        }
    }
}
