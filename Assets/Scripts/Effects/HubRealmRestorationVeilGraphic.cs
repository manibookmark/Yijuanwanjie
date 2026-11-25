using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Draws the dormant realm veil with a feathered circular opening. It owns
    /// the mesh geometry so the timeline only supplies semantic reveal progress.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HubRealmRestorationVeilGraphic : MaskableGraphic
    {
        private const int SegmentCount = 72;
        private Vector2 _origin;
        private float _revealProgress;
        private float _maximumRadius = 1450f;
        private float _feather = 210f;

        public void Configure(
            Color veilColor,
            Vector2 origin,
            float maximumRadius,
            float feather)
        {
            color = veilColor;
            _origin = origin;
            _maximumRadius = Mathf.Max(1f, maximumRadius);
            _feather = Mathf.Max(1f, feather);
            raycastTarget = false;
            SetVerticesDirty();
        }

        public void SetRevealProgress(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            if (Mathf.Abs(_revealProgress - clamped) < 0.0001f)
                return;
            _revealProgress = clamped;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_revealProgress >= 0.999f || color.a <= 0.001f)
                return;

            Rect bounds = GetPixelAdjustedRect();
            if (_revealProgress <= 0.001f)
            {
                AddSolidRect(vertexHelper, bounds, color);
                return;
            }

            float eased = 1f - Mathf.Pow(1f - _revealProgress, 3f);
            float clearRadius = _maximumRadius * eased;
            float featherRadius = clearRadius +
                                  _feather * (0.35f + 0.65f * eased);
            float outerRadius = Mathf.Max(
                _maximumRadius + _feather + 800f,
                bounds.size.magnitude + _origin.magnitude + 200f);

            Color transparent = color;
            transparent.a = 0f;
            for (int index = 0; index < SegmentCount; index++)
            {
                float angleA = index / (float)SegmentCount * Mathf.PI * 2f;
                float angleB = (index + 1) / (float)SegmentCount *
                               Mathf.PI * 2f;
                Vector2 directionA = new Vector2(
                    Mathf.Cos(angleA), Mathf.Sin(angleA));
                Vector2 directionB = new Vector2(
                    Mathf.Cos(angleB), Mathf.Sin(angleB));

                Vector2 innerA = _origin + directionA * clearRadius;
                Vector2 innerB = _origin + directionB * clearRadius;
                Vector2 softA = _origin + directionA * featherRadius;
                Vector2 softB = _origin + directionB * featherRadius;
                Vector2 outerA = _origin + directionA * outerRadius;
                Vector2 outerB = _origin + directionB * outerRadius;

                AddQuad(
                    vertexHelper,
                    innerA, innerB, softB, softA,
                    transparent, transparent, color, color);
                AddQuad(
                    vertexHelper,
                    softA, softB, outerB, outerA,
                    color, color, color, color);
            }
        }

        private static void AddSolidRect(
            VertexHelper helper, Rect rect, Color colour)
        {
            AddQuad(
                helper,
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMin, rect.yMax),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMax, rect.yMin),
                colour, colour, colour, colour);
        }

        private static void AddQuad(
            VertexHelper helper,
            Vector2 first,
            Vector2 second,
            Vector2 third,
            Vector2 fourth,
            Color firstColor,
            Color secondColor,
            Color thirdColor,
            Color fourthColor)
        {
            int start = helper.currentVertCount;
            AddVertex(helper, first, firstColor);
            AddVertex(helper, second, secondColor);
            AddVertex(helper, third, thirdColor);
            AddVertex(helper, fourth, fourthColor);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start + 2, start + 3, start);
        }

        private static void AddVertex(
            VertexHelper helper, Vector2 position, Color colour)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = colour;
            helper.AddVert(vertex);
        }
    }
}
