using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Erase 的权威整批结果，零 Damage；位置来自执行开始，死亡仍走统一经验链。</summary>
    public sealed class EraseResolutionResult
    {
        public int SourceCardInstanceId { get; }
        public IReadOnlyList<CombatEffectTarget> Targets { get; }
        public IReadOnlyList<EnemyDefeatResult> Defeats { get; }

        internal EraseResolutionResult(int source, List<CombatEffectTarget> targets,
            List<EnemyDefeatResult> defeats)
        {
            SourceCardInstanceId = source;
            Targets = targets.AsReadOnly();
            Defeats = defeats.AsReadOnly();
        }
    }

    /// <summary>
    /// Owns Erase selection and atomic batch execution for actual play and preview. Freeze the nearest
    /// attackable logical row before filtering Stage Bosses, so an immune front row never redirects behind it.
    /// Resolve every frozen identity before publishing results; a defeat callback cannot admit replacements.
    /// </summary>
    internal static class EraseResolver
    {
        internal static EraseResolutionResult Execute(IList<EnemyStats> enemies,
            TargetRule rule, int requestedIndex, int sourceCardInstanceId)
        {
            List<int> legal = AttackResolver.GetAttackableTargets(enemies);
            if (rule == TargetRule.NearestAttackableRow)
            {
                int row = int.MaxValue;
                foreach (int index in legal) row = Math.Min(row, enemies[index].row);
                legal.RemoveAll(index => enemies[index].row != row);
                legal.Sort((a, b) => {
                    int order = enemies[a].column.CompareTo(enemies[b].column);
                    return order != 0 ? order : a.CompareTo(b);
                });
            }
            else
            {
                // Legacy single-target callers already resolve the automatic fallback.
                legal.RemoveAll(index => index != requestedIndex);
            }
            var targets = new List<CombatEffectTarget>();
            var defeats = new List<EnemyDefeatResult>();
            var identities = new HashSet<int>();
            foreach (int index in legal)
            {
                EnemyStats enemy = enemies[index];
                if (!DefeatResolver.IsEraseImmune(enemy) && enemy.IsDeployed &&
                    !enemy.IsWithdrawn && identities.Add(enemy.runtimeId))
                    targets.Add(new CombatEffectTarget(enemy, index));
            }
            foreach (CombatEffectTarget target in targets)
            {
                EnemyDefeatResult defeat = DefeatResolver.ResolveErase(enemies, target.TargetIndex);
                if (!defeat.WasImmune) defeats.Add(defeat);
            }
            return new EraseResolutionResult(sourceCardInstanceId, targets, defeats);
        }
    }
}
