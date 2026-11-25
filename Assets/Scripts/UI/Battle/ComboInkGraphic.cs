using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 原毛笔轮廓的网格粒子与沿笔势流动的灵光；高连击以金锋增强。
    /// 粒子使用离线轮廓点云，不渲染数字贴图，也不创建逐粒子对象。
    /// BrushComboView 负责时间，本文只负责把当前状态变成 UI 网格。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ComboInkGraphic : MaskableGraphic
    {
        private readonly List<ComboBrushPointCloud.Point> _targets = new List<ComboBrushPointCloud.Point>();
        private readonly List<ComboBrushPointCloud.Point> _live = new List<ComboBrushPointCloud.Point>();
        private ComboBrushPointCloud.Point[] _origins = System.Array.Empty<ComboBrushPointCloud.Point>();
        private float _originHeight = 1f;
        private float _lastRenderedHeight = 1f;

        private string _glyph = string.Empty;
        private float _progress = 1f;
        private int _tier;
        private bool _dispersing;
        private bool _broken;
        private bool _visible;
        private float _referenceHeight = 180f;

        public void SetReferenceHeight(float height)
        {
            _referenceHeight = Mathf.Max(1f, height);
            SetVerticesDirty();
        }

        public void SetGlyph(
            string glyph,
            float progress,
            int tier,
            bool dispersing,
            bool broken)
        {
            glyph = glyph ?? string.Empty;
            if (_glyph != glyph || _dispersing != dispersing || _broken != broken ||
                (progress <= 0.001f && progress < _progress))
            {
                // Snapshot the actual visible cloud, including an interrupted gather.
                if (CombatLogger.EnableVerboseDiagnostics)
                    TraceGlyph($"Retarget from={_glyph} to={glyph} progress={progress:F3} disperse={dispersing} broken={broken}");
                _origins = _live.ToArray();
                _originHeight = _lastRenderedHeight;
                _targets.Clear();
                for (int digit = 0; digit < glyph.Length; digit++)
                {
                    var samples = ComboBrushPointCloud.Get(glyph[digit]);
                    // Two full layers and a sparse halo stay below UI's vertex limit.
                    int sampleCount = Mathf.Min(samples.Length, 4800 / Mathf.Max(1, glyph.Length));
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        var point = samples[sampleIndex * samples.Length / sampleCount];
                        point.Position.x += (digit - (glyph.Length - 1f) * 0.5f) * 0.64f;
                        _targets.Add(point);
                    }
                }
            }
            if (CombatLogger.EnableVerboseDiagnostics && progress >= 0.34f && _progress < 0.34f && !dispersing)
                TraceGlyph($"GatherComplete target={glyph}");
            _glyph = glyph;
            _progress = Mathf.Clamp01(progress);
            _tier = Mathf.Clamp(tier, 0, 3);
            _dispersing = dispersing;
            _broken = broken;
            _visible = !string.IsNullOrEmpty(_glyph);
            SetVerticesDirty();
        }

        public void ClearGlyph()
        {
            if (!_visible && string.IsNullOrEmpty(_glyph))
                return;
            TraceGlyph("Clear");
            _visible = false;
            _glyph = string.Empty;
            _targets.Clear();
            _live.Clear();
            _origins = System.Array.Empty<ComboBrushPointCloud.Point>();
            SetVerticesDirty();
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void TraceGlyph(string action)
        {
            if (!CombatLogger.EnableVerboseDiagnostics) return;
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log($"[ComboTrace][Glyph] frame={Time.frameCount} t={Time.unscaledTime:F3} " +
                    $"ink={GetInstanceID()} action={action} previousProgress={_progress:F3} " +
                    $"glyph={_glyph} visible={_visible} active={isActiveAndEnabled} " +
                    $"origins={_origins.Length} targets={_targets.Count} live={_live.Count}", this);
        }

        // 保留旧的窄接口，避免已有预制体或手工调试脚本失去编译入口。
        public void SetPulse(float progress, int tier, bool retract)
        {
            SetGlyph(_glyph, progress, tier, retract, false);
        }

        public void ClearPulse()
        {
            ClearGlyph();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!_visible || string.IsNullOrEmpty(_glyph))
                return;

            Rect rect = rectTransform.rect;
            if (rect.width < 1f || rect.height < 1f)
                return;

            float height = Mathf.Min(rect.height * (5f / 6f), _referenceHeight * (5f / 6f));
            height = Mathf.Min(height, rect.width / Mathf.Max(0.67f, _glyph.Length * 0.64f));
            float cell = height / 7f;
            DrawMilestoneOrbit(vh, rect, cell);
            DrawLandingStroke(vh, rect, cell);

            // Stamp/retract gets a short-lived burst of ink motes.  These are
            // deliberately drawn as UI geometry so the effect remains visible
            // in ScreenSpaceOverlay canvases (a regular ParticleSystem renderer
            // would otherwise be sorted behind the HUD on some platforms).
            DrawBurstParticles(vh, rect, cell);
            DrawBrushCloud(vh, rect.center, height);
            if (_broken)
                DrawBreakSlash(vh, rect, cell);
        }

        private void DrawBurstParticles(VertexHelper vh, Rect rect, float cell)
        {
            // Broad directional spray with staggered heads and long fading tails.
            // Keep its centre hollow so the numeral remains the focal point.
            float travel = _dispersing ? Smooth(_progress)
                : Smooth(Mathf.Clamp01((_progress - 0.14f) / 0.76f));
            float motion = 1f - travel;
            float alpha = _dispersing
                ? motion * (0.56f + _tier * 0.08f)
                : Mathf.Clamp01((_progress - 0.12f) * 18f) * motion *
                    (0.78f + _tier * 0.07f);
            if (alpha <= 0.01f)
                return;

            int count = 28 + _tier * 12;
            Vector2 center = rect.center;
            float radiusX = Mathf.Max(cell * 6.5f, rect.width * 0.52f);
            float radiusY = Mathf.Max(cell * 4f, rect.height * 0.46f);
            for (int i = 0; i < count; i++)
            {
                float seed = Hash01(401 + i * 29 + _tier * 13);
                float angle = -0.65f + seed * 1.65f + (i % 2) * Mathf.PI;
                float ring = 0.52f + Hash01(773 + i * 17) * 0.52f;
                Vector2 target = center + new Vector2(
                    Mathf.Cos(angle) * radiusX * ring,
                    Mathf.Sin(angle) * radiusY * ring);
                Vector2 position = Vector2.Lerp(center, target,
                    Mathf.Lerp(0.38f, 1f, travel));
                float size = cell * (0.075f + seed * 0.12f + _tier * 0.016f);
                Color tint = CoreColor(_tier);
                tint.a = alpha * (0.45f + seed * 0.55f);
                Vector2 tangent = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float length = size * (7f + _tier * 1.4f) * (0.35f + motion * 0.65f);
                Vector2 tail = position - tangent * length;
                Color halo = tint;
                halo.a *= 0.18f;
                AddRibbon(vh, tail, position, size * 0.12f, size * 1.4f, halo);
                // Four tapering sections fade toward the tail rather than
                // leaving a uniformly opaque bar behind every particle.
                for (int segment = 0; segment < 4; segment++)
                {
                    float t = segment / 4f;
                    float next = (segment + 1f) / 4f;
                    Color trail = tint;
                    trail.a *= 0.15f + next * 0.65f;
                    AddRibbon(vh, Vector2.Lerp(tail, position, t),
                        Vector2.Lerp(tail, position, next),
                        size * t * 0.46f, size * next * 0.46f, trail);
                }
                Color head = Color.Lerp(tint, Color.white, 0.75f);
                head.a = tint.a;
                AddRibbon(vh, position - tangent * size * 1.5f,
                    position + tangent * size * 0.8f, size * 0.42f, 0f, head);
            }
        }

        private void DrawLandingStroke(VertexHelper vh, Rect rect, float cell)
        {
            if (_dispersing || _progress >= 0.60f) return;
            float strike = Mathf.Clamp01((_progress - 0.08f) / 0.52f);
            float flash = Mathf.Clamp01(strike * 8f) * Mathf.Pow(1f - strike, 2f);
            if (flash <= 0.001f) return;
            Vector2 direction = new Vector2(1f, 0.18f).normalized;
            float reach = Mathf.Min(rect.width * 0.47f, cell * 6.8f);
            Vector2 a = rect.center - direction * reach;
            Vector2 b = rect.center + direction * reach * Mathf.Lerp(0.5f, 1f, strike);
            Color ink = new Color(0.012f, 0.035f, 0.04f, flash * 0.72f);
            AddRibbon(vh, a, b, cell * (0.26f + _tier * 0.045f), 0f, ink);
            Color glow = CoreColor(_tier);
            glow.a = flash * (0.20f + _tier * 0.055f);
            AddRibbon(vh, a, b, cell * 0.34f, 0f, glow);
            Color edge = Color.Lerp(CoreColor(_tier), Color.white, 0.60f);
            edge.a = flash * 0.90f;
            AddRibbon(vh, a, b, cell * (0.025f + _tier * 0.009f), 0f, edge);
        }

        private void DrawBrushCloud(VertexHelper vh, Vector2 center, float height)
        {
            int count = _dispersing ? _origins.Length : Mathf.Max(_targets.Count, _origins.Length);
            _live.Clear();
            float gather = Smooth(Mathf.Clamp01(_progress / 0.34f));
            float scatter = Smooth(_progress);
            for (int i = 0; i < count; i++)
            {
                float seed = Hash01(i * 71 + 19);
                bool hasTarget = i < _targets.Count;
                var target = hasTarget ? _targets[i] : _origins[i];
                var origin = i < _origins.Length ? _origins[i] : target;
                if (i < _origins.Length)
                    origin.Position *= _originHeight / Mathf.Max(0.001f, height);
                if (!hasTarget)
                    target = origin;
                if (i >= _origins.Length)
                {
                    origin.Position += new Vector2(-0.24f - seed * 0.30f,
                        (seed - 0.5f) * 0.28f);
                    origin.Alpha = 0f;
                    origin.Stretch = 2f + seed * 3f;
                }
                var point = target;
                if (_dispersing)
                {
                    point = origin;
                    point.Position += new Vector2(0.24f + seed * 0.48f,
                        (seed - 0.42f) * 0.46f) * scatter * (_broken ? 1f : -0.45f);
                    point.Alpha *= 1f - scatter;
                    point.Stretch = origin.Stretch + Mathf.Sin(scatter * Mathf.PI) * (2f + seed * 3f);
                }
                else
                {
                    if (!hasTarget)
                    {
                        target.Position += new Vector2(0.25f + seed * 0.30f, (seed - 0.5f) * 0.4f);
                        target.Alpha = 0f;
                    }
                    // Curved travel joins the source and destination with zero
                    // offset at both ends; no teleport when a hit interrupts it.
                    point.Position = Vector2.Lerp(origin.Position, target.Position, gather);
                    point.Position.y += Mathf.Sin(gather * Mathf.PI) * (seed - 0.5f) * 0.10f;
                    point.Alpha = Mathf.Lerp(origin.Alpha, target.Alpha, gather);
                    point.Luminance = Mathf.Lerp(origin.Luminance, target.Luminance, gather);
                    Vector2 targetDirection = Vector2.Dot(origin.Direction, target.Direction) < 0f
                        ? -target.Direction : target.Direction;
                    point.Direction = Vector2.Lerp(origin.Direction, targetDirection, gather).normalized;
                    point.Stretch = Mathf.Lerp(origin.Stretch, 0f, gather) +
                        Mathf.Sin(gather * Mathf.PI) * (1f + seed * 2f);
                }
                _live.Add(point);
            }
            // Draw complete layers, not per-point outlines: a later particle's
            // dark edge must never overwrite an already rendered bright stroke.
            DrawCloudLayer(vh, center, height, true);
            DrawCloudLayer(vh, center, height, false);
            _lastRenderedHeight = height;
        }

        private void DrawCloudLayer(VertexHelper vh, Vector2 center, float height, bool underlay)
        {
            float unit = height / 96f;
            Color body = Color.Lerp(CoreColor(_tier), Color.white, 0.84f);
            for (int i = 0; i < _live.Count; i++)
            {
                var point = _live[i];
                if (point.Alpha < 0.005f) continue;
                Vector2 position = center + point.Position * height;
                Vector2 direction = point.Direction.sqrMagnitude > 0.01f
                    ? point.Direction : Vector2.up;
                float length = unit * (0.72f + point.Stretch * 0.70f);
                float width = unit * 0.58f;
                if (underlay)
                {
                    // Thin, continuous ink separation on bright backgrounds.
                    Color ink = new Color(0.025f, 0.045f, 0.045f, point.Alpha * 0.68f);
                    float edge = unit * 0.30f;
                    AddRibbon(vh, position - direction * (length + edge),
                        position + direction * (length + edge), width + edge, width + edge, ink);
                    if (i % 4 == 0)
                    {
                        Color glow = CoreColor(_tier);
                        glow.a = point.Alpha * 0.055f / (1f + point.Stretch);
                        AddRibbon(vh, position - direction * (length + unit),
                            position + direction * (length + unit), width + unit, width + unit, glow);
                    }
                    continue;
                }
                // Source luminance supplies restrained dry-brush transparency,
                // not hundreds of muddy grey/black pigment colours.
                Color pigment = body;
                pigment.a = point.Alpha * Mathf.Lerp(0.80f, 1f, Smooth(point.Luminance));
                AddRibbon(vh, position - direction * length,
                    position + direction * length, width, width * 0.80f, pigment);
            }
        }

        private static void AddRibbon(VertexHelper vh, Vector2 a, Vector2 b,
            float wa, float wb, Color tint)
        {
            Vector2 delta = b - a;
            if (delta.sqrMagnitude < 0.0001f) return;
            Vector2 n = new Vector2(-delta.y, delta.x).normalized;
            int v = vh.currentVertCount;
            vh.AddVert(a + n * wa, tint, Vector2.zero);
            vh.AddVert(b + n * wb, tint, Vector2.zero);
            vh.AddVert(b - n * wb, tint, Vector2.zero);
            vh.AddVert(a - n * wa, tint, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v, v + 2, v + 3);
        }

        private void DrawMilestoneOrbit(VertexHelper vh, Rect rect, float cell)
        {
            if (_dispersing || _progress >= 0.999f) return;
            float travel = Mathf.Clamp01((_progress - 0.10f) / 0.82f);
            float envelope = Mathf.Clamp01(travel * 7f) * Mathf.Pow(1f - travel, 1.4f);
            for (int arc = 0; arc < 2 + Mathf.Min(_tier, 2); arc++)
            {
                const int segments = 28;
                float radiusX = Mathf.Min(rect.width * 0.50f, cell * 7f);
                float radiusY = cell * (3.2f + arc * 0.38f) * Mathf.Lerp(0.80f, 1.14f, travel);
                float startAngle = -2.8f + arc * 2.1f + travel * (1.8f + _tier * 0.18f);
                for (int i = 0; i < segments; i++)
                {
                    float t = i / (float)segments;
                    float next = (i + 1f) / segments;
                    float arcLength = 1.65f + _tier * 0.18f;
                    float angle = startAngle + t * arcLength;
                    float nextAngle = startAngle + next * arcLength;
                    Vector2 p = rect.center + new Vector2(
                        Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                    Vector2 q = rect.center + new Vector2(
                        Mathf.Cos(nextAngle) * radiusX, Mathf.Sin(nextAngle) * radiusY);
                    p.y += (p.x - rect.center.x) * 0.19f;
                    q.y += (q.x - rect.center.x) * 0.19f;
                    Color tint = CoreColor(_tier);
                    tint.a = envelope * Mathf.Sin(t * Mathf.PI) * (0.65f + _tier * 0.08f);
                    float width = cell * (0.065f + _tier * 0.024f);
                    Color halo = tint;
                    halo.a *= 0.22f;
                    AddRibbon(vh, p, q, width * t * 5f, width * next * 5f, halo);
                    AddRibbon(vh, p, q, width * t, width * next, tint);
                    if (_tier >= 2)
                    {
                        Color core = Color.Lerp(tint, Color.white, 0.65f);
                        core.a = tint.a * 0.85f;
                        AddRibbon(vh, p, q, width * t * 0.25f,
                            width * next * 0.25f, core);
                    }
                }
            }
        }

        private void DrawBreakSlash(VertexHelper vh, Rect rect, float cell)
        {
            float alpha = (1f - Smooth(_progress)) * (0.54f + _tier * 0.07f);
            if (alpha <= 0.01f)
                return;

            Vector2 start = rect.center + new Vector2(-rect.width * 0.34f, rect.height * 0.26f);
            Vector2 end = rect.center + new Vector2(rect.width * 0.34f, -rect.height * 0.26f);
            Color under = new Color(0.04f, 0.01f, 0.008f, alpha * 0.78f);
            Color ink = new Color(1f, 0.30f, 0.16f, alpha);
            AddSegment(vh, start, end, cell * 0.13f, under);
            AddSegment(vh, start, end, cell * 0.055f, ink);
        }


        private static Color CoreColor(int tier)
        {
            switch (Mathf.Clamp(tier, 0, 3))
            {
                case 1: return new Color(0.68f, 0.94f, 0.83f, 1f);
                case 2: return new Color(0.94f, 0.76f, 0.40f, 1f);
                case 3: return new Color(1.00f, 0.89f, 0.61f, 1f);
                default: return new Color(0.58f, 0.85f, 0.79f, 1f);
            }
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static float Hash01(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return (x & 0x00ffffffu) / 16777215f;
            }
        }


        private static void AddSegment(
            VertexHelper vh,
            Vector2 start,
            Vector2 end,
            float width,
            Color tint)
        {
            Vector2 delta = end - start;
            if (delta.sqrMagnitude < 0.0001f)
                return;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * width;
            int index = vh.currentVertCount;
            vh.AddVert(start + normal, tint, Vector2.zero);
            vh.AddVert(end + normal, tint, Vector2.zero);
            vh.AddVert(end - normal, tint, Vector2.zero);
            vh.AddVert(start - normal, tint, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
