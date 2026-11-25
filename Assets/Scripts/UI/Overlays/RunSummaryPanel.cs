using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Cards;
using WanjieScroll.UI.Presentations;
using WanjieScroll.UI.Rewards;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Overlays
{
    /// <summary>
    /// 整次 Dungeon Run 的共用胜败 Results 视图。它只消费不可变快照，
    /// 左侧回答“本局用了什么”，右侧回答“本局取得了什么结果”。
    /// 首通解锁以左下方文字清单展示，详情与滚动由 RunSummaryUnlockView 封装。
    /// 模块审查：本类仍高于 UI 文件关注阈值，但职责保持为
    /// 一次性结果树的组装与 Hover 绑定；可滚动伤害排行的滚动状态、裁剪和
    /// 行布局已下沉到 RunSummaryDamageRankingView，本类不持有第二套滚动生命周期。
    /// 本轮保留组装边界：用牌分页状态仅属于本次结果树，操作锁和失败提示由本视图
    /// 唯一持有；不新增业务状态。若分页需跨次恢复或加入筛选，应重新审查并提取。
    /// </summary>
    public sealed class RunSummaryPanel : MonoBehaviour
    {
        private static readonly Color DefeatTitleColor =
            new Color(0.70f, 0.71f, 0.74f, 1f);
        private static readonly Color DefeatAccent =
            new Color(0.55f, 0.57f, 0.60f, 1f);

        private readonly Dictionary<string, CardConfig> _cardsByContentId =
            new Dictionary<string, CardConfig>(StringComparer.OrdinalIgnoreCase);

        private Canvas _hostCanvas;
        private GameObject _root;
        private Button _primaryButton;
        private Button _secondaryButton;
        private Text _actionError;
        private CardPresentationProfile _cardPresentation;
        private CardTooltipView _tooltip;
        private bool _actionDispatched;

        public event Action<DungeonRunResultAction> ActionRequested;

        public bool IsOpen => _root != null;

        public static RunSummaryPanel CreateHosted(
            Transform parent,
            IReadOnlyList<CardConfig> cardPool)
        {
            var canvasObject = new GameObject(
                "DungeonRunResultCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            // ScreenSpaceCamera 让 URP 的 FXAA/MSAA 对结算页生效；
            // 找不到主相机时退回 Overlay（抗锯齿不生效但功能完整）。
            Camera worldCamera = Camera.main;
            if (worldCamera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = worldCamera;
                canvas.planeDistance = 100f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32700;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Keep the fixed result paper inside the viewport on wide and tall screens.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            GraphicRaycaster raycaster =
                canvasObject.GetComponent<GraphicRaycaster>();
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            raycaster.ignoreReversedGraphics = false;

            RunSummaryPanel panel =
                canvasObject.AddComponent<RunSummaryPanel>();
            panel.SetHostCanvas(canvas);
            panel.SetCardCatalog(cardPool);
            return panel;
        }

        public void SetHostCanvas(Canvas canvas)
        {
            _hostCanvas = canvas != null
                ? canvas
                : throw new ArgumentNullException(nameof(canvas));
        }

        public void SetCardCatalog(IReadOnlyList<CardConfig> cardPool)
        {
            _cardsByContentId.Clear();
            if (cardPool != null)
            {
                for (int i = 0; i < cardPool.Count; i++)
                {
                    CardConfig card = cardPool[i];
                    if (card == null ||
                        string.IsNullOrWhiteSpace(card.contentId) ||
                        _cardsByContentId.ContainsKey(card.contentId))
                    {
                        continue;
                    }
                    _cardsByContentId.Add(card.contentId, card);
                }
            }
            _cardPresentation = CardPresentationProfile.LoadDefault();
        }

        /// <summary>显示胜败共用结果页；重复显示会替换旧视图。</summary>
        public void Show(DungeonRunResultSnapshot snapshot, bool canContinueDungeon = false,
            string navigationHint = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (_hostCanvas == null)
                throw new InvalidOperationException(
                    "RunSummaryPanel 尚未设置宿主 Canvas。");

            Close();
            EnsureTooltip();
            _actionDispatched = false;
            _root = RewardPanelFactory.CreateRoot(
                _hostCanvas,
                "DungeonRunResults",
                addDismissButton: false,
                out CanvasGroup rootGroup,
                out _);
            RectTransform panel = RewardPanelFactory.CreatePanel(
                _root.transform,
                "ResultsPaper",
                new Vector2(1760f, 1000f),
                out _);

            bool victory = snapshot.Status == DungeonRunResultStatus.Victory;
            bool demoComplete = victory && DemoReleasePolicy.IsDemoFinale(snapshot.DungeonContentId);
            if (demoComplete)
            {
                canContinueDungeon = false;
                navigationHint = "感谢游玩！前三关试玩已完成，可返回仙域再次探索。";
            }
            Color accent = AccentGold(victory);
            CreateSummaryHeader(
                panel,
                demoComplete ? "试玩结束" : victory ? "战斗胜利" : "战斗失败",
                victory);

            RectTransform left = CreateSurface(
                panel,
                "VisualSummary",
                new Vector2(0.035f, 0.14f),
                new Vector2(0.445f, 0.90f));
            RectTransform right = CreateSurface(
                panel,
                "StatisticsSummary",
                new Vector2(0.485f, 0.14f),
                new Vector2(0.965f, 0.90f));

            CreateVerticalDivider(panel);

            BuildCrawlerSection(left, snapshot);
            BuildUsedCardSection(left, snapshot, accent);
            BuildFinalGainSection(left, snapshot, accent);
            RectTransform unlocks = CreateSection(left, "UnlockSection", "本局解锁",
                new Vector2(0.035f, 0f), new Vector2(0.965f, 0.175f), accent);
            RunSummaryUnlockView.Build(unlocks, snapshot.Rewards, BindTooltip);

            if (victory)
                BuildJadeHighlight(right, snapshot);
            BuildMetricGrid(right, snapshot, accent);
            BuildDamageChart(right, snapshot, accent);

            if (victory && canContinueDungeon)
            {
                _primaryButton = CreateActionButton(panel, "ContinueDungeonButton", "进入下一关",
                    RewardPanelFactory.PrimaryAction, DungeonRunResultAction.ContinueDungeon,
                    new Vector2(0.27f, 0.04f), new Vector2(0.47f, 0.117f));
                _secondaryButton = CreateActionButton(panel, "ReturnHubButton", "返回神域",
                    RewardPanelFactory.NeutralAction, DungeonRunResultAction.ReturnToHub,
                    new Vector2(0.53f, 0.04f), new Vector2(0.73f, 0.117f));
            }
            else if (victory)
            {
                _primaryButton = CreateActionButton(
                    panel,
                    "ReturnHubButton",
                    demoComplete ? "返回仙域" : "返回神域",
                    RewardPanelFactory.PrimaryAction,
                    DungeonRunResultAction.ReturnToHub,
                    new Vector2(0.41f, 0.04f),
                    new Vector2(0.59f, 0.117f));
            }
            else
            {
                _primaryButton = CreateActionButton(
                    panel,
                    "RetryButton",
                    "重新挑战",
                    RewardPanelFactory.PrimaryAction,
                    DungeonRunResultAction.RetryDungeon,
                    new Vector2(0.27f, 0.04f),
                    new Vector2(0.47f, 0.117f));
                _secondaryButton = CreateActionButton(
                    panel,
                    "ReturnHubButton",
                    "返回神域",
                    RewardPanelFactory.NeutralAction,
                    DungeonRunResultAction.ReturnToHub,
                    new Vector2(0.53f, 0.04f),
                    new Vector2(0.73f, 0.117f));
            }

            _actionError = RewardPanelFactory.CreateText(
                panel, "ActionError", navigationHint ?? string.Empty, 16,
                TextAnchor.MiddleCenter, RewardPanelFactory.PrimaryText);
            Place(_actionError.rectTransform,
                new Vector2(0.05f, 0.005f), new Vector2(0.95f, 0.037f));
            _actionError.raycastTarget = false;
            StartCoroutine(RewardPanelFactory.AnimateIn(rootGroup, panel));
        }

        /// <summary>操作失败后恢复本页输入；详细错误由流程层记录。</summary>
        public void RecoverFromActionFailure(string playerMessage = null)
        {
            if (!IsOpen)
                return;
            _actionDispatched = false;
            SetActionsInteractable(true);
            if (_actionError != null)
                _actionError.text = string.IsNullOrWhiteSpace(playerMessage)
                    ? "操作未完成，请重试或返回神域。"
                    : playerMessage;
        }

        public void SetActionsInteractable(bool interactable)
        {
            if (_primaryButton != null)
                _primaryButton.interactable = interactable;
            if (_secondaryButton != null)
                _secondaryButton.interactable = interactable;
        }

        public void Close()
        {
            StopAllCoroutines();
            _tooltip?.Hide();
            if (_root != null)
                Destroy(_root);
            _root = null;
            _primaryButton = null;
            _secondaryButton = null;
            _actionError = null;
            _actionDispatched = false;
        }

        private static Color AccentGold(bool victory)
            => victory ? RewardPanelFactory.WarmGold : DefeatAccent;

        private void BuildCrawlerSection(
            Transform parent,
            DungeonRunResultSnapshot snapshot)
        {
            RectTransform section = CreateSection(
                parent,
                "CrawlerSection",
                string.Empty,
                new Vector2(0.035f, 0.685f),
                new Vector2(0.965f, 0.985f),
                RewardPanelFactory.WarmGold);
            int count = Math.Min(3, snapshot.CrawlerContentIds.Count);
            if (count == 0)
            {
                CreateEmptyState(section, "本局没有记录到出战英灵");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                string crawlerId = snapshot.CrawlerContentIds[i];
                ResolveCrawlerPresentation(
                    crawlerId,
                    out string displayName,
                    out string description,
                    out Sprite artwork);
                float width = count switch
                {
                    1 => 0.50f,
                    2 => 0.37f,
                    _ => 0.285f
                };
                float gap = 0.035f;
                float total = count * width + (count - 1) * gap;
                float x = (1f - total) * 0.5f + i * (width + gap);
                RectTransform tile;
                if (CharacterPresentationCatalog.TryGetHeroArrivalForSubject(
                        crawlerId,
                        out CharacterPresentationProfile profile))
                {
                    tile = CreateAnimatedCrawlerTile(
                        section,
                        "Crawler_" + crawlerId,
                        profile,
                        i * 0.10f,
                        new Vector2(x, 0.03f),
                        new Vector2(x + width, 0.97f));
                }
                else
                {
                    tile = CreateArtworkTile(
                        section,
                        "Crawler_" + crawlerId,
                        displayName,
                        artwork,
                        new Vector2(x, 0.03f),
                        new Vector2(x + width, 0.97f));
                }
                BindTooltip(
                    tile.gameObject,
                    displayName + "\n" + description);
            }
        }

        private void BuildUsedCardSection(
            Transform parent,
            DungeonRunResultSnapshot snapshot,
            Color accent)
        {
            RectTransform section = CreateSection(
                parent,
                "UsedCardSection",
                "使用卡牌",
                new Vector2(0.035f, 0.355f),
                new Vector2(0.965f, 0.665f),
                accent);
            var cards = new List<DungeonRunCardResult>();
            for (int i = 0; i < snapshot.Cards.Count; i++)
            {
                if (snapshot.Cards[i].TimesPlayed > 0)
                    cards.Add(snapshot.Cards[i]);
            }
            cards.Sort((left, right) =>
            {
                int played = right.TimesPlayed.CompareTo(left.TimesPlayed);
                return played != 0
                    ? played
                    : right.DamageDealt.CompareTo(left.DamageDealt);
            });
            if (cards.Count == 0)
            {
                CreateEmptyState(section, "本局没有打出卡牌");
                return;
            }

            const int pageSize = 12;
            int page = 0;
            int pageCount = (cards.Count + pageSize - 1) / pageSize;
            RectTransform list = CreateSurface(section, "CardPage",
                new Vector2(0f, 0.16f), Vector2.one);
            Text pageLabel = RewardPanelFactory.CreateText(section, "CardCount",
                string.Empty, 14, TextAnchor.MiddleCenter, RewardPanelFactory.SecondaryText);
            Place(pageLabel.rectTransform, new Vector2(0.25f, 0f), new Vector2(0.75f, 0.15f));
            Button previous = null;
            Button next = null;
            if (pageCount > 1)
            {
                previous = RewardPanelFactory.CreateButton(section, "PreviousCards",
                    "上一页", RewardPanelFactory.NeutralAction);
                next = RewardPanelFactory.CreateButton(section, "NextCards",
                    "下一页", RewardPanelFactory.NeutralAction);
                foreach (Button pager in new[] { previous, next })
                {
                    Text label = pager.GetComponentInChildren<Text>();
                    label.fontSize = 14;
                    label.rectTransform.offsetMin = new Vector2(4f, 1f);
                    label.rectTransform.offsetMax = new Vector2(-4f, -1f);
                }
                Place(previous.GetComponent<RectTransform>(),
                    new Vector2(0.03f, 0f), new Vector2(0.23f, 0.15f));
                Place(next.GetComponent<RectTransform>(),
                    new Vector2(0.77f, 0f), new Vector2(0.97f, 0.15f));
                previous.onClick.AddListener(() => { page--; RenderPage(); });
                next.onClick.AddListener(() => { page++; RenderPage(); });
            }
            RenderPage();

            void RenderPage()
            {
                _tooltip?.Hide();
                for (int i = list.childCount - 1; i >= 0; i--)
                {
                    GameObject oldTile = list.GetChild(i).gameObject;
                    oldTile.SetActive(false);
                    Destroy(oldTile);
                }
                page = Mathf.Clamp(page, 0, pageCount - 1);
                pageLabel.text = pageCount > 1
                    ? $"共 {cards.Count} 种 · {page + 1}/{pageCount} 页"
                    : $"共 {cards.Count} 种";
                if (previous != null) previous.interactable = page > 0;
                if (next != null) next.interactable = page < pageCount - 1;
                int shown = Math.Min(pageSize, cards.Count - page * pageSize);
                int columns = Math.Min(2, shown);
                int rows = (shown + columns - 1) / columns;
                for (int i = 0; i < shown; i++)
                {
                    DungeonRunCardResult result = cards[page * pageSize + i];
                    string displayName = ResolveCardName(result.ContentId);
                    float cellWidth = 0.94f / columns;
                    float cellHeight = 0.90f / rows;
                    float x = 0.03f + i % columns * cellWidth;
                    float yMax = 0.98f - i / columns * cellHeight;
                    RectTransform tile = CreatePlainTextItem(list,
                        "Card_" + result.ContentId, displayName + " ×" + result.TimesPlayed,
                        new Vector2(x + 0.012f, yMax - cellHeight + 0.02f),
                        new Vector2(x + cellWidth - 0.012f, yMax));
                    BindTooltip(tile.gameObject,
                        BuildCardTooltip(ResolveCard(result.ContentId)));
                }
            }
        }

        private void BuildFinalGainSection(
            Transform parent,
            DungeonRunResultSnapshot snapshot,
            Color accent)
        {
            RectTransform section = CreateSection(
                parent,
                "FinalGainSection",
                "本局增益",
                new Vector2(0.035f, 0.195f),
                new Vector2(0.965f, 0.335f),
                accent);
            List<FinalGain> gains = BuildFinalGains(snapshot);
            if (gains.Count == 0)
            {
                CreateEmptyState(section, "本局没有额外状态增益");
                return;
            }

            int columns = Math.Min(3, gains.Count);
            int rows = (gains.Count + columns - 1) / columns;
            for (int i = 0; i < gains.Count; i++)
            {
                FinalGain gain = gains[i];
                float cellWidth = 0.94f / columns;
                float cellHeight = 0.88f / rows;
                int column = i % columns;
                int row = i / columns;
                float x = 0.03f + column * cellWidth;
                float yMax = 0.98f - row * cellHeight;
                RectTransform tile = CreateGainTile(
                    section,
                    "Gain_" + gain.IconId,
                    gain.Label + " " + gain.FormattedValue,
                    new Vector2(x + 0.01f, yMax - cellHeight + 0.02f),
                    new Vector2(x + cellWidth - 0.01f, yMax));
                if (tile != null)
                {
                    BindTooltip(
                        tile.gameObject,
                        gain.Label + "\n" + gain.Description);
                }
            }
        }

        /// <summary>通关灵玉是本局结果的核心数字，与数据格子分开放大展示。</summary>
        private static void BuildJadeHighlight(
            Transform parent,
            DungeonRunResultSnapshot snapshot)
        {
            var container = new GameObject(
                "DungeonJadeHighlight",
                typeof(RectTransform));
            container.transform.SetParent(parent, false);
            RectTransform rect = container.GetComponent<RectTransform>();
            Place(
                rect,
                new Vector2(0.62f, 0.88f),
                new Vector2(0.955f, 0.985f));

            Text value = RewardPanelFactory.CreateText(
                rect,
                "Value",
                snapshot.LevelBonusJade > 0
                    ? $"整关灵玉 +{snapshot.DungeonBonusJade}\n终层灵玉 +{snapshot.LevelBonusJade}"
                    : $"通关灵玉 +{snapshot.DungeonBonusJade}",
                34,
                TextAnchor.MiddleRight,
                RewardPanelFactory.WarmGold);
            value.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform valueRect = value.rectTransform;
            valueRect.anchorMin = new Vector2(0f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = new Vector2(-54f, 0f);

            var iconGo = new GameObject(
                "JadeIcon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            iconGo.transform.SetParent(rect, false);
            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(1f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(40f, 40f);
            iconRect.anchoredPosition = new Vector2(-24f, 0f);
            Image icon = iconGo.GetComponent<Image>();
            icon.sprite = RuntimeArtLoader.LoadSprite(
                "Sprites/UI/Daozang/utility-icons/currency-lingyu");
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            if (icon.sprite == null)
                icon.color = Color.clear;
        }

        private static void BuildMetricGrid(
            Transform parent,
            DungeonRunResultSnapshot snapshot,
            Color accent)
        {
            var metrics = new List<ResultMetric>
            {
                new ResultMetric("游戏时间", FormatElapsed(snapshot.ElapsedRunSeconds)),
                new ResultMetric("完成层数", snapshot.FloorsCleared.ToString()),
                new ResultMetric("达到等级", snapshot.LevelReached.ToString()),
                new ResultMetric("局内收集", snapshot.CollectedJade.ToString())
            };
            metrics.Add(new ResultMetric(
                "击败敌人",
                snapshot.EnemiesDefeated.ToString()));
            metrics.Add(new ResultMetric(
                "最高叠势",
                snapshot.MaxCombo.ToString()));
            metrics.Add(new ResultMetric(
                "治疗总量",
                snapshot.TotalHealing.ToString()));
            metrics.Add(new ResultMetric(
                "免耗出牌",
                snapshot.FreeCardsPlayed.ToString()));

            RectTransform section = CreateSection(
                parent,
                "MetricSection",
                "本局数据",
                new Vector2(0.035f, 0.60f),
                new Vector2(0.965f, 0.86f),
                accent);
            int rows = (metrics.Count + 1) / 2;
            for (int i = 0; i < metrics.Count; i++)
            {
                int column = i % 2;
                int row = i / 2;
                float xMin = column == 0 ? 0.035f : 0.515f;
                float xMax = column == 0 ? 0.485f : 0.965f;
                float rowHeight = 0.88f / rows;
                float yMax = 0.98f - row * rowHeight;
                CreateMetricTile(
                    section,
                    "Metric_" + i,
                    metrics[i],
                    accent,
                    new Vector2(xMin, yMax - rowHeight + 0.025f),
                    new Vector2(xMax, yMax));
            }
        }

        private void BuildDamageChart(
            Transform parent,
            DungeonRunResultSnapshot snapshot,
            Color accent)
        {
            RectTransform section = CreateSection(
                parent,
                "DamageChart",
                "伤害排行",
                new Vector2(0.035f, 0.04f),
                new Vector2(0.965f, 0.56f),
                accent);
            var cards = new List<DungeonRunCardResult>();
            for (int i = 0; i < snapshot.Cards.Count; i++)
            {
                DungeonRunCardResult card = snapshot.Cards[i];
                if (card.DamageDealt > 0)
                {
                    cards.Add(card);
                }
            }
            cards.Sort((left, right) =>
                right.DamageDealt.CompareTo(left.DamageDealt));
            var entries = new List<RunSummaryDamageRankingView.Entry>(
                cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                CardConfig card = ResolveCard(cards[i].ContentId);
                Color barColor = card != null
                    ? CardColorPalette.GetMainColor(card.color)
                    : new Color(0.22f, 0.68f, 0.57f, 1f);
                entries.Add(new RunSummaryDamageRankingView.Entry(
                    ResolveCardName(cards[i].ContentId),
                    cards[i].DamageDealt,
                    cards[i].TimesPlayed,
                    barColor,
                    BuildCardTooltip(card)));
            }
            RunSummaryDamageRankingView.Build(section, entries, BindTooltip);
        }

        private static RectTransform CreateSurface(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            Place(rect, anchorMin, anchorMax);
            Image image = go.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            return rect;
        }

        private static void CreateSummaryHeader(
            Transform parent,
            string title,
            bool victory)
        {
            Text text = RewardPanelFactory.CreateText(
                parent,
                "Title",
                title,
                42,
                TextAnchor.MiddleCenter,
                victory
                    ? RewardPanelFactory.PrimaryText
                    : DefeatTitleColor);
            Place(
                text.rectTransform,
                new Vector2(0.05f, 0.895f),
                new Vector2(0.95f, 0.985f));
        }

        private static void CreateVerticalDivider(Transform parent)
        {
            var go = new GameObject(
                "ColumnDivider",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            Place(
                rect,
                new Vector2(0.463f, 0.145f),
                new Vector2(0.464f, 0.875f));
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.58f, 0.48f, 0.29f, 0.16f);
            image.raycastTarget = false;
        }

        private static RectTransform CreateSection(
            Transform parent,
            string name,
            string heading,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color accent)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            Place(rect, anchorMin, anchorMax);
            Image image = go.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            if (string.IsNullOrWhiteSpace(heading))
                return rect;

            Text title = RewardPanelFactory.CreateText(
                rect,
                "Heading",
                heading,
                21,
                TextAnchor.MiddleLeft,
                accent);
            AnchorTopBand(
                title.rectTransform,
                leftInset: 24f,
                rightInset: 24f,
                topInset: 0f,
                height: 32f);

            var divider = new GameObject(
                "HeadingDivider",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            divider.transform.SetParent(rect, false);
            RectTransform dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = dividerRect.anchorMax =
                new Vector2(0f, 1f);
            dividerRect.pivot = new Vector2(0f, 1f);
            dividerRect.anchoredPosition = new Vector2(24f, -40f);
            dividerRect.sizeDelta = new Vector2(165f, 2f);
            Image dividerImage = divider.GetComponent<Image>();
            dividerImage.color = new Color(
                accent.r, accent.g, accent.b, 0.60f);
            dividerImage.raycastTarget = false;

            var contentObject = new GameObject(
                "Content",
                typeof(RectTransform));
            contentObject.transform.SetParent(rect, false);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = new Vector2(0f, -48f);
            return content;
        }

        private static void AnchorTopBand(
            RectTransform rect,
            float leftInset,
            float rightInset,
            float topInset,
            float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(
                (leftInset - rightInset) * 0.5f,
                -topInset);
            rect.sizeDelta = new Vector2(
                -(leftInset + rightInset),
                height);
        }

        private static RectTransform CreatePlainTextItem(
            Transform parent,
            string name,
            string text,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            RectTransform item = CreateTile(parent, name, anchorMin, anchorMax);
            Text label = RewardPanelFactory.CreateText(
                item,
                "Text",
                text,
                16,
                TextAnchor.MiddleLeft,
                RewardPanelFactory.PrimaryText);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 16;
            Place(
                label.rectTransform,
                new Vector2(0.035f, 0.04f),
                new Vector2(0.965f, 0.96f));
            return item;
        }

        private static RectTransform CreateArtworkTile(
            Transform parent,
            string name,
            string label,
            Sprite artwork,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            RectTransform tile = CreateTile(parent, name, anchorMin, anchorMax);
            var artGo = new GameObject(
                "Artwork",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            artGo.transform.SetParent(tile, false);
            Image image = artGo.GetComponent<Image>();
            image.sprite = artwork;
            image.preserveAspect = true;
            image.color = artwork != null
                ? Color.white
                : new Color(0.22f, 0.28f, 0.26f, 1f);
            image.raycastTarget = false;
            Place(
                image.rectTransform,
                new Vector2(0.05f, 0.27f),
                new Vector2(0.95f, 0.95f));
            if (artwork == null)
            {
                Text fallback = RewardPanelFactory.CreateText(
                    artGo.transform,
                    "FallbackGlyph",
                    string.IsNullOrWhiteSpace(label)
                        ? "卷"
                        : label.Substring(0, 1),
                    28,
                    TextAnchor.MiddleCenter,
                    RewardPanelFactory.WarmGold);
                Place(fallback.rectTransform, Vector2.zero, Vector2.one);
            }
            Text text = RewardPanelFactory.CreateText(
                tile,
                "Label",
                label,
                16,
                TextAnchor.MiddleCenter,
                RewardPanelFactory.PrimaryText);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = 16;
            Place(
                text.rectTransform,
                new Vector2(0.04f, 0.03f),
                new Vector2(0.96f, 0.25f));
            return tile;
        }

        private static RectTransform CreateAnimatedCrawlerTile(
            Transform parent,
            string name,
            CharacterPresentationProfile profile,
            float delaySeconds,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            RectTransform tile = CreateTile(parent, name, anchorMin, anchorMax);
            RunSummaryCrawlerAnimator.TryAttach(tile, profile, delaySeconds);
            return tile;
        }

        private static RectTransform CreateTile(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            Place(rect, anchorMin, anchorMax);
            Image image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.001f);
            image.raycastTarget = true;
            return rect;
        }

        private static void CreateMetricTile(
            Transform parent,
            string name,
            ResultMetric metric,
            Color accent,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject prefab = RunSummaryTilePrefabs.Load(
                RunSummaryTilePrefabs.MetricTilePath, "本局数据");
            if (prefab == null)
                return;
            GameObject tile = Instantiate(prefab, parent);
            tile.name = name;
            RectTransform rect = tile.GetComponent<RectTransform>();
            Place(rect, anchorMin, anchorMax);
            tile.GetComponent<RunSummaryMetricTileView>()
                ?.SetData(metric.Label, metric.Value, accent);
        }

        private static RectTransform CreateGainTile(
            Transform parent,
            string name,
            string text,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject prefab = RunSummaryTilePrefabs.Load(
                RunSummaryTilePrefabs.GainTilePath, "本局增益");
            if (prefab == null)
                return null;
            GameObject tile = Instantiate(prefab, parent);
            tile.name = name;
            RectTransform rect = tile.GetComponent<RectTransform>();
            Place(rect, anchorMin, anchorMax);
            tile.GetComponent<RunSummaryGainTileView>()?.SetData(text);
            return rect;
        }

        private static void CreateEmptyState(Transform parent, string text)
        {
            Text empty = RewardPanelFactory.CreateText(
                parent,
                "Empty",
                text,
                15,
                TextAnchor.MiddleLeft,
                RewardPanelFactory.SecondaryText);
            Place(
                empty.rectTransform,
                new Vector2(0.035f, 0.08f),
                new Vector2(0.965f, 0.92f));
        }

        private void EnsureTooltip()
        {
            if (_tooltip != null)
                return;
            var tooltipGo = new GameObject(
                "RunSummaryTooltip",
                typeof(RectTransform));
            tooltipGo.transform.SetParent(_hostCanvas.transform, false);
            _tooltip = tooltipGo.AddComponent<CardTooltipView>();
        }

        private void BindTooltip(GameObject target, string text)
        {
            if (target == null || string.IsNullOrWhiteSpace(text))
                return;
            RunSummaryHoverTarget hover =
                target.AddComponent<RunSummaryHoverTarget>();
            hover.Bind(
                () => _tooltip?.ShowText(text),
                () => _tooltip?.Hide());
        }

        private CardConfig ResolveCard(string contentId)
        {
            return !string.IsNullOrWhiteSpace(contentId) &&
                   _cardsByContentId.TryGetValue(
                       contentId,
                       out CardConfig card)
                ? card
                : null;
        }

        private string ResolveCardName(string contentId)
        {
            CardConfig card = ResolveCard(contentId);
            return card != null && !string.IsNullOrWhiteSpace(card.cardName)
                ? card.cardName
                : contentId;
        }

        private Sprite ResolveCardArtwork(CardConfig card, string contentId)
        {
            if (card?.iconArt != null)
                return card.iconArt;
            if (card?.cardArt != null)
                return card.cardArt;
            return _cardPresentation?.GetArtwork(contentId);
        }

        private void ResolveCrawlerPresentation(
            string crawlerId,
            out string displayName,
            out string description,
            out Sprite artwork)
        {
            displayName = crawlerId;
            description = "本局出战英灵。";
            artwork = null;
            try
            {
                if (!ContentCatalog.Current.TryGetCrawler(
                        crawlerId,
                        out CrawlerData crawler))
                {
                    return;
                }
                if (!string.IsNullOrWhiteSpace(crawler.name))
                    displayName = crawler.name;
                if (!string.IsNullOrWhiteSpace(crawler.playstyleTag))
                    description = crawler.playstyleTag;
                CardConfig crawlerCard = ResolveCard(crawler.cardContentId);
                artwork = ResolveCardArtwork(
                    crawlerCard,
                    crawler.cardContentId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[RunSummaryPanel] 英灵介绍解析失败：" +
                    $"{crawlerId}，{exception.Message}");
            }
        }

        private static string BuildCardTooltip(CardConfig card)
        {
            return CardTextRenderer.GetTooltipText(card);
        }

        private static List<FinalGain> BuildFinalGains(
            DungeonRunResultSnapshot snapshot)
        {
            var gains = new List<FinalGain>();
            AddGain(gains, "基础灵力", snapshot.BaseManaBonus, false, "mana",
                "每回合基础灵力增加。");
            AddGain(gains, "百术周天", snapshot.CardPlayGrowthBaseManaBonus, false, "mana",
                $"本天机独立增加基础灵力；累计真实出牌 {snapshot.CardPlayGrowthTotalRealPlays} 次。");
            AddGain(gains, "护体", snapshot.BaseArmorPerTurnBonus, false,
                "armor", "每回合获得的基础护甲增加。");
            AddGain(gains, PowerUpGlyphConfig.GetStatDisplayName(StatType.FengRui),
                snapshot.MightBonus, true, "might",
                PowerUpGlyphConfig.GetStatDescription(StatType.FengRui));
            AddGain(gains, PowerUpGlyphConfig.GetStatDisplayName(StatType.TianYun),
                snapshot.RunLuckBonus, true, "luck",
                PowerUpGlyphConfig.GetStatDescription(StatType.TianYun));
            AddGain(gains, PowerUpGlyphConfig.GetStatDisplayName(StatType.LianYu),
                snapshot.RunGreedBonus, true, "greed",
                PowerUpGlyphConfig.GetStatDescription(StatType.LianYu));
            AddGain(gains, PowerUpGlyphConfig.GetStatDisplayName(StatType.MingWu),
                snapshot.RunGrowthBonus, true, "growth",
                PowerUpGlyphConfig.GetStatDescription(StatType.MingWu));
            return gains;
        }

        private static void AddGain(
            ICollection<FinalGain> destination,
            string label,
            int amount,
            bool percentage,
            string iconId,
            string description)
        {
            if (amount <= 0)
                return;
            destination.Add(new FinalGain(
                label,
                amount,
                percentage,
                iconId,
                description));
        }

        private void Dispatch(DungeonRunResultAction action)
        {
            if (_actionDispatched)
                return;
            _actionDispatched = true;
            if (_actionError != null)
                _actionError.text = string.Empty;
            SetActionsInteractable(false);
            ActionRequested?.Invoke(action);
        }

        private Button CreateActionButton(
            Transform parent,
            string name,
            string label,
            Color accent,
            DungeonRunResultAction action,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            Button button = RewardPanelFactory.CreateButton(
                parent,
                name,
                label,
                accent);
            Place(button.GetComponent<RectTransform>(), anchorMin, anchorMax);
            button.onClick.AddListener(() => Dispatch(action));
            return button;
        }

        private static string FormatElapsed(int totalSeconds)
        {
            TimeSpan elapsed = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            return elapsed.TotalHours >= 1d
                ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        private static void Place(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            Close();
            if (_tooltip != null)
                Destroy(_tooltip.gameObject);
            _tooltip = null;
        }

        private readonly struct ResultMetric
        {
            public ResultMetric(string label, string value)
            {
                Label = label;
                Value = value;
            }

            public string Label { get; }
            public string Value { get; }
        }

        private readonly struct FinalGain
        {
            public FinalGain(
                string label,
                int amount,
                bool percentage,
                string iconId,
                string description)
            {
                Label = label;
                Amount = amount;
                Percentage = percentage;
                IconId = iconId;
                Description = description;
            }

            public string Label { get; }
            public int Amount { get; }
            public bool Percentage { get; }
            public string IconId { get; }
            public string Description { get; }
            public string FormattedValue =>
                $"+{Amount}{(Percentage ? "%" : string.Empty)}";
        }
    }

    /// <summary>把结果页静态缩略图转换为不持有业务状态的 Hover 表面。</summary>
    internal sealed class RunSummaryHoverTarget : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        private Action _onEnter;
        private Action _onExit;

        public void Bind(Action onEnter, Action onExit)
        {
            _onEnter = onEnter;
            _onExit = onExit;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            _onEnter?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
            => _onExit?.Invoke();

        private void OnDisable()
            => _onExit?.Invoke();
    }
}
