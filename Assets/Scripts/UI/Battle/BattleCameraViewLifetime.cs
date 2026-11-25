using System;

namespace WanjieScroll.UI.Battle
{
    /// <summary>
    /// Pure arbitration for one camera-view channel. A lease's identity must
    /// still be current before progress or release can alter the active view.
    /// No camera, scene, UI object or callback is owned by this ledger.
    /// </summary>
    public sealed class BattleCameraViewLifetime
    {
        private long _nextToken;
        private long _activeToken;
        private float _targetZoom = 1f;
        private float _progress;

        public bool IsActive => _activeToken != 0;
        public float Progress => IsActive ? _progress : 0f;
        public float Zoom => IsActive ? 1f + (_targetZoom - 1f) * _progress : 1f;

        public long Begin(float targetZoom)
        {
            if (!IsValidZoom(targetZoom))
                throw new ArgumentOutOfRangeException(nameof(targetZoom));
            long token = checked(_nextToken + 1);
            _nextToken = token;
            _activeToken = token;
            _targetZoom = targetZoom;
            _progress = 0f;
            return token;
        }

        public bool IsCurrent(long token) => token > 0 && token == _activeToken;

        public bool TrySetProgress(long token, float progress)
        {
            if (!IsCurrent(token) || float.IsNaN(progress) || float.IsInfinity(progress)) return false;
            _progress = Math.Max(0f, Math.Min(1f, progress));
            return true;
        }

        public bool TryRelease(long token)
        {
            if (!IsCurrent(token)) return false;
            Cancel();
            return true;
        }

        public void Cancel()
        {
            _activeToken = 0;
            _targetZoom = 1f;
            _progress = 0f;
        }

        public static bool IsValidZoom(float zoom)
            => !float.IsNaN(zoom) && !float.IsInfinity(zoom) && zoom >= 1f;
    }
}
