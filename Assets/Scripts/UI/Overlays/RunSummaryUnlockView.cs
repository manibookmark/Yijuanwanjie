using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Content;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Hub;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.UI.Overlays
{
    /// <summary>
    /// 将本局新增解锁回执展示为两列可滚动文字，并按稳定 ID 解析说明。
    /// 只拥有内容区布局，不发放奖励；标题与 Tooltip 生命周期由结果页负责。
    /// </summary>
    internal static class RunSummaryUnlockView
    {
        private const float RowHeight = 30f;

        internal static void Build(
            RectTransform section,
            IReadOnlyList<DungeonRunRewardResult> rewards,
            Action<GameObject, string> bindTooltip)
        {
            if (section == null)
                throw new ArgumentNullException(nameof(section));

            var entries = new List<DungeonRunRewardResult>();
            if (rewards != null)
            {
                for (int i = 0; i < rewards.Count; i++)
                {
                    DungeonRunRewardResult reward = rewards[i];
                    if (reward.NewlyGranted &&
                        reward.Category != DungeonRunRewardCategory.Dungeon)
                        entries.Add(reward);
                }
            }

            if (entries.Count == 0)
            {
                Text empty = RewardPanelFactory.CreateText(section, "Empty",
                    "本局没有新增解锁", 17, TextAnchor.UpperLeft,
                    RewardPanelFactory.SecondaryText);
                Stretch(empty.rectTransform, new Vector2(0.035f, 0f),
                    new Vector2(0.965f, 1f));
                return;
            }

            RectTransform root = CreateRect(section, "UnlockScroll",
                new Vector2(0.035f, 0f), new Vector2(0.965f, 1f));
            root.gameObject.AddComponent<Image>().color =
                new Color(0f, 0f, 0f, 0.001f);
            RectTransform viewport = CreateRect(root, "Viewport",
                Vector2.zero, new Vector2(0.96f, 1f));
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = CreateRect(viewport, "Content",
                new Vector2(0f, 1f), Vector2.one);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, ((entries.Count + 1) / 2) * RowHeight);

            ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RowHeight;
            scroll.verticalScrollbar = CreateScrollbar(root);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalNormalizedPosition = 1f;

            TianluRegistryCatalog catalog = Resources.Load<TianluRegistryCatalog>(
                "Content/Hub/tianlu-registry-catalog");
            for (int i = 0; i < entries.Count; i++)
            {
                DungeonRunRewardResult reward = entries[i];
                string name = string.IsNullOrWhiteSpace(reward.DisplayName)
                    ? reward.TargetContentId : reward.DisplayName;
                string label = $"{CategoryLabel(reward.Category)} · {name}";
                Text text = RewardPanelFactory.CreateText(content,
                    $"Unlock_{i}", label, 17, TextAnchor.MiddleLeft,
                    RewardPanelFactory.SecondaryText);
                int column = i % 2;
                Stretch(text.rectTransform,
                    new Vector2(column * 0.5f, 1f),
                    new Vector2((column + 1) * 0.5f, 1f));
                text.rectTransform.offsetMin = new Vector2(0f, -(i / 2 + 1) * RowHeight);
                text.rectTransform.offsetMax = new Vector2(-12f, -(i / 2) * RowHeight);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 13;
                text.resizeTextMaxSize = 17;
                text.raycastTarget = true;
                bindTooltip?.Invoke(text.gameObject,
                    label + "\n" + ResolveDescription(catalog, reward));
            }
        }

        private static string ResolveDescription(
            TianluRegistryCatalog catalog, DungeonRunRewardResult reward)
        {
            if (reward.Category == DungeonRunRewardCategory.PowerUp)
                return "已开放，可在道藏中消耗灵玉修习。";
            if (catalog?.entries != null)
            {
                foreach (TianluRegistryCatalog.Entry entry in catalog.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.unlockedText))
                        continue;
                    bool matches = Matches(entry.contentId, reward.GrantContentId) ||
                        Matches(entry.contentId, reward.TargetContentId);
                    if (!matches && MatchesCategory(entry.kind, reward.Category))
                        matches = Matches(entry.checkContentId, reward.TargetContentId);
                    if (matches)
                        return entry.unlockedText;
                }
            }

            if (reward.Category == DungeonRunRewardCategory.Crawler)
            {
                try
                {
                    if (ContentCatalog.Current.TryGetCrawler(reward.TargetContentId,
                            out CrawlerData crawler) &&
                        !string.IsNullOrWhiteSpace(crawler.playstyleTag))
                        return crawler.playstyleTag;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[RunSummaryUnlock] 英灵说明解析失败：{reward.TargetContentId}，{exception.Message}");
                }
            }
            return "已开放，可在神域中查看。";
        }

        private static bool Matches(string left, string right) =>
            !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static bool MatchesCategory(TianluEntryKind kind, DungeonRunRewardCategory category) =>
            (kind == TianluEntryKind.Crawler && category == DungeonRunRewardCategory.Crawler) ||
            (kind == TianluEntryKind.Relic && category == DungeonRunRewardCategory.Relic) ||
            (kind == TianluEntryKind.Arcana && category == DungeonRunRewardCategory.Arcana) ||
            (kind == TianluEntryKind.Facility && category == DungeonRunRewardCategory.Facility);

        private static string CategoryLabel(DungeonRunRewardCategory category) => category switch
        {
            DungeonRunRewardCategory.Crawler => "英灵",
            DungeonRunRewardCategory.Facility => "设施",
            DungeonRunRewardCategory.Relic => "遗珍",
            DungeonRunRewardCategory.Arcana => "天机",
            DungeonRunRewardCategory.PowerUp => "经诀",
            _ => "解锁"
        };

        private static Scrollbar CreateScrollbar(RectTransform parent)
        {
            RectTransform root = CreateRect(parent, "Scrollbar",
                new Vector2(0.98f, 0f), Vector2.one);
            root.gameObject.AddComponent<Image>().color =
                new Color(0.18f, 0.18f, 0.16f, 0.32f);
            RectTransform handle = CreateRect(root, "Handle",
                new Vector2(0.16f, 0f), new Vector2(0.84f, 1f));
            Image image = handle.gameObject.AddComponent<Image>();
            image.color = new Color(0.56f, 0.48f, 0.31f, 0.85f);
            Scrollbar scrollbar = root.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = image;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private static RectTransform CreateRect(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            Stretch(rect, anchorMin, anchorMax);
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
