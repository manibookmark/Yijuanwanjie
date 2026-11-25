using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 剪纸碎裂死亡特效。
    /// 流程：裂纹闪现 → 纸片碎裂旋转下沉 → 纸屑/墨灰粒子 → 淡墨印消去。
    /// 替换旧的 DeathFadeRoutine 缩小淡出。
    /// </summary>
    public class PaperCutDeath : MonoBehaviour
    {
        private static PaperCutDeath _instance;

        /// <summary>
        /// 在指定位置播放剪纸碎裂死亡特效。
        /// targetTransform 的 SpriteRenderer 会被隐藏（不销毁，由调用方管理生命周期）。
        /// </summary>
        public static void Play(
            Transform target,
            Sprite enemySprite,
            Color enemyTint,
            Transform effectContainer,
            System.Action onComplete = null)
        {
            Play(
                target,
                enemySprite,
                enemyTint,
                effectContainer,
                null,
                onComplete);
        }

        /// <summary>
        /// 播放死亡表现，并分别通知主体腾空帧与完整尾效结束帧。
        /// </summary>
        public static void Play(
            Transform target,
            Sprite enemySprite,
            Color enemyTint,
            Transform effectContainer,
            System.Action onSubjectVacated,
            System.Action onComplete)
        {
            EnsureInstance(effectContainer);
            _instance.StartCoroutine(
                RunPaperCut(
                    target,
                    enemySprite,
                    enemyTint,
                    onSubjectVacated,
                    onComplete));
        }

        /// <summary>
        /// Boss 专用：主体保持原尺寸压暗后化为纸屑与墨灰，不生成缩小的
        /// 完整角色复制体，避免大型角色死亡时出现“突然变小”的错觉。
        /// </summary>
        public static void PlayBossDisperse(
            Transform target,
            Color enemyTint,
            Transform effectContainer,
            System.Action onSubjectVacated,
            System.Action onComplete)
        {
            EnsureInstance(effectContainer);
            _instance.StartCoroutine(
                RunBossDisperse(
                    target,
                    enemyTint,
                    onSubjectVacated,
                    onComplete));
        }

        private static void EnsureInstance(Transform container)
        {
            if (_instance != null) return;
            var go = new GameObject("PaperCutDeath");
            if (container != null) go.transform.SetParent(container, false);
            _instance = go.AddComponent<PaperCutDeath>();
        }

        private static IEnumerator RunPaperCut(
            Transform target,
            Sprite sprite,
            Color tint,
            System.Action onSubjectVacated,
            System.Action onComplete)
        {
            if (target == null)
            {
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log("[VFX] PaperCut target null");
                onSubjectVacated?.Invoke();
                onComplete?.Invoke();
                yield break;
            }

            Vector3 pos = target.position;
            var sr = target.GetComponent<SpriteRenderer>();
            Color baseColor = sr != null ? sr.color : Color.white;
            // 碎片会被挂到独立的世界特效容器，必须保留目标经过编队根
            // 与入场运动根合成后的世界缩放，不能读取角色子节点的 localScale。
            Vector3 baseScale = target.lossyScale;

            // 1. 裂纹闪现（0.06s 白色压暗）
            if (sr != null)
            {
                sr.color = new Color(0.3f, 0.25f, 0.2f, 1f);
            }
            yield return new WaitForSeconds(0.06f);

            // 2. 隐藏原 Sprite
            if (sr != null)
            {
                sr.enabled = false;
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log($"[VFX] PaperCut vacancy callback target={target.name}");
            }
            onSubjectVacated?.Invoke();

            // 3. 生成纸片碎片（4-6 片）
            int fragmentCount = Random.Range(4, 7);
            var fragments = new List<GameObject>();
            Color fragmentColor = new Color(
                Mathf.Clamp01(baseColor.r * 0.85f),
                Mathf.Clamp01(baseColor.g * 0.80f),
                Mathf.Clamp01(baseColor.b * 0.75f),
                1f);

            for (int i = 0; i < fragmentCount; i++)
            {
                var fragGo = new GameObject($"PaperFragment_{i}", typeof(SpriteRenderer));
                if (_instance != null)
                    fragGo.transform.SetParent(_instance.transform, true);
                fragGo.transform.position = pos + (Vector3)(Random.insideUnitCircle * 0.15f);
                fragGo.transform.localScale = baseScale * Random.Range(0.3f, 0.5f);
                fragGo.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));

                var fragSr = fragGo.GetComponent<SpriteRenderer>();
                fragSr.sprite = sprite;
                fragSr.color = fragmentColor;
                fragSr.sortingOrder = 15;

                fragments.Add(fragGo);
            }

            // 4. 生成纸屑/墨灰粒子（8-16 粒）
            int scrapCount = Random.Range(8, 17);
            var scraps = new List<GameObject>();
            Sprite scrapSprite = RuntimeArtLoader.GetVFXSprite("paper_scrap");
            Sprite inkSprite = RuntimeArtLoader.GetVFXSprite("ink_dot");

            for (int i = 0; i < scrapCount; i++)
            {
                bool isInk = i % 3 == 0;
                var scrapGo = new GameObject($"Scrap_{i}", typeof(SpriteRenderer));
                if (_instance != null)
                    scrapGo.transform.SetParent(_instance.transform, true);
                scrapGo.transform.position = pos + (Vector3)(Random.insideUnitCircle * 0.2f);
                scrapGo.transform.localScale = baseScale * Random.Range(0.08f, 0.15f);

                var scrapSr = scrapGo.GetComponent<SpriteRenderer>();
                scrapSr.sprite = isInk ? inkSprite : scrapSprite;
                scrapSr.color = isInk
                    ? new Color(0.15f, 0.12f, 0.10f, 0.9f)
                    : new Color(0.92f, 0.86f, 0.76f, 0.9f);
                scrapSr.sortingOrder = 16;

                scraps.Add(scrapGo);
            }

            // 5. 动画：碎片旋转下沉 + 粒子飘散（0.4s）
            float duration = 0.4f;
            float timer = 0f;

            // 给每片碎片一个随机的飞散方向
            var fragVelocities = new Vector2[fragments.Count];
            var fragRotSpeeds = new float[fragments.Count];
            for (int i = 0; i < fragments.Count; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = Random.Range(0.8f, 2.0f);
                fragVelocities[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                fragRotSpeeds[i] = Random.Range(-180f, 180f);
            }

            var scrapVelocities = new Vector2[scraps.Count];
            for (int i = 0; i < scraps.Count; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = Random.Range(0.5f, 1.5f);
                scrapVelocities[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            }

            while (timer < duration)
            {
                timer += Time.deltaTime;
                float t = timer / duration;

                // 碎片：飞散 + 下沉 + 旋转 + 淡出
                for (int i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i] == null) continue;
                    var v = fragVelocities[i];
                    // 重力下沉
                    v.y -= 3f * Time.deltaTime;
                    fragVelocities[i] = v;

                    fragments[i].transform.position += (Vector3)(v * Time.deltaTime);
                    fragments[i].transform.Rotate(0, 0, fragRotSpeeds[i] * Time.deltaTime);

                    var fsr = fragments[i].GetComponent<SpriteRenderer>();
                    if (fsr != null)
                    {
                        var c = fsr.color;
                        c.a = Mathf.Lerp(1f, 0f, t);
                        fsr.color = c;
                    }
                }

                // 粒子：飘散 + 淡出
                for (int i = 0; i < scraps.Count; i++)
                {
                    if (scraps[i] == null) continue;
                    var v = scrapVelocities[i];
                    v.y -= 1.5f * Time.deltaTime;
                    scrapVelocities[i] = v;

                    scraps[i].transform.position += (Vector3)(v * Time.deltaTime);

                    var ssr = scraps[i].GetComponent<SpriteRenderer>();
                    if (ssr != null)
                    {
                        var c = ssr.color;
                        c.a = Mathf.Lerp(0.9f, 0f, t);
                        ssr.color = c;
                    }
                }

                yield return null;
            }

            // 6. 清理碎片和粒子
            foreach (var f in fragments) if (f != null) Destroy(f);
            foreach (var s in scraps) if (s != null) Destroy(s);

            // 7. 留下淡墨印（0.2s 淡去）
            var markGo = new GameObject("InkMark", typeof(SpriteRenderer));
            if (_instance != null)
                markGo.transform.SetParent(_instance.transform, true);
            markGo.transform.position = pos;
            markGo.transform.localScale = baseScale * 0.8f;
            var markSr = markGo.GetComponent<SpriteRenderer>();
            markSr.sprite = inkSprite;
            markSr.color = new Color(0.15f, 0.12f, 0.10f, 0.3f);
            markSr.sortingOrder = 14;

            timer = 0f;
            while (timer < 0.2f)
            {
                timer += Time.deltaTime;
                float t = timer / 0.2f;
                var c = markSr.color;
                c.a = Mathf.Lerp(0.3f, 0f, t);
                markSr.color = c;
                yield return null;
            }

            Destroy(markGo);

            // 8. 隐藏目标（由调用方决定是否 Destroy）
            if (target != null)
                target.gameObject.SetActive(false);
            onComplete?.Invoke();
        }

        private static IEnumerator RunBossDisperse(
            Transform target,
            Color tint,
            System.Action onSubjectVacated,
            System.Action onComplete)
        {
            if (target == null)
            {
                onSubjectVacated?.Invoke();
                onComplete?.Invoke();
                yield break;
            }

            SpriteRenderer renderer = target.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                target.gameObject.SetActive(false);
                onSubjectVacated?.Invoke();
                onComplete?.Invoke();
                yield break;
            }

            Vector3 position = renderer.bounds.center;
            Vector3 originalScale = target.localScale;
            Color originalColor = renderer.color;
            float halfWidth = Mathf.Max(0.35f, renderer.bounds.extents.x);
            float halfHeight = Mathf.Max(0.45f, renderer.bounds.extents.y);

            float dimElapsed = 0f;
            const float dimDuration = 0.14f;
            while (dimElapsed < dimDuration)
            {
                if (target == null || renderer == null)
                {
                    onSubjectVacated?.Invoke();
                    onComplete?.Invoke();
                    yield break;
                }
                dimElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(dimElapsed / dimDuration);
                Color color = Color.Lerp(
                    originalColor,
                    new Color(
                        originalColor.r * 0.34f,
                        originalColor.g * 0.30f,
                        originalColor.b * 0.26f,
                        0.72f),
                    t);
                renderer.color = color;
                target.localScale = originalScale;
                yield return null;
            }

            renderer.enabled = false;
            target.localScale = originalScale;
            onSubjectVacated?.Invoke();

            Sprite paperSprite = RuntimeArtLoader.GetVFXSprite("paper_scrap");
            Sprite inkSprite = RuntimeArtLoader.GetVFXSprite("ink_dot");
            int particleCount = Random.Range(24, 33);
            var particles = new List<GameObject>(particleCount);
            var velocities = new List<Vector2>(particleCount);
            var rotations = new List<float>(particleCount);
            for (int i = 0; i < particleCount; i++)
            {
                bool ink = i % 3 == 0;
                var particle = new GameObject(
                    $"BossDisperse_{i}",
                    typeof(SpriteRenderer));
                if (_instance != null)
                    particle.transform.SetParent(_instance.transform, true);
                particle.transform.position = position + new Vector3(
                    Random.Range(-halfWidth, halfWidth),
                    Random.Range(-halfHeight, halfHeight),
                    0f);
                float scale = Random.Range(0.055f, 0.11f) *
                    Mathf.Max(0.7f, Mathf.Max(
                        Mathf.Abs(originalScale.x),
                        Mathf.Abs(originalScale.y)));
                particle.transform.localScale = Vector3.one * scale;
                SpriteRenderer particleRenderer =
                    particle.GetComponent<SpriteRenderer>();
                particleRenderer.sprite = ink ? inkSprite : paperSprite;
                particleRenderer.color = ink
                    ? new Color(0.13f, 0.10f, 0.08f, 0.92f)
                    : new Color(
                        Mathf.Clamp01(tint.r * 0.92f + 0.08f),
                        Mathf.Clamp01(tint.g * 0.86f + 0.10f),
                        Mathf.Clamp01(tint.b * 0.74f + 0.15f),
                        0.96f);
                particleRenderer.sortingOrder = renderer.sortingOrder + 2;
                particles.Add(particle);

                Vector2 radial = ((Vector2)(particle.transform.position - position))
                    .normalized;
                velocities.Add(radial * Random.Range(0.45f, 1.25f) +
                    Vector2.up * Random.Range(0.15f, 0.65f));
                rotations.Add(Random.Range(-220f, 220f));
            }

            float elapsed = 0f;
            const float duration = 0.62f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < particles.Count; i++)
                {
                    GameObject particle = particles[i];
                    if (particle == null) continue;
                    Vector2 velocity = velocities[i];
                    velocity.y -= 0.7f * Time.deltaTime;
                    velocities[i] = velocity;
                    particle.transform.position +=
                        (Vector3)(velocity * Time.deltaTime);
                    particle.transform.Rotate(
                        0f,
                        0f,
                        rotations[i] * Time.deltaTime);
                    SpriteRenderer particleRenderer =
                        particle.GetComponent<SpriteRenderer>();
                    Color color = particleRenderer.color;
                    color.a = Mathf.Lerp(color.a, 0f, t);
                    particleRenderer.color = color;
                }
                yield return null;
            }

            for (int i = 0; i < particles.Count; i++)
            {
                if (particles[i] != null)
                    Destroy(particles[i]);
            }
            if (target != null)
            {
                target.localScale = originalScale;
                target.gameObject.SetActive(false);
            }
            onComplete?.Invoke();
        }
    }
}
