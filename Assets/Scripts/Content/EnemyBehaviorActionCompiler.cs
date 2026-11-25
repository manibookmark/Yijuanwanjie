using System;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// Compiles the authored action payload shared by legacy sequences and
    /// health groups. Selection policy and weights belong to the profile;
    /// this boundary owns payload validation and preserves typed card data.
    /// </summary>
    internal static class EnemyBehaviorActionCompiler
    {
        internal static bool TryCompile(
            EnemyBehaviorActionData action,
            string sourcePath,
            out EnemyBehaviorAction compiled,
            out string error)
        {
            compiled = default;
            if (action == null)
            {
                error = $"{sourcePath} 不能为 null。";
                return false;
            }
            if (action.intentType != EnemyIntentType.Attack &&
                action.intentType != EnemyIntentType.Defend &&
                action.intentType != EnemyIntentType.Wait &&
                action.intentType != EnemyIntentType.Escape &&
                action.intentType !=
                    EnemyIntentType.AddEncounterTemporaryCard &&
                action.intentType != EnemyIntentType.Compound)
            {
                error = $"{sourcePath} 只支持 " +
                    "Attack、Defend、Wait、Escape、" +
                    "AddEncounterTemporaryCard 或 Compound。";
                return false;
            }
            bool requiresZeroAmount =
                action.intentType == EnemyIntentType.Wait ||
                action.intentType == EnemyIntentType.Escape;
            if (requiresZeroAmount &&
                action.amount != 0)
            {
                error = $"{sourcePath}.amount " +
                        $"在 {action.intentType} 行动中必须为 0。";
                return false;
            }
            if (!requiresZeroAmount &&
                action.amount <= 0)
            {
                error = $"{sourcePath}.amount 必须大于 0。";
                return false;
            }
            if (action.intentType ==
                EnemyIntentType.AddEncounterTemporaryCard)
            {
                if (string.IsNullOrWhiteSpace(
                        action.temporaryCardContentId))
                {
                    error = $"{sourcePath}.temporaryCardContentId " +
                        "不能为空。";
                    return false;
                }
                if (!Enum.IsDefined(
                        typeof(EncounterTemporaryCardPlacement),
                        action.temporaryCardPlacement))
                {
                    error = $"{sourcePath}.temporaryCardPlacement 无效。";
                    return false;
                }
                if (action.temporaryCardMaxRemaining < 0)
                {
                    error = $"{sourcePath}.temporaryCardMaxRemaining " +
                        "不能为负；0 表示不限制。";
                    return false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(
                         action.temporaryCardContentId) ||
                     action.temporaryCardMaxRemaining != 0)
            {
                error = $"{sourcePath}.temporaryCardContentId / " +
                    "temporaryCardMaxRemaining " +
                    "只适用于 AddEncounterTemporaryCard。";
                return false;
            }

            if (action.intentType == EnemyIntentType.Compound)
            {
                if (action.compoundPrimaryIntentType !=
                        EnemyIntentType.Attack &&
                    action.compoundPrimaryIntentType !=
                        EnemyIntentType.Defend &&
                    action.compoundPrimaryIntentType !=
                        EnemyIntentType.Wait)
                {
                    error = $"{sourcePath}.compoundPrimaryIntentType " +
                        "必须为 Attack、Defend 或 Wait。";
                    return false;
                }
                if (action.compoundIntentType !=
                        EnemyIntentType.Attack &&
                    action.compoundIntentType !=
                        EnemyIntentType.Defend &&
                    action.compoundIntentType !=
                        EnemyIntentType.Wait &&
                    action.compoundIntentType !=
                        EnemyIntentType.AddEncounterTemporaryCard)
                {
                    error = $"{sourcePath}.compoundIntentType " +
                        "必须为 Attack、Defend、Wait 或 " +
                        "AddEncounterTemporaryCard。";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(
                        action.compoundDescription))
                {
                    error = $"{sourcePath}.compoundDescription " +
                        "不能为空。";
                    return false;
                }
                if (action.compoundIntentType == EnemyIntentType.Wait &&
                    action.compoundAmount != 0)
                {
                    error = $"{sourcePath}.compoundAmount " +
                        "在 Wait 次行动中必须为0。";
                    return false;
                }
                if (action.compoundIntentType != EnemyIntentType.Wait &&
                    action.compoundAmount <= 0)
                {
                    error = $"{sourcePath}.compoundAmount " +
                        "必须大于0。";
                    return false;
                }
                if (action.compoundIntentType ==
                    EnemyIntentType.AddEncounterTemporaryCard)
                {
                    if (string.IsNullOrWhiteSpace(
                            action.compoundTemporaryCardContentId))
                    {
                        error = $"{sourcePath}." +
                            "compoundTemporaryCardContentId 不能为空。";
                        return false;
                    }
                    if (!Enum.IsDefined(
                            typeof(EncounterTemporaryCardPlacement),
                            action.compoundTemporaryCardPlacement))
                    {
                        error = $"{sourcePath}." +
                            "compoundTemporaryCardPlacement 无效。";
                        return false;
                    }
                    if (action.compoundTemporaryCardMaxRemaining < 0)
                    {
                        error = $"{sourcePath}." +
                            "compoundTemporaryCardMaxRemaining 不能为负。";
                        return false;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(
                             action.compoundTemporaryCardContentId) ||
                         action.compoundTemporaryCardMaxRemaining != 0)
                {
                    error = $"{sourcePath} 复合临时卡载荷只适用于 " +
                        "AddEncounterTemporaryCard 次行动。";
                    return false;
                }
            }
            else if (action.compoundPrimaryIntentType !=
                        EnemyIntentType.Defend ||
                     action.compoundIntentType != EnemyIntentType.None ||
                     action.compoundAmount != 0 ||
                     !string.IsNullOrWhiteSpace(
                         action.compoundDescription) ||
                     !string.IsNullOrWhiteSpace(action.compoundActionId) ||
                     !string.IsNullOrWhiteSpace(
                         action.compoundTemporaryCardContentId) ||
                     action.compoundTemporaryCardMaxRemaining != 0)
            {
                error = $"{sourcePath} 复合载荷只适用于 Compound。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(action.description))
            {
                error = $"{sourcePath}.description " +
                        "不能为空。";
                return false;
            }

            try
            {
                compiled = new EnemyBehaviorAction(
                    action.intentType,
                    action.amount,
                    action.description,
                    action.actionId,
                    action.temporaryCardContentId,
                    action.temporaryCardPlacement,
                    temporaryCardMaxRemaining: action.temporaryCardMaxRemaining,
                    compound: action.intentType == EnemyIntentType.Compound
                        ? new EnemyBehaviorCompound(
                            action.compoundPrimaryIntentType,
                            action.compoundIntentType,
                            action.compoundAmount,
                            action.compoundDescription,
                            action.compoundActionId,
                            action.compoundTemporaryCardContentId,
                            action.compoundTemporaryCardPlacement,
                            null,
                            action.compoundTemporaryCardMaxRemaining)
                        : default);
            }
            catch (ArgumentException exception)
            {
                error = $"{sourcePath}: {exception.Message}";
                return false;
            }
            error = null;
            return true;
        }
    }
}
