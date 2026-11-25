using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;
using WanjieScroll.UI.Hub;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 生产关卡配置的统一引用边界。它只检查稳定 ID、资产自校验与跨资产合同，
    /// 不把任何生产地图的节点顺序、坐标或连线复制进代码。
    /// </summary>
    public static class ProductionConfigurationValidator
    {
        private const string LevelResourcePath = "Levels";
        private const string MapResourcePath = "MapPreviewProfiles";
        private const string BattleResourcePath = "Content/Battle";
        private const string RewardRuleResourcePath = "Content/LevelUpRewards";
        private const string ReferenceLoadoutResourcePath =
            "Content/ReferenceLoadouts";
        private const string HubCatalogResourcePath =
            "Content/Hub/hub-world-catalog";

        /// <summary>
        /// Production admission requires explicit ordinary nodes to use the
        /// shared generator. Standalone scenarios and legacy/special roles keep
        /// their author contracts; this does not classify enemies by name.
        /// </summary>
        public static List<string> ValidateOrdinaryEncounter(LevelNode node)
        {
            var errors = new List<string>();
            if (node == null || node.encounterRole != LevelEncounterRole.Ordinary) return errors;
            if (!node.HasCommonEncounter)
                errors.Add("普通战必须绑定全局普通遭遇生成合同：" + node.contentId);
            return errors;
        }

        public static List<string> CollectErrors()
        {
            var errors = new List<string>();
            List<CardConfig> authorCards = null;
            try
            {
                // Validate the complete author metadata, independently of volume,
                // account and publication filters. These definitions are temporary.
                try { authorCards = ContentRuntimeFactory.CreateAllCardPool(); }
                catch (Exception exception)
                { errors.Add("普通奖励作者目录加载失败：" + exception.Message); }
                errors.AddRange(Validate(
                    Resources.LoadAll<LevelRunProfile>(LevelResourcePath),
                    Resources.LoadAll<MapPreviewProfile>(MapResourcePath),
                    Resources.LoadAll<BattleScenarioProfile>(BattleResourcePath),
                    Resources.LoadAll<LevelUpRewardRuleProfile>(RewardRuleResourcePath),
                    Resources.Load<HubWorldCatalog>(HubCatalogResourcePath),
                    Resources.LoadAll<RunReferenceLoadoutProfile>(ReferenceLoadoutResourcePath),
                    authorCards));
                return errors;
            }
            finally
            {
                foreach (CardConfig card in authorCards ?? new List<CardConfig>())
                {
                    if (card == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(card);
                    else UnityEngine.Object.DestroyImmediate(card);
                }
            }
        }

        /// <summary>
        /// 可用内存配置测试的纯入口。生产加载路径只存在于 CollectErrors，
        /// 规则校验本身不依赖 AssetDatabase 或 Editor 生命周期。
        /// </summary>
        public static List<string> Validate(
            IReadOnlyList<LevelRunProfile> levels,
            IReadOnlyList<MapPreviewProfile> maps,
            IReadOnlyList<BattleScenarioProfile> scenarios,
            IReadOnlyList<LevelUpRewardRuleProfile> rewardRules,
            HubWorldCatalog hubCatalog)
            => Validate(
                levels,
                maps,
                scenarios,
                rewardRules,
                hubCatalog,
                referenceLoadouts: null);

        /// <summary>
        /// 同上，并可传入实测参考装载以校验场景的稳定引用。传 null 保留
        /// 内存夹具的既有纯校验入口；生产 CollectErrors 始终传入完整资源集。
        /// </summary>
        public static List<string> Validate(
            IReadOnlyList<LevelRunProfile> levels,
            IReadOnlyList<MapPreviewProfile> maps,
            IReadOnlyList<BattleScenarioProfile> scenarios,
            IReadOnlyList<LevelUpRewardRuleProfile> rewardRules,
            HubWorldCatalog hubCatalog,
            IReadOnlyList<RunReferenceLoadoutProfile> referenceLoadouts,
            IReadOnlyList<CardConfig> ordinaryAuthorCatalog = null)
        {
            var errors = new List<string>();
            Dictionary<string, LevelRunProfile> levelsById = BuildIndex(
                levels, item => item.contentId, "LevelRunProfile", errors);
            Dictionary<string, MapPreviewProfile> mapsById = BuildIndex(
                maps, item => item.contentId, "MapPreviewProfile", errors);
            Dictionary<string, BattleScenarioProfile> scenariosById =
                BuildIndex(
                    scenarios,
                    item => item.scenarioId,
                    "BattleScenarioProfile",
                    errors);
            BuildIndex(
                    rewardRules,
                    item => item.contentId,
                    "LevelUpRewardRuleProfile",
                    errors);
            List<string> globalErrors = GlobalLevelUpRewardRuleProfileCatalog.Validate(rewardRules);
            errors.AddRange(globalErrors);
            if (ordinaryAuthorCatalog != null && globalErrors.Count == 0)
                AddPrefixed(errors, GlobalLevelUpRewardRuleProfileCatalog.ContentId,
                    GlobalLevelUpRewardRuleProfileCatalog.ResolveRequired(rewardRules)
                        .ValidateOrdinaryCatalog(ordinaryAuthorCatalog));
            Dictionary<string, RunReferenceLoadoutProfile>
                referenceLoadoutsById = BuildIndex(
                    referenceLoadouts ??
                        Array.Empty<RunReferenceLoadoutProfile>(),
                    item => item.contentId,
                    "RunReferenceLoadoutProfile",
                    errors);

            foreach (RunReferenceLoadoutProfile reference in
                     referenceLoadouts ??
                     Array.Empty<RunReferenceLoadoutProfile>())
            {
                if (reference != null)
                {
                    AddPrefixed(
                        errors,
                        reference.contentId,
                        reference.ValidateProfile());
                }
            }

            foreach (MapPreviewProfile map in maps ??
                     Array.Empty<MapPreviewProfile>())
            {
                if (map != null && !map.TryValidate(out string mapError))
                    errors.Add($"地图 {map.contentId}：{mapError}");
            }

            var validatedScenarios = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (LevelRunProfile level in levels ??
                     Array.Empty<LevelRunProfile>())
            {
                if (level == null)
                    continue;

                if (referenceLoadouts != null)
                {
                    ValidateLevelReferenceLoadoutContracts(
                        level,
                        referenceLoadoutsById,
                        errors);
                }

                if (!mapsById.TryGetValue(
                        level.mapPresentationContentId ?? string.Empty,
                        out MapPreviewProfile map))
                {
                    AddPrefixed(errors, level.contentId, level.Validate());
                    errors.Add(
                        $"关卡 {level.contentId} 找不到地图：" +
                        $"{level.mapPresentationContentId}。");
                }
                else
                {
                    AddPrefixed(
                        errors,
                        level.contentId,
                        LevelMapConfigurationValidator.Validate(level, map));
                }

                ValidateOptionalLevelReference(
                    level.contentId,
                    "前置关卡",
                    level.prerequisiteLevelContentId,
                    levelsById,
                    errors);
                ValidateOptionalLevelReference(
                    level.contentId,
                    "下一层",
                    level.nextFloorContentId,
                    levelsById,
                    errors);

                AddPrefixed(errors, level.contentId,
                    GlobalLevelUpRewardRuleProfileCatalog.ValidateLevelRuleReference(
                        level.levelUpRewardRuleProfileId));

                if (level.nodes == null)
                    continue;
                for (int i = 0; i < level.nodes.Count; i++)
                {
                    LevelNode node = level.nodes[i];
                    AddPrefixed(errors, level.contentId, ValidateOrdinaryEncounter(node));
                    if (node == null ||
                        node.nodeType != LevelNodeType.Encounter &&
                        node.nodeType != LevelNodeType.Finale)
                    {
                        continue;
                    }
                    if (!scenariosById.TryGetValue(
                            node.scenarioId ?? string.Empty,
                            out BattleScenarioProfile scenario))
                    {
                        errors.Add(
                            $"关卡 {level.contentId} 的节点 {node.contentId} " +
                            $"找不到战斗配置：{node.scenarioId}。");
                        continue;
                    }
                    if (!validatedScenarios.Add(scenario.scenarioId))
                        continue;
                    if (referenceLoadouts != null &&
                        !string.IsNullOrWhiteSpace(
                            scenario.referenceLoadoutContentId) &&
                        !referenceLoadoutsById.ContainsKey(
                            scenario.referenceLoadoutContentId))
                    {
                        errors.Add(
                            $"战斗配置 {scenario.scenarioId} 找不到实测参考装载：" +
                            scenario.referenceLoadoutContentId + "。");
                    }
                    try
                    {
                        AddPrefixed(
                            errors,
                            scenario.scenarioId,
                            scenario.ValidateProfile());
                    }
                    catch (Exception exception)
                    {
                        errors.Add(
                            $"战斗配置 {scenario.scenarioId} 校验失败：" +
                            exception.Message);
                    }
                }
            }

            ValidateHubCatalog(hubCatalog, levelsById, errors);
            return errors;
        }

        /// <summary>
        /// Dungeon 只把 referenceLoadoutContentId 当稳定合同，不依赖 Content
        /// 资源；生产装配层在这里把它与实际参考装载交叉校验。
        /// </summary>
        private static void ValidateLevelReferenceLoadoutContracts(
            LevelRunProfile level,
            IReadOnlyDictionary<string, RunReferenceLoadoutProfile>
                referenceLoadoutsById,
            ICollection<string> errors)
        {
            foreach (LevelNode node in level.nodes ??
                     new List<LevelNode>())
            {
                foreach (LevelInteractionEffect effect in
                         node?.completionEffects ??
                         new List<LevelInteractionEffect>())
                {
                    if (effect?.kind != LevelInteractionEffectKind.ReachLevel ||
                        effect.targetLevelResult == null ||
                        string.IsNullOrWhiteSpace(effect.targetLevelResult
                            .referenceLoadoutContentId))
                    {
                        continue;
                    }
                    if (!referenceLoadoutsById.ContainsKey(
                            effect.targetLevelResult
                                .referenceLoadoutContentId))
                    {
                        errors.Add(
                            $"关卡 {level.contentId} 的目标等级交互 " +
                            $"{effect.effectId} 找不到实测参考装载：" +
                            effect.targetLevelResult
                                .referenceLoadoutContentId + "。");
                    }
                }
            }
        }

        private static void ValidateHubCatalog(
            HubWorldCatalog hubCatalog,
            IReadOnlyDictionary<string, LevelRunProfile> levelsById,
            ICollection<string> errors)
        {
            if (hubCatalog == null)
            {
                errors.Add(
                    $"缺少 Resources/{HubCatalogResourcePath}.asset。");
                return;
            }

            AddPrefixed(
                errors,
                "HubWorldCatalog",
                hubCatalog.CollectValidationErrors());
            if (hubCatalog.entries == null)
                return;

            for (int i = 0; i < hubCatalog.entries.Count; i++)
            {
                HubWorldCatalog.Entry entry = hubCatalog.entries[i];
                if (entry == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(entry.unlockLevelContentId) &&
                    !levelsById.ContainsKey(entry.unlockLevelContentId))
                {
                    errors.Add(
                        $"Hub 关区 {entry.destinationId} 找不到解锁关卡：" +
                        $"{entry.unlockLevelContentId}。");
                }
                if (string.IsNullOrWhiteSpace(entry.firstFloorContentId) ||
                    !levelsById.TryGetValue(
                        entry.firstFloorContentId,
                        out LevelRunProfile firstFloor))
                {
                    errors.Add(
                        $"Hub 关区 {entry.destinationId} 找不到首层：" +
                        $"{entry.firstFloorContentId}。");
                    continue;
                }
                if (!string.Equals(
                        entry.destinationId,
                        firstFloor.dungeonContentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Hub 关区 {entry.destinationId} 与首层 " +
                        $"{entry.firstFloorContentId} 的 dungeonContentId " +
                        $"{firstFloor.dungeonContentId} 不一致。");
                }
            }
        }

        private static void ValidateOptionalLevelReference(
            string ownerId,
            string label,
            string referenceId,
            IReadOnlyDictionary<string, LevelRunProfile> levelsById,
            ICollection<string> errors)
        {
            if (!string.IsNullOrWhiteSpace(referenceId) &&
                !levelsById.ContainsKey(referenceId))
            {
                errors.Add($"关卡 {ownerId} 的{label}不存在：{referenceId}。");
            }
        }

        private static Dictionary<string, T> BuildIndex<T>(
            IReadOnlyList<T> items,
            Func<T, string> idSelector,
            string label,
            ICollection<string> errors)
            where T : UnityEngine.Object
        {
            var result = new Dictionary<string, T>(
                StringComparer.OrdinalIgnoreCase);
            if (items == null)
                return result;

            for (int i = 0; i < items.Count; i++)
            {
                T item = items[i];
                if (item == null)
                {
                    errors.Add($"{label}[{i}] 不能为空。");
                    continue;
                }
                string id = idSelector(item);
                if (string.IsNullOrWhiteSpace(id))
                {
                    errors.Add($"{label} {item.name} 缺少稳定 ID。");
                    continue;
                }
                if (!result.TryAdd(id, item))
                    errors.Add($"{label} 稳定 ID 重复：{id}。");
            }
            return result;
        }

        private static void AddPrefixed(
            ICollection<string> destination,
            string ownerId,
            IEnumerable<string> source)
        {
            if (source == null)
                return;
            foreach (string error in source)
                destination.Add($"{ownerId}：{error}");
        }
    }
}
