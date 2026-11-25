using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// BOSS 主体专属受击反馈。只做短促压暗、描亮与后坐，始终保留主体
    /// renderer 的启用状态和原始 Alpha；规则伤害与命中粒子由现有链路拥有。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class BossHitResponse : MonoBehaviour
    {
        private const float CompressDuration = 0.055f;
        private const float RecoverDuration = 0.12f;

        private SpriteRenderer _renderer;
        private Coroutine _routine;
        private Color _baseColor;
        private Vector3 _baseScale;
        private Vector3 _basePosition;
        private bool _hasSnapshot;

        public void Play(bool critical)
        {
            EnsureRenderer();
            if (_renderer == null || !isActiveAndEnabled)
                return;
            if (_routine != null)
            {
                StopCoroutine(_routine);
                Restore();
            }

            _baseColor = _renderer.color;
            _baseScale = transform.localScale;
            _basePosition = transform.localPosition;
            _hasSnapshot = true;
            _routine = StartCoroutine(PlayRoutine(critical));
        }

        private IEnumerator PlayRoutine(bool critical)
        {
            Color compressed = Color.Lerp(
                _baseColor,
                new Color(0.20f, 0.12f, 0.22f, _baseColor.a),
                0.48f);
            Color highlight = Color.Lerp(
                _baseColor,
                new Color(1f, 0.90f, 0.62f, _baseColor.a),
                critical ? 0.78f : 0.58f);
            float recoil = critical ? 0.12f : 0.075f;
            float scale = critical ? 1.045f : 1.028f;

            float elapsed = 0f;
            while (elapsed < CompressDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / CompressDuration);
                _renderer.color = Color.Lerp(_baseColor, compressed, t);
                transform.localPosition = _basePosition +
                    Vector3.right * Mathf.Lerp(0f, recoil, t);
                transform.localScale = _baseScale *
                    Mathf.Lerp(1f, scale, t);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < RecoverDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / RecoverDuration);
                float flash = Mathf.Sin(t * Mathf.PI);
                Color pulseColor = Color.Lerp(
                    compressed,
                    highlight,
                    flash);
                pulseColor.a = _baseColor.a;
                _renderer.color = pulseColor;
                transform.localPosition = Vector3.Lerp(
                    _basePosition + Vector3.right * recoil,
                    _basePosition,
                    t);
                transform.localScale = Vector3.Lerp(
                    _baseScale * scale,
                    _baseScale,
                    t);
                yield return null;
            }

            Restore();
            _routine = null;
        }

        private void EnsureRenderer()
        {
            if (_renderer == null)
                _renderer = GetComponent<SpriteRenderer>();
        }

        private void Restore()
        {
            if (!_hasSnapshot)
                return;
            if (_renderer != null)
                _renderer.color = _baseColor;
            transform.localScale = _baseScale;
            transform.localPosition = _basePosition;
            _hasSnapshot = false;
        }

        private void OnDisable()
        {
            if (_routine != null)
                StopCoroutine(_routine);
            _routine = null;
            Restore();
        }
    }
}
