using System;
using System.Collections;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns the visual proxy for a defeated enemy.  Core has already selected
    /// and erased the enemy; this class only animates the proxy and publishes
    /// vacancy/completion callbacks exactly once.
    /// </summary>
    internal static class ErasedEnemyEffect
    {
        private const float DefaultRowWidth = 10.8f;
        private const float DefaultBandHeight = 0.72f;
        private const float DefaultIntensity = 1.35f;

        public static IEnumerator Play(Transform target, Transform parent,
            Color color, Action onVacated, Action onComplete,
            bool annihilation = false, float rowWidth = DefaultRowWidth,
            float intensity = DefaultIntensity, float bandHeight = DefaultBandHeight)
        {
            if (target == null)
            {
                onVacated?.Invoke();
                onComplete?.Invoke();
                yield break;
            }

            if (annihilation)
            {
                yield return PlayRowAnnihilation(
                    target,
                    parent,
                    color,
                    onVacated,
                    onComplete,
                    rowWidth,
                    intensity,
                    bandHeight);
                yield break;
            }

            yield return PlaySealErase(
                target,
                parent,
                color,
                onVacated,
                onComplete);
        }

        private static IEnumerator PlaySealErase(Transform target, Transform parent,
            Color color, Action onVacated, Action onComplete)
        {
            var root = new GameObject("FumoSealErase");
            root.transform.SetParent(parent, false);
            root.transform.position = target.position;
            var ps = CreateParticleSystem(
                root,
                "SealParticles",
                ProceduralParticleMaterial.GetAdditive(),
                96,
                88);
            var particles = new ParticleSystem.Particle[96];
            var sprite = target.GetComponent<SpriteRenderer>();
            Color tint = sprite != null ? sprite.color : Color.white;
            Vector3 originalScale = target.localScale;
            float elapsed = 0f;
            bool vacated = false;
            const float duration = 0.48f;
            try
            {
                yield return null;
                if (root == null || target == null) yield break;
                ps.Play();
                while (elapsed < duration && root != null && target != null)
                {
                    float t = Mathf.Clamp01(elapsed / duration);
                    float contract = Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01(t / 0.6f));
                    target.localScale = originalScale * Mathf.Lerp(1f, 0.2f, contract);
                    if (sprite != null)
                    {
                        Color faded = tint;
                        faded.a *= 1f - contract;
                        sprite.color = faded;
                    }
                    for (int i = 0; i < particles.Length; i++)
                    {
                        float angle = i / (float)particles.Length * Mathf.PI * 2f + t * 2.5f;
                        float radius = i < 64
                            ? Mathf.Lerp(0.78f, 0.04f, contract)
                            : Mathf.Lerp(0.2f, 0.88f, Mathf.InverseLerp(0.5f, 1f, t));
                        Color faded = color;
                        faded.a *= Mathf.Sin(t * Mathf.PI) * (i < 64 ? 1f : 0.45f);
                        particles[i] = Particle(
                            new Vector3(
                                Mathf.Cos(angle) * radius,
                                Mathf.Sin(angle) * radius * 1.15f,
                                0f),
                            faded,
                            i < 64 ? 0.065f : 0.025f);
                    }
                    ps.SetParticles(particles, particles.Length);
                    if (!vacated && elapsed >= 0.29f)
                    {
                        vacated = true;
                        target.gameObject.SetActive(false);
                        onVacated?.Invoke();
                    }
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            finally
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                if (target != null) target.gameObject.SetActive(false);
                if (!vacated) onVacated?.Invoke();
                onComplete?.Invoke();
            }
        }

        /// <summary>
        /// Fumozhu's row annihilation: a board-width void band sweeps across
        /// the row, collapses each enemy into a black seal, then detonates into
        /// fine purple motes.  The target proxy is hidden during the collapse;
        /// the presentation never changes combat state or damage.
        /// </summary>
        private static IEnumerator PlayRowAnnihilation(Transform target,
            Transform parent, Color color, Action onVacated, Action onComplete,
            float rowWidth, float intensity, float bandHeight)
        {
            rowWidth = Mathf.Clamp(rowWidth, 4f, 18f);
            intensity = Mathf.Clamp(intensity, 0.5f, 3f);
            bandHeight = Mathf.Clamp(bandHeight, 0.22f, 1.8f);

            var root = new GameObject("FumoRowAnnihilation");
            root.transform.SetParent(parent, false);
            // The authority receipt points at the first erased proxy, which is
            // usually the leftmost enemy.  The row band itself must be centred
            // on the board, otherwise half of the effect hangs off the left
            // edge.  Prefer the camera's board centre and keep a deterministic
            // leftmost-target fallback for headless probes.
            float rowCenterX = ResolveRowCenterX(target, rowWidth);
            root.transform.position = new Vector3(
                rowCenterX,
                target.position.y,
                target.position.z);
            int bandCount = Mathf.Clamp(Mathf.RoundToInt(84f * intensity), 72, 192);
            int burstCount = Mathf.Clamp(Mathf.RoundToInt(112f * intensity), 96, 256);
            int coreCount = Mathf.Clamp(Mathf.RoundToInt(28f * intensity), 24, 72);
            int shockCount = Mathf.Clamp(Mathf.RoundToInt(56f * intensity), 56, 144);
            int flashCount = Mathf.Clamp(Mathf.RoundToInt(24f * intensity), 24, 72);
            var band = CreateParticleSystem(
                root,
                "FumoVoidRowBand",
                ProceduralParticleMaterial.GetAlphaBlended(),
                bandCount,
                91);
            var core = CreateParticleSystem(
                root,
                "FumoVoidCore",
                ProceduralParticleMaterial.GetAdditive(),
                coreCount,
                94);
            var shock = CreateParticleSystem(
                root,
                "FumoVoidShockwave",
                ProceduralParticleMaterial.GetAdditive(),
                shockCount,
                95);
            var flash = CreateParticleSystem(
                root,
                "FumoVoidFlash",
                ProceduralParticleMaterial.GetAdditive(),
                flashCount,
                96);
            var bandParticles = new ParticleSystem.Particle[bandCount];
            AnnihilationDebrisInfinityVisual debris = null;
            var coreParticles = new ParticleSystem.Particle[coreCount];
            var shockParticles = new ParticleSystem.Particle[shockCount];
            var flashParticles = new ParticleSystem.Particle[flashCount];
            var sprite = target.GetComponent<SpriteRenderer>();
            Color tint = sprite != null ? sprite.color : Color.white;
            Vector3 originalScale = target.localScale;
            float elapsed = 0f;
            bool vacated = false;
            const float duration = 1.02f;
            try
            {
                debris = AnnihilationDebrisInfinityVisual.Create(root.transform, color, intensity, burstCount);
                yield return null;
                if (root == null || target == null) yield break;
                band.Play();
                core.Play();
                shock.Play();
                flash.Play();
                while (elapsed < duration && root != null && target != null)
                {
                    float t = Mathf.Clamp01(elapsed / duration);
                    float collapseT = Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01(t / 0.34f));
                    float sweepT = Mathf.Clamp01((t - 0.05f) / 0.42f);
                    target.localScale = originalScale * Mathf.Lerp(1f, 0.035f, collapseT);
                    if (sprite != null)
                    {
                        Color faded = tint;
                        faded.a *= 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(t / 0.29f));
                        sprite.color = faded;
                    }

                    UpdateVoidBand(
                        bandParticles,
                        rowWidth,
                        bandHeight,
                        sweepT,
                        t,
                        color,
                        intensity);
                    if (debris != null) debris.SetProgress(t);
                    UpdateVoidCore(
                        coreParticles,
                        collapseT,
                        t,
                        color,
                        intensity);
                    UpdateVoidShockwave(
                        shockParticles,
                        Mathf.Clamp01((t - 0.17f) / 0.62f),
                        t,
                        color,
                        rowWidth,
                        bandHeight,
                        intensity);
                    UpdateVoidFlash(
                        flashParticles,
                        Mathf.Clamp01((t - 0.16f) / 0.27f),
                        t,
                        color,
                        intensity);
                    band.SetParticles(bandParticles, bandParticles.Length);
                    core.SetParticles(coreParticles, coreParticles.Length);
                    shock.SetParticles(shockParticles, shockParticles.Length);
                    flash.SetParticles(flashParticles, flashParticles.Length);

                    if (!vacated && elapsed >= 0.27f)
                    {
                        vacated = true;
                        target.gameObject.SetActive(false);
                        onVacated?.Invoke();
                    }
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            finally
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                if (target != null) target.gameObject.SetActive(false);
                if (!vacated) onVacated?.Invoke();
                onComplete?.Invoke();
            }
        }

        private static float ResolveRowCenterX(Transform target, float rowWidth)
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                float depth = Mathf.Abs(target.position.z - camera.transform.position.z);
                return camera.ViewportToWorldPoint(
                    new Vector3(.5f, .5f, depth)).x;
            }
            return target.position.x + rowWidth * .5f;
        }

        private static void UpdateVoidBand(
            ParticleSystem.Particle[] particles,
            float rowWidth,
            float bandHeight,
            float sweepT,
            float t,
            Color sourceColor,
            float intensity)
        {
            Color voidColor = Color.Lerp(
                new Color(0.015f, 0.006f, 0.045f, 0.98f),
                new Color(0.28f, 0.045f, 0.52f, 0.92f),
                0.42f);
            Color edgeColor = Color.Lerp(sourceColor, new Color(0.64f, 0.12f, 1f, 1f), 0.62f);
            for (int i = 0; i < particles.Length; i++)
            {
                float u = i / (float)Mathf.Max(1, particles.Length - 1);
                float wave = Mathf.Sin(u * Mathf.PI * 8f + t * 17f) * bandHeight * 0.34f;
                float x = Mathf.Lerp(-rowWidth * 0.5f, rowWidth * 0.5f, u);
                float sweepFade = Mathf.Clamp01(1f - Mathf.Abs(u - sweepT) * 4.2f);
                float tailFade = Mathf.Clamp01(1f - Mathf.Max(0f, sweepT - u) * 1.5f);
                float alpha = Mathf.Clamp01((0.18f + sweepFade * 0.82f) * tailFade) *
                    Mathf.Clamp01(1f - Mathf.Max(0f, t - 0.47f) / 0.45f);
                bool edge = (i % 5) == 0;
                Color c = edge ? edgeColor : voidColor;
                c.a *= alpha * (0.88f + 0.16f * intensity);
                particles[i] = Particle(
                    new Vector3(x, wave + Mathf.Sin(u * Mathf.PI * 3f + t * 10f) *
                        bandHeight * 0.18f, 0.08f),
                    c,
                    edge ? 0.105f + 0.025f * intensity : 0.16f + 0.04f * intensity);
            }
        }


        private static void UpdateVoidCore(
            ParticleSystem.Particle[] particles,
            float collapseT,
            float t,
            Color sourceColor,
            float intensity)
        {
            Color coreColor = Color.Lerp(Color.white,
                Color.Lerp(sourceColor, new Color(0.25f, 0.025f, 0.62f, 1f), 0.78f), 0.58f);
            for (int i = 0; i < particles.Length; i++)
            {
                float u = i / (float)Mathf.Max(1, particles.Length - 1);
                float angle = u * Mathf.PI * 2f + t * (i % 2 == 0 ? 5.5f : -4.5f);
                float radius = Mathf.Lerp(0.85f, 0.075f, collapseT) *
                    (0.56f + 0.44f * Mathf.Sin(u * Mathf.PI));
                float alpha = Mathf.Sin(Mathf.Clamp01(t / 0.46f) * Mathf.PI);
                if (t > 0.46f)
                    alpha *= Mathf.Clamp01(1f - (t - 0.46f) / 0.3f);
                Color c = coreColor;
                c.a *= alpha * (0.82f + 0.2f * intensity);
                particles[i] = Particle(
                    new Vector3(Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius * 0.82f, 0.12f),
                    c,
                    Mathf.Lerp(0.16f, 0.035f, collapseT) *
                        (i % 4 == 0 ? 1.3f : 0.82f));
            }
        }

        private static void UpdateVoidShockwave(
            ParticleSystem.Particle[] particles,
            float shockT,
            float t,
            Color sourceColor,
            float rowWidth,
            float bandHeight,
            float intensity)
        {
            Color edgeColor = Color.Lerp(
                new Color(0.7f, 0.16f, 1f, 1f),
                Color.white,
                0.3f);
            float ringFade = Mathf.Sin(Mathf.Clamp01(shockT) * Mathf.PI);
            float radiusX = Mathf.Lerp(0.12f, rowWidth * 0.56f, EaseOutCubic(shockT));
            float radiusY = Mathf.Lerp(0.08f, 0.72f + bandHeight * 1.9f,
                EaseOutCubic(shockT));
            for (int i = 0; i < particles.Length; i++)
            {
                float u = i / (float)Mathf.Max(1, particles.Length);
                float angle = u * Mathf.PI * 2f + t * 3.5f;
                float jitter = 1f + Mathf.Sin(i * 2.17f + t * 19f) * 0.08f;
                Color c = (i % 9 == 0)
                    ? Color.white
                    : Color.Lerp(edgeColor, sourceColor, 0.26f);
                c.a *= ringFade * (0.62f + 0.38f * intensity);
                particles[i] = Particle(
                    new Vector3(
                        Mathf.Cos(angle) * radiusX * jitter,
                        Mathf.Sin(angle) * radiusY * jitter,
                        -0.12f),
                    c,
                    (i % 9 == 0 ? 0.085f : 0.035f) *
                        (0.9f + intensity * 0.2f));
            }
        }

        private static void UpdateVoidFlash(
            ParticleSystem.Particle[] particles,
            float flashT,
            float t,
            Color sourceColor,
            float intensity)
        {
            float pulse = Mathf.Sin(Mathf.Clamp01(flashT) * Mathf.PI);
            float radius = Mathf.Lerp(0.04f, 0.9f, EaseOutCubic(flashT));
            Color flashColor = Color.Lerp(Color.white,
                Color.Lerp(sourceColor, new Color(0.66f, 0.2f, 1f, 1f), 0.72f),
                0.28f);
            for (int i = 0; i < particles.Length; i++)
            {
                float u = i / (float)Mathf.Max(1, particles.Length);
                float angle = u * Mathf.PI * 2f + t * (i % 2 == 0 ? 2.8f : -2.4f);
                float radial = radius * (0.55f + 0.45f * Mathf.Sin(u * Mathf.PI));
                Color c = flashColor;
                c.a *= pulse * (0.56f + 0.34f * intensity);
                particles[i] = Particle(
                    new Vector3(Mathf.Cos(angle) * radial,
                        Mathf.Sin(angle) * radial * 0.74f,
                        -0.18f),
                    c,
                    Mathf.Lerp(0.18f, 0.045f, flashT) *
                        (i % 6 == 0 ? 1.35f : 0.8f));
            }
        }

        private static float EaseOutCubic(float value)
        {
            value = Mathf.Clamp01(value);
            float inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }

        private static ParticleSystem CreateParticleSystem(
            GameObject root,
            string name,
            Material material,
            int maxParticles,
            int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 1f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            return ps;
        }

        private static ParticleSystem.Particle Particle(
            Vector3 position, Color color, float size)
        {
            return new ParticleSystem.Particle
            {
                position = position,
                startColor = color,
                startSize = size,
                startLifetime = 1f,
                remainingLifetime = 1f
            };
        }
    }
}
