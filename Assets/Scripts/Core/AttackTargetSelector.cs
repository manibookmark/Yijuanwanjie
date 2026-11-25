using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 一张牌执行期间的自动选敌游标。可攻击资格仍由 AttackResolver 统一给出；
    /// 本类拥有空间方向、攻击意图优先级与随机列袋等逐牌选择状态，
    /// 并在每个目标位结算前基于实时合法目标重新选择。
    /// 周期批次可显式共享按策略/排隔离的空间游标；资格集合和 SubHit 预算仍逐计划持有。
    /// 列火覆盖仅从 TerrainAttackCoverage 查询；本类仍唯一拥有近排资格与方向游标，
    /// 不新增地形生命周期。新增独立选择状态或达到 800 行时重新审查模块边界。
    /// </summary>
    internal sealed class AttackTargetSelector
    {
        /// <summary>
        /// Ephemeral spatial progress for one owner's attack batch. Policies do
        /// not advance each other's cursor; no target identity, damage or author
        /// plan is retained. Ordinary card execution does not use this context.
        /// </summary>
        internal sealed class SpatialProgress
        {
            private readonly Dictionary<TargetPolicy, Dictionary<int, int>> _cursors =
                new Dictionary<TargetPolicy, Dictionary<int, int>>();

            internal int GetCursor(TargetPolicy policy, int row, bool reverse)
                => _cursors.TryGetValue(policy, out Dictionary<int, int> rows) &&
                    rows.TryGetValue(row, out int cursor)
                        ? cursor : reverse ? EnemyFormationLayout.Columns - 1 : 0;

            internal void SetCursor(TargetPolicy policy, int row, int nextColumn)
            {
                if (!_cursors.TryGetValue(policy, out Dictionary<int, int> rows))
                {
                    rows = new Dictionary<int, int>();
                    _cursors.Add(policy, rows);
                }
                rows[row] = nextColumn;
            }
        }

        private readonly ResolutionMode _mode;
        private readonly IRandomSource _random;
        private readonly IReadOnlyList<EnemyIntent> _upcomingIntents;
        private readonly BossOverkillSession _bossOverkill;
        private readonly SpatialProgress _spatialProgress;
        private readonly ColumnSweepTargetSelector _columnSweep;
        private readonly FrozenAttackTargetSet _frozenAttackTargets;
        private readonly TerrainAttackCoverage _terrainCoverage;
        private readonly Dictionary<int, int> _leftCursors = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _rightCursors = new Dictionary<int, int>();
        private readonly HashSet<int> _upcomingCycleVisited = new HashSet<int>();
        private readonly List<int> _randomColumnBag = new List<int>();
        private readonly HashSet<int> _selectedThisSubHit = new HashSet<int>();
        private readonly HashSet<int> _frozenRowRuntimeIds = new HashSet<int>();
        private TargetPolicy _activePolicy;
        private ResolvedTerrainEffectSpec _activeTerrainEffect;
        private int _remainingTargetSlots;
        private bool _subHitStarted;
        private int _targetColumn = -1;
        private int _nextEmptyColumn = -1;
        private int _waveNextColumn;
        private int _randomCurrentColumn = -1;
        /// <summary>The column chosen by the shared native bag, including a wide target's selected lane.</summary>
        internal int SelectedRandomColumn => _randomCurrentColumn;
        private int _randomScannedColumns;
        private int _randomLastTarget = -1;
        private bool _randomAdvancePending;
        private bool _randomMultiHitAborted;

        public AttackTargetSelector(
            ResolutionMode mode,
            IRandomSource random,
            IReadOnlyList<EnemyIntent> upcomingIntents,
            BossOverkillSession bossOverkill,
            SpatialProgress spatialProgress = null,
            FrozenAttackTargetSet frozenAttackTargets = null,
            TerrainAttackCoverage terrainCoverage = null)
        {
            _mode = mode;
            _random = random;
            _upcomingIntents = upcomingIntents;
            _bossOverkill = bossOverkill;
            _spatialProgress = spatialProgress;
            _columnSweep = new ColumnSweepTargetSelector(bossOverkill);
            _frozenAttackTargets = frozenAttackTargets;
            _terrainCoverage = terrainCoverage;
        }

        /// <summary>
        /// 开始一个 SubHit。这里唯一冻结目标位预算；目标索引本身不冻结，
        /// 后续每个位置仍会读取前一位置结算后的实时阵型。
        /// FrozenNearestAttackableRow 另外冻结身份集合，动态资格只缩小而不扩充它。
        /// FrozenAttackableSet 则复用整次执行开始前捕获的身份集合。
        /// </summary>
        public void BeginSubHit(
            IList<EnemyStats> enemies,
            TargetPolicy policy,
            int configuredTargetCount,
            int targetColumn = -1,
            ResolvedTerrainEffectSpec terrainEffect = null)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));
            _activePolicy = policy;
            _activeTerrainEffect = terrainEffect;
            _targetColumn = targetColumn;
            _selectedThisSubHit.Clear();
            _frozenRowRuntimeIds.Clear();
            _waveNextColumn = 0;
            _randomScannedColumns = 0;
            _randomLastTarget = -1;
            if (policy == TargetPolicy.FrozenAttackableSet)
            {
                if (_frozenAttackTargets == null)
                    throw new InvalidOperationException("整次攻击冻结策略需要执行开始时捕获的目标集合。");
                _remainingTargetSlots = configuredTargetCount <= 0
                    ? _frozenAttackTargets.Count
                    : Math.Min(configuredTargetCount, _frozenAttackTargets.Count);
                _subHitStarted = true;
                return;
            }
            if (policy == TargetPolicy.SequentialColumnSweep)
            {
                _columnSweep.BeginObject(configuredTargetCount, targetColumn);
                _subHitStarted = true;
                return;
            }
            if (policy == TargetPolicy.ColumnFrontOnceLeftToRight)
            {
                _remainingTargetSlots = configuredTargetCount <= 0
                    ? EnemyFormationLayout.Columns : Math.Min(configuredTargetCount, EnemyFormationLayout.Columns);
                _subHitStarted = true;
                return;
            }
            if (policy == TargetPolicy.FrozenNearestAttackableRow)
            {
                List<int> candidates = GetEligibleTargets(enemies);
                if (candidates.Count > 0)
                {
                    // A sweep covers one physical row. Temporary threat priority
                    // only orders single-target attacks; fresh backfill must not
                    // split a row shared with a surviving deep or wide enemy.
                    int nearestRow = int.MaxValue;
                    for (int i = 0; i < candidates.Count; i++)
                        nearestRow = Math.Min(nearestRow, enemies[candidates[i]].row);
                    for (int i = 0; i < candidates.Count; i++)
                        if (enemies[candidates[i]].row == nearestRow)
                            _frozenRowRuntimeIds.Add(enemies[candidates[i]].runtimeId);
                }
                _remainingTargetSlots = configuredTargetCount <= 0
                    ? _frozenRowRuntimeIds.Count
                    : Math.Min(configuredTargetCount, _frozenRowRuntimeIds.Count);
                _subHitStarted = true;
                return;
            }
            _remainingTargetSlots = policy == TargetPolicy.AllAttackable ||
                configuredTargetCount <= 0
                    ? GetEligibleTargets(enemies).Count
                    : configuredTargetCount;
            _subHitStarted = true;
        }

        public bool TrySelectNext(
            IList<EnemyStats> enemies,
            out int targetIndex)
        {
            targetIndex = -1;
            if (!_subHitStarted)
                throw new InvalidOperationException(
                    "选择目标前必须调用 BeginSubHit。");
            // Native random targeting validates the prefetched column before
            // retiring a completed Amount. Even the last object's tail draws
            // belong to the authoritative random tape, without another hit.
            if (_activePolicy == TargetPolicy.RandomColumnBag ||
                _activePolicy == TargetPolicy.RandomColumnBagMultiHit)
                return TrySelectRandomColumnBag(enemies, out targetIndex);
            if (_activePolicy == TargetPolicy.SequentialColumnSweep)
                return _columnSweep.TrySelectNext(enemies, out targetIndex);
            if (_remainingTargetSlots <= 0)
                return false;
            _remainingTargetSlots--;

            if (_activePolicy == TargetPolicy.PlayerSelected)
                throw new InvalidOperationException(
                    "首发未实现 PlayerSelected；攻击必须自动选敌。");

            List<int> candidates = _activePolicy == TargetPolicy.FrozenAttackableSet
                ? _frozenAttackTargets.SelectDirectTargets(enemies, _bossOverkill)
                : GetEligibleTargets(enemies);
            if (_targetColumn >= 0)
                candidates.RemoveAll(index =>
                {
                    int column = EnemyColumn(enemies, index);
                    return column > _targetColumn ||
                        column + enemies[index].GridWidth <= _targetColumn;
                });
            candidates.RemoveAll(index => _selectedThisSubHit.Contains(index));
            if (_activePolicy == TargetPolicy.ColumnFrontOnceLeftToRight)
            {
                while (_waveNextColumn < EnemyFormationLayout.Columns)
                {
                    int column = _waveNextColumn++;
                    targetIndex = FindColumnFrontCandidate(enemies, candidates, column);
                    if (targetIndex < 0) continue;
                    _selectedThisSubHit.Add(targetIndex);
                    return true;
                }
                return false;
            }
            if (_activePolicy == TargetPolicy.FrozenNearestAttackableRow)
                candidates.RemoveAll(index =>
                    !_frozenRowRuntimeIds.Contains(enemies[index].runtimeId));
            if (candidates.Count == 0)
                return false;

            // Restart the initial left-to-right identity list per object; the
            // universe itself is retained for the whole execution.
            if (_activePolicy == TargetPolicy.FrozenAttackableSet)
            {
                targetIndex = candidates[0];
                _selectedThisSubHit.Add(targetIndex);
                return true;
            }

            targetIndex = SelectByPolicy(enemies, candidates);

            if (targetIndex < 0)
                return false;
            _selectedThisSubHit.Add(targetIndex);
            return true;
        }

        private int SelectByPolicy(
            IList<EnemyStats> enemies,
            List<int> candidates)
        {
            TargetPolicy policy = _activePolicy;

            if (policy == TargetPolicy.FrozenNearestAttackableRow)
                return SelectSpatial(enemies, candidates,
                    enemies[candidates[0]].row, reverse: false);

            int nearestRow = EnemyRow(enemies, candidates[0]);
            candidates.RemoveAll(index => EnemyRow(enemies, index) != nearestRow);

            // Fire coverage ranks only this existing nearest threat tier. Once
            // covered, keep its original spatial cursor rather than opening a
            // farther row or choosing the column with the fewest layers.
            if (policy == TargetPolicy.LeftToRight && _terrainCoverage != null &&
                _activeTerrainEffect?.targetMode == TerrainTargetMode.ColumnDynamicFront &&
                _activeTerrainEffect.effectKind == TerrainEffectKind.Damage &&
                _activeTerrainEffect.baseMagnitude > 0 && _activeTerrainEffect.magnitudeScale > 0f &&
                _activeTerrainEffect.triggerCount > 0 &&
                !string.IsNullOrWhiteSpace(_activeTerrainEffect.effectId))
            {
                string effectId = _activeTerrainEffect.effectId;
                if (candidates.Exists(index =>
                        !_terrainCoverage.IsCovered(effectId, EnemyColumn(enemies, index))))
                    candidates.RemoveAll(index =>
                        _terrainCoverage.IsCovered(effectId, EnemyColumn(enemies, index)));
            }

            if (policy == TargetPolicy.RandomAttackable &&
                _mode == ResolutionMode.Actual)
            {
                int pick = _random != null
                    ? _random.Range(0, candidates.Count)
                    : UnityEngine.Random.Range(0, candidates.Count);
                return candidates[Mathf.Clamp(pick, 0, candidates.Count - 1)];
            }

            if (policy == TargetPolicy.UpcomingAttackFirst ||
                policy == TargetPolicy.UpcomingAttackFirstRightToLeft)
            {
                return SelectUpcomingFirst(
                    enemies,
                    candidates,
                    nearestRow,
                    reverse: policy ==
                        TargetPolicy.UpcomingAttackFirstRightToLeft);
            }

            if (policy == TargetPolicy.FirstAttackable)
                return candidates[0];

            bool reverse = policy == TargetPolicy.SequentialRightToLeft ||
                policy == TargetPolicy.PlayedTargetThenSequential;
            return SelectSpatial(enemies, candidates, nearestRow, reverse);
        }

        /// <summary>
        /// An ordered projectile still selects a column after all real targets
        /// have run out. VC's AddDummyEnemies wraps the current column, records
        /// it without a damage event, then advances in the same direction.
        /// </summary>
        internal bool TrySelectEmptyColumn(out int column)
        {
            column = _targetColumn;
            if (column >= 0) return true;
            bool reverse = _activePolicy == TargetPolicy.SequentialRightToLeft ||
                _activePolicy == TargetPolicy.PlayedTargetThenSequential;
            if (!reverse && _activePolicy != TargetPolicy.LeftToRight &&
                _activePolicy != TargetPolicy.MultiTarget)
                return false;
            column = _nextEmptyColumn >= 0 ? _nextEmptyColumn
                : reverse ? EnemyFormationLayout.Columns - 1 : 0;
            _nextEmptyColumn = PositiveModulo(column + (reverse ? -1 : 1),
                EnemyFormationLayout.Columns);
            return true;
        }

        private List<int> GetEligibleTargets(
            IList<EnemyStats> enemies)
            => AttackResolver.GetResolutionTargets(
                enemies,
                _bossOverkill);

        private int SelectUpcomingFirst(
            IList<EnemyStats> enemies,
            List<int> candidates,
            int row,
            bool reverse)
        {
            var unvisited = candidates.FindAll(index =>
                !_upcomingCycleVisited.Contains(index));
            if (unvisited.Count == 0)
            {
                for (int i = 0; i < candidates.Count; i++)
                    _upcomingCycleVisited.Remove(candidates[i]);
                unvisited.AddRange(candidates);
            }

            List<int> prioritized = unvisited.FindAll(index =>
                IsUpcomingAttacker(enemies, index));
            List<int> pool = prioritized.Count > 0
                ? prioritized
                : unvisited;
            // 天师杖的“攻击者优先”和“兜底”各自按右→左排序；不能让
            // 攻击者组结束时的游标改变兜底组的起点。桃木剑保留既有连续游标。
            int selected = reverse
                ? SelectEdge(enemies, pool, takeRightmost: true)
                : SelectSpatial(enemies, pool, row, reverse: false);
            _upcomingCycleVisited.Add(selected);
            return selected;
        }

        private static int SelectEdge(
            IList<EnemyStats> enemies,
            List<int> candidates,
            bool takeRightmost)
        {
            candidates.Sort((a, b) =>
            {
                int columnOrder = EnemyColumn(enemies, a)
                    .CompareTo(EnemyColumn(enemies, b));
                return columnOrder != 0 ? columnOrder : a.CompareTo(b);
            });
            return takeRightmost
                ? candidates[candidates.Count - 1]
                : candidates[0];
        }

        private bool TrySelectRandomColumnBag(IList<EnemyStats> enemies, out int targetIndex)
        {
            targetIndex = -1;
            bool multiHit = _activePolicy == TargetPolicy.RandomColumnBagMultiHit;
            // Native's emergency exit retires the entire remaining attack, not
            // just one Amount. BeginSubHit must not re-enable an aborted batch.
            if (multiHit && _randomMultiHitAborted) return false;
            if (_randomAdvancePending)
            {
                if (_randomLastTarget >= 0 && _randomLastTarget < enemies.Count &&
                    enemies[_randomLastTarget]?.IsDead == true)
                {
                    // MultiHit's successful same-column replacement resets the
                    // search counter. Both death paths still prefetch Next.
                    if (multiHit && FindColumnFrontCandidate(enemies,
                            GetEligibleTargets(enemies), _randomCurrentColumn) >= 0)
                        _randomScannedColumns = 0;
                    else
                        _randomScannedColumns++;
                }
                _randomCurrentColumn = DrawRandomColumn();
                _randomAdvancePending = false;
                _randomLastTarget = -1;
            }
            if (_randomCurrentColumn < 0)
                _randomCurrentColumn = DrawRandomColumn();

            // ProcessColumns checks its safety exit before AliveCount, including
            // a death advance that just crossed the limit.
            if (multiHit && _randomScannedColumns > 100000)
            {
                _randomMultiHitAborted = true;
                return false;
            }

            List<int> candidates = GetEligibleTargets(enemies);
            // Native targeting stops after the post-hit prefetch when no
            // attackable enemies remain; do not spend RNG scanning empty slots.
            if (candidates.Count == 0) return false;
            if (_targetColumn >= 0)
                candidates.RemoveAll(index => EnemyColumn(enemies, index) > _targetColumn ||
                    EnemyRightColumn(enemies, index) < _targetColumn);
            while (multiHit || _randomScannedColumns < EnemyFormationLayout.Columns)
            {
                if (multiHit && _randomScannedColumns > 100000)
                {
                    _randomMultiHitAborted = true;
                    return false;
                }
                int candidate = FindColumnFrontCandidate(enemies, candidates, _randomCurrentColumn);
                // Empty, unavailable and already-hit slots are consumed from
                // the complete bag. Keep the next draw when this Amount ends.
                if (candidate < 0)
                {
                    _randomScannedColumns++;
                    _randomCurrentColumn = DrawRandomColumn();
                    continue;
                }
                if (_remainingTargetSlots <= 0) return false;
                if (!multiHit && _selectedThisSubHit.Contains(candidate))
                {
                    _randomScannedColumns++;
                    _randomCurrentColumn = DrawRandomColumn();
                    continue;
                }
                _remainingTargetSlots--;
                if (!multiHit) _selectedThisSubHit.Add(candidate);
                _randomLastTarget = candidate;
                _randomAdvancePending = true;
                targetIndex = candidate;
                return true;
            }
            return false;
        }

        private int DrawRandomColumn()
        {
            if (_randomColumnBag.Count == 0) RefillRandomColumnBag();
            int ordinal = _mode == ResolutionMode.Actual
                ? _random?.Range(0, _randomColumnBag.Count) ?? UnityEngine.Random.Range(0, _randomColumnBag.Count)
                : 0;
            ordinal = Mathf.Clamp(ordinal, 0, _randomColumnBag.Count - 1);
            int column = _randomColumnBag[ordinal];
            _randomColumnBag.RemoveAt(ordinal);
            if (_randomColumnBag.Count == 0) RefillRandomColumnBag();
            return column;
        }

        private void RefillRandomColumnBag()
        {
            for (int column = 0; column < EnemyFormationLayout.Columns; column++)
                _randomColumnBag.Add(column);
        }

        private static int FindColumnFrontCandidate(
            IList<EnemyStats> enemies,
            List<int> candidates,
            int column)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                int index = candidates[i];
                if (EnemyColumn(enemies, index) <= column &&
                    EnemyRightColumn(enemies, index) >= column)
                {
                    return index;
                }
            }
            return -1;
        }

        private bool IsUpcomingAttacker(
            IList<EnemyStats> enemies,
            int targetIndex)
        {
            if (_upcomingIntents == null || targetIndex < 0 ||
                targetIndex >= _upcomingIntents.Count ||
                targetIndex >= enemies.Count)
            {
                return false;
            }

            EnemyStats enemy = enemies[targetIndex];
            return EnemyFormationLayout.CanAttackPlayer(enemy) &&
                EnemyFormationLayout.IsColumnFront(enemies, targetIndex) &&
                !enemy.justFilledFrontRow &&
                !enemy.isDisarmed &&
                _upcomingIntents[targetIndex].ActionType ==
                    EnemyActionType.Attack;
        }

        private int SelectSpatial(
            IList<EnemyStats> enemies,
            List<int> candidates,
            int row,
            bool reverse)
        {
            candidates.Sort((a, b) =>
            {
                int columnOrder = EnemyColumn(enemies, a)
                    .CompareTo(EnemyColumn(enemies, b));
                return columnOrder != 0 ? columnOrder : a.CompareTo(b);
            });

            Dictionary<int, int> cursors = reverse
                ? _rightCursors
                : _leftCursors;
            int cursor = _spatialProgress != null
                ? _spatialProgress.GetCursor(_activePolicy, row, reverse)
                : cursors.TryGetValue(row, out int stored)
                    ? stored : reverse ? EnemyFormationLayout.Columns - 1 : 0;
            int selected = -1;

            if (reverse)
            {
                for (int i = candidates.Count - 1; i >= 0; i--)
                {
                    if (EnemyRightColumn(enemies, candidates[i]) > cursor)
                        continue;
                    selected = candidates[i];
                    break;
                }
                if (selected < 0)
                    selected = candidates[candidates.Count - 1];
            }
            else
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (EnemyColumn(enemies, candidates[i]) < cursor)
                        continue;
                    selected = candidates[i];
                    break;
                }
                if (selected < 0)
                    selected = candidates[0];
            }

            int nextCursor = PositiveModulo(reverse
                    ? EnemyColumn(enemies, selected) - 1
                    : EnemyRightColumn(enemies, selected) + 1,
                EnemyFormationLayout.Columns);
            _nextEmptyColumn = nextCursor;
            if (_spatialProgress != null)
                _spatialProgress.SetCursor(_activePolicy, row, nextCursor);
            else
                cursors[row] = nextCursor;

            return selected;
        }

        private static int EnemyRow(IList<EnemyStats> enemies, int index)
            => EnemyFormationLayout.GetPlayerTargetPriorityRow(enemies[index]);

        private static int EnemyColumn(IList<EnemyStats> enemies, int index)
        {
            EnemyStats enemy = enemies[index];
            return enemy != null && enemy.column >= 0
                ? enemy.column
                : index % EnemyFormationLayout.Columns;
        }

        private static int EnemyRightColumn(
            IList<EnemyStats> enemies,
            int index)
            => EnemyColumn(enemies, index) +
                Math.Max(1, enemies[index]?.GridWidth ?? 1) - 1;

        private static int PositiveModulo(int value, int modulus)
            => (value % modulus + modulus) % modulus;
    }
}
