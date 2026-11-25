using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>
    /// 遗珍采集专属演出。与战斗节点净化(MapNodePurificationEffect)刻意区分：
    /// 不使用纸屑/墨滴/朱砂的"净化"语言，改用暖金灵光向本体收束、上升灵尘，
    /// 遗珍本体在金光中微微上浮、放大、淡金后归藏，喻示"灵物入匣、福泽入体"。
    /// 纯运行时粒子，无外部材质依赖。
    /// </summary>
    public sealed class MapNodeRelicCollectionEffect : MonoBehaviour
    {
        private ParticleSystem _goldWave;
        private ParticleSystem _spiritRise;
        private ParticleSystem _radiance;
        private ParticleSystem _finalBurst;
        private SpriteRenderer _targetRenderer;
        private Coroutine _coroutine;

        private static Material _additiveMaterial;
        private static Material _alphaMaterial;
        private static Texture2D _softCircle;
        private static Texture2D _spark;

        private static readonly Color GoldCore =
            new Color(1f, 0.86f, 0.42f, 0.95f);
        private static readonly Color GoldWarm =
            new Color(1f, 0.72f, 0.30f, 0.85f);
        private static readonly Color GoldSoft =
            new Color(1f, 0.92f, 0.66f, 0.70f);

        /// <summary>在指定 sprite renderer 上播放遗珍采集效果。</summary>
        public void Play(
            SpriteRenderer target,
            float duration,
            System.Action onComplete)
        {
            _targetRenderer = target;
            if (_coroutine != null)
                StopCoroutine(_coroutine);
            _coroutine = StartCoroutine(
                CollectionRoutine(duration, onComplete));
        }

        private IEnumerator CollectionRoutine(
            float duration,
            System.Action onComplete)
        {
            duration = Mathf.Clamp(duration, 0.48f, 0.68f);
            CreateEffects();

            // 两圈暖金灵光从外围向遗珍本体汇拢，避免战斗节点的爆散语言。
            _goldWave?.Play();
            EmitConvergence(_goldWave, 22, 1.18f, 0.18f, 2.15f);
            EmitConvergence(_goldWave, 14, 0.78f, 0.12f, 1.75f);

            // 灵尘持续上浮。
            _spiritRise?.Play();
            _radiance?.Play();

            // 遗珍本体：亮金、上浮、放大、淡出。
            float elapsed = 0f;
            Vector3 baseScale = _targetRenderer != null
                ? _targetRenderer.transform.localScale
                : Vector3.one;
            Color baseColor = _targetRenderer != null
                ? _targetRenderer.color
                : Color.white;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // 前 40% 亮金 + 上浮放大，后 60% 淡出。
                float brightIn = Mathf.Clamp01(t / 0.4f);
                float fadeOut = Mathf.Clamp01((t - 0.4f) / 0.6f);

                if (_targetRenderer != null)
                {
                    Color c = Color.Lerp(baseColor, GoldSoft, brightIn);
                    c.a = Mathf.Lerp(baseColor.a, baseColor.a, brightIn) *
                           (1f - fadeOut);
                    _targetRenderer.color = c;

                    float rise = Mathf.Sin(brightIn * Mathf.PI * 0.5f) * 0.5f;
                    _targetRenderer.transform.localPosition =
                        Vector3.up * rise;

                    float grow = Mathf.Lerp(1f, 1.18f, brightIn);
                    _targetRenderer.transform.localScale =
                        baseScale * grow * (1f - fadeOut * 0.06f);
                }

                yield return null;
            }

            // 收束：灵尘向上托起，不做战斗命中式的径向爆炸。
            _finalBurst?.Play();
            EmitLift(_finalBurst, 18, 0.22f, 1.35f);

            if (_targetRenderer != null)
            {
                _targetRenderer.color = new Color(
                    baseColor.r, baseColor.g, baseColor.b, 0f);
            }

            yield return new WaitForSecondsRealtime(
                0.18f);

            onComplete?.Invoke();
        }

        private void CreateEffects()
        {
            _goldWave = CreateBurst(
                "GoldConvergence", GetAdditiveMaterial(), GetSoftCircle(),
                GoldWarm, 1.6f, 0.55f, 0f);
            _goldWave.GetComponent<ParticleSystemRenderer>().renderMode =
                ParticleSystemRenderMode.Stretch;
            var stretch = _goldWave.velocityOverLifetime;
            stretch.enabled = true;
            stretch.space = ParticleSystemSimulationSpace.Local;

            _spiritRise = CreateLoop(
                "SpiritRise", GetAdditiveMaterial(), GetSpark(),
                GoldSoft, 0.5f, 1.4f, 0.12f, 2.2f);
            var rise = _spiritRise.velocityOverLifetime;
            rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.World;
            rise.y = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);

            _radiance = CreateLoop(
                "Radiance", GetAlphaMaterial(), GetSoftCircle(),
                GoldWarm, 0.9f, 0.7f, 0.28f, 1.6f);

            _finalBurst = CreateBurst(
                "FinalGoldBurst", GetAdditiveMaterial(), GetSpark(),
                GoldCore, 1.4f, 0.6f, 0f);
            transform.localPosition = Vector3.zero;
        }

        private ParticleSystem CreateBurst(
            string name,
            Material material,
            Texture texture,
            Color color,
            float startSpeed,
            float lifetime,
            float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startSpeed = startSpeed;
            main.startLifetime = lifetime;
            main.startColor = color;
            main.startSize = new ParticleSystem.MinMaxCurve(
                0.5f * 0.35f, 1.1f * 0.35f);
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.gravityModifier = gravity;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.12f;

            var sizeLife = ps.sizeOverLifetime;
            sizeLife.enabled = true;
            sizeLife.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.3f),
                    new Keyframe(0.3f, 1.4f),
                    new Keyframe(1f, 0.1f)));

            var colorLife = ps.colorOverLifetime;
            colorLife.enabled = true;
            colorLife.color = new ParticleSystem.MinMaxGradient(
                FadeGradient(color));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (material != null && texture != null)
                material.mainTexture = texture;
            if (material != null)
                renderer.material = material;
            renderer.alignment = ParticleSystemRenderSpace.View;

            return ps;
        }

        private ParticleSystem CreateLoop(
            string name,
            Material material,
            Texture texture,
            Color color,
            float rate,
            float lifetime,
            float size,
            float speed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;
            shape.radiusThickness = 0.4f;

            var colorLife = ps.colorOverLifetime;
            colorLife.enabled = true;
            colorLife.color = new ParticleSystem.MinMaxGradient(
                FadeGradient(color));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (material != null && texture != null)
                material.mainTexture = texture;
            if (material != null)
                renderer.material = material;
            renderer.alignment = ParticleSystemRenderSpace.View;

            return ps;
        }

        private static void EmitConvergence(
            ParticleSystem ps,
            int count,
            float radius,
            float size,
            float speed)
        {
            if (ps == null) return;
            Vector3 center = ps.transform.position;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, count) +
                              Random.Range(-0.08f, 0.08f);
                Vector3 direction = new Vector3(
                    Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var emit = new ParticleSystem.EmitParams
                {
                    position = center + direction *
                        Random.Range(radius * 0.88f, radius * 1.08f),
                    velocity = -direction *
                        Random.Range(speed * 0.88f, speed * 1.08f),
                    startSize = size * Random.Range(0.72f, 1.18f)
                };
                ps.Emit(emit, 1);
            }
        }

        private static void EmitLift(
            ParticleSystem ps, int count, float size, float speed)
        {
            if (ps == null) return;
            Vector3 center = ps.transform.position;
            for (int i = 0; i < count; i++)
            {
                float horizontal = Random.Range(-0.42f, 0.42f);
                var emit = new ParticleSystem.EmitParams
                {
                    position = center + new Vector3(
                        Random.Range(-0.12f, 0.12f),
                        Random.Range(-0.05f, 0.08f),
                        0f),
                    velocity = new Vector3(
                        horizontal,
                        Random.Range(speed * 0.82f, speed * 1.18f),
                        0f),
                    startSize = size * Random.Range(0.75f, 1.2f)
                };
                ps.Emit(emit, 1);
            }
        }

        private static Gradient FadeGradient(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(color.a, 0.2f),
                    new GradientAlphaKey(color.a * 0.6f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Material GetAdditiveMaterial()
        {
            if (_additiveMaterial == null)
                _additiveMaterial = CreateMaterial(true);
            return _additiveMaterial;
        }

        private static Material GetAlphaMaterial()
        {
            if (_alphaMaterial == null)
                _alphaMaterial = CreateMaterial(false);
            return _alphaMaterial;
        }

        private static Material CreateMaterial(bool additive)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;

            var material = new Material(shader)
            {
                name = "MapNodeRelicCollectionMat",
                hideFlags = HideFlags.HideAndDontSave
            };
            return material;
        }

        private static Texture2D GetSoftCircle()
        {
            if (_softCircle == null)
            {
                const int size = 64;
                _softCircle = new Texture2D(
                    size, size, TextureFormat.RGBA32, false)
                {
                    name = "RelicSoftCircle",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                float c = size * 0.5f;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - c) / c;
                    float dy = (y + 0.5f - c) / c;
                    float alpha = Mathf.Clamp01(
                        1f - Mathf.Sqrt(dx * dx + dy * dy));
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    _softCircle.SetPixel(x, y,
                        new Color(1f, 1f, 1f, alpha));
                }
                _softCircle.Apply();
            }
            return _softCircle;
        }

        private static Texture2D GetSpark()
        {
            if (_spark == null)
            {
                const int size = 32;
                _spark = new Texture2D(
                    size, size, TextureFormat.RGBA32, false)
                {
                    name = "RelicSpark",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size * 0.5f) / (size * 0.5f);
                    float dy = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = (1f - d) > 0f ? (1f - d) : 0f;
                    alpha = Mathf.Pow(alpha, 2.4f);
                    _spark.SetPixel(x, y,
                        new Color(1f, 1f, 1f, alpha));
                }
                _spark.Apply();
            }
            return _spark;
        }

        private void OnDestroy()
        {
            if (_coroutine != null)
                StopCoroutine(_coroutine);
        }
    }
}
