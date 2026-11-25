using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns the shared shape-particle manifestation, opacity and cleanup.
    /// Baked closed shape samples retain each artifact's silhouette without
    /// displaying a whole card illustration or its paper background.
    /// Timing/placement follow the existing support-artifact language; Core
    /// already owns the resolved rule, and any HUD transfer remains separate.
    /// The original class/GUID stays compatible; mirror and Muddle share one
    /// lifecycle. Adding another lifecycle requires a new module review.
    /// </summary>
    public sealed class MirrorSelfEffectPlayer : MonoBehaviour
    {
        private readonly HashSet<GameObject> _roots = new HashSet<GameObject>();
        private readonly Dictionary<TextAsset, ImageParticleShape> _shapes =
            new Dictionary<TextAsset, ImageParticleShape>();

        public void Play(Vector3 position, VfxProfile profile)
        {
            PlayArtifact(position, profile, null);
        }

        /// <summary>Forms one artifact and hands its actual origin to the caller
        /// once during contraction. This owner never creates rule or stat gains.</summary>
        public bool PlayArtifact(Vector3 position, VfxProfile profile,
            Action<Vector3> onManifested)
        {
            if (!isActiveAndEnabled || profile == null ||
                profile.selfEffectParticleShape == null) return false;
            TextAsset source = profile.selfEffectParticleShape;
            if (!_shapes.TryGetValue(source, out ImageParticleShape shape))
            {
                if (!ImageParticleShape.TryRead(source, out shape)) return false;
                _shapes.Add(source, shape);
            }
            StartCoroutine(Animate(position, profile, shape, onManifested));
            return true;
        }

        private IEnumerator Animate(Vector3 source, VfxProfile profile,
            ImageParticleShape shape, Action<Vector3> onManifested)
        {
            // A newly added owner gets one stable active frame before creating
            // particle renderer state. No Infinity modules are used here.
            yield return null;
            if (!isActiveAndEnabled) yield break;
            float scale = CardCastPlacement.SupportScale(source, profile, shape.BodySize);
            Vector3 reach = shape.Reach + Vector3.one * 0.025f;
            reach.z += 0.2f / Mathf.Max(0.4f, scale);
            Vector3 origin = CardCastPlacement.AboveCard(source, profile, scale, reach);
            var root = new GameObject("SupportArtifactParticles");
            root.transform.SetParent(transform, false);
            root.transform.position = origin;
            _roots.Add(root);
            try
            {
                var ps = root.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.playOnAwake = false;
                main.loop = false;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = shape.Count;
                var emission = ps.emission;
                emission.enabled = false;
                var shapeModule = ps.shape;
                shapeModule.enabled = false;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlended();
                renderer.sortingOrder = 85;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                var particles = new ParticleSystem.Particle[shape.Count];
                float duration = Mathf.Clamp(profile.selfEffectDuration, 0.4f, 1.3f);
                float elapsed = 0f;
                bool manifested = false;
                // The hierarchy is active for a complete frame before Play.
                yield return null;
                if (root == null || !isActiveAndEnabled) yield break;
                ps.Play();
                while (elapsed < duration && root != null && isActiveAndEnabled)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    // Same reveal, contraction and HUD handoff as the existing
                    // bracelet/pearl support family, rather than an image fade.
                    float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.12f));
                    float fade = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.42f, 0.68f, t));
                    float groupScale = scale * Mathf.Lerp(0.72f, 1f, appear) *
                        Mathf.Lerp(0.35f, 1f, fade);
                    root.transform.position = origin + Vector3.up * t * 0.2f;
                    UpdateParticles(particles, shape, t, appear, fade, groupScale,
                        profile.selfEffectStyle == CardSelfEffectStyle.TianjiMirror);
                    ps.SetParticles(particles, particles.Length);
                    if (!manifested && t >= 0.5f)
                    {
                        manifested = true;
                        onManifested?.Invoke(root.transform.position);
                    }
                    if (t >= 0.68f) break;
                    yield return null;
                }
            }
            finally
            {
                _roots.Remove(root);
                if (root != null) Destroy(root);
            }
        }

        private static void UpdateParticles(ParticleSystem.Particle[] particles,
            ImageParticleShape shape, float t, float appear, float fade, float scale,
            bool mirrorGoldAccents)
        {
            float alpha = appear * fade;
            float sparkleWindow = Mathf.Sin(Mathf.InverseLerp(0.12f, 0.42f, t) * Mathf.PI);
            for (int i = 0; i < particles.Length; i++)
            {
                ImageParticleShape.Point point = shape.GetPoint(i);
                float phase = i * 2.399963f;
                Vector3 scatter = new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) *
                    (1f - appear) * 0.025f;
                Color color = point.Color;
                // Only the original mirror receives its warm bronze accents;
                // another artifact keeps the colors frozen in its shape input.
                bool gold = color.r > 0.7f && color.g > 0.5f && color.b < 0.6f;
                if (mirrorGoldAccents && gold && i % 7 == 0)
                    color = Color.Lerp(color, new Color(1f, 0.95f, 0.72f, color.a),
                        sparkleWindow * 0.16f);
                color.a *= alpha;
                particles[i] = new ParticleSystem.Particle
                {
                    position = (point.Position + scatter) * scale,
                    startColor = color,
                    startSize = point.Size * scale,
                    startLifetime = 2f,
                    remainingLifetime = 2f
                };
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            foreach (GameObject root in _roots)
                if (root != null) Destroy(root);
            _roots.Clear();
        }
    }
}
