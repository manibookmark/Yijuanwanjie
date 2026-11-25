using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns the single enemy FIFO, ordinary-turn versus player-interrupt completion,
    /// and progression suspension. Counter callbacks append separate global batches;
    /// future intent refresh never rewrites queued receipts. Headless and presentation
    /// consumers use this same queue. Card callbacks collect work until the outer card
    /// settlement releases it. No UI or animation policy is required by Core.
    /// </summary>
    internal sealed class EnemyTurnSequence
    {
        internal delegate bool ResolveAction(EnemyActionCommand command, out CombatEnemyActionResult result);
        private readonly CombatState _state;
        private readonly Func<bool> _hasPendingExperience;
        private readonly Func<IReadOnlyList<EnemyActionCommand>> _prepare;
        private readonly ResolveAction _resolve;
        private readonly Action _complete;
        private readonly Action _completeInterrupt;
        private readonly Queue<EnemyActionCommand> _pending = new Queue<EnemyActionCommand>();
        private bool _prepared, _automatic, _advancing, _suspended, _deferred, _playerInterrupt;
        private int _stagedConsumers;

        internal bool IsActive { get; private set; }
        /// <summary>The active queue belongs to a player-card interrupt, even while its phase is EnemyActing.</summary>
        internal bool IsPlayerInterrupt => IsActive && _playerInterrupt;
        internal int PendingCount => _pending.Count;
        internal event Action Ready;

        internal EnemyTurnSequence(CombatState state, Func<bool> hasPendingExperience,
            Func<IReadOnlyList<EnemyActionCommand>> prepare, ResolveAction resolve, Action complete,
            Action completeInterrupt)
        {
            _state = state;
            _hasPendingExperience = hasPendingExperience;
            _prepare = prepare;
            _resolve = resolve;
            _complete = complete;
            _completeInterrupt = completeInterrupt;
        }

        internal void Begin()
        {
            if (IsActive || _state.IsBattleOver) return;
            IsActive = true;
            _prepared = _automatic = _suspended = _deferred = _playerInterrupt = false;
            _pending.Clear();
            EnsureReady();
        }

        /// <summary>Only a successfully completed real card reaches this boundary.</summary>
        internal void OnCardPlayed()
        {
            if (_state.IsBattleOver) return;
            _state.EnemyInterrupts?.Advance(EnemyInterruptAdvanceCause.CardPlayed, AppendInterruptBatch);
        }

        internal IDisposable AcquireStagedExecution()
        {
            _stagedConsumers++;
            return new StagedLease(this);
        }

        private sealed class StagedLease : IDisposable
        {
            private EnemyTurnSequence _owner;
            internal StagedLease(EnemyTurnSequence owner) { _owner = owner; }
            public void Dispose()
            {
                if (_owner == null) return;
                _owner._stagedConsumers--;
                _owner = null;
            }
        }

        private void AppendInterruptBatch(EnemyInterruptTrigger trigger)
        {
            if (_state.IsBattleOver || _state.IsCombatOver()) return;
            var batch = _state.EnemyAi.CaptureInterruptCommands();
            if (batch.Count > 0)
            {
                if (!IsActive)
                {
                    IsActive = _prepared = _playerInterrupt = _deferred = true;
                    _automatic = _stagedConsumers == 0;
                }
                foreach (EnemyActionCommand command in batch) _pending.Enqueue(command);
            }
            // Must remain within this zero callback, before the counter's guarded write-back.
            _state.EnemyAi.RefreshInterruptIntents();
            _state.EnemyInterrupts.Synchronize();
        }

        internal void Drain()
        {
            _automatic = true;
            if (!IsActive) return;
            while (TryResolve(out _)) { }
            Complete();
        }

        internal bool TryPeek(out EnemyActionCommand command)
        {
            command = default;
            if (!EnsureReady()) return false;
            // Never animate a stale actor and settle another actor at its impact.
            while (_pending.Count > 0 && _state.EnemyAi?.TryValidateReceipt(_pending.Peek()) == false)
                _pending.Dequeue();
            if (_pending.Count == 0) return false;
            command = _pending.Peek();
            return true;
        }

        internal bool TryResolve(out CombatEnemyActionResult result)
        {
            result = default;
            while (EnsureReady() && _pending.Count > 0)
            {
                bool resolved;
                _advancing = true;
                try
                {
                    resolved = _resolve(_pending.Dequeue(), out result);
                    if (_state.IsCombatOver()) _pending.Clear();
                }
                finally { _advancing = false; }
                if (resolved) return true;
                result = default;
            }
            return false;
        }

        /// <summary>
        /// A staged impact belongs to the receipt captured before its wind-up.
        /// Consume at most that entry; an invalidated actor never borrows the
        /// next actor's action or impact. Headless Drain uses the skipping overload.
        /// </summary>
        internal bool TryResolve(EnemyActionCommand expected, out CombatEnemyActionResult result)
        {
            result = default;
            if (!EnsureReady() || _pending.Count == 0 || expected.Receipt == null ||
                !ReferenceEquals(_pending.Peek().Receipt, expected.Receipt)) return false;
            _advancing = true;
            try
            {
                bool resolved = _resolve(_pending.Dequeue(), out result);
                if (_state.IsCombatOver()) _pending.Clear();
                if (!resolved) result = default;
                return resolved;
            }
            finally { _advancing = false; }
        }

        internal bool Complete()
        {
            if (!IsActive || !EnsureReady() || _pending.Count > 0) return false;
            IsActive = false;
            if (_playerInterrupt) _completeInterrupt();
            else _complete();
            return true;
        }

        /// <summary>Called at an ordinary progression checkpoint; no re-entry into an in-flight attack.</summary>
        internal void ResumeAfterProgression()
        {
            if (!IsActive || _advancing) return;
            bool wasSuspended = _suspended || _deferred;
            _deferred = false;
            if (!EnsureReady()) return;
            if (_automatic) Drain();
            else if (wasSuspended) Ready?.Invoke();
        }

        internal void Clear()
        {
            IsActive = false;
            _pending.Clear();
            _prepared = _automatic = _suspended = _deferred = _playerInterrupt = false;
        }

        private bool EnsureReady()
        {
            if (!IsActive || _advancing || _deferred) return false;
            if (_state.IsBattleOver) { Clear(); return false; }
            if (!_state.IsDefeat && (_state.AwaitingRewardSelection || _hasPendingExperience()))
            {
                _suspended = true;
                _state.CurrentPhase = _state.AwaitingRewardSelection
                    ? BattlePhase.AwaitingReward : BattlePhase.CardResolving;
                return false;
            }
            _suspended = false;
            if (!_prepared)
            {
                _prepared = true;
                _advancing = true;
                try
                {
                    if (!_state.IsCombatOver())
                    {
                        foreach (EnemyActionCommand command in _prepare()) _pending.Enqueue(command);
                        if (IsActive && !_state.IsBattleOver && !_state.IsCombatOver())
                            _state.EnemyInterrupts?.Advance(
                                EnemyInterruptAdvanceCause.NormalIntentsQueued, AppendInterruptBatch);
                    }
                }
                finally { _advancing = false; }
                // Terrain at preparation may also create an ordinary progression checkpoint.
                if (!IsActive || _state.IsBattleOver) return false;
                if (!_state.IsDefeat && (_state.AwaitingRewardSelection || _hasPendingExperience()))
                    return EnsureReady();
            }
            if (!_state.IsCombatOver()) _state.CurrentPhase = BattlePhase.EnemyActing;
            return true;
        }
    }
}
