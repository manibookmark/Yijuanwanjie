using System.Collections.Generic;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Progression;
using WanjieScroll.UI.Hub;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Compatibility adapter for existing scene consumers. Authored catalog
    /// projection and completion rules are shared with non-UI Run settlement.
    /// </summary>
    public sealed class DungeonCompletionUnlockResolver
    {
        private readonly DungeonCompletionUnlockCatalog _catalog;

        public DungeonCompletionUnlockResolver(TianluRegistryCatalog tianluCatalog, HubWorldCatalog hubWorldCatalog)
            : this(DungeonCompletionUnlockCatalog.FromSources(tianluCatalog, hubWorldCatalog)) { }

        private DungeonCompletionUnlockResolver(DungeonCompletionUnlockCatalog catalog) => _catalog = catalog;

        public static DungeonCompletionUnlockResolver LoadDefault()
            => new DungeonCompletionUnlockResolver(DungeonCompletionUnlockCatalog.LoadDefault());

        public IReadOnlyList<DungeonRunRewardResult> Resolve(string clearedLevelContentId,
            bool newlyCleared, IReadOnlyList<LevelRewardReceipt> directRewards)
            => _catalog.Resolve(clearedLevelContentId, newlyCleared, directRewards);
    }
}
