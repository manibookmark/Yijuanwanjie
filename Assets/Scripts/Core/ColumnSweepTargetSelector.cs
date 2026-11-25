using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns one card's ordered column sweep: retained column cursor, per-object
    /// hit budget and identity set, and the empty/duplicate/death search budget.
    /// A hit advances after its damage resolves so a newly exposed enemy can
    /// join the same object on a later circuit. No damage or visual state lives here.
    /// </summary>
    internal sealed class ColumnSweepTargetSelector
    {
        private readonly BossOverkillSession _bossOverkill;
        private readonly HashSet<int> _hitRuntimeIds = new HashSet<int>();
        private int _column;
        private int _searchSteps;
        private int _remainingHits;
        private int _restrictedColumn;
        private int _lastTargetIndex = -1;

        internal ColumnSweepTargetSelector(BossOverkillSession bossOverkill)
            => _bossOverkill = bossOverkill;

        internal void BeginObject(int targetCount, int restrictedColumn)
        {
            _hitRuntimeIds.Clear();
            _searchSteps = 0;
            _remainingHits = targetCount <= 0
                ? EnemyFormationLayout.Columns
                : Math.Min(targetCount, EnemyFormationLayout.Columns);
            _restrictedColumn = restrictedColumn;
            _lastTargetIndex = -1;
        }

        internal bool TrySelectNext(IList<EnemyStats> enemies, out int targetIndex)
        {
            targetIndex = -1;
            if (_lastTargetIndex >= 0)
            {
                // OncePerEnemy counts death advancement separately from hits.
                if (enemies[_lastTargetIndex].IsDead) _searchSteps++;
                AdvanceColumn();
                _lastTargetIndex = -1;
            }

            List<int> candidates = AttackResolver.GetResolutionTargets(enemies, _bossOverkill);
            if (candidates.Count == 0) return false;
            while (_searchSteps < EnemyFormationLayout.Columns)
            {
                int candidate = FindFront(enemies, candidates);
                if (candidate < 0)
                {
                    _searchSteps++;
                    AdvanceColumn();
                    continue;
                }
                // Native processing checks the next column before retiring an
                // exhausted hit budget; the next object retains that column.
                if (_remainingHits <= 0) return false;
                if (_hitRuntimeIds.Contains(enemies[candidate].runtimeId))
                {
                    _searchSteps++;
                    AdvanceColumn();
                    continue;
                }
                _remainingHits--;
                _hitRuntimeIds.Add(enemies[candidate].runtimeId);
                _lastTargetIndex = candidate;
                targetIndex = candidate;
                return true;
            }
            return false;
        }

        private int FindFront(IList<EnemyStats> enemies, List<int> candidates)
        {
            if (_restrictedColumn >= 0 && _column != _restrictedColumn) return -1;
            foreach (int index in candidates)
            {
                EnemyStats enemy = enemies[index];
                int left = enemy.column >= 0
                    ? enemy.column : index % EnemyFormationLayout.Columns;
                if (left <= _column && left + enemy.GridWidth > _column) return index;
            }
            return -1;
        }

        private void AdvanceColumn()
            => _column = (_column + 1) % EnemyFormationLayout.Columns;
    }
}
