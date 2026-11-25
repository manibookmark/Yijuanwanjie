using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    [Serializable]
    public sealed class EnemyBehaviorActionData
    {
        public EnemyIntentType intentType;
        [Min(0)] public int amount = 1;
        [Min(1)] public int weight = 1;
        [Tooltip("仅健康组选招使用：有限非负 float 权重；0 保留来源的零权重合同。")]
        public float healthWeight = 1f;
        public bool canBeConsecutive = true;
        public bool prioritiseForStartPhase;
        public string description;
        [Tooltip("可选稳定招式 ID，例如 attack.yinjiangjun.camera_cross")]
        public string actionId;
        [Tooltip("仅 AddEncounterTemporaryCard 使用：要加入牌组的临时卡稳定 contentId。卡面效果仍由内容作者源定义。")]
        public string temporaryCardContentId;
        [Tooltip("仅 AddEncounterTemporaryCard 使用：旧值随机非顶插入抽牌堆；Hand 直接入手，不受标准抽牌数限制。")]
        public EncounterTemporaryCardPlacement temporaryCardPlacement =
            EncounterTemporaryCardPlacement.RandomNonTopDrawPile;
        [Min(0), Tooltip("仅 AddEncounterTemporaryCard 使用：同遭遇、同卡ID、同本行为Profile在所有牌区共享的残留上限；0 不限制（兼容旧配置）。G3显式填3。")]
        public int temporaryCardMaxRemaining;

        [Header("Compound action (optional)")]
        [Tooltip("仅 Compound 使用：主行动之外的第二项类型化行动。")]
        public EnemyIntentType compoundPrimaryIntentType = EnemyIntentType.Defend;
        public EnemyIntentType compoundIntentType = EnemyIntentType.None;
        [Min(0)] public int compoundAmount;
        public string compoundDescription;
        public string compoundActionId;
        public string compoundTemporaryCardContentId;
        public EncounterTemporaryCardPlacement compoundTemporaryCardPlacement =
            EncounterTemporaryCardPlacement.RandomNonTopDrawPile;
        [Min(0)] public int compoundTemporaryCardMaxRemaining;
    }

    [Serializable]
    public sealed class EnemyHealthBehaviorGroupData
    {
        [Range(0, 100)] public float requiredHealthPercent = 100f;
        [Tooltip("来源有符号中断计时；仅正值触发准入。完整动作队列调度尚待接入，正式G4配置仍须验收。")]
        public int turnCountdown = -1;
        public List<EnemyBehaviorActionData> actions =
            new List<EnemyBehaviorActionData>();
    }

    [Serializable]
    public sealed class EnemyDeathBehaviorActionData
    {
        [Range(0, 100)] public float chancePercent;
        public EnemyBehaviorActionData action;
    }

    /// <summary>
    /// 敌人的配置化行动资源。内容层拥有固定序列或加权选择数据，构建后
    /// 只向 Core 交付不依赖 Unity 资源的运行时定义。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewEnemyBehavior",
        menuName = "万界长卷/内容/敌人行为")]
    public sealed class EnemyBehaviorProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string contentId = "behavior.enemy.new.v1";
        public EnemyBehaviorSelectionMode selectionMode =
            EnemyBehaviorSelectionMode.FixedSequence;
        [Tooltip("普通怪／层Boss加权行为的相邻去重开关；关Boss由Core按BossRank.Stage统一启用，不依赖此值。")]
        public bool avoidImmediateRepeat;
        [FormerlySerializedAs("fixedSequence")]
        public List<EnemyBehaviorActionData> actions =
            new List<EnemyBehaviorActionData>();

        [Header("One-way phase switch (optional)")]
        [Tooltip("启用后，满足生命或合法行动数任一阈值时永久切到 phaseTwoActions。")]
        public bool hasPhaseSwitch;
        [Range(1, 99)] public int phaseHealthThresholdPercent = 50;
        [Min(1)] public int phaseActionCountThreshold = 7;
        public List<EnemyBehaviorActionData> phaseTwoActions =
            new List<EnemyBehaviorActionData>();

        [Header("Reversible health groups (explicit opt-in)")]
        public bool useHealthGroups;
        public List<EnemyHealthBehaviorGroupData> healthGroups =
            new List<EnemyHealthBehaviorGroupData>();

        [Header("Independent source death actions (optional)")]
        public List<EnemyDeathBehaviorActionData> deathActions =
            new List<EnemyDeathBehaviorActionData>();

        public bool TryBuildDefinition(
            out EnemyBehaviorDefinition definition,
            out string error)
        {
            definition = null;
            if (schemaVersion != CurrentSchemaVersion)
            {
                error = $"{contentId} schemaVersion 必须为 " +
                        $"{CurrentSchemaVersion}。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(contentId))
            {
                error = "敌人行为必须使用稳定 contentId。";
                return false;
            }
            if (!Enum.IsDefined(
                    typeof(EnemyBehaviorSelectionMode), selectionMode))
            {
                error = $"{contentId}.selectionMode 无效。";
                return false;
            }
            try
            {
                var deaths = new List<EnemyDeathActionDefinition>();
                foreach (EnemyDeathBehaviorActionData data in deathActions ?? new List<EnemyDeathBehaviorActionData>())
                {
                    if (data == null)
                        throw new ArgumentException("Death action cannot be null.");
                    if (!EnemyBehaviorActionCompiler.TryCompile(data.action,
                            contentId + ".deathActions[" + deaths.Count + "]", out var action, out error))
                        return false;
                    deaths.Add(new EnemyDeathActionDefinition(data.chancePercent, action));
                }
                return useHealthGroups
                    ? TryBuildHealthDefinition(deaths, out definition, out error)
                    : TryBuildLegacyDefinition(deaths, out definition, out error);
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private bool TryBuildLegacyDefinition(
            IReadOnlyList<EnemyDeathActionDefinition> deaths,
            out EnemyBehaviorDefinition definition,
            out string error)
        {
            definition = null;
            if (healthGroups != null && healthGroups.Count > 0)
            {
                error = $"{contentId}.healthGroups 必须显式启用 useHealthGroups。";
                return false;
            }
            if (actions == null || actions.Count == 0)
            {
                error = $"{contentId} 至少需要一项行动。";
                return false;
            }
            if (hasPhaseSwitch)
            {
                if (selectionMode != EnemyBehaviorSelectionMode.WeightedRandom)
                {
                    error = $"{contentId}.hasPhaseSwitch 只适用于 WeightedRandom。";
                    return false;
                }
                if (phaseHealthThresholdPercent < 1 ||
                    phaseHealthThresholdPercent > 99)
                {
                    error = $"{contentId}.phaseHealthThresholdPercent 必须在1到99之间。";
                    return false;
                }
                if (phaseActionCountThreshold <= 0)
                {
                    error = $"{contentId}.phaseActionCountThreshold 必须大于0。";
                    return false;
                }
                if (phaseTwoActions == null || phaseTwoActions.Count == 0)
                {
                    error = $"{contentId}.phaseTwoActions 至少需要一项行动。";
                    return false;
                }
            }
            if (avoidImmediateRepeat &&
                selectionMode != EnemyBehaviorSelectionMode.WeightedRandom)
            {
                error = $"{contentId}.avoidImmediateRepeat 只适用于加权随机行为。";
                return false;
            }
            if (!TryCompileLegacyActions(actions, "actions",
                    out EnemyBehaviorChoice[] choices, out error))
                return false;
            EnemyBehaviorChoice[] phaseChoices = null;
            if (hasPhaseSwitch &&
                !TryCompileLegacyActions(phaseTwoActions, "phaseTwoActions",
                    out phaseChoices, out error))
                return false;
            definition = new EnemyBehaviorDefinition(
                contentId,
                selectionMode,
                choices,
                avoidImmediateRepeat,
                phaseChoices,
                hasPhaseSwitch ? phaseHealthThresholdPercent : 0,
                hasPhaseSwitch ? phaseActionCountThreshold : 0,
                deaths);
            error = null;
            return true;
        }

        private bool TryCompileLegacyActions(
            IReadOnlyList<EnemyBehaviorActionData> authored,
            string listName,
            out EnemyBehaviorChoice[] choices,
            out string error)
        {
            choices = null;
            var compiled = new EnemyBehaviorChoice[authored.Count];
            long totalWeight = 0;
            for (int i = 0; i < authored.Count; i++)
            {
                EnemyBehaviorActionData data = authored[i];
                string sourcePath = $"{contentId}.{listName}[{i}]";
                if (!EnemyBehaviorActionCompiler.TryCompile(
                        data, sourcePath, out EnemyBehaviorAction action, out error))
                    return false;
                int weight = selectionMode == EnemyBehaviorSelectionMode.WeightedRandom
                    ? data.weight
                    : 1;
                if (weight <= 0)
                {
                    error = $"{sourcePath}.weight 必须大于 0。";
                    return false;
                }
                totalWeight += weight;
                if (totalWeight > int.MaxValue)
                {
                    error = $"{contentId} 的行为总权重不能超过 Int32.MaxValue。";
                    return false;
                }
                compiled[i] = new EnemyBehaviorChoice(action, weight);
            }
            choices = compiled;
            error = null;
            return true;
        }

        private bool TryBuildHealthDefinition(
            IReadOnlyList<EnemyDeathActionDefinition> deaths,
            out EnemyBehaviorDefinition definition,
            out string error)
        {
            definition = null;
            if (selectionMode != EnemyBehaviorSelectionMode.WeightedRandom)
            {
                error = $"{contentId}.useHealthGroups 只适用于 WeightedRandom。";
                return false;
            }
            if (hasPhaseSwitch || avoidImmediateRepeat)
            {
                error = $"{contentId}.useHealthGroups 不能同时启用 " +
                        "hasPhaseSwitch 或 avoidImmediateRepeat。";
                return false;
            }
            if ((actions != null && actions.Count > 0) ||
                (phaseTwoActions != null && phaseTwoActions.Count > 0))
            {
                error = $"{contentId}.useHealthGroups 要求 actions 和 " +
                        "phaseTwoActions 为空，避免混用两种行为数据。";
                return false;
            }
            if (healthGroups == null || healthGroups.Count == 0)
            {
                error = $"{contentId}.healthGroups 至少需要一个组。";
                return false;
            }
            var groups = new List<EnemyHealthBehaviorGroup>(healthGroups.Count);
            float previousThreshold = 100f;
            for (int i = 0; i < healthGroups.Count; i++)
            {
                EnemyHealthBehaviorGroupData group = healthGroups[i];
                string sourcePath = $"{contentId}.healthGroups[{i}]";
                if (group == null)
                {
                    error = $"{sourcePath} 不能为 null。";
                    return false;
                }
                float threshold = group.requiredHealthPercent;
                if (float.IsNaN(threshold) || float.IsInfinity(threshold) ||
                    threshold < 0f || threshold > 100f ||
                    (i == 0 && threshold != 100f) || threshold > previousThreshold)
                {
                    error = $"{sourcePath}.requiredHealthPercent 必须是0到100的有限值，" +
                            "首组必须为100，后续阈值不能递增。";
                    return false;
                }
                if (group.actions == null || group.actions.Count == 0)
                {
                    error = $"{sourcePath}.actions 至少需要一项行动。";
                    return false;
                }
                var choices = new List<EnemyHealthBehaviorChoice>(group.actions.Count);
                for (int j = 0; j < group.actions.Count; j++)
                {
                    EnemyBehaviorActionData data = group.actions[j];
                    string actionPath = $"{sourcePath}.actions[{j}]";
                    if (!EnemyBehaviorActionCompiler.TryCompile(
                            data, actionPath, out EnemyBehaviorAction action, out error))
                        return false;
                    if (float.IsNaN(data.healthWeight) ||
                        float.IsInfinity(data.healthWeight) || data.healthWeight < 0f)
                    {
                        error = $"{actionPath}.healthWeight 必须是有限非负数。";
                        return false;
                    }
                    choices.Add(new EnemyHealthBehaviorChoice(
                        action, data.healthWeight, data.canBeConsecutive,
                        data.prioritiseForStartPhase));
                }
                groups.Add(new EnemyHealthBehaviorGroup(threshold, choices, group.turnCountdown));
                previousThreshold = threshold;
            }
            definition = new EnemyBehaviorDefinition(
                new EnemyHealthBehaviorDefinition(contentId, groups), deaths);
            error = null;
            return true;
        }
    }
}
