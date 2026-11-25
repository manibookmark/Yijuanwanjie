using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 出牌堆视图：打出的卡牌在屏幕右侧堆叠。
    /// Z 轴旋转与棋盘格右侧边角度对齐。
    /// 注意：ScreenSpaceOverlay Canvas 不支持真 3D 透视，
    /// X 轴旋转只会压扁不会产生纵深感，因此默认 pitchAngle=0。
    /// </summary>
    public class PlayPileView : MonoBehaviour
    {
        [Header("布局参数")]
        [Tooltip("堆叠中卡牌的缩放比例")]
        public float cardScale = 0.55f;
        [Tooltip("每张卡牌向上的垂直偏移（像素）")]
        public float stackOffset = 32f;
        [Tooltip("堆叠中最多可见的卡牌数量")]
        public int maxVisibleCards = 5;

        [Header("旋转")]
        [Tooltip("Z轴旋转（度），与棋盘格右侧边角度对齐")]
        public float tiltAngle = 15f;
        [Tooltip("X轴旋转（度）——ScreenSpaceOverlay下只会压扁，建议0")]
        public float pitchAngle = 0f;

        [Header("位置")]
        [Tooltip("出牌堆锚点位置（像素）")]
        public Vector2 pilePosition = new Vector2(-280f, -60f);
        [Tooltip("出牌堆尺寸（像素）")]
        public Vector2 pileSize = new Vector2(220f, 520f);

        private readonly List<CardView> _pileCards = new List<CardView>();
        private bool _layoutReady;
        private int _generation;
        private Button _inspectButton;

        public float PileCardScale => cardScale;

        public void ConfigureInspection(System.Action inspect)
        {
            if (_inspectButton == null)
            {
                var hit = new GameObject("InspectPile", typeof(RectTransform), typeof(Image), typeof(Button));
                hit.transform.SetParent(transform, false);
                hit.transform.SetAsFirstSibling();
                var rect = hit.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                hit.GetComponent<Image>().color = Color.clear;
                _inspectButton = hit.GetComponent<Button>();
                _inspectButton.transition = Selectable.Transition.None;
            }
            _inspectButton.onClick.RemoveAllListeners();
            _inspectButton.onClick.AddListener(() => inspect?.Invoke());
            var group = GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.blocksRaycasts = true;
                group.interactable = true;
            }
        }

        /// <summary>
        /// Captures the current pile generation for an in-flight card.
        /// Clearing the pile invalidates every card that started flying in an
        /// older generation, so it cannot land back into a newly restarted chain.
        /// </summary>
        public int CaptureGeneration() => _generation;

        public void AddCard(CardView cardView)
        {
            TryAddCard(cardView, _generation);
        }

        public bool TryAddCard(CardView cardView, int expectedGeneration)
        {
            if (cardView == null) return false;
            if (expectedGeneration != _generation)
            {
                Destroy(cardView.gameObject);
                return false;
            }

            if (cardView.cardButton != null)
                cardView.cardButton.interactable = false;
            var hover = cardView.GetComponent<CardHoverHandler>();
            if (hover != null)
            {
                hover.StopAllCoroutines();
                hover.enabled = false;
            }

            var cg = cardView.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }

            cardView.transform.SetParent(transform, false);
            _pileCards.Add(cardView);

            RepositionPile();
            TrimPile();
            return true;
        }

        /// <summary>
        /// Removes a card that has already landed in the pile but is then
        /// consumed by a destroy-on-use presentation. The visual owner is
        /// responsible for destroying the card object after this bookkeeping
        /// update so the remaining stack can be laid out immediately.
        /// </summary>
        public bool RemoveCard(CardView cardView)
        {
            if (cardView == null) return false;
            bool removed = _pileCards.Remove(cardView);
            if (removed)
                RepositionPile();
            return removed;
        }

        public void ClearPile()
        {
            unchecked
            {
                _generation++;
            }
            foreach (var card in _pileCards)
            {
                if (card != null) Destroy(card.gameObject);
            }
            _pileCards.Clear();
        }

        private void RepositionPile()
        {
            int visibleCount = Mathf.Min(_pileCards.Count, maxVisibleCards);
            int startIdx = _pileCards.Count - visibleCount;

            for (int i = 0; i < visibleCount; i++)
            {
                var card = _pileCards[startIdx + i];
                if (card == null) continue;

                float yOffset = (visibleCount - 1 - i) * stackOffset;
                card.transform.localPosition = new Vector3(0f, yOffset, 0f);
                card.transform.localScale = Vector3.one * cardScale;
                card.transform.localRotation = Quaternion.identity;
                card.transform.SetSiblingIndex(i);
            }
        }

        private void TrimPile()
        {
            while (_pileCards.Count > maxVisibleCards)
            {
                var oldCard = _pileCards[0];
                _pileCards.RemoveAt(0);
                if (oldCard != null) Destroy(oldCard.gameObject);
            }
        }

        public static PlayPileView Create(Transform parent)
        {
            var go = new GameObject(
                "PlayPileView",
                typeof(RectTransform),
                typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var view = go.AddComponent<PlayPileView>();
            view.InitTransform();
            return view;
        }

        private void InitTransform()
        {
            var rt = GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            ApplyTransform();
            _layoutReady = true;
        }

        private void ApplyTransform()
        {
            var rt = GetComponent<RectTransform>();
            rt.localRotation = Quaternion.Euler(pitchAngle, 0f, tiltAngle);
            rt.anchoredPosition = pilePosition;
            rt.sizeDelta = pileSize;
            RepositionPile();
        }

        private void OnValidate()
        {
            // AddComponent 瞬间 Unity 会回调 OnValidate，此时改
            // RectTransform 会触发禁用期的 SendMessage 警告；
            // 首次布局完成前跳过。
            if (!_layoutReady) return;
            ApplyTransform();
        }
    }
}
