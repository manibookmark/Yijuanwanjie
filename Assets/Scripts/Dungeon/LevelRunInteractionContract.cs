using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 一项实测只能确认“在某套参考装载下达到某等级”时的作者合同。
    /// 它故意不保存推测出来的原始经验数值，也不把参考装载的显示名当作
    /// 运行时关联键。ReachLevel 只将当前等级推进到此结果，并由事务发出
    /// 对应目标等级的一次正常奖励选择。
    /// </summary>
    [Serializable]
    public sealed class LevelInteractionTargetLevelContract
    {
        [Tooltip("用于复核该观察结果的参考装载稳定 contentId；不使用显示名称关联。")]
        public string referenceLoadoutContentId;

        [Min(2), Tooltip("实测交互结算后必须达到的最低等级；不会降低更高等级的 Run。")]
        public int targetLevel;

        /// <summary>
        /// Whether this serialized shell actually carries an authored target.
        /// Empty shells are emitted by Unity for optional nested classes and
        /// must not be interpreted as an invalid contract on other effects.
        /// </summary>
        public bool IsSpecified =>
            !string.IsNullOrWhiteSpace(referenceLoadoutContentId) ||
            targetLevel != 0;

        internal void Validate(string effectId, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(referenceLoadoutContentId))
            {
                errors.Add(
                    $"目标等级交互 {effectId} 缺少参考装载 stable contentId。");
            }

            if (targetLevel < 2)
            {
                errors.Add(
                    $"目标等级交互 {effectId} 的 targetLevel 必须不小于 2。");
            }
        }
    }

    /// <summary>
    /// 只在某个历史地图节点允许的一条精炼分支。正式进化不需要副卡；这里仅
    /// 固定稳定配方、主卡和结果，避免“同一 recipe ID”意外放出其他分支。
    /// </summary>
    [Serializable]
    public sealed class LevelInteractionFixedEvolutionContract
    {
        [Tooltip("全局演化表中的稳定 recipe contentId。")]
        public string recipeContentId;

        [Tooltip("此节点允许的主卡稳定 contentId。")]
        public string primaryCardContentId;

        [Tooltip("旧存档兼容字段；正式进化与历史 RefineCard 均不再读取。")]
        public string secondaryCardContentId;

        [Tooltip("此节点允许的结果卡稳定 contentId。")]
        public string resultCardContentId;

        /// <summary>True only when Unity's serialized shell contains author data.</summary>
        public bool IsSpecified =>
            !string.IsNullOrWhiteSpace(recipeContentId) ||
            !string.IsNullOrWhiteSpace(primaryCardContentId) ||
            !string.IsNullOrWhiteSpace(resultCardContentId);

        internal void Validate(string effectId, List<string> errors)
        {
            ValidateId(recipeContentId, "recipeContentId", effectId, errors);
            ValidateId(primaryCardContentId, "primaryCardContentId", effectId, errors);
            ValidateId(resultCardContentId, "resultCardContentId", effectId, errors);
        }

        private static void ValidateId(
            string contentId,
            string field,
            string effectId,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(contentId))
            {
                errors.Add(
                    $"固定精炼交互 {effectId} 缺少 {field} stable contentId。");
            }
        }
    }

    /// <summary>
    /// 目标等级的最小规则边界。这里隐藏了“没有原始 XP 证据”的决定：
    /// 只前推等级，保留已有经验和定点余量，绝不伪造一笔经验来源。
    /// </summary>
    internal static class DungeonRunTargetLevelRules
    {
        internal static int ResolveReachedLevel(
            CombatProgressionState progression,
            LevelInteractionTargetLevelContract contract)
        {
            if (progression == null)
                throw new ArgumentNullException(nameof(progression));
            if (contract == null || contract.targetLevel < 2)
            {
                throw new InvalidOperationException(
                    "目标等级交互缺少合法的参考结果合同。");
            }

            return Math.Max(progression.CurrentLevel, contract.targetLevel);
        }
    }
}
