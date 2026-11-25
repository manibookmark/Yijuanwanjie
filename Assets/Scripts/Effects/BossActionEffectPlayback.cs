using System;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns one effect's stroke/Infinity tree and its camera-motion lease.
    /// Callers receive only cancellation, never writable renderer state.
    /// </summary>
    public sealed class BossActionEffectPlayback : MonoBehaviour, IDisposable
    {
        private IDisposable _cameraMotion;
        private bool _armed;
        private bool _disposed;

        internal void Arm(IDisposable cameraMotion)
        {
            if (_disposed)
            {
                cameraMotion?.Dispose();
                return;
            }
            _cameraMotion = cameraMotion;
            _armed = true;
        }

        private void Update()
        {
            // Both child owners destroy themselves on natural completion.
            // Infinity may finish after the stroke, so do not guess a timeout.
            if (_armed && transform.childCount == 0) Dispose();
        }

        public void Dispose() => Release(true);
        private void OnDisable() => Release(true);
        private void OnDestroy() => Release(false);

        private void Release(bool destroyRoot)
        {
            if (_disposed) return;
            _disposed = true;
            IDisposable motion = _cameraMotion;
            _cameraMotion = null;
            try { motion?.Dispose(); }
            finally
            {
                if (destroyRoot)
                {
                    // Hide descendants synchronously; delayed Destroy alone
                    // would leave a visible frame after owner cancellation.
                    if (gameObject.activeSelf) gameObject.SetActive(false);
                    if (Application.isPlaying) Destroy(gameObject);
                    else DestroyImmediate(gameObject);
                }
            }
        }
    }
}
