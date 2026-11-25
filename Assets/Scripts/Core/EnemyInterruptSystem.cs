using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum EnemyInterruptAdvanceCause
    {
        CardPlayed,
        NormalIntentsQueued
    }

    public readonly struct EnemyInterruptSnapshot
    {
        internal EnemyInterruptSnapshot(int runtimeId, int maximum, int current)
        {
            RuntimeId = runtimeId;
            MaximumCountdown = maximum;
            CurrentCountdown = current;
        }

        public int RuntimeId { get; }
        public int MaximumCountdown { get; }
        public int CurrentCountdown { get; }
    }

    /// <summary>A counter reached zero; RuntimeId identifies the cause, not an action target.</summary>
    public readonly struct EnemyInterruptTrigger
    {
        internal EnemyInterruptTrigger(int runtimeId, EnemyInterruptAdvanceCause cause,
            int maximum, int previous, int decremented)
        {
            RuntimeId = runtimeId;
            Cause = cause;
            MaximumCountdown = maximum;
            PreviousCountdown = previous;
            DecrementedCountdown = decremented;
        }

        public int RuntimeId { get; }
        public EnemyInterruptAdvanceCause Cause { get; }
        public int MaximumCountdown { get; }
        public int PreviousCountdown { get; }
        public int DecrementedCountdown { get; }
    }

    /// <summary>
    /// Sole owner of an encounter's registered interrupt counters. Actor qualification,
    /// health selection, event delivery and action queues belong to their callers.
    /// Advance snapshots the initial keys and reads each counter's live details. Native
    /// order is retained: unchecked decrement; synchronous per-key zero callback while
    /// the registry still contains the old current; write the local result only if the
    /// live maximum is unchanged. Exceptions preserve earlier writes and release the guard.
    /// Project safety adaptation: registration identity is also captured, so callbacks
    /// may remove/clear/re-register without reviving or overwriting a replaced entry.
    /// Newly created or replaced registrations wait for the next Advance. Native dictionary
    /// access can instead throw after removal. No automatic per-turn reset is installed.
    /// </summary>
    public sealed class EnemyInterruptSystem
    {
        private sealed class Counter
        {
            internal Counter(int maximum)
            {
                Maximum = Current = maximum;
            }

            internal int Maximum;
            internal int Current;
        }

        private readonly Dictionary<int, Counter> _counters = new Dictionary<int, Counter>();
        private bool _advancing;

        public int Count => _counters.Count;

        /// <summary>A detached immutable observation; reading it never advances or resets counters.</summary>
        public IReadOnlyList<EnemyInterruptSnapshot> Snapshot
        {
            get
            {
                var snapshot = new EnemyInterruptSnapshot[_counters.Count];
                int i = 0;
                foreach (KeyValuePair<int, Counter> item in _counters)
                    snapshot[i++] = new EnemyInterruptSnapshot(item.Key, item.Value.Maximum, item.Value.Current);
                return Array.AsReadOnly(snapshot);
            }
        }

        /// <summary>Register always creates a new registration and sets maximum and current, including nonpositive values.</summary>
        public void Register(int runtimeId, int maximumCountdown)
        {
            RequireRuntimeId(runtimeId);
            _counters[runtimeId] = new Counter(maximumCountdown);
        }

        /// <summary>Without reset, an existing current is retained even when it exceeds the new maximum.</summary>
        public void Update(int runtimeId, int maximumCountdown, bool resetCurrentCountdown)
        {
            RequireRuntimeId(runtimeId);
            if (!_counters.TryGetValue(runtimeId, out Counter counter))
            {
                Register(runtimeId, maximumCountdown);
                return;
            }
            counter.Maximum = maximumCountdown;
            if (resetCurrentCountdown) counter.Current = maximumCountdown;
        }

        public bool Deregister(int runtimeId)
        {
            RequireRuntimeId(runtimeId);
            return _counters.Remove(runtimeId);
        }

        public bool TryGetSnapshot(int runtimeId, out EnemyInterruptSnapshot snapshot)
        {
            RequireRuntimeId(runtimeId);
            snapshot = default;
            if (!_counters.TryGetValue(runtimeId, out Counter counter)) return false;
            snapshot = new EnemyInterruptSnapshot(runtimeId, counter.Maximum, counter.Current);
            return true;
        }

        /// <summary>Explicit reset only; callers must establish the actual scheduling contract.</summary>
        public void ResetCounters()
        {
            foreach (Counter counter in _counters.Values) counter.Current = counter.Maximum;
        }

        public void Clear() => _counters.Clear();

        /// <summary>
        /// Advance one real completed-card or queued-normal-intents boundary. Every initial,
        /// still-identical registration advances once; every nonpositive result invokes its
        /// own synchronous callback. Callers own exactly-once event delivery and global actions.
        /// Recursive Advance is rejected; callback registry updates remain legal.
        /// </summary>
        public void Advance(EnemyInterruptAdvanceCause cause, Action<EnemyInterruptTrigger> onZero)
        {
            if (_advancing)
                throw new InvalidOperationException("Interrupt counters are already advancing.");
            if (cause != EnemyInterruptAdvanceCause.CardPlayed &&
                cause != EnemyInterruptAdvanceCause.NormalIntentsQueued)
                throw new ArgumentOutOfRangeException(nameof(cause));
            if (onZero == null) throw new ArgumentNullException(nameof(onZero));

            _advancing = true;
            try
            {
                // Identity references protect removal/re-registration; counter values are not copied.
                var initial = new List<KeyValuePair<int, Counter>>(_counters);
                foreach (KeyValuePair<int, Counter> item in initial)
                {
                    if (!_counters.TryGetValue(item.Key, out Counter counter) ||
                        !ReferenceEquals(counter, item.Value))
                        continue;
                    int maximum = counter.Maximum;
                    int previous = counter.Current;
                    int current = unchecked(previous - 1);
                    if (current <= 0)
                    {
                        int decremented = current;
                        current = maximum;
                        onZero(new EnemyInterruptTrigger(item.Key, cause, maximum, previous, decremented));
                    }
                    if (_counters.TryGetValue(item.Key, out Counter live) &&
                        ReferenceEquals(live, counter) && live.Maximum == maximum)
                        live.Current = current;
                }
            }
            finally { _advancing = false; }
        }

        private static void RequireRuntimeId(int runtimeId)
        {
            if (runtimeId <= 0) throw new ArgumentOutOfRangeException(nameof(runtimeId));
        }
    }
}
