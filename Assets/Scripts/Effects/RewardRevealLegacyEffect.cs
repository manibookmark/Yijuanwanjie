using System.Collections;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Infinity 资源缺失或损坏时使用的旧 Shuriken 安全回退。正式表现规则不再
    /// 从这里读取；保留它只为避免奖励弹窗因资源故障完全失去反馈。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class RewardRevealLegacyEffect : MonoBehaviour
    {
        private enum CelebrationTier
        {
            Quiet,
            Flourish,
            Grand,
            Celestial
        }

        private ParticleSystem _convergence;
        private ParticleSystem _corona;
        private ParticleSystem _fireworks;
        private ParticleSystem _glitter;
        private Coroutine _celebration;
        private Color _color;
        private RewardRarity _rarity;
        private CelebrationTier _celebrationTier;
        private float _intensity;

        private static Material _material;
        private static Texture2D _softSpark;

        public void Initialize(
            Color accent,
            RewardRarity rarity,
            int sortingOrder = 100)
        {
            _rarity = rarity;
            _celebrationTier = ResolveCelebrationTier(rarity);
            _intensity = ResolveIntensity(rarity);
            _color = ResolveParticleColor(accent, rarity);
            _convergence = CreateSystem(
                "LegacyRewardConvergence", sortingOrder, 0.56f, 0.105f);
            _corona = CreateSystem(
                "LegacyRewardCorona", sortingOrder + 1, 0.42f, 0.15f);
            _fireworks = CreateSystem(
                "LegacyRewardFireworkBurst", sortingOrder + 2, 0.82f, 0.09f);
            _glitter = CreateSystem(
                "LegacyRewardFortuneGlitter", sortingOrder + 3, 1.05f, 0.07f);

            var fireworkMain = _fireworks.main;
            fireworkMain.gravityModifier = 0.32f;
            var glitterMain = _glitter.main;
            glitterMain.gravityModifier = 0.16f;
            PlayReveal();
        }

        public void PlayHoverPulse()
        {
            EmitCorona(Mathf.Max(5, ResolveParticleCount(_rarity) / 4));
        }

        private void PlayReveal()
        {
            int count = ResolveParticleCount(_rarity);
            float radius = ResolveRadius(_rarity);
            float travelTime = ResolveTravelTime(_rarity);
            EmitConvergence(
                Mathf.RoundToInt(count * _intensity),
                radius * Mathf.Lerp(1f, 1.20f, _intensity - 1f),
                travelTime);
            EmitCorona(Mathf.RoundToInt(
                Mathf.Max(8, count / 2) * _intensity));
            _celebration = StartCoroutine(PlayCelebration());
        }

        private IEnumerator PlayCelebration()
        {
            yield return new WaitForSecondsRealtime(0.05f);

            int count = Mathf.RoundToInt(
                ResolveFireworkCount(_rarity) * _intensity *
                ResolveCelebrationCountMultiplier(_celebrationTier));
            int burstCount = ResolveBurstCount(_celebrationTier);
            Vector3[] origins =
            {
                new Vector3(-1.48f, 0.62f, 0f),
                new Vector3(1.48f, 0.56f, 0f),
                new Vector3(-1.34f, -0.72f, 0f),
                new Vector3(1.34f, -0.68f, 0f),
                new Vector3(-0.78f, 1.02f, 0f),
                new Vector3(0.78f, 1.00f, 0f),
                new Vector3(0f, -1.02f, 0f),
                new Vector3(0f, 1.14f, 0f)
            };
            for (int i = 0; i < burstCount; i++)
            {
                Vector3 origin = origins[i % origins.Length];
                EmitFirework(origin, Mathf.Max(8, count - i % 3), i);
                EmitGlitter(origin, Mathf.Max(5, count / 2));
                if (i + 1 < burstCount)
                {
                    yield return new WaitForSecondsRealtime(
                        _celebrationTier == CelebrationTier.Celestial
                            ? 0.055f
                            : 0.08f);
                }
            }
            if ((int)_celebrationTier >= (int)CelebrationTier.Grand)
                EmitGlitter(Vector3.down * 0.94f, count * 2);
            _celebration = null;
        }

        private ParticleSystem CreateSystem(
            string objectName,
            int sortingOrder,
            float lifetime,
            float size)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var system = child.AddComponent<ParticleSystem>();
            system.Stop(
                withChildren: true,
                stopBehavior: ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.8f;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = _color;
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var sizeLife = system.sizeOverLifetime;
            sizeLife.enabled = true;
            sizeLife.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.32f),
                    new Keyframe(0.24f, 1f),
                    new Keyframe(0.78f, 0.72f),
                    new Keyframe(1f, 0f)));

            var colorLife = system.colorOverLifetime;
            colorLife.enabled = true;
            colorLife.color = new ParticleSystem.MinMaxGradient(
                CreateFadeGradient(_color));

            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetMaterial();
            renderer.sortingOrder = sortingOrder;
            renderer.alignment = ParticleSystemRenderSpace.View;
            return system;
        }

        private void EmitConvergence(int count, float radius, float travelTime)
        {
            if (_convergence == null)
                return;
            _convergence.Play();
            Vector3 center = _convergence.transform.position;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, count) +
                              Random.Range(-0.07f, 0.07f);
                Vector3 direction = new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                float spawnRadius = radius * Random.Range(0.84f, 1.08f);
                var emit = new ParticleSystem.EmitParams
                {
                    position = center + direction * spawnRadius,
                    velocity = -direction * spawnRadius /
                               Mathf.Max(0.1f, travelTime),
                    startSize = Random.Range(0.075f, 0.14f),
                    startColor = _color
                };
                _convergence.Emit(emit, 1);
            }
        }

        private void EmitCorona(int count)
        {
            if (_corona == null)
                return;
            _corona.Play();
            Vector3 center = _corona.transform.position;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, count);
                Vector3 radial = new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var emit = new ParticleSystem.EmitParams
                {
                    position = center + radial * Random.Range(0.12f, 0.28f),
                    velocity = radial * Random.Range(0.16f, 0.38f) +
                               Vector3.up * Random.Range(0.08f, 0.24f),
                    startSize = Random.Range(0.10f, 0.20f),
                    startColor = _color
                };
                _corona.Emit(emit, 1);
            }
        }

        private void EmitFirework(Vector3 localOrigin, int count, int phase)
        {
            if (_fireworks == null)
                return;
            _fireworks.Play();
            Vector3 origin = _fireworks.transform.position + localOrigin;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, count) +
                              phase * 0.21f + Random.Range(-0.08f, 0.08f);
                Vector3 radial = new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Color sparkColor = i % 3 == 0
                    ? Color.Lerp(_color, Color.white, 0.58f)
                    : _color;
                sparkColor.a = _color.a;
                var emit = new ParticleSystem.EmitParams
                {
                    position = origin + radial * Random.Range(0.025f, 0.08f),
                    velocity = radial * Random.Range(0.70f, 1.16f) +
                               Vector3.up * Random.Range(0.06f, 0.20f),
                    startLifetime = Random.Range(0.68f, 0.94f),
                    startSize = Random.Range(0.065f, 0.125f),
                    startColor = sparkColor
                };
                _fireworks.Emit(emit, 1);
            }
        }

        private void EmitGlitter(Vector3 localOrigin, int count)
        {
            if (_glitter == null)
                return;
            _glitter.Play();
            Vector3 origin = _glitter.transform.position + localOrigin;
            for (int i = 0; i < count; i++)
            {
                Color glitterColor = Color.Lerp(
                    _color, Color.white, Random.Range(0.30f, 0.72f));
                glitterColor.a = _color.a * 0.86f;
                var emit = new ParticleSystem.EmitParams
                {
                    position = origin + new Vector3(
                        Random.Range(-0.26f, 0.26f),
                        Random.Range(-0.10f, 0.18f),
                        0f),
                    velocity = new Vector3(
                        Random.Range(-0.18f, 0.18f),
                        Random.Range(0.38f, 0.78f),
                        0f),
                    startLifetime = Random.Range(0.72f, 1.10f),
                    startSize = Random.Range(0.045f, 0.095f),
                    startColor = glitterColor
                };
                _glitter.Emit(emit, 1);
            }
        }

        private static int ResolveParticleCount(RewardRarity rarity)
        {
            return rarity switch
            {
                RewardRarity.Common => 14,
                RewardRarity.Uncommon => 20,
                RewardRarity.Rare => 30,
                RewardRarity.VeryRare => 42,
                RewardRarity.UltraRare => 58,
                _ => 14
            };
        }

        private static int ResolveFireworkCount(RewardRarity rarity)
            => 9 + Mathf.Clamp((int)rarity, 0, 4) * 3;

        private static CelebrationTier ResolveCelebrationTier(
            RewardRarity rarity)
        {
            return rarity switch
            {
                RewardRarity.Common => CelebrationTier.Quiet,
                RewardRarity.Uncommon => CelebrationTier.Flourish,
                RewardRarity.Rare => CelebrationTier.Grand,
                _ => CelebrationTier.Celestial
            };
        }

        private static int ResolveBurstCount(CelebrationTier tier)
        {
            return tier switch
            {
                CelebrationTier.Quiet => 2,
                CelebrationTier.Flourish => 4,
                CelebrationTier.Grand => 6,
                CelebrationTier.Celestial => 8,
                _ => 4
            };
        }

        private static float ResolveCelebrationCountMultiplier(
            CelebrationTier tier)
        {
            return tier switch
            {
                CelebrationTier.Quiet => 0.72f,
                CelebrationTier.Flourish => 1f,
                CelebrationTier.Grand => 1.28f,
                CelebrationTier.Celestial => 1.58f,
                _ => 1f
            };
        }

        private static float ResolveIntensity(RewardRarity rarity)
        {
            return rarity switch
            {
                RewardRarity.Rare => 1.28f,
                RewardRarity.VeryRare => 1.42f,
                RewardRarity.UltraRare => 1.58f,
                _ => 1f
            };
        }

        private static float ResolveRadius(RewardRarity rarity)
            => 0.82f + (int)rarity * 0.10f;

        private static float ResolveTravelTime(RewardRarity rarity)
            => Mathf.Max(0.34f, 0.52f - (int)rarity * 0.035f);

        private static Color ResolveParticleColor(
            Color accent,
            RewardRarity rarity)
        {
            Color grade = rarity switch
            {
                RewardRarity.Common => new Color(0.78f, 0.76f, 0.70f),
                RewardRarity.Uncommon => new Color(0.48f, 0.78f, 0.68f),
                RewardRarity.Rare => new Color(1f, 0.76f, 0.28f),
                RewardRarity.VeryRare => new Color(0.70f, 0.50f, 0.94f),
                RewardRarity.UltraRare => new Color(1f, 0.94f, 0.70f),
                _ => Color.white
            };
            Color result = Color.Lerp(accent, grade, 0.68f);
            result.a = 0.92f;
            return result;
        }

        private static Gradient CreateFadeGradient(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(Color.white, 0.58f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(color.a, 0.16f),
                    new GradientAlphaKey(color.a * 0.72f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Material GetMaterial()
        {
            if (_material != null)
                return _material;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return null;
            _material = new Material(shader)
            {
                name = "RewardRevealLegacyParticleMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = GetSoftSpark()
            };
            return _material;
        }

        private static Texture2D GetSoftSpark()
        {
            if (_softSpark != null)
                return _softSpark;
            const int size = 32;
            _softSpark = new Texture2D(
                size, size, TextureFormat.RGBA32, false)
            {
                name = "RewardRevealLegacySoftSpark",
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
                float cross = Mathf.Pow(
                    Mathf.Clamp01(1f - Mathf.Min(dx, dy)), 5f);
                float radial = Mathf.Pow(
                    Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)),
                    2.4f);
                _softSpark.SetPixel(
                    x,
                    y,
                    new Color(1f, 1f, 1f, Mathf.Max(radial, cross * 0.45f)));
            }
            _softSpark.Apply();
            return _softSpark;
        }

        private void OnDisable()
        {
            if (_celebration == null)
                return;
            StopCoroutine(_celebration);
            _celebration = null;
        }
    }
}
