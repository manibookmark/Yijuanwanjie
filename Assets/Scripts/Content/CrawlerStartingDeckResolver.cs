using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 英灵起手包的唯一装配入口。英灵配置只拥有“金卡 + 固定四张支援牌”知识；
    /// 战斗场景只保留其前置通用牌，不能再自行复制英灵牌表。
    /// </summary>
    public static class CrawlerStartingDeckResolver
    {
        private const string LegacySunWukongSelectionAlias = "wukong";

        public const int RequiredCommonCardCount = 5;
        public const int RequiredSupportCardCount = 4;
        public const int RequiredPackageCardCount =
            RequiredSupportCardCount + 1;

        /// <summary>按英灵稳定 ID 解析金卡本体和四张支援牌。</summary>
        public static List<CardConfig> ResolvePackage(
            string crawlerContentId,
            IReadOnlyList<CardConfig> cardPool,
            Func<string, bool> isEligible)
        {
            CrawlerData crawler = ResolveConfiguredCrawler(
                crawlerContentId,
                "英灵起手包");
            List<string> packageIds = BuildPackageContentIds(crawler);
            return StableCardPoolResolver.ResolveRequired(
                packageIds,
                cardPool,
                isEligible,
                $"英灵起手包 {crawler.contentId} ");
        }

        /// <summary>
        /// 为新 Run 组合“场景通用前缀 + 所选英灵金卡 + 四张支援牌”。
        /// 旧场景仍把预选英灵包放在 deckContentIds 尾部；本方法只继承固定
        /// 五张通用牌并丢弃其后全部遗留牌，使场景只承担兼容回退，不再拥有
        /// 英灵牌表知识。
        /// </summary>
        public static List<CardConfig> ComposeRunEntryDeck(
            IReadOnlyList<string> scenarioDeckContentIds,
            string scenarioCrawlerContentId,
            string selectedCrawlerContentId,
            IReadOnlyList<CardConfig> cardPool,
            Func<string, bool> isEligible)
        {
            if (scenarioDeckContentIds == null)
                throw new ArgumentNullException(nameof(scenarioDeckContentIds));
            if (cardPool == null)
                throw new ArgumentNullException(nameof(cardPool));

            if (scenarioDeckContentIds.Count < RequiredCommonCardCount)
            {
                throw new InvalidOperationException(
                    $"战斗场景至少需要 {RequiredCommonCardCount} 张通用起始牌，" +
                    $"当前为 {scenarioDeckContentIds.Count} 张。");
            }

            CrawlerData scenarioCrawler = ResolveScenarioCrawler(
                scenarioDeckContentIds,
                scenarioCrawlerContentId);
            CrawlerData selectedCrawler = ResolveSelectedCrawler(
                selectedCrawlerContentId,
                scenarioCrawler);

            int packageStart = IndexOf(
                scenarioDeckContentIds,
                scenarioCrawler.cardContentId,
                RequiredCommonCardCount);
            if (packageStart < 0)
            {
                throw new InvalidOperationException(
                    $"战斗场景牌组缺少预选英灵金卡：" +
                    $"{scenarioCrawler.cardContentId}。");
            }

            var contentIds = new List<string>(
                RequiredCommonCardCount + RequiredPackageCardCount);
            for (int i = 0; i < RequiredCommonCardCount; i++)
                contentIds.Add(scenarioDeckContentIds[i]);
            contentIds.AddRange(BuildPackageContentIds(selectedCrawler));

            return StableCardPoolResolver.ResolveRequired(
                contentIds,
                cardPool,
                isEligible,
                $"Run 起始牌组（{selectedCrawler.contentId}）");
        }

        /// <summary>返回全新的有序稳定 ID 列表；重复项代表重复实例。</summary>
        public static List<string> BuildPackageContentIds(CrawlerData crawler)
        {
            if (crawler == null)
                throw new ArgumentNullException(nameof(crawler));
            if (string.IsNullOrWhiteSpace(crawler.cardContentId))
            {
                throw new InvalidOperationException(
                    $"英灵 {crawler.contentId} 未配置金卡 cardContentId。");
            }
            if (crawler.startingCardContentIds == null ||
                crawler.startingCardContentIds.Count !=
                    RequiredSupportCardCount)
            {
                int count = crawler.startingCardContentIds?.Count ?? 0;
                throw new InvalidOperationException(
                    $"英灵 {crawler.contentId} 必须配置 " +
                    $"{RequiredSupportCardCount} 张支援牌，当前为 {count} 张。");
            }

            var result = new List<string>(RequiredPackageCardCount)
            {
                crawler.cardContentId
            };
            for (int i = 0; i < crawler.startingCardContentIds.Count; i++)
            {
                string contentId = crawler.startingCardContentIds[i];
                if (string.IsNullOrWhiteSpace(contentId))
                {
                    throw new InvalidOperationException(
                        $"英灵 {crawler.contentId} 的支援牌[{i}] contentId 为空。");
                }
                if (string.Equals(
                        contentId,
                        crawler.cardContentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"英灵 {crawler.contentId} 的四张支援牌不得包含金卡本体。");
                }
                result.Add(contentId);
            }
            return result;
        }

        private static CrawlerData ResolveSelectedCrawler(
            string selectedCrawlerContentId,
            CrawlerData fallback)
        {
            if (string.IsNullOrWhiteSpace(selectedCrawlerContentId))
                return fallback;

            if (string.Equals(
                    selectedCrawlerContentId,
                    LegacySunWukongSelectionAlias,
                    StringComparison.OrdinalIgnoreCase))
            {
                selectedCrawlerContentId =
                    GameSession.DefaultCrawlerContentId;
            }

            if (!ContentCatalog.Current.TryGetCrawler(
                    selectedCrawlerContentId,
                    out CrawlerData selected))
            {
                throw new InvalidOperationException(
                    $"所选英灵不存在：{selectedCrawlerContentId}。");
            }

            // 选择了真实存在的英灵后，配置缺口必须显式失败，不能静默换回
            // 场景预选角色，否则 UI 选择与实际牌组会不一致。
            BuildPackageContentIds(selected);
            return selected;
        }

        private static CrawlerData ResolveScenarioCrawler(
            IReadOnlyList<string> scenarioDeckContentIds,
            string scenarioCrawlerContentId)
        {
            if (!string.IsNullOrWhiteSpace(scenarioCrawlerContentId))
            {
                return ResolveConfiguredCrawler(
                    scenarioCrawlerContentId,
                    "战斗场景预选英灵");
            }

            CrawlerData match = null;
            IReadOnlyList<VolumeContent> volumes =
                ContentCatalog.Current.Volumes;
            for (int volumeIndex = 0;
                 volumeIndex < volumes.Count;
                 volumeIndex++)
            {
                List<CrawlerData> crawlers = volumes[volumeIndex]?.crawlers;
                if (crawlers == null)
                    continue;

                for (int crawlerIndex = 0;
                     crawlerIndex < crawlers.Count;
                     crawlerIndex++)
                {
                    CrawlerData crawler = crawlers[crawlerIndex];
                    if (crawler == null ||
                        string.IsNullOrWhiteSpace(crawler.cardContentId) ||
                        IndexOf(
                            scenarioDeckContentIds,
                            crawler.cardContentId,
                            RequiredCommonCardCount) < 0)
                    {
                        continue;
                    }

                    BuildPackageContentIds(crawler);
                    if (match != null)
                    {
                        throw new InvalidOperationException(
                            "战斗场景未配置预选英灵 ID，且遗留牌组包含多个" +
                            $"英灵金卡：{match.cardContentId}、" +
                            $"{crawler.cardContentId}。");
                    }
                    match = crawler;
                }
            }

            if (match == null)
            {
                throw new InvalidOperationException(
                    "战斗场景未配置预选英灵 ID，且遗留牌组中无法推断" +
                    "已配置四张支援牌的英灵金卡。");
            }
            return match;
        }

        private static CrawlerData ResolveConfiguredCrawler(
            string crawlerContentId,
            string label)
        {
            if (string.IsNullOrWhiteSpace(crawlerContentId) ||
                !ContentCatalog.Current.TryGetCrawler(
                    crawlerContentId,
                    out CrawlerData crawler))
            {
                throw new InvalidOperationException(
                    $"{label}不存在：{crawlerContentId}。");
            }
            BuildPackageContentIds(crawler);
            return crawler;
        }

        private static int IndexOf(
            IReadOnlyList<string> values,
            string expected,
            int startIndex = 0)
        {
            for (int i = Math.Max(0, startIndex); i < values.Count; i++)
            {
                if (string.Equals(
                        values[i],
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
