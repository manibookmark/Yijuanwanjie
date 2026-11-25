using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Meta;
using WanjieScroll.Progression;
using WanjieScroll.Dungeon;

namespace WanjieScroll.UI.Hub
{
    /// <summary>天箓名册条目类别；顺序即面板签顺序。</summary>
    public enum TianluEntryKind
    {
        Crawler = 0,
        Relic = 1,
        Arcana = 2,
        Level = 3,
        Facility = 4
    }

    /// <summary>条目解锁判定方式；判定只认存档信号，文案不参与逻辑。</summary>
    public enum TianluUnlockCheck
    {
        Always = 0,
        Crawler = 1,
        Relic = 2,
        Arcana = 3,
        Level = 4,
        FacilityShop = 5,
        FacilityArcanas = 6,
        FacilityBlacksmith = 7,
        FacilityWanbaoge = 8
    }

    /// <summary>面板消费的单条名册条目。</summary>
    public sealed class TianluRegistryItem
    {
        public TianluRegistryItem(
            string contentId,
            TianluEntryKind kind,
            string displayName,
            string unlockedText,
            string lockedText,
            bool unlocked)
        {
            ContentId = contentId;
            Kind = kind;
            DisplayName = displayName;
            UnlockedText = unlockedText;
            LockedText = lockedText;
            Unlocked = unlocked;
        }

        public string ContentId { get; }
        public TianluEntryKind Kind { get; }
        public string DisplayName { get; }
        public string UnlockedText { get; }
        public string LockedText { get; }
        public bool Unlocked { get; }
    }

    /// <summary>
    /// 天箓名册目录配置。静态条目元数据全部在资产内维护；
    /// 解锁状态由存档 MetaProgression 只读判定，两者不互写。
    /// </summary>
    [CreateAssetMenu(
        fileName = "tianlu-registry-catalog",
        menuName = "万界长卷/Hub 天箓名册目录")]
    public sealed class TianluRegistryCatalog : ScriptableObject, IDungeonCompletionUnlockSource,
        IProgressionRegistrySource
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("条目自身稳定 contentId（tianlu.entry.*.v1）")]
            public string contentId;
            [Tooltip("条目类别，决定所属签")]
            public TianluEntryKind kind;
            [Tooltip("关区显示名；不参与稳定关联")]
            public string displayName;
            [TextArea(1, 3), Tooltip("已收录时显示的一句描述")]
            public string unlockedText;
            [TextArea(1, 3), Tooltip("未收录时显示的解锁条件文案，仅作展示")]
            public string lockedText;
            [Tooltip("解锁判定方式")]
            public TianluUnlockCheck check;
            [Tooltip("判定键（存档中的 contentId）；Always 与 Facility* 不需要")]
            public string checkContentId;
            [Tooltip("首次通关时展示该解锁提示的关区首层 ID；只用于结算提示，不参与解锁判定")]
            public string completionLevelContentId;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>Expose authored completion conditions without involving panel state.</summary>
        public IReadOnlyList<DungeonCompletionUnlockDefinition> GetCompletionUnlocks()
        {
            var result = new List<DungeonCompletionUnlockDefinition>();
            foreach (Entry entry in entries ?? new List<Entry>())
            {
                if (entry == null || entry.kind == TianluEntryKind.Level ||
                    string.IsNullOrWhiteSpace(entry.completionLevelContentId))
                    continue;
                DungeonRunRewardCategory category = entry.kind switch
                {
                    TianluEntryKind.Crawler => DungeonRunRewardCategory.Crawler,
                    TianluEntryKind.Relic => DungeonRunRewardCategory.Relic,
                    TianluEntryKind.Arcana => DungeonRunRewardCategory.Arcana,
                    TianluEntryKind.Facility => DungeonRunRewardCategory.Facility,
                    _ => throw new InvalidOperationException("Unknown completion entry kind: " + entry.kind)
                };
                result.Add(new DungeonCompletionUnlockDefinition(entry.completionLevelContentId,
                    entry.contentId, category, entry.contentId, entry.displayName));
            }
            return result.AsReadOnly();
        }

        /// <summary>
        /// 解析为面板条目列表；跳过缺少 contentId 的条目，其余保持配置顺序。
        /// </summary>
        public List<TianluRegistryItem> Resolve(MetaProgression metaProgression)
        {
            var items = new List<TianluRegistryItem>(
                entries != null ? entries.Count : 0);
            if (entries == null)
                return items;

            foreach (Entry entry in entries)
            {
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.contentId))
                    continue;

                items.Add(new TianluRegistryItem(
                    entry.contentId,
                    entry.kind,
                    entry.displayName,
                    entry.unlockedText,
                    entry.lockedText,
                    EvaluateUnlock(entry, metaProgression)));
            }
            return items;
        }

        /// <summary>Use the same authored entries and unlock checks in non-UI account observations.</summary>
        public IReadOnlyList<ProgressionRegistryItemSnapshot> ReadProgression(MetaProgression account)
        {
            var result = new List<ProgressionRegistryItemSnapshot>();
            foreach (TianluRegistryItem item in Resolve(account))
                result.Add(new ProgressionRegistryItemSnapshot {
                    contentId = item.ContentId, kind = item.Kind.ToString(),
                    displayName = item.DisplayName, unlocked = item.Unlocked
                });
            return result.AsReadOnly();
        }

        /// <summary>
        /// 显示顺序 = 配置顺序（即预计解锁顺序）。暂不按类别分组或收录
        /// 状态重排；后续细化预计解锁顺序时只改这里与对应测试。
        /// </summary>
        public static List<TianluRegistryItem> SortForDisplay(
            IEnumerable<TianluRegistryItem> items)
        {
            // List 构造函数完成防御性拷贝，不依赖 LINQ。
            return new List<TianluRegistryItem>(items);
        }

        private static bool EvaluateUnlock(
            Entry entry,
            MetaProgression metaProgression)
        {
            switch (entry.check)
            {
                case TianluUnlockCheck.Always:
                    return true;
                case TianluUnlockCheck.Crawler:
                    return metaProgression != null &&
                           metaProgression.IsCrawlerUnlocked(entry.checkContentId);
                case TianluUnlockCheck.Relic:
                    return metaProgression != null &&
                           metaProgression.IsRelicUnlocked(entry.checkContentId);
                case TianluUnlockCheck.Arcana:
                    return metaProgression != null &&
                           metaProgression.IsArcanaUnlocked(entry.checkContentId);
                case TianluUnlockCheck.Level:
                    return metaProgression != null &&
                           metaProgression.IsLevelCleared(entry.checkContentId);
                case TianluUnlockCheck.FacilityShop:
                    return metaProgression != null &&
                           metaProgression.IsDaozangUnlocked;
                case TianluUnlockCheck.FacilityArcanas:
                    return metaProgression != null &&
                           metaProgression.IsTianjiBuildingUnlocked;
                case TianluUnlockCheck.FacilityBlacksmith:
                    return metaProgression != null &&
                           metaProgression.blacksmithUnlocked;
                case TianluUnlockCheck.FacilityWanbaoge:
                    return metaProgression != null &&
                           metaProgression.IsWanbaogeUnlocked;
                default:
                    return false;
            }
        }
    }
}
