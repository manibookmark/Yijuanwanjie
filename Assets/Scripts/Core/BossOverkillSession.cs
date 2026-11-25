using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 一场战斗中 Boss 余威资格和活动尸体的唯一状态所有者。
    ///
    /// 它隐藏四条不变量：普通敌人永不成为余威目标；Boss 必须在一个完整
    /// 伤害批次中被击败；所有其他敌人都已死亡或撤离；结束余威会原子清除
    /// 活动目标。AttackPlan 只描述卡牌攻击，不携带遭遇阶段开关。
    /// </summary>
    public sealed class BossOverkillSession
    {
        private readonly IList<EnemyStats> _enemies;
        private readonly bool _enabled;
        private int _activeTargetRuntimeId;

        public BossOverkillSession(
            IList<EnemyStats> enemies,
            bool enabled)
        {
            _enemies = enemies ??
                throw new ArgumentNullException(nameof(enemies));
            _enabled = enabled;
            ValidateRuntimeIds();
            End();
        }

        public bool IsActive =>
            TryResolveActiveTargetIndex(out _);

        internal int ActiveTargetIndex =>
            TryResolveActiveTargetIndex(out int index) ? index : -1;

        internal bool TryGetActiveTargetIndex(out int targetIndex)
            => TryResolveActiveTargetIndex(out targetIndex);

        internal bool IsActiveTarget(int targetIndex)
            => TryResolveActiveTargetIndex(out int activeIndex) &&
               targetIndex == activeIndex;

        /// <summary>
        /// 余威会话与创建它的敌人集合共同构成一个状态边界。
        /// 不允许解算器将一个遭遇的会话应用到另一个集合。
        /// </summary>
        internal void RequireRoster(IList<EnemyStats> enemies)
        {
            if (!ReferenceEquals(_enemies, enemies))
            {
                throw new ArgumentException(
                    "BossOverkillSession 必须与当前解算使用同一个敌人集合。",
                    nameof(enemies));
            }
        }

        public bool IsActiveTargetRuntimeId(int runtimeId)
            => runtimeId > 0 && IsActive &&
               runtimeId == _activeTargetRuntimeId;

        /// <summary>
        /// 在一个完整 SubHit、DoT tick 或地形伤害批次之后尝试开启余威。
        /// 参数只接收本次批次的新击败，不泄露外部累计列表或偏移布局。
        /// </summary>
        internal bool TryBeginAfterDamageBatch(
            IReadOnlyList<EnemyDefeatResult> defeats)
        {
            if (!_enabled || IsActive || defeats == null ||
                defeats.Count == 0 ||
                HasUnresolvedEnemy())
            {
                return false;
            }

            for (int i = defeats.Count - 1; i >= 0; i--)
            {
                if (!TryResolveEligibleDefeatedBoss(
                        defeats[i],
                        out int candidateIndex))
                {
                    continue;
                }
                SetActiveTarget(candidateIndex);
                return true;
            }
            return false;
        }

        internal bool TryBeginAfterDefeat(EnemyDefeatResult defeat)
            => TryBeginAfterDamageBatch(
                new[] { defeat });

        /// <summary>结束本场余威并清除所有兼容投影标记。</summary>
        internal void End()
        {
            _activeTargetRuntimeId = 0;
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null)
                    _enemies[i].isOverkillTarget = false;
            }
        }

        /// <summary>
        /// 为 Preview 的敌人克隆创建隔离状态机。规则开关与当前活动目标按
        /// runtimeId 投影，但之后的激活/结束只修改克隆，绝不触碰实战状态。
        /// </summary>
        internal BossOverkillSession CreatePreviewSession(
            IList<EnemyStats> clonedEnemies)
        {
            var preview = new BossOverkillSession(
                clonedEnemies,
                _enabled);
            if (IsActiveTargetRuntimeId(_activeTargetRuntimeId) &&
                preview.TryFindIndexByRuntimeId(
                    _activeTargetRuntimeId,
                    out int previewIndex))
            {
                preview.SetActiveTarget(previewIndex);
            }
            return preview;
        }

        private bool TryResolveEligibleDefeatedBoss(
            EnemyDefeatResult defeat,
            out int targetIndex)
        {
            targetIndex = -1;
            if (defeat.TargetEnemyRuntimeId <= 0 ||
                !TryFindIndexByRuntimeId(
                    defeat.TargetEnemyRuntimeId,
                    out targetIndex) ||
                defeat.TargetIndex != targetIndex)
            {
                return false;
            }
            EnemyStats enemy = _enemies[targetIndex];
            return enemy != null && enemy.isBoss && enemy.IsDeployed &&
                !enemy.IsWithdrawn && enemy.IsDead;
        }

        private bool HasUnresolvedEnemy()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyStats enemy = _enemies[i];
                if (enemy != null && !enemy.IsDead && !enemy.IsWithdrawn)
                    return true;
            }
            return false;
        }

        private void SetActiveTarget(int targetIndex)
        {
            End();
            _activeTargetRuntimeId = _enemies[targetIndex].runtimeId;
            _enemies[targetIndex].isOverkillTarget = true;
        }

        private bool TryResolveActiveTargetIndex(out int targetIndex)
        {
            targetIndex = -1;
            return _activeTargetRuntimeId > 0 &&
                   TryFindIndexByRuntimeId(
                       _activeTargetRuntimeId,
                       out targetIndex) &&
                   IsProjectedTarget(_enemies, targetIndex);
        }

        private bool TryFindIndexByRuntimeId(
            int runtimeId,
            out int targetIndex)
        {
            targetIndex = -1;
            if (runtimeId <= 0) return false;
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] == null ||
                    _enemies[i].runtimeId != runtimeId)
                {
                    continue;
                }
                targetIndex = i;
                return true;
            }
            return false;
        }

        private void ValidateRuntimeIds()
        {
            var ids = new HashSet<int>();
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyStats enemy = _enemies[i];
                if (enemy == null) continue;
                if (enemy.runtimeId <= 0 || !ids.Add(enemy.runtimeId))
                {
                    throw new ArgumentException(
                        "BossOverkillSession 要求每个敌人拥有单场唯一且大于 0 的 runtimeId。",
                        nameof(_enemies));
                }
            }
        }

        private static bool IsProjectedTarget(
            IList<EnemyStats> enemies,
            int targetIndex)
        {
            if (enemies == null || targetIndex < 0 ||
                targetIndex >= enemies.Count)
            {
                return false;
            }
            EnemyStats enemy = enemies[targetIndex];
            return enemy != null && enemy.isBoss && enemy.IsDeployed &&
                !enemy.IsWithdrawn && enemy.IsDead &&
                enemy.isOverkillTarget;
        }
    }
}
