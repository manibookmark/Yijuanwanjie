using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns one-turn column replay queues. Initial projectiles append their
    /// selected columns, including empty slots and duplicates; turn end consumes
    /// them before publishing callbacks.
    /// Only base damage/Area rules and source identity survive the cast, never
    /// Combo, Amount, cards, gems or LimitBreak. A fresh encounter has an empty owner.
    /// </summary>
    public sealed class ColumnReplayAttackSystem
    {
        private sealed class Cast
        {
            internal int RuntimeId, BaseDamage;
            internal string ContentId;
            internal bool AreaEnabled;
            internal float AreaCoefficient;
            internal int[] Columns;
        }

        private readonly CombatState _state;
        private readonly List<Cast> _pending = new List<Cast>();
        private bool _triggering;
        public int PendingColumnCount
        {
            get
            {
                int count = 0;
                foreach (Cast cast in _pending) count = checked(count + cast.Columns.Length);
                return count;
            }
        }

        public event Action<ColumnReplayAttackEvent> Resolved;
        internal event Action<AttackResolutionResult> AttackResolved;

        internal ColumnReplayAttackSystem(CombatState state)
            => _state = state ?? throw new ArgumentNullException(nameof(state));

        internal void RegisterResolvedAttack(CardConfig card, CardEffectSpec effect,
            AttackResolutionResult result)
        {
            if (effect?.replayColumnsAtPlayerTurnEnd != true ||
                _state.IsBattleOver || _state.IsDefeat || result == null ||
                result.DirectTargetColumns.Count == 0) return;
            if (effect.effectType != EffectType.Damage || effect.repeatAtPlayerTurnEnd ||
                card == null || card.runtimeInstanceId <= 0)
                throw new ArgumentException("Column replay requires a real independent Damage source.");
            DamageSpec damage = effect.damageSpec?.baseHitDamage > 0 ? effect.damageSpec : null;
            int baseDamage = damage?.baseHitDamage ?? effect.amount;
            if (baseDamage <= 0 || result.DirectTargetColumns.Count > AttackPlanBuilder.MaximumSubHitsPerPlan)
                throw new ArgumentException("Invalid column replay base damage or attack budget.");
            var columns = new int[result.DirectTargetColumns.Count];
            for (int i = 0; i < columns.Length; i++)
            {
                columns[i] = result.DirectTargetColumns[i];
                if (columns[i] < 0 || columns[i] >= EnemyFormationLayout.Columns)
                    throw new ArgumentException("Invalid column replay target column.");
            }
            _pending.Add(new Cast {
                RuntimeId = card.runtimeInstanceId, ContentId = card.contentId ?? string.Empty,
                BaseDamage = baseDamage, AreaEnabled = damage?.area?.enabled ?? true,
                AreaCoefficient = damage?.area?.coefficient ?? 0.2f, Columns = columns });
        }

        /// <summary>Consumes exactly this turn's queue; no targets still consumes it and reentry cannot repeat it.</summary>
        internal void OnPlayerTurnEnded()
        {
            if (_triggering) return;
            if (_state.IsBattleOver || _state.IsDefeat) { Clear(); return; }
            _triggering = true;
            var due = new List<Cast>(_pending);
            _pending.Clear();
            // End-boundary values are captured once, before replay callbacks.
            var stats = new CombatStatSnapshot(_state.Player, 0);
            try
            {
                foreach (Cast cast in due)
                {
                    if (_state.IsBattleOver || _state.IsDefeat) break;
                    AttackPlan plan = BuildPlan(cast, stats);
                    var targets = new List<CombatEffectTarget>();
                    for (int i = 0; i < _state.Enemies.Count; i++)
                        if (_state.Enemies[i] != null)
                            targets.Add(new CombatEffectTarget(_state.Enemies[i], i));
                    AttackResolutionResult result = AttackResolver.Execute(plan, _state.EnemyRoster,
                        ResolutionMode.Actual, _state.Random ?? DefaultRandomSource.Instance);
                    if (result.Events.Count == 0) continue;
                    var ids = new HashSet<int>();
                    foreach (DamageEvent hit in result.Events) ids.Add(hit.TargetEnemyRuntimeId);
                    targets.RemoveAll(target => !ids.Contains(target.RuntimeId));
                    Resolved?.Invoke(new ColumnReplayAttackEvent(cast.ContentId, _state.CurrentTurn,
                        plan, result, targets.AsReadOnly()));
                    AttackResolved?.Invoke(result);
                }
            }
            finally { _triggering = false; }
        }

        private static AttackPlan BuildPlan(Cast cast, CombatStatSnapshot stats)
        {
            int damage = DamageSystem.RoundToEven((double)cast.BaseDamage * (1f + stats.Might / 100f));
            var plan = new AttackPlan(cast.RuntimeId, stats.Area, canTriggerSecondaryEffects: false);
            var area = new AreaSpec { enabled = cast.AreaEnabled, coefficient = cast.AreaCoefficient,
                triggerMode = AreaTriggerMode.PerSubHit };
            for (int i = 0; i < cast.Columns.Length; i++)
            {
                plan.AddSubHit(new AttackSubHit(i, 0, damage, TargetPolicy.FirstAttackable,
                    1, new CritSpec { enabled = false }, area, null, null, null, cast.Columns[i]));
                plan.AddDisplayedDamage(damage);
            }
            return plan;
        }

        internal void Clear() => _pending.Clear();
    }
}
