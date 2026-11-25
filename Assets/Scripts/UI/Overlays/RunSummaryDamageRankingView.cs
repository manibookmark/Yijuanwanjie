using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.UI.Overlays
{
    /// <summary>
    /// 结算页伤害排行的滚动与行布局。调用方只提供已经排序的稳定卡牌名称、
    /// 实际伤害和五行条形色，本组件不读取日志、不聚合统计，也不拥有结果页
    /// 生命周期。入场动画（条形生长 + 数字 count-up）由
    /// RunSummaryRankingAnimator 驱动，与本组件的静态布局解耦。
    /// </summary>
    internal static class RunSummaryDamageRankingView
    {
        internal readonly struct Entry
        {
            public string Label { get; }
            public int Damage { get; }
            public int TimesPlayed { get; }
            public Color BarColor { get; }
            public string TooltipText { get; }
            public float AverageDamage => TimesPlayed > 0
                ? Damage / (float)TimesPlayed
                : 0f;

            public Entry(
                string label,
                int damage,
                int timesPlayed,
                Color barColor,
                string tooltipText = null)
            {
                Label = label ?? string.Empty;
                Damage = Math.Max(0, damage);
                TimesPlayed = Math.Max(0, timesPlayed);
                BarColor = barColor;
                TooltipText = tooltipText ?? string.Empty;
            }
        }

        private const float RowHeight = 48f;
        private const float ScrollLeft = 0.025f;
        private const float ScrollRight = 0.98f;
        private const float ViewportWidth = 0.955f;

        internal static void Build(
            RectTransform section,
            IReadOnlyList<Entry> entries,
            Action<GameObject, string> bindTooltip = null)
        {
            if (section == null)
                throw new ArgumentNullException(nameof(section));
            if (entries == null || entries.Count == 0)
            {
                Text empty = RewardPanelFactory.CreateText(
                    section,
                    "Empty",
                    "本局没有记录到卡牌伤害",
                    17,
                    TextAnchor.MiddleCenter,
                    RewardPanelFactory.SecondaryText);
                Place(
                    empty.rectTransform,
                    new Vector2(0.04f, 0.08f),
                    new Vector2(0.96f, 0.74f));
                return;
            }

            CreateColumnHeader(section, bindTooltip);

            RectTransform scrollRoot = CreateRect(
                section,
                "RankingScroll",
                new Vector2(ScrollLeft, 0.035f),
                new Vector2(ScrollRight, 0.88f));
            Image hitSurface = scrollRoot.gameObject.AddComponent<Image>();
            hitSurface.color = new Color(0f, 0f, 0f, 0.001f);

            RectTransform viewport = CreateRect(
                scrollRoot,
                "Viewport",
                Vector2.zero,
                new Vector2(ViewportWidth, 1f));
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateRect(
                viewport,
                "Content",
                new Vector2(0f, 1f),
                Vector2.one);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(
                0f,
                Math.Max(RowHeight, entries.Count * RowHeight));
            content.anchoredPosition = Vector2.zero;

            Scrollbar scrollbar = CreateScrollbar(scrollRoot);
            ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalNormalizedPosition = 1f;

            RunSummaryRankingAnimator animator =
                content.gameObject.AddComponent<RunSummaryRankingAnimator>();
            int maximum = Math.Max(1, entries[0].Damage);
            for (int i = 0; i < entries.Count; i++)
                CreateRow(content, entries[i], maximum, i, animator,
                    bindTooltip);
        }

        private static void CreateRow(
            RectTransform content,
            Entry entry,
            int maximum,
            int index,
            RunSummaryRankingAnimator animator,
            Action<GameObject, string> bindTooltip)
        {
            GameObject prefab = RunSummaryTilePrefabs.Load(
                RunSummaryTilePrefabs.RankRowPath, "伤害排行");
            if (prefab == null)
                return;
            GameObject rowObject = UnityEngine.Object.Instantiate(
                prefab, content);
            rowObject.name = $"Rank_{index + 1}";
            RectTransform row = rowObject.GetComponent<RectTransform>();
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = Vector2.one;
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, RowHeight);
            row.anchoredPosition = new Vector2(0f, -index * RowHeight);

            RunSummaryRankRowView view =
                rowObject.GetComponent<RunSummaryRankRowView>();
            if (view == null)
            {
                Debug.LogError(
                    "[RunSummary] 伤害排行行 Prefab 缺少 " +
                    "RunSummaryRankRowView 组件，请重新生成 tile Prefab。");
                return;
            }
            view.SetData(entry.Label, entry.BarColor);
            if (bindTooltip != null && !string.IsNullOrWhiteSpace(entry.TooltipText))
            {
                Image hitSurface = rowObject.GetComponent<Image>();
                if (hitSurface == null)
                {
                    hitSurface = rowObject.AddComponent<Image>();
                    hitSurface.color = Color.clear;
                }
                hitSurface.raycastTarget = true;
                bindTooltip(rowObject, entry.TooltipText);
            }

            animator.RegisterRow(
                view.FillRect,
                view.DamageText,
                view.AverageText,
                Mathf.Clamp01(entry.Damage / (float)maximum),
                entry.Damage,
                entry.AverageDamage,
                "{0}",
                "{0:0.#}");
        }

        private static void CreateColumnHeader(
            RectTransform section,
            Action<GameObject, string> bindTooltip)
        {
            // Match both nested horizontal coordinate spaces used by the rows.
            RectTransform headerRoot = CreateRect(
                section, "RankingHeader",
                new Vector2(ScrollLeft, 0.89f),
                new Vector2(ScrollRight, 1f));
            RectTransform columns = CreateRect(
                headerRoot, "Columns", Vector2.zero,
                new Vector2(ViewportWidth, 1f));
            Text total = RewardPanelFactory.CreateText(
                columns,
                "TotalDamageHeading",
                "总伤",
                16,
                TextAnchor.MiddleRight,
                RewardPanelFactory.SecondaryText);

            Text average = RewardPanelFactory.CreateText(
                columns,
                "AverageDamageHeading",
                "次均伤害",
                16,
                TextAnchor.MiddleRight,
                RewardPanelFactory.SecondaryText);
            RunSummaryRankRowView.ApplyNumericColumnLayout(total, average);

            if (bindTooltip != null)
            {
                total.raycastTarget = true;
                average.raycastTarget = true;
                bindTooltip(total.gameObject,
                    "总伤：该卡牌在本局造成的总伤害。\n条长：以伤害榜首为满条，表示相对于榜首的伤害。");
                bindTooltip(average.gameObject,
                    "次均伤害：平均每次出牌造成的伤害（总伤害 ÷ 使用次数）。");
            }
        }

        private static Scrollbar CreateScrollbar(RectTransform parent)
        {
            RectTransform root = CreateRect(
                parent,
                "Scrollbar",
                new Vector2(0.968f, 0f),
                Vector2.one);
            Image background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.18f, 0.18f, 0.16f, 0.32f);

            RectTransform handle = CreateRect(
                root,
                "Handle",
                new Vector2(0.16f, 0f),
                new Vector2(0.84f, 1f));
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.56f, 0.48f, 0.31f, 0.85f);

            Scrollbar scrollbar = root.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private static RectTransform CreateRect(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            Place(rect, anchorMin, anchorMax);
            return rect;
        }

        private static void Place(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
