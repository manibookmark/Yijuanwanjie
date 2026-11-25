using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>A quiet sand layer under the resident. It owns only its canvas
    /// grains; the resident owner supplies composed entry/exit visibility.</summary>
    public sealed class CrawlerSandPresenceGraphic : MaskableGraphic
    {
        private float _clock;
        private float _opacity;
        private float _formation = 1f;
        public void SetPresence(float opacity, float formation = 1f)
        { _opacity = Mathf.Clamp01(opacity); _formation = Mathf.Clamp01(formation); SetVerticesDirty(); }
        protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
        private void Update() { _clock += Time.unscaledDeltaTime; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int count = _formation < 1f ? 170 : 52;
            for (int i = 0; i < count; i++)
            {
                float cycle = Mathf.Repeat(_clock * 0.3f + i * 0.618034f, 1f);
                float angle = i * 2.39996f + _clock * 0.2f;
                Vector2 p = new Vector2(Mathf.Cos(angle) * (24f + i % 9 * 4f),
                    -rectTransform.rect.height * 0.4f + Mathf.Sin(angle) * 9f + cycle * 32f);
                if (_formation < 1f)
                {
                    float gather = Mathf.SmoothStep(0f, 1f, _formation);
                    float height = rectTransform.rect.height;
                    float along = (i % 31) / 30f;
                    Vector2 silhouette = new Vector2(Mathf.Sin(angle) * (8f + Mathf.Sin(along * Mathf.PI) * 16f),
                        (along - 0.45f) * height * 0.86f);
                    Vector2 scattered = new Vector2(Mathf.Cos(angle) * height * 0.65f,
                        Mathf.Sin(angle) * height * 0.65f);
                    p = Vector2.Lerp(scattered, silhouette, gather);
                }
                float size = 0.5f + i % 4 * 0.3f;
                Color tint = i % 13 == 0 ? new Color(0.38f, 0.72f, 0.8f) : new Color(0.87f, 0.72f, 0.44f);
                tint.a = Mathf.Sin(cycle * Mathf.PI) * _opacity * (_formation < 1f ? 0.8f : 0.55f);
                int v = vh.currentVertCount;
                vh.AddVert(p + new Vector2(-size, -size), tint, Vector2.zero);
                vh.AddVert(p + new Vector2(size, -size), tint, Vector2.right);
                vh.AddVert(p + new Vector2(size, size), tint, Vector2.one);
                vh.AddVert(p + new Vector2(-size, size), tint, Vector2.up);
                vh.AddTriangle(v, v + 1, v + 2); vh.AddTriangle(v, v + 2, v + 3);
            }
        }
    }
}
