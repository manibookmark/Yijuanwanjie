using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 一套可复放的外部实测入场基线。它只拥有实测明确给出的牌组与基础
    /// 属性，不把关卡路线、升级表或交互结果藏进起始装载。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewRunReferenceLoadout",
        menuName = "万界长卷/Run 实测参考装载")]
    public sealed class RunReferenceLoadoutProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [Header("身份")]
        public int schemaVersion = CurrentSchemaVersion;
        [Tooltip("用于关卡交互与场景的稳定关联键；不使用显示名称。")]
        public string contentId = "loadout.reference.new";

        [Header("英灵与起始牌组")]
        [Tooltip("实测入场的固定英灵；此装载不接受玩家换人。")]
        public string crawlerContentId;
        [Tooltip("有序、可重复的起始牌；必须与该英灵的金卡 + 四张支援牌完全一致。")]
        public List<string> deckContentIds = new List<string>();

        [Header("实测基础属性")]
        [Min(1)] public int baseMana = 3;
        [Min(0)] public int startingArea;
        [Min(0)] public int startingGrowth;
        [Tooltip("复放竞品实测时不叠加本项目局外经诀属性；遗珍与 Arcana 规则仍照常读取。")]
        public bool suppressMetaStatProgression = true;

        public void ApplyTo(PlayerStats player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            player.faLi = Mathf.Max(1, baseMana);
            player.bengLie = Mathf.Max(0, startingArea);
            player.mingWu = Mathf.Max(0, startingGrowth);
        }

        /// <summary>
        /// 每次调用都解析出新的运行时卡实例；Profile 从不持有可变战斗卡。
        /// </summary>
        public List<CardConfig> ResolveDeck(
            IReadOnlyList<CardConfig> cardPool,
            Func<string, bool> isEligible)
        {
            return StableCardPoolResolver.ResolveRequired(
                deckContentIds,
                cardPool,
                isEligible,
                $"参考装载 {contentId} 的起始牌组");
        }

        public List<string> ValidateProfile(ContentCatalog catalog = null)
        {
            catalog ??= ContentCatalog.Current;
            var errors = new List<string>();
            if (schemaVersion != CurrentSchemaVersion)
                errors.Add($"schemaVersion 必须为 {CurrentSchemaVersion}。");
            if (string.IsNullOrWhiteSpace(contentId))
                errors.Add("contentId 不能为空。");
            if (baseMana < 1)
                errors.Add("baseMana 必须大于 0。");
            if (startingArea < 0 || startingGrowth < 0)
                errors.Add("Area 与 Growth 不能为负数。");

            if (!TryGetCrawler(catalog, crawlerContentId, out CrawlerData crawler))
            {
                errors.Add($"参考英灵 contentId 不存在：{crawlerContentId}。");
                return errors;
            }

            List<string> expected;
            try
            {
                expected = CrawlerStartingDeckResolver.BuildPackageContentIds(
                    crawler);
            }
            catch (Exception exception)
            {
                errors.Add("参考英灵起手包无效：" + exception.Message);
                return errors;
            }

            if (deckContentIds == null || !deckContentIds.SequenceEqual(
                    expected, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(
                    "参考起始牌组必须严格等于固定英灵的金卡加四张支援牌。" +
                    "重复项与顺序均属于合同。");
            }

            var cardIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (VolumeContent volume in catalog.Volumes)
            {
                foreach (CardData card in volume?.cards ??
                         new List<CardData>())
                {
                    if (!string.IsNullOrWhiteSpace(card?.contentId))
                        cardIds.Add(card.contentId);
                }
            }

            foreach (string cardId in deckContentIds ??
                     new List<string>())
            {
                if (string.IsNullOrWhiteSpace(cardId) ||
                    !cardIds.Contains(cardId))
                {
                    errors.Add($"参考起始牌不存在：{cardId}。");
                }
            }
            return errors;
        }

        private static bool TryGetCrawler(
            ContentCatalog catalog,
            string crawlerContentId,
            out CrawlerData crawler)
        {
            crawler = null;
            if (catalog == null || string.IsNullOrWhiteSpace(crawlerContentId))
                return false;

            foreach (VolumeContent volume in catalog.Volumes)
            {
                foreach (CrawlerData candidate in volume?.crawlers ??
                         new List<CrawlerData>())
                {
                    if (candidate != null && string.Equals(
                            candidate.contentId,
                            crawlerContentId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        crawler = candidate;
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>Run 实测参考装载的 Resources 读取入口。</summary>
    public static class RunReferenceLoadoutProfileCatalog
    {
        private const string ResourceDir = "Content/ReferenceLoadouts";
        private static readonly Dictionary<string, RunReferenceLoadoutProfile>
            Cache = new Dictionary<string, RunReferenceLoadoutProfile>(
                StringComparer.OrdinalIgnoreCase);

        public static RunReferenceLoadoutProfile Get(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                return null;
            if (Cache.TryGetValue(contentId, out RunReferenceLoadoutProfile cached))
                return cached;

            RunReferenceLoadoutProfile[] profiles =
                Resources.LoadAll<RunReferenceLoadoutProfile>(ResourceDir);
            Cache.Clear();
            for (int i = 0; i < profiles.Length; i++)
            {
                RunReferenceLoadoutProfile profile = profiles[i];
                if (profile == null || string.IsNullOrWhiteSpace(
                        profile.contentId))
                {
                    continue;
                }
                if (!Cache.TryAdd(profile.contentId, profile))
                {
                    throw new InvalidOperationException(
                        "RunReferenceLoadoutProfile contentId 重复：" +
                        profile.contentId);
                }
            }
            return Cache.TryGetValue(contentId, out cached) ? cached : null;
        }

        public static void ClearCache() => Cache.Clear();
    }
}
