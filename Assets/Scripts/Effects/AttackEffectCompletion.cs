using System;
using System.Collections.Generic;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// One attack source's short-effect completion boundary. Closing launch/arrival
    /// work does not finish the source while queued receipts or its contact tails
    /// remain. It never waits for shared emitters, resident terrain or other casts.
    /// The attack/death owners poll this ticket; it owns no Unity object or clock.
    /// Cancellation retires observations and outstanding leases without pretending
    /// an impact arrived, so late callbacks cannot reopen a retired source.
    /// </summary>
    public sealed class AttackEffectCompletion
    {
        private sealed class WorkLease : IDisposable
        {
            private AttackEffectCompletion _owner;
            internal WorkLease(AttackEffectCompletion owner) { _owner = owner; }
            public void Dispose()
            {
                AttackEffectCompletion owner = _owner;
                _owner = null;
                if (owner != null && !owner._cancelled && !owner._completed)
                    owner._workCount--;
            }
        }

        private readonly List<Func<bool>> _observations = new List<Func<bool>>();
        private int _workCount;
        private bool _closed;
        private bool _completed;
        private bool _cancelled;

        public bool IsCancelled => _cancelled;

        /// <summary>Acquire before scheduling work which may emit a contact tail
        /// after CloseSource. Dispose is idempotent, including after cancellation.</summary>
        public IDisposable HoldWork()
        {
            if (!_cancelled && !_completed) _workCount++;
            return new WorkLease(_cancelled || _completed ? null : this);
        }

        /// <summary>The supplied predicate belongs only to this source's short
        /// effect. For pooled objects use a per-use lease, never object identity.</summary>
        public void ObserveUntil(Func<bool> completed)
        {
            if (completed != null && !_cancelled && !_completed)
                _observations.Add(completed);
        }

        public void CloseSource() { _closed = true; }

        public bool IsComplete
        {
            get
            {
                if (_cancelled || _completed) return true;
                if (!_closed || _workCount > 0) return false;
                for (int i = _observations.Count - 1; i >= 0; i--)
                {
                    if (!_observations[i]()) continue;
                    _observations.RemoveAt(i);
                }
                if (_observations.Count > 0) return false;
                _completed = true;
                return true;
            }
        }

        public void Cancel()
        {
            _cancelled = true;
            _workCount = 0;
            _observations.Clear();
        }
    }
}
