using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>
    /// 普通节点纯粒子净化。运行时创建三个 Burst 粒子效果，
    /// 复用已有 paper_scrap / ink_dot / cinnabar_powder 贴图。
    /// Sprite 在 0.55s 内 alpha 1→0、scale 1→0.82。
    /// </summary>
    public sealed class MapNodePurificationEffect : MonoBehaviour
    {
        private ParticleSystem _paperScrapBurst;
        private ParticleSystem _inkDotBurst;
        private ParticleSystem _cinnabarBurst;
        private SpriteRenderer _targetRenderer;
        private Coroutine _coroutine;

        /// <summary>在指定 sprite renderer 上播放净化效果。</summary>
        public void Play(SpriteRenderer target, float duration, System.Action onComplete)
        {
            _targetRenderer = target;
            _coroutine = StartCoroutine(PurifyRoutine(duration, onComplete));
        }

        private IEnumerator PurifyRoutine(float duration, System.Action onComplete)
        {
            CreateBursts();

            // Play all three bursts
            _paperScrapBurst?.Play();
            _inkDotBurst?.Play();
            _cinnabarBurst?.Play();

            // Fade and shrink the sprite
            float elapsed = 0f;
            float fadeDuration = Mathf.Min(0.55f, duration);
            Vector3 baseScale = _targetRenderer != null
                ? _targetRenderer.transform.localScale
                : Vector3.one;
            Color baseColor = _targetRenderer != null
                ? _targetRenderer.color
                : Color.white;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                float alpha = 1f - t;
                float scale = Mathf.Lerp(1f, 0.82f, t);

                if (_targetRenderer != null)
                {
                    _targetRenderer.color = new Color(
                        baseColor.r, baseColor.g, baseColor.b, alpha);
                    _targetRenderer.transform.localScale =
                        baseScale * scale;
                }
                yield return null;
            }

            // Wait for remaining duration for particles to finish
            float remaining = duration - elapsed;
            if (remaining > 0f)
                yield return new WaitForSecondsRealtime(remaining);

            onComplete?.Invoke();
        }

        private void CreateBursts()
        {
            _paperScrapBurst = CreateBurst(
                "PaperScrapBurst",
                Resources.Load<Sprite>("Sprites/VFX/paper_scrap"),
                new Color(0.92f, 0.85f, 0.70f, 0.9f),
                12, 0.3f, 0.5f);

            _inkDotBurst = CreateBurst(
                "InkDotBurst",
                Resources.Load<Sprite>("Sprites/VFX/ink_dot"),
                new Color(0.10f, 0.08f, 0.06f, 0.7f),
                8, 0.2f, 0.3f);

            _cinnabarBurst = CreateBurst(
                "CinnabarBurst",
                Resources.Load<Sprite>("Sprites/VFX/cinnabar_powder"),
                new Color(0.94f, 0.55f, 0.20f, 0.8f),
                6, 0.15f, 0.25f);
        }

        private ParticleSystem CreateBurst(
            string name,
            Sprite sprite,
            Color color,
            int count,
            float startSpeed,
            float lifetime)
        {
            if (sprite == null) return null;

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSpeed = startSpeed;
            main.startLifetime = lifetime;
            main.startColor = color;
            main.startSize = 0.1f;
            main.maxParticles = count;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;
            var burst = new ParticleSystem.Burst(0f, count);
            emission.burstCount = 1;
            emission.SetBurst(0, burst);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Sprites/Default"));
                if (sprite.texture != null)
                    mat.mainTexture = sprite.texture;
                renderer.material = mat;
            }

            return ps;
        }

        private void OnDestroy()
        {
            if (_coroutine != null)
                StopCoroutine(_coroutine);
        }
    }
}
