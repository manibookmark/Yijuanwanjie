using System;
using System.Collections;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns one automatic, local-space book ornament. It never transfers MP or
    /// holds the book's manifestation callback. Native configuration and Play
    /// have separate stable-frame gates; playback changes only owned material.
    /// </summary>
    internal sealed class BookMoteInfinityVisual : MonoBehaviour
    {
        private const string HostResource = "Effects/Boss/BossNaturalBurst";
        private const string MaterialResource = "Effects/Boss/BossActionParticle_Infinity";
        private const float EmissionDuration = 0.12f;
        private InfinityParticleSystem _system;
        private Material _material;
        private Color _color;
        private int _count;
        private bool _playIssued;

        internal static BookMoteInfinityVisual Create(Transform parent, Color color, int count)
        {
            GameObject prefab = Resources.Load<GameObject>(HostResource);
            Material source = Resources.Load<Material>(MaterialResource);
            if (prefab == null || source == null)
                throw new InvalidOperationException("天书灵气缺少Infinity宿主或粒子材质。");

            var root = new GameObject("BookManaMotesInfinity");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * 10000f;
            var owner = root.AddComponent<BookMoteInfinityVisual>();
            owner._color = color;
            owner._count = Mathf.Clamp(count, 16, 112);
            GameObject channel = Instantiate(prefab, root.transform, false);
            channel.name = "BookManaMoteChannel";
            owner._system = channel.GetComponent<InfinityParticleSystem>();
            if (owner._system == null)
            {
                Destroy(root);
                throw new InvalidOperationException("天书灵气宿主缺少InfinityParticleSystem。");
            }

            owner._material = new Material(source)
            {
                name = "BookManaMotesRuntime",
                enableInstancing = true
            };
            owner._material.SetColor("_DeepColor", color);
            owner._material.SetColor("_CoreColor", color);
            owner._material.SetColor("_HighlightColor", color);
            owner._material.SetFloat("_WispShape", 0f);
            owner._material.SetFloat("_SmokeBlend", 0f);
            owner._material.SetFloat("_Softness", 0.35f);
            owner._material.SetFloat("_Opacity", 0f);
            owner.StartCoroutine(owner.Prepare());
            return owner;
        }

        internal void SetOpacity(float opacity)
        {
            // Material properties are managed; no runtime native module is touched.
            if (_material != null) _material.SetFloat("_Opacity", Mathf.Clamp01(opacity));
        }

        private IEnumerator Prepare()
        {
            yield return null;
            yield return null;
            if (!isActiveAndEnabled || _system == null) yield break;
            Configure();
            yield return null;
            yield return null;
            if (!isActiveAndEnabled || _system == null || _playIssued) yield break;
            transform.localPosition = Vector3.zero;
            _playIssued = true;
            _system.Play(withChildren: false, restart: false);
        }

        private void Configure()
        {
            // The bare authored host is active and playOnAwake=false. All native
            // access is confined to this stopped-state preparation and the sole Play.
            _system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _system.main;
            main.duration = EmissionDuration;
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.simulationSpace = InfinitySimulationSpace.Local;
            main.startDelay = new InfinityMinMaxCurve(0f);
            main.startLifetime = Range(0.12f, 0.20f);
            main.startSpeed = new InfinityMinMaxCurve(0f);
            main.startSize3D = false;
            float sizeScale = Mathf.Lerp(1f, 1.08f, Mathf.InverseLerp(48f, 112f, _count));
            main.startSize = Range(0.034f * sizeScale, 0.048f * sizeScale);
            main.startRotation3D = false;
            main.startRotation = Range(-Mathf.PI, Mathf.PI);
            main.startColor = new InfinityMinMaxGradient(new Color(1f, 1f, 1f, _color.a));
            main.gravityModifier = new InfinityMinMaxCurve(0f);
            main.maxParticles = _count;
            main.autoRandomSeed = false;
            main.randomSeed = GetInstanceID();

            var emission = _system.emission;
            emission.enabled = true;
            emission.rateOverTime = new InfinityMinMaxCurve(_count / EmissionDuration);
            emission.rateOverDistance = new InfinityMinMaxCurve(0f);
            emission.SetBursts(Array.Empty<InfinityEmissionBurst>());

            var shape = _system.shape;
            shape.enabled = true;
            shape.shapeType = InfinityShapeType.Circle;
            shape.radius = 0.5f;
            shape.radiusThickness = 1f;
            shape.arcValue = 360f;
            shape.arcMode = InfinityMultiModeValue.Random;
            shape.arcSpread = 0f;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(1f, 0.62f, 1f);

            var velocity = _system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = InfinitySimulationSpace.Local;
            velocity.x = velocity.y = velocity.z = new InfinityMinMaxCurve(0f);
            velocity.orbitalX = velocity.orbitalY = new InfinityMinMaxCurve(0f);
            velocity.orbitalOffsetX = velocity.orbitalOffsetY = velocity.orbitalOffsetZ = new InfinityMinMaxCurve(0f);
            velocity.orbitalZ = new InfinityMinMaxCurve(0.8f);
            velocity.radial = new InfinityMinMaxCurve(0.05f);
            velocity.speedModifier = new InfinityMinMaxCurve(1f);

            var noise = _system.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new InfinityMinMaxCurve(0.015f);
            noise.strengthY = new InfinityMinMaxCurve(0.018f);
            noise.strengthZ = new InfinityMinMaxCurve(0f);
            noise.frequency = 1.8f;
            noise.scrollSpeed = new InfinityMinMaxCurve(0.8f);
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = InfinityNoiseQuality.Low;

            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            var lifetimeColor = _system.colorOverLifetime;
            lifetimeColor.enabled = true;
            lifetimeColor.color = new InfinityMinMaxGradient(fade);
            var size = _system.sizeOverLifetime;
            size.enabled = false;

            var renderer = _system.renderer;
            renderer.material = _material;
            renderer.renderMode = InfinityRenderMode.Billboard;
            renderer.sortingOrder = 87;
            renderer.customBatchKey = GetInstanceID();
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0f;
        }

        private static InfinityMinMaxCurve Range(float minimum, float maximum)
            => new InfinityMinMaxCurve(InfinityCurveStateMode.TwoConstants, minimum, maximum, 1f, null, null);

        private void OnDisable()
        {
            StopAllCoroutines();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
