using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WanjieScroll.Dungeon
{
    /// <summary>Explicit author intent; legacy Unspecified nodes never gain ordinary eligibility.</summary>
    public enum LevelEncounterRole
    {
        Unspecified = 0,
        Ordinary = 1,
        Elite = 2,
        Boss = 3
    }

    /// <summary>
    /// Owns exploration authoring eligibility and graph safety, not random state.
    /// Every reward needs one distinct ordinary guard. Groups must have disjoint
    /// candidate sets, so sampling one group cannot consume another group's slot.
    /// Reward count and fixed jade amounts are authored, never floor-specific constants.
    /// </summary>
    [Serializable]
    public sealed class LevelExplorationGuardRule
    {
        public string ruleId;
        public int revision = 1;
        public List<string> rewardNodeIds = new List<string>();
        public List<string> candidateEncounterNodeIds = new List<string>();

        /// <summary>
        /// Validates the whole optional contract, including all possible sampled edges.
        /// Null/empty rule lists preserve legacy behavior. Does not edit or reorder assets.
        /// </summary>
        public static List<string> Validate(LevelRunProfile profile)
        {
            var errors = new List<string>();
            if (profile == null) { errors.Add("探索守卫缺少楼层配置。"); return errors; }
            if (profile.explorationGuards == null || profile.explorationGuards.Count == 0) return errors;
            if (string.IsNullOrWhiteSpace(profile.contentId)) errors.Add("探索守卫楼层 contentId 不能为空。");
            var nodes = ReadNodes(profile.nodes, errors);
            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            var rewards = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (LevelExplorationGuardRule rule in profile.explorationGuards)
            {
                if (rule == null) { errors.Add("explorationGuards 包含空规则。"); continue; }
                if (string.IsNullOrWhiteSpace(rule.ruleId) || !ruleIds.Add(rule.ruleId))
                    errors.Add("探索 ruleId 为空或重复：" + rule.ruleId);
                if (rule.revision <= 0) errors.Add("探索 revision 必须为正数：" + rule.ruleId);
                ValidateIds(rule.rewardNodeIds, rewards, "探索奖励", rule.ruleId, errors);
                ValidateIds(rule.candidateEncounterNodeIds, candidates, "探索候选（跨组必须互斥）", rule.ruleId, errors);
                if (rule.rewardNodeIds == null || rule.rewardNodeIds.Count == 0)
                    errors.Add("探索规则至少需要一个奖励节点：" + rule.ruleId);
                if (rule.candidateEncounterNodeIds == null ||
                    rule.candidateEncounterNodeIds.Count < (rule.rewardNodeIds?.Count ?? 1))
                    errors.Add("探索普通战候选数量少于奖励数量：" + rule.ruleId);
            }

            foreach (string rewardId in rewards)
            {
                if (!nodes.TryGetValue(rewardId, out LevelNode reward))
                { errors.Add("探索奖励引用不存在：" + rewardId); continue; }
                if (reward.nodeType != LevelNodeType.Interaction ||
                    reward.encounterRole != LevelEncounterRole.Unspecified)
                    errors.Add("探索奖励必须是无战斗角色的 Interaction：" + rewardId);
                if ((reward.prerequisiteNodeIds?.Count ?? 0) != 0)
                    errors.Add("探索奖励的静态前置必须为空：" + rewardId);
                if ((reward.victoryRewards?.Count ?? 0) != 0)
                    errors.Add("探索奖励不能同时配置 victoryRewards：" + rewardId);
                LevelInteractionEffect effect = reward.completionEffects?.Count == 1
                    ? reward.completionEffects[0] : null;
                if (effect == null || effect.kind != LevelInteractionEffectKind.GrantJade ||
                    effect.amount <= 0 || effect.amountMax != 0 || string.IsNullOrWhiteSpace(effect.effectId))
                    errors.Add("探索奖励必须只有一个有效的固定 GrantJade 效果：" + rewardId);
            }
            foreach (string candidateId in candidates)
            {
                if (!nodes.TryGetValue(candidateId, out LevelNode candidate))
                { errors.Add("探索候选引用不存在：" + candidateId); continue; }
                if (candidate.nodeType != LevelNodeType.Encounter ||
                    candidate.encounterRole != LevelEncounterRole.Ordinary ||
                    string.IsNullOrWhiteSpace(candidate.scenarioId))
                    errors.Add("探索候选必须是显式 Ordinary 遭遇，不能是精英、Boss 或终局：" + candidateId);
                if ((candidate.victoryRewards?.Count ?? 0) != 0 || (candidate.completionEffects?.Count ?? 0) != 0)
                    errors.Add("探索候选不能已有固定 victory/completion 奖励：" + candidateId);
            }
            foreach (LevelNode node in nodes.Values)
            {
                if (rewards.Contains(node.contentId) ||
                    (node.nodeType != LevelNodeType.Interaction && node.nodeType != LevelNodeType.Exit)) continue;
                foreach (string parent in node.prerequisiteNodeIds ?? new List<string>())
                    if (parent != null && candidates.Contains(parent))
                        errors.Add("探索候选已守卫固定地图交互：" + parent + " -> " + node.contentId);
            }
            if (errors.Count == 0) ValidatePossibleGraph(nodes, profile.explorationGuards, errors);
            return errors;
        }

        /// <summary>
        /// Canonical semantic identity. Presentation, positions, snapshots and array order
        /// are intentionally excluded; graph validity is checked separately on every use.
        /// </summary>
        internal static string ValidatedContract(LevelRunProfile profile)
        {
            List<string> errors = Validate(profile);
            if (profile != null && string.IsNullOrWhiteSpace(profile.contentId))
                errors.Add("探索视图所属楼层 contentId 不能为空。");
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
            var key = new StringBuilder();
            Append(key, profile.contentId);
            foreach (LevelExplorationGuardRule rule in Rules(profile).OrderBy(r => r.ruleId, StringComparer.Ordinal))
            {
                Append(key, rule.ruleId);
                Append(key, rule.revision.ToString(CultureInfo.InvariantCulture));
                Append(key, rule.rewardNodeIds.Count.ToString(CultureInfo.InvariantCulture));
                foreach (string id in rule.rewardNodeIds.OrderBy(id => id, StringComparer.Ordinal))
                {
                    LevelInteractionEffect effect = profile.FindNode(id).completionEffects[0];
                    Append(key, id);
                    Append(key, effect.effectId);
                    Append(key, effect.amount.ToString(CultureInfo.InvariantCulture));
                }
                Append(key, rule.candidateEncounterNodeIds.Count.ToString(CultureInfo.InvariantCulture));
                foreach (string id in rule.candidateEncounterNodeIds.OrderBy(id => id, StringComparer.Ordinal)) Append(key, id);
            }
            return key.ToString();
        }

        internal static IReadOnlyList<LevelExplorationGuardRule> Rules(LevelRunProfile profile)
            => (IReadOnlyList<LevelExplorationGuardRule>)profile.explorationGuards ?? Array.Empty<LevelExplorationGuardRule>();

        private static void Append(StringBuilder key, string value)
            => key.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);

        private static void ValidateIds(List<string> ids, HashSet<string> all, string kind, string ruleId, List<string> errors)
        {
            if (ids == null) { errors.Add(kind + "列表为空：" + ruleId); return; }
            foreach (string id in ids)
                if (string.IsNullOrWhiteSpace(id) || !all.Add(id)) errors.Add(kind + " ID 为空或重复：" + id);
        }

        private static Dictionary<string, LevelNode> ReadNodes(List<LevelNode> source, List<string> errors)
        {
            var nodes = new Dictionary<string, LevelNode>(StringComparer.Ordinal);
            if (source == null) { errors.Add("探索规则缺少 nodes。"); return nodes; }
            var effects = new HashSet<string>(StringComparer.Ordinal);
            foreach (LevelNode node in source)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.contentId))
                { errors.Add("探索配置包含空节点或空 contentId。"); continue; }
                if (nodes.ContainsKey(node.contentId)) errors.Add("探索配置节点 ID 重复：" + node.contentId);
                else nodes.Add(node.contentId, node);
                foreach (LevelInteractionEffect effect in node.completionEffects ?? new List<LevelInteractionEffect>())
                    if (effect == null || string.IsNullOrWhiteSpace(effect.effectId) || !effects.Add(effect.effectId))
                        errors.Add("探索配置 completionEffect ID 为空或重复：" + node.contentId);
            }
            foreach (LevelNode node in nodes.Values)
            {
                if ((node.prerequisiteNodeIds?.Count ?? 0) > 1) errors.Add("节点最多只能有一个静态前置：" + node.contentId);
                foreach (string parent in node.prerequisiteNodeIds ?? new List<string>())
                    if (string.IsNullOrWhiteSpace(parent) || !nodes.ContainsKey(parent))
                        errors.Add("探索配置静态前置引用不存在：" + node.contentId + " -> " + parent);
            }
            return nodes;
        }

        private static void ValidatePossibleGraph(Dictionary<string, LevelNode> nodes,
            List<LevelExplorationGuardRule> rules, List<string> errors)
        {
            var parents = nodes.ToDictionary(pair => pair.Key,
                pair => new List<string>(pair.Value.prerequisiteNodeIds ?? new List<string>()), StringComparer.Ordinal);
            foreach (LevelExplorationGuardRule rule in rules)
                foreach (string rewardId in rule.rewardNodeIds) parents[rewardId].AddRange(rule.candidateEncounterNodeIds);
            // The union graph rejects any possible sampled cycle, including cycles crossing
            // groups. A seed must never decide whether an otherwise identical asset is valid.
            var children = nodes.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
            foreach (var pair in parents) foreach (string parent in pair.Value) children[parent].Add(pair.Key);
            var counts = parents.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
            var ready = new Queue<string>(counts.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            int visited = 0;
            while (ready.Count > 0)
            {
                string id = ready.Dequeue();
                visited++;
                foreach (string child in children[id]) if (--counts[child] == 0) ready.Enqueue(child);
            }
            if (visited != nodes.Count) errors.Add("探索守卫的可能前置关系包含循环（含跨组循环）。");
        }
    }
}
