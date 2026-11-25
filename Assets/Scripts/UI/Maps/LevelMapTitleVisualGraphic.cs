using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Maps
{
    /// <summary>
    /// 程序化绘制地图标题的稳定对比层与装饰线，不依赖地图素材。
    /// </summary>
    public sealed class LevelMapTitleVisualGraphic : MaskableGraphic
    {
        private static readonly float[] HorizontalSamples =
            { -0.5f, -0.43f, -0.31f, 0f, 0.31f, 0.43f, 0.5f };

        private static readonly float[] HorizontalAlpha =
            { 0f, 0.22f, 0.72f, 1f, 0.72f, 0.22f, 0f };

        private static readonly float[] VerticalSamples =
            { -0.5f, -0.36f, 0f, 0.36f, 0.5f };

        private static readonly float[] VerticalAlpha =
            { 0f, 0.62f, 1f, 0.62f, 0f };

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = GetPixelAdjustedRect();
            AddMistRibbon(vertexHelper, rect);
            AddAccents(vertexHelper, rect);
        }

        private static void AddMistRibbon(
            VertexHelper vertexHelper,
            Rect rect)
        {
            Color mist = new Color(0.012f, 0.047f, 0.054f, 1f);
            int xCount = HorizontalSamples.Length;
            int yCount = VerticalSamples.Length;

            for (int y = 0; y < yCount - 1; y++)
            {
                for (int x = 0; x < xCount - 1; x++)
                {
                    Vector2 bottomLeft = SamplePosition(rect, x, y);
                    Vector2 topLeft = SamplePosition(rect, x, y + 1);
                    Vector2 topRight = SamplePosition(rect, x + 1, y + 1);
                    Vector2 bottomRight = SamplePosition(rect, x + 1, y);

                    Color colorBottomLeft = WithAlpha(
                        mist,
                        0.78f * HorizontalAlpha[x] *
                        VerticalAlpha[y]);
                    Color colorTopLeft = WithAlpha(
                        mist,
                        0.78f * HorizontalAlpha[x] *
                        VerticalAlpha[y + 1]);
                    Color colorTopRight = WithAlpha(
                        mist,
                        0.78f * HorizontalAlpha[x + 1] *
                        VerticalAlpha[y + 1]);
                    Color colorBottomRight = WithAlpha(
                        mist,
                        0.78f * HorizontalAlpha[x + 1] *
                        VerticalAlpha[y]);

                    AddQuad(
                        vertexHelper,
                        bottomLeft,
                        topLeft,
                        topRight,
                        bottomRight,
                        colorBottomLeft,
                        colorTopLeft,
                        colorTopRight,
                        colorBottomRight);
                }
            }
        }

        private static void AddAccents(
            VertexHelper vertexHelper,
            Rect rect)
        {
            float centerX = rect.center.x;
            float lineY = rect.center.y - 33f;
            Color gold = new Color(0.78f, 0.58f, 0.25f, 0.5f);
            AddSoftLine(
                vertexHelper,
                new Vector2(centerX - 282f, lineY),
                new Vector2(centerX - 156f, lineY),
                gold);
            AddSoftLine(
                vertexHelper,
                new Vector2(centerX + 156f, lineY),
                new Vector2(centerX + 282f, lineY),
                gold);
            AddSoftDiamond(
                vertexHelper,
                new Vector2(centerX - 143f, lineY),
                3.2f,
                new Color(gold.r, gold.g, gold.b, gold.a * 1.3f));
            AddSoftDiamond(
                vertexHelper,
                new Vector2(centerX + 143f, lineY),
                3.2f,
                new Color(gold.r, gold.g, gold.b, gold.a * 1.3f));
        }

        private static Vector2 SamplePosition(Rect rect, int x, int y)
        {
            return new Vector2(
                rect.center.x + HorizontalSamples[x] * rect.width,
                rect.center.y + VerticalSamples[y] * rect.height);
        }

        private static void AddSoftLine(
            VertexHelper vertexHelper,
            Vector2 from,
            Vector2 to,
            Color color)
        {
            const float halfHeight = 0.65f;
            Color transparent = WithAlpha(color, 0f);
            Color halo = WithAlpha(color, color.a * 0.3f);
            AddQuad(
                vertexHelper,
                new Vector2(from.x, from.y - halfHeight * 2.8f),
                new Vector2(from.x, from.y - halfHeight),
                new Vector2(to.x, to.y - halfHeight),
                new Vector2(to.x, to.y - halfHeight * 2.8f),
                transparent,
                halo,
                halo,
                transparent);
            AddQuad(
                vertexHelper,
                new Vector2(from.x, from.y - halfHeight),
                new Vector2(from.x, from.y + halfHeight),
                new Vector2(to.x, to.y + halfHeight),
                new Vector2(to.x, to.y - halfHeight),
                color,
                color,
                color,
                color);
            AddQuad(
                vertexHelper,
                new Vector2(from.x, from.y + halfHeight),
                new Vector2(from.x, from.y + halfHeight * 2.8f),
                new Vector2(to.x, to.y + halfHeight * 2.8f),
                new Vector2(to.x, to.y + halfHeight),
                halo,
                transparent,
                transparent,
                halo);
        }

        private static void AddSoftDiamond(
            VertexHelper vertexHelper,
            Vector2 center,
            float size,
            Color color)
        {
            int start = vertexHelper.currentVertCount;
            Color transparent = WithAlpha(color, 0f);
            vertexHelper.AddVert(center, color, Vector2.zero);
            vertexHelper.AddVert(
                center + Vector2.up * size, transparent, Vector2.zero);
            vertexHelper.AddVert(
                center + Vector2.right * size, transparent, Vector2.zero);
            vertexHelper.AddVert(
                center + Vector2.down * size, transparent, Vector2.zero);
            vertexHelper.AddVert(
                center + Vector2.left * size, transparent, Vector2.zero);
            vertexHelper.AddTriangle(start, start + 1, start + 2);
            vertexHelper.AddTriangle(start, start + 2, start + 3);
            vertexHelper.AddTriangle(start, start + 3, start + 4);
            vertexHelper.AddTriangle(start, start + 4, start + 1);
        }

        private static void AddQuad(
            VertexHelper vertexHelper,
            Vector2 bottomLeft,
            Vector2 topLeft,
            Vector2 topRight,
            Vector2 bottomRight,
            Color colorBottomLeft,
            Color colorTopLeft,
            Color colorTopRight,
            Color colorBottomRight)
        {
            int start = vertexHelper.currentVertCount;
            vertexHelper.AddVert(bottomLeft, colorBottomLeft, Vector2.zero);
            vertexHelper.AddVert(topLeft, colorTopLeft, Vector2.zero);
            vertexHelper.AddVert(topRight, colorTopRight, Vector2.zero);
            vertexHelper.AddVert(bottomRight, colorBottomRight, Vector2.zero);
            vertexHelper.AddTriangle(start, start + 1, start + 2);
            vertexHelper.AddTriangle(start, start + 2, start + 3);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }
    }
}
