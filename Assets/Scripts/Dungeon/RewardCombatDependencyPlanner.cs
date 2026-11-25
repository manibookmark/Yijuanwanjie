using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>A logical combat's measured authoring strength; repeated visual instances are not slots.</summary>
    public sealed class RewardCombatStrength
    {
        public string NodeId { get; }
        public bool IsBoss { get; }
        public double Score { get; }
        public string Explanation { get; }

        public RewardCombatStrength(string nodeId, bool isBoss, double score, string explanation)
        {
            NodeId = nodeId;
            IsBoss = isBoss;
            Score = score;
            Explanation = explanation ?? string.Empty;
        }
    }

    /// <summary>Only the prerequisite is proposed. Reward contents and rarity remain authored values.</summary>
    public sealed class RewardCombatDependencyBinding
    {
        public string RewardNodeId { get; }
        public string CombatNodeId { get; }
        public RewardRarity Rarity { get; }
        public double CombatStrength { get; }
        public bool IsFixedBossExit { get; }
        public int FixedJadeAmount { get; }
        public IReadOnlyList<string> PreviousPrerequisiteNodeIds { get; }

        internal RewardCombatDependencyBinding(
            LevelNode reward, RewardCombatStrength combat, RewardRarity rarity, int jadeAmount)
        {
            RewardNodeId = reward.contentId;
            CombatNodeId = combat.NodeId;
            Rarity = rarity;
            CombatStrength = combat.Score;
            IsFixedBossExit = reward.nodeType == LevelNodeType.Exit;
            FixedJadeAmount = jadeAmount;
            PreviousPrerequisiteNodeIds = Array.AsReadOnly(
                (reward.prerequisiteNodeIds ?? new List<string>()).ToArray());
        }
    }

    /// <summary>A complete validated snapshot. It holds no mutable level or asset references.</summary>
    public sealed class RewardCombatDependencyPlan
    {
        public IReadOnlyList<RewardCombatDependencyBinding> Bindings { get; }
        public IReadOnlyList<RewardCombatStrength> CombatStrengths { get; }

        internal RewardCombatDependencyPlan(
            IEnumerable<RewardCombatDependencyBinding> bindings,
            IEnumerable<RewardCombatStrength> strengths)
        {
            Bindings = Array.AsReadOnly(bindings.ToArray());
            CombatStrengths = Array.AsReadOnly(strengths.ToArray());
        }
    }

    /// <summary>
    /// Owns reward-to-combat ordering, logical identity and cycle safety. Build never writes a node.
    /// Interaction rewards span ascending non-Boss combat strength in ascending authored rarity;
    /// ties use stable node IDs. Distinct combats are preferred, but sharing is allowed when needed.
    /// An Encounter Boss is reserved for its one existing fixed-jade map Exit. A sole Finale Boss
    /// already ends the floor and requires no Exit or reward binding of its own. Combat settlement
    /// drops remain a separate authored contract, as do every reward's effects and rarity.
    /// Strength estimation and asset loading belong to the caller, not this rule module.
    /// </summary>
    public static class RewardCombatDependencyPlanner
    {
        public static RewardCombatDependencyPlan Build(
            IEnumerable<LevelNode> nodes,
            IEnumerable<RewardCombatStrength> combatStrengths)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (combatStrengths == null) throw new ArgumentNullException(nameof(combatStrengths));
            Dictionary<string, LevelNode> byId = ReadNodes(nodes);
            Dictionary<string, string> parents = ReadParents(byId);
            EnsureAcyclic(parents);
            List<RewardCombatStrength> strengths = ReadStrengths(combatStrengths, byId);
            List<RewardCombatStrength> ordinary = strengths.Where(s => !s.IsBoss).ToList();
            List<RewardCombatStrength> bosses = strengths.Where(s => s.IsBoss).ToList();
            List<LevelNode> rewards = byId.Values
                .Where(n => n.nodeType == LevelNodeType.Interaction)
                .OrderBy(RewardRarityOf).ThenBy(n => n.contentId, StringComparer.Ordinal).ToList();
            List<LevelNode> exits = byId.Values.Where(n => n.nodeType == LevelNodeType.Exit)
                .OrderBy(n => n.contentId, StringComparer.Ordinal).ToList();
            var bindings = new List<RewardCombatDependencyBinding>();

            ValidateTerminationContract(byId.Values, bosses, exits);
            if (rewards.Count > 0 && ordinary.Count == 0)
                throw new InvalidOperationException("Interaction 奖励没有可绑定的非 Boss 逻辑战斗。");

            // Remove only the edges this plan owns before checking proposed replacements.
            foreach (LevelNode reward in rewards.Concat(exits)) parents[reward.contentId] = null;
            if (exits.Count == 1)
            {
                LevelNode exit = exits[0];
                int jade = FixedExitJade(exit);
                parents[exit.contentId] = bosses[0].NodeId;
                EnsureAcyclic(parents);
                bindings.Add(new RewardCombatDependencyBinding(exit, bosses[0], RewardRarityOf(exit), jade));
            }

            var chosen = new RewardCombatStrength[rewards.Count];
            bool found = TryAssign(rewards, ordinary, parents, chosen, 0,
                new HashSet<string>(StringComparer.Ordinal), rewards.Count <= ordinary.Count);
            if (!found && rewards.Count <= ordinary.Count)
                found = TryAssign(rewards, ordinary, parents, chosen, 0,
                    new HashSet<string>(StringComparer.Ordinal), false);
            if (!found)
                throw new InvalidOperationException("无法在保留战斗路线且不产生循环的前提下，按奖励稀有度单调匹配非 Boss 战斗。");

            EnsureAcyclic(parents);
            for (int i = 0; i < rewards.Count; i++)
                bindings.Add(new RewardCombatDependencyBinding(rewards[i], chosen[i], RewardRarityOf(rewards[i]), 0));
            return new RewardCombatDependencyPlan(bindings, strengths);
        }

        /// <summary>
        /// Revalidates the entire plan against the current logical nodes before changing any edge.
        /// Only reward prerequisites are written: combat victoryRewards, map completionEffects,
        /// their authored rarity and combat routes are preserved. Reapplying the same plan is safe.
        /// </summary>
        public static void Apply(RewardCombatDependencyPlan plan, IEnumerable<LevelNode> nodes)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            Dictionary<string, LevelNode> byId = ReadNodes(nodes);
            RewardCombatDependencyPlan current = Build(byId.Values, plan.CombatStrengths);
            var expected = current.Bindings.ToDictionary(b => b.RewardNodeId, StringComparer.Ordinal);
            if (plan.Bindings.Count != expected.Count)
                throw new InvalidOperationException("奖励节点已变化，请重新构建依赖计划。");
            var writes = new List<KeyValuePair<LevelNode, List<string>>>();
            foreach (RewardCombatDependencyBinding binding in plan.Bindings)
            {
                RewardCombatDependencyBinding actual;
                if (!expected.TryGetValue(binding.RewardNodeId, out actual) ||
                    actual.CombatNodeId != binding.CombatNodeId || actual.Rarity != binding.Rarity ||
                    actual.IsFixedBossExit != binding.IsFixedBossExit || actual.FixedJadeAmount != binding.FixedJadeAmount)
                    throw new InvalidOperationException("奖励依赖计划与当前节点合同不一致：" + binding.RewardNodeId);
                LevelNode reward = byId[binding.RewardNodeId];
                if (reward.prerequisiteNodeIds != null && reward.prerequisiteNodeIds.Count == 1 &&
                    reward.prerequisiteNodeIds[0] == binding.CombatNodeId) continue;
                writes.Add(new KeyValuePair<LevelNode, List<string>>(
                    reward, new List<string> { binding.CombatNodeId }));
            }
            foreach (KeyValuePair<LevelNode, List<string>> write in writes)
                write.Key.prerequisiteNodeIds = write.Value;
        }

        private static Dictionary<string, LevelNode> ReadNodes(IEnumerable<LevelNode> nodes)
        {
            var result = new Dictionary<string, LevelNode>(StringComparer.Ordinal);
            foreach (LevelNode node in nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.contentId))
                    throw new InvalidOperationException("奖励依赖包含空节点或空逻辑 contentId。");
                if (!Enum.IsDefined(typeof(LevelNodeType), node.nodeType))
                    throw new InvalidOperationException("奖励依赖包含未知节点类型：" + node.contentId);
                if (result.ContainsKey(node.contentId))
                    throw new InvalidOperationException("逻辑节点 contentId 重复：" + node.contentId);
                result.Add(node.contentId, node);
            }
            if (result.Count == 0) throw new InvalidOperationException("奖励依赖缺少逻辑节点。");
            return result;
        }

        private static Dictionary<string, string> ReadParents(Dictionary<string, LevelNode> nodes)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (LevelNode node in nodes.Values)
            {
                List<string> ids = node.prerequisiteNodeIds;
                if (ids != null && ids.Count > 1)
                    throw new InvalidOperationException("节点最多只能有一个前置：" + node.contentId);
                string parent = ids != null && ids.Count == 1 ? ids[0] : null;
                if (ids != null && ids.Count == 1 && (string.IsNullOrWhiteSpace(parent) || !nodes.ContainsKey(parent)))
                    throw new InvalidOperationException("节点前置不存在：" + node.contentId + " -> " + parent);
                result.Add(node.contentId, parent);
            }
            return result;
        }

        private static List<RewardCombatStrength> ReadStrengths(
            IEnumerable<RewardCombatStrength> input, Dictionary<string, LevelNode> nodes)
        {
            var result = new Dictionary<string, RewardCombatStrength>(StringComparer.Ordinal);
            foreach (RewardCombatStrength item in input)
            {
                LevelNode node;
                if (item == null || string.IsNullOrWhiteSpace(item.NodeId) ||
                    !nodes.TryGetValue(item.NodeId, out node) || !IsCombat(node))
                    throw new InvalidOperationException("战斗强度引用不存在或非战斗的逻辑节点。");
                if (double.IsNaN(item.Score) || double.IsInfinity(item.Score) || item.Score <= 0)
                    throw new InvalidOperationException("战斗强度必须是有限正数：" + item.NodeId);
                RewardCombatStrength previous;
                if (result.TryGetValue(item.NodeId, out previous))
                {
                    if (previous.IsBoss != item.IsBoss || previous.Score != item.Score)
                        throw new InvalidOperationException("同一逻辑战斗存在冲突的强度：" + item.NodeId);
                    continue; // Multiple visuals or repeated input never create additional combat slots.
                }
                result.Add(item.NodeId, item);
            }
            foreach (LevelNode node in nodes.Values.Where(IsCombat))
                if (!result.ContainsKey(node.contentId))
                    throw new InvalidOperationException("逻辑战斗缺少强度数据：" + node.contentId);
            return result.Values.OrderBy(s => s.Score).ThenBy(s => s.NodeId, StringComparer.Ordinal).ToList();
        }

        private static bool IsCombat(LevelNode node)
        {
            return node.nodeType == LevelNodeType.Encounter || node.nodeType == LevelNodeType.Finale;
        }

        private static void ValidateTerminationContract(
            IEnumerable<LevelNode> nodes, IReadOnlyList<RewardCombatStrength> bosses, IReadOnlyList<LevelNode> exits)
        {
            LevelNode[] finales = nodes.Where(n => n.nodeType == LevelNodeType.Finale).ToArray();
            if (finales.Length > 0)
            {
                // Finale keeps its combat settlement contract. It must never become an ordinary
                // reward target, an invented map Exit, or a second interpretation of floor completion.
                if (finales.Length != 1 || bosses.Count != 1 || bosses[0].NodeId != finales[0].contentId || exits.Count != 0)
                    throw new InvalidOperationException("终层奖励依赖只允许唯一 Finale 对应唯一 Boss 且没有 Exit，不能混用多个 Boss、Finale 或出口。");
                return;
            }
            if ((bosses.Count > 0 || exits.Count > 0) && (bosses.Count != 1 || exits.Count != 1))
                throw new InvalidOperationException("奖励依赖要求一名楼层 Encounter Boss 对应一个固定灵玉 Exit，不能猜测多 Boss 或多出口的配对。");
        }

        private static RewardRarity RewardRarityOf(LevelNode node)
        {
            if (node.completionEffects == null || node.completionEffects.Count == 0)
                throw new InvalidOperationException("奖励节点没有实际 completionEffects：" + node.contentId);
            RewardRarity rarity = RewardRarity.Common;
            foreach (LevelInteractionEffect effect in node.completionEffects)
            {
                if (effect == null || !Enum.IsDefined(typeof(RewardRarity), effect.rarity))
                    throw new InvalidOperationException("奖励节点包含空效果或无效 rarity：" + node.contentId);
                if (effect.rarity > rarity) rarity = effect.rarity;
            }
            return rarity;
        }

        private static int FixedExitJade(LevelNode exit)
        {
            if (exit.completionEffects == null || exit.completionEffects.Count != 1)
                throw new InvalidOperationException("Boss 出口必须只配置一项固定 GrantJade：" + exit.contentId);
            LevelInteractionEffect effect = exit.completionEffects[0];
            if (effect == null || effect.kind != LevelInteractionEffectKind.GrantJade || effect.amount <= 0 ||
                (effect.amountMax != 0 && effect.amountMax != effect.amount))
                throw new InvalidOperationException("Boss 出口必须发放固定正数灵玉，不能随机或附带其它奖励：" + exit.contentId);
            return effect.amount;
        }

        private static bool TryAssign(
            List<LevelNode> rewards, List<RewardCombatStrength> combats,
            Dictionary<string, string> parents, RewardCombatStrength[] chosen,
            int index, HashSet<string> used, bool distinct)
        {
            if (index == rewards.Count) return true;
            LevelNode reward = rewards[index];
            // Spread multiple rewards over the full strength range; one reward uses its actual rarity rank.
            double desired = rewards.Count == 1
                ? (int)RewardRarityOf(reward) * (combats.Count - 1) / (double)(int)RewardRarity.UltraRare
                : index * (combats.Count - 1) / (double)(rewards.Count - 1);
            IEnumerable<int> order = Enumerable.Range(0, combats.Count)
                .OrderBy(i => Math.Abs(i - desired)).ThenBy(i => i);
            foreach (int combatIndex in order)
            {
                RewardCombatStrength combat = combats[combatIndex];
                if ((distinct && used.Contains(combat.NodeId)) ||
                    (index > 0 && combat.Score < chosen[index - 1].Score) ||
                    Reaches(parents, combat.NodeId, reward.contentId)) continue;
                parents[reward.contentId] = combat.NodeId;
                chosen[index] = combat;
                bool newlyUsed = used.Add(combat.NodeId);
                if (TryAssign(rewards, combats, parents, chosen, index + 1, used, distinct)) return true;
                if (newlyUsed) used.Remove(combat.NodeId);
                parents[reward.contentId] = null;
                chosen[index] = null;
            }
            return false;
        }

        private static bool Reaches(Dictionary<string, string> parents, string start, string target)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (string current = start; current != null; current = parents[current])
            {
                if (current == target) return true;
                if (!visited.Add(current)) throw new InvalidOperationException("奖励依赖存在前置循环：" + current);
            }
            return false;
        }

        private static void EnsureAcyclic(Dictionary<string, string> parents)
        {
            foreach (string id in parents.Keys)
                if (parents[id] != null && Reaches(parents, parents[id], id))
                    throw new InvalidOperationException("奖励依赖存在前置循环：" + id);
        }
    }
}
