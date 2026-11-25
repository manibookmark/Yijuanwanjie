using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Content;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Formal new-Run authoring: owns the entry snapshot and base player values.
    /// Ordinary entries add common cards to the selected crawler's package;
    /// an explicitly authored source reference owns its fixed crawler and deck.
    /// The first-floor profile remains the owner of the prerequisite clear ID;
    /// crawler content remains the owner of its hero-plus-four-support package.
    /// </summary>
    [CreateAssetMenu(fileName = "NewRunEntry", menuName = "万界长卷/正式 Run 入口")]
    public sealed class RunEntryLoadoutProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public string contentId;
        public string firstFloorContentId;
        public string volumeId;
        [Tooltip("显式的正式起点；不随关卡默认调试快照或 SceneContext 改变。")]
        public string entrySnapshotId;
        [Tooltip("已确认的固定来源装载；空值使用所选英灵。引用时不再配置通用起始牌，也不重复叠加领卷属性。")]
        public string referenceLoadoutContentId;
        [Tooltip("完整作者来源时间线稳定 ID；空值保留已声明的旧入口。已配置却未启用时拒绝进入。")]
        public string sideEventTimelineContentId;
        [Min(1)] public int maxHealth = 50;
        [Min(1)] public int baseMana = 2;
        [Min(1)] public int handSize = 3;
        [Tooltip("仅通用起始牌；重复 ID 表示不同实例。英灵牌包来自所选英灵作者源。")]
        public List<string> commonCardContentIds = new List<string>();

        public List<string> ValidateProfile()
        {
            var errors = new List<string>();
            if (schemaVersion != CurrentSchemaVersion)
                errors.Add($"schemaVersion 必须为 {CurrentSchemaVersion}。");
            if (string.IsNullOrWhiteSpace(contentId)) errors.Add("contentId 不能为空。");
            if (string.IsNullOrWhiteSpace(firstFloorContentId))
                errors.Add("firstFloorContentId 不能为空。");
            if (string.IsNullOrWhiteSpace(volumeId)) errors.Add("volumeId 不能为空。");
            if (string.IsNullOrWhiteSpace(entrySnapshotId))
                errors.Add("entrySnapshotId 不能为空。");
            if (!string.IsNullOrEmpty(sideEventTimelineContentId) &&
                (string.IsNullOrWhiteSpace(sideEventTimelineContentId) ||
                 sideEventTimelineContentId != sideEventTimelineContentId.Trim()))
                errors.Add("来源时间线必须使用精确稳定 contentId。");
            if (maxHealth < 1 || baseMana < 1 || handSize < 1)
                errors.Add("正式入口基础生命、灵力与手牌数必须大于 0。");
            bool reference = !string.IsNullOrEmpty(referenceLoadoutContentId);
            if (reference && (string.IsNullOrWhiteSpace(referenceLoadoutContentId) ||
                referenceLoadoutContentId != referenceLoadoutContentId.Trim()))
                errors.Add("固定来源装载必须使用精确稳定 contentId。");
            if (reference)
            {
                if (commonCardContentIds == null || commonCardContentIds.Count != 0)
                    errors.Add("固定来源装载不能再追加通用起始牌。");
            }
            else if (commonCardContentIds == null || commonCardContentIds.Count !=
                CrawlerStartingDeckResolver.RequiredCommonCardCount)
                errors.Add($"正式入口必须配置 {CrawlerStartingDeckResolver.RequiredCommonCardCount} 张通用起始牌。");
            else
                for (int i = 0; i < commonCardContentIds.Count; i++)
                    if (string.IsNullOrWhiteSpace(commonCardContentIds[i]))
                        errors.Add($"通用起始牌[{i}] 缺少稳定 contentId。");
            return errors;
        }
    }

    /// <summary>Stable-ID lookup for formal entries; missing or duplicate assets never fall back.</summary>
    public static class RunEntryLoadoutProfileCatalog
    {
        private const string ResourceDir = "Content/RunEntries";
        private static readonly Dictionary<string, RunEntryLoadoutProfile> Cache =
            new Dictionary<string, RunEntryLoadoutProfile>(StringComparer.OrdinalIgnoreCase);

        public static RunEntryLoadoutProfile Get(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("正式 Run 入口 ID 不能为空。", nameof(contentId));
            if (Cache.TryGetValue(contentId, out RunEntryLoadoutProfile found)) return found;
            var loaded = new Dictionary<string, RunEntryLoadoutProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (RunEntryLoadoutProfile profile in Resources.LoadAll<RunEntryLoadoutProfile>(ResourceDir))
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.contentId))
                    throw new InvalidOperationException("正式 Run 入口资源缺少稳定 contentId。");
                if (!loaded.TryAdd(profile.contentId, profile))
                    throw new InvalidOperationException("正式 Run 入口 contentId 重复：" + profile.contentId);
            }
            Cache.Clear();
            foreach (KeyValuePair<string, RunEntryLoadoutProfile> entry in loaded)
                Cache.Add(entry.Key, entry.Value);
            if (!Cache.TryGetValue(contentId, out found))
                throw new InvalidOperationException("找不到正式 Run 入口配置：" + contentId);
            return found;
        }

        /// <summary>
        /// Defeat restart uses the same formal entry as Hub. False with no error
        /// means an unmigrated first floor may use its legacy entry; false with an
        /// error is a broken/ambiguous formal definition and must never fall back.
        /// </summary>
        public static bool TryFindForFirstFloor(string firstFloorContentId,
            out RunEntryLoadoutProfile profile, out string error)
        {
            try
            {
                return TryFindForFirstFloor(firstFloorContentId,
                    Resources.LoadAll<RunEntryLoadoutProfile>(ResourceDir), out profile, out error);
            }
            catch (Exception exception)
            {
                profile = null;
                error = "正式入口目录读取失败：" + exception.Message;
                return false;
            }
        }

        /// <summary>Same uniqueness contract for an explicitly loaded authoring set.</summary>
        public static bool TryFindForFirstFloor(string firstFloorContentId,
            IEnumerable<RunEntryLoadoutProfile> candidates,
            out RunEntryLoadoutProfile profile, out string error)
        {
            profile = null;
            error = null;
            if (string.IsNullOrWhiteSpace(firstFloorContentId) || candidates == null)
            {
                error = "正式入口查找缺少首层 ID 或配置集合。";
                return false;
            }
            RunEntryLoadoutProfile found = null;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RunEntryLoadoutProfile candidate in candidates)
            {
                if (candidate == null || candidate.ValidateProfile().Count > 0 ||
                    !ids.Add(candidate.contentId))
                {
                    error = "正式入口配置无效或 contentId 重复；不能退回旧入口。";
                    return false;
                }
                if (!string.Equals(candidate.firstFloorContentId, firstFloorContentId,
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null)
                {
                    error = "同一首层存在多个正式入口：" + firstFloorContentId;
                    return false;
                }
                found = candidate;
            }
            profile = found;
            return profile != null;
        }

        public static void ClearCache() => Cache.Clear();
    }
}
