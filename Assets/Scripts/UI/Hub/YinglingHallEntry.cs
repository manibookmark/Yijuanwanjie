using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Meta;
using WanjieScroll.UI.Presentations;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// 群英殿名册的只读展示快照。内容来自正式内容包，解锁只读存档；
    /// 不把遗留招募价、未制作的占位英灵或演出是否存在当成解锁规则。
    /// </summary>
    public sealed class YinglingHallEntry
    {
        private YinglingHallEntry(
            CrawlerData data, string volumeId, string volumeName,
            bool unlocked, CharacterPresentationProfile arrival)
        {
            ContentId = data.contentId;
            DisplayName = data.name;
            VolumeId = volumeId;
            VolumeName = volumeName;
            Unlocked = unlocked;
            Playstyle = data.playstyleTag ?? string.Empty;
            EnterSummary = data.enterEffect ?? string.Empty;
            TriggerSummary = data.triggerEffect ?? string.Empty;
            UnlockHint = string.IsNullOrWhiteSpace(data.unlockCondition)
                ? "继续探索，在战斗中与这位英灵结缘。"
                : data.unlockCondition;
            ArrivalProfile = arrival;
            OpeningVoiceCueId = ResolveOpeningVoiceCue(arrival);
            Title = arrival == null ? volumeName : (arrival.displayTitle ?? string.Empty)
                .Replace(data.name, string.Empty).Trim(' ', '·', '・', '—');
        }

        public string ContentId { get; }
        public string DisplayName { get; }
        public string VolumeId { get; }
        public string VolumeName { get; }
        public string Title { get; }
        public string Playstyle { get; }
        public string EnterSummary { get; }
        public string TriggerSummary { get; }
        public string UnlockHint { get; }
        public string OpeningVoiceCueId { get; }
        public bool Unlocked { get; }
        public CharacterPresentationProfile ArrivalProfile { get; }

        private static string ResolveOpeningVoiceCue(CharacterPresentationProfile arrival)
        {
            if (arrival == null)
                return string.Empty;
            if (IsVoiceCue(arrival.openingAudioCueId))
                return arrival.openingAudioCueId;
            if (arrival.lines != null)
            {
                for (int i = 0; i < arrival.lines.Count; i++)
                {
                    string cueId = arrival.lines[i]?.audioCueId;
                    if (IsVoiceCue(cueId))
                        return cueId;
                }
            }
            return string.Empty;
        }

        private static bool IsVoiceCue(string cueId)
        {
            return !string.IsNullOrWhiteSpace(cueId) &&
                   cueId.StartsWith("voice.", StringComparison.Ordinal);
        }

        public static List<YinglingHallEntry> Load(MetaProgression meta)
        {
            var entries = new List<YinglingHallEntry>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            ContentCatalog catalog = ContentCatalog.Current;
            foreach (VolumeContent volume in catalog.Volumes)
            {
                foreach (CrawlerData crawler in volume.crawlers)
                {
                    if (crawler == null || string.IsNullOrWhiteSpace(crawler.contentId) ||
                        !DemoReleasePolicy.AllowsCrawler(crawler.contentId) ||
                        !ids.Add(crawler.contentId))
                        continue;
                    bool unlocked = meta.IsCrawlerUnlocked(crawler.contentId);
                    // 没有正式英灵卡的 legacy 条目不是当前发布名册；保留旧档已解锁者。
                    if (string.IsNullOrWhiteSpace(crawler.cardContentId) && !unlocked)
                        continue;

                    // 悟空的通用卡包归属不等于人物所属世界；展示映射只认稳定 ID。
                    string volumeId = crawler.contentId == GameSession.DefaultCrawlerContentId
                        ? "xiyou" : crawler.volumeId;
                    string volumeName = catalog.TryGetVolume(volumeId, out VolumeContent source)
                        ? source.displayName : volume.displayName;
                    CharacterPresentationCatalog.TryGetHeroArrivalForSubject(
                        crawler.contentId, out CharacterPresentationProfile arrival);
                    entries.Add(new YinglingHallEntry(
                        crawler, volumeId, volumeName, unlocked, arrival));
                }
            }
            return entries;
        }
    }
}
