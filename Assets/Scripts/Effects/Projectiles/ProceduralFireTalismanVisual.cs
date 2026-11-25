using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Owns local fire accents around the shared talisman paper and brush ink.
    /// The caller alone owns its host transform, flight and hit timing. Paper
    /// stays on the X axis so a casting owner can rotate the whole host upright.
    /// </summary>
    public sealed class ProceduralFireTalismanVisual : IProjectileVisual
    {
        public const string VisualTypeName = "ProceduralFireTalisman";
        private TalismanPaperVisual _paper;
        private Mesh _glow;
        private GameObject _root;
        private FireTalismanFlameMotion _motion;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _opacityBlock;
        private static Material _material;
        private static readonly int TintId = Shader.PropertyToID("_Color");

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            Cleanup();
            try
            {
                SpriteRenderer sprite = host.GetComponent<SpriteRenderer>();
                if (sprite != null) { sprite.sprite = null; sprite.enabled = false; }
                host.transform.localScale = Vector3.one * scale;
                _root = new GameObject("FireTalismanGeometry");
                _root.transform.SetParent(host.transform, false);
                if (_material == null)
                    _material = new Material(Shader.Find("Sprites/Default"))
                    { hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };

                float paperWidth = profile != null
                    ? Mathf.Clamp(profile.fireTalismanPaperWidth, 0.75f, 1.6f) : 1f;
                Color glyphCore = profile != null ? profile.fireTalismanGlyphCoreColor
                    : new Color(1f, 0.48f, 0.10f, 1f);
                float intensity = profile != null
                    ? Mathf.Clamp(profile.fireTalismanFlameIntensity, 0f, 1.5f) : 0.4f;
                var paperHost = new GameObject("TalismanPaperHost");
                paperHost.transform.SetParent(_root.transform, false);
                _paper = new TalismanPaperVisual();
                _paper.Initialize(paperHost.transform, profile, TalismanInkMotif.Fire, 24);
                CreateGlow(paperWidth, glyphCore, intensity);

                int ribbonCount = profile != null
                    ? Mathf.Clamp(profile.fireTalismanFlameRibbonCount, 0, 6) : 2;
                int sparkCount = profile != null
                    ? Mathf.Clamp(profile.fireTalismanSparkCount, 0, 24) : 4;
                if (intensity <= 0f) { ribbonCount = 0; sparkCount = 0; }
                Color flame = profile != null ? profile.fireTalismanFlameColor
                    : new Color(1f, 0.24f, 0.025f, 0.82f);
                Color flameCore = profile != null ? profile.fireTalismanFlameCoreColor
                    : new Color(1f, 0.72f, 0.22f, 0.95f);
                var ribbons = new LineRenderer[ribbonCount * 2];
                for (int i = 0; i < ribbonCount; i++)
                {
                    ribbons[i * 2] = FlameLine("FireAura", flame,
                        0.048f + 0.030f * intensity, 23, 22);
                    ribbons[i * 2 + 1] = FlameLine("FireAuraCore", flameCore,
                        0.018f + 0.014f * intensity, 28, 22);
                }
                float sparkSize = profile != null
                    ? Mathf.Clamp(profile.fireTalismanSparkSize, 0.008f, 0.045f) : 0.014f;
                var sparks = new LineRenderer[sparkCount];
                for (int i = 0; i < sparkCount; i++)
                    sparks[i] = FlameLine("FineFireSpark", flameCore, sparkSize, 29, 3);
                _motion = _root.AddComponent<FireTalismanFlameMotion>();
                _motion.Configure(ribbons, sparks, paperWidth, intensity);
                var ownRenderers = new List<Renderer>();
                foreach (Renderer renderer in _root.GetComponentsInChildren<Renderer>(true))
                    if (!renderer.transform.IsChildOf(paperHost.transform)) ownRenderers.Add(renderer);
                _renderers = ownRenderers.ToArray();
                _opacityBlock = new MaterialPropertyBlock();
                SetStrokeReveal(1f);
                SetOpacity(1f);
                _root.SetActive(false);
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        private void CreateGlow(float paperWidth, Color core, float intensity)
        {
            const int segments = 28;
            var vertices = new Vector3[segments + 1];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * 3];
            core.a = 0.26f * Mathf.Clamp01(intensity);
            colors[0] = core;
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                vertices[i + 1] = new Vector3(
                    Mathf.Cos(angle) * 0.68f, Mathf.Sin(angle) * 0.34f * paperWidth, 0f);
                colors[i + 1] = new Color(core.r, core.g, core.b, 0f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }
            _glow = new Mesh { name = "FireTalismanSoftGlow",
                hideFlags = HideFlags.HideAndDontSave, vertices = vertices,
                colors = colors, triangles = triangles };
            _glow.RecalculateBounds();
            var glow = new GameObject("FireTalismanGlow", typeof(MeshFilter), typeof(MeshRenderer));
            glow.transform.SetParent(_root.transform, false);
            glow.GetComponent<MeshFilter>().sharedMesh = _glow;
            var renderer = glow.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = 22;
        }

        private LineRenderer Glyph(string name, Color color, float width, int order,
            params Vector3[] points)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.widthMultiplier = width;
            line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
            line.startColor = line.endColor = color;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.sortingOrder = order;
            return line;
        }

        private LineRenderer FlameLine(string name, Color color, float width, int order,
            int pointCount)
        {
            var line = Glyph(name, color, width, order, new Vector3[pointCount]);
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0f),
                new Keyframe(0.22f, 0.9f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(color, 0f),
                    new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(color.a, 0.25f),
                    new GradientAlphaKey(color.a * 0.7f, 0.7f),
                    new GradientAlphaKey(0f, 1f) });
            line.colorGradient = gradient;
            return line;
        }

        public void OnFlightStart()
        {
            if (_root == null) return;
            _motion.BeginFlight();
            _root.SetActive(true);
        }

        public void OnFlightEnd()
        {
            if (_root != null) _root.SetActive(false);
        }

        /// <summary>
        /// Writes the cached Bezier ink paths in brush order. Paper remains whole;
        /// fire motion owns only its separate ribbons and sparks. Flight start
        /// preserves this progress so the casting owner can reveal ink on hold.
        /// </summary>
        public void SetStrokeReveal(float progress)
        {
            _paper?.SetStrokeReveal(progress);
        }

        /// <summary>
        /// Applies one opacity multiplier to paper, glyphs, fire and sparks.
        /// Accent motion writes positions only, so its Update cannot restore
        /// alpha while the casting owner is fading this visual in or out.
        /// </summary>
        public void SetOpacity(float opacity)
        {
            _paper?.SetOpacity(opacity);
            if (_renderers == null || _opacityBlock == null) return;
            _opacityBlock.Clear();
            _opacityBlock.SetColor(TintId, new Color(1f, 1f, 1f, Mathf.Clamp01(opacity)));
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].SetPropertyBlock(_opacityBlock);
        }

        public Vector3 GetTrailAnchorOffset() => new Vector3(-0.48f, 0f, 0f);

        public void Cleanup()
        {
            _paper?.Cleanup();
            if (_root != null) { _root.SetActive(false); DestroyOwned(_root); }
            if (_glow != null) DestroyOwned(_glow);
            _root = null;
            _paper = null;
            _glow = null;
            _motion = null;
            _renderers = null;
            _opacityBlock = null;
        }

        private static void DestroyOwned(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

    }

    /// <summary>Animates only this fire talisman's local accents; never moves its host.</summary>
    internal sealed class FireTalismanFlameMotion : MonoBehaviour
    {
        private LineRenderer[] _ribbons;
        private LineRenderer[] _sparks;
        private float _paperWidth;
        private float _intensity;
        private float _age;

        public void Configure(LineRenderer[] ribbons, LineRenderer[] sparks,
            float paperWidth, float intensity)
        {
            _ribbons = ribbons;
            _sparks = sparks;
            _paperWidth = paperWidth;
            _intensity = intensity;
            BeginFlight();
        }

        public void BeginFlight()
        {
            _age = 0f;
            DrawAccents();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            DrawAccents();
        }

        private void DrawAccents()
        {
            if (_ribbons == null || _sparks == null) return;
            bool swirling = _intensity >= 0.7f;
            for (int i = 0; i < _ribbons.Length / 2; i++)
            {
                LineRenderer outer = _ribbons[i * 2];
                LineRenderer inner = _ribbons[i * 2 + 1];
                for (int j = 0; j < outer.positionCount; j++)
                {
                    float t = (float)j / (outer.positionCount - 1);
                    float phase = i * 2.1f + _age * 13f;
                    Vector3 position;
                    if (swirling)
                    {
                        float angle = phase + t * Mathf.PI * 1.5f;
                        float radius = 0.9f + Mathf.Sin(t * 12f + phase) * 0.10f;
                        position = new Vector3(
                            Mathf.Cos(angle) * (0.51f + _intensity * 0.16f) * radius,
                            Mathf.Sin(angle) * (0.21f * _paperWidth + _intensity * 0.07f) * radius,
                            0f);
                    }
                    else
                    {
                        float side = i % 2 == 0 ? 1f : -1f;
                        position = new Vector3(-0.28f - t * (0.35f + _intensity * 0.22f),
                            side * (0.13f * _paperWidth + Mathf.Sin(t * 5f + phase) * 0.035f),
                            0f);
                    }
                    outer.SetPosition(j, position);
                    inner.SetPosition(j, position);
                }
            }
            for (int i = 0; i < _sparks.Length; i++)
            {
                float t = Mathf.Repeat(_age * 2.6f + i * 0.618034f, 1f);
                float angle = i * 2.399963f + _age * 5f;
                float radius = 0.65f + t * 0.45f;
                Vector3 point = new Vector3(
                    Mathf.Cos(angle) * (0.48f + _intensity * 0.24f) * radius - t * 0.12f,
                    Mathf.Sin(angle) * (0.18f * _paperWidth + _intensity * 0.12f) * radius,
                    0f);
                Vector3 tail = new Vector3(0.026f + _intensity * 0.03f, 0.018f, 0f);
                _sparks[i].SetPosition(0, point - tail);
                _sparks[i].SetPosition(1, point - tail * 0.5f);
                _sparks[i].SetPosition(2, point);
            }
        }
    }
}
