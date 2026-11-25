using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Joins two immutable contracts at the composition boundary: Dungeon owns
    /// selected guard IDs; Map owns authored positions. No RNG, save or asset
    /// mutation is allowed here. A missing slot is an authoring failure, not a
    /// fallback to an unrelated static position or a second random draw.
    /// </summary>
    internal static class LevelMapExplorationBindings
    {
        internal static void Validate(LevelRunProfile level, MapPreviewProfile map,
            List<string> errors)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rewards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LevelExplorationGuardRule rule in level.explorationGuards ??
                     new List<LevelExplorationGuardRule>())
            {
                if (rule == null) continue;
                candidates.UnionWith(rule.candidateEncounterNodeIds ?? new List<string>());
                rewards.UnionWith(rule.rewardNodeIds ?? new List<string>());
            }
            var slots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MapExplorationRewardSlot slot in map.explorationRewardSlots ??
                     new List<MapExplorationRewardSlot>())
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.guardGroupId)) continue;
                slots.Add(slot.guardGroupId);
                if (!candidates.Contains(slot.guardGroupId))
                    errors.Add("探索奖励槽不属于候选守卫：" + slot.guardGroupId);
            }
            foreach (string candidate in candidates)
                if (!slots.Contains(candidate))
                    errors.Add("探索候选守卫缺少奖励承载槽：" + candidate);
            foreach (string reward in rewards)
                if (map.nodes == null || map.nodes.Count(node => node != null &&
                        string.Equals(node.interactionGroupId, reward, StringComparison.OrdinalIgnoreCase)) != 1)
                    errors.Add("探索奖励必须对应一个地图代表节点：" + reward);
        }

        internal static IReadOnlyDictionary<string, Vector2> Resolve(
            LevelRunSession session, MapPreviewProfile map)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (map == null) throw new ArgumentNullException(nameof(map));
            var errors = new List<string>();
            Validate(session.Profile, map, errors);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" | ", errors));

            var positions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            foreach (LevelExplorationGuardRule rule in session.Profile.explorationGuards ??
                     new List<LevelExplorationGuardRule>())
            {
                foreach (string reward in rule.rewardNodeIds)
                {
                    if (!session.TryGetExplorationGuard(reward, out string guard))
                        throw new InvalidOperationException("Run 缺少冻结的探索守卫映射：" + reward);
                    MapExplorationRewardSlot slot = map.explorationRewardSlots.Single(item =>
                        string.Equals(item.guardGroupId, guard, StringComparison.OrdinalIgnoreCase));
                    MapPreviewNode visual = map.nodes.Single(item => string.Equals(
                        item.interactionGroupId, reward, StringComparison.OrdinalIgnoreCase));
                    positions.Add(visual.contentId, slot.normalizedPosition);
                }
            }
            return positions;
        }
    }
}
