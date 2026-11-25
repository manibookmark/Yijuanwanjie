using System;
using BigInteger = System.Numerics.BigInteger;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 经验来源决定哪些全局倍率参与结算。所有来源共享同一份 Run 级
    /// 定点余量，避免小额明悟收益在逐笔取整时丢失。
    /// </summary>
    public enum ExperienceSource
    {
        EnemyKill,
        DirectCard,
        Character,
        Skip,
        Debug
    }

    /// <summary>
    /// 显式经验表耗尽后的需求续算方式。PowerCurve 保持旧内容行为；
    /// LinearFromExplicitTail 从显式表最后一项开始按固定增量续算。
    /// </summary>
    public enum LevelExperienceContinuationMode
    {
        PowerCurve,
        LinearFromExplicitTail
    }

    /// <summary>
    /// 一次 Run 的升级经验规则。显式列表按当前等级索引（Lv.1 使用第 0 项）；
    /// 未覆盖的等级由 continuationMode 选择指数曲线或从显式尾项线性续算。
    /// </summary>
    [Serializable]
    public sealed class LevelExperienceRules
    {
        [Min(1)] public int baseRequirement = LevelSystem.GetRequiredXP(1);
        [Min(0.01f)] public float curveExponent = LevelSystem.XpCurveExponent;
        public LevelExperienceContinuationMode continuationMode =
            LevelExperienceContinuationMode.PowerCurve;
        [Min(1)] public int continuationIncrement = 1;
        public List<int> explicitRequirements = new List<int>();

        public int GetRequiredExperience(int currentLevel)
        {
            int level = Math.Max(1, currentLevel);
            int explicitIndex = level - 1;
            if (explicitRequirements != null &&
                explicitIndex < explicitRequirements.Count &&
                explicitRequirements[explicitIndex] > 0)
            {
                return explicitRequirements[explicitIndex];
            }

            if (continuationMode ==
                    LevelExperienceContinuationMode.LinearFromExplicitTail &&
                explicitRequirements != null &&
                explicitRequirements.Count > 0 &&
                level > explicitRequirements.Count &&
                explicitRequirements[explicitRequirements.Count - 1] > 0)
            {
                int tail = explicitRequirements[
                    explicitRequirements.Count - 1];
                long continuationSteps =
                    (long)level - explicitRequirements.Count;
                long continued = tail + continuationSteps *
                    Math.Max(1, continuationIncrement);
                return (int)Math.Min(int.MaxValue, continued);
            }

            double curved = Math.Max(1, baseRequirement) *
                            Math.Pow(level, Math.Max(0.01f, curveExponent));
            return Math.Max(1, (int)Math.Round(curved));
        }

        public LevelExperienceRules Clone()
        {
            return new LevelExperienceRules
            {
                baseRequirement = baseRequirement,
                curveExponent = curveExponent,
                continuationMode = continuationMode,
                continuationIncrement = continuationIncrement,
                explicitRequirements = explicitRequirements != null
                    ? new List<int>(explicitRequirements)
                    : new List<int>()
            };
        }
    }

    /// <summary>
    /// 跨战斗共享的等级与经验状态。它不包含 UI 或场景生命周期，关卡会话只需
    /// 持有一个实例并将其注入每场战斗。
    /// </summary>
    public sealed class CombatProgressionState
    {
        public const int ExperienceCarryScale = 10_000;

        private readonly LevelExperienceRules _rules;

        public int CurrentLevel { get; private set; }
        public int CurrentExperience { get; private set; }
        public int XpFractionCarryUnits { get; private set; }
        public LevelExperienceRules Rules => _rules;

        public CombatProgressionState(
            LevelExperienceRules rules = null,
            int currentLevel = 1,
            int currentExperience = 0,
            int xpFractionCarryUnits = 0)
        {
            _rules = (rules ?? new LevelExperienceRules()).Clone();
            CurrentLevel = Math.Max(1, currentLevel);
            CurrentExperience = Math.Max(0, currentExperience);
            XpFractionCarryUnits = NormalizeCarry(xpFractionCarryUnits);
        }

        public int GetRequiredExperience(int currentLevel)
            => _rules.GetRequiredExperience(currentLevel);

        public void Store(int currentLevel, int currentExperience)
        {
            CurrentLevel = Math.Max(1, currentLevel);
            CurrentExperience = Math.Max(0, currentExperience);
        }

        /// <summary>原子恢复等级、经验和共享定点余量；地图提交使用与战斗相同的成长状态。</summary>
        public void Restore(int currentLevel, int currentExperience, int carryUnits)
        {
            Store(currentLevel, currentExperience);
            XpFractionCarryUnits = NormalizeCarry(carryUnits);
        }

        /// <summary>
        /// 把一笔基础经验按来源策略、明悟与招煞统一换算为整数经验。
        /// 小于 1 点的部分保留在本状态中，并由后续任意经验来源继续累计。
        /// </summary>
        public int SettleExperience(
            int rawExperience,
            ExperienceSource source,
            int growthPercent,
            int cursePercent)
        {
            if (rawExperience <= 0)
                return 0;

            int growth = Math.Max(
                0,
                Math.Min(RunProgressionState.DefaultGrowthCap,
                    growthPercent));
            int curse = Math.Max(0, cursePercent);
            // Preserve each integer division and the shared carry exactly even
            // when full source Curse makes the intermediate larger than Int64.
            BigInteger scaledUnits = (BigInteger)rawExperience * ExperienceCarryScale;

            if (AppliesGrowth(source))
                scaledUnits = scaledUnits * (100L + growth) / 100L;
            if (AppliesCurse(source))
                scaledUnits = scaledUnits * (100L + curse) / 100L;

            BigInteger totalUnits = scaledUnits + XpFractionCarryUnits;
            BigInteger settled = totalUnits / ExperienceCarryScale;
            XpFractionCarryUnits = (int)(totalUnits % ExperienceCarryScale);
            return (int)BigInteger.Min(int.MaxValue, settled);
        }

        private static bool AppliesGrowth(ExperienceSource source)
        {
            return source != ExperienceSource.Debug;
        }

        private static bool AppliesCurse(ExperienceSource source)
        {
            return source == ExperienceSource.EnemyKill;
        }

        private static int NormalizeCarry(int carryUnits)
        {
            if (carryUnits <= 0)
                return 0;
            return carryUnits % ExperienceCarryScale;
        }
    }
}
