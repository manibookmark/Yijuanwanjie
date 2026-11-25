using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Renders the deterministic spirit-to-formation energy path in one batched
    /// UI mesh. Facilities keep their own native awakening visuals.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HubRealmRestorationEnergyGraphic : MaskableGraphic
    {
        private struct EnergyParticle
        {
            public float Delay;
            public float Lateral;
            public float Along;
            public float Size;
            public float Seed;
        }

        private readonly List<EnergyParticle> _spiritParticles =
            new List<EnergyParticle>(180);

        private HubRealmRestorationProfile _profile;
        private Vector2 _spiritOrigin;
        private Vector2 _spiritSourceSize;
        private Vector2 _portalTarget;
        private float _spiritProgress;
        private Texture2D _softParticleTexture;

        public override Texture mainTexture
        {
            get
            {
                EnsureSoftParticleTexture();
                return _softParticleTexture != null
                    ? _softParticleTexture
                    : Texture2D.whiteTexture;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            EnsureSoftParticleTexture();
        }

        public void Configure(
            HubRealmRestorationProfile profile,
            Vector2 spiritOrigin,
            Vector2 spiritSourceSize,
            Vector2 portalTarget)
        {
            _profile = profile;
            _spiritOrigin = spiritOrigin;
            _spiritSourceSize = Vector2.Max(Vector2.zero, spiritSourceSize);
            _portalTarget = portalTarget;
            RebuildParticles(_spiritParticles, profile.SpiritParticleCount, 31);
            SetVerticesDirty();
        }

        public void SetProgress(float spirit)
        {
            _spiritProgress = Mathf.Clamp01(spirit);
            SetVerticesDirty();
        }

        public void RefreshTuningPreview()
        {
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (_profile == null)
                return;

            DrawPathParticles(
                helper,
                _spiritParticles,
                _spiritOrigin,
                _spiritSourceSize,
                _portalTarget,
                _profile.SpiritFlightArc,
                _profile.SpiritParticleSpread,
                _spiritProgress,
                _profile.SpiritParticleSize,
                _profile.SpiritParticleBrightness,
                _profile.JadeEnergyColor,
                true);
        }

        private static void DrawPathParticles(
            VertexHelper helper,
            List<EnergyParticle> particles,
            Vector2 start,
            Vector2 sourceSize,
            Vector2 end,
            float arc,
            float spread,
            float progress,
            float sizeScale,
            float brightness,
            Color colour,
            bool converge)
        {
            if (progress <= 0.001f || progress >= 0.999f)
                return;

            Vector2 path = end - start;
            Vector2 normal = path.sqrMagnitude > 0.001f
                ? new Vector2(-path.y, path.x).normalized
                : Vector2.up;
            Vector2 control = (start + end) * 0.5f + normal * arc;

            for (int index = 0; index < particles.Count; index++)
            {
                EnergyParticle particle = particles[index];
                float local = Mathf.Clamp01(
                    (progress - particle.Delay) /
                    Mathf.Max(0.05f, 1f - particle.Delay));
                if (local <= 0.001f || local >= 0.999f)
                    continue;

                float eased = 1f - Mathf.Pow(1f - local, 2.4f);
                Vector2 silhouetteOffset = sourceSize.sqrMagnitude > 0.01f
                    ? new Vector2(
                        particle.Lateral * sourceSize.x * 0.42f,
                        particle.Along * sourceSize.y * 0.42f)
                    : Vector2.zero;
                Vector2 source = start + silhouetteOffset +
                    normal * particle.Lateral * spread * 0.28f +
                    path.normalized * particle.Along * spread * 0.12f;
                if (!converge)
                    source = start + normal * particle.Lateral * spread * 0.25f;
                Vector2 position = QuadraticBezier(source, control, end, eased);
                Vector2 tangent = QuadraticTangent(source, control, end, eased);

                float alpha = Mathf.Sin(local * Mathf.PI) *
                              Mathf.Clamp(brightness, 0f, 2.5f) *
                              Mathf.Lerp(0.36f, 0.84f, particle.Seed);
                float size = Mathf.Lerp(2.4f, 6.8f, particle.Size) * sizeScale;
                float stretch = Mathf.Lerp(1.8f, 7.5f, eased);
                Color envelope = colour;
                envelope.a = Mathf.Clamp01(alpha * 0.36f);
                AddOrientedQuad(
                    helper,
                    position,
                    tangent,
                    new Vector2(size * stretch * 1.25f, size * 1.8f),
                    envelope);
                Color core = colour;
                core.a = Mathf.Clamp01(alpha);
                AddOrientedQuad(
                    helper,
                    position,
                    tangent,
                    new Vector2(size * stretch, size),
                    core);
            }
        }

        private static void RebuildParticles(
            List<EnergyParticle> target, int count, int salt)
        {
            target.Clear();
            target.Capacity = Mathf.Max(target.Capacity, count);
            for (int index = 0; index < count; index++)
            {
                float seed = Fraction((index + salt) * 0.61803398875f);
                float seed2 = Fraction((index + salt) * 0.754877666f);
                float seed3 = Fraction((index + salt) * 0.569840296f);
                target.Add(new EnergyParticle
                {
                    Delay = Mathf.Lerp(0f, 0.34f, seed3),
                    Lateral = Mathf.Lerp(-1f, 1f, seed),
                    Along = Mathf.Lerp(-1f, 1f, seed2),
                    Size = seed2,
                    Seed = seed
                });
            }
        }

        private static Vector2 QuadraticBezier(
            Vector2 start, Vector2 control, Vector2 end, float progress)
        {
            float inverse = 1f - progress;
            return inverse * inverse * start +
                   2f * inverse * progress * control +
                   progress * progress * end;
        }

        private static Vector2 QuadraticTangent(
            Vector2 start, Vector2 control, Vector2 end, float progress)
        {
            Vector2 tangent =
                2f * (1f - progress) * (control - start) +
                2f * progress * (end - control);
            return tangent.sqrMagnitude > 0.001f
                ? tangent.normalized
                : Vector2.right;
        }

        private static void AddOrientedQuad(
            VertexHelper helper,
            Vector2 centre,
            Vector2 direction,
            Vector2 size,
            Color colour)
        {
            Vector2 right = direction.normalized * (size.x * 0.5f);
            Vector2 up = new Vector2(-direction.y, direction.x).normalized *
                         (size.y * 0.5f);
            AddQuad(
                helper,
                centre - right - up,
                centre - right + up,
                centre + right + up,
                centre + right - up,
                colour);
        }

        private static void AddQuad(
            VertexHelper helper,
            Vector2 first,
            Vector2 second,
            Vector2 third,
            Vector2 fourth,
            Color colour)
        {
            int start = helper.currentVertCount;
            AddVertex(helper, first, new Vector2(0f, 0f), colour);
            AddVertex(helper, second, new Vector2(0f, 1f), colour);
            AddVertex(helper, third, new Vector2(1f, 1f), colour);
            AddVertex(helper, fourth, new Vector2(1f, 0f), colour);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start + 2, start + 3, start);
        }

        private static void AddVertex(
            VertexHelper helper, Vector2 position, Vector2 uv, Color colour)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.uv0 = uv;
            vertex.color = colour;
            helper.AddVert(vertex);
        }

        private void EnsureSoftParticleTexture()
        {
            if (_softParticleTexture != null)
                return;

            const int size = 32;
            _softParticleTexture = new Texture2D(
                size, size, TextureFormat.RGBA32, false)
            {
                name = "HubRealmRestorationSoftParticle",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float centre = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f - centre) / centre;
                    float ny = (y + 0.5f - centre) / centre;
                    float alpha = Mathf.Clamp01(
                        1f - Mathf.Sqrt(nx * nx + ny * ny));
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    _softParticleTexture.SetPixel(
                        x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _softParticleTexture.Apply();
        }

        private static float Fraction(float value) =>
            value - Mathf.Floor(value);

        protected override void OnDestroy()
        {
            if (_softParticleTexture != null)
            {
                if (Application.isPlaying)
                    Destroy(_softParticleTexture);
                else
                    DestroyImmediate(_softParticleTexture);
            }
            base.OnDestroy();
        }
    }
}
