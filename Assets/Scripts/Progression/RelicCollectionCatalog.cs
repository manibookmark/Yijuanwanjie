using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Builds the read-only collection from actual permanent level grants.
    /// Imported future relic definitions never become collection goals by themselves.
    /// Account ownership remains authoritative; this catalog does not unlock or equip anything.
    /// </summary>
    public static class RelicCollectionCatalog
    {
        public static RelicCollectionSnapshot Resolve(MetaProgression account,
            ContentCatalog content, IEnumerable<LevelRunProfile> levels)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            var items = new List<RelicCollectionItem>();
            var levelIds = new HashSet<string>(StringComparer.Ordinal);
            var relicIds = new HashSet<string>(StringComparer.Ordinal);
            var targetKinds = new Dictionary<string, LevelRewardKind>(StringComparer.Ordinal);

            foreach (LevelRunProfile level in levels)
            {
                if (level == null || string.IsNullOrWhiteSpace(level.contentId) ||
                    !levelIds.Add(level.contentId))
                    throw new InvalidOperationException("Relic collection requires distinct stable level IDs.");
                foreach (LevelNode node in level.nodes ?? new List<LevelNode>())
                {
                    if (node == null)
                        throw new InvalidOperationException("Relic collection encountered a null level node.");
                    foreach (LevelInteractionEffect effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                    {
                        if (effect == null)
                            throw new InvalidOperationException("Relic collection encountered a null level effect.");
                        if (effect.kind == LevelInteractionEffectKind.GrantRelic)
                            Add(effect.targetContentId, LevelRewardKind.RelicUnlock);
                    }
                    foreach (LevelRewardGrant grant in node.victoryRewards ?? new List<LevelRewardGrant>())
                    {
                        if (grant == null)
                            throw new InvalidOperationException("Relic collection encountered a null victory reward.");
                        if (grant.kind == LevelRewardKind.RelicUnlock || grant.kind == LevelRewardKind.CrawlerUnlock)
                            Add(grant.targetContentId, grant.kind);
                    }
                }
            }
            // Resource enumeration order is not a product rule. Stable IDs make
            // this read-only catalog deterministic without depending on map routes.
            items.Sort((left, right) => StringComparer.Ordinal.Compare(left.ContentId, right.ContentId));
            return new RelicCollectionSnapshot(items);

            void Add(string contentId, LevelRewardKind kind)
            {
                if (string.IsNullOrWhiteSpace(contentId))
                    throw new InvalidOperationException("Permanent collection rewards require a stable target ID.");
                if (targetKinds.TryGetValue(contentId, out LevelRewardKind previous) && previous != kind)
                    throw new InvalidOperationException("Permanent reward target has conflicting kinds: " + contentId);
                targetKinds[contentId] = kind;
                if (kind != LevelRewardKind.RelicUnlock || !relicIds.Add(contentId)) return;
                if (!content.TryGetRelic(contentId, out RelicData relic) ||
                    !string.Equals(relic.contentId, contentId, StringComparison.Ordinal) ||
                    !Enum.IsDefined(typeof(RelicType), relic.relicType) ||
                    string.IsNullOrWhiteSpace(relic.name) || string.IsNullOrWhiteSpace(relic.description) ||
                    string.IsNullOrWhiteSpace(relic.unlockLocation))
                    throw new InvalidOperationException("Formal collection relic is missing valid content: " + contentId);
                items.Add(new RelicCollectionItem(contentId, relic.name, relic.description,
                    relic.unlockLocation, account?.IsRelicUnlocked(contentId) ?? false));
            }
        }
    }

    /// <summary>A detached collection result; it cannot write back to account or author data.</summary>
    public sealed class RelicCollectionSnapshot
    {
        public IReadOnlyList<RelicCollectionItem> Items { get; }
        public int OwnedCount { get; }
        public int TotalCount => Items.Count;

        internal RelicCollectionSnapshot(List<RelicCollectionItem> items)
        {
            Items = new List<RelicCollectionItem>(items).AsReadOnly();
            int ownedCount = 0;
            foreach (RelicCollectionItem item in Items)
                if (item.IsOwned) ownedCount++;
            OwnedCount = ownedCount;
        }
    }

    public sealed class RelicCollectionItem
    {
        public string ContentId { get; }
        public string Name { get; }
        public string Description { get; }
        public string UnlockLocation { get; }
        public bool IsOwned { get; }

        internal RelicCollectionItem(string contentId, string name, string description,
            string unlockLocation, bool isOwned)
        {
            ContentId = contentId;
            Name = name;
            Description = description;
            UnlockLocation = unlockLocation;
            IsOwned = isOwned;
        }
    }
}
