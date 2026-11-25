using System;
using System.Collections;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;

namespace WanjieScroll.Effects
{
    /// <summary>Owns the loose violet debris of one row annihilation. The row
    /// band, proxy collapse and vacancy callbacks remain with ErasedEnemyEffect.
    /// Native preparation and Play are separately gated; no native calls follow Play.</summary>
    internal sealed class AnnihilationDebrisInfinityVisual : MonoBehaviour
    {
        private InfinityParticleSystem _system;
        private Material _material;
        private int _count;
        private float _intensity;
        private bool _ready, _requested, _played;

        internal static AnnihilationDebrisInfinityVisual Create(Transform parent,
            Color sourceColor, float intensity, int count)
        {
            var prefab = Resources.Load<GameObject>("Effects/Boss/BossNaturalBurst");
            var material = Resources.Load<Material>("Effects/Boss/BossActionParticle_Infinity");
            if (prefab == null || material == null)
                throw new InvalidOperationException("伏魔珠缺少Infinity碎屑宿主或材质。");
            var root = new GameObject("FumoAnnihilationDebrisInfinity");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * 10000f;
            var owner = root.AddComponent<AnnihilationDebrisInfinityVisual>();
            owner._count = Mathf.Clamp(count, 96, 256);
            owner._intensity = Mathf.Clamp(intensity, 0.5f, 3f);
            owner._system = Instantiate(prefab, root.transform, false).GetComponent<InfinityParticleSystem>();
            if (owner._system == null)
            {
                Destroy(root);
                throw new InvalidOperationException("伏魔珠碎屑宿主缺少InfinityParticleSystem。");
            }
            Color edge = Color.Lerp(sourceColor, new Color(0.78f, 0.18f, 1f, 1f), 0.68f);
            owner._material = new Material(material) { name = "FumoDebrisRuntime", enableInstancing = true };
            owner._material.SetColor("_DeepColor", edge * 0.7f);
            owner._material.SetColor("_CoreColor", edge);
            owner._material.SetColor("_HighlightColor", Color.Lerp(Color.white, edge, 0.44f));
            owner._material.SetFloat("_WispShape", 0.64f);
            owner._material.SetFloat("_SmokeBlend", 0f);
            owner._material.SetFloat("_Softness", 0.24f);
            owner._material.SetFloat("_Opacity", 0f);
            owner.StartCoroutine(owner.Prepare());
            return owner;
        }

        internal void SetProgress(float t)
        {
            float detonation = Mathf.Clamp01((t - 0.27f) / 0.55f);
            float opacity = Mathf.Sin(detonation * Mathf.PI) *
                Mathf.Clamp01(1f - Mathf.Max(0f, t - 0.52f) / 0.48f);
            if (_material != null) _material.SetFloat("_Opacity", opacity);
            if (t >= 0.27f) _requested = true;
            BeginIfReady();
        }

        private IEnumerator Prepare()
        {
            yield return null;
            yield return null;
            if (!isActiveAndEnabled || _system == null) yield break;
            Configure();
            yield return null;
            yield return null;
            if (!isActiveAndEnabled || _system == null) yield break;
            _ready = true;
            BeginIfReady();
        }

        private void BeginIfReady()
        {
            if (!_ready || !_requested || _played || !isActiveAndEnabled) return;
            transform.localPosition = Vector3.zero;
            _played = true;
            _system.Play(withChildren: false, restart: false);
        }

        private void Configure()
        {
            _system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _system.main;
            main.duration = 0.14f;
            main.loop = false;
            main.prewarm = main.playOnAwake = main.useUnscaledTime = false;
            main.simulationSpace = InfinitySimulationSpace.Local;
            main.startDelay = new InfinityMinMaxCurve(0f);
            main.startLifetime = Range(0.28f, 0.52f);
            main.startSpeed = new InfinityMinMaxCurve(0f);
            main.startSize3D = main.startRotation3D = false;
            main.startSize = Range(0.028f, 0.066f);
            main.startRotation = Range(-Mathf.PI, Mathf.PI);
            main.startColor = new InfinityMinMaxGradient(Color.white);
            main.gravityModifier = new InfinityMinMaxCurve(0f);
            main.maxParticles = _count;
            main.autoRandomSeed = false;
            main.randomSeed = GetInstanceID();
            var emission = _system.emission;
            emission.enabled = true;
            emission.rateOverTime = new InfinityMinMaxCurve(_count / 0.14f);
            emission.rateOverDistance = new InfinityMinMaxCurve(0f);
            emission.SetBursts(Array.Empty<InfinityEmissionBurst>());
            var shape = _system.shape;
            shape.enabled = true;
            shape.shapeType = InfinityShapeType.Circle;
            shape.radius = 0.18f;
            shape.radiusThickness = 0.15f;
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
            velocity.orbitalZ = new InfinityMinMaxCurve(0.7f);
            velocity.orbitalOffsetX = velocity.orbitalOffsetY = velocity.orbitalOffsetZ = new InfinityMinMaxCurve(0f);
            velocity.radial = Range(5.8f, 7f + _intensity * 1.5f);
            velocity.speedModifier = new InfinityMinMaxCurve(1f);
            var noise = _system.noise;
            noise.enabled = noise.separateAxes = true;
            noise.strengthX = new InfinityMinMaxCurve(0.08f);
            noise.strengthY = new InfinityMinMaxCurve(0.06f);
            noise.strengthZ = new InfinityMinMaxCurve(0f);
            noise.frequency = 2.4f;
            noise.scrollSpeed = new InfinityMinMaxCurve(0.8f);
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = InfinityNoiseQuality.Medium;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            var color = _system.colorOverLifetime;
            color.enabled = true;
            color.color = new InfinityMinMaxGradient(fade);
            var size = _system.sizeOverLifetime;
            size.enabled = false;
            var renderer = _system.renderer;
            renderer.material = _material;
            renderer.renderMode = InfinityRenderMode.Billboard;
            renderer.sortingOrder = 93;
            renderer.customBatchKey = GetInstanceID();
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0f;
        }

        private static InfinityMinMaxCurve Range(float min, float max)
            => new InfinityMinMaxCurve(InfinityCurveStateMode.TwoConstants, min, max, 1f, null, null);

        private void OnDisable()
        {
            _ready = false;
            StopAllCoroutines();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
