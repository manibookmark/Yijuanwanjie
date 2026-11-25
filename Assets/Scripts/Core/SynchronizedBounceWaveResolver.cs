using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 结算“攻击物同步推进”的弹跳序列。模块同时拥有波次时序、全局列游标和
    /// 每个攻击物的上一目标，避免预览、实战或单卡代码各自推导同一套规则。
    /// </summary>
    internal static class SynchronizedBounceWaveResolver
    {
        public static bool IsRequired(AttackPlan plan)
        {
            if (plan == null) return false;
            for (int i = 0; i < plan.SubHits.Count; i++)
            {
                ResolvedBounceSpec bounce = plan.SubHits[i]?.Bounce;
                if (bounce != null && bounce.enabled &&
                    bounce.sequenceMode ==
                    BounceSequenceMode.SynchronizedWavesByAttackInstanceCount)
                {
                    return true;
                }
            }
            return false;
        }

        public static AttackResolutionResult Execute(
            AttackPlan plan,
            IList<EnemyStats> enemies,
            BossOverkillSession bossOverkill)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            ValidatePlan(plan);

            var result = new AttackResolutionResult
            {
                HitCount = plan.SubHits.Count,
                HitDamage = plan.SubHits.Count > 0
                    ? plan.SubHits[0].NormalHitDamage
                    : 0,
                DisplayedCardDamage = plan.DisplayedCardDamage
            };
            if (plan.SubHits.Count == 0) return result;

            int bounceWaveCount = plan.SubHits[0].Bounce.hitsPerDirectHit;
            var lastTargetKeys = new int[plan.SubHits.Count];
            var lastTargetColumns = new int[plan.SubHits.Count];
            for (int i = 0; i < lastTargetKeys.Length; i++)
            {
                lastTargetKeys[i] = int.MinValue;
                lastTargetColumns[i] = int.MinValue;
            }

            var selector = new FrozenWaveColumnSelector(
                IsRightToLeft(plan.SubHits[0].TargetPolicy),
                bossOverkill);
            for (int wave = 0; wave <= bounceWaveCount; wave++)
            {
                int attackableCount =
                    AttackResolver.GetResolutionTargets(
                        enemies,
                        bossOverkill).Count;
                // Direct 轮允许所有攻击物集中命中单体；弹跳轮开始时若不足两个
                // 可攻击目标，则整条弹跳序列结束。
                if (attackableCount == 0 ||
                    (wave > 0 && attackableCount < 2)) break;

                selector.BeginWave(
                    enemies,
                    wave > 0 ? lastTargetColumns[0] : int.MinValue);

                for (int projectile = 0;
                     projectile < plan.SubHits.Count;
                     projectile++)
                {
                    AttackSubHit subHit = plan.SubHits[projectile];
                    int target = selector.SelectNext(
                        enemies,
                        out int targetColumn);
                    if (target < 0) continue;

                    int targetKey = enemies[target].runtimeId;
                    // 列槽仍会被消费，但弹跳不能返回该攻击物刚命中的同一敌人。
                    // 目标在本轮中途死亡且该列没有后排时，这会自然产生一次落空。
                    if (wave > 0 && targetKey == lastTargetKeys[projectile])
                        continue;
                    DamageKind kind = wave == 0
                        ? DamageKind.Direct
                        : DamageKind.Bounce;
                    EnemyDamageReceipt damageReceipt = AttackResolver.ApplyDamage(
                        enemies,
                        target,
                        plan.SourceCardInstanceId,
                        subHit.AttackInstanceIndex,
                        subHit.SubHitIndex,
                        kind,
                        subHit.NormalHitDamage +
                        subHit.Bounce.damageIncrementPerBounce * wave,
                        false,
                        canTriggerArea: false,
                        canTriggerSecondaryEffects: wave == 0 && plan.CanTriggerSecondaryEffects);
                    var batchDefeats = new List<EnemyDefeatResult>();
                    result.AddDamage(damageReceipt, batchDefeats);
                    lastTargetKeys[projectile] = targetKey;
                    lastTargetColumns[projectile] = targetColumn;

                    // 每一发都是一个完整攻击物；死亡后立即刷新列前目标，
                    // 并保持与通用结算相同的余威激活时机。
                    bossOverkill?.TryBeginAfterDamageBatch(batchDefeats);
                }
            }

            return result;
        }

        private static void ValidatePlan(AttackPlan plan)
        {
            int bounceWaveCount = -1;
            bool? rightToLeft = null;
            var attackInstances = new HashSet<int>();
            for (int i = 0; i < plan.SubHits.Count; i++)
            {
                AttackSubHit subHit = plan.SubHits[i];
                ResolvedBounceSpec bounce = subHit?.Bounce;
                if (bounce == null || !bounce.enabled ||
                    bounce.sequenceMode !=
                    BounceSequenceMode.SynchronizedWavesByAttackInstanceCount)
                {
                    throw new InvalidOperationException(
                        "同步弹跳计划不能混用其他弹跳时序。");
                }
                if (subHit.DirectTargetCount != 1)
                    throw new InvalidOperationException(
                        "同步弹跳目前要求每个攻击物恰好一个 Direct 目标。");
                if (!attackInstances.Add(subHit.AttackInstanceIndex))
                    throw new InvalidOperationException(
                        "同步弹跳目前要求每个攻击物只有一个 SubHit。");
                if (!SupportsPolicy(subHit.TargetPolicy))
                    throw new InvalidOperationException(
                        $"同步弹跳不支持目标策略 {subHit.TargetPolicy}。");
                bool subHitRightToLeft = IsRightToLeft(subHit.TargetPolicy);
                if (!rightToLeft.HasValue)
                    rightToLeft = subHitRightToLeft;
                else if (rightToLeft.Value != subHitRightToLeft)
                    throw new InvalidOperationException(
                        "同步弹跳的所有攻击物必须使用同一目标方向。");
                if ((subHit.Crit != null && subHit.Crit.enabled) ||
                    (subHit.Area != null && subHit.Area.enabled &&
                     subHit.Area.triggerMode != AreaTriggerMode.Disabled) ||
                    (subHit.TerrainEffect != null &&
                     !string.IsNullOrWhiteSpace(
                         subHit.TerrainEffect.effectId)) ||
                    (subHit.OnHitEffects != null &&
                     subHit.OnHitEffects.Count > 0))
                {
                    throw new InvalidOperationException(
                        "同步弹跳不能与暴击、范围、地形或逐 Hit 控制效果混用。" +
                        $" subHit={i}, " +
                        $"crit={subHit.Crit?.enabled == true}, " +
                        $"area={subHit.Area?.enabled == true && subHit.Area.triggerMode != AreaTriggerMode.Disabled}, " +
                        $"terrain={subHit.TerrainEffect?.effectId ?? "<none>"}, " +
                        $"onHitCount={subHit.OnHitEffects?.Count ?? 0}。");
                }

                if (bounceWaveCount < 0)
                    bounceWaveCount = bounce.hitsPerDirectHit;
                else if (bounceWaveCount != bounce.hitsPerDirectHit)
                    throw new InvalidOperationException(
                        "同步弹跳的所有攻击物必须拥有相同波次数。");
            }
        }

        private static bool SupportsPolicy(TargetPolicy policy)
            => policy == TargetPolicy.SequentialRightToLeft ||
                policy == TargetPolicy.PlayedTargetThenSequential ||
                policy == TargetPolicy.LeftToRight ||
                policy == TargetPolicy.MultiTarget;

        private static bool IsRightToLeft(TargetPolicy policy)
            => policy == TargetPolicy.SequentialRightToLeft ||
                policy == TargetPolicy.PlayedTargetThenSequential;

        /// <summary>
        /// 一次出牌共用的空间游标。每轮开始时冻结当轮可攻击列槽；每个槽命中时
        /// 再读取该列当前最前目标，因此前排死亡后同列后排能立即接棒，而没有
        /// 后排的空列不会把本轮剩余命中挤到其他列。
        /// </summary>
        private sealed class FrozenWaveColumnSelector
        {
            private readonly bool _rightToLeft;
            private readonly BossOverkillSession _bossOverkill;
            private readonly List<int> _waveColumns = new List<int>();
            private int _leftCursor;
            private int _rightCursor = EnemyFormationLayout.Columns - 1;
            private int _nextColumnIndex;

            public FrozenWaveColumnSelector(
                bool rightToLeft,
                BossOverkillSession bossOverkill)
            {
                _rightToLeft = rightToLeft;
                _bossOverkill = bossOverkill;
            }

            public void BeginWave(
                IList<EnemyStats> enemies,
                int firstProjectilePreviousColumn)
            {
                List<int> candidates = AttackResolver.GetResolutionTargets(
                    enemies,
                    _bossOverkill);
                _waveColumns.Clear();
                for (int i = 0; i < candidates.Count; i++)
                {
                    int column = StartColumn(enemies, candidates[i]);
                    if (!_waveColumns.Contains(column))
                        _waveColumns.Add(column);
                }
                _waveColumns.Sort();
                _nextColumnIndex = FindStartingColumnIndex();

                if (firstProjectilePreviousColumn == int.MinValue ||
                    _waveColumns.Count < 2)
                {
                    return;
                }

                if (_waveColumns[_nextColumnIndex] ==
                    firstProjectilePreviousColumn)
                {
                    _nextColumnIndex = AdvanceIndex(_nextColumnIndex);
                }
            }

            public int SelectNext(
                IList<EnemyStats> enemies,
                out int column)
            {
                column = -1;
                if (_waveColumns.Count == 0) return -1;
                column = _waveColumns[_nextColumnIndex];
                _nextColumnIndex = AdvanceIndex(_nextColumnIndex);
                if (_rightToLeft)
                    _rightCursor = PositiveModulo(
                        column - 1,
                        EnemyFormationLayout.Columns);
                else
                    _leftCursor = PositiveModulo(
                        column + 1,
                        EnemyFormationLayout.Columns);
                return ResolveCurrentTarget(enemies, column);
            }

            private int FindStartingColumnIndex()
            {
                if (_rightToLeft)
                {
                    for (int i = _waveColumns.Count - 1; i >= 0; i--)
                        if (_waveColumns[i] <= _rightCursor) return i;
                    return _waveColumns.Count - 1;
                }

                for (int i = 0; i < _waveColumns.Count; i++)
                    if (_waveColumns[i] >= _leftCursor) return i;
                return 0;
            }

            private int AdvanceIndex(int index)
            {
                if (_rightToLeft)
                    return PositiveModulo(index - 1, _waveColumns.Count);
                return (index + 1) % _waveColumns.Count;
            }

            private int ResolveCurrentTarget(
                IList<EnemyStats> enemies,
                int column)
            {
                List<int> attackable = AttackResolver.GetResolutionTargets(
                    enemies,
                    _bossOverkill);
                for (int i = 0; i < attackable.Count; i++)
                {
                    int target = attackable[i];
                    if (StartColumn(enemies, target) <= column &&
                        EndColumn(enemies, target) >= column)
                    {
                        return target;
                    }
                }
                return -1;
            }

            private static int StartColumn(
                IList<EnemyStats> enemies,
                int index)
            {
                EnemyStats enemy = enemies[index];
                return enemy.column >= 0
                    ? enemy.column
                    : index % EnemyFormationLayout.Columns;
            }

            private static int EndColumn(
                IList<EnemyStats> enemies,
                int index)
                => StartColumn(enemies, index) +
                    Math.Max(1, enemies[index].GridWidth) - 1;

            private static int PositiveModulo(int value, int modulus)
                => (value % modulus + modulus) % modulus;
        }
    }
}
