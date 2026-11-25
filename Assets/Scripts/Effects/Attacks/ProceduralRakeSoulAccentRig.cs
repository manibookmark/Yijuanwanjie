using System;
using System.Collections;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;

namespace WanjieScroll.Effects.Attacks
{
    /// <summary>Owns the optional native soul flames at the evolved rake head.
    /// The sweep retains its weapon shape, contacts and clock. This owner alone
    /// prepares and plays its two active channels, then only updates managed
    /// transforms/materials; it never touches native modules after Play.</summary>
    internal sealed class ProceduralRakeSoulAccentRig : MonoBehaviour
    {
        private const float RingStart = .14f;
        private const float RingEnd = .58f;
        private readonly InfinityParticleSystem[] _systems = new InfinityParticleSystem[2];
        private readonly Material[] _materials = new Material[2];
        private int _count;
        private float _duration, _intensity, _intensity01;
        private bool _ready, _requested, _played, _cleared;

        internal static ProceduralRakeSoulAccentRig Create(
            Transform parent, VfxProfile profile, float duration)
        {
            if (profile == null || profile.rakeSoulAccentIntensity <= 0f ||
                profile.rakeSoulAccentParticleCount <= 0) return null;
            var prefab = Resources.Load<GameObject>("Effects/Boss/BossNaturalBurst");
            var sourceMaterial = Resources.Load<Material>("Effects/Boss/BossActionParticle_Infinity");
            if (prefab == null || sourceMaterial == null)
                throw new InvalidOperationException("九齿噬魂缺少Infinity魂焰宿主或材质。");
            var root = new GameObject("RakeSoulFlamesInfinity");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * 10000f;
            var owner = root.AddComponent<ProceduralRakeSoulAccentRig>();
            owner._count = Mathf.Clamp(profile.rakeSoulAccentParticleCount, 1, 96);
            owner._duration = Mathf.Max(.42f, duration);
            owner._intensity = Mathf.Max(0f, profile.rakeSoulAccentIntensity);
            owner._intensity01 = Mathf.Clamp01((owner._intensity - 1f) / 2f);
            for (int i = 0; i < owner._systems.Length; i++)
            {
                if (i == 1 && owner._count <= 48) continue;
                owner._systems[i] = Instantiate(prefab, root.transform, false)
                    .GetComponent<InfinityParticleSystem>();
                if (owner._systems[i] == null)
                {
                    Destroy(root);
                    throw new InvalidOperationException("九齿噬魂宿主缺少InfinityParticleSystem。");
                }
                var material = new Material(sourceMaterial)
                { name = i == 0 ? "RakeSoulCrownRuntime" : "RakeSoulEmbersRuntime", enableInstancing = true };
                material.SetColor("_DeepColor", profile.rakeSoulAccentPrimaryColor);
                material.SetColor("_CoreColor", profile.rakeSoulAccentSecondaryColor);
                material.SetColor("_HighlightColor", Color.Lerp(profile.rakeSoulAccentSecondaryColor, Color.white, .16f));
                material.SetFloat("_SmokeBlend", i == 0 ? .72f : 0f);
                material.SetFloat("_SmokeStretch", 1.8f);
                material.SetFloat("_SmokeCurl", .65f);
                material.SetFloat("_WispShape", i == 0 ? .3f : .64f);
                material.SetFloat("_Softness", i == 0 ? .55f : .25f);
                material.SetFloat("_Opacity", 0f);
                owner._materials[i] = material;
            }
            owner.StartCoroutine(owner.Prepare());
            return owner;
        }

        internal void Render(Vector3 head, float motion, float fade)
        {
            if (_cleared) return;
            // Counter the sweep parent's rotation so the crown stays at the
            // head in the board plane and never migrates to the handle.
            transform.position = head;
            transform.rotation = Quaternion.identity;
            float phase = Mathf.Clamp01(Mathf.InverseLerp(RingStart, RingEnd, motion));
            _materials[0].SetFloat("_Opacity", fade * Mathf.Sin(phase * Mathf.PI) *
                Mathf.Lerp(.48f, .98f, Mathf.Clamp01(_intensity / 2f)));
            if (_materials[1] != null)
                _materials[1].SetFloat("_Opacity", fade * Mathf.Lerp(.45f, .85f, Mathf.Clamp01(_intensity / 2.2f)));
            if (motion >= RingStart) _requested = true;
            BeginIfReady();
        }

        private IEnumerator Prepare()
        {
            yield return null;
            yield return null;
            if (!isActiveAndEnabled || _cleared) yield break;
            Configure(0);
            Configure(1);
            yield return null;
            yield return null;
            if (!isActiveAndEnabled || _cleared) yield break;
            _ready = true;
            BeginIfReady();
        }

        private void BeginIfReady()
        {
            if (!_ready || !_requested || _played || _cleared || !isActiveAndEnabled) return;
            _played = true;
            foreach (var system in _systems)
                if (system != null) system.Play(withChildren: false, restart: false);
        }

        private void Configure(int layer)
        {
            if (_systems[layer] == null) return;
            bool crown = layer == 0;
            int count = crown ? Mathf.Min(_count, 48) : _count - 48;
            float emissionDuration = _duration * (crown ? RingEnd - RingStart : .72f);
            var system = _systems[layer];
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.duration = emissionDuration;
            main.loop = main.prewarm = main.playOnAwake = main.useUnscaledTime = false;
            main.simulationSpace = InfinitySimulationSpace.Local;
            main.startDelay = new InfinityMinMaxCurve(0f);
            main.startLifetime = crown ? Range(.14f, .24f) : Range(.12f, .22f);
            main.startSpeed = new InfinityMinMaxCurve(0f);
            main.startSize3D = main.startRotation3D = false;
            main.startSize = crown ? Range(.22f, .4f) : Range(.04f, .085f);
            main.startRotation = Range(-Mathf.PI, Mathf.PI);
            main.startColor = new InfinityMinMaxGradient(crown ? new Color(.5f, 1f, 1f, 1f) : Color.white);
            main.gravityModifier = new InfinityMinMaxCurve(0f);
            main.maxParticles = count;
            main.autoRandomSeed = false;
            main.randomSeed = unchecked(GetInstanceID() * 3 + layer);
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = new InfinityMinMaxCurve(count / emissionDuration);
            emission.rateOverDistance = new InfinityMinMaxCurve(0f);
            emission.SetBursts(Array.Empty<InfinityEmissionBurst>());
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = InfinityShapeType.Circle;
            shape.radius = crown ? Mathf.Lerp(.82f, 1.22f, _intensity01) : Mathf.Lerp(.96f, 1.45f, _intensity01);
            shape.radiusThickness = crown ? .18f : .84f;
            shape.arcValue = 360f;
            shape.arcMode = InfinityMultiModeValue.Random;
            shape.arcSpread = 0f;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(1f, crown ? Mathf.Lerp(.55f, .72f, _intensity01) : .5f, 1f);
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = InfinitySimulationSpace.Local;
            velocity.x = velocity.z = new InfinityMinMaxCurve(0f);
            velocity.y = crown ? Range(.25f, .6f) : Range(.15f, .45f);
            velocity.orbitalX = velocity.orbitalY = new InfinityMinMaxCurve(0f);
            velocity.orbitalZ = new InfinityMinMaxCurve(crown ? Mathf.Lerp(2.4f, 4.2f, _intensity01) : -2.4f);
            velocity.orbitalOffsetX = velocity.orbitalOffsetY = velocity.orbitalOffsetZ = new InfinityMinMaxCurve(0f);
            velocity.radial = new InfinityMinMaxCurve(crown ? .12f : .3f);
            velocity.speedModifier = new InfinityMinMaxCurve(1f);
            var noise = system.noise;
            noise.enabled = noise.separateAxes = true;
            noise.strengthX = new InfinityMinMaxCurve(crown ? .08f : .14f);
            noise.strengthY = new InfinityMinMaxCurve(crown ? .12f : .18f);
            noise.strengthZ = new InfinityMinMaxCurve(0f);
            noise.frequency = 2.2f;
            noise.scrollSpeed = new InfinityMinMaxCurve(.7f);
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = InfinityNoiseQuality.Medium;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f),
                    new GradientAlphaKey(.65f, .65f), new GradientAlphaKey(0f, 1f) });
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = new InfinityMinMaxGradient(gradient);
            var size = system.sizeOverLifetime;
            size.enabled = false;
            var renderer = system.renderer;
            renderer.material = _materials[layer];
            renderer.renderMode = InfinityRenderMode.Billboard;
            renderer.sortingOrder = 84 + layer;
            renderer.customBatchKey = unchecked(GetInstanceID() * 3 + layer);
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0f;
        }

        private static InfinityMinMaxCurve Range(float min, float max)
            => new InfinityMinMaxCurve(InfinityCurveStateMode.TwoConstants, min, max, 1f, null, null);

        internal void Clear()
        {
            if (_cleared) return;
            _cleared = true;
            _ready = false;
            StopAllCoroutines();
            Destroy(gameObject);
        }

        private void OnDisable() => Clear();

        private void OnDestroy()
        {
            foreach (var material in _materials)
                if (material != null) Destroy(material);
        }
    }
}
