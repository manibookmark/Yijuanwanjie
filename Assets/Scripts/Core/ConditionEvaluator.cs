namespace WanjieScroll.Core
{
    /// <summary>What a condition list needs, without evaluating it or consulting a random source.</summary>
    public readonly struct ConditionEvaluationRequirements
    {
        internal ConditionEvaluationRequirements(bool requiresEvaluation, bool requiresRandom)
        {
            RequiresEvaluation = requiresEvaluation;
            RequiresRandom = requiresRandom;
        }
        public bool RequiresEvaluation { get; }
        public bool RequiresRandom { get; }
    }

    /// <summary>
    /// 条件评估器。检查 ConditionRule 是否满足。
    /// 纯函数，不修改任何状态。
    /// </summary>
    public static class ConditionEvaluator
    {
        /// <summary>Observation uses the same condition vocabulary without executing any prerequisite.</summary>
        public static ConditionEvaluationRequirements DescribeRequirements(
            System.Collections.Generic.IReadOnlyList<ConditionRule> conditions)
        {
            bool evaluation = false, random = false;
            if (conditions != null)
                foreach (ConditionRule condition in conditions)
                {
                    if (condition == null) continue;
                    switch (condition.type)
                    {
                        case ConditionTypeId.ComboAtLeast:
                        case ConditionTypeId.HealthBelow:
                        case ConditionTypeId.EnemyIsBoss:
                            evaluation = true;
                            break;
                        case ConditionTypeId.RandomChance:
                            evaluation = true; random = true;
                            break;
                    }
                }
            return new ConditionEvaluationRequirements(evaluation, random);
        }

        /// <summary>
        /// 检查单个条件是否满足。
        /// </summary>
        public static bool Check(ConditionRule condition, EffectResolveInput input)
        {
            if (condition == null) return true;
            if (input == null) return false;

            switch (condition.type)
            {
                case ConditionTypeId.None:
                    return true;

                case ConditionTypeId.ComboAtLeast:
                    return input.combo >= condition.intValue;

                case ConditionTypeId.HealthBelow:
                    return input.TargetHealthRatio < condition.floatValue;

                case ConditionTypeId.EnemyIsBoss:
                    return input.TargetIsBoss;

                case ConditionTypeId.RandomChance:
                    return input.random != null && input.random.Value01() < condition.floatValue;

                default:
                    return true;
            }
        }

        /// <summary>
        /// 检查条件列表是否全部满足。空列表视为无条件通过。
        /// </summary>
        public static bool CheckAll(System.Collections.Generic.List<ConditionRule> conditions, EffectResolveInput input)
        {
            if (conditions == null || conditions.Count == 0) return true;
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!Check(conditions[i], input))
                    return false;
            }
            return true;
        }
    }
}
