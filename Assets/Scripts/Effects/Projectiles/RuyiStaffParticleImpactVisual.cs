using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Pure-particle contact layers for the Ruyi Staff stroke. The first layer
    /// is the pale-gold force burst; the second is a restrained cinnabar ember
    /// burst so ten baseline hits stay readable rather than filling the screen.
    /// </summary>
    public sealed class RuyiStaffParticleImpactVisual : MonoBehaviour
    {
        public const string ContactRootName = "RuyiStaffParticleContact";
        public const string ImpactRootName = "RuyiStaffParticleImpact";

        private float _elapsed;
        private float _lifetime;

        public static GameObject PlayContact(
            Transform parent,
            Vector3 position,
            VfxProfile profile)
        {
            Color color = profile != null
                ? profile.hitParticleColor
                : new Color(1f, 0.82f, 0.24f, 1f);
            int count = profile != null
                ? Mathf.Max(0, profile.hitParticleCount)
                : 6;
            float size = profile != null
                ? Mathf.Max(0.035f, profile.hitParticleSize)
                : 0.065f;
            float speed = profile != null
                ? Mathf.Max(1.8f, profile.hitParticleSpeed)
                : 3f;
            float lifetime = profile != null
                ? Mathf.Max(0.12f, profile.hitParticleLifetime)
                : 0.18f;
            return PlayBurst(
                parent,
                position,
                ContactRootName,
                "StaffForceSparks",
                count,
                size,
                speed,
                lifetime,
                color,
                true,
                34);
        }

        public static GameObject PlayImpact(
            Transform parent,
            Vector3 position,
            VfxProfile profile,
            bool isCrit)
        {
            Color color = profile != null
                ? profile.secondaryParticleColor
                : new Color(0.95f, 0.28f, 0.045f, 0.9f);
            int count = profile != null
                ? Mathf.Max(0, profile.secondaryParticleCount)
                : 4;
            float size = profile != null
                ? Mathf.Max(0.03f, profile.secondaryParticleSize)
                : 0.045f;
            float speed = profile != null
                ? Mathf.Max(1.2f, profile.secondaryParticleSpeed)
                : 1.8f;
            float lifetime = profile != null
                ? Mathf.Max(0.15f, profile.secondaryParticleLifetime)
                : 0.22f;
            float criticalScale = isCrit ? 1.22f : 1f;
            return PlayBurst(
                parent,
                position,
                ImpactRootName,
                "StaffCinnabarBurst",
                count,
                size * criticalScale,
                speed * criticalScale,
                lifetime,
                color,
                false,
                33);
        }

        private static GameObject PlayBurst(
            Transform parent,
            Vector3 position,
            string rootName,
            string particleName,
            int count,
            float size,
            float speed,
            float lifetime,
            Color color,
            bool stretched,
            int sortingOrder)
        {
            if (count <= 0) return null;

            var root = new GameObject(rootName);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var visual = root.AddComponent<RuyiStaffParticleImpactVisual>();
            visual._lifetime = lifetime + 0.12f;
            visual.CreateBurst(
                particleName,
                count,
                size,
                speed,
                lifetime,
                color,
                stretched,
                sortingOrder);
            return root;
        }

        private void CreateBurst(
            string name,
            int count,
            float size,
            float speed,
            float lifetime,
            Color color,
            bool stretched,
            int sortingOrder)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var ps = child.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = Mathf.Max(12, count + 2);
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                lifetime * 0.72f,
                lifetime * 1.12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                speed * 0.58f,
                speed);
            main.startSize = new ParticleSystem.MinMaxCurve(
                size * 0.62f,
                size * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                -Mathf.PI,
                Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Color.Lerp(Color.white, color, 0.28f),
                color);
            main.gravityModifier = stretched ? 0.08f : 0.42f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = stretched ? 0.055f : 0.085f;
            shape.radiusThickness = 1f;

            var colorLifetime = ps.colorOverLifetime;
            colorLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(color, 0.18f),
                    new GradientColorKey(color * 0.55f, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(color.a, 0f),
                    new GradientAlphaKey(color.a, 0.48f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorLifetime.color = gradient;

            var sizeLifetime = ps.sizeOverLifetime;
            sizeLifetime.enabled = true;
            sizeLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.35f),
                    new Keyframe(0.18f, 1f),
                    new Keyframe(1f, 0f)));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = stretched
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.lengthScale = stretched ? 3.6f : 1f;
            renderer.velocityScale = stretched ? 0.18f : 0f;
            renderer.sharedMaterial = ProceduralParticleMaterial.GetAdditive();
            renderer.sortingOrder = sortingOrder;

            ps.Emit(count);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= _lifetime)
                Destroy(gameObject);
        }
    }
}
