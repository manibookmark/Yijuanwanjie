using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Content
{
    /// <summary>
    /// Owns the one production ordinary-reward rule and its authoring boundary.
    /// Missing or ambiguous configuration never falls back to a floor rule. Legacy
    /// membership is exposed separately for frozen-checkpoint compatibility only.
    /// </summary>
    public static class GlobalLevelUpRewardRuleProfileCatalog
    {
        public const string ContentId = "reward.level-up.global.v1";
        private const string ResourceDirectory = "Content/LevelUpRewards";

        public static LevelUpRewardRuleProfile GetRequired()
            => ResolveRequired(Resources.LoadAll<LevelUpRewardRuleProfile>(ResourceDirectory));

        public static LevelUpRewardRuleProfile ResolveRequired(
            IEnumerable<LevelUpRewardRuleProfile> profiles)
        {
            List<LevelUpRewardRuleProfile> snapshot = Snapshot(profiles);
            List<string> errors = Validate(snapshot);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            foreach (LevelUpRewardRuleProfile profile in snapshot)
                if (profile != null && string.Equals(profile.contentId, ContentId, StringComparison.Ordinal))
                    return profile;
            throw new InvalidOperationException("全局升级奖励规则缺失。");
        }

        /// <summary>Pure validation; retained legacy assets are not production alternatives.</summary>
        public static List<string> Validate(IEnumerable<LevelUpRewardRuleProfile> profiles)
        {
            var errors = new List<string>();
            int count = 0;
            foreach (LevelUpRewardRuleProfile profile in profiles ?? Array.Empty<LevelUpRewardRuleProfile>())
            {
                if (profile == null) continue;
                if (!string.Equals(profile.contentId, ContentId, StringComparison.Ordinal))
                {
                    if (profile.useGlobalOrdinaryCatalog)
                        errors.Add($"全局普通奖励只能使用唯一稳定ID {ContentId}，当前为{profile.contentId}。");
                    continue;
                }
                count++;
                if (!profile.useGlobalOrdinaryCatalog)
                    errors.Add("全局升级奖励规则必须启用useGlobalOrdinaryCatalog。");
                foreach (string error in profile.ValidateProfile())
                    errors.Add($"{ContentId}：{error}");
            }
            if (count == 0)
                errors.Add($"缺少唯一全局升级奖励规则：{ContentId}。");
            else if (count > 1)
                errors.Add($"全局升级奖励规则contentId重复：{ContentId}。");
            return errors;
        }

        /// <summary>An empty obsolete selector also uses the mandatory global rule.</summary>
        public static List<string> ValidateLevelRuleReference(string contentId)
        {
            var errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(contentId) &&
                !string.Equals(contentId, ContentId, StringComparison.Ordinal))
                errors.Add($"正式关卡不得选择局部普通升级规则{contentId}；普通奖励统一使用{ContentId}。");
            return errors;
        }

        public static IReadOnlyList<string> GetLegacyFrozenCardContentIds()
            => GetLegacyFrozenCardContentIds(Resources.LoadAll<LevelUpRewardRuleProfile>(ResourceDirectory));

        /// <summary>
        /// This union cannot grant qualification or supply a new offer. Progression
        /// still validates actual types and volumes before restoring saved choices.
        /// </summary>
        public static IReadOnlyList<string> GetLegacyFrozenCardContentIds(
            IEnumerable<LevelUpRewardRuleProfile> profiles)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LevelUpRewardRuleProfile profile in profiles ?? Array.Empty<LevelUpRewardRuleProfile>())
            {
                if (profile == null || profile.useGlobalOrdinaryCatalog ||
                    string.Equals(profile.contentId, ContentId, StringComparison.Ordinal)) continue;
                foreach (string id in profile.cardContentIds ?? new List<string>())
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
            }
            var result = new List<string>(ids);
            result.Sort(StringComparer.Ordinal);
            return result.AsReadOnly();
        }

        private static List<LevelUpRewardRuleProfile> Snapshot(IEnumerable<LevelUpRewardRuleProfile> profiles)
            => profiles == null ? new List<LevelUpRewardRuleProfile>() : new List<LevelUpRewardRuleProfile>(profiles);
    }
}
