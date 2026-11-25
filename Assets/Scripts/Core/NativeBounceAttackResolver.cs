using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns native per-projectile bounce execution: one card-wide column cursor,
    /// only the projectile's previous enemy excluded, and hit/Area/death ordering.
    /// Receipts describe actual hits; neither preview nor presentation recomputes them.
    /// </summary>
    internal static class NativeBounceAttackResolver
    {
        public static bool IsRequired(AttackPlan plan)
        {
            if (plan == null) return false;
            for (int i = 0; i < plan.SubHits.Count; i++)
            {
                ResolvedBounceSpec bounce = plan.SubHits[i]?.Bounce;
                if (bounce?.enabled == true &&
                    bounce.sequenceMode == BounceSequenceMode.NativePerAttackInstance)
                    return true;
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
                HitDamage = plan.SubHits.Count > 0 ? plan.SubHits[0].NormalHitDamage : 0,
                DisplayedCardDamage = plan.DisplayedCardDamage
            };
            if (plan.SubHits.Count == 0) return result;

            var selector = new ColumnCursor(
                plan.SubHits[0].TargetPolicy == TargetPolicy.SequentialRightToLeft);
            for (int projectile = 0; projectile < plan.SubHits.Count; projectile++)
            {
                AttackSubHit hit = plan.SubHits[projectile];
                int previousRuntimeId = 0;
                // The builder resolves P independently of the projectile count:
                // P = base Amount + player Amount; Combo and LB only add projectiles.
                long hitBudget = 1L + hit.Bounce.hitsPerDirectHit;
                for (int ordinal = 0; ordinal < hitBudget; ordinal++)
                {
                    // Capture fronts before damage. A direct kill exposes its rear
                    // only to the next hit, never to this hit's own Area batch.
                    List<int> fronts = AttackResolver.GetResolutionTargets(enemies, bossOverkill);
                    int target = selector.SelectNext(
                        enemies, fronts, previousRuntimeId, out int column);
                    if (target < 0) break;

                    previousRuntimeId = enemies[target].runtimeId;
                    DamageKind kind = ordinal == 0 ? DamageKind.Direct : DamageKind.Bounce;
                    bool canTriggerArea = hit.Area?.enabled == true &&
                        hit.Area.triggerMode == AreaTriggerMode.PerDirectTarget;
                    if (kind == DamageKind.Direct) result.RecordDirectTargetColumn(column);

                    var defeats = new List<EnemyDefeatResult>();
                    result.AddDamage(AttackResolver.ApplyDamage(
                        enemies, target, plan.SourceCardInstanceId,
                        hit.AttackInstanceIndex, hit.SubHitIndex, kind,
                        hit.NormalHitDamage, false,
                        canTriggerArea: canTriggerArea,
                        canTriggerSecondaryEffects: kind == DamageKind.Direct &&
                            plan.CanTriggerSecondaryEffects), defeats);

                    if (canTriggerArea)
                        ResolveArea(plan, hit, enemies, fronts, result, defeats);

                    // Preserve the shared project's integer HP and Boss overkill
                    // lifecycle. Only this complete hit + Area batch may enable it.
                    bossOverkill?.TryBeginAfterDamageBatch(defeats);
                }
            }
            return result;
        }

        private static void ResolveArea(
            AttackPlan plan,
            AttackSubHit hit,
            IList<EnemyStats> enemies,
            IReadOnlyList<int> capturedFronts,
            AttackResolutionResult result,
            List<EnemyDefeatResult> defeats)
        {
            int splash = DamageSystem.CalculateSplashDamage(
                hit.UnroundedHitDamage, plan.AreaStat, hit.Area.coefficient);
            if (splash <= 0) return;
            for (int i = 0; i < capturedFronts.Count; i++)
            {
                int target = capturedFronts[i];
                EnemyStats enemy = enemies[target];
                // A surviving direct target remains in Area. A dead target,
                // including the active Boss corpse, does not receive Area.
                if (enemy == null || enemy.IsDead || !enemy.IsDeployed || enemy.IsWithdrawn)
                    continue;
                result.AddDamage(AttackResolver.ApplyDamage(
                    enemies, target, plan.SourceCardInstanceId,
                    hit.AttackInstanceIndex, hit.SubHitIndex,
                    DamageKind.Splash, splash, false), defeats);
            }
        }

        private static void ValidatePlan(AttackPlan plan)
        {
            TargetPolicy? policy = null;
            int? bounceCount = null;
            var attackInstances = new HashSet<int>();
            foreach (AttackSubHit hit in plan.SubHits)
            {
                if (hit?.Bounce?.enabled != true ||
                    hit.Bounce.sequenceMode != BounceSequenceMode.NativePerAttackInstance)
                    throw new InvalidOperationException("原生弹跳计划不能混用其它弹跳时序。");
                if (hit.DirectTargetCount != 1 || hit.TargetColumn >= 0 ||
                    !attackInstances.Add(hit.AttackInstanceIndex))
                    throw new InvalidOperationException("原生弹跳要求每个攻击物只有一个自动选敌 SubHit。");
                if (hit.TargetPolicy != TargetPolicy.SequentialRightToLeft &&
                    hit.TargetPolicy != TargetPolicy.LeftToRight)
                    throw new InvalidOperationException("原生弹跳要求按列顺序选敌。");
                if (policy.HasValue && policy.Value != hit.TargetPolicy)
                    throw new InvalidOperationException("原生弹跳的所有攻击物必须使用相同列方向。");
                policy = hit.TargetPolicy;
                if (bounceCount.HasValue && bounceCount.Value != hit.Bounce.hitsPerDirectHit)
                    throw new InvalidOperationException("原生弹跳的所有攻击物必须使用相同命中预算。");
                bounceCount = hit.Bounce.hitsPerDirectHit;
                // Unity clones can materialize an unset inline terrain object.
                // Its stable effect ID, rather than object presence, enables terrain.
                if (hit.Bounce.damageIncrementPerBounce != 0 ||
                    hit.Crit?.enabled == true ||
                    !string.IsNullOrWhiteSpace(hit.TerrainEffect?.effectId) ||
                    hit.OnHitEffects.Count > 0)
                    throw new InvalidOperationException("原生弹跳不支持递增伤害、独立暴击、地形或控制附加效果。");
                if (hit.Area?.enabled == true &&
                    hit.Area.triggerMode != AreaTriggerMode.Disabled &&
                    hit.Area.triggerMode != AreaTriggerMode.PerDirectTarget)
                    throw new InvalidOperationException("原生弹跳的范围伤害必须随每次有效命中触发。");
            }
        }

        /// <summary>
        /// Advances across column slots for the whole card. Empty slots and the
        /// previous enemy consume no hit budget; a full scan without a new enemy
        /// ends only the current projectile. Wide bodies remain one enemy object.
        /// </summary>
        private sealed class ColumnCursor
        {
            private readonly int _step;
            private int _nextColumn;

            public ColumnCursor(bool rightToLeft)
            {
                _step = rightToLeft ? -1 : 1;
                _nextColumn = rightToLeft ? EnemyFormationLayout.Columns - 1 : 0;
            }

            public int SelectNext(
                IList<EnemyStats> enemies,
                IReadOnlyList<int> candidates,
                int previousRuntimeId,
                out int column)
            {
                column = -1;
                for (int examined = 0; examined < EnemyFormationLayout.Columns; examined++)
                {
                    int current = _nextColumn;
                    _nextColumn = (_nextColumn + _step + EnemyFormationLayout.Columns) %
                        EnemyFormationLayout.Columns;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        int index = candidates[i];
                        EnemyStats enemy = enemies[index];
                        int left = enemy.column >= 0
                            ? enemy.column : index % EnemyFormationLayout.Columns;
                        if (left > current || left + enemy.GridWidth <= current) continue;
                        // The sorted exposed-front list gives the nearest body
                        // at this column. Do not look through an excluded body.
                        if (enemy.runtimeId == previousRuntimeId) break;
                        column = current;
                        return index;
                    }
                }
                return -1;
            }
        }
    }
}
