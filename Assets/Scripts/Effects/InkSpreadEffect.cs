using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 墨迹扩散效果（GDD 11.5 / 13.3）
    /// 连击链可视化中的墨迹扩散动效，随连击数增大
    /// 可用粒子系统或程序化渲染
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class InkSpreadEffect : MonoBehaviour
    {
        [Header("墨迹参数")]
        public Sprite inkSprite;
        public float baseScale = 1f;
        public float maxScale = 5f;
        public float spreadDuration = 0.8f;
        public AnimationCurve spreadCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("颜色")]
        public Color inkColor = new Color(0.05f, 0.05f, 0.1f, 0.6f);
        public Color climaxColor = new Color(0.8f, 0.1f, 0.1f, 0.8f);

        private SpriteRenderer _renderer;
        private Coroutine _spreadCoroutine;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null)
                _renderer = gameObject.AddComponent<SpriteRenderer>();

            if (inkSprite != null)
                _renderer.sprite = inkSprite;

            _renderer.color = inkColor;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 播放墨迹扩散
        /// </summary>
        /// <param name="intensity">0-1，随连击数递增</param>
        /// <param name="isClimax">是否为连击高潮模式</param>
        public void Play(float intensity = 0.5f, bool isClimax = false)
        {
            if (_spreadCoroutine != null)
                StopCoroutine(_spreadCoroutine);

            _renderer.color = isClimax ? climaxColor : inkColor;
            gameObject.SetActive(true);
            _spreadCoroutine = StartCoroutine(SpreadCoroutine(intensity));
        }

        /// <summary>
        /// 停止效果
        /// </summary>
        public void Stop()
        {
            if (_spreadCoroutine != null)
                StopCoroutine(_spreadCoroutine);
            gameObject.SetActive(false);
        }

        private System.Collections.IEnumerator SpreadCoroutine(float intensity)
        {
            float targetScale = Mathf.Lerp(baseScale, maxScale, Mathf.Clamp01(intensity));
            float timer = 0f;
            Vector3 startScale = Vector3.zero;
            Vector3 endScale = Vector3.one * targetScale;

            // 扩散阶段
            while (timer < spreadDuration)
            {
                timer += Time.deltaTime;
                float t = timer / spreadDuration;
                float curveValue = spreadCurve.Evaluate(t);

                transform.localScale = Vector3.Lerp(startScale, endScale, curveValue);

                // 透明度先增后减
                var c = _renderer.color;
                if (t < 0.3f)
                {
                    c.a = Mathf.Lerp(0f, 1f, t / 0.3f);
                }
                else
                {
                    c.a = Mathf.Lerp(1f, 0.2f, (t - 0.3f) / 0.7f);
                }
                _renderer.color = c;

                yield return null;
            }

            // 保持淡出
            timer = 0f;
            float fadeDuration = 1.0f;
            var startColor = _renderer.color;
            while (timer < fadeDuration)
            {
                timer += Time.deltaTime;
                float t = timer / fadeDuration;
                var c = startColor;
                c.a = Mathf.Lerp(startColor.a, 0f, t);
                _renderer.color = c;
                yield return null;
            }

            gameObject.SetActive(false);
            _spreadCoroutine = null;
        }
    }
}
