using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// One read-only draw-pile paper marker and its hover lifetime. DeckPileView supplies
    /// the actual pending count; this component has no combat rules, card cache
    /// or independent counter. Its tooltip may live on the same HUD Canvas and is explicitly
    /// hidden/released on disable, owner retirement and destruction.
    /// </summary>
    internal sealed class DeckConfusionStatusPresentation : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private Text _anchor;
        private DeckConfusionPaperGraphic _paper;
        private string _detail;
        private GameObject _tooltipRoot;
        private CardTooltipView _tooltip;
        private bool _hovered;
        private EventSystem _pointerSystem;
        private PointerEventData _pointer;

        internal bool IsAnchoredTo(Text anchor) => _anchor == anchor;

        internal static DeckConfusionStatusPresentation Create(Text anchor)
        {
            var root = new GameObject("DrawPileConfusionStatus", typeof(RectTransform),
                typeof(CanvasRenderer));
            root.SetActive(false);
            root.transform.SetParent(anchor.rectTransform.parent as RectTransform ?? anchor.rectTransform, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(52f, 34f);
            var paper = root.AddComponent<DeckConfusionPaperGraphic>();
            paper.raycastTarget = true;

            var owner = root.AddComponent<DeckConfusionStatusPresentation>();
            owner._anchor = anchor;
            owner._paper = paper;
            owner.AlignToAnchor();
            return owner;
        }

        internal void Present(int remaining)
        {
            if (remaining <= 0) { Hide(); return; }
            _detail = "待抽扰灵×" + remaining + "\n抽到扰灵时，本回合手牌费用随机±2";
            _paper.SetCount(remaining);
            AlignToAnchor();
            gameObject.SetActive(true);
            if (_hovered) _tooltip?.ShowText(_detail);
        }

        private void AlignToAnchor()
        {
            if (_anchor == null) return;
            RectTransform host = _anchor.rectTransform.parent as RectTransform ?? _anchor.rectTransform;
            var rect = (RectTransform)transform;
            if (rect.parent != host) rect.SetParent(host, false);
            Vector3 centre = host.InverseTransformPoint(
                _anchor.rectTransform.TransformPoint(_anchor.rectTransform.rect.center));
            rect.anchoredPosition = new Vector2(12f, centre.y - host.rect.center.y);
        }

        internal void Hide()
        {
            HideTooltip();
            gameObject.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || !TopmostHoverGate.IsTopmost(eventData, gameObject)) return;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            if (_tooltip == null)
            {
                _tooltipRoot = new GameObject("DrawPileConfusionTooltip", typeof(RectTransform));
                _tooltipRoot.transform.SetParent(canvas.transform, false);
                _tooltip = _tooltipRoot.AddComponent<CardTooltipView>();
            }
            _hovered = true;
            _tooltip.ShowText(_detail);
        }

        public void OnPointerExit(PointerEventData eventData) => HideTooltip();
        public void OnPointerDown(PointerEventData eventData) => HideTooltip();

        private void LateUpdate()
        {
            if (!_hovered) return;
            // A modal may appear while the mouse is still; do not rely on a
            // PointerExit event to withdraw a tooltip above that new modal.
            EventSystem current = EventSystem.current;
            if (current == null) { HideTooltip(); return; }
            if (_pointer == null || current != _pointerSystem)
            {
                _pointerSystem = current;
                _pointer = new PointerEventData(current);
            }
            _pointer.Reset();
            _pointer.position = Input.mousePosition;
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)transform, _pointer.position, camera) ||
                !TopmostHoverGate.IsTopmost(_pointer, gameObject)) HideTooltip();
        }

        private void HideTooltip()
        {
            _hovered = false;
            _tooltip?.Hide();
        }

        internal void Dispose()
        {
            Hide();
            ReleaseTooltip();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        private void ReleaseTooltip()
        {
            HideTooltip();
            GameObject root = _tooltipRoot;
            _tooltip = null;
            _tooltipRoot = null;
            if (root == null) return;
            root.SetActive(false);
            if (Application.isPlaying) Destroy(root);
            else DestroyImmediate(root);
        }

        private void OnDisable() => ReleaseTooltip();
        private void OnDestroy() => ReleaseTooltip();
    }

    /// <summary>Small folded sheets, with a cut corner and a translucent edge
    /// light. Geometry stays inside the marker's own Canvas-scaled rectangle.</summary>
    internal sealed class DeckConfusionPaperGraphic : MaskableGraphic
    {
        private int _sheets = 1;

        internal void SetCount(int count)
        {
            int sheets = Mathf.Clamp(count, 1, 3);
            if (sheets == _sheets) return;
            _sheets = sheets;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            Vector2 scale = new Vector2(rect.width / 52f, rect.height / 34f);
            for (int layer = _sheets - 1; layer >= 0; layer--)
            {
                Vector2 shift = new Vector2(-layer * 3f, layer * 2f);
                var points = new[] { new Vector2(-10f, -12f), new Vector2(13f, -12f),
                    new Vector2(13f, 4f), new Vector2(6f, 11f), new Vector2(-10f, 11f) };
                for (int i = 0; i < points.Length; i++)
                    points[i] = rect.center + Vector2.Scale(points[i] + shift, scale);
                Color body = layer == 0 ? new Color(0.43f, 0.28f, 0.59f, 0.96f)
                    : new Color(0.26f, 0.16f, 0.38f, 0.96f);
                for (int i = 0; i < points.Length; i++)
                    Stroke(mesh, points[i], points[(i + 1) % points.Length], 4f,
                        new Color(0.64f, 0.39f, 0.88f, 0.13f));
                Polygon(mesh, points, body);
                for (int i = 0; i < points.Length; i++)
                    Stroke(mesh, points[i], points[(i + 1) % points.Length], 0.8f,
                        new Color(0.69f, 0.50f, 0.86f, 0.85f));
                Polygon(mesh, new[] { points[2],
                    rect.center + Vector2.Scale(new Vector2(6f, 4f) + shift, scale), points[3] },
                    new Color(0.82f, 0.68f, 0.95f, 0.98f));
            }
        }

        private static void Polygon(VertexHelper mesh, Vector2[] points, Color color)
        {
            int start = mesh.currentVertCount;
            foreach (Vector2 point in points) mesh.AddVert(point, color, Vector2.zero);
            for (int i = 1; i + 1 < points.Length; i++) mesh.AddTriangle(start, start + i, start + i + 1);
        }

        private static void Stroke(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 delta = to - from;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * 0.5f);
            Polygon(mesh, new[] { from - normal, to - normal, to + normal, from + normal }, color);
        }
    }
}
