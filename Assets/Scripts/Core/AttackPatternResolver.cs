using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 攻击模式解析器：根据卡牌的 AttackPattern 分配投射物目标
    /// 通用封装，CombatController 和 VFXTest 都调这个
    /// </summary>
    public static class AttackPatternResolver
    {
        /// <summary>
        /// 根据攻击模式分配目标索引数组
        /// </summary>
        /// <param name="pattern">攻击模式</param>
        /// <param name="primaryTarget">主目标索引</param>
        /// <param name="projectileCount">投射物数量</param>
        /// <param name="enemyCount">敌人总数</param>
        /// <returns>每发投射物的目标索引数组</returns>
        public static int[] ResolveTargets(AttackPattern pattern, int primaryTarget, int projectileCount, int enemyCount)
        {
            if (enemyCount <= 0) return System.Array.Empty<int>();
            projectileCount = System.Math.Max(1, projectileCount);
            primaryTarget = ((primaryTarget % enemyCount) + enemyCount) % enemyCount; // 安全取模

            switch (pattern)
            {
                case AttackPattern.None:
                    return System.Array.Empty<int>();

                case AttackPattern.SingleShot:
                    // 单发：只打主目标
                    return new int[] { primaryTarget };

                case AttackPattern.MultiShot:
                    // 多发：从主目标开始，从左往右循环分配
                    // 例：3怪5发，主目标=0 → [0,1,2,0,1]
                    return CycleTargets(primaryTarget, projectileCount, enemyCount);

                case AttackPattern.Arc:
                    // 弧线：全部打主目标（斧头类，多发打同一个）
                    var arcTargets = new int[projectileCount];
                    for (int i = 0; i < projectileCount; i++)
                        arcTargets[i] = primaryTarget;
                    return arcTargets;

                case AttackPattern.WhipSlash:
                    // 横扫：从左到右每个敌人打1发
                    return AllEnemies(enemyCount);

                case AttackPattern.Explosion:
                    // 爆炸：全部打主目标（爆炸点为中心）
                    var explodeTargets = new int[projectileCount];
                    for (int i = 0; i < projectileCount; i++)
                        explodeTargets[i] = primaryTarget;
                    return explodeTargets;

                case AttackPattern.MultiTarget:
                    // 多目标：每个敌人打1发
                    return AllEnemies(enemyCount);

                default:
                    return new int[] { primaryTarget };
            }
        }

        /// <summary>
        /// 从主目标开始循环分配：0→1→2→0→1...
        /// </summary>
        private static int[] CycleTargets(int startTarget, int count, int enemyCount)
        {
            var targets = new int[count];
            for (int i = 0; i < count; i++)
                targets[i] = (startTarget + i) % enemyCount;
            return targets;
        }

        /// <summary>
        /// 所有敌人各打1发
        /// </summary>
        private static int[] AllEnemies(int enemyCount)
        {
            var targets = new int[enemyCount];
            for (int i = 0; i < enemyCount; i++)
                targets[i] = i;
            return targets;
        }
    }
}
