using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 关 BOSS 从待命到部署的权威结果。runtimeId 是稳定身份，列表索引只供
    /// 同一场战斗内定位现有角色节点。
    /// </summary>
    public readonly struct LevelBossArrivalEvent
    {
        public int BossRuntimeId { get; }
        public int BossIndex { get; }

        public LevelBossArrivalEvent(int bossRuntimeId, int bossIndex)
        {
            BossRuntimeId = bossRuntimeId;
            BossIndex = bossIndex;
        }
    }

    /// <summary>
    /// 一场战斗中关卡 BOSS 部署资格的唯一所有者。
    ///
    /// 模块隐藏三条稳定规则：一场至多一个关 BOSS；只要任何其他
    /// 敌人（包括层 BOSS）存活，关 BOSS 就不属于战场；最后一名其他敌人的
    /// 完整结算结束后，关 BOSS 才一次性部署到前排中央。层 BOSS 不进入
    /// 本状态机，只由编队规则保证排在队尾。表现层只消费到达事件。
    /// </summary>
    public sealed class LevelBossEncounter
    {
        private readonly IList<EnemyStats> _enemies;
        private readonly int _bossIndex = -1;
        private bool _arrivalPublished;

        public LevelBossEncounter(IList<EnemyStats> enemies)
        {
            _enemies = enemies ??
                throw new ArgumentNullException(nameof(enemies));

            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyStats enemy = _enemies[i];
                if (enemy == null)
                    throw new ArgumentException(
                        $"敌人列表第 {i} 项为空。",
                        nameof(enemies));
                if (!enemy.isBoss || enemy.bossRank != BossRank.Stage)
                    continue;
                if (_bossIndex >= 0)
                    throw new InvalidOperationException(
                        "一场战斗只能配置一个关 BOSS。");
                _bossIndex = i;
            }

            if (_bossIndex < 0) return;

            EnemyStats boss = _enemies[_bossIndex];
            boss.justFilledFrontRow = false;
            boss.IsDeployed = true;
            if (HasLivingOtherEnemy())
                boss.IsDeployed = false;
        }

        public bool HasLevelBoss => _bossIndex >= 0;

        public bool IsWaiting =>
            _bossIndex >= 0 &&
            !_arrivalPublished &&
            !_enemies[_bossIndex].IsDead &&
            !_enemies[_bossIndex].IsWithdrawn &&
            !_enemies[_bossIndex].IsDeployed;

        public int BossIndex => _bossIndex;

        /// <summary>
        /// 在一次完整卡牌、状态或地形结算边界调用。条件未满足时无副作用；
        /// 条件首次满足时部署并返回一次事件，此后永远不会重复返回。
        /// </summary>
        public bool TryArrive(out LevelBossArrivalEvent arrival)
        {
            arrival = default;
            if (!IsWaiting || HasLivingOtherEnemy()) return false;

            EnemyStats boss = _enemies[_bossIndex];
            EnemyFormationLayout.DeployBossAtFrontCenter(
                _enemies,
                _bossIndex);
            boss.IsDeployed = true;
            boss.justFilledFrontRow = true;
            _arrivalPublished = true;
            arrival = new LevelBossArrivalEvent(
                boss.runtimeId,
                _bossIndex);
            return true;
        }

        private bool HasLivingOtherEnemy()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (i == _bossIndex) continue;
                EnemyStats enemy = _enemies[i];
                if (enemy != null &&
                    !enemy.IsDead &&
                    !enemy.IsWithdrawn)
                    return true;
            }
            return false;
        }
    }
}
