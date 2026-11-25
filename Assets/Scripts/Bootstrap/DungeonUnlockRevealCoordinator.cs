using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.UI.Hub;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 终层首通解锁的逐个揭示编排。由 RewardFeedbackPresenter 统一管理奖励反馈，
    /// 只负责把结算奖励清单翻译成展示序列，不提交奖励、不修改存档。
    /// 新关区由地图自身表达，不进入揭示序列。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonUnlockRevealCoordinator : MonoBehaviour
    {
        private readonly Dictionary<string, CardConfig> _cardsByContentId =
            new Dictionary<string, CardConfig>(StringComparer.OrdinalIgnoreCase);

        private Canvas _canvas;
        private RewardFeedbackPresenter _overlay;
        private TianluRegistryCatalog _tianluCatalog;
        private Coroutine _sequence;

        public bool IsPlaying => _sequence != null;

        public void Bind(IReadOnlyList<CardConfig> cardPool)
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
            _tianluCatalog = Resources.Load<TianluRegistryCatalog>(
                "Content/Hub/tianlu-registry-catalog");
        }

        /// <summary>清单中存在需要揭示的新解锁（英灵与新关区除外）。</summary>
        public static bool HasReveals(
            IReadOnlyList<DungeonRunRewardResult> rewards)
        {
            return CollectReveals(rewards).Count > 0;
        }

        /// <summary>逐个揭示新解锁，全部关闭后回调；没有可揭示项时立即回调。</summary>
        public void PlaySequence(
            IReadOnlyList<DungeonRunRewardResult> rewards,
            Action onCompleted)
        {
            if (_sequence != null)
                return;
            List<DungeonRunRewardResult> pending = CollectReveals(rewards);
            if (pending.Count == 0)
            {
                onCompleted?.Invoke();
                return;
            }
            _sequence = StartCoroutine(RunSequence(pending, onCompleted));
        }

        private IEnumerator RunSequence(
            List<DungeonRunRewardResult> pending,
            Action onCompleted)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                RewardFeedbackItem item = BuildItem(pending[i]);
                if (item == null)
                    continue;

                bool closed = false;
                _overlay ??= RewardFeedbackPresenter.Create(this);
                _overlay.Show(
                    EnsureCanvas(),
                    item,
                    () => closed = true);

                while (!closed)
                    yield return null;
            }

            _overlay?.CloseSilently();
            _sequence = null;
            onCompleted?.Invoke();
        }

        private RewardFeedbackItem BuildItem(DungeonRunRewardResult reward)
        {
            string displayName = string.IsNullOrWhiteSpace(reward.DisplayName)
                ? reward.TargetContentId
                : reward.DisplayName;
            string description = ResolveDescription(reward);
            Sprite artwork = null;
            string presentationId = null;
            string title;
            Color accent;
            RewardRarity rarity;
            RewardFeedbackKind kind = RewardFeedbackKind.Generic;

            switch (reward.Category)
            {
                case DungeonRunRewardCategory.Crawler:
                    title = $"新英灵 · {displayName}";
                    accent = new Color(0.86f, 0.70f, 0.39f, 1f);
                    rarity = RewardRarity.Rare;
                    artwork = ResolveCrawlerArtwork(
                        reward.TargetContentId,
                        ref description);
                    break;
                case DungeonRunRewardCategory.Relic:
                    title = $"遗珍收录 · {displayName}";
                    accent = new Color(0.72f, 0.57f, 0.34f, 1f);
                    rarity = RewardRarity.Uncommon;
                    kind = RewardFeedbackKind.Relic;
                    presentationId = RewardArtworkCatalog.ResolveRelicPresentationId(
                        reward.TargetContentId);
                    artwork = RewardArtworkCatalog.Resolve(presentationId);
                    break;
                case DungeonRunRewardCategory.Arcana:
                    title = $"天机显现 · {displayName}";
                    accent = new Color(0.58f, 0.48f, 0.72f, 1f);
                    rarity = RewardRarity.Uncommon;
                    break;
                case DungeonRunRewardCategory.Facility:
                    title = $"设施开放 · {displayName}";
                    accent = SpiritEnergyColorPalette.Energy;
                    rarity = RewardRarity.Uncommon;
                    if (string.Equals(reward.TargetContentId,
                            "tianlu.entry.facility.tianjige.v1",
                            StringComparison.Ordinal))
                    {
                        presentationId = RewardArtworkCatalog.TianjiFacilityPresentationId;
                        artwork = RewardArtworkCatalog.Resolve(presentationId);
                    }
                    break;
                default:
                    title = $"新开放 · {displayName}";
                    accent = new Color(0.86f, 0.70f, 0.39f, 1f);
                    rarity = RewardRarity.Common;
                    break;
            }

            return new RewardFeedbackItem
            {
                ReceiptId = reward.GrantContentId,
                Kind = kind,
                Title = title,
                DisplayName = displayName,
                Description = description,
                PresentationId = presentationId,
                Artwork = artwork,
                Rarity = rarity,
                Accent = accent
            };
        }

        private string ResolveDescription(DungeonRunRewardResult reward)
        {
            if (_tianluCatalog?.entries != null)
            {
                for (int i = 0; i < _tianluCatalog.entries.Count; i++)
                {
                    TianluRegistryCatalog.Entry entry =
                        _tianluCatalog.entries[i];
                    if (entry == null)
                        continue;
                    if (string.Equals(
                            entry.contentId,
                            reward.GrantContentId,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            entry.contentId,
                            reward.TargetContentId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return string.IsNullOrWhiteSpace(entry.unlockedText)
                            ? "已开放，可在神域中查看。"
                            : entry.unlockedText;
                    }
                }
            }
            return "已开放，可在神域中查看。";
        }

        private Sprite ResolveCrawlerArtwork(
            string crawlerId,
            ref string description)
        {
            try
            {
                if (!ContentCatalog.Current.TryGetCrawler(
                        crawlerId,
                        out CrawlerData crawler))
                {
                    return null;
                }
                if (!string.IsNullOrWhiteSpace(crawler.playstyleTag))
                    description = crawler.playstyleTag;
                if (!string.IsNullOrWhiteSpace(crawler.cardContentId) &&
                    _cardsByContentId.TryGetValue(
                        crawler.cardContentId,
                        out CardConfig card) &&
                    card != null)
                {
                    return card.iconArt != null ? card.iconArt : card.cardArt;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[DungeonUnlockReveal] 英灵素材解析失败：" +
                    $"{crawlerId}，{exception.Message}");
            }
            return null;
        }

        private static List<DungeonRunRewardResult> CollectReveals(
            IReadOnlyList<DungeonRunRewardResult> rewards)
        {
            var result = new List<DungeonRunRewardResult>();
            if (rewards == null)
                return result;
            for (int i = 0; i < rewards.Count; i++)
            {
                DungeonRunRewardResult reward = rewards[i];
                if (!reward.NewlyGranted)
                    continue;
                // 新关区由地图表达；经诀资格在结果页显示，不追加异步揭示段。
                if (reward.Category == DungeonRunRewardCategory.Dungeon ||
                    reward.Category == DungeonRunRewardCategory.PowerUp)
                    continue;
                result.Add(reward);
            }
            return result;
        }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null)
                return _canvas;
            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystem = new GameObject(
                    "DungeonUnlockRevealEventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform, false);
            }
            var canvasObject = new GameObject(
                "DungeonUnlockRevealCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            Camera camera = Camera.main;
            if (camera != null)
            {
                _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                _canvas.worldCamera = camera;
                _canvas.planeDistance = 100f;
            }
            else
            {
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32600;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            GraphicRaycaster raycaster =
                canvasObject.GetComponent<GraphicRaycaster>();
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            raycaster.ignoreReversedGraphics = false;
            return _canvas;
        }

        private void OnDestroy()
        {
            _overlay?.CloseSilently();
        }
    }
}
