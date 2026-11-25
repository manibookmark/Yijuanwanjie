using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.UI.Views
{
    /// <summary>Canvas-only fine sand geometry. Its caller owns timing and
    /// resource/card arrival; these three visible streams never apply gains.</summary>
    public sealed class SandSpiritTransferGraphic : MaskableGraphic
    {
        private Vector2 _source, _target;
        private float _progress;
        private bool _condensing;

        public void SetPath(Vector2 source, Vector2 target, float progress, bool condensing = false)
        {
            _source = source; _target = target;
            _progress = Mathf.Clamp01(progress); _condensing = condensing;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!_condensing && _progress < 0.22f)
            {
                Color rune = new Color(0.42f, 0.8f, 0.86f,
                    Mathf.Sin(_progress / 0.22f * Mathf.PI) * 0.85f);
                for (int i = 0; i < 15; i++)
                    Quad(vh, _source + new Vector2(i < 9 ? 0f : (i - 11) * 4f,
                        i < 9 ? (i - 4) * 4f : 4f), 1.1f, rune);
            }
            for (int stream = 0; stream < 3; stream++)
            {
                for (int grain = 0; grain < 38; grain++)
                {
                    float seed = grain * 2.39996f + stream * 1.73f;
                    float phase = grain / 38f;
                    float opacity;
                    Vector2 point;
                    if (_condensing)
                    {
                        float radius = Mathf.Lerp(100f, 5f, _progress);
                        point = _source + new Vector2(Mathf.Cos(seed), Mathf.Sin(seed)) *
                            radius * (0.45f + phase * 0.55f);
                        opacity = Mathf.Sin(_progress * Mathf.PI);
                    }
                    else
                    {
                        float travel = Mathf.Clamp01((_progress - 0.1f - phase * 0.1f) / 0.75f);
                        Vector2 bend = Vector2.up * Mathf.Sin(travel * Mathf.PI) * (58f + stream * 24f);
                        Vector2 jitter = new Vector2(Mathf.Sin(seed + _progress * 15f),
                            Mathf.Cos(seed + _progress * 13f)) * (2f + phase * 6f);
                        point = Vector2.Lerp(_source, _target, travel) + bend + jitter;
                        opacity = Mathf.Sin(travel * Mathf.PI) * (0.5f + phase * 0.5f);
                    }
                    Color tint = grain % 12 == 0
                        ? new Color(0.4f, 0.8f, 0.9f, opacity * 0.72f)
                        : new Color(0.92f, 0.77f, 0.46f, opacity);
                    Quad(vh, point, 0.8f + (grain % 4) * 0.55f, tint);
                }
            }
        }

        private static void Quad(VertexHelper vh, Vector2 p, float size, Color tint)
        {
            int first = vh.currentVertCount;
            vh.AddVert(p + new Vector2(-size, -size), tint, Vector2.zero);
            vh.AddVert(p + new Vector2(size, -size), tint, Vector2.right);
            vh.AddVert(p + new Vector2(size, size), tint, Vector2.one);
            vh.AddVert(p + new Vector2(-size, size), tint, Vector2.up);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
