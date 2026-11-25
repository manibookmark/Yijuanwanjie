using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>Managed presentation of a projected cycle; owns no gameplay counter.</summary>
    public sealed class TianjiCycleRing : MaskableGraphic
    {
        private float _progress;

        public void ShowProgress(float progress)
        {
            _progress = Mathf.Clamp01(progress);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect bounds = rectTransform.rect;
            float outer = Mathf.Min(bounds.width, bounds.height) * .5f;
            float inner = Mathf.Max(0, outer - 3);
            Color active = Color.Lerp(new Color(.36f, .66f, .60f, 1),
                new Color(.88f, .73f, .43f, 1), Mathf.InverseLerp(.9f, 1, _progress));
            for (int i = 0; i < RunSegmentCount; i++)
            {
                float a = Mathf.PI * .5f - i * Mathf.PI * 2 / RunSegmentCount;
                float b = Mathf.PI * .5f - (i + .85f) * Mathf.PI * 2 / RunSegmentCount;
                Color tint = i < Mathf.RoundToInt(_progress * RunSegmentCount)
                    ? active : new Color(.36f, .48f, .44f, .22f);
                int offset = mesh.currentVertCount;
                Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 v = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                mesh.AddVert(bounds.center + u * outer, tint, Vector2.zero);
                mesh.AddVert(bounds.center + v * outer, tint, Vector2.zero);
                mesh.AddVert(bounds.center + v * inner, tint, Vector2.zero);
                mesh.AddVert(bounds.center + u * inner, tint, Vector2.zero);
                mesh.AddTriangle(offset, offset + 1, offset + 2);
                mesh.AddTriangle(offset, offset + 2, offset + 3);
            }
        }

        private const int RunSegmentCount = WanjieScroll.Core.RunCardPlayGrowth.RealPlaysPerCycle;
    }
}
