using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Maps
{
    /// <summary>
    /// Static F-style ebony case: a round jade seat joined to a slim numeric
    /// plate. Owns its mesh only; the prefab owns the real currency icon and
    /// live Text. The numeric plate extends with the wallet's authored width.
    /// No sample icon, digits, runtime children, material or animation is baked in.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapWalletJadeGraphic : MaskableGraphic
    {
        private static readonly Color WoodDark = new Color32(38, 27, 22, 255);
        private static readonly Color WoodLight = new Color32(83, 59, 43, 255);
        private static readonly Color InsetDark = new Color32(20, 22, 21, 255);
        private static readonly Color InsetLight = new Color32(35, 35, 31, 255);

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect box = GetPixelAdjustedRect();
            if (box.width < 70f || box.height < 20f) return;
            float radius = Mathf.Min(25f, box.height * 0.5f);
            Vector2 seat = new Vector2(box.xMin + radius, box.center.y);
            Rect plate = new Rect(seat.x, box.center.y - radius * 0.76f,
                box.xMax - seat.x, radius * 1.52f);

            Color shadow = new Color(0f, 0f, 0f, 0.20f);
            Rect shadowPlate = plate;
            shadowPlate.position += new Vector2(0f, -2.5f);
            AddPlate(vertices, shadowPlate, 6f, shadow, shadow);
            AddDisc(vertices, seat + new Vector2(0f, -2.5f), radius, shadow, shadow);

            // Thin hard case with a dark low-frequency wooden rim.
            AddPlate(vertices, plate, 6f, WoodDark, WoodLight);
            AddPlate(vertices, Inset(plate, 1.2f), 5f,
                Color.Lerp(WoodDark, Color.black, 0.30f), WoodDark);
            AddPlate(vertices, Inset(plate, 2.6f), 4f,
                WoodDark, Color.Lerp(WoodLight, WoodDark, 0.20f));
            AddPlate(vertices, Inset(plate, 3.6f), 3f, InsetDark, InsetLight);

            // The larger round end overlaps the rectangular plate, as in F.
            AddDisc(vertices, seat, radius, WoodDark, WoodLight);
            AddDisc(vertices, seat, radius - 1.2f,
                Color.Lerp(WoodDark, Color.black, 0.30f), WoodDark);
            AddDisc(vertices, seat, radius - 2.7f,
                WoodDark, Color.Lerp(WoodLight, WoodDark, 0.20f));
            AddDisc(vertices, seat, radius - 3.8f, InsetDark, InsetLight);
        }

        private void AddDisc(VertexHelper vertices, Vector2 center, float radius,
            Color lower, Color upper)
        {
            if (radius <= 0f) return;
            const int segments = 64;
            int first = vertices.currentVertCount;
            AddVertex(vertices, center, Color.Lerp(lower, upper, 0.5f));
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                AddVertex(vertices, center + unit * radius,
                    Color.Lerp(lower, upper, (unit.y + 1f) * 0.5f));
            }
            for (int i = 0; i < segments; i++)
                vertices.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % segments);
        }

        private void AddPlate(VertexHelper vertices, Rect rect, float cut,
            Color lower, Color upper)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            cut = Mathf.Min(cut, Mathf.Min(rect.width, rect.height) * 0.5f);
            Vector2[] edge =
            {
                new Vector2(rect.xMin + cut, rect.yMin),
                new Vector2(rect.xMax - cut, rect.yMin),
                new Vector2(rect.xMax, rect.yMin + cut),
                new Vector2(rect.xMax, rect.yMax - cut),
                new Vector2(rect.xMax - cut, rect.yMax),
                new Vector2(rect.xMin + cut, rect.yMax),
                new Vector2(rect.xMin, rect.yMax - cut),
                new Vector2(rect.xMin, rect.yMin + cut)
            };
            int first = vertices.currentVertCount;
            AddVertex(vertices, rect.center, Color.Lerp(lower, upper, 0.5f));
            for (int i = 0; i < edge.Length; i++)
                AddVertex(vertices, edge[i], Color.Lerp(lower, upper,
                    Mathf.InverseLerp(rect.yMin, rect.yMax, edge[i].y)));
            for (int i = 0; i < edge.Length; i++)
                vertices.AddTriangle(first, first + 1 + i,
                    first + 1 + (i + 1) % edge.Length);
        }

        private void AddVertex(VertexHelper vertices, Vector2 position, Color tint)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = tint * color;
            vertex.uv0 = Vector2.zero;
            vertices.AddVert(vertex);
        }

        private static Rect Inset(Rect rect, float inset) =>
            new Rect(rect.x + inset, rect.y + inset,
                rect.width - inset * 2f, rect.height - inset * 2f);
    }
}
