using UnityEngine;
using UnityEngine.EventSystems;

namespace WanjieScroll.UI.Daozang
{
    /// <summary>
    /// Owns hover tooltip lifetime for one Daozang control. Empty text keeps
    /// the surface silent, which lets locked talismans update in place.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DaozangTooltipTrigger : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private CardTooltipView _tooltip;
        private string _text;
        private bool _hovered;

        public string TooltipText => _text;

        public void Bind(CardTooltipView tooltip, string text)
        {
            _tooltip = tooltip;
            SetText(text);
        }

        public void SetText(string text)
        {
            _text = text;
            if (_hovered && string.IsNullOrWhiteSpace(_text))
                Hide();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!TopmostHoverGate.IsTopmost(eventData, gameObject))
                return;
            if (_tooltip == null || string.IsNullOrWhiteSpace(_text))
                return;
            _hovered = true;
            _tooltip.ShowText(_text);
        }

        public void OnPointerExit(PointerEventData eventData) => Hide();

        public void OnPointerDown(PointerEventData eventData) => Hide();

        private void OnDisable() => Hide();

        private void Hide()
        {
            if (!_hovered)
                return;
            _hovered = false;
            _tooltip?.Hide();
        }
    }
}
