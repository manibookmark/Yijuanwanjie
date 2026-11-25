using System.Collections;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects.Attacks
{
    /// <summary>
    /// Owns only the restrained secondary accents around the simulated whip:
    /// one brief air ring at first tension and a faint wake behind the tip
    /// during the left-to-right pull. It never owns damage timing or rope state.
    /// </summary>
    internal sealed class ProceduralWhipAccentRig
    {
        private const int BurstParticleCount = 48;
        private const int WakeParticleCount = 30;

        private readonly ParticleSystem _burst;
        private readonly ParticleSystem _wake;
        private readonly ParticleSystem.Particle[] _burstParticles =
            new ParticleSystem.Particle[BurstParticleCount];
        private readonly ParticleSystem.Particle[] _wakeParticles =
            new ParticleSystem.Particle[WakeParticleCount];
        private readonly float _particleLifetime;
        private readonly float _ringIntensity;
        private readonly float _wakeIntensity;
        private readonly float _wakeLength;

        public ProceduralWhipAccentRig(
            Transform parent,
            VfxProfile profile,
            float duration)
        {
            _particleLifetime = duration + 0.2f;
            _ringIntensity = profile != null
                ? Mathf.Max(0f, profile.whipAirRingIntensity)
                : 1f;
            _wakeIntensity = profile != null
                ? Mathf.Max(0f, profile.whipAirWakeIntensity)
                : 1f;
            _wakeLength = profile != null
                ? Mathf.Max(0.25f, profile.whipAirWakeLength)
                : 1f;
            _wake = CreateParticleSystem(
                parent,
                "FlexibleWhipTipAirWake",
                WakeParticleCount,
                80);
            _burst = CreateParticleSystem(
                parent,
                "FlexibleWhipFirstContactAirRing",
                BurstParticleCount,
                83);
        }

        public void Render(
            Vector3 leftContact,
            Vector3 tip,
            float motion,
            float fade)
        {
            RenderFirstContactBurst(leftContact, motion, fade);
            RenderTipWake(tip, motion, fade);
        }

        public void Clear()
        {
            StopAndClear(_burst);
            StopAndClear(_wake);
        }

        private void RenderFirstContactBurst(
            Vector3 center,
            float motion,
            float fade)
        {
            if (_ringIntensity <= 0f ||
                motion < 0.275f || motion > 0.47f)
            {
                _burst.SetParticles(_burstParticles, 0);
                return;
            }

            float phase = Mathf.InverseLerp(0.275f, 0.47f, motion);
            float eased = 1f - Mathf.Pow(1f - phase, 3f);
            float scale = Mathf.Lerp(0.7f, 1.3f,
                Mathf.Clamp01(_ringIntensity * 0.5f));
            float radius = Mathf.Lerp(0.075f, 0.78f, eased) * scale;
            float alpha = Mathf.Sin(phase * Mathf.PI) * fade *
                Mathf.Min(1f, 0.58f * _ringIntensity);
            float size = Mathf.Lerp(0.14f, 0.07f, phase) * scale;
            Color color = new Color(1f, 0.88f, 0.64f, alpha);
            for (int i = 0; i < BurstParticleCount; i++)
            {
                float angle = i / (float)BurstParticleCount *
                    Mathf.PI * 2f;
                Vector3 radial = new Vector3(
                    Mathf.Cos(angle) * 1.16f,
                    Mathf.Sin(angle) * 0.82f,
                    0f);
                _burstParticles[i] = CreateParticle(
                    center + radial * radius,
                    size,
                    color);
            }
            _burst.SetParticles(
                _burstParticles,
                _burstParticles.Length);
        }

        private void RenderTipWake(
            Vector3 tip,
            float motion,
            float fade)
        {
            if (_wakeIntensity <= 0f ||
                motion < 0.29f || motion > 0.72f)
            {
                _wake.SetParticles(_wakeParticles, 0);
                return;
            }

            float sweep = Mathf.InverseLerp(0.29f, 0.72f, motion);
            float edgeFade = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, 0.12f, sweep)) *
                (1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.82f, 1f, sweep)));
            int particlesPerStrand = WakeParticleCount / 2;
            for (int i = 0; i < WakeParticleCount; i++)
            {
                int strand = i / particlesPerStrand;
                float u = (i % particlesPerStrand) /
                    (float)(particlesPerStrand - 1);
                float verticalSign = strand == 0 ? -1f : 1f;
                Vector3 position = tip + Vector3.left *
                    (0.06f + u * 1.12f * _wakeLength);
                position += Vector3.up * verticalSign *
                    (0.035f + Mathf.Sin(u * Mathf.PI) * 0.115f);
                float alpha = edgeFade * fade * (1f - u) *
                    Mathf.Min(1f, 0.28f * _wakeIntensity);
                float size = Mathf.Lerp(0.13f, 0.065f, u) *
                    Mathf.Lerp(0.8f, 1.35f,
                        Mathf.Clamp01(_wakeIntensity / 3f));
                _wakeParticles[i] = CreateParticle(
                    position,
                    size,
                    new Color(0.94f, 0.96f, 1f, alpha));
            }
            _wake.SetParticles(
                _wakeParticles,
                _wakeParticles.Length);
        }

        private ParticleSystem.Particle CreateParticle(
            Vector3 position,
            float size,
            Color color)
        {
            return new ParticleSystem.Particle
            {
                position = position,
                velocity = Vector3.zero,
                startLifetime = _particleLifetime,
                remainingLifetime = _particleLifetime,
                startSize = size,
                startColor = color
            };
        }

        private static ParticleSystem CreateParticleSystem(
            Transform parent,
            string name,
            int maxParticles,
            int sortingOrder)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            system.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize3D = false;
            main.maxParticles = maxParticles;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial =
                ProceduralParticleMaterial.GetAdditive();
            renderer.sortingOrder = sortingOrder;
            system.Play();
            return system;
        }

        private static void StopAndClear(ParticleSystem system)
        {
            if (system == null) return;
            system.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    /// <summary>
    /// Brief target-side directional response for the whip sweep. The original
    /// formation position is restored before defeat/backfill presentation runs.
    /// </summary>
    internal static class ProceduralWhipTargetResponse
    {
        public static IEnumerator NudgeRight(
            Transform target,
            float distance = 0.16f,
            float duration = 0.14f)
        {
            if (target == null) yield break;

            Vector3 original = target.localPosition;
            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float offset = Mathf.Sin(normalized * Mathf.PI) * distance;
                target.localPosition = original + Vector3.right * offset;
                yield return null;
            }

            if (target != null)
                target.localPosition = original;
        }
    }
}
