using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 灵玉奖励的玉质升腾仪式：青碧玉片自中心向上飘升，环绕玉尘缓旋，
    /// 底部光点向内聚拢，衬在灵玉本体之后。所有关卡发灵玉共用。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JadeShardEffect : MonoBehaviour
    {
        private ParticleSystem _shards;
        private ParticleSystem _orbitDust;
        private ParticleSystem _gatheringRing;
        private Coroutine _sequence;
        private Color _jade;

        private static Material _material;
        private static Texture2D _jadeSpark;

        public int SystemCount { get; private set; }

        public void Initialize(int sortingOrder)
        {
            _jade = new Color(0.42f, 0.86f, 0.62f, 0.94f);
            _shards = CreateSystem(
                "JadeShards", sortingOrder, 1.10f, 0.13f);
            _orbitDust = CreateSystem(
                "JadeOrbitDust", sortingOrder + 1, 1.26f, 0.07f);
            _gatheringRing = CreateSystem(
                "JadeGatheringRing", sortingOrder + 2, 0.66f, 0.09f);
            SystemCount = 3;
            _sequence = StartCoroutine(PlayAscension());
        }

        public void PlayHoverPulse()
        {
            EmitShards(10, 0.86f);
            EmitOrbit(10, 0.48f);
        }

        private IEnumerator PlayAscension()
        {
            EmitGathering(26, 1.05f);
            EmitShards(22, 0.92f);
            yield return new WaitForSecondsRealtime(0.12f);
            EmitOrbit(30, 0.62f);
            yield return new WaitForSecondsRealtime(0.12f);
            EmitGathering(18, 0.82f);
            EmitShards(16, 1.02f);
            _sequence = null;
        }

        private ParticleSystem CreateSystem(
            string objectName,
            int sortingOrder,
            float lifetime,
            float size)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            system.Stop(true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1.2f;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = _jade;
            main.maxParticles = 180;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.16f),
                    new Keyframe(0.24f, 1f),
                    new Keyframe(0.72f, 0.68f),
                    new Keyframe(1f, 0f)));
            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(
                CreateFadeGradient(_jade));
            ParticleSystemRenderer renderer =
                child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetMaterial();
            renderer.sortingOrder = sortingOrder;
            renderer.alignment = ParticleSystemRenderSpace.View;
            return system;
        }

        private void EmitShards(int count, float radius)
        {
            if (_shards == null) return;
            _shards.Play();
            Vector3 center = _shards.transform.position;
            for (int i = 0; i < count; i++)
            {
                _shards.Emit(new ParticleSystem.EmitParams
                {
                    position = center + new Vector3(
                        Random.Range(-radius, radius) * 0.55f,
                        Random.Range(-0.42f, 0.30f),
                        0f),
                    velocity = new Vector3(
                        Random.Range(-0.10f, 0.10f),
                        Random.Range(0.34f, 0.62f),
                        0f),
                    startLifetime = Random.Range(0.86f, 1.24f),
                    startSize = Random.Range(0.08f, 0.17f),
                    startColor = Color.Lerp(_jade, Color.white,
                        Random.Range(0.14f, 0.46f))
                }, 1);
            }
        }

        private void EmitOrbit(int count, float radius)
        {
            if (_orbitDust == null) return;
            _orbitDust.Play();
            Vector3 center = _orbitDust.transform.position;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, count) +
                              Random.Range(-0.10f, 0.10f);
                Vector3 radial = new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 tangent = new Vector3(-radial.y, radial.x, 0f);
                _orbitDust.Emit(new ParticleSystem.EmitParams
                {
                    position = center + radial * Random.Range(
                        radius * 0.70f, radius),
                    velocity = tangent * Random.Range(0.22f, 0.44f) +
                               Vector3.up * 0.06f,
                    startLifetime = Random.Range(0.92f, 1.36f),
                    startSize = Random.Range(0.05f, 0.11f),
                    startColor = Color.Lerp(_jade, Color.white,
                        Random.Range(0.22f, 0.62f))
                }, 1);
            }
        }

        private void EmitGathering(int count, float radius)
        {
            if (_gatheringRing == null) return;
            _gatheringRing.Play();
            Vector3 center = _gatheringRing.transform.position;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, count) +
                              Random.Range(-0.12f, 0.12f);
                Vector3 radial = new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle) * 0.34f, 0f);
                _gatheringRing.Emit(new ParticleSystem.EmitParams
                {
                    position = center + radial * Random.Range(
                        radius * 0.66f, radius),
                    velocity = -radial * Random.Range(0.26f, 0.50f) +
                               Vector3.up * 0.10f,
                    startLifetime = Random.Range(0.54f, 0.82f),
                    startSize = Random.Range(0.055f, 0.12f),
                    startColor = Color.Lerp(_jade, Color.white,
                        Random.Range(0.30f, 0.70f))
                }, 1);
            }
        }

        private static Gradient CreateFadeGradient(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(Color.white, 0.45f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(color.a, 0.12f),
                    new GradientAlphaKey(color.a * 0.76f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Material GetMaterial()
        {
            if (_material != null) return _material;
            Shader shader = Shader.Find("Sprites/Default") ??
                            Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;
            _material = new Material(shader)
            {
                name = "JadeShardParticleMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = GetJadeSpark()
            };
            return _material;
        }

        private static Texture2D GetJadeSpark()
        {
            if (_jadeSpark != null) return _jadeSpark;
            const int size = 32;
            _jadeSpark = new Texture2D(
                size, size, TextureFormat.RGBA32, false)
            {
                name = "JadeShardSoftSpark",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float center = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - center) / center;
                float dy = Mathf.Abs(y + 0.5f - center) / center;
                float radial = Mathf.Pow(Mathf.Clamp01(
                    1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.0f);
                float diamond = Mathf.Pow(
                    Mathf.Clamp01(1f - Mathf.Abs(dx) - Mathf.Abs(dy)), 3f);
                _jadeSpark.SetPixel(x, y,
                    new Color(1f, 1f, 1f, Mathf.Max(radial, diamond)));
            }
            _jadeSpark.Apply();
            return _jadeSpark;
        }

        private void OnDisable()
        {
            if (_sequence == null) return;
            StopCoroutine(_sequence);
            _sequence = null;
        }
    }
}
