using System;
using System.Collections.Generic;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>联合校验关卡逻辑配置与地图表现配置的稳定 ID 合同。</summary>
    public static class LevelMapConfigurationValidator
    {
        public static List<string> Validate(
            LevelRunProfile level,
            MapPreviewProfile map)
        {
            List<string> errors = Validate(
                level,
                map,
                RewardArtworkCatalog.ResolveInteractionPresentationId,
                RewardArtworkCatalog.CanResolvePresentationId);
            if (level == null || map == null || errors.Count > 0) return errors;
            try
            {
                // Read metadata only. Missing shared subjects are caught before
                // map objects are created; this never opens a reward or scene.
                MapRewardNodeStyleResolver.Resolve(level.nodes, map);
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(exception.Message);
            }
            return errors;
        }

        /// <summary>
        /// Resource lookup is injected so the stable configuration contract
        /// can be checked without creating a scene or reward UI.
        /// </summary>
        public static List<string> Validate(
            LevelRunProfile level,
            MapPreviewProfile map,
            Func<LevelInteractionEffect, string> resolvePresentationId,
            Func<string, bool> canResolvePresentationId)
        {
            var errors = new List<string>();
            if (level == null)
            {
                errors.Add("LevelRunProfile 不能为空。");
                return errors;
            }
            if (map == null)
            {
                errors.Add(
                    $"关卡 {level.contentId} 缺少 MapPreviewProfile。");
                return errors;
            }

            errors.AddRange(level.Validate());
            if (!map.TryValidate(out string mapError))
                errors.Add(mapError);

            if (level.mapPresentationContentId != map.contentId)
            {
                errors.Add(
                    $"关卡 {level.contentId} 指向地图 " +
                    $"{level.mapPresentationContentId}，实际加载为 {map.contentId}。");
            }

            var logicalIds = new HashSet<string>();
            if (level.nodes != null)
            {
                for (int i = 0; i < level.nodes.Count; i++)
                {
                    LevelNode node = level.nodes[i];
                    if (node != null &&
                        !string.IsNullOrWhiteSpace(node.contentId))
                        logicalIds.Add(node.contentId);
                }
            }

            var visualGroups = new HashSet<string>();
            if (map.nodes != null)
            {
                for (int i = 0; i < map.nodes.Count; i++)
                {
                    MapPreviewNode node = map.nodes[i];
                    if (node != null &&
                        !string.IsNullOrWhiteSpace(node.interactionGroupId))
                        visualGroups.Add(node.interactionGroupId);
                }
            }

            foreach (string logicalId in logicalIds)
            {
                if (!visualGroups.Contains(logicalId))
                    errors.Add($"逻辑节点 {logicalId} 缺少视觉交互组。");
            }
            foreach (string groupId in visualGroups)
            {
                if (!logicalIds.Contains(groupId))
                    errors.Add($"视觉交互组 {groupId} 没有逻辑节点。");
            }

            LevelMapExplorationBindings.Validate(level, map, errors);
            LevelMapNodePresentationValidator.Validate(
                level,
                resolvePresentationId,
                canResolvePresentationId,
                errors);

            return errors;
        }
    }
}
