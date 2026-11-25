using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.Effects.Transitions
{
    /// <summary>
    /// 常驻的纸雾网格。只拥有雾团形状和漂移，不读取场景、资源或战斗状态。
    /// 使用 UI 默认材质和顶点渐变，把分散的云团和小片雾粒组织成一层
    /// 从中线向两侧拨开的开场雾；不依赖粒子系统、纹理读回或逐帧对象分配。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleMistGraphic : MaskableGraphic
    {
        private const int CloudCount = 36;
        private const int Segments = 12;
        private const int Rings = 3;
        private float _elapsed;
        private float _reveal;

        public void SetMotion(float elapsed, float reveal)
        {
            _elapsed = elapsed;
            _reveal = Mathf.Clamp01(reveal);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            if (_reveal >= 1f)
                return;

            Rect rect = GetPixelAdjustedRect();
            float opening = _reveal * _reveal * (3f - 2f * _reveal);
            float opacity = 1f - Mathf.SmoothStep(0f, 1f, _reveal);
            for (int index = 0; index < CloudCount; index++)
                AddCloud(vertices, rect, index, opening, opacity);
        }

        private void AddCloud(
            VertexHelper vertices, Rect rect, int index, float opening, float opacity)
        {
            float phase = Hash01(index, 7) * Mathf.PI * 2f;
            // Hash positions instead of laying the puffs on a grid.  A grid
            // produces four horizontal bands that read as a single mask;
            // scattered positions read as small paper-fog particles.
            float normalizedX = Mathf.Lerp(0.04f, 0.96f, Hash01(index, 31));
            float normalizedY = Mathf.Lerp(0.07f, 0.93f, Hash01(index, 37));
            float driftX = Mathf.Sin(_elapsed * 0.46f + phase) * 0.018f;
            float driftY = Mathf.Cos(_elapsed * 0.33f + phase) * 0.014f;
            float side = normalizedX < 0.5f ? -1f : 1f;
            float particleScale = Mathf.Lerp(0.68f, 1.18f, Hash01(index, 29));
            normalizedX += driftX + side * opening * (0.42f + Hash01(index, 11) * 0.22f);
            normalizedY += driftY - opening * (0.04f + Hash01(index, 13) * 0.10f);

            Vector2 center = new Vector2(
                Mathf.LerpUnclamped(rect.xMin, rect.xMax, normalizedX),
                Mathf.LerpUnclamped(rect.yMin, rect.yMax, normalizedY));
            Vector2 radius = new Vector2(
                rect.width * Mathf.Lerp(0.026f, 0.075f, Hash01(index, 17)) * particleScale,
                rect.height * Mathf.Lerp(0.045f, 0.13f, Hash01(index, 19)) * particleScale);
            float cool = Hash01(index, 23);
            Color cloudColor = Color.Lerp(
                new Color(0.62f, 0.73f, 0.78f, 0.50f),
                new Color(0.95f, 0.97f, 0.92f, 0.44f), cool);
            cloudColor *= color;
            cloudColor.a *= opacity;

            int first = vertices.currentVertCount;
            AddVertex(vertices, center, cloudColor);
            for (int ring = 1; ring <= Rings; ring++)
            {
                float distance = ring / (float)Rings;
                Color ringColor = cloudColor;
                ringColor.a *= ring == Rings ? 0f : (ring == 1 ? 0.80f : 0.28f);
                for (int segment = 0; segment < Segments; segment++)
                {
                    float angle = segment * (Mathf.PI * 2f / Segments);
                    float shape = 1f + Mathf.Sin(angle * 3f + phase) * 0.075f;
                    Vector2 point = center + new Vector2(
                        Mathf.Cos(angle) * radius.x,
                        Mathf.Sin(angle) * radius.y) * (distance * shape);
                    AddVertex(vertices, point, ringColor);
                }
            }

            for (int segment = 0; segment < Segments; segment++)
            {
                int next = (segment + 1) % Segments;
                vertices.AddTriangle(first, first + 1 + segment, first + 1 + next);
                for (int ring = 1; ring < Rings; ring++)
                {
                    int inner = first + 1 + (ring - 1) * Segments;
                    int outer = inner + Segments;
                    vertices.AddTriangle(inner + segment, outer + segment, outer + next);
                    vertices.AddTriangle(inner + segment, outer + next, inner + next);
                }
            }
        }

        private static void AddVertex(VertexHelper vertices, Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = Vector2.zero;
            vertices.AddVert(vertex);
        }

        private static float Hash01(int index, int salt)
        {
            uint value = unchecked((uint)(index * 374761393 + salt * 668265263));
            value = (value ^ (value >> 13)) * 1274126177u;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }
    }
}
