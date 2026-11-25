using System;
using System.Collections;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;
using WanjieScroll.Effects.Terrain;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns automatic fire tongues, embers and a sparse smoke wake. Each native
    /// channel is prepared stopped behind two frame gates and played once.
    /// Receipt timing belongs to callers; subsequent intensity/fade is managed.
    /// </summary>
    internal sealed class FireInfinityParticleField : MonoBehaviour
    {
        private const string HostResource = "Effects/Boss/BossNaturalBurst";
        private const string MaterialResource = "Effects/FireInfinityParticle";
        private const int CapacityBudget = 8192;
        private static int _allocatedCapacity;

        private struct Parameters
        {
            internal bool Loop, WorldSpace;
            internal float Duration, Radius, Height, Width, RiseSpeed, FlameRate, CoreStrength, GlowStrength, Radiance;
            internal Color Deep, Flame, Core;
            internal int SortingOrder, Capacity;
        }

        private readonly InfinityParticleSystem[] _systems = new InfinityParticleSystem[3];
        private readonly Material[] _materials = new Material[4];
        private Parameters _parameters;
        private MonoBehaviour _host;
        private Func<bool> _active;
        private Mesh _glowMesh;
        private bool _ready, _pendingBegin, _playIssued, _capacityHeld;
        private int _capacity;
        private Vector3 _beginPosition;
        private float _playedTime;
        private float _opacity = 1f;

        internal static FireInfinityParticleField PrepareIgnition(Transform parent,
            MonoBehaviour host, Func<bool> active, float density)
        {
            return Create(parent, host, active, new Parameters
            {
                Duration = 0.10f, Radius = 0.56f, Height = 1.35f, Width = 0.48f,
                RiseSpeed = 1.4f, FlameRate = 900f * Mathf.Clamp(density, 0.3f, 1f),
                Deep = new Color(0.56f, 0.055f, 0.012f),
                Flame = new Color(1f, 0.26f, 0.025f), Core = new Color(1f, 0.93f, 0.62f),
                CoreStrength = 0.9f, GlowStrength = 0.34f, Radiance = 1.75f,
                SortingOrder = 46, Capacity = 192, WorldSpace = true
            }, false);
        }

        internal static FireInfinityParticleField CreateTerrain(Transform parent,
            TerrainVfxProfile profile, int stacks, bool persistent, bool trigger = false)
        {
            int burst = trigger
                ? Mathf.Clamp(profile.triggerBurst + Mathf.Min(stacks, 12) * 2, 16, 48)
                : Mathf.Clamp(profile.placementBurst, 4, 18);
            float rate = persistent
                ? Mathf.Min(36f, profile.baseEmission + profile.emissionPerStack * Mathf.Max(0, stacks - 1))
                : burst / 0.10f;
            rate *= persistent ? 2.4f : (trigger ? 2.1f : 2.4f);
            return Create(parent, null, null, new Parameters
            {
                Loop = persistent, Duration = persistent ? 1f : 0.10f,
                Radius = profile.radius * 0.92f, Height = profile.height * (trigger ? 1.75f : 1.5f),
                Width = 0.42f * profile.flameWidthScale,
                RiseSpeed = trigger ? profile.triggerWaveHeight * 0.75f : profile.height * 0.5f,
                FlameRate = rate, Deep = profile.secondaryColor, Flame = profile.primaryColor,
                Core = profile.coreColor, CoreStrength = Mathf.Clamp01(profile.coreIntensity + 0.2f),
                GlowStrength = persistent ? Mathf.Clamp(profile.groundGlowStrength * 2.3f, 0.16f, 0.35f) : 0.38f,
                Radiance = trigger ? 1.85f : 1.55f,
                SortingOrder = profile.sortingOrder + (trigger ? 5 : 2),
                Capacity = persistent ? 144 : 192
            }, true);
        }

        private static FireInfinityParticleField Create(Transform parent, MonoBehaviour host,
            Func<bool> active, Parameters parameters, bool automaticBegin)
        {
            GameObject prefab = Resources.Load<GameObject>(HostResource);
            Material material = Resources.Load<Material>(MaterialResource);
            if (prefab == null || material == null)
                throw new InvalidOperationException("火焰Infinity缺少原生宿主或专用材质。");
            var root = new GameObject(parameters.Loop ? "InfinityGroundFire" : "InfinityFireIgnition");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * 10000f;
            var owner = root.AddComponent<FireInfinityParticleField>();
            owner._parameters = parameters;
            owner._host = host;
            owner._active = active;
            owner._pendingBegin = automaticBegin;
            owner._beginPosition = parent != null ? parent.position : Vector3.zero;
            // Soft cosmetic budget: every real attack/terrain anchor retains its field.
            owner._capacity = Mathf.Clamp(CapacityBudget - _allocatedCapacity, 48, parameters.Capacity);
            _allocatedCapacity += owner._capacity;
            owner._capacityHeld = true;
            for (int layer = 0; layer < 3; layer++)
            {
                GameObject channel = Instantiate(prefab, root.transform, false);
                channel.name = layer == 0 ? "FireTongues" : (layer == 1 ? "FireEmbers" : "ThinSmoke");
                // The billboard's lower edge starts near the ground anchor;
                // its larger body grows upward instead of below the shadow.
                channel.transform.localPosition = layer == 0
                    ? Vector3.up * parameters.Height * 0.42f : Vector3.zero;
                owner._systems[layer] = channel.GetComponent<InfinityParticleSystem>();
                if (owner._systems[layer] == null)
                {
                    Destroy(root);
                    throw new InvalidOperationException("火焰宿主缺少InfinityParticleSystem。");
                }
                owner._materials[layer] = owner.CreateMaterial(material, layer);
            }
            if (parameters.GlowStrength > 0f) owner.CreateGroundGlow(material);
            owner.StartCoroutine(owner.Prepare());
            return owner;
        }

        internal void Begin(Vector3 worldPosition)
        {
            if (_playIssued || !CanContinue()) return;
            _beginPosition = worldPosition;
            _pendingBegin = true;
            if (_ready) BeginReady();
        }

        internal void SetOpacity(float opacity)
        {
            _opacity = Mathf.Clamp01(opacity);
            for (int layer = 0; layer < _materials.Length; layer++)
                if (_materials[layer] != null)
                    _materials[layer].SetFloat("_Opacity", _opacity *
                        (layer == 3 ? _parameters.GlowStrength * HeatFade : 1f));
        }

        private IEnumerator Prepare()
        {
            yield return null;
            yield return null;
            if (!CanContinue()) yield break;
            for (int layer = 0; layer < 3; layer++) Configure(layer);
            yield return null;
            yield return null;
            if (!CanContinue()) yield break;
            _ready = true;
            if (_pendingBegin) BeginReady();
        }

        private void BeginReady()
        {
            if (!_ready || _playIssued || !CanContinue()) return;
            if (_parameters.WorldSpace) transform.position = _beginPosition;
            else transform.localPosition = Vector3.zero;
            _playIssued = true;
            for (int layer = 0; layer < 3; layer++)
                _systems[layer].Play(withChildren: false, restart: false);
        }

        private Material CreateMaterial(Material source, int layer)
        {
            var material = new Material(source) { name = "OwnedInfinityFireLayer" + layer, enableInstancing = true };
            material.SetColor("_DeepColor", _parameters.Deep);
            material.SetColor("_FlameColor", _parameters.Flame);
            material.SetColor("_CoreColor", _parameters.Core);
            material.SetFloat("_CoreStrength", _parameters.CoreStrength);
            material.SetFloat("_Radiance", layer == 2 ? 1f : _parameters.Radiance);
            material.SetFloat("_Layer", layer);
            material.SetFloat("_Opacity", layer == 3 ? _parameters.GlowStrength : 1f);
            return material;
        }

        private void Configure(int layer)
        {
            bool flame = layer == 0, smoke = layer == 2;
            InfinityParticleSystem system = _systems[layer];
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            float density = (float)_capacity / _parameters.Capacity;
            var main = system.main;
            main.duration = _parameters.Duration;
            main.loop = _parameters.Loop;
            main.prewarm = false;
            main.playOnAwake = false;
            main.useUnscaledTime = false;
            main.simulationSpace = _parameters.WorldSpace ? InfinitySimulationSpace.World : InfinitySimulationSpace.Local;
            main.scalingMode = InfinityScalingMode.Hierarchy;
            main.startDelay = new InfinityMinMaxCurve(0f);
            main.startLifetime = _parameters.Loop
                ? (flame ? Range(0.40f, 0.62f) : (smoke ? Range(0.35f, 0.55f) : Range(0.22f, 0.38f)))
                : (flame || smoke ? Range(0.24f, 0.38f) : Range(0.20f, 0.32f));
            main.startSpeed = new InfinityMinMaxCurve(0f);
            main.startSize3D = true;
            main.startSizeX = flame ? Range(_parameters.Width * 0.85f, _parameters.Width * 1.25f)
                : (smoke ? Range(0.16f, 0.28f) : Range(0.018f, 0.042f));
            float height = _parameters.Height;
            main.startSizeY = flame ? Range(height * 0.85f, height * 1.25f)
                : (smoke ? Range(0.26f, 0.44f) : Range(0.045f, 0.08f));
            main.startSizeZ = new InfinityMinMaxCurve(1f);
            main.startRotation3D = false;
            main.startRotation = Range(-0.18f, 0.18f);
            main.startColor = new InfinityMinMaxGradient(Color.white);
            main.gravityModifier = new InfinityMinMaxCurve(flame || smoke ? 0f : 0.08f);
            main.maxParticles = flame ? _capacity - _capacity / 5 - _capacity / 10 : (smoke ? _capacity / 10 : _capacity / 5);
            main.autoRandomSeed = false;
            main.randomSeed = unchecked(GetInstanceID() * 3 + layer);

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = new InfinityMinMaxCurve(_parameters.FlameRate * density *
                (flame ? 1f : (smoke ? 0.075f : 0.22f)));
            emission.rateOverDistance = new InfinityMinMaxCurve(0f);
            emission.SetBursts(Array.Empty<InfinityEmissionBurst>());
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = InfinityShapeType.Circle;
            shape.radius = _parameters.Radius;
            shape.radiusThickness = 0.85f;
            shape.arcValue = 360f;
            shape.arcMode = InfinityMultiModeValue.Random;
            shape.arcSpread = 0f;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(1f, 0.12f, 1f);
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = _parameters.WorldSpace ? InfinitySimulationSpace.World : InfinitySimulationSpace.Local;
            velocity.x = Range(flame ? -0.12f : -0.2f, flame ? 0.12f : 0.2f);
            velocity.y = Range(_parameters.RiseSpeed * (smoke ? 0.4f : 0.7f),
                _parameters.RiseSpeed * (smoke ? 0.65f : 1.35f));
            velocity.z = new InfinityMinMaxCurve(0f);
            velocity.orbitalX = velocity.orbitalY = velocity.orbitalZ = new InfinityMinMaxCurve(0f);
            velocity.orbitalOffsetX = velocity.orbitalOffsetY = velocity.orbitalOffsetZ = new InfinityMinMaxCurve(0f);
            velocity.radial = new InfinityMinMaxCurve(0f);
            velocity.speedModifier = new InfinityMinMaxCurve(1f);
            var noise = system.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new InfinityMinMaxCurve(flame ? 0.035f : 0.025f);
            noise.strengthY = new InfinityMinMaxCurve(flame ? 0.055f : 0.04f);
            noise.strengthZ = new InfinityMinMaxCurve(0f);
            noise.frequency = 1.35f;
            noise.scrollSpeed = new InfinityMinMaxCurve(0.6f);
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = InfinityNoiseQuality.Low;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(flame ? 1f : 0.85f, 0.08f),
                    new GradientAlphaKey(flame ? 0.9f : 0.48f, 0.60f), new GradientAlphaKey(0f, 1f) });
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new InfinityMinMaxGradient(gradient);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = false;
            var sizeCurve = new AnimationCurve(new Keyframe(0f, flame ? 0.78f : 0.35f),
                new Keyframe(0.12f, 1f), new Keyframe(0.65f, smoke ? 1.15f : 0.88f),
                new Keyframe(1f, smoke ? 1.3f : 0.28f));
            size.size = new InfinityMinMaxCurve(InfinityCurveStateMode.Curve, 0f, 1f, 1f, sizeCurve, sizeCurve);
            var renderer = system.renderer;
            renderer.material = _materials[layer];
            renderer.renderMode = InfinityRenderMode.Billboard;
            renderer.sortingOrder = _parameters.SortingOrder + (flame ? 0 : (smoke ? -1 : 2));
            renderer.customBatchKey = unchecked(GetInstanceID() * 3 + layer);
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0f;
        }

        private void CreateGroundGlow(Material source)
        {
            _materials[3] = CreateMaterial(source, 3);
            _materials[3].enableInstancing = false;
            var glow = new GameObject("ManagedGroundHeat", typeof(MeshFilter), typeof(MeshRenderer));
            glow.transform.SetParent(transform, false);
            glow.transform.localScale = new Vector3(_parameters.Radius * 2.4f, _parameters.Radius * 0.45f, 1f);
            _glowMesh = new Mesh { name = "FireGroundHeatQuad" };
            _glowMesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f), new Vector3(0.5f, -0.5f) };
            _glowMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            _glowMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _glowMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _glowMesh.RecalculateBounds();
            glow.GetComponent<MeshFilter>().sharedMesh = _glowMesh;
            var renderer = glow.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _materials[3];
            renderer.sortingOrder = _parameters.SortingOrder - 2;
        }

        private static InfinityMinMaxCurve Range(float minimum, float maximum)
            => new InfinityMinMaxCurve(InfinityCurveStateMode.TwoConstants, minimum, maximum, 1f, null, null);

        private bool CanContinue()
            => isActiveAndEnabled && (_active == null ||
                (_host != null && _host.isActiveAndEnabled && _active()));

        private float HeatFade => _parameters.Loop || !_playIssued ? 1f :
            1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.48f, _playedTime));

        private void Update()
        {
            if (_active != null && (_host == null || !_host.isActiveAndEnabled || !_active()))
            { Destroy(gameObject); return; }
            if (_playIssued && !_parameters.Loop)
            {
                _playedTime += Time.deltaTime;
                // The added heat flash follows the same short ignition tail.
                if (_materials[3] != null)
                    _materials[3].SetFloat("_Opacity", _opacity * _parameters.GlowStrength *
                        HeatFade);
                if (_playedTime >= _parameters.Duration + 0.58f) Destroy(gameObject);
            }
        }

        private void OnDisable()
        {
            _ready = false;
            StopAllCoroutines();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            _active = null;
            if (_capacityHeld) { _allocatedCapacity = Mathf.Max(0, _allocatedCapacity - _capacity); _capacityHeld = false; }
            foreach (Material material in _materials) if (material != null) Destroy(material);
            if (_glowMesh != null) Destroy(_glowMesh);
        }
    }
}
