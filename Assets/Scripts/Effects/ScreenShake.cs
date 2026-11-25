using System;
using System.Collections;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 屏幕震动效果
    /// 受击/Boss天劫时触发
    /// </summary>
    public class ScreenShake : MonoBehaviour
    {
        public static ScreenShake Instance { get; private set; }

        [Header("参数")]
        public float defaultDuration = 0.3f;
        public float defaultIntensity = 5f;

        private Vector3 _originalPos;
        private Coroutine _shakeCoroutine;

        public bool IsMoving => _shakeCoroutine != null;

        private void Awake()
        {
            Instance = this;
            _originalPos = transform.localPosition;
        }

        private void OnDisable()
        {
            if (_shakeCoroutine != null)
            {
                StopCoroutine(_shakeCoroutine);
                _shakeCoroutine = null;
            }
            transform.localPosition = _originalPos;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 触发屏幕震动
        /// </summary>
        public void Shake(float duration = -1f, float intensity = -1f)
        {
            if (duration < 0) duration = defaultDuration;
            if (intensity < 0) intensity = defaultIntensity;

            PrepareForMotion();
            _shakeCoroutine = StartCoroutine(ShakeCoroutine(duration, intensity));
        }

        /// <summary>
        /// 播放可复用预设中的确定性屏幕反馈。振幅按屏幕高度换算，因此
        /// 编辑器 F10 与不同分辨率的真实战斗能保持相同主观强度。
        /// </summary>
        public void PlayScreenMotion(
            BossScreenMotionKind kind,
            float duration,
            float viewportAmplitude)
        {
            PlayScreenMotionScoped(kind, duration, viewportAmplitude);
        }

        public IDisposable PlayScreenMotionScoped(
            BossScreenMotionKind kind,
            float duration,
            float viewportAmplitude)
        {
            if (!isActiveAndEnabled || kind == BossScreenMotionKind.None ||
                duration <= 0f || viewportAmplitude <= 0f ||
                float.IsNaN(duration) || float.IsInfinity(duration) ||
                float.IsNaN(viewportAmplitude) || float.IsInfinity(viewportAmplitude))
                return null;
            PrepareForMotion();
            _shakeCoroutine = StartCoroutine(
                ScreenMotionCoroutine(kind, duration, viewportAmplitude));
            return new MotionLease(this, _shakeCoroutine);
        }

        private sealed class MotionLease : IDisposable
        {
            private ScreenShake _owner;
            private readonly Coroutine _routine;
            public MotionLease(ScreenShake owner, Coroutine routine)
            {
                _owner = owner;
                _routine = routine;
            }
            public void Dispose()
            {
                ScreenShake owner = _owner;
                _owner = null;
                if (owner != null) owner.CancelOwnedMotion(_routine);
            }
        }

        private void CancelOwnedMotion(Coroutine routine)
        {
            if (routine == null || _shakeCoroutine != routine) return;
            StopCoroutine(routine);
            _shakeCoroutine = null;
            transform.localPosition = _originalPos;
        }

        /// <summary>
        /// 轻微震动（受击）
        /// </summary>
        public void ShakeLight()
        {
            Shake(0.15f, 3f);
        }

        /// <summary>
        /// 强烈震动（天劫/Boss技能）
        /// </summary>
        public void ShakeStrong()
        {
            Shake(0.5f, 12f);
        }

        private System.Collections.IEnumerator ShakeCoroutine(float duration, float intensity)
        {
            float timer = 0f;

            while (timer < duration)
            {
                // Presentation shake must keep its real-time duration during
                // hit-stop and level-up pauses.
                timer += Time.unscaledDeltaTime;
                float t = timer / duration;

                // 强度随时间衰减
                float currentIntensity = intensity * (1f - t);
                float x = UnityEngine.Random.Range(-1f, 1f) * currentIntensity;
                float y = UnityEngine.Random.Range(-1f, 1f) * currentIntensity;

                transform.localPosition = _originalPos + new Vector3(x, y, 0);

                yield return null;
            }

            transform.localPosition = _originalPos;
            _shakeCoroutine = null;
        }

        private IEnumerator ScreenMotionCoroutine(
            BossScreenMotionKind kind,
            float duration,
            float viewportAmplitude)
        {
            float elapsed = 0f;
            float worldAmplitude = GetVisibleHeight() * viewportAmplitude;
            while (elapsed < duration)
            {
                Vector2 unit = BossActionEffectSampler.SampleScreenOffset(
                    kind,
                    elapsed,
                    duration);
                transform.localPosition = _originalPos +
                    new Vector3(
                        unit.x * worldAmplitude,
                        unit.y * worldAmplitude,
                        0f);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            transform.localPosition = _originalPos;
            _shakeCoroutine = null;
        }

        private void PrepareForMotion()
        {
            if (_shakeCoroutine != null)
            {
                StopCoroutine(_shakeCoroutine);
                _shakeCoroutine = null;
                transform.localPosition = _originalPos;
            }
            else
            {
                _originalPos = transform.localPosition;
            }
        }

        private float GetVisibleHeight()
        {
            Camera camera = GetComponent<Camera>();
            if (camera == null)
                return 10.8f;
            if (camera.orthographic)
                return camera.orthographicSize * 2f;
            return 2f * 10f * Mathf.Tan(
                camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        public static void ShakeSafe(float duration = -1f, float intensity = -1f)
        {
            if (Instance != null)
                Instance.Shake(duration, intensity);
        }

        public static void ShakeLightSafe()
        {
            if (Instance != null)
                Instance.ShakeLight();
        }

        public static void ShakeStrongSafe()
        {
            if (Instance != null)
                Instance.ShakeStrong();
        }
    }
}
