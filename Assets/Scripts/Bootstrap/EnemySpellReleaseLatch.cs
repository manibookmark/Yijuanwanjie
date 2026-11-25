using System;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Pairs a committed grant with its action's visual release once.
    /// Owns no gameplay state, callback or scene object.</summary>
    public sealed class EnemySpellReleaseLatch
    {
        private long _generation;
        private int _enemyIndex;
        private string _actionId;
        private bool _active, _granted, _released, _delivered;

        public void Begin(long generation, int enemyIndex, string actionId)
        {
            Clear();
            _generation = generation;
            _enemyIndex = enemyIndex;
            _actionId = actionId ?? string.Empty;
            _active = true;
        }

        public bool Matches(long generation, int enemyIndex, string actionId)
            => _active && generation == _generation && enemyIndex == _enemyIndex &&
               string.Equals(actionId ?? string.Empty, _actionId, StringComparison.Ordinal);

        public bool MatchesReceipt(int enemyIndex, string actionId)
            => _active && enemyIndex == _enemyIndex &&
               string.Equals(actionId ?? string.Empty, _actionId, StringComparison.Ordinal);

        public bool Grant(int enemyIndex, string actionId, int actualCount)
        {
            if (!MatchesReceipt(enemyIndex, actionId) || _granted || actualCount <= 0)
                return false;
            _granted = true;
            return true;
        }

        public bool Release(long generation, int enemyIndex, string actionId)
        {
            if (!Matches(generation, enemyIndex, actionId) || _released)
                return false;
            _released = true;
            return true;
        }

        public bool TryDeliver()
        {
            if (!_active || !_granted || !_released || _delivered) return false;
            _delivered = true;
            return true;
        }

        public void Clear()
        {
            _active = _granted = _released = _delivered = false;
            _actionId = null;
        }
    }
}
