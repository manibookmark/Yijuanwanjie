using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns the demo's release scope only. A permitted ID still needs the normal
    /// content, manual acceptance and progression gates; this policy unlocks nothing.
    /// The tutorial is admitted by its exact level ID without a dungeon ID.
    /// </summary>
    public static class DemoReleasePolicy
    {
        public const string TutorialLevelContentId = "level.tutorial.huaguoshan.v1";

        public static bool IsDemoBuild
        {
            get
            {
#if WANJIE_DEMO
                return true;
#else
                return false;
#endif
            }
        }

        public static bool AllowsDungeon(string dungeonId) => AllowsDungeon(dungeonId, IsDemoBuild);
        public static bool AllowsLevel(string levelContentId, string dungeonContentId)
            => AllowsLevel(levelContentId, dungeonContentId, IsDemoBuild);
        public static bool AllowsCard(string cardId) => AllowsCard(cardId, IsDemoBuild);
        public static bool AllowsCrawler(string crawlerId) => AllowsCrawler(crawlerId, IsDemoBuild);
        public static bool IsDemoFinale(string dungeonId) => IsDemoFinale(dungeonId, IsDemoBuild);

        /// <summary>Exact stable dungeon IDs; future releases are outside demo scope.</summary>
        public static bool AllowsDungeon(string dungeonId, bool demo)
        {
            return !demo ||
                string.Equals(dungeonId, "dungeon.xiyou.shuangchaling", StringComparison.Ordinal) ||
                string.Equals(dungeonId, "dungeon.xiyou.yingchoujian", StringComparison.Ordinal) ||
                string.Equals(dungeonId, "dungeon.xiyou.gaolaozhuang", StringComparison.Ordinal);
        }

        /// <summary>
        /// Accepts the explicit tutorial or a level owned by an allowed dungeon.
        /// Existence and the level's actual ownership still require normal validation.
        /// </summary>
        public static bool AllowsLevel(string levelContentId, string dungeonContentId, bool demo)
        {
            if (!demo) return true;
            if (string.Equals(levelContentId, TutorialLevelContentId, StringComparison.Ordinal))
                return string.IsNullOrEmpty(dungeonContentId);
            return !string.IsNullOrWhiteSpace(levelContentId) && AllowsDungeon(dungeonContentId, true);
        }

        /// <summary>Additional demo exclusion, composed with existing card admission.</summary>
        public static bool AllowsCard(string cardId, bool demo)
        {
            return !demo || (!string.IsNullOrWhiteSpace(cardId) &&
                !string.Equals(cardId, "card.xiyou.guanyin.v1", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Also covers direct crawler admission without a card offering.</summary>
        public static bool AllowsCrawler(string crawlerId, bool demo)
        {
            return !demo || (!string.IsNullOrWhiteSpace(crawlerId) &&
                !string.Equals(crawlerId, "crawler.xiyou.guanyin.v1", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Only a successful final clear of this dungeon should end the demo.</summary>
        public static bool IsDemoFinale(string dungeonId, bool demo)
        {
            return demo && string.Equals(dungeonId, "dungeon.xiyou.gaolaozhuang", StringComparison.Ordinal);
        }
    }
}
