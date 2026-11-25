using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 敌人受击闪烁效果
    /// 被攻击时闪白
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class HitFlash : MonoBehaviour
    {
        [Header("闪烁参数")]
        public float flashDuration = 0.15f;
        public int flashCount = 2;
        public Color flashColor = Color.white;

        private SpriteRenderer _renderer;
        private Color _originalColor;
        private Coroutine _flashCoroutine;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _originalColor = _renderer.color;
        }

        /// <summary>
        /// 播放受击闪烁
        /// </summary>
        public void Flash()
        {
            if (!isActiveAndEnabled) return;
            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
                RestoreColor();
            }
            _originalColor = _renderer.color;
            _flashCoroutine = StartCoroutine(FlashCoroutine());
        }

        private System.Collections.IEnumerator FlashCoroutine()
        {
            for (int i = 0; i < flashCount; i++)
            {
                // 变白
                _renderer.color = flashColor;
                yield return new WaitForSeconds(flashDuration / (flashCount * 2f));

                // 恢复
                _renderer.color = _originalColor;
                yield return new WaitForSeconds(flashDuration / (flashCount * 2f));
            }

            _renderer.color = _originalColor;
            _flashCoroutine = null;
        }

        private void RestoreColor()
        {
            if (_renderer != null)
                _renderer.color = _originalColor;
        }

        private void OnDisable()
        {
            if (_flashCoroutine != null)
                StopCoroutine(_flashCoroutine);
            _flashCoroutine = null;
            RestoreColor();
        }

        public static HitFlash GetOrAdd(GameObject go)
        {
            var flash = go.GetComponent<HitFlash>();
            if (flash == null)
                flash = go.AddComponent<HitFlash>();
            return flash;
        }

        public static void FlashSafe(GameObject go)
        {
            var flash = go.GetComponent<HitFlash>();
            if (flash != null)
                flash.Flash();
        }
    }
}
