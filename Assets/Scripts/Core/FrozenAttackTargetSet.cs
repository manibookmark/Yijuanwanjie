using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// One attack execution's immutable target universe. Captures the project's
    /// exposed, deduplicated targets once; direct hits and Area share its stable
    /// identity and column order. Current eligibility can only remove members.
    /// </summary>
    internal sealed class FrozenAttackTargetSet
    {
        private readonly int[] _runtimeIds;

        internal int Count => _runtimeIds.Length;

        internal FrozenAttackTargetSet(IList<EnemyStats> enemies, BossOverkillSession bossOverkill)
        {
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            List<int> initial = AttackResolver.GetResolutionTargets(enemies, bossOverkill);
            initial.Sort((a, b) =>
            {
                int order = Column(enemies, a).CompareTo(Column(enemies, b));
                return order != 0 ? order : a.CompareTo(b);
            });
            _runtimeIds = new int[initial.Count];
            for (int i = 0; i < initial.Count; i++)
                _runtimeIds[i] = enemies[initial[i]].runtimeId;
        }

        internal List<int> SelectDirectTargets(IList<EnemyStats> enemies, BossOverkillSession bossOverkill)
            => OrderSubset(enemies, AttackResolver.GetResolutionTargets(enemies, bossOverkill));

        internal List<int> SelectAreaTargets(IList<EnemyStats> enemies)
            => OrderSubset(enemies, AttackResolver.GetAttackableTargets(enemies));

        private List<int> OrderSubset(IList<EnemyStats> enemies, List<int> eligible)
        {
            var byId = new Dictionary<int, int>();
            for (int i = 0; i < eligible.Count; i++)
                byId.Add(enemies[eligible[i]].runtimeId, eligible[i]);
            var result = new List<int>(_runtimeIds.Length);
            for (int i = 0; i < _runtimeIds.Length; i++)
                if (byId.TryGetValue(_runtimeIds[i], out int index))
                    result.Add(index);
            return result;
        }

        private static int Column(IList<EnemyStats> enemies, int index)
            => enemies[index].column >= 0 ? enemies[index].column : index % EnemyFormationLayout.Columns;
    }
}
