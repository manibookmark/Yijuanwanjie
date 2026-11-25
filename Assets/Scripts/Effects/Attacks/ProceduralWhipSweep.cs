using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects.Attacks
{
    /// <summary>
    /// One authoritative Direct event and its captured world-space anchor.
    /// Core owns target selection; the whip presentation never queries the
    /// current formation or adds a newly exposed enemy.
    /// </summary>
    public readonly struct AuthoritativeWhipHit
    {
        public readonly DamageEvent DamageEvent;
        public readonly Vector3 Position;
        public readonly int PendingHitIndex;

        public AuthoritativeWhipHit(
            DamageEvent damageEvent,
            Vector3 position,
            int pendingHitIndex)
        {
            DamageEvent = damageEvent;
            Position = position;
            PendingHitIndex = pendingHitIndex;
        }
    }

    /// <summary>
    /// All targets snapshotted by one attack object. One instance is always
    /// presented as one complete sweep, regardless of target count.
    /// </summary>
    public sealed class AuthoritativeWhipSweep
    {
        public int AttackInstanceIndex { get; }
        public List<AuthoritativeWhipHit> Hits { get; } =
            new List<AuthoritativeWhipHit>();

        public AuthoritativeWhipSweep(int attackInstanceIndex)
        {
            AttackInstanceIndex = attackInstanceIndex;
        }
    }

    /// <summary>
    /// Groups recorded Direct events by attack object and keeps deterministic
    /// bookkeeping order. The player presents the whole group symbolically in
    /// one frame; this order never becomes a target-by-target travel path.
    /// </summary>
    public static class AuthoritativeWhipSweepBuilder
    {
        public static List<AuthoritativeWhipSweep> Build(
            IReadOnlyList<AuthoritativeWhipHit> source)
        {
            var result = new List<AuthoritativeWhipSweep>();
            var byAttackInstance = new Dictionary<int,
                AuthoritativeWhipSweep>();
            if (source == null) return result;

            for (int i = 0; i < source.Count; i++)
            {
                AuthoritativeWhipHit hit = source[i];
                if (hit.DamageEvent.Kind != DamageKind.Direct)
                    continue;

                int attackInstanceIndex =
                    hit.DamageEvent.AttackInstanceIndex;
                if (!byAttackInstance.TryGetValue(
                        attackInstanceIndex,
                        out AuthoritativeWhipSweep sweep))
                {
                    sweep = new AuthoritativeWhipSweep(
                        attackInstanceIndex);
                    byAttackInstance.Add(attackInstanceIndex, sweep);
                    result.Add(sweep);
                }
                sweep.Hits.Add(hit);
            }

            for (int i = 0; i < result.Count; i++)
            {
                result[i].Hits.Sort((left, right) =>
                {
                    int worldOrder = left.Position.x.CompareTo(
                        right.Position.x);
                    return worldOrder != 0
                        ? worldOrder
                        : left.PendingHitIndex.CompareTo(
                            right.PendingHitIndex);
                });
            }
            return result;
        }
    }

    /// <summary>
    /// Owns one continuous flexible screen-space whip, its propagating bend,
    /// particle silhouette and cleanup for authority-recorded sweep groups.
    /// The root stays at the injected card-area attack origin while a constrained
    /// rope simulation throws the tip to the left edge of the enemy front line,
    /// drags it across to the right, then retracts it. Target anchors only time
    /// left-to-right presentation feedback; the rope never constructs a path
    /// through individual enemies. No sprite, mesh, line or trail renderer
    /// participates.
    ///
    /// Module review: this is one cohesive VFX state machine. It uniquely owns
    /// all temporary whip particles and mutable playback state; Core continues
    /// to own targets and damage. Re-review if another visual mode or lifecycle
    /// is added here instead of replacing this one.
    /// </summary>
    public static class ProceduralWhipSweepPlayer
    {
        public const string VisualTypeName = "ProceduralWhipSweep";
        public const string RootName = "ProceduralWhipSweepRoot";
        public const string Revision =
            "ProceduralWhipSweep-v12-soft-verlet-spline";

        private const int RopeParticleCount = 192;
        private const float LeftContactMoment = 0.3f;
        private const float RightContactMoment = 0.7f;

        public static IEnumerator Play(
            Transform visualParent,
            Vector3 attackOrigin,
            IReadOnlyList<AuthoritativeWhipSweep> sweeps,
            VfxProfile profile,
            Action<AuthoritativeWhipHit> onEachHit)
        {
            if (sweeps == null || sweeps.Count == 0)
            {
                yield break;
            }

            float launchInterval = profile != null
                ? Mathf.Max(0f, profile.whipInterSweepDelay)
                : 0.1f;
            var activeSweeps = new List<IEnumerator>();
            int nextSweepIndex = 0;
            float elapsed = 0f;
            while (nextSweepIndex < sweeps.Count || activeSweeps.Count > 0)
            {
                while (nextSweepIndex < sweeps.Count &&
                       elapsed + Mathf.Epsilon >=
                           nextSweepIndex * launchInterval)
                {
                    AuthoritativeWhipSweep sweep =
                        sweeps[nextSweepIndex++];
                    if (sweep == null || sweep.Hits.Count == 0)
                        continue;
                    activeSweeps.Add(PlayOneSweep(
                        visualParent,
                        attackOrigin,
                        sweep,
                        profile,
                        onEachHit));
                }

                for (int i = activeSweeps.Count - 1; i >= 0; i--)
                {
                    if (!activeSweeps[i].MoveNext())
                        activeSweeps.RemoveAt(i);
                }

                if (nextSweepIndex >= sweeps.Count &&
                    activeSweeps.Count == 0)
                    break;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private static IEnumerator PlayOneSweep(
            Transform visualParent,
            Vector3 attackOrigin,
            AuthoritativeWhipSweep sweep,
            VfxProfile profile,
            Action<AuthoritativeWhipHit> onEachHit)
        {
            SweepGeometry geometry = SweepGeometry.From(
                attackOrigin,
                sweep,
                profile);
            float duration = profile != null
                ? Mathf.Max(0.3f, profile.whipSweepDuration)
                : 0.64f;
            var rig = new WhipParticleRig(
                visualParent,
                profile,
                duration,
                sweep.AttackInstanceIndex);

            int nextHitIndex = 0;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float normalized = Mathf.Clamp01(elapsed / duration);
                float reveal = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0f, 0.06f, normalized));
                float fade = reveal * (1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.88f, 1f, normalized)));
                rig.Render(
                    geometry,
                    normalized,
                    fade);

                while (nextHitIndex < sweep.Hits.Count &&
                       normalized >= GetHitMoment(
                           geometry,
                           sweep,
                           nextHitIndex))
                {
                    onEachHit?.Invoke(sweep.Hits[nextHitIndex]);
                    nextHitIndex++;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            while (nextHitIndex < sweep.Hits.Count)
            {
                onEachHit?.Invoke(sweep.Hits[nextHitIndex]);
                nextHitIndex++;
            }

            rig.Release();
        }

        private static float GetHitMoment(
            SweepGeometry geometry,
            AuthoritativeWhipSweep sweep,
            int hitIndex)
        {
            if (sweep == null || sweep.Hits.Count == 0)
                return LeftContactMoment;

            float left = geometry.LeftContact.x;
            float right = geometry.RightContact.x;
            if (right - left <= 0.01f)
                return LeftContactMoment;

            float horizontal = Mathf.InverseLerp(
                left,
                right,
                sweep.Hits[hitIndex].Position.x);
            return Mathf.Lerp(
                LeftContactMoment,
                RightContactMoment,
                horizontal);
        }

        private readonly struct SweepGeometry
        {
            public readonly Vector3 Start;
            public readonly Vector3 LeftContact;
            public readonly Vector3 RightContact;
            public readonly float ArcHeight;
            public readonly float Flexibility;
            public readonly float FullLength;

            private SweepGeometry(
                Vector3 start,
                Vector3 leftContact,
                Vector3 rightContact,
                float arcHeight,
                float flexibility)
            {
                Start = start;
                LeftContact = leftContact;
                RightContact = rightContact;
                ArcHeight = arcHeight;
                Flexibility = Mathf.Clamp01(flexibility);
                FullLength = Mathf.Max(
                    Vector3.Distance(start, leftContact),
                    Vector3.Distance(start, rightContact)) *
                    Mathf.Lerp(1.12f, 1.36f, Flexibility);
            }

            public static SweepGeometry From(
                Vector3 attackOrigin,
                AuthoritativeWhipSweep sweep,
                VfxProfile profile)
            {
                // Anchors choose only the horizontal center of the enemy group.
                // Vertical aim remains on one stable front-line gesture, so
                // mixed rows never pull the symbolic whip deeper into formation.
                Vector3 center = Vector3.zero;
                for (int i = 0; i < sweep.Hits.Count; i++)
                    center += sweep.Hits[i].Position;
                center /= sweep.Hits.Count;
                Vector3 leftmostTarget = sweep.Hits[0].Position;

                float padding = profile != null
                    ? Mathf.Max(0.2f, profile.whipSpanPadding)
                    : 0.62f;
                float arcHeight = profile != null
                    ? Mathf.Max(0.08f, profile.whipArcHeight)
                    : 0.38f;
                float flexibility = profile != null
                    ? Mathf.Clamp01(profile.whipFlexibility)
                    : 0.78f;
                float planeZ = center.z - 0.28f;

                Camera camera = Camera.main;
                if (camera == null)
                {
                    Vector3 fallbackStart = new Vector3(
                        attackOrigin.x,
                        attackOrigin.y,
                        planeZ);
                    Vector3 fallbackLeft = new Vector3(
                        leftmostTarget.x,
                        leftmostTarget.y,
                        planeZ);
                    Vector3 fallbackRight = new Vector3(
                        center.x + 2.6f + padding,
                        Mathf.Min(center.y, -0.55f),
                        planeZ);
                    return new SweepGeometry(
                        fallbackStart,
                        fallbackLeft,
                        fallbackRight,
                        arcHeight,
                        flexibility);
                }

                Vector3 viewportCenter = camera.WorldToViewportPoint(center);
                float viewportX = viewportCenter.z > 0f
                    ? Mathf.Clamp(viewportCenter.x, 0.4f, 0.6f)
                    : 0.5f;
                Vector3 start = new Vector3(
                    attackOrigin.x,
                    attackOrigin.y,
                    planeZ);
                Vector3 leftContact = new Vector3(
                    leftmostTarget.x,
                    leftmostTarget.y,
                    planeZ);
                Vector3 rightContact = ViewportToPlane(
                    camera,
                    new Vector2(viewportX + 0.31f, 0.43f),
                    planeZ);
                rightContact += Vector3.right * padding;
                return new SweepGeometry(
                    start,
                    leftContact,
                    rightContact,
                    arcHeight,
                    flexibility);
            }

            public void GetGuide(
                float motion,
                out Vector3 root,
                out Vector3 tip,
                out float deployedLength)
            {
                root = Start;
                float clamped = Mathf.Clamp01(motion);
                if (clamped <= LeftContactMoment)
                {
                    float cast = EaseOutCubic(Mathf.InverseLerp(
                        0f,
                        LeftContactMoment,
                        clamped));
                    tip = Vector3.Lerp(Start, LeftContact, cast);
                    tip += Vector3.up * ArcHeight *
                        Mathf.Sin(cast * Mathf.PI) * 0.72f;
                    deployedLength = Mathf.Max(
                        Vector3.Distance(root, tip) * 1.08f,
                        FullLength * Mathf.Lerp(0.04f, 1f, cast));
                    return;
                }

                if (clamped <= RightContactMoment)
                {
                    float sweep = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            LeftContactMoment,
                            RightContactMoment,
                            clamped));
                    tip = Vector3.Lerp(LeftContact, RightContact, sweep);
                    tip += Vector3.up * ArcHeight *
                        Mathf.Sin(sweep * Mathf.PI) * 0.08f;
                    deployedLength = FullLength;
                    return;
                }

                float retract = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(RightContactMoment, 0.98f, clamped));
                tip = Vector3.Lerp(RightContact, Start, retract);
                tip += Vector3.up * ArcHeight *
                    Mathf.Sin(retract * Mathf.PI) * 0.64f;
                deployedLength = Mathf.Max(
                    Vector3.Distance(root, tip) * 1.06f,
                    FullLength * Mathf.Lerp(1f, 0.035f, retract));
            }

            private static Vector3 ViewportToPlane(
                Camera camera,
                Vector2 viewport,
                float planeZ)
            {
                float distance = Vector3.Dot(
                    new Vector3(0f, 0f, planeZ) -
                        camera.transform.position,
                    camera.transform.forward);
                distance = Mathf.Max(0.01f, Mathf.Abs(distance));
                Vector3 world = camera.ViewportToWorldPoint(
                    new Vector3(viewport.x, viewport.y, distance));
                world.z = planeZ;
                return world;
            }

            private static float EaseOutCubic(float value)
            {
                float inverse = 1f - Mathf.Clamp01(value);
                return 1f - inverse * inverse * inverse;
            }
        }

        private sealed class WhipParticleRig
        {
            private readonly GameObject _root;
            private readonly ParticleSystem _shadow;
            private readonly ParticleSystem _rope;
            private readonly ParticleSystem.Particle[] _shadowParticles =
                new ParticleSystem.Particle[RopeParticleCount];
            private readonly ParticleSystem.Particle[] _ropeParticles =
                new ParticleSystem.Particle[RopeParticleCount];
            private readonly Vector3[] _ropePoints =
                new Vector3[RopeParticleCount];
            private readonly FlexibleWhipSimulation _simulation;
            private readonly ProceduralWhipAccentRig _accents;
            private readonly Color _ropeColor;
            private readonly Color _tipColor;
            private readonly Color _inkColor;
            private readonly float _thickness;
            private readonly float _particleLifetime;

            public WhipParticleRig(
                Transform parent,
                VfxProfile profile,
                float duration,
                int attackInstanceIndex)
            {
                _root = new GameObject(
                    $"{RootName}-{attackInstanceIndex}");
                if (parent != null)
                    _root.transform.SetParent(parent, false);

                _ropeColor = profile != null
                    ? profile.whipSegmentColor
                    : new Color(0.48f, 0.22f, 0.07f, 1f);
                _tipColor = profile != null
                    ? profile.whipJointColor
                    : new Color(0.95f, 0.58f, 0.14f, 1f);
                _inkColor = profile != null
                    ? profile.whipInkColor
                    : new Color(0.24f, 0.055f, 0.025f, 0.82f);
                _thickness = profile != null
                    ? Mathf.Max(0.05f, profile.whipSegmentThickness)
                    : 0.12f;
                _particleLifetime = duration + 0.2f;
                _simulation = new FlexibleWhipSimulation(
                    profile != null ? profile.whipFlexibility : 0.78f);
                _accents = new ProceduralWhipAccentRig(
                    _root.transform,
                    profile,
                    duration);

                _shadow = CreateParticleSystem(
                    "FlexibleWhipSoftShadow",
                    RopeParticleCount,
                    ProceduralParticleMaterial.GetAlphaBlended(),
                    81);
                _rope = CreateParticleSystem(
                    "FlexibleWhipSoftBody",
                    RopeParticleCount,
                    ProceduralParticleMaterial.GetAlphaBlended(),
                    82);
            }

            public void Render(
                SweepGeometry geometry,
                float motion,
                float fade)
            {
                geometry.GetGuide(
                    motion,
                    out Vector3 root,
                    out Vector3 tip,
                    out float deployedLength);
                _simulation.Step(
                    root,
                    tip,
                    deployedLength,
                    geometry.ArcHeight,
                    Time.deltaTime);
                _accents.Render(
                    geometry.LeftContact,
                    tip,
                    motion,
                    fade);
                for (int i = 0; i < RopeParticleCount; i++)
                {
                    _ropePoints[i] = _simulation.Sample(
                        i / (float)(RopeParticleCount - 1));
                }

                RenderLayer(
                    _shadow,
                    _shadowParticles,
                    fade,
                    _thickness * 1.48f,
                    shadow: true);
                RenderLayer(
                    _rope,
                    _ropeParticles,
                    fade,
                    _thickness,
                    shadow: false);
            }

            public void Release()
            {
                _accents.Clear();
                StopAndClear(_shadow);
                StopAndClear(_rope);
                UnityEngine.Object.Destroy(_root, 0.08f);
            }

            private void RenderLayer(
                ParticleSystem system,
                ParticleSystem.Particle[] particles,
                float fade,
                float thickness,
                bool shadow)
            {
                for (int i = 0; i < RopeParticleCount; i++)
                {
                    float u = i / (float)(RopeParticleCount - 1);
                    float taper = Mathf.Lerp(
                        1.05f,
                        0.2f,
                        Mathf.Pow(u, 1.35f));
                    float neighborDistance;
                    if (i < RopeParticleCount - 1)
                    {
                        neighborDistance = Vector3.Distance(
                            _ropePoints[i],
                            _ropePoints[i + 1]);
                    }
                    else
                    {
                        neighborDistance = Vector3.Distance(
                            _ropePoints[i - 1],
                            _ropePoints[i]);
                    }
                    float size = Mathf.Max(
                        thickness * taper,
                        neighborDistance * 1.38f);
                    Color color = shadow
                        ? WithAlpha(
                            _inkColor,
                            _inkColor.a * fade * 0.42f)
                        : Color.Lerp(
                            _ropeColor,
                            _tipColor,
                            Mathf.SmoothStep(0f, 1f,
                                Mathf.InverseLerp(0.72f, 1f, u)));
                    if (!shadow)
                        color = WithAlpha(color, color.a * fade);
                    particles[i] = new ParticleSystem.Particle
                    {
                        position = _ropePoints[i] +
                            (shadow
                                ? new Vector3(0f, -_thickness * 0.08f, 0.01f)
                                : Vector3.zero),
                        velocity = Vector3.zero,
                        startLifetime = _particleLifetime,
                        remainingLifetime = _particleLifetime,
                        startSize = size,
                        startColor = color
                    };
                }
                system.SetParticles(particles, particles.Length);
            }

            private ParticleSystem CreateParticleSystem(
                string name,
                int maxParticles,
                Material material,
                int sortingOrder)
            {
                var child = new GameObject(name);
                child.transform.SetParent(_root.transform, false);
                ParticleSystem ps = child.AddComponent<ParticleSystem>();
                ps.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.loop = false;
                main.playOnAwake = false;
                main.simulationSpace =
                    ParticleSystemSimulationSpace.World;
                main.startLifetime = _particleLifetime;
                main.startSpeed = 0f;
                main.startSize3D = false;
                main.maxParticles = maxParticles;

                var emission = ps.emission;
                emission.enabled = false;
                var shape = ps.shape;
                shape.enabled = false;

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.sharedMaterial = material;
                renderer.sortingOrder = sortingOrder;
                ps.Play();
                return ps;
            }

            private static Color WithAlpha(Color color, float alpha)
            {
                color.a = Mathf.Clamp01(alpha);
                return color;
            }

            private static void StopAndClear(ParticleSystem ps)
            {
                if (ps == null) return;
                ps.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
