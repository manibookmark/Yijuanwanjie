using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Reward-pool cost and its sampling weight, independent of payment cost.</summary>
    public readonly struct LevelRewardCostWeight
    {
        public int RewardPoolCost { get; }
        public float Weight { get; }

        public LevelRewardCostWeight(int rewardPoolCost, float weight)
        {
            if (rewardPoolCost < 0) throw new ArgumentOutOfRangeException(nameof(rewardPoolCost));
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
                throw new ArgumentOutOfRangeException(nameof(weight));
            RewardPoolCost = rewardPoolCost;
            Weight = weight;
        }
    }

    /// <summary>One inclusive reached-level segment with explicit object membership.</summary>
    public sealed class LevelCostRewardStage
    {
        public int MinLevel { get; }
        public int MaxLevel { get; }
        public IReadOnlyList<string> CardContentIds { get; }
        public IReadOnlyList<LevelRewardCostWeight> CostWeights { get; }

        public LevelCostRewardStage(int minLevel, int maxLevel,
            IReadOnlyList<string> cardContentIds,
            IReadOnlyList<LevelRewardCostWeight> costWeights)
        {
            if (minLevel < 1 || maxLevel < minLevel)
                throw new ArgumentOutOfRangeException(nameof(minLevel));
            if (cardContentIds == null || cardContentIds.Count == 0)
                throw new ArgumentException("等级奖励段必须声明具体卡牌成员。", nameof(cardContentIds));
            if (costWeights == null || costWeights.Count == 0)
                throw new ArgumentException("等级奖励段必须声明奖励费用权重。", nameof(costWeights));
            var members = new List<string>(cardContentIds.Count);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in cardContentIds)
            {
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                    throw new ArgumentException("等级奖励段含空或重复卡牌ID。", nameof(cardContentIds));
                members.Add(id);
            }
            members.Sort(StringComparer.Ordinal);
            var weights = new List<LevelRewardCostWeight>(costWeights.Count);
            var costs = new HashSet<int>();
            bool hasPositiveWeight = false;
            foreach (LevelRewardCostWeight item in costWeights)
            {
                if (!costs.Add(item.RewardPoolCost))
                    throw new ArgumentException("等级奖励段含重复费用档。", nameof(costWeights));
                hasPositiveWeight |= item.Weight > 0f;
                weights.Add(item);
            }
            if (!hasPositiveWeight)
                throw new ArgumentException("等级奖励段没有正权重费用档。", nameof(costWeights));
            weights.Sort((a, b) => a.RewardPoolCost.CompareTo(b.RewardPoolCost));
            MinLevel = minLevel;
            MaxLevel = maxLevel;
            CardContentIds = members.AsReadOnly();
            CostWeights = weights.AsReadOnly();
        }
    }

    /// <summary>
    /// Owns the ordinary-card sampling policy: exact segment membership, independent
    /// reward costs and cost-first sampling without replacement. Qualification is a
    /// caller-provided snapshot; this module never writes account, deck or offer state.
    /// Empty and zero-weight buckets cannot supply fallback cards.
    /// </summary>
    public sealed class LevelCostRewardPolicy
    {
        public string ContentId { get; }
        public int Version { get; }
        public IReadOnlyList<LevelCostRewardStage> Stages { get; }

        public LevelCostRewardPolicy(string contentId, int version,
            IReadOnlyList<LevelCostRewardStage> stages)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("等级奖励规则缺少稳定ID。", nameof(contentId));
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            if (stages == null || stages.Count == 0)
                throw new ArgumentException("等级奖励规则没有等级段。", nameof(stages));
            var copy = new List<LevelCostRewardStage>(stages.Count);
            foreach (LevelCostRewardStage stage in stages)
            {
                if (stage == null) throw new ArgumentException("等级奖励段为空。", nameof(stages));
                copy.Add(stage);
            }
            copy.Sort((a, b) => a.MinLevel.CompareTo(b.MinLevel));
            for (int i = 1; i < copy.Count; i++)
                if (copy[i].MinLevel <= copy[i - 1].MaxLevel)
                    throw new ArgumentException("等级奖励段不能重叠。", nameof(stages));
            ContentId = contentId;
            Version = version;
            Stages = copy.AsReadOnly();
        }

        /// <summary>Bind an unversioned request, or reject a different frozen rule.</summary>
        public LevelUpRewardRequest BindRequest(LevelUpRewardRequest request)
        {
            if (request.ReachedLevel < 1)
                throw new InvalidOperationException("等级费用奖励缺少实际到达等级。");
            if ((!string.IsNullOrWhiteSpace(request.RuleContentId) || request.RuleVersion != 0) &&
                (!string.Equals(request.RuleContentId, ContentId, StringComparison.Ordinal) ||
                 request.RuleVersion != Version))
                throw new InvalidOperationException("奖励请求的规则ID或版本与当前策略不一致。");
            return new LevelUpRewardRequest(request.ReachedLevel, request.Source,
                ContentId, Version, request.IsReroll);
        }

        public IReadOnlyList<CardConfig> RollCards(LevelUpRewardRequest request,
            IReadOnlyList<CardConfig> qualifiedCards, IRandomSource random, int count)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            CardDraw draw = Prepare(request, qualifiedCards, count);
            var result = new List<CardConfig>(count);
            for (int i = 0; i < count; i++) result.Add(draw.Draw(random));
            return result.AsReadOnly();
        }

        internal CardDraw Prepare(LevelUpRewardRequest request,
            IReadOnlyList<CardConfig> qualifiedCards, int requiredCount)
        {
            request = BindRequest(request);
            if (qualifiedCards == null) throw new ArgumentNullException(nameof(qualifiedCards));
            if (requiredCount < 1) throw new ArgumentOutOfRangeException(nameof(requiredCount));
            LevelCostRewardStage stage = null;
            foreach (LevelCostRewardStage candidate in Stages)
                if (request.ReachedLevel >= candidate.MinLevel && request.ReachedLevel <= candidate.MaxLevel)
                { stage = candidate; break; }
            if (stage == null)
                throw new InvalidOperationException($"等级 {request.ReachedLevel} 没有声明普通奖励段。");
            var ids = new HashSet<string>(stage.CardContentIds, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var buckets = new List<CostBucket>(stage.CostWeights.Count);
            foreach (LevelRewardCostWeight weight in stage.CostWeights)
                if (weight.Weight > 0f) buckets.Add(new CostBucket(weight.RewardPoolCost, weight.Weight));
            foreach (CardConfig card in qualifiedCards)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.contentId) || card.IsEncounterTemporary ||
                    !seen.Add(card.contentId))
                    throw new InvalidOperationException("普通奖励资格投影含空项、临时牌或重复ID。");
                if (!ids.Contains(card.contentId)) continue;
                if (card.rewardPoolCost < 0)
                    throw new InvalidOperationException($"卡牌 {card.contentId} 未声明独立奖励费用。");
                foreach (CostBucket bucket in buckets)
                    if (bucket.Cost == card.rewardPoolCost) { bucket.Cards.Add(card); break; }
            }
            int count = 0;
            foreach (CostBucket bucket in buckets)
            {
                bucket.Cards.Sort((a, b) => StringComparer.Ordinal.Compare(a.contentId, b.contentId));
                count += bucket.Cards.Count;
            }
            if (count < requiredCount)
                throw new InvalidOperationException(
                    $"等级 {request.ReachedLevel} 的正权重费用档只有 {count} 张不同合法卡牌，需要 {requiredCount} 张。");
            return new CardDraw(buckets);
        }

        internal sealed class CostBucket
        {
            internal readonly int Cost;
            internal readonly float Weight;
            internal readonly List<CardConfig> Cards = new List<CardConfig>();
            internal CostBucket(int cost, float weight) { Cost = cost; Weight = weight; }
        }

        /// <summary>Private per-generation draw state; never survives into a pending offer.</summary>
        internal sealed class CardDraw
        {
            private readonly List<CostBucket> _buckets;
            internal CardDraw(List<CostBucket> buckets) { _buckets = buckets; }

            internal CardConfig Draw(IRandomSource random)
            {
                double total = 0d;
                foreach (CostBucket bucket in _buckets)
                    if (bucket.Cards.Count > 0) total += bucket.Weight;
                if (total <= 0d) throw new InvalidOperationException("普通奖励没有剩余的正权重费用档。");
                double roll = random.Value01() * total;
                CostBucket selected = null;
                foreach (CostBucket bucket in _buckets)
                {
                    if (bucket.Cards.Count == 0) continue;
                    selected = bucket;
                    roll -= bucket.Weight;
                    if (roll < 0d) break;
                }
                int index = random.Range(0, selected.Cards.Count);
                CardConfig card = selected.Cards[index];
                selected.Cards.RemoveAt(index);
                return card;
            }
        }
    }
}
