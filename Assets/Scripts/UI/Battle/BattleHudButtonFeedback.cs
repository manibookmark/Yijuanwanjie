using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// Adds transform-only pointer feedback without changing the authored
    /// RectTransform layout. Every state is calculated from a captured rest
    /// state, so repeated input cannot accumulate position or scale drift.
    /// </summary>
    public sealed class BattleHudButtonFeedback : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        [SerializeField] private float hoverScale = 1.04f;
        [SerializeField] private float pressedScale = 0.94f;
        [SerializeField] private float pressedYOffset = -2f;
        [SerializeField] private Color pressedTint =
            new Color(0.78f, 0.70f, 0.56f, 1f);

        private RectTransform _rectTransform;
        private Button _button;
        private Graphic _targetGraphic;
        private Vector3 _restScale;
        private Vector2 _restPosition;
        private Color _restColor = Color.white;
        private bool _hasRestState;
        private bool _hovered;
        private bool _pressed;

        public void Bind(Graphic targetGraphic)
        {
            CacheComponents();
            if (_targetGraphic != targetGraphic)
            {
                RestoreRestState();
                _targetGraphic = targetGraphic;
                _hasRestState = false;
            }

            CaptureRestState();
            ApplyVisualState();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!WanjieScroll.UI.TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            if (!CanInteract()) return;
            _hovered = true;
            ApplyVisualState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
            ApplyVisualState();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!CanInteract()) return;
            _pressed = true;
            ApplyVisualState();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            ApplyVisualState();
        }

        private void Awake()
        {
            CacheComponents();
            CaptureRestState();
        }

        private void OnDisable()
        {
            _hovered = false;
            _pressed = false;
            RestoreRestState();
        }

        private void CacheComponents()
        {
            if (_rectTransform == null)
                _rectTransform = transform as RectTransform;
            if (_button == null)
                _button = GetComponent<Button>();
        }

        private void CaptureRestState()
        {
            if (_hasRestState || _rectTransform == null) return;
            _restScale = _rectTransform.localScale;
            _restPosition = _rectTransform.anchoredPosition;
            if (_targetGraphic != null)
                _restColor = _targetGraphic.color;
            _hasRestState = true;
        }

        private bool CanInteract()
        {
            return isActiveAndEnabled &&
                   (_button == null || _button.IsInteractable());
        }

        private void ApplyVisualState()
        {
            CaptureRestState();
            if (!_hasRestState) return;

            float scale = _pressed
                ? pressedScale
                : (_hovered ? hoverScale : 1f);
            _rectTransform.localScale = _restScale * scale;
            _rectTransform.anchoredPosition = _restPosition +
                (_pressed ? new Vector2(0f, pressedYOffset) : Vector2.zero);

            if (_targetGraphic != null)
                _targetGraphic.color = _pressed ? pressedTint : _restColor;
        }

        private void RestoreRestState()
        {
            if (!_hasRestState) return;
            if (_rectTransform != null)
            {
                _rectTransform.localScale = _restScale;
                _rectTransform.anchoredPosition = _restPosition;
            }
            if (_targetGraphic != null)
                _targetGraphic.color = _restColor;
        }
    }
}
