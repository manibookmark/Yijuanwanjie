using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.UI.Cards;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 奖励候选的 Hover 文本生命周期：使用奖励面板拥有的共享 Tooltip，
    /// 关闭、点击或禁用时立即隐藏；不修改卡牌的缩放与奖励选择状态。
    /// </summary>
    public sealed class RewardCardTooltipPresenter : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private CardConfig _card;
        private CardTooltipView _tooltip;
        private RewardCardTooltipContext _context;
        private Button _button;
        private bool _hovered;
        private bool _visible;
        private bool _includeAnalysis = true;

        public void Bind(CardConfig card, CardTooltipView tooltip,
            RewardCardTooltipContext context)
        {
            _card = card;
            _tooltip = tooltip;
            _context = context;
            _button = GetComponent<Button>();
        }

        public void SetIncludeAnalysis(bool value) => _includeAnalysis = value;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            _hovered = true;
            TryShow();
        }

        private void Update()
        {
            if (!_hovered) return;
            if (_visible && (_button == null || !_button.IsInteractable()))
                Hide();
            else if (!_visible)
                TryShow();
        }

        private void TryShow()
        {
            if (_tooltip == null || _card == null ||
                _button == null || !_button.IsInteractable()) return;
            _visible = true;
            _tooltip.ShowText(_context != null
                ? _context.GetText(_card, _includeAnalysis)
                : CardTextRenderer.GetTooltipText(_card));
        }

        public void OnPointerExit(PointerEventData eventData) => Hide();
        public void OnPointerDown(PointerEventData eventData) => Hide();

        private void OnDisable() => Hide();

        private void Hide()
        {
            if (!_hovered) return;
            _hovered = false;
            if (_visible) _tooltip?.Hide();
            _visible = false;
        }
    }
}
