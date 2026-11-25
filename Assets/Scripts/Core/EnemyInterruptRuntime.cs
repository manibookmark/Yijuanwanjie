using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns health-group admission and lifecycle binding for encounter interrupts.
    /// Counters remain exclusively in EnemyInterruptSystem; this module never selects
    /// an AI choice, consumes phase-start priority, or owns an action queue.
    /// </summary>
    public sealed class EnemyInterruptRuntime
    {
        private readonly CombatState _state;
        private readonly EnemyInterruptSystem _counters = new EnemyInterruptSystem();
        private readonly Dictionary<int, Binding> _bindings = new Dictionary<int, Binding>();

        private readonly struct Binding
        {
            internal Binding(EnemyStats actor, EnemyHealthBehaviorDefinition definition, int group)
            { Actor = actor; Definition = definition; Group = group; }
            internal EnemyStats Actor { get; }
            internal EnemyHealthBehaviorDefinition Definition { get; }
            internal int Group { get; }
        }

        internal EnemyInterruptRuntime(CombatState state) { _state = state; }

        public int Count => _counters.Count;
        public IReadOnlyList<EnemyInterruptSnapshot> Snapshot => _counters.Snapshot;
        public bool TryGetSnapshot(int runtimeId, out EnemyInterruptSnapshot snapshot)
            => _counters.TryGetSnapshot(runtimeId, out snapshot);

        /// <summary>Global ordinary-batch gate; cancelling an intent does not clear interrupt admission.</summary>
        public bool HasFrontInterrupts
        {
            get
            {
                for (int i = 0; i < _state.Enemies.Count; i++)
                {
                    EnemyStats enemy = _state.Enemies[i];
                    if (enemy != null && !enemy.IsDead && enemy.IsDeployed &&
                        EnemyFormationLayout.CanAttackPlayer(enemy) &&
                        EnemyFormationLayout.IsColumnFront(_state.Enemies, i) &&
                        _bindings.TryGetValue(enemy.runtimeId, out Binding binding) &&
                        ReferenceEquals(binding.Actor, enemy)) return true;
                }
                return false;
            }
        }

        internal void Synchronize()
        {
            var retained = new HashSet<int>();
            if (!_state.IsBattleOver)
                foreach (EnemyStats enemy in _state.Enemies)
                {
                    if (enemy == null || enemy.runtimeId <= 0 || enemy.IsDead || !enemy.IsDeployed ||
                        enemy.IsWithdrawn || enemy.isOverkillTarget) continue;
                    EnemyHealthBehaviorDefinition definition = enemy.behaviorDefinition?.HealthGroups;
                    if (definition == null || enemy.baseHealth <= 0) continue;
                    int group = definition.ResolveHealthGroupIndex(enemy.currentHealth, enemy.baseHealth);
                    if (group < 0)
                    {
                        // Native null-group refresh retains its previous interrupt flag.
                        if (_bindings.TryGetValue(enemy.runtimeId, out Binding previous) &&
                            ReferenceEquals(previous.Actor, enemy) && ReferenceEquals(previous.Definition, definition))
                            retained.Add(enemy.runtimeId);
                        continue;
                    }
                    if (!definition.Groups[group].TriggersInterrupt) continue;
                    retained.Add(enemy.runtimeId);
                    var binding = new Binding(enemy, definition, group);
                    if (!_bindings.TryGetValue(enemy.runtimeId, out Binding old) ||
                        !ReferenceEquals(old.Actor, enemy) || !ReferenceEquals(old.Definition, definition))
                        _counters.Register(enemy.runtimeId, definition.Groups[group].TurnCountdown);
                    else if (old.Group != group)
                        _counters.Update(enemy.runtimeId, definition.Groups[group].TurnCountdown, true);
                    _bindings[enemy.runtimeId] = binding;
                }
            foreach (int runtimeId in new List<int>(_bindings.Keys))
                if (!retained.Contains(runtimeId))
                {
                    _bindings.Remove(runtimeId);
                    _counters.Deregister(runtimeId);
                }
        }

        internal void Advance(EnemyInterruptAdvanceCause cause, Action<EnemyInterruptTrigger> onZero)
        {
            Synchronize();
            _counters.Advance(cause, onZero);
        }

        internal void Clear()
        {
            _bindings.Clear();
            _counters.Clear();
        }
    }
}
