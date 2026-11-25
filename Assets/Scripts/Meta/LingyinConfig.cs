using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Meta
{
    public enum LingyinGrade
    {
        Common,
        Uncommon,
        Rare,
        VeryRare,
        UltraRare
    }

    public enum LingyinMaterial
    {
        Copper,
        Jade,
        Gold,
        // Append-only: old serialized Copper/Jade/Gold values remain stable.
        Silver
    }

    /// <summary>
    /// 灵印的统一内容配置。稳定身份、宿主规则与战斗效果在此汇合，地图只引用
    /// contentId，卡牌实例只保存精简后的 CardSealSpec。
    /// </summary>
    [CreateAssetMenu(fileName = "NewLingyin", menuName = "万界长卷/灵印")]
    public sealed class LingyinConfig : ScriptableObject
    {
        public string contentId;
        public string displayName;
        public string sealCharacter;
        public CardSealEffect effectType;
        [Min(1)] public int effectValue = 1;
        public LingyinGrade grade;
        public LingyinMaterial material;
        public string presentationId;
        [TextArea(1, 3)] public string description;

        public CardSealSpec ToCardSealSpec()
            => new CardSealSpec
            {
                contentId = contentId,
                effect = effectType,
                value = effectValue,
                comboScalable = false
            };

        public bool IsValid(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(contentId))
                error = "灵印缺少稳定 contentId。";
            else if (string.IsNullOrWhiteSpace(displayName))
                error = $"灵印 {contentId} 缺少显示名。";
            else if (string.IsNullOrWhiteSpace(sealCharacter))
                error = $"灵印 {contentId} 缺少印面字。";
            else if (effectValue <= 0)
                error = $"灵印 {contentId} 的 effectValue 必须大于 0。";
            return error == null;
        }
    }

    public static class LingyinCatalog
    {
        private const string ResourceFolder = "Content/Seals";
        private static LingyinConfig[] _configs;

        // Retain asset references, not copied mutable fields. Descriptions and
        // specifications still come from the current objects on every lookup.
        // Discovery is shared across scene/tooltip queries; tools can explicitly
        // refresh after adding or deleting resources while the domain survives.
        private static LingyinConfig[] Configs
            => _configs ??= Resources.LoadAll<LingyinConfig>(ResourceFolder);

        public static void Refresh()
            => _configs = Resources.LoadAll<LingyinConfig>(ResourceFolder);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearCache() => _configs = null;

        /// <summary>
        /// 账号已解锁普通灵印构成的铜箱候选池。池身份独立于当前候选内容，
        /// 便于奖励检查点记录其作者规则版本。
        /// </summary>
        public const string UnlockedCopperChestPoolId =
            "pool.seal.xiyou.copper-chest.unlocked.v1";
        public const string UnlockedSilverChestPoolId =
            "pool.seal.xiyou.silver-chest.unlocked.v1";
        /// <summary>
        /// F5 Finale 的高阶印匣。候选只来自账号已解锁的珍稀／仙品灵印，
        /// 池身份独立于本次冻结的候选列表，便于存档与作者版本审计。
        /// </summary>
        public const string UnlockedHighTierChestPoolId =
            "pool.seal.xiyou.high-tier-chest.unlocked.v1";

        private static readonly string[] DefaultUnlockedSealIds =
        {
            "seal.xiyou.double-damage.common.v1",
            "seal.xiyou.draw.common.v1",
            "seal.xiyou.armor.common.v1",
            "seal.xiyou.amount.common.v1",
            "seal.xiyou.benglie.common.v1"
        };

        /// <summary>新账号与 schema 2 旧存档共享的五枚基础灵印。</summary>
        public static List<string> CreateDefaultUnlockedSealContentIds()
            => new List<string>(DefaultUnlockedSealIds);

        /// <summary>
        /// 从账号已解锁集合生成铜箱可抽取规则。只返回已存在、配置有效且
        /// 品阶不高于上品（Rare）的灵印，并按稳定 contentId 排序。
        /// 返回值是规则副本，不向调用方暴露可变的作者资产。
        /// </summary>
        public static IReadOnlyList<CardSealSpec> GetUnlockedCopperChestPool(
            IEnumerable<string> unlockedSealContentIds)
            => GetUnlockedChestConfigs(unlockedSealContentIds)
                .Select(config => config.ToCardSealSpec())
                .ToList()
                .AsReadOnly();

        /// <summary>
        /// 银纹箱优先保证的较高等级候选。完整三选一仍来自铜纹箱相同的已解锁
        /// 合法池；当前以 Rare 作为银纹优先级，候选不足时由普通池补齐。
        /// </summary>
        public static IReadOnlyList<string>
            GetUnlockedSilverChestPreferredContentIds(
                IEnumerable<string> unlockedSealContentIds)
            => GetUnlockedChestConfigs(unlockedSealContentIds)
                .Where(config => config.grade >= LingyinGrade.Rare)
                .Select(config => config.contentId)
                .ToList()
                .AsReadOnly();

        /// <summary>
        /// 高阶印匣的完整候选池。只接受已解锁、配置有效且品阶为珍稀或
        /// 仙品的灵印；结果按稳定 contentId 排序。
        /// </summary>
        public static IReadOnlyList<CardSealSpec>
            GetUnlockedHighTierChestPool(
                IEnumerable<string> unlockedSealContentIds)
            => GetUnlockedHighTierConfigs(unlockedSealContentIds)
                .Select(config => config.ToCardSealSpec())
                .ToList()
                .AsReadOnly();

        /// <summary>高阶印匣优先抽取仙品候选；不足时由完整高阶池补齐。</summary>
        public static IReadOnlyList<string>
            GetUnlockedHighTierChestPreferredContentIds(
                IEnumerable<string> unlockedSealContentIds)
            => GetUnlockedHighTierConfigs(unlockedSealContentIds)
                .Where(config => config.grade >= LingyinGrade.UltraRare)
                .Select(config => config.contentId)
                .ToList()
                .AsReadOnly();

        private static List<LingyinConfig> GetUnlockedChestConfigs(
            IEnumerable<string> unlockedSealContentIds)
        {
            var unlocked = new HashSet<string>(
                unlockedSealContentIds ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            LingyinConfig[] all = Configs;
            var eligible = new List<LingyinConfig>();
            for (int i = 0; i < all.Length; i++)
            {
                LingyinConfig candidate = all[i];
                if (candidate == null ||
                    candidate.grade > LingyinGrade.Rare ||
                    !unlocked.Contains(candidate.contentId) ||
                    !candidate.IsValid(out _))
                {
                    continue;
                }
                eligible.Add(candidate);
            }

            eligible.Sort((left, right) => string.Compare(
                left.contentId,
                right.contentId,
                StringComparison.Ordinal));
            return eligible;
        }

        private static List<LingyinConfig> GetUnlockedHighTierConfigs(
            IEnumerable<string> unlockedSealContentIds)
        {
            var unlocked = new HashSet<string>(
                unlockedSealContentIds ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            LingyinConfig[] all = Configs;
            var eligible = new List<LingyinConfig>();
            for (int i = 0; i < all.Length; i++)
            {
                LingyinConfig candidate = all[i];
                if (candidate == null ||
                    candidate.grade < LingyinGrade.VeryRare ||
                    !unlocked.Contains(candidate.contentId) ||
                    !candidate.IsValid(out _))
                {
                    continue;
                }
                eligible.Add(candidate);
            }

            eligible.Sort((left, right) => string.Compare(
                left.contentId,
                right.contentId,
                StringComparison.Ordinal));
            return eligible;
        }

        public static bool TryGet(string contentId, out LingyinConfig config)
        {
            config = null;
            if (string.IsNullOrWhiteSpace(contentId))
                return false;
            LingyinConfig[] all = Configs;
            for (int i = 0; i < all.Length; i++)
            {
                LingyinConfig candidate = all[i];
                if (candidate != null && string.Equals(
                        candidate.contentId,
                        contentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    config = candidate;
                    return true;
                }
            }
            return false;
        }

        public static List<string> ValidateAll()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            LingyinConfig[] all = Configs;
            for (int i = 0; i < all.Length; i++)
            {
                LingyinConfig config = all[i];
                if (config == null)
                    continue;
                if (!config.IsValid(out string error))
                    errors.Add(error);
                else if (!ids.Add(config.contentId))
                    errors.Add($"灵印 contentId 重复：{config.contentId}。");
            }
            return errors;
        }
    }
}
