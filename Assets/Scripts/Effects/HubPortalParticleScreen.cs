using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Draws a coherent depth field emitted by the Bagua formation. Particles are
    /// arranged in radial lanes, accelerate past the viewer, and leave the screen
    /// instead of stopping on its perimeter. This makes outward optical flow read
    /// as the camera being pulled into the formation rather than as an explosion.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup))]
    public sealed class HubPortalParticleScreen : MaskableGraphic
    {
        private struct PullParticle
        {
            public Vector2 Source;
            public Vector2 Direction;
            public float Bend;
            public float Delay;
            public float Duration;
            public float Seed;
            public float Size;
            public float MaxStretch;
            public float BaseAlpha;
            public Color Colour;
        }

        // Three depth lanes extend each of the eight trigram directions. The
        // regular skeleton reads as formation energy; per-particle offsets keep
        // it from looking like a rigid loading spinner.
        private const int RadialLaneCount = 24;
        private readonly List<PullParticle> _particles =
            new List<PullParticle>(216);
        private CanvasGroup _canvasGroup;
        private Texture2D _softParticleTexture;
        private int _particleCount = 216;
        private Vector2 _sourceOrigin = new Vector2(0f, -80f);
        private Vector2 _sourceSize = new Vector2(590f, 204f);
        private float _particleScale = 1.08f;
        private float _particleBrightness = 1.12f;
        private float _pullProgress;

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
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            EnsureSoftParticleTexture();
            RebuildParticleField();
            ResetPresentation();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureSoftParticleTexture();
            if (_particles.Count == 0)
                RebuildParticleField();
            SetVerticesDirty();
        }

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

        public void Configure(
            int particleCount,
            Vector2 sourceOrigin,
            Vector2 sourceSize,
            float particleScale,
            float particleBrightness)
        {
            int clampedCount = Mathf.Clamp(particleCount, 96, 480);
            Vector2 clampedSourceSize = new Vector2(
                Mathf.Max(100f, sourceSize.x),
                Mathf.Max(48f, sourceSize.y));
            bool geometryChanged = clampedCount != _particleCount ||
                                   !Approximately(sourceOrigin, _sourceOrigin) ||
                                   !Approximately(clampedSourceSize, _sourceSize);
            float clampedScale = Mathf.Clamp(particleScale, 0.7f, 2f);
            float clampedBrightness = Mathf.Clamp(
                particleBrightness,
                0.5f,
                1.8f);
            bool appearanceChanged =
                Mathf.Abs(clampedScale - _particleScale) > 0.0001f ||
                Mathf.Abs(clampedBrightness - _particleBrightness) > 0.0001f;

            _particleCount = clampedCount;
            _sourceOrigin = sourceOrigin;
            _sourceSize = clampedSourceSize;
            _particleScale = clampedScale;
            _particleBrightness = clampedBrightness;
            if (geometryChanged)
                RebuildParticleField();
            else if (appearanceChanged)
                SetVerticesDirty();
        }

        public void BeginOpening()
        {
            _canvasGroup.alpha = 1f;
            gameObject.SetActive(true);
            SetPullProgress(0f);
        }

        public void SetPullProgress(float progress)
        {
            float clamped = Mathf.Clamp01(progress);
            if (Mathf.Abs(_pullProgress - clamped) < 0.0001f)
                return;

            _pullProgress = clamped;
            SetVerticesDirty();
        }

        public void ResetPresentation()
        {
            _pullProgress = 0f;
            if (_canvasGroup != null)
                _canvasGroup.alpha = 0f;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_pullProgress <= 0.001f ||
                _pullProgress >= 0.999f ||
                _particles.Count == 0)
            {
                return;
            }

            Rect bounds = GetPixelAdjustedRect();
            for (int index = 0; index < _particles.Count; index++)
            {
                PullParticle particle = _particles[index];
                float local = Mathf.Clamp01(
                    (_pullProgress - particle.Delay) /
                    Mathf.Max(0.08f, particle.Duration));
                if (local <= 0.001f || local >= 0.999f)
                    continue;

                float travelProgress = Mathf.Pow(local, 2.15f);
                Vector2 perpendicular = new Vector2(
                    -particle.Direction.y,
                    particle.Direction.x);
                float travelDistance = DistanceBeyondScreen(
                    bounds,
                    particle.Source,
                    particle.Direction);
                Vector2 end = particle.Source +
                              particle.Direction * travelDistance;
                Vector2 firstControl = particle.Source +
                                       particle.Direction * 34f +
                                       perpendicular * particle.Bend;
                Vector2 secondControl = Vector2.Lerp(
                                            particle.Source,
                                            end,
                                            0.42f) +
                                        perpendicular * particle.Bend * 0.42f;
                Vector2 position = CubicBezier(
                    particle.Source,
                    firstControl,
                    secondControl,
                    end,
                    travelProgress);
                Vector2 tangent = CubicTangent(
                    particle.Source,
                    firstControl,
                    secondControl,
                    end,
                    travelProgress);

                float ignition = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0f, 0.10f, local));
                float exitFade = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.70f, 1f, local));
                float pulse = 0.88f + 0.12f * Mathf.Sin(
                    local * Mathf.PI * 2f + particle.Seed * 19f);
                float alpha = particle.BaseAlpha * ignition * exitFade *
                              pulse * _particleBrightness;
                if (alpha <= 0.002f)
                    continue;

                float depth = Mathf.Pow(local, 1.55f);
                float size = particle.Size * _particleScale *
                             Mathf.Lerp(0.62f, 2.75f, depth);
                float stretch = Mathf.Lerp(
                    1.15f,
                    particle.MaxStretch,
                    depth);
                float rotation = Mathf.Atan2(tangent.y, tangent.x);

                Color envelope = SpiritEnergyColorPalette.ParticleDeep;
                envelope.a = Mathf.Clamp01(alpha * 0.78f);
                AddQuad(
                    vertexHelper,
                    position,
                    new Vector2(size * stretch * 1.16f, size * 1.72f),
                    rotation,
                    envelope);

                Color core = particle.Colour;
                core.a = Mathf.Clamp01(alpha);
                AddQuad(
                    vertexHelper,
                    position,
                    new Vector2(size * stretch, size),
                    rotation,
                    core);
            }
        }

        private void RebuildParticleField()
        {
            _particles.Clear();
            _particles.Capacity = Mathf.Max(_particles.Capacity, _particleCount);

            for (int index = 0; index < _particleCount; index++)
            {
                int lane = index % RadialLaneCount;
                int rank = index / RadialLaneCount;
                float seed = Fraction((index + 1) * 0.61803398875f);
                float seed2 = Fraction((index + 1) * 0.754877666f);
                float seed3 = Fraction((index + 1) * 0.569840296f);
                float laneAngle = lane / (float)RadialLaneCount * Mathf.PI * 2f;
                float angle = laneAngle + Mathf.Lerp(-0.018f, 0.018f, seed3);
                Vector2 direction = new Vector2(
                    Mathf.Sin(angle),
                    Mathf.Cos(angle)).normalized;

                float band = 0.09f + (rank % 6) * 0.066f;
                float radius = band + Mathf.Lerp(-0.012f, 0.012f, seed2);
                Vector2 source = _sourceOrigin + new Vector2(
                    direction.x * _sourceSize.x * radius,
                    direction.y * _sourceSize.y * radius);
                float laneBendSign = lane % 2 == 0 ? 1f : -1f;

                _particles.Add(new PullParticle
                {
                    Source = source,
                    Direction = direction,
                    Bend = laneBendSign * Mathf.Lerp(24f, 82f, seed2),
                    Delay = Mathf.Lerp(0.025f, 0.16f, seed3) +
                            (rank % 3) * 0.008f,
                    Duration = Mathf.Lerp(0.60f, 0.76f, seed),
                    Seed = seed,
                    Size = Mathf.Lerp(2.2f, 5.1f, seed2),
                    MaxStretch = Mathf.Lerp(6.2f, 12.5f, seed3),
                    BaseAlpha = Mathf.Lerp(0.30f, 0.64f, seed),
                    Colour = index % 7 == 0
                        ? SpiritEnergyColorPalette.ParticleHighlight
                        : SpiritEnergyColorPalette.ParticleCore
                });
            }

            SetVerticesDirty();
        }

        private static float DistanceBeyondScreen(
            Rect bounds,
            Vector2 origin,
            Vector2 direction)
        {
            float horizontal = direction.x >= 0f
                ? (bounds.xMax - origin.x) / Mathf.Max(0.001f, direction.x)
                : (bounds.xMin - origin.x) / Mathf.Min(-0.001f, direction.x);
            float vertical = direction.y >= 0f
                ? (bounds.yMax - origin.y) / Mathf.Max(0.001f, direction.y)
                : (bounds.yMin - origin.y) / Mathf.Min(-0.001f, direction.y);
            return Mathf.Max(120f, Mathf.Min(horizontal, vertical) + 180f);
        }

        private void EnsureSoftParticleTexture()
        {
            if (_softParticleTexture != null)
                return;

            const int textureSize = 40;
            _softParticleTexture = new Texture2D(
                textureSize,
                textureSize,
                TextureFormat.RGBA32,
                false)
            {
                name = "HubPortalDepthStreak",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float centre = textureSize * 0.5f;
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float nx = (x + 0.5f - centre) / centre;
                    float ny = (y + 0.5f - centre) / centre;
                    float alpha = Mathf.Clamp01(
                        1f - Mathf.Sqrt(nx * nx + ny * ny));
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    _softParticleTexture.SetPixel(
                        x,
                        y,
                        new Color(1f, 1f, 1f, alpha));
                }
            }
            _softParticleTexture.Apply();
        }

        private static void AddQuad(
            VertexHelper vertexHelper,
            Vector2 centre,
            Vector2 size,
            float rotation,
            Color colour)
        {
            float cosine = Mathf.Cos(rotation);
            float sine = Mathf.Sin(rotation);
            Vector2 right = new Vector2(cosine, sine) * (size.x * 0.5f);
            Vector2 up = new Vector2(-sine, cosine) * (size.y * 0.5f);
            int firstVertex = vertexHelper.currentVertCount;

            AddVertex(vertexHelper, centre - right - up, new Vector2(0f, 0f), colour);
            AddVertex(vertexHelper, centre - right + up, new Vector2(0f, 1f), colour);
            AddVertex(vertexHelper, centre + right + up, new Vector2(1f, 1f), colour);
            AddVertex(vertexHelper, centre + right - up, new Vector2(1f, 0f), colour);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
            vertexHelper.AddTriangle(firstVertex + 2, firstVertex + 3, firstVertex);
        }

        private static void AddVertex(
            VertexHelper vertexHelper,
            Vector2 position,
            Vector2 uv,
            Color colour)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.uv0 = uv;
            vertex.color = colour;
            vertexHelper.AddVert(vertex);
        }

        private static Vector2 CubicBezier(
            Vector2 start,
            Vector2 firstControl,
            Vector2 secondControl,
            Vector2 end,
            float progress)
        {
            float inverse = 1f - progress;
            return inverse * inverse * inverse * start +
                   3f * inverse * inverse * progress * firstControl +
                   3f * inverse * progress * progress * secondControl +
                   progress * progress * progress * end;
        }

        private static Vector2 CubicTangent(
            Vector2 start,
            Vector2 firstControl,
            Vector2 secondControl,
            Vector2 end,
            float progress)
        {
            float inverse = 1f - progress;
            Vector2 tangent =
                3f * inverse * inverse * (firstControl - start) +
                6f * inverse * progress * (secondControl - firstControl) +
                3f * progress * progress * (end - secondControl);
            return tangent.sqrMagnitude > 0.0001f
                ? tangent.normalized
                : Vector2.right;
        }

        private static float Fraction(float value)
        {
            return value - Mathf.Floor(value);
        }

        private static bool Approximately(Vector2 first, Vector2 second)
        {
            return Mathf.Abs(first.x - second.x) < 0.01f &&
                   Mathf.Abs(first.y - second.y) < 0.01f;
        }
    }
}
