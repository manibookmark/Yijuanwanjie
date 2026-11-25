using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Pure-particle Ruyi Staff projectile. Alpha-blended particles form a
    /// readable black-iron shaft and two complete antique-gold end caps; additive motes
    /// only provide a restrained wake. No SpriteRenderer, MeshRenderer or
    /// TrailRenderer participates in the look. This single owner keeps body
    /// dimensions, colors and particle lifecycle together; flight and hits
    /// remain owned by the existing host and authoritative presentation events.
    /// </summary>
    public sealed class RuyiStaffParticleVisual : IProjectileVisual
    {
        public const string VisualTypeName = "RuyiStaffParticles";
        public const string RootName = "RuyiStaffParticleRoot";
        public const string Revision = "RuyiStaffParticles-v6-iron-gold-endcaps";

        private Transform _root;
        private ParticleSystem _shaft;
        private ParticleSystem _shaftHighlight;
        private ParticleSystem _shaftShadow;
        private ParticleSystem _goldCaps;
        private ParticleSystem _wake;
        private ParticleSystem _embers;
        private Color _shaftColor;
        private Color _shaftHighlightColor;
        private Color _shaftShadowColor;
        private Color _goldColor;
        private Color _wakeColor;
        private Color _emberColor;
        private float _lengthScale = 1f;
        private float _thicknessScale = 1f;
        private float _wakeScale = 1f;

        static RuyiStaffParticleVisual()
        {
            ProjectileVisualFactory.Register(
                VisualTypeName,
                () => new RuyiStaffParticleVisual());
        }

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            SpriteRenderer sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null)
            {
                sprite.sprite = null;
                sprite.enabled = false;
            }

            host.transform.localScale = Vector3.one * scale;
            _root = EnsureChild(host.transform, RootName);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
            _root.gameObject.SetActive(true);

            // F7/F6 battlefield backgrounds are both dark brown. A nearly
            // black shaft disappears there and leaves only additive motes, so
            // keep the iron body dark but above the background luminance.
            _shaftColor = new Color(0.16f, 0.17f, 0.185f, 1f);
            _goldColor = profile != null
                ? Color.Lerp(
                    new Color(0.72f, 0.44f, 0.1f, 1f),
                    profile.hitParticleColor,
                    0.34f)
                : new Color(0.88f, 0.58f, 0.14f, 1f);
            _shaftHighlightColor = new Color(0.44f, 0.47f, 0.49f, 0.88f);
            _shaftShadowColor = new Color(0.075f, 0.08f, 0.09f, 0.88f);
            _wakeColor = profile != null
                ? profile.trailColor
                : new Color(1f, 0.66f, 0.12f, 0.86f);
            _emberColor = profile != null
                ? profile.secondaryParticleColor
                : new Color(0.95f, 0.24f, 0.045f, 0.82f);
            _lengthScale = ReadPositiveScale(
                profile != null ? profile.ruyiStaffLengthScale : 1f);
            _thicknessScale = ReadPositiveScale(
                profile != null ? profile.ruyiStaffThicknessScale : 1f);
            _wakeScale = ReadPositiveScale(
                profile != null ? profile.ruyiStaffWakeScale : 1f);
            float configuredWakeLifetime = profile != null &&
                profile.trailTime > 0f
                    ? profile.trailTime
                    : 0.14f;

            _shaft = EnsurePersistentBar(
                _root,
                "StaffIronShaft",
                _shaftColor,
                1.5f * _lengthScale,
                0.16f * _thicknessScale,
                1,
                ProceduralParticleMaterial.GetAlphaBlendedBox(),
                28);
            _shaftHighlight = EnsurePersistentBar(
                _root,
                "StaffShaftHighlight",
                _shaftHighlightColor,
                0.86f * _lengthScale,
                0.025f * _thicknessScale,
                1,
                ProceduralParticleMaterial.GetAlphaBlendedBox(),
                29);
            _shaftShadow = EnsurePersistentBar(
                _root,
                "StaffShaftShadow",
                _shaftShadowColor,
                0.86f * _lengthScale,
                0.035f * _thicknessScale,
                1,
                ProceduralParticleMaterial.GetAlphaBlendedBox(),
                29);
            _goldCaps = EnsurePersistentBar(
                _root,
                // Keep the pooled child identity; its four narrow collars
                // are replaced by two caps inside the original total length.
                "StaffGoldBands",
                _goldColor,
                0.30f * _lengthScale,
                0.22f * _thicknessScale,
                2,
                ProceduralParticleMaterial.GetAlphaBlendedBox(),
                30);
            _wake = EnsureWake(
                _root,
                "StaffGoldenWake",
                _wakeColor,
                16f,
                0.052f * _wakeScale,
                configuredWakeLifetime * 0.93f,
                25);
            _embers = EnsureWake(
                _root,
                "StaffCinnabarEmbers",
                _emberColor,
                5f,
                0.032f * _wakeScale,
                configuredWakeLifetime * 1.29f,
                24);

            StopAndClear(_shaft);
            StopAndClear(_shaftHighlight);
            StopAndClear(_shaftShadow);
            StopAndClear(_goldCaps);
            StopAndClear(_wake);
            StopAndClear(_embers);
        }

        public void OnFlightStart()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            EmitPersistentBar(
                _shaft,
                _shaftColor,
                1.5f * _lengthScale,
                0.16f * _thicknessScale,
                Vector3.zero);
            EmitPersistentBar(
                _shaftHighlight,
                _shaftHighlightColor,
                0.86f * _lengthScale,
                0.025f * _thicknessScale,
                new Vector3(0f, 0.038f * _thicknessScale, -0.005f));
            EmitPersistentBar(
                _shaftShadow,
                _shaftShadowColor,
                0.86f * _lengthScale,
                0.035f * _thicknessScale,
                new Vector3(0f, -0.053f * _thicknessScale, -0.005f));
            EmitGoldCaps(
                _goldCaps,
                _goldColor,
                _lengthScale,
                _thicknessScale);
            RestartWake(_wake);
            RestartWake(_embers);
        }

        public void OnFlightEnd()
        {
            StopAndClear(_shaft);
            StopAndClear(_shaftHighlight);
            StopAndClear(_shaftShadow);
            StopAndClear(_goldCaps);
            StopEmitting(_wake);
            StopEmitting(_embers);
        }

        public Vector3 GetTrailAnchorOffset() => Vector3.zero;

        public void Cleanup()
        {
            StopAndClear(_shaft);
            StopAndClear(_shaftHighlight);
            StopAndClear(_shaftShadow);
            StopAndClear(_goldCaps);
            StopAndClear(_wake);
            StopAndClear(_embers);
        }

        private static ParticleSystem EnsurePersistentBar(
            Transform parent,
            string name,
            Color color,
            float width,
            float height,
            int maxParticles,
            Material material,
            int sortingOrder)
        {
            ParticleSystem ps = EnsureParticleSystem(parent, name);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = 2f;
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = width;
            main.startSizeY = height;
            main.startSizeZ = 1f;
            main.startColor = color;
            main.maxParticles = Mathf.Max(1, maxParticles);

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0f;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            return ps;
        }

        private static ParticleSystem EnsureWake(
            Transform parent,
            string name,
            Color color,
            float rateOverDistance,
            float size,
            float lifetime,
            int sortingOrder)
        {
            ParticleSystem ps = EnsureParticleSystem(parent, name);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                lifetime * 0.72f,
                lifetime * 1.18f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(
                size * 0.62f,
                size * 1.2f);
            main.startColor = color;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = rateOverDistance;

            var shape = ps.shape;
            shape.enabled = false;

            var colorLifetime = ps.colorOverLifetime;
            colorLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(color, 0.22f),
                    new GradientColorKey(color * 0.58f, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(color.a, 0f),
                    new GradientAlphaKey(color.a * 0.78f, 0.42f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorLifetime.color = gradient;

            var sizeLifetime = ps.sizeOverLifetime;
            sizeLifetime.enabled = true;
            sizeLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.42f),
                    new Keyframe(0.2f, 1f),
                    new Keyframe(1f, 0f)));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = ProceduralParticleMaterial.GetAdditive();
            renderer.sortingOrder = sortingOrder;
            return ps;
        }

        private static void EmitPersistentBar(
            ParticleSystem ps,
            Color color,
            float width,
            float height,
            Vector3 position)
        {
            if (ps == null) return;
            StopAndClear(ps);
            var particle = new ParticleSystem.Particle
            {
                position = position,
                velocity = Vector3.zero,
                startLifetime = 2f,
                remainingLifetime = 2f,
                startSize3D = new Vector3(width, height, 1f),
                startColor = color
            };
            ps.SetParticles(new[] { particle }, 1);
            ps.Play();
        }

        private static void EmitGoldCaps(
            ParticleSystem ps,
            Color color,
            float lengthScale,
            float thicknessScale)
        {
            if (ps == null) return;
            StopAndClear(ps);
            // Centers +/-0.60 and width 0.30 cover both ends up to +/-0.75,
            // preserving the original 1.50 shaft length and 0.22 cap thickness.
            float[] positions = { -0.60f, 0.60f };
            var particles = new ParticleSystem.Particle[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                particles[i] = new ParticleSystem.Particle
                {
                    position = new Vector3(
                        positions[i] * lengthScale,
                        0f,
                        -0.01f),
                    velocity = Vector3.zero,
                    startLifetime = 2f,
                    remainingLifetime = 2f,
                    startSize3D = new Vector3(
                        0.30f * lengthScale,
                        0.22f * thicknessScale,
                        1f),
                    startColor = color
                };
            }
            ps.SetParticles(particles, particles.Length);
            ps.Play();
        }

        private static float ReadPositiveScale(float value)
        {
            return value > 0f ? value : 1f;
        }

        private static void RestartWake(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
        }

        private static void StopEmitting(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private static void StopAndClear(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static ParticleSystem EnsureParticleSystem(
            Transform parent,
            string name)
        {
            Transform child = EnsureChild(parent, name);
            ParticleSystem ps = child.GetComponent<ParticleSystem>();
            if (ps == null)
                ps = child.gameObject.AddComponent<ParticleSystem>();
            // AddComponent starts a default ParticleSystem immediately. The
            // system must be fully stopped before mutating duration-sensitive
            // main-module settings, and pooled systems may still have tails.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return ps;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var childObject = new GameObject(name);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }
    }
}
