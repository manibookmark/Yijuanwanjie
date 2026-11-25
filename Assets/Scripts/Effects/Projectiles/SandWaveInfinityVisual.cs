using System;
using System.Collections;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Owns three one-shot Infinity channels for one sand wave. Native modules
    /// are configured only after activation settles; a second two-frame gate
    /// precedes the sole Play. Afterwards only Transforms and owned materials
    /// move/fade. Contacts and generated-card completion belong to the caller.
    /// </summary>
    internal sealed class SandWaveInfinityVisual : MonoBehaviour
    {
        internal const string MaterialResource = "Effects/LiushaSandInfinity";
        internal const float TailDuration = 0.72f;
        private const string NativeHostResource = "Effects/Boss/BossNaturalBurst";
        private const int FullWaveCapacity = 1500;
        private const int MinimumWaveCapacity = 96;
        private const int ConcurrentCapacityBudget = 24576;
        private static int _allocatedCapacity;
        private readonly InfinityParticleSystem[] _systems = new InfinityParticleSystem[3];
        private readonly Material[] _materials = new Material[3];
        private bool _ready, _playIssued, _released, _ownsCapacity;
        private int _waveIndex, _capacity;
        private float _duration, _playedTime;
        private MonoBehaviour _host;
        private Func<bool> _attackActive;

        internal static SandWaveInfinityVisual Create(Transform parent, MonoBehaviour host,
            Func<bool> attackActive, int waveIndex, int capacity, float duration)
        {
            GameObject prefab = Resources.Load<GameObject>(NativeHostResource);
            Material material = Resources.Load<Material>(MaterialResource);
            if (prefab == null || material == null)
                throw new InvalidOperationException("缺少流沙诀Infinity粒子宿主或沙质材质。");
            var root = new GameObject("LiushaInfinitySandWave");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, 10000f, 0f);
            var visual = root.AddComponent<SandWaveInfinityVisual>();
            visual._host = host;
            visual._attackActive = attackActive;
            visual._waveIndex = waveIndex;
            int requestedCapacity = Mathf.Clamp(capacity, MinimumWaveCapacity, FullWaveCapacity);
            // Rapid casts share a cosmetic density budget. Keep a small real
            // wave even when saturated; authority never queues or drops a cast.
            visual._capacity = Mathf.Clamp(ConcurrentCapacityBudget - _allocatedCapacity,
                MinimumWaveCapacity, requestedCapacity);
            _allocatedCapacity += visual._capacity;
            visual._ownsCapacity = true;
            visual._duration = duration;
            for (int layer = 0; layer < 3; layer++)
            {
                GameObject channel = Instantiate(prefab, root.transform, false);
                channel.name = layer == 0 ? "RollingSandBody" :
                    (layer == 1 ? "FallingSandGrains" : "FineSandDust");
                visual._systems[layer] = channel.GetComponent<InfinityParticleSystem>();
                if (visual._systems[layer] == null)
                {
                    Destroy(root);
                    throw new InvalidOperationException("流沙诀宿主缺少InfinityParticleSystem。");
                }
                visual._materials[layer] = new Material(material)
                {
                    name = "LiushaInfinityLayer" + layer,
                    enableInstancing = true
                };
                visual._materials[layer].SetFloat("_Layer", layer);
                visual._materials[layer].SetFloat("_Opacity", layer == 0 ? 0.63f :
                    (layer == 1 ? 0.95f : 0.65f));
                visual._materials[layer].SetFloat("_CastFade", 1f);
            }
            return visual;
        }

        internal IEnumerator Prepare()
        {
            // No native access in Create/Awake/OnEnable or inactive hierarchies.
            yield return null;
            yield return null;
            if (this == null || !isActiveAndEnabled) yield break;
            for (int layer = 0; layer < 3; layer++) Configure(layer);
            yield return null;
            yield return null;
            if (this != null && isActiveAndEnabled) _ready = true;
        }

        internal void Begin(Vector3 position)
        {
            if (!_ready || _playIssued || !isActiveAndEnabled)
                throw new InvalidOperationException("Infinity砂浪未通过双门控初始化。");
            // The host itself stays at a stable identity transform. Only the
            // three World-space emitter transforms follow the authority crest.
            transform.position = Vector3.zero;
            SetPosition(position, 0f);
            _playIssued = true;
            for (int layer = 0; layer < 3; layer++)
                _systems[layer].Play(withChildren: false, restart: false);
        }

        internal void SetPosition(Vector3 crest, float normalizedTime)
        {
            float pulse = Mathf.Sin(normalizedTime * Mathf.PI);
            for (int layer = 0; layer < 3; layer++)
            {
                float sway = Mathf.Sin(normalizedTime * 12f + _waveIndex * 1.7f + layer);
                Vector3 offset = layer == 0
                    ? new Vector3(-0.22f, 0.24f + pulse * 0.16f + sway * 0.04f, 0f)
                    : (layer == 1 ? new Vector3(-0.12f, 0.40f + sway * 0.10f, 0f)
                        : new Vector3(-0.70f, 0.25f + pulse * 0.12f, 0f));
                _systems[layer].transform.position = crest + offset;
            }
        }

        internal void Release(bool completed)
        {
            if (_released) return;
            _released = true;
            _attackActive = null;
            if (!completed || !_playIssued || !isActiveAndEnabled)
                Destroy(gameObject);
            else
                StartCoroutine(FadeTail());
        }

        private IEnumerator FadeTail()
        {
            // Emission ends through non-looping duration, never Stop/Emit.
            // This visual tail does not hold the attack completion receipt.
            float elapsed = 0f;
            while (elapsed < TailDuration)
            {
                elapsed += Time.deltaTime;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, elapsed / TailDuration);
                for (int layer = 0; layer < 3; layer++)
                    if (_materials[layer] != null) _materials[layer].SetFloat("_CastFade", fade);
                yield return null;
            }
            Destroy(gameObject);
        }

        private void Configure(int layer)
        {
            InfinityParticleSystem system = _systems[layer];
            // The reused authored host is active and playOnAwake=false. This
            // pre-Play Stop/Clear is only used after the first stable-frame gate.
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            bool body = layer == 0, dust = layer == 2;
            int bodyCapacity = Mathf.Max(8, Mathf.FloorToInt(_capacity * 0.08f));
            int dustCapacity = Mathf.Max(8, Mathf.FloorToInt(_capacity * 0.07f));
            int capacity = body ? bodyCapacity : (dust ? dustCapacity : _capacity - bodyCapacity - dustCapacity);
            float density = (float)_capacity / FullWaveCapacity;
            var main = system.main;
            main.duration = _duration;
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.useUnscaledTime = false;
            main.simulationSpace = InfinitySimulationSpace.World;
            main.startDelay = new InfinityMinMaxCurve(0f);
            main.startLifetime = Range(body ? 0.24f : (dust ? 0.32f : 0.25f),
                body ? 0.42f : (dust ? 0.62f : 0.46f));
            main.startSpeed = new InfinityMinMaxCurve(0f);
            main.startSize3D = false;
            main.startSize = Range(body ? 0.52f : (dust ? 0.36f : 0.025f),
                body ? 0.88f : (dust ? 0.72f : 0.073f));
            main.startRotation3D = false;
            main.startRotation = Range(-Mathf.PI, Mathf.PI);
            main.startColor = new InfinityMinMaxGradient(new Color(0.80f, 0.80f, 0.80f, 1f), Color.white);
            main.gravityModifier = new InfinityMinMaxCurve(body ? 0.22f : (dust ? 0.015f : 0.72f));
            main.maxParticles = capacity;
            main.autoRandomSeed = false;
            main.randomSeed = unchecked((int)(0xA511E9B3u + (uint)_waveIndex * 2654435761u + (uint)layer * 97u));

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = Curve(new AnimationCurve(new Keyframe(0f, 0.35f),
                new Keyframe(0.08f, 1f), new Keyframe(0.72f, 1f),
                new Keyframe(0.94f, 0.32f), new Keyframe(1f, 0f)),
                (body ? 190f : (dust ? 120f : 2800f)) * density);
            emission.rateOverDistance = new InfinityMinMaxCurve(0f);
            emission.SetBursts(Array.Empty<InfinityEmissionBurst>());

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = InfinityShapeType.Circle;
            shape.radius = body ? 0.36f : (dust ? 0.43f : 0.34f);
            shape.radiusThickness = 1f;
            shape.arcValue = 360f;
            shape.arcMode = InfinityMultiModeValue.Random;
            shape.arcSpread = 0f;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(body ? 1.5f : 1.2f, body ? 0.72f : 0.58f, 1f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = InfinitySimulationSpace.World;
            velocity.x = Range(body ? -1.8f : (dust ? -1.6f : -2.2f), body ? -0.4f : -0.25f);
            velocity.y = Range(body ? 0.45f : (dust ? 0.35f : 0.9f), body ? 1.45f : (dust ? 0.85f : 2.6f));
            velocity.z = new InfinityMinMaxCurve(0f);
            velocity.orbitalX = velocity.orbitalY = new InfinityMinMaxCurve(0f);
            velocity.orbitalOffsetX = velocity.orbitalOffsetY = velocity.orbitalOffsetZ = new InfinityMinMaxCurve(0f);
            velocity.orbitalZ = new InfinityMinMaxCurve(body ? 0.9f : (dust ? 0.12f : 0.2f));
            velocity.radial = new InfinityMinMaxCurve(0.08f);
            velocity.speedModifier = new InfinityMinMaxCurve(1f);

            var noise = system.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new InfinityMinMaxCurve(body ? 0.10f : 0.06f);
            noise.strengthY = new InfinityMinMaxCurve(body ? 0.18f : 0.10f);
            noise.strengthZ = new InfinityMinMaxCurve(0f);
            noise.frequency = body ? 1.8f : 2.4f;
            noise.scrollSpeed = new InfinityMinMaxCurve(0.65f);
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = InfinityNoiseQuality.Medium;

            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.10f),
                    new GradientAlphaKey(0.8f, 0.65f), new GradientAlphaKey(0f, 1f) });
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new InfinityMinMaxGradient(fade);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = false;
            size.size = Curve(new AnimationCurve(new Keyframe(0f, body || dust ? 0.65f : 1f),
                new Keyframe(0.3f, 1f), new Keyframe(1f, dust ? 1.55f : 0.65f)), 1f);

            var renderer = system.renderer;
            renderer.material = _materials[layer];
            renderer.renderMode = InfinityRenderMode.Billboard;
            renderer.sortingOrder = dust ? 44 : 45 + layer;
            renderer.customBatchKey = unchecked(GetInstanceID() * 3 + layer);
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0f;
        }

        private static InfinityMinMaxCurve Range(float minimum, float maximum)
            => new InfinityMinMaxCurve(InfinityCurveStateMode.TwoConstants, minimum, maximum, 1f, null, null);

        private static InfinityMinMaxCurve Curve(AnimationCurve curve, float multiplier)
            => new InfinityMinMaxCurve(InfinityCurveStateMode.Curve, 0f, multiplier, multiplier, curve, curve);

        private void Update()
        {
            // Cleanup must not depend on a coroutine finally when its host is
            // disabled/destroyed. Normal completion lets the visual tail finish.
            if (_host == null || !_host.isActiveAndEnabled ||
                (!_released && _attackActive != null && !_attackActive()))
            {
                Destroy(gameObject);
                return;
            }
            if (_playIssued)
            {
                _playedTime += Time.deltaTime;
                if (_playedTime > _duration + TailDuration + 0.5f)
                    Destroy(gameObject);
            }
        }

        private void OnDisable()
        {
            // Unity/Infinity owns native deactivation. No Stop/Clear/module call.
            _ready = false;
            StopAllCoroutines();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_ownsCapacity)
            {
                _allocatedCapacity = Mathf.Max(0, _allocatedCapacity - _capacity);
                _ownsCapacity = false;
            }
            foreach (Material material in _materials)
                if (material != null) Destroy(material);
        }
    }
}
