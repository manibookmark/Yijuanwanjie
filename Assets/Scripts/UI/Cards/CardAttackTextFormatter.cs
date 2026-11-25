using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Cards
{
    /// <summary>
    /// Explains detached host attack plans without calculating damage or targets.
    /// Attack-instance identity owns object counts; sub-hit sequences own per-hit
    /// wording. Different host stages and heterogeneous hits remain distinct.
    /// All grouping is local to one formatting call; no combat state is retained.
    /// </summary>
    internal static class CardAttackTextFormatter
    {
        internal static string Format(CardAttackDisplaySummary summary)
        {
            if (summary == null || summary.Plans.Count == 0) return null;
            var stages = new List<string>();
            for (int i = 0; i < summary.Plans.Count; i++)
            {
                string stage = FormatPlan(summary.Plans[i]);
                if (!string.IsNullOrEmpty(stage))
                    stages.Add(summary.Plans.Count > 1 ? $"第{i + 1}段：{stage}" : stage);
            }
            return stages.Count == 0 ? null :
                (summary.IsEstimate ? "≈" : string.Empty) + string.Join("；", stages);
        }

        private static string FormatPlan(AttackPlan plan)
        {
            if (plan == null || plan.SubHits.Count == 0) return null;
            var instances = new SortedDictionary<int, List<AttackSubHit>>();
            foreach (AttackSubHit hit in plan.SubHits)
            {
                if (!instances.TryGetValue(hit.AttackInstanceIndex, out List<AttackSubHit> hits))
                {
                    hits = new List<AttackSubHit>();
                    instances.Add(hit.AttackInstanceIndex, hits);
                }
                hits.Add(hit);
            }

            List<AttackSubHit> template = null;
            bool sameSequence = true;
            foreach (List<AttackSubHit> hits in instances.Values)
            {
                if (template == null) template = hits;
                else if (!SameSequence(template, hits)) sameSequence = false;
            }
            if (sameSequence)
            {
                if (plan.ResidentBirdCount > 0 && AllSameHit(template))
                    return $"{plan.ResidentBirdCount}只驻场单位，每只初击{template.Count}发，" +
                        $"每发造成{template[0].NormalHitDamage}伤害";
                if (template.Count == 1)
                    return FormatHits(template[0], instances.Count);
                if (AllSameHit(template) && template[0].DirectTargetCount == 1)
                    return $"攻击{instances.Count}次，每次连续命中{template.Count}次，" +
                        $"每次命中造成{template[0].NormalHitDamage}伤害";
                return $"{instances.Count}个攻击物，每个" + FormatSequence(template);
            }

            var objects = new List<string>();
            foreach (var instance in instances)
                objects.Add($"攻击物{instance.Key + 1}：{FormatSequence(instance.Value)}");
            return string.Join("；", objects);
        }

        private static bool SameSequence(List<AttackSubHit> left, List<AttackSubHit> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
                if (!SameHit(left[i], right[i])) return false;
            return true;
        }

        private static bool SameHit(AttackSubHit left, AttackSubHit right)
            => left.NormalHitDamage == right.NormalHitDamage &&
                left.TargetPolicy == right.TargetPolicy &&
                left.DirectTargetCount == right.DirectTargetCount &&
                left.TargetColumn == right.TargetColumn;

        private static bool AllSameHit(List<AttackSubHit> hits)
        {
            for (int i = 1; i < hits.Count; i++)
                if (!SameHit(hits[0], hits[i])) return false;
            return true;
        }

        private static string FormatSequence(List<AttackSubHit> hits)
        {
            var groups = new List<string>();
            for (int start = 0; start < hits.Count;)
            {
                int end = start + 1;
                while (end < hits.Count && SameHit(hits[start], hits[end])) end++;
                groups.Add(FormatHits(hits[start], end - start));
                start = end;
            }
            return string.Join("；随后", groups);
        }

        private static string FormatHits(AttackSubHit hit, int count)
        {
            string times = $"{count}次";
            int damage = hit.NormalHitDamage;
            if (hit.TargetPolicy == TargetPolicy.SequentialColumnSweep)
                return $"横扫{times}，每次最多命中{hit.DirectTargetCount}名敌人，" +
                    $"各造成{damage}伤害";
            if (hit.TargetPolicy == TargetPolicy.RandomColumnBagMultiHit)
                return $"攻击{times}，每次最多命中{hit.DirectTargetCount}次，" +
                    $"每次命中造成{damage}伤害；可重复攻击同一敌人";
            if (hit.TargetPolicy == TargetPolicy.ColumnFrontOnceLeftToRight)
            {
                string columns = hit.DirectTargetCount > 0 &&
                    hit.DirectTargetCount < EnemyFormationLayout.Columns
                        ? $"最多{hit.DirectTargetCount}列" : "各列";
                return $"巡游{times}，每次对{columns}前敌各造成{damage}伤害";
            }
            if (hit.DirectTargetCount <= 0 || hit.TargetPolicy == TargetPolicy.AllAttackable)
            {
                string targets = hit.TargetPolicy == TargetPolicy.FrozenNearestAttackableRow
                    ? "当前前排敌人" : "当前可攻击敌人";
                return $"攻击{times}，每次对{targets}各造成{damage}伤害";
            }
            if (hit.DirectTargetCount == 1)
                return $"攻击{times}，每次造成{damage}伤害";
            return $"攻击{times}，每次对最多{hit.DirectTargetCount}名敌人各造成{damage}伤害";
        }
    }
}
