using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Selects the encounter's seed definitions without creating cards or changing
    /// Run state. Tutorial seeds follow their authored teaching sequence; ordinary
    /// empty Runs retain the formal common-prefix and selected-crawler contract.
    /// DungeonRunSession remains the owner of an already established physical deck.
    /// </summary>
    public static class RunCombatDeckResolver
    {
        public static List<CardConfig> Resolve(BattleScenarioProfile scenario,
            DungeonRunSession activeRun, string selectedCrawlerContentId,
            IReadOnlyList<CardConfig> cardPool, Func<string, bool> isEligible)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            RunReferenceLoadoutProfile reference = scenario.ResolveReferenceLoadout();
            if (reference != null) return reference.ResolveDeck(cardPool, isEligible);

            bool initializesRun = activeRun != null && activeRun.RunCardCount == 0 && !activeRun.HasPlayerState;
            if (scenario.deckContentIds == null || scenario.deckContentIds.Count == 0)
                throw new InvalidOperationException(initializesRun
                    ? "正式 Run 场景缺少起始牌组或实测参考装载。"
                    : "战斗场景缺少起始牌组。");

            bool tutorial = activeRun != null && string.Equals(
                activeRun.CurrentFloor.Profile.contentId,
                RunCombatRules.TutorialLevelContentId, StringComparison.Ordinal);
            if (initializesRun && !tutorial)
                return CrawlerStartingDeckResolver.ComposeRunEntryDeck(
                    scenario.deckContentIds, scenario.crawlerContentId,
                    selectedCrawlerContentId, cardPool, isEligible);

            return StableCardPoolResolver.ResolveRequired(scenario.deckContentIds,
                cardPool, isEligible, tutorial ? "教程战斗牌组" : "战斗牌组");
        }
    }
}
