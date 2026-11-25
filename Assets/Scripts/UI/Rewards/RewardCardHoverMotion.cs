using UnityEngine;
using UnityEngine.EventSystems;
using WanjieScroll.UI.Views;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>
    /// 奖励候选专用悬停反馈。只改变候选 VisualRoot，卡牌与灵印共用，
    /// 不参与手牌重排、奖励选择或父节点切换。
    /// </summary>
    public sealed class RewardCardHoverMotion : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        // Reward cards already use a compact, enlarged presentation scale. Keep
        // their text at that scale while hovering; lift, sway and highlight
        // provide the feedback without making the copy harder to read.
        private const float HoverScale = 1f;
        private const float HoverLift = 14f;
        private const float MaxSwayDegrees = 2f;

        private System.Action<bool> _setHighlight;
        private RectTransform _visual;
        private Vector3 _basePosition;
        private Vector3 _baseScale;
        private Quaternion _baseRotation;
        private float _blend;
        private bool _hovered;
        private bool _pressed;

        public bool IsHovered => _hovered;

        public void Bind(CardView card)
        {
            Bind(card != null ? card.visualRoot : null,
                card != null ? (System.Action<bool>)card.SetHighlight : null);
        }

        public void Bind(RectTransform visual, System.Action<bool> setHighlight)
        {
            _visual = visual;
            _setHighlight = setHighlight;
            if (_visual == null) return;
            _basePosition = _visual.localPosition;
            _baseScale = _visual.localScale;
            _baseRotation = _visual.localRotation;
        }

        private void Update()
        {
            if (_visual == null) return;
            float target = _hovered ? 1f : 0f;
            float response = 1f - Mathf.Exp(
                -Time.unscaledDeltaTime * (_hovered ? 14f : 10f));
            _blend = Mathf.Lerp(_blend, target, response);

            float pressScale = _pressed ? 0.975f : 1f;
            float scale = Mathf.Lerp(1f, HoverScale, _blend) * pressScale;
            _visual.localScale = Vector3.Scale(
                _baseScale,
                new Vector3(scale, scale, 1f));
            _visual.localPosition = _basePosition +
                                    Vector3.up * (HoverLift * _blend);

            float sway = Mathf.Sin(Time.unscaledTime * 2.5f) *
                         MaxSwayDegrees * _blend;
            _visual.localRotation = _baseRotation *
                                    Quaternion.Euler(0f, 0f, sway);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            _hovered = true;
            _setHighlight?.Invoke(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
            _setHighlight?.Invoke(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_hovered) _pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
        }

        private void OnDisable()
        {
            _hovered = false;
            _pressed = false;
            _blend = 0f;
            _setHighlight?.Invoke(false);
            if (_visual == null) return;
            _visual.localPosition = _basePosition;
            _visual.localScale = _baseScale;
            _visual.localRotation = _baseRotation;
        }
    }
}
