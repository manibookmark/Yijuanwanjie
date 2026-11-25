using UnityEngine;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// One Canvas owns the active buff tooltip identity. The last icon to show
    /// it owns visibility; CardTooltipView owns text layout and pointer/edge
    /// placement, sharing the same bounded tooltip behavior as cards.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class BuffTooltipView : MonoBehaviour
    {
        private Canvas _canvas;
        private GameObject _root;
        private CardTooltipView _tooltip;
        private BuffIconView _owner;

        internal static BuffTooltipView ForCanvas(Canvas canvas)
        {
            if (canvas == null) return null;
            BuffTooltipView view = canvas.GetComponent<BuffTooltipView>();
            return view != null ? view : canvas.gameObject.AddComponent<BuffTooltipView>();
        }

        private void Awake() => _canvas = GetComponent<Canvas>();

        internal void Show(BuffIconView owner, string text)
        {
            if (!isActiveAndEnabled || _canvas == null || !_canvas.isActiveAndEnabled)
            {
                HideCurrent();
                return;
            }
            if (owner == null || !owner.isActiveAndEnabled)
                return;
            EnsureBuilt();
            _owner = owner;
            _tooltip.ShowText(text);
        }

        internal void Hide(BuffIconView owner)
        {
            if (ReferenceEquals(owner, _owner)) HideCurrent();
        }

        private void HideCurrent()
        {
            _owner = null;
            if (_tooltip != null) _tooltip.Hide();
        }

        private void Update()
        {
            if (_canvas == null || !_canvas.isActiveAndEnabled ||
                _owner == null || !_owner.isActiveAndEnabled) HideCurrent();
        }

        private void EnsureBuilt()
        {
            if (_tooltip != null) return;
            _root = new GameObject("BuffTooltip", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            _tooltip = _root.AddComponent<CardTooltipView>();
        }

        private void OnDisable() => HideCurrent();

        private void OnDestroy()
        {
            HideCurrent();
            if (_root != null)
            {
                _root.SetActive(false);
                Destroy(_root);
            }
            _tooltip = null;
            _root = null;
        }
    }
}
