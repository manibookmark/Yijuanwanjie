using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects
{
    public enum TalismanPaperTier { Plain, Patterned, Gold }
    public enum TalismanInkMotif { Unfold, Mana, Fire }

    /// <summary>
    /// Owns one translucent spirit sheet and its three-stage brushwork. The long
    /// axis is local X; callers own their host, rotation, clock and transfers.
    /// Rim, ink, tip, opacity and cleanup all belong to this child hierarchy.
    /// </summary>
    public sealed class TalismanPaperVisual
    {
        private GameObject _root;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<InkStroke> _ink = new List<InkStroke>();
        private Renderer[] _renderers;
        private LineRenderer _tip;
        private MaterialPropertyBlock _opacity;
        private float _paperWidth;
        private int _baseOrder;
        private static Material _material;
        private static readonly int TintId = Shader.PropertyToID("_Color");

        public static Vector2 GetBodySize(VfxProfile profile, TalismanInkMotif motif)
            => new Vector2(0.98f, 0.41f * PaperWidth(profile, motif));

        public void Initialize(Transform parent, VfxProfile profile,
            TalismanInkMotif motif, int baseSortingOrder)
        {
            Cleanup();
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            try
            {
                _root = new GameObject("SpiritTalismanBody");
                _root.transform.SetParent(parent, false);
                _paperWidth = PaperWidth(profile, motif);
                _baseOrder = baseSortingOrder;
                TalismanPaperTier tier = profile != null ? profile.talismanPaperTier : TalismanPaperTier.Plain;
                bool gold = tier == TalismanPaperTier.Gold;
                Color ink;
                Color core;
                if (motif == TalismanInkMotif.Fire)
                {
                    ink = new Color(1f, 0.3f, 0.065f, 1f);
                    core = profile != null ? profile.fireTalismanGlyphCoreColor : new Color(1f, 0.8f, 0.35f, 1f);
                    core = Color.Lerp(core, gold ? new Color(1f, 0.98f, 0.8f) : new Color(1f, 0.88f, 0.55f), 0.38f);
                }
                else
                {
                    Color fallback = motif == TalismanInkMotif.Unfold
                        ? new Color(0.15f, 0.82f, 0.69f) : new Color(0.2f, 0.7f, 1f);
                    Color primary = profile != null ? profile.selfEffectPrimaryColor : fallback;
                    Color light = profile != null ? profile.selfEffectSecondaryColor : new Color(0.82f, 1f, 0.94f);
                    ink = Color.Lerp(primary, light, 0.22f);
                    core = Color.Lerp(light, Color.white, 0.42f);
                }
                ink.a = core.a = 1f;
                Color face = motif == TalismanInkMotif.Fire ? new Color(0.38f, 0.84f, 0.8f)
                    : Color.Lerp(ink, new Color(0.52f, 0.93f, 0.91f), 0.6f);
                Color rim = motif == TalismanInkMotif.Fire ? new Color(0.62f, 0.98f, 0.9f)
                    : Color.Lerp(ink, core, 0.56f);
                CreateSpiritSheet(face, rim, tier);
                if (motif == TalismanInkMotif.Unfold) CreateUnfoldInk(ink, core);
                else if (motif == TalismanInkMotif.Mana) CreateManaInk(ink, core);
                else CreateFireInk(ink, core);
                _tip = Line("WritingSpiritTip", core, gold ? 0.042f : 0.038f,
                    new[] { new Vector3(-0.005f, 0f), new Vector3(0.005f, 0f) }, false);
                _tip.numCapVertices = 8;
                _tip.sortingOrder = _baseOrder + 5;
                _renderers = _root.GetComponentsInChildren<Renderer>(true);
                _opacity = new MaterialPropertyBlock();
                SetStrokeReveal(0f);
                SetOpacity(1f);
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        private static float PaperWidth(VfxProfile profile, TalismanInkMotif motif)
            => motif == TalismanInkMotif.Fire
                ? Mathf.Clamp(profile != null ? profile.fireTalismanPaperWidth : 1f, 0.75f, 1.6f) : 1.15f;

        private static Vector3 PaperPoint(Vector2 point, float width)
            => new Vector3(point.y, -point.x * width, 0f);

        private void CreateSpiritSheet(Color face, Color rim, TalismanPaperTier tier)
        {
            // A talisman is a tall strip with a flat head and foot. Keep only
            // small cut corners; rounded ends made the spirit sheet look like
            // a badge instead of a paper charm.
            Vector3[] edge =
            {
                PaperPoint(new Vector2(-0.171f, 0.48f), _paperWidth),
                PaperPoint(new Vector2(0.171f, 0.48f), _paperWidth),
                PaperPoint(new Vector2(0.18f, 0.47f), _paperWidth),
                PaperPoint(new Vector2(0.18f, -0.47f), _paperWidth),
                PaperPoint(new Vector2(0.171f, -0.48f), _paperWidth),
                PaperPoint(new Vector2(-0.171f, -0.48f), _paperWidth),
                PaperPoint(new Vector2(-0.18f, -0.47f), _paperWidth),
                PaperPoint(new Vector2(-0.18f, 0.47f), _paperWidth)
            };
            bool gold = tier == TalismanPaperTier.Gold;
            float edgeAlpha = gold ? 0.28f : 0.20f;
            var vertices = new Vector3[edge.Length + 1];
            var colors = new Color[vertices.Length];
            var triangles = new int[edge.Length * 3];
            colors[0] = new Color(face.r, face.g, face.b, gold ? 0.19f : 0.14f);
            for (int i = 0; i < edge.Length; i++)
            {
                vertices[i + 1] = edge[i];
                float light = 0.92f + Mathf.Clamp01((edge[i].x + 0.48f) / 0.96f) * 0.08f;
                colors[i + 1] = new Color(face.r * light, face.g * light, face.b * light, edgeAlpha);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % edge.Length + 1;
            }
            AddMesh("TranslucentSpiritSheet", vertices, colors, triangles, _baseOrder);

            // Feather only the thin physical rim. No external halo is created.
            vertices = new Vector3[edge.Length * 2];
            colors = new Color[vertices.Length];
            triangles = new int[edge.Length * 6];
            for (int i = 0; i < edge.Length; i++)
            {
                vertices[i * 2] = edge[i];
                vertices[i * 2 + 1] = new Vector3(edge[i].x * 1.015f, edge[i].y * 1.035f, 0f);
                colors[i * 2] = new Color(rim.r, rim.g, rim.b, 0.12f);
                colors[i * 2 + 1] = new Color(rim.r, rim.g, rim.b, 0f);
                int next = (i + 1) % edge.Length * 2;
                int index = i * 6;
                triangles[index] = i * 2;
                triangles[index + 1] = next;
                triangles[index + 2] = i * 2 + 1;
                triangles[index + 3] = i * 2 + 1;
                triangles[index + 4] = next;
                triangles[index + 5] = next + 1;
            }
            AddMesh("NarrowSpiritRimFeather", vertices, colors, triangles, _baseOrder - 1);
            rim.a = gold ? 0.80f : 0.64f;
            var outline = Line("CompleteSpiritOutline", rim, gold ? 0.011f : 0.009f, edge, false);
            outline.loop = true;
            outline.sortingOrder = _baseOrder + 1;
            if (tier != TalismanPaperTier.Plain) CreateTierMarks(tier);
        }

        private void AddMesh(string name, Vector3[] vertices, Color[] colors, int[] triangles, int order)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices, colors = colors, triangles = triangles, uv = new Vector2[vertices.Length] };
            _meshes.Add(mesh);
            mesh.RecalculateBounds();
            var child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            child.transform.SetParent(_root.transform, false);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial();
            renderer.sortingOrder = order;
        }

        private void CreateTierMarks(TalismanPaperTier tier)
        {
            bool gold = tier == TalismanPaperTier.Gold;
            Color color = gold ? new Color(1f, 0.87f, 0.4f, 0.96f) : new Color(1f, 0.45f, 0.16f, 0.78f);
            for (int side = -1; side <= 1; side += 2)
                for (int end = -1; end <= 1; end += 2)
                {
                    Line("TierCornerSeal", color, gold ? 0.012f : 0.008f,
                        SamplePath(new[] { new Vector2(side * 0.075f, end * 0.413f),
                            new Vector2(side * 0.105f, end * 0.443f), new Vector2(side * 0.159f, end * 0.416f),
                            new Vector2(side * 0.135f, end * 0.366f) }), true).sortingOrder = _baseOrder + 1;
                    if (gold)
                        Line("GoldSealReturn", color, 0.007f,
                            SamplePath(new[] { new Vector2(side * 0.087f, end * 0.407f),
                                new Vector2(side * 0.124f, end * 0.392f), new Vector2(side * 0.115f, end * 0.371f),
                                new Vector2(side * 0.143f, end * 0.354f) }), true).sortingOrder = _baseOrder + 1;
                }
        }

        private void CreateUnfoldInk(Color ink, Color core)
        {
            AddInk("UnfoldCrown", ink, core, 0.02f,
                new Vector2(-0.13f, 0.29f), new Vector2(-0.12f, 0.4f), new Vector2(-0.035f, 0.37f),
                new Vector2(0f, 0.305f), new Vector2(0.035f, 0.37f), new Vector2(0.12f, 0.4f), new Vector2(0.13f, 0.29f));
            AddInk("UnfoldSealBelly", ink, core, 0.024f,
                new Vector2(0f, 0.27f), new Vector2(-0.025f, 0.225f), new Vector2(-0.02f, 0.16f),
                new Vector2(-0.115f, 0.155f), new Vector2(-0.15f, 0.09f), new Vector2(-0.045f, 0.095f),
                new Vector2(0f, 0.04f), new Vector2(0.045f, 0.095f), new Vector2(0.15f, 0.09f),
                new Vector2(0.115f, 0.155f), new Vector2(0.035f, 0.175f), new Vector2(0.005f, 0.1f),
                new Vector2(0.005f, -0.04f), new Vector2(0.005f, -0.105f), new Vector2(-0.04f, -0.175f), new Vector2(0.05f, -0.21f));
            AddInk("UnfoldSealFoot", ink, core, 0.022f,
                new Vector2(0.05f, -0.215f), new Vector2(-0.015f, -0.22f), new Vector2(-0.125f, -0.25f),
                new Vector2(-0.085f, -0.315f), new Vector2(-0.04f, -0.345f), new Vector2(0.095f, -0.275f),
                new Vector2(0.11f, -0.33f), new Vector2(0.09f, -0.38f), new Vector2(0.01f, -0.405f), new Vector2(0f, -0.35f));
        }

        private void CreateManaInk(Color ink, Color core)
        {
            AddInk("ManaCloudCrown", ink, core, 0.02f,
                new Vector2(-0.13f, 0.3f), new Vector2(-0.11f, 0.4f), new Vector2(-0.025f, 0.38f),
                new Vector2(0f, 0.315f), new Vector2(0.035f, 0.38f), new Vector2(0.115f, 0.39f), new Vector2(0.14f, 0.29f));
            AddInk("ManaFlowBelly", ink, core, 0.024f,
                new Vector2(0.04f, 0.27f), new Vector2(-0.18f, 0.18f), new Vector2(-0.13f, 0.04f),
                new Vector2(0.08f, 0.06f), new Vector2(0.18f, 0.005f), new Vector2(0.04f, -0.12f),
                new Vector2(-0.09f, -0.13f), new Vector2(-0.18f, -0.17f), new Vector2(-0.025f, -0.24f), new Vector2(0.095f, -0.235f));
            AddInk("ManaSealFoot", ink, core, 0.022f,
                new Vector2(-0.11f, -0.275f), new Vector2(-0.04f, -0.315f), new Vector2(0.045f, -0.24f),
                new Vector2(0.11f, -0.285f), new Vector2(0.11f, -0.335f), new Vector2(-0.075f, -0.32f),
                new Vector2(-0.06f, -0.38f), new Vector2(-0.015f, -0.41f), new Vector2(0.08f, -0.395f), new Vector2(0.1f, -0.35f));
        }

        private void CreateFireInk(Color ink, Color core)
        {
            // Two recognizable flame-shaped sigils are preserved, now written
            // as crown, connected belly and one coherent seal-foot gesture.
            Vector2[] flame =
            {
                new Vector2(0.006f, 0.355f), new Vector2(-0.01f, 0.308f),
                new Vector2(-0.073f, 0.299f), new Vector2(-0.058f, 0.247f),
                new Vector2(-0.046f, 0.264f), new Vector2(-0.027f, 0.28f),
                new Vector2(-0.027f, 0.298f), new Vector2(-0.014f, 0.248f),
                new Vector2(-0.09f, 0.224f), new Vector2(-0.075f, 0.185f),
                new Vector2(-0.061f, 0.147f), new Vector2(0.032f, 0.141f),
                new Vector2(0.053f, 0.181f), new Vector2(0.079f, 0.228f),
                new Vector2(0.038f, 0.265f), new Vector2(0.033f, 0.293f),
                new Vector2(0.013f, 0.264f), new Vector2(0.005f, 0.299f), new Vector2(0.006f, 0.355f)
            };
            AddInk("FireCrown", ink, core, 0.02f, flame);
            var belly = new Vector2[flame.Length + 3];
            belly[0] = new Vector2(0f, 0.16f);
            belly[1] = new Vector2(0.02f, 0.145f);
            belly[2] = new Vector2(-0.016f, 0.13f);
            for (int i = 0; i < flame.Length; i++)
                belly[i + 3] = new Vector2(flame[i].x * 1.25f, (flame[i].y - 0.355f) * 1.32f + 0.104f);
            AddInk("FireSealBelly", ink, core, 0.024f, belly);
            AddInk("FireSealFoot", ink, core, 0.022f,
                new Vector2(0.015f, -0.15f), new Vector2(0.1f, -0.19f), new Vector2(0.16f, -0.225f),
                new Vector2(0.06f, -0.25f), new Vector2(-0.12f, -0.3f), new Vector2(-0.12f, -0.345f),
                new Vector2(0.03f, -0.33f), new Vector2(0.14f, -0.29f), new Vector2(0.13f, -0.385f), new Vector2(0.015f, -0.418f));
        }

        private void AddInk(string name, Color ink, Color core, float width, params Vector2[] controls)
        {
            Vector3[] points = SamplePath(controls);
            Color glow = ink;
            glow.a = 0.16f;
            var layers = new[] { Line(name + "OuterGlow", glow, width * 2.1f, points, true),
                Line(name, ink, width, points, true), Line(name + "BrightCore", core, width * 0.34f, points, true) };
            for (int i = 0; i < layers.Length; i++) layers[i].sortingOrder = _baseOrder + 2 + i;
            _ink.Add(new InkStroke(layers, points));
        }

        private Vector3[] SamplePath(Vector2[] controls)
        {
            if (controls == null || controls.Length < 4 || (controls.Length - 1) % 3 != 0)
                throw new ArgumentException("A brush path requires complete cubic Bezier segments.", nameof(controls));
            const int samples = 12;
            int segments = (controls.Length - 1) / 3;
            var points = new Vector3[segments * samples + 1];
            points[0] = PaperPoint(controls[0], _paperWidth);
            for (int segment = 0; segment < segments; segment++)
                for (int step = 1; step <= samples; step++)
                {
                    float t = (float)step / samples;
                    float s = 1f - t;
                    int start = segment * 3;
                    Vector2 point = s * s * s * controls[start] + 3f * s * s * t * controls[start + 1]
                        + 3f * s * t * t * controls[start + 2] + t * t * t * controls[start + 3];
                    points[segment * samples + step] = PaperPoint(point, _paperWidth);
                }
            return points;
        }

        private LineRenderer Line(string name, Color color, float width, Vector3[] points, bool tapered)
        {
            var child = new GameObject(name, typeof(LineRenderer));
            child.transform.SetParent(_root.transform, false);
            var line = child.GetComponent<LineRenderer>();
            line.sharedMaterial = GetMaterial();
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.widthCurve = tapered
                ? new AnimationCurve(new Keyframe(0f, 0.28f), new Keyframe(0.24f, 0.96f),
                    new Keyframe(0.65f, 1f), new Keyframe(1f, 0.24f)) : AnimationCurve.Linear(0f, 1f, 1f, 1f);
            line.startColor = line.endColor = color;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.sortingOrder = _baseOrder + 2;
            line.positionCount = points.Length;
            line.SetPositions(points);
            return line;
        }

        public void SetStrokeReveal(float progress)
        {
            float written = Mathf.Clamp01(progress) * 3f;
            if (_tip != null) _tip.enabled = false;
            for (int i = 0; i < _ink.Count; i++)
            {
                float phase = Mathf.Clamp01(written - i);
                _ink[i].Reveal(phase);
                if (_tip != null && phase > 0f && phase < 1f)
                {
                    _tip.transform.localPosition = _ink[i].LeadingPoint;
                    _tip.enabled = true;
                }
            }
        }

        public void SetOpacity(float opacity)
        {
            if (_opacity == null || _renderers == null) return;
            _opacity.Clear();
            _opacity.SetColor(TintId, new Color(1f, 1f, 1f, Mathf.Clamp01(opacity)));
            foreach (Renderer renderer in _renderers)
                if (renderer != null) renderer.SetPropertyBlock(_opacity);
        }

        public void Cleanup()
        {
            if (_root != null) { _root.SetActive(false); DestroyOwned(_root); }
            foreach (Mesh mesh in _meshes) if (mesh != null) DestroyOwned(mesh);
            _root = null;
            _meshes.Clear();
            _ink.Clear();
            _renderers = null;
            _opacity = null;
            _tip = null;
        }

        private static Material GetMaterial()
        {
            if (_material != null) return _material;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) throw new InvalidOperationException("No alpha-blended talisman shader is available.");
            _material = new Material(shader) { name = "SpiritTalismanSurfaceAndInk",
                hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };
            return _material;
        }

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        private sealed class InkStroke
        {
            private readonly LineRenderer[] _layers;
            private readonly Vector3[] _points;
            private readonly float[] _distance;
            private float _lastProgress = -1f;
            public Vector3 LeadingPoint { get; private set; }

            public InkStroke(LineRenderer[] layers, Vector3[] points)
            {
                _layers = layers;
                _points = points;
                _distance = new float[points.Length];
                LeadingPoint = points[0];
                for (int i = 1; i < points.Length; i++)
                    _distance[i] = _distance[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            }

            public void Reveal(float progress)
            {
                if (Mathf.Approximately(progress, _lastProgress)) return;
                _lastProgress = progress;
                float length = progress * _distance[_distance.Length - 1];
                int end = 1;
                while (end < _points.Length - 1 && _distance[end] < length) end++;
                float segment = _distance[end] - _distance[end - 1];
                LeadingPoint = progress >= 1f ? _points[_points.Length - 1]
                    : Vector3.Lerp(_points[end - 1], _points[end], segment > 0f ? (length - _distance[end - 1]) / segment : 0f);
                foreach (LineRenderer line in _layers)
                {
                    if (line == null) continue;
                    line.enabled = progress > 0f;
                    if (progress <= 0f) continue;
                    if (progress >= 1f)
                    {
                        line.positionCount = _points.Length;
                        line.SetPositions(_points);
                        continue;
                    }
                    line.positionCount = end + 1;
                    for (int i = 0; i < end; i++) line.SetPosition(i, _points[i]);
                    line.SetPosition(end, LeadingPoint);
                }
            }
        }
    }
}
