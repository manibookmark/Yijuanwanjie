using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum TerrainEffectChangeKind
    {
        Placed,
        Triggered,
        Removed
    }

    /// <summary>
    /// 地形系统向战斗表现发布的不可变快照。表现层只消费该事件，不读取或修改
    /// TerrainEffectInstance，从而不会把粒子生命周期混入权威结算。
    /// </summary>
    public readonly struct TerrainEffectChange
    {
        public readonly TerrainEffectChangeKind ChangeKind;
        public readonly int RuntimeId;
        public readonly string EffectId;
        public readonly TerrainEffectKind EffectKind;
        public readonly TerrainEffectTiming TriggerTiming;
        public readonly DamageKind DamageKind;
        public readonly int SourceCardInstanceId;
        public readonly int AttackInstanceIndex;
        public readonly int TargetIndex;
        public readonly int Column;
        public readonly int Row;
        public readonly int MagnitudePerStack;
        public readonly int Stacks;
        public readonly int RemainingTriggers;
        public readonly TerrainTargetMode TargetMode;
        public readonly bool PersistUntilCombatEnd;

        public int TotalMagnitude => MagnitudePerStack * Stacks;

        internal TerrainEffectChange(
            TerrainEffectChangeKind changeKind,
            TerrainEffectInstance instance,
            int attackInstanceIndex,
            int targetIndex)
        {
            ChangeKind = changeKind;
            RuntimeId = instance.RuntimeId;
            EffectId = instance.EffectId;
            EffectKind = instance.EffectKind;
            TriggerTiming = instance.TriggerTiming;
            DamageKind = instance.DamageKind;
            SourceCardInstanceId = instance.SourceCardInstanceId;
            AttackInstanceIndex = attackInstanceIndex;
            TargetIndex = targetIndex;
            Column = instance.Column;
            Row = instance.Row;
            MagnitudePerStack = instance.MagnitudePerStack;
            Stacks = instance.Stacks;
            RemainingTriggers = instance.RemainingTriggers;
            TargetMode = instance.TargetMode;
            PersistUntilCombatEnd = instance.PersistUntilCombatEnd;
        }
    }

    /// <summary>
    /// 不绑定单位的权威地形实例。CellSnapshot以格和批次保留快照；
    /// ColumnDynamicFront以列与effectId跨出牌累层，最近来源仅用于伤害归属。
    /// </summary>
    public sealed class TerrainEffectInstance
    {
        public int RuntimeId { get; internal set; }
        public string EffectId { get; internal set; }
        public TerrainEffectKind EffectKind { get; internal set; }
        public TerrainEffectTiming TriggerTiming { get; internal set; }
        public DamageKind DamageKind { get; internal set; }
        public int SourceCardInstanceId { get; internal set; }
        public int Column { get; internal set; }
        public int Row { get; internal set; }
        public int MagnitudePerStack { get; internal set; }
        public int Stacks { get; internal set; }
        public int RemainingTriggers { get; internal set; }
        public TerrainTargetMode TargetMode { get; internal set; }
        public float AreaCoefficient { get; internal set; }
        public bool PersistUntilCombatEnd { get; internal set; }
        public int TotalMagnitude => MagnitudePerStack * Stacks;
    }

    /// <summary>
    /// 地形效果的统一生命周期模块。旧格快照按批次独立，动态列层跨出牌累积；
    /// 本模块唯一拥有叠层、目标刷新、当前属性结算、击败发布和移除规则。
    /// </summary>
    public sealed class TerrainEffectSystem
    {
        private readonly CombatState _state;
        private readonly List<TerrainEffectInstance> _active =
            new List<TerrainEffectInstance>();
        private int _nextRuntimeId = 1;

        public event Action<TerrainEffectChange> Changed;
        public event Action<DamageEvent> DamageResolved;
        public event Action<EnemyDefeatResult> EnemyDefeated;

        public IReadOnlyList<TerrainEffectInstance> Active => _active;

        /// <summary>Changes only when owned layers or their lifetime change; preview reads never advance it.</summary>
        public long Revision { get; private set; }

        public TerrainEffectSystem(CombatState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// 接收完整攻击产生的放置。旧格快照只合并本批；动态列层跨批合并，
        /// 不保存叠势、数量、初击锋锐或原卡灵印，避免回合末再次乘宿主倍率。
        /// </summary>
        public void Schedule(IReadOnlyList<TerrainEffectPlacement> placements)
        {
            if (placements == null || placements.Count == 0) return;

            int batchStart = _active.Count;
            for (int i = 0; i < placements.Count; i++)
            {
                TerrainEffectPlacement placement = placements[i];
                if (string.IsNullOrWhiteSpace(placement.EffectId) ||
                    placement.MagnitudePerStack <= 0 ||
                    placement.TriggerCount <= 0)
                {
                    continue;
                }

                TerrainEffectInstance instance = FindBatchMatch(
                    batchStart,
                    placement);
                if (instance == null)
                {
                    instance = new TerrainEffectInstance
                    {
                        RuntimeId = _nextRuntimeId++,
                        EffectId = placement.EffectId,
                        EffectKind = placement.EffectKind,
                        TriggerTiming = placement.TriggerTiming,
                        DamageKind = placement.DamageKind,
                        SourceCardInstanceId = placement.SourceCardInstanceId,
                        Column = placement.Column,
                        Row = placement.Row,
                        MagnitudePerStack = placement.MagnitudePerStack,
                        Stacks = 0,
                        RemainingTriggers = placement.TriggerCount,
                        TargetMode = placement.TargetMode,
                        AreaCoefficient = placement.AreaCoefficient,
                        PersistUntilCombatEnd = placement.PersistUntilCombatEnd
                    };
                    _active.Add(instance);
                }

                if (instance.TargetMode == TerrainTargetMode.ColumnDynamicFront)
                    instance.SourceCardInstanceId = placement.SourceCardInstanceId;
                instance.Stacks = checked(instance.Stacks + 1);
                Revision = checked(Revision + 1);
                Changed?.Invoke(new TerrainEffectChange(
                    TerrainEffectChangeKind.Placed,
                    instance,
                    placement.AttackInstanceIndex,
                    placement.TargetIndex));
            }
        }

        /// <summary>
        /// 同格旧快照聚合后一次命中；动态列以当前前敌和当前锋锐、裂空结算，
        /// 每列一次聚合直击。基础列层随后清除，持久列层仅在战斗清理时消失。
        /// </summary>
        public void Trigger(TerrainEffectTiming timing)
        {
            var due = new List<TerrainEffectInstance>();
            for (int i = 0; i < _active.Count; i++)
            {
                TerrainEffectInstance instance = _active[i];
                if (instance.TriggerTiming == timing)
                    due.Add(instance);
            }

            var processed = new HashSet<int>();
            // Dynamic columns fire left to right. Legacy cell snapshots retain their batch order.
            due.Sort((a, b) => a.TargetMode != b.TargetMode
                ? a.TargetMode.CompareTo(b.TargetMode)
                : a.TargetMode == TerrainTargetMode.ColumnDynamicFront
                    ? b.Column.CompareTo(a.Column) : a.RuntimeId.CompareTo(b.RuntimeId));
            for (int i = due.Count - 1; i >= 0; i--)
            {
                TerrainEffectInstance seed = due[i];
                if (!processed.Add(seed.RuntimeId)) continue;

                var triggerGroup = new List<TerrainEffectInstance> { seed };
                if (seed.EffectKind == TerrainEffectKind.Damage)
                {
                    for (int j = i - 1; j >= 0; j--)
                    {
                        TerrainEffectInstance candidate = due[j];
                        if (!CanAggregateDamage(seed, candidate) ||
                            !processed.Add(candidate.RuntimeId))
                        {
                            continue;
                        }
                        triggerGroup.Add(candidate);
                    }
                }

                int targetIndex = seed.TargetMode == TerrainTargetMode.ColumnDynamicFront
                    ? FindFrontEnemyInColumn(seed.Column)
                    : FindEnemyAtCell(seed.Column, seed.Row);
                EnemyDamageReceipt? damageReceipt = ResolveEffect(
                    triggerGroup,
                    targetIndex);

                for (int j = 0; j < triggerGroup.Count; j++)
                {
                    TerrainEffectInstance instance = triggerGroup[j];
                    if (!instance.PersistUntilCombatEnd)
                    {
                        instance.RemainingTriggers--;
                        Revision = checked(Revision + 1);
                    }
                    Changed?.Invoke(new TerrainEffectChange(
                        TerrainEffectChangeKind.Triggered,
                        instance,
                        -1,
                        targetIndex));
                }

                if (damageReceipt.HasValue)
                {
                    EnemyDamageReceipt resolved = damageReceipt.Value;
                    DamageResolved?.Invoke(resolved.Event);
                    if (resolved.TryGetDefeat(
                            out EnemyDefeatResult defeat))
                        EnemyDefeated?.Invoke(defeat);
                    if (seed.TargetMode == TerrainTargetMode.ColumnDynamicFront)
                        ResolveColumnSplash(seed, resolved.Event.RequestedDamage);
                }

                for (int j = 0; j < triggerGroup.Count; j++)
                {
                    TerrainEffectInstance instance = triggerGroup[j];
                    if (instance.RemainingTriggers > 0) continue;
                    if (_active.Remove(instance))
                        Revision = checked(Revision + 1);
                    Changed?.Invoke(new TerrainEffectChange(
                        TerrainEffectChangeKind.Removed,
                        instance,
                        -1,
                        targetIndex));
                }
            }
        }

        public void ClearAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                TerrainEffectInstance instance = _active[i];
                _active.RemoveAt(i);
                Revision = checked(Revision + 1);
                Changed?.Invoke(new TerrainEffectChange(
                    TerrainEffectChangeKind.Removed,
                    instance,
                    -1,
                    -1));
            }
        }

        private TerrainEffectInstance FindBatchMatch(
            int batchStart,
            TerrainEffectPlacement placement)
        {
            int start = placement.TargetMode == TerrainTargetMode.ColumnDynamicFront ? 0 : batchStart;
            for (int i = start; i < _active.Count; i++)
            {
                TerrainEffectInstance candidate = _active[i];
                if (candidate.EffectId == placement.EffectId &&
                    candidate.EffectKind == placement.EffectKind &&
                    candidate.TriggerTiming == placement.TriggerTiming &&
                    candidate.DamageKind == placement.DamageKind &&
                    (placement.TargetMode == TerrainTargetMode.ColumnDynamicFront ||
                        candidate.SourceCardInstanceId == placement.SourceCardInstanceId) &&
                    candidate.Column == placement.Column &&
                    candidate.Row == placement.Row &&
                    candidate.MagnitudePerStack == placement.MagnitudePerStack &&
                    candidate.RemainingTriggers == placement.TriggerCount &&
                    candidate.TargetMode == placement.TargetMode &&
                    candidate.PersistUntilCombatEnd == placement.PersistUntilCombatEnd &&
                    Math.Abs(candidate.AreaCoefficient - placement.AreaCoefficient) < 0.0001f)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static bool CanAggregateDamage(
            TerrainEffectInstance left,
            TerrainEffectInstance right)
        {
            return right.EffectKind == TerrainEffectKind.Damage &&
                string.Equals(
                    left.EffectId,
                    right.EffectId,
                    StringComparison.Ordinal) &&
                left.TriggerTiming == right.TriggerTiming &&
                left.DamageKind == right.DamageKind &&
                left.Column == right.Column &&
                left.Row == right.Row &&
                left.TargetMode == right.TargetMode &&
                left.PersistUntilCombatEnd == right.PersistUntilCombatEnd &&
                Math.Abs(left.AreaCoefficient - right.AreaCoefficient) < 0.0001f;
        }

        private EnemyDamageReceipt? ResolveEffect(
            IReadOnlyList<TerrainEffectInstance> instances,
            int targetIndex)
        {
            if (instances == null || instances.Count == 0 ||
                instances[0].EffectKind != TerrainEffectKind.Damage ||
                targetIndex < 0 ||
                targetIndex >= _state.Enemies.Count)
            {
                return null;
            }

            TerrainEffectInstance representative = instances[0];
            long requestedTotal = 0;
            for (int i = 0; i < instances.Count; i++)
            {
                TerrainEffectInstance instance = instances[i];
                requestedTotal += (long)Math.Max(
                    0,
                    instance.MagnitudePerStack) * Math.Max(
                    0,
                    instance.Stacks);
            }
            decimal magnitude = requestedTotal;
            if (representative.TargetMode == TerrainTargetMode.ColumnDynamicFront)
                magnitude *= Math.Max(0m, 1m + _state.Player.fengRui / 100m);
            int requested = (int)Math.Min(int.MaxValue, magnitude);
            return DamageSystem.ResolveEnemyDamage(
                _state.EnemyRoster,
                targetIndex,
                new EnemyDamageRequest(
                    representative.SourceCardInstanceId,
                    -1,
                    -1,
                    representative.DamageKind,
                    requested));
        }

        private void ResolveColumnSplash(TerrainEffectInstance source, int directDamage)
        {
            int splash = DamageSystem.CalculateSplashDamage(directDamage,
                _state.Player.bengLie, source.AreaCoefficient);
            if (splash <= 0) return;
            List<int> targets = AttackResolver.GetAttackableTargets(_state.EnemyRoster);
            for (int i = 0; i < targets.Count; i++)
            {
                EnemyDamageReceipt receipt = DamageSystem.ResolveEnemyDamage(_state.EnemyRoster,
                    targets[i], new EnemyDamageRequest(source.SourceCardInstanceId, -1, -1,
                        DamageKind.Splash, splash));
                DamageResolved?.Invoke(receipt.Event);
                if (receipt.TryGetDefeat(out EnemyDefeatResult defeat)) EnemyDefeated?.Invoke(defeat);
            }
        }

        private int FindFrontEnemyInColumn(int column)
        {
            for (int i = 0; i < _state.Enemies.Count; i++)
            {
                EnemyStats enemy = _state.Enemies[i];
                if (enemy == null || !enemy.IsDeployed || enemy.IsWithdrawn ||
                    !EnemyFormationLayout.IsColumnFront(_state.Enemies, i)) continue;
                int left = enemy.column >= 0 ? enemy.column : i % EnemyFormationLayout.Columns;
                if (column >= left && column < left + enemy.GridWidth) return i;
            }
            return -1;
        }

        private int FindEnemyAtCell(int column, int row)
        {
            for (int i = 0; i < _state.Enemies.Count; i++)
            {
                EnemyStats enemy = _state.Enemies[i];
                if (enemy == null || enemy.IsDead ||
                    !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy))
                    continue;
                int enemyColumn = enemy.column >= 0
                    ? enemy.column
                    : i % EnemyFormationLayout.Columns;
                if (column >= enemyColumn &&
                    column < enemyColumn + enemy.GridWidth &&
                    row >= enemy.row &&
                    row < enemy.row + enemy.GridDepth)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
