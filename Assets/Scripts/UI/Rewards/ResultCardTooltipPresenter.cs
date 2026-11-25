using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.UI.Cards;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 已提交成果卡停留期的 Tooltip 所有者。只读取最终卡牌说明快照和卡面矩形，
    /// 不依赖被禁用的卡牌按钮，也不添加射线目标，因此不抢全屏“点击收取”。
    /// Overlay 在演出结束后启用、开始收取前撤销；本模块负责移出/禁用时隐藏。
    /// </summary>
    public sealed class ResultCardTooltipPresenter : MonoBehaviour
    {
        private CardTooltipView _tooltip;
        private RectTransform _cardRect;
        private Canvas _canvas;
        private string _text;
        private bool _armed;
        private bool _visible;

        internal static ResultCardTooltipPresenter Create(Transform overlayRoot)
        {
            var host = new GameObject("ResultCardHover", typeof(RectTransform));
            host.transform.SetParent(overlayRoot, false);
            var presenter = host.AddComponent<ResultCardTooltipPresenter>();
            var tooltipObject = new GameObject("ResultCardTooltip", typeof(RectTransform));
            tooltipObject.transform.SetParent(overlayRoot, false);
            presenter._tooltip = tooltipObject.AddComponent<CardTooltipView>();
            presenter._canvas = overlayRoot.GetComponentInParent<Canvas>();
            return presenter;
        }

        internal static string BuildText(CardConfig card)
        {
            if (card == null) return null;
            string rules = CardTextRenderer.GetTooltipText(card);
            string text = string.IsNullOrWhiteSpace(rules)
                ? card.cardName
                : card.cardName + "\n\n" + rules;
            return text;
        }

        internal void Bind(RectTransform cardRect, string text)
        {
            Hide();
            _cardRect = cardRect;
            _text = text;
        }

        internal void EnableForResultHold()
        {
            _armed = _cardRect != null && !string.IsNullOrWhiteSpace(_text);
        }

        internal void Hide()
        {
            _armed = false;
            HideTooltip();
        }

        private void Update()
        {
            if (!_armed || _cardRect == null || !_cardRect.gameObject.activeInHierarchy)
            {
                HideTooltip();
                return;
            }
            // Collection stays owned by the existing overlay click target.
            // Do not let this frame's pointer-down briefly redisplay the tooltip.
            if (Input.GetMouseButtonDown(0))
            {
                HideTooltip();
                return;
            }
            Camera camera = _canvas != null &&
                _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(
                _cardRect, Input.mousePosition, camera);
            if (!inside)
            {
                HideTooltip();
                return;
            }
            if (_visible || _tooltip == null) return;
            _tooltip.transform.SetAsLastSibling();
            _tooltip.ShowText(_text);
            _visible = true;
        }

        private void HideTooltip()
        {
            if (_tooltip != null) _tooltip.Hide();
            _visible = false;
        }

        private void OnDisable() => Hide();
        private void OnDestroy() => Hide();
    }
}
