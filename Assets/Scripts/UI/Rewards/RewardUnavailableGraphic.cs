using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Rewards
{
    /// <summary>通用不可用状态符号；只拥有自身几何，不依赖业务原因或贴图。</summary>
    public sealed class RewardUnavailableGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.42f;
            float thickness = radius * 0.07f;
            const int segments = 96;
            for (int i = 0; i < segments; i++)
            {
                float first = i * Mathf.PI * 2f / segments;
                float next = (i + 1) * Mathf.PI * 2f / segments;
                Vector2 a = new Vector2(Mathf.Cos(first), Mathf.Sin(first));
                Vector2 b = new Vector2(Mathf.Cos(next), Mathf.Sin(next));
                Quad(mesh, center + a * radius, center + b * radius,
                    center + b * (radius - thickness), center + a * (radius - thickness));
            }
            float halfWidth = radius * 0.42f;
            Quad(mesh, center + new Vector2(-halfWidth, -thickness),
                center + new Vector2(halfWidth, -thickness),
                center + new Vector2(halfWidth, thickness),
                center + new Vector2(-halfWidth, thickness));
        }

        private void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero);
            mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero);
            mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
