using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SpiritEnergyColorPalette =
    WanjieScroll.Core.SpiritEnergyColorPalette;

namespace WanjieScroll.UI.Presentations
{
    /// <summary>
    /// 在一个 CanvasRenderer 内批量绘制卷灵聚散光屑。粒子布局确定、边缘透明，
    /// 不创建逐粒子 GameObject，也不使用带矩形底色的贴图。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterMaterializationGraphic : MaskableGraphic
    {
        private struct Particle
        {
            public Vector2 Outside;
            public Vector2 AroundActor;
            public Vector2 GatherRing;
            public float Delay;
            public float Arc;
            public float Length;
            public float Thickness;
            public float Seed;
        }

        private const int DefaultParticleCount = 96;
        private const int MaximumParticleCount = 1024;
        private readonly List<Particle> _particles =
            new List<Particle>(DefaultParticleCount);

        private float _progress;
        private bool _reverse;

        public bool HasParticles => _particles.Count > 0;

        public void Configure(
            Vector2 rootSize,
            Vector2 actorCenter,
            Vector2 actorSize,
            int particleCount,
            float sizeScale)
        {
            int count = particleCount > 0
                ? Mathf.Min(particleCount, MaximumParticleCount)
                : DefaultParticleCount;
            float scale = Mathf.Max(0.1f, sizeScale);
            _particles.Clear();
            if (_particles.Capacity < count)
                _particles.Capacity = count;

            var random = new System.Random(0x484C53);
            for (int index = 0; index < count; index++)
            {
                float seed = Next01(random);
                float angle = Next01(random) * Mathf.PI * 2f;
                float radial = Mathf.Sqrt(Next01(random));
                Vector2 aroundActor = actorCenter + new Vector2(
                    Mathf.Cos(angle) * actorSize.x * 0.34f * radial,
                    Mathf.Sin(angle) * actorSize.y * 0.46f * radial);

                float gatherAngle = Next01(random) * Mathf.PI * 2f;
                float gatherRadius = Mathf.Lerp(
                    0.56f, 0.92f, Next01(random));
                Vector2 gatherRing = actorCenter + new Vector2(
                    Mathf.Cos(gatherAngle) * actorSize.x * gatherRadius,
                    Mathf.Sin(gatherAngle) * actorSize.y *
                    gatherRadius * 0.74f);

                _particles.Add(new Particle
                {
                    Outside = NextLeftPointOutside(random, rootSize),
                    AroundActor = aroundActor,
                    GatherRing = gatherRing,
                    Delay = Mathf.Lerp(0f, 0.24f, Next01(random)),
                    Arc = Mathf.Lerp(-90f, 90f, Next01(random)),
                    Length = Mathf.Lerp(32f, 82f, seed) * scale,
                    Thickness = Mathf.Lerp(
                        4.2f, 10.5f, Next01(random)) * scale,
                    Seed = seed
                });
            }

            _progress = 0f;
            _reverse = false;
            SetVerticesDirty();
        }

        public void SetProgress(float normalized, bool reverse)
        {
            float nextProgress = Mathf.Clamp01(normalized);
            if (Mathf.Approximately(_progress, nextProgress) &&
                _reverse == reverse)
            {
                return;
            }

            _progress = nextProgress;
            _reverse = reverse;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (_progress <= 0.001f || _progress >= 0.999f)
                return;

            for (int index = 0; index < _particles.Count; index++)
            {
                Particle particle = _particles[index];
                float local = Mathf.Clamp01(
                    (_progress - particle.Delay) /
                    Mathf.Max(0.01f, 1f - particle.Delay));
                if (local <= 0.001f || local >= 0.999f)
                    continue;

                Vector2 start = _reverse
                    ? particle.AroundActor
                    : particle.GatherRing;
                Vector2 end = _reverse
                    ? particle.Outside
                    : particle.AroundActor;
                Vector2 path = end - start;
                Vector2 pathDirection = path.sqrMagnitude > 0.001f
                    ? path.normalized
                    : Vector2.right;
                Vector2 normal = new Vector2(
                    -pathDirection.y, pathDirection.x);
                float arcDirection = _reverse ? -1f : 1f;
                Vector2 control = (start + end) * 0.5f +
                                  normal * particle.Arc * arcDirection;

                float eased = EaseOutCubic(local);
                Vector2 position = QuadraticBezier(
                    start, control, end, eased);
                Vector2 tangent = QuadraticTangent(
                    start, control, end, eased);

                float visibility = SmoothStep01(local / 0.12f) *
                                   SmoothStep01((1f - local) / 0.28f);
                float alpha = visibility *
                              Mathf.Lerp(0.42f, 0.9f, particle.Seed);
                float pulse = 1f + Mathf.Sin(local * Mathf.PI) * 0.28f;
                float length = particle.Length * pulse;
                float thickness = particle.Thickness *
                                  Mathf.Lerp(1.15f, 0.72f, local);

                Color envelope = Color.Lerp(
                    SpiritEnergyColorPalette.ParticleDeep,
                    SpiritEnergyColorPalette.ParticleCore,
                    particle.Seed * 0.55f);
                envelope.a = alpha * 0.46f;
                AddSoftKite(
                    helper,
                    position,
                    tangent,
                    length * 1.42f,
                    thickness * 2.35f,
                    envelope);

                Color core = Color.Lerp(
                    SpiritEnergyColorPalette.ParticleCore,
                    SpiritEnergyColorPalette.ParticleHighlight,
                    Mathf.Lerp(0.08f, 0.72f, particle.Seed));
                core.a = alpha;
                AddSoftKite(
                    helper,
                    position,
                    tangent,
                    length,
                    thickness,
                    core);

                if ((index & 7) == 0)
                {
                    Color glint =
                        SpiritEnergyColorPalette.ParticleHighlight;
                    glint.a = alpha * 0.72f;
                    AddSoftKite(
                        helper,
                        position,
                        tangent,
                        thickness * 2.4f,
                        thickness * 1.65f,
                        glint);
                }
            }
        }

        private static Vector2 NextLeftPointOutside(
            System.Random random,
            Vector2 rootSize)
        {
            float depth = Mathf.Lerp(35f, 190f, Next01(random));
            return new Vector2(
                -rootSize.x * 0.5f - depth,
                Mathf.Lerp(
                    -rootSize.y * 0.48f,
                    rootSize.y * 0.48f,
                    Next01(random)));
        }

        private static void AddSoftKite(
            VertexHelper helper,
            Vector2 center,
            Vector2 direction,
            float length,
            float thickness,
            Color color)
        {
            Vector2 forward = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Vector2.right;
            Vector2 normal = new Vector2(-forward.y, forward.x);
            Vector2 along = forward * Mathf.Max(0.1f, length) * 0.5f;
            Vector2 across = normal * Mathf.Max(0.1f, thickness) * 0.5f;
            Color transparent = color;
            transparent.a = 0f;

            int start = helper.currentVertCount;
            helper.AddVert(center, color, Vector2.zero);
            helper.AddVert(center + across, transparent, Vector2.zero);
            helper.AddVert(center + along, transparent, Vector2.zero);
            helper.AddVert(center - across, transparent, Vector2.zero);
            helper.AddVert(center - along, transparent, Vector2.zero);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
            helper.AddTriangle(start, start + 3, start + 4);
            helper.AddTriangle(start, start + 4, start + 1);
        }

        private static Vector2 QuadraticBezier(
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float progress)
        {
            float inverse = 1f - progress;
            return inverse * inverse * start +
                   2f * inverse * progress * control +
                   progress * progress * end;
        }

        private static Vector2 QuadraticTangent(
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float progress)
        {
            Vector2 tangent =
                2f * (1f - progress) * (control - start) +
                2f * progress * (end - control);
            return tangent.sqrMagnitude > 0.001f
                ? tangent.normalized
                : Vector2.right;
        }

        private static float EaseOutCubic(float value)
        {
            value = Mathf.Clamp01(value);
            return 1f - Mathf.Pow(1f - value, 3f);
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static float Next01(System.Random random)
        {
            return (float)random.NextDouble();
        }
    }
}
