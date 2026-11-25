using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Immutable pre-flight strike and its original logical anchor.
    /// These receipts are already applied to rules; a trace only schedules their feedback.</summary>
    public sealed class RicochetInitialStrike
    {
        public bool HasTarget => TargetIndex >= 0;
        public int TargetIndex { get; }
        public int TargetEnemyRuntimeId { get; }
        public int TargetRow { get; }
        public int TargetColumn { get; }
        public int TargetOriginColumn { get; }
        public int TargetWidth { get; }
        public int TargetDepth { get; }
        public float AnchorX { get; }
        public float AnchorY { get; }
        public IReadOnlyList<DamageEvent> DamageEvents { get; }

        internal RicochetInitialStrike(int target, EnemyStats enemy, int column,
            IReadOnlyList<DamageEvent> events)
        {
            TargetIndex = target;
            TargetEnemyRuntimeId = enemy?.runtimeId ?? 0;
            TargetRow = enemy?.row ?? 0;
            TargetColumn = column;
            TargetOriginColumn = enemy != null && enemy.column >= 0 ? enemy.column
                : target >= 0 ? target % EnemyFormationLayout.Columns : -1;
            TargetWidth = enemy?.GridWidth ?? 1;
            TargetDepth = enemy?.GridDepth ?? 1;
            AnchorX = HasTarget ? (column + 0.5f) / EnemyFormationLayout.Columns : 0.5f;
            AnchorY = HasTarget ? (TargetRow + TargetDepth * 0.5f) / EnemyFormationLayout.VisibleRows : 0.5f;
            DamageEvents = new List<DamageEvent>(events).AsReadOnly();
        }

        internal RicochetInitialStrike(RicochetInitialStrike anchor, IReadOnlyList<DamageEvent> events)
        {
            TargetIndex = anchor.TargetIndex; TargetEnemyRuntimeId = anchor.TargetEnemyRuntimeId;
            TargetRow = anchor.TargetRow; TargetColumn = anchor.TargetColumn;
            TargetOriginColumn = anchor.TargetOriginColumn;
            TargetWidth = anchor.TargetWidth; TargetDepth = anchor.TargetDepth;
            AnchorX = anchor.AnchorX; AnchorY = anchor.AnchorY;
            DamageEvents = new List<DamageEvent>(events).AsReadOnly();
        }
    }

    /// <summary>
    /// Owns each ricochet's pre-applied strike, feedback delivery and segment column locks.
    /// All initial strikes settle before any flight. Delivery never writes HP or emits another
    /// damage event; a later legal collision does. Geometry and the volley clock live in the resolver.
    /// </summary>
    internal sealed class RicochetInitialStrikeOwner
    {
        private sealed class StrikeState
        {
            internal RicochetInitialStrike Initial;
            internal readonly HashSet<int> LockedColumns = new HashSet<int>();
            internal bool InitialDelivered;
            internal int NextDamageOrdinal = 1;
        }

        private readonly AttackPlan _plan;
        private readonly IList<EnemyStats> _enemies;
        private readonly AttackResolutionResult _result;
        private readonly BossOverkillSession _bossOverkill;
        private readonly List<StrikeState> _strikes = new List<StrikeState>();

        internal RicochetInitialStrikeOwner(AttackPlan plan, IList<EnemyStats> enemies,
            AttackResolutionResult result, ResolutionMode mode, IRandomSource random,
            BossOverkillSession bossOverkill)
        {
            _plan = plan; _enemies = enemies; _result = result; _bossOverkill = bossOverkill;
            var selector = new AttackTargetSelector(mode, random, null, bossOverkill);
            for (int projectile = 0; projectile < plan.SubHits.Count; projectile++)
            {
                AttackSubHit hit = plan.SubHits[projectile];
                selector.BeginSubHit(enemies, TargetPolicy.RandomColumnBag, 1, hit.TargetColumn);
                RicochetInitialStrike initial = null;
                while (selector.TrySelectNext(enemies, out int target))
                {
                    int column = selector.SelectedRandomColumn;
                    EnemyStats enemy = enemies[target];
                    // Capture the object and anchor before direct damage or its splash can kill it.
                    var snapshot = new RicochetInitialStrike(target, enemy, column, Array.Empty<DamageEvent>());
                    int firstReceipt = result.Events.Count;
                    ApplyHit(projectile, target, 0, column);
                    var receipts = new List<DamageEvent>();
                    for (int i = firstReceipt; i < result.Events.Count; i++) receipts.Add(result.Events[i]);
                    initial = new RicochetInitialStrike(snapshot, receipts);
                }
                _strikes.Add(new StrikeState { Initial = initial ??
                    new RicochetInitialStrike(-1, null, -1, Array.Empty<DamageEvent>()) });
            }
        }

        internal RicochetInitialStrike GetInitialStrike(int projectile) => _strikes[projectile].Initial;
        internal bool HasUndeliveredInitial(int projectile)
            => _strikes[projectile].Initial.HasTarget && !_strikes[projectile].InitialDelivered;

        internal bool CanDeliverInitial(int projectile)
            => HasUndeliveredInitial(projectile) &&
                !_strikes[projectile].LockedColumns.Contains(_strikes[projectile].Initial.TargetColumn);

        internal bool CanDamageEnemy(int projectile, int target)
        {
            StrikeState state = _strikes[projectile];
            EnemyStats enemy = _enemies[target];
            if (enemy == null || (HasUndeliveredInitial(projectile) &&
                enemy.runtimeId == state.Initial.TargetEnemyRuntimeId)) return false;
            int column = Column(enemy, target);
            for (int c = column; c < column + enemy.GridWidth; c++)
                if (state.LockedColumns.Contains(c)) return false;
            return true;
        }

        internal void DeliverInitial(int projectile, bool fallback = false)
        {
            if (!HasUndeliveredInitial(projectile))
                throw new InvalidOperationException("A ricochet initial receipt can only be delivered once.");
            if (!fallback && !CanDeliverInitial(projectile))
                throw new InvalidOperationException("The current ricochet segment has already delivered this column.");
            StrikeState state = _strikes[projectile];
            state.InitialDelivered = true;
            state.LockedColumns.Add(state.Initial.TargetColumn);
        }

        internal int ResolveAdditionalHit(int projectile, int target)
        {
            if (!CanDamageEnemy(projectile, target))
                throw new InvalidOperationException("This ricochet collision is awaiting feedback or its column is locked.");
            StrikeState state = _strikes[projectile];
            if (state.NextDamageOrdinal >= _plan.RicochetProjectile.MaximumCollisionsPerProjectile)
                throw new InvalidOperationException("Ricochet collision bound invariant was exceeded.");
            int ordinal = state.NextDamageOrdinal++;
            EnemyStats enemy = _enemies[target];
            int column = Column(enemy, target);
            for (int c = column; c < column + enemy.GridWidth; c++) state.LockedColumns.Add(c);
            ApplyHit(projectile, target, ordinal, column);
            return ordinal;
        }

        internal void BeginReflectedSegment(int projectile) => _strikes[projectile].LockedColumns.Clear();

        private void ApplyHit(int projectile, int target, int ordinal, int column)
        {
            AttackSubHit hit = _plan.SubHits[projectile];
            _result.RecordDirectTargetColumn(column);
            int splash = hit.Area?.enabled == true && hit.Area.triggerMode != AreaTriggerMode.Disabled
                ? DamageSystem.CalculateSplashDamage(hit.UnroundedHitDamage, _plan.AreaStat, hit.Area.coefficient) : 0;
            // Native area sees this hit's front roster before its direct death exposes a replacement.
            // Freeze only this batch's identities; the next hit can select the newly exposed enemy.
            List<int> areaTargets = splash > 0 ? AttackResolver.GetAttackableTargets(_enemies) : new List<int>();
            var defeats = new List<EnemyDefeatResult>();
            _result.AddDamage(DamageSystem.ResolveEnemyDamage(_enemies, target,
                new EnemyDamageRequest(_plan.SourceCardInstanceId, hit.AttackInstanceIndex,
                    ordinal, DamageKind.Direct, hit.NormalHitDamage,
                    canTriggerArea: hit.Area?.enabled == true,
                    canTriggerSecondaryEffects: _plan.CanTriggerSecondaryEffects)), defeats);
            foreach (int splashTarget in areaTargets)
            {
                if (_enemies[splashTarget] == null || _enemies[splashTarget].IsDead) continue;
                _result.AddDamage(DamageSystem.ResolveEnemyDamage(_enemies, splashTarget,
                    new EnemyDamageRequest(_plan.SourceCardInstanceId, hit.AttackInstanceIndex,
                        ordinal, DamageKind.Splash, splash)), defeats);
            }
            _bossOverkill?.TryBeginAfterDamageBatch(defeats);
        }

        private static int Column(EnemyStats enemy, int index)
            => enemy.column >= 0 ? enemy.column : index % EnemyFormationLayout.Columns;
    }
}
