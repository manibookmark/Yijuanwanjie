using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>Authoring assets expose completion rules without exposing their UI models.</summary>
    public interface IDungeonCompletionUnlockSource
    {
        IReadOnlyList<DungeonCompletionUnlockDefinition> GetCompletionUnlocks();
    }

    public sealed class DungeonCompletionUnlockDefinition
    {
        public string RequiredLevelContentId { get; }
        public string GrantContentId { get; }
        public DungeonRunRewardCategory Category { get; }
        public string TargetContentId { get; }
        public string DisplayName { get; }

        public DungeonCompletionUnlockDefinition(string requiredLevelContentId,
            string grantContentId, DungeonRunRewardCategory category,
            string targetContentId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(requiredLevelContentId) ||
                string.IsNullOrWhiteSpace(grantContentId) || string.IsNullOrWhiteSpace(targetContentId))
                throw new ArgumentException("Completion unlocks require stable condition, grant and target IDs.");
            if (!Enum.IsDefined(typeof(DungeonRunRewardCategory), category))
                throw new ArgumentOutOfRangeException(nameof(category));
            RequiredLevelContentId = requiredLevelContentId;
            GrantContentId = grantContentId;
            Category = category;
            TargetContentId = targetContentId;
            DisplayName = displayName ?? string.Empty;
        }
    }

    /// <summary>
    /// Resolves completion rewards from the original authoring assets through a
    /// typed, read-only contract. It never reads UI fields or duplicates a catalog.
    /// </summary>
    public sealed class DungeonCompletionUnlockCatalog
    {
        private readonly IReadOnlyList<DungeonCompletionUnlockDefinition> _definitions;

        public DungeonCompletionUnlockCatalog(IEnumerable<DungeonCompletionUnlockDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var copy = new List<DungeonCompletionUnlockDefinition>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DungeonCompletionUnlockDefinition definition in definitions)
            {
                if (definition == null || !ids.Add(definition.GrantContentId))
                    throw new ArgumentException("Completion unlock definitions are null or duplicate.");
                copy.Add(definition);
            }
            _definitions = copy.AsReadOnly();
        }

        public static DungeonCompletionUnlockCatalog FromSources(params IDungeonCompletionUnlockSource[] sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var definitions = new List<DungeonCompletionUnlockDefinition>();
            foreach (IDungeonCompletionUnlockSource source in sources)
            {
                if (source == null) throw new ArgumentException("Missing completion unlock source.");
                definitions.AddRange(source.GetCompletionUnlocks());
            }
            return new DungeonCompletionUnlockCatalog(definitions);
        }

        public static DungeonCompletionUnlockCatalog LoadDefault()
        {
            var sources = new List<IDungeonCompletionUnlockSource>();
            foreach (string path in new[] { "Content/Hub/tianlu-registry-catalog", "Content/Hub/hub-world-catalog" })
            {
                ScriptableObject asset = Resources.Load<ScriptableObject>(path);
                if (!(asset is IDungeonCompletionUnlockSource source))
                    throw new InvalidOperationException("Completion catalog does not implement its rule contract: " + path);
                sources.Add(source);
            }
            var authored = FromSources(sources.ToArray());
            var definitions = new List<DungeonCompletionUnlockDefinition>(authored._definitions)
            {
                new DungeonCompletionUnlockDefinition(
                    DaozangProgressionRules.GreedUnlockLevelContentId,
                    "unlock.daozang.greed.v1", DungeonRunRewardCategory.PowerUp,
                    "greed", DaozangProgressionRules.Get("greed").Name)
            };
            return new DungeonCompletionUnlockCatalog(definitions);
        }

        public IReadOnlyList<DungeonRunRewardResult> Resolve(string clearedLevelContentId,
            bool newlyCleared, IReadOnlyList<LevelRewardReceipt> directRewards)
        {
            if (string.IsNullOrWhiteSpace(clearedLevelContentId))
                throw new ArgumentException("Completion requires a stable level ID.", nameof(clearedLevelContentId));
            var results = new List<DungeonRunRewardResult>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (directRewards != null)
                foreach (LevelRewardReceipt reward in directRewards)
                    Add(new DungeonRunRewardResult(reward.GrantContentId, reward.Kind,
                        reward.TargetContentId, reward.DisplayName, reward.NewlyGranted), results, ids);
            foreach (DungeonCompletionUnlockDefinition definition in _definitions)
                if (string.Equals(definition.RequiredLevelContentId, clearedLevelContentId, StringComparison.OrdinalIgnoreCase))
                    Add(new DungeonRunRewardResult(definition.GrantContentId, definition.Category,
                        definition.TargetContentId, definition.DisplayName, newlyCleared), results, ids);
            return results.AsReadOnly();
        }

        private static void Add(DungeonRunRewardResult reward, List<DungeonRunRewardResult> results, HashSet<string> ids)
        {
            if (string.IsNullOrWhiteSpace(reward.GrantContentId) || string.IsNullOrWhiteSpace(reward.TargetContentId) ||
                !ids.Add(reward.GrantContentId))
                throw new InvalidOperationException("Completion reward IDs are missing or duplicate: " + reward.GrantContentId);
            results.Add(reward);
        }
    }
}
