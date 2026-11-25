using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// One explicit play-all request, confined to its original combat and turn.
    /// Owns cancellation and finite attempt limits. Selection never plays a card;
    /// the caller submits through the normal combat path and reports its result.
    /// Limits stop this request only and never change manual card-use rules.
    /// </summary>
    public sealed class CombatAutoPlaySession
    {
        public const int DefaultActionLimit = 128;
        public const int DefaultInstanceLimit = 16;

        private readonly int _turn;
        private readonly int _actionLimit;
        private readonly int _instanceLimit;
        private readonly HashSet<CardConfig> _excluded = new HashSet<CardConfig>();
        private readonly Dictionary<CardConfig, int> _uses = new Dictionary<CardConfig, int>();
        private CardConfig _pending;
        private int _attempts;
        private bool _cancelled;

        public CombatAutoPlaySession(CombatController source,
            int actionLimit = DefaultActionLimit, int instanceLimit = DefaultInstanceLimit)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (actionLimit <= 0) throw new ArgumentOutOfRangeException(nameof(actionLimit));
            if (instanceLimit <= 0) throw new ArgumentOutOfRangeException(nameof(instanceLimit));
            _turn = source.State.CurrentTurn;
            _actionLimit = actionLimit;
            _instanceLimit = instanceLimit;
        }

        public CombatController Source { get; }
        public bool IsActive => !_cancelled && _attempts < _actionLimit &&
            Source.State.CurrentTurn == _turn && !Source.IsCombatOver();

        public CardConfig SelectNext()
        {
            if (!IsActive || !Source.State.CanPlayCards) return null;
            if (_pending != null && Source.State.Deck.HasInHand(_pending)) return _pending;
            _pending = CombatAutoPlayPolicy.SelectNext(Source, _excluded);
            if (_pending == null) Cancel();
            return _pending;
        }

        public void RecordAttempt(CardConfig card, bool success)
        {
            if (!IsActive || ReferenceEquals(card, null) || !ReferenceEquals(card, _pending)) return;
            _pending = null;
            _attempts++;
            if (!success)
            {
                // Do not retry a rejected instance forever; other cards can run.
                _excluded.Add(card);
                return;
            }
            _uses.TryGetValue(card, out int used);
            if (++used >= _instanceLimit) _excluded.Add(card);
            _uses[card] = used;
        }

        public void Cancel()
        {
            _cancelled = true;
            _pending = null;
        }
    }
}
