using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Composes temporary presentation limits against the clock captured by the
    /// first lease. Each caller releases only its own limit; an old hit-stop
    /// cannot resume a newer pause. No UI, coroutine or scene state lives here.
    /// </summary>
    public sealed class PresentationTimeScale
    {
        private sealed class Lease : IDisposable
        {
            private PresentationTimeScale _owner;
            private readonly long _id;

            public Lease(PresentationTimeScale owner, long id)
            { _owner = owner; _id = id; }

            public void Dispose()
            {
                PresentationTimeScale owner = _owner;
                _owner = null;
                owner?.Release(_id);
            }
        }

        private readonly Func<float> _read;
        private readonly Action<float> _write;
        private readonly Dictionary<long, float> _limits = new Dictionary<long, float>();
        private float _baseline;
        private long _nextId;

        public static PresentationTimeScale Shared { get; } =
            new PresentationTimeScale(() => Time.timeScale, value => Time.timeScale = value);

        public int ActiveLeaseCount => _limits.Count;

        public PresentationTimeScale(Func<float> read, Action<float> write)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        public IDisposable Hold(float maximumTimeScale)
        {
            if (float.IsNaN(maximumTimeScale) || float.IsInfinity(maximumTimeScale) || maximumTimeScale < 0f)
                throw new ArgumentOutOfRangeException(nameof(maximumTimeScale));
            if (_limits.Count == 0)
            {
                _baseline = _read();
                if (float.IsNaN(_baseline) || float.IsInfinity(_baseline) || _baseline < 0f)
                    throw new InvalidOperationException("Presentation clock has an invalid baseline.");
            }
            long id = ++_nextId;
            _limits.Add(id, maximumTimeScale);
            Apply();
            return new Lease(this, id);
        }

        private void Release(long id)
        {
            if (!_limits.Remove(id)) return;
            Apply();
        }

        private void Apply()
        {
            float effective = _baseline;
            foreach (float limit in _limits.Values) effective = Math.Min(effective, limit);
            _write(effective);
        }
    }
}
