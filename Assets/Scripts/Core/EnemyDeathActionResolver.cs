using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>One independently rolled source death action. Only player damage is adopted;
    /// unsupported actions fail during content compilation instead of becoming a no-op.</summary>
    public readonly struct EnemyDeathActionDefinition
    {
        public EnemyDeathActionDefinition(float chancePercent, EnemyBehaviorAction action)
        {
            if (float.IsNaN(chancePercent) || float.IsInfinity(chancePercent) ||
                chancePercent < 0 || chancePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(chancePercent));
            if (action.IntentType != EnemyIntentType.Attack || action.Amount <= 0)
                throw new ArgumentException("Death actions currently require positive player damage.", nameof(action));
            ChancePercent = chancePercent;
            Action = action;
        }

        public float ChancePercent { get; }
        public EnemyBehaviorAction Action { get; }

        internal static IReadOnlyList<EnemyDeathActionDefinition> Capture(
            IReadOnlyList<EnemyDeathActionDefinition> actions)
        {
            if (actions == null || actions.Count == 0) return Array.Empty<EnemyDeathActionDefinition>();
            var copy = new EnemyDeathActionDefinition[actions.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = new EnemyDeathActionDefinition(actions[i].ChancePercent, actions[i].Action);
            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>Owns this encounter's consumed death-action identities. It accepts only an
    /// authoritative dead actor, rolls on the existing combat random source, and sends damage
    /// through the existing player receiver (shield, HP and revival). It owns no turn queue,
    /// damage ledger, visual lifetime, reward state or independent random stream.</summary>
    internal sealed class EnemyDeathActionResolver
    {
        private readonly CombatState _state;
        private readonly PlayerDamageReceiver _damage;
        private readonly HashSet<int> _consumed = new HashSet<int>();
        internal event Action<DamageResolution> DamageApplied;

        internal EnemyDeathActionResolver(CombatState state, PlayerDamageReceiver damage)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _damage = damage ?? throw new ArgumentNullException(nameof(damage));
        }

        internal int Resolve(EnemyDefeatResult defeat)
        {
            if (defeat.WasImmune) return 0;
            if (defeat.TargetIndex < 0 || defeat.TargetIndex >= _state.Enemies.Count)
                throw new InvalidOperationException("Death action receipt has no actual enemy slot.");
            EnemyStats enemy = _state.Enemies[defeat.TargetIndex];
            if (enemy == null || enemy.runtimeId <= 0 || enemy.runtimeId != defeat.TargetEnemyRuntimeId)
                throw new InvalidOperationException("Death action receipt and actual runtime actor disagree.");
            IReadOnlyList<EnemyDeathActionDefinition> actions = enemy.behaviorDefinition?.DeathActions;
            if (!enemy.IsDead || enemy.IsWithdrawn || actions == null || actions.Count == 0 ||
                !_consumed.Add(enemy.runtimeId)) return 0;

            int applied = 0;
            foreach (EnemyDeathActionDefinition action in actions)
            {
                if (_state.Player.currentHealth <= 0 || action.ChancePercent <= 0) continue;
                if (action.ChancePercent < 100 &&
                    (_state.Random ?? DefaultRandomSource.Instance).Value01() >= action.ChancePercent / 100f)
                    continue;
                DamageResolution damage = _damage.Apply(action.Action.Amount);
                applied++;
                DamageApplied?.Invoke(damage);
            }
            return applied;
        }
    }
}
