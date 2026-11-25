using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Meta;
using WanjieScroll.Progression;
using WanjieScroll.Dungeon;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Hub
{
    /// <summary>
    /// Hub 关区目录配置。Bootstrap 读取条目并按通关存档判定解锁；
    /// 新增关区只需扩展资产，不改面板交互代码。
    /// </summary>
    [CreateAssetMenu(
        fileName = "hub-world-catalog",
        menuName = "万界长卷/Hub 关区目录")]
    public sealed class HubWorldCatalog : ScriptableObject, IDungeonCompletionUnlockSource
    {
        /// <summary>
        /// 单个关区条目。firstFloorContentId 是该入口唯一的运行路由；
        /// unlockLevelContentId 只负责可用性，不参与路由推断。
        /// </summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("稳定目标 ID；只用于入口选择，不使用显示名关联")]
            public string destinationId;
            [Tooltip("该关区入口启动的首层 LevelRunProfile 稳定 contentId")]
            public string firstFloorContentId;
            [Tooltip("解锁所需通关关卡的稳定 contentId；为空表示默认解锁")]
            public string unlockLevelContentId;
            [Tooltip("正式新 Run 装载规则稳定 ID；为空保留现有入口流程")]
            public string runEntryContentId;
            [Tooltip("关区显示名；不参与稳定关联")]
            public string title;
            [Tooltip("关区描述，支持换行")]
            public string subtitle;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>Expose the same authored prerequisite used by the Hub, without creating a UI destination.</summary>
        public IReadOnlyList<DungeonCompletionUnlockDefinition> GetCompletionUnlocks()
        {
            var result = new List<DungeonCompletionUnlockDefinition>();
            foreach (Entry entry in entries ?? new List<Entry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.unlockLevelContentId)) continue;
                result.Add(new DungeonCompletionUnlockDefinition(entry.unlockLevelContentId,
                    entry.destinationId, DungeonRunRewardCategory.Dungeon,
                    entry.firstFloorContentId, entry.title));
            }
            return result.AsReadOnly();
        }

        /// <summary>
        /// 将入口 ID 解析成首层稳定 ID。重复条目和空路由都视为配置错误，
        /// Bootstrap 不再维护任何关区特判。
        /// </summary>
        public bool TryResolveFirstFloorContentId(
            string destinationId,
            out string firstFloorContentId,
            out string error)
        {
            firstFloorContentId = null;
            if (!TryResolveEntry(destinationId, out Entry entry, out error))
                return false;
            firstFloorContentId = entry.firstFloorContentId;
            return true;
        }

        /// <summary>Resolve one authored route, including its optional formal Run policy.</summary>
        public bool TryResolveEntry(string destinationId, out Entry entry, out string error)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(destinationId))
            {
                error = "关区 destinationId 不能为空。";
                return false;
            }

            Entry match = null;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry candidate = entries[i];
                    if (candidate == null || !string.Equals(
                            candidate.destinationId,
                            destinationId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (match != null)
                    {
                        error = $"关区 destinationId 重复：{destinationId}。";
                        return false;
                    }
                    match = candidate;
                }
            }

            if (match == null)
            {
                error = $"找不到关区入口：{destinationId}。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(match.firstFloorContentId))
            {
                error = $"关区 {destinationId} 缺少 firstFloorContentId。";
                return false;
            }

            if (!IsInBuildScope(match))
            {
                error = "此关区不在本次试玩范围内。";
                return false;
            }

            entry = match;
            error = null;
            return true;
        }

        /// <summary>校验目录自身的稳定 ID 与路由唯一性。</summary>
        public List<string> CollectValidationErrors()
        {
            var errors = new List<string>();
            if (entries == null || entries.Count == 0)
            {
                errors.Add("Hub 关区目录至少需要一个条目。");
                return errors;
            }

            var destinationIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null)
                {
                    errors.Add($"Hub 关区条目[{i}] 不能为空。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(entry.destinationId))
                {
                    errors.Add($"Hub 关区条目[{i}] 的 destinationId 不能为空。");
                }
                else if (!destinationIds.Add(entry.destinationId))
                {
                    errors.Add($"Hub 关区 destinationId 重复：{entry.destinationId}。");
                }
                if (string.IsNullOrWhiteSpace(entry.firstFloorContentId))
                {
                    errors.Add(
                        $"Hub 关区 {entry.destinationId} 缺少 firstFloorContentId。");
                }
                if (!string.IsNullOrWhiteSpace(entry.unlockLevelContentId) &&
                    string.Equals(
                        entry.unlockLevelContentId,
                        entry.firstFloorContentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Hub 关区 {entry.destinationId} 不能由自身首层解锁。");
                }
            }
            return errors;
        }

        /// <summary>按通关记录把条目解析为面板目标列表。</summary>
        public List<HubDestination> ResolveDestinations(
            MetaProgression metaProgression)
        {
            var destinations = new List<HubDestination>(
                entries != null ? entries.Count : 0);
            if (entries == null)
                return destinations;

            foreach (Entry entry in entries)
            {
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.destinationId) || !IsInBuildScope(entry))
                    continue;

                bool available = string.IsNullOrWhiteSpace(
                    entry.unlockLevelContentId) ||
                    metaProgression != null &&
                    metaProgression.IsLevelCleared(entry.unlockLevelContentId);

                destinations.Add(new HubDestination(
                    entry.destinationId,
                    entry.title,
                    entry.subtitle,
                    available));
            }
            return destinations;
        }

        private static bool IsInBuildScope(Entry entry)
        {
            if (!DemoReleasePolicy.IsDemoBuild) return true;
            LevelRunProfile floor = LevelRunProfileCatalog.Get(entry.firstFloorContentId);
            return DemoReleasePolicy.AllowsLevel(floor.contentId, floor.dungeonContentId);
        }
    }
}
