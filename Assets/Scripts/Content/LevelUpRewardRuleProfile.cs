using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    [Serializable]
    public sealed class LevelRewardCostWeightData
    {
        public int rewardPoolCost;
        public float weight;
    }

    [Serializable]
    public sealed class LevelRewardStageData
    {
        public int minLevel = 1;
        public int maxLevel = int.MaxValue;
        public List<string> cardContentIds = new List<string>();
        public List<LevelRewardCostWeightData> costWeights = new List<LevelRewardCostWeightData>();
    }

    /// <summary>
    /// 一套可复用的升级奖励卡池规则。该配置只保存稳定内容 ID
    /// 和抽样合同，不持有局内牌组或奖励状态。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewLevelUpRewardRule",
        menuName = "万界长卷/升级奖励规则")]
    public sealed class LevelUpRewardRuleProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        [Tooltip("稳定奖励规则 ID。")]
        public string contentId;
        [Tooltip("允许进入该奖励池的卡牌稳定 contentId。")]
        public List<string> cardContentIds = new List<string>();
        [Min(1), Tooltip("必须声明的不同卡牌数；自然模式允许资格筛选缩池，但仍须至少 maxChoiceCount 张合法卡牌。")]
        public int requiredCardCount = 3;
        [Min(1)] public int baseChoiceCount = 3;
        [Min(1)] public int maxChoiceCount = 3;
        public bool luckAffectsChoiceCount;
        [Range(0f, 1f)] public float cardRewardChance = 1f;
        public bool uniqueCardChoices = true;

        [Tooltip("显式启用自然卡牌/灵印混合池；旧资源缺省为 false，仍使用严格白名单。")]
        public bool useNaturalCardSealPool;
        [Range(0f, 1f)] public float naturalSealOfferChance = 0.4f;
        [Tooltip("cardContentIds 中无需账号解锁的基础子集；其余候选必须在 unlockedCards 中。仍需通过发布资格。")]
        public List<string> naturalBaseCardContentIds = new List<string>();
        [Tooltip("自然奖励允许的灵印稳定 ID；仅已注册、账号已解锁且有普通品阶权重者入池。空表不提供灵印。")]
        public List<string> naturalSealContentIds = new List<string>();
        [Tooltip("独立启用共享普通资格和等级费用抽样；不改变旧自然卡印模式。")]
        public bool useLevelCostPolicy;
        [Tooltip("唯一全局普通奖励目录来自作者 ordinaryRewardEnabled，不维护关卡或 Profile 卡牌白名单。")]
        public bool useGlobalOrdinaryCatalog;
        public int levelCostPolicyVersion = 1;
        public List<LevelRewardStageData> levelCostStages = new List<LevelRewardStageData>();

        public LevelCostRewardPolicy CreateLevelCostPolicy()
            => CreateLevelCostPolicy(null);

        /// <summary>
        /// Global membership is projected from author admission levels; qualification is
        /// applied per request by Progression. The parameterless overload validates the
        /// weights without pretending that a runtime ordinary directory was supplied.
        /// </summary>
        public LevelCostRewardPolicy CreateLevelCostPolicy(IReadOnlyList<CardConfig> cards)
        {
            if (!useLevelCostPolicy) return null;
            if (useGlobalOrdinaryCatalog && cards != null)
            {
                List<string> errors = ValidateOrdinaryCatalog(cards);
                if (errors.Count > 0)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }
            var stages = new List<LevelCostRewardStage>();
            foreach (LevelRewardStageData stage in levelCostStages ?? new List<LevelRewardStageData>())
            {
                if (stage == null) throw new InvalidOperationException("等级奖励段不能为空。");
                var weights = new List<LevelRewardCostWeight>();
                foreach (LevelRewardCostWeightData weight in stage.costWeights ?? new List<LevelRewardCostWeightData>())
                {
                    if (weight == null) throw new InvalidOperationException("奖励费用权重不能为空。");
                    weights.Add(new LevelRewardCostWeight(weight.rewardPoolCost, weight.weight));
                }
                IReadOnlyList<string> members = stage.cardContentIds;
                if (useGlobalOrdinaryCatalog)
                {
                    var projected = new List<string>();
                    if (cards == null)
                        projected.Add("card.internal.global-policy-validation");
                    else
                        foreach (CardConfig card in cards)
                            if (card != null && card.ordinaryRewardEnabled &&
                                card.rewardMinPlayerLevel <= stage.maxLevel)
                                projected.Add(card.contentId);
                    members = projected;
                }
                stages.Add(new LevelCostRewardStage(stage.minLevel, stage.maxLevel,
                    members, weights));
            }
            var policy = new LevelCostRewardPolicy(contentId, levelCostPolicyVersion, stages);
            if (useGlobalOrdinaryCatalog)
            {
                int nextLevel = 1;
                for (int i = 0; i < policy.Stages.Count; i++)
                {
                    LevelCostRewardStage stage = policy.Stages[i];
                    if (stage.MinLevel != nextLevel)
                        throw new InvalidOperationException("全局奖励等级段必须从Lv1连续覆盖，不能缺级。");
                    if (stage.MaxLevel == int.MaxValue)
                    {
                        if (i != policy.Stages.Count - 1)
                            throw new InvalidOperationException("全局奖励的无限末段必须是最后一段。");
                        return policy;
                    }
                    nextLevel = stage.MaxLevel + 1;
                }
                throw new InvalidOperationException("全局奖励等级段必须覆盖到int.MaxValue。");
            }
            return policy;
        }

        /// <summary>
        /// Validates every opted-in author object before release or account filters.
        /// Payment cost and permanent-unlock conditions do not determine reward admission.
        /// </summary>
        public List<string> ValidateOrdinaryCatalog(IReadOnlyList<CardConfig> cards)
        {
            var errors = new List<string>();
            if (cards == null)
            {
                errors.Add("普通奖励作者目录不能为空引用。");
                return errors;
            }
            LevelCostRewardPolicy policy;
            try
            {
                policy = CreateLevelCostPolicy();
                if (policy == null)
                {
                    errors.Add("普通奖励作者目录需要等级费用策略。");
                    return errors;
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
                return errors;
            }
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardConfig card in cards)
            {
                if (card == null || !card.ordinaryRewardEnabled) continue;
                string id = card.contentId;
                if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || !ids.Add(id))
                    errors.Add($"普通奖励作者目录含空、非稳定或重复contentId：{id}。");
                if (card.rewardMinPlayerLevel < 1)
                    errors.Add($"普通奖励 {id} 的rewardMinPlayerLevel必须至少为1。");
                if (card.rewardPoolCost < 0)
                    errors.Add($"普通奖励 {id} 必须显式配置非负rewardPoolCost。");
                bool covered = false;
                foreach (LevelCostRewardStage stage in policy.Stages)
                {
                    if (stage.MaxLevel < card.rewardMinPlayerLevel) continue;
                    foreach (LevelRewardCostWeight weight in stage.CostWeights)
                        if (weight.RewardPoolCost == card.rewardPoolCost && weight.Weight > 0f)
                            covered = true;
                }
                if (!covered)
                    errors.Add($"普通奖励 {id} 从准入等级起没有匹配rewardPoolCost的正权重等级段。");
            }
            return errors;
        }

        public RewardConfig CreateRuntimeConfig()
        {
            return new RewardConfig
            {
                baseChoiceCount = Math.Max(1, baseChoiceCount),
                maxChoiceCount = Math.Max(baseChoiceCount, maxChoiceCount),
                luckAffectsChoiceCount = luckAffectsChoiceCount,
                cardRewardChance = Math.Max(0f,
                    Math.Min(1f, cardRewardChance)),
                uniqueCardChoices = uniqueCardChoices,
                useNaturalCardSealPool = useNaturalCardSealPool,
                naturalSealOfferChance = naturalSealOfferChance
            };
        }

        public List<string> ValidateProfile()
        {
            var errors = new List<string>();
            if (schemaVersion != CurrentSchemaVersion)
                errors.Add($"schemaVersion 必须为 {CurrentSchemaVersion}。");
            if (string.IsNullOrWhiteSpace(contentId))
                errors.Add("升级奖励规则 contentId 不能为空。");
            if (baseChoiceCount <= 0 || maxChoiceCount < baseChoiceCount)
                errors.Add("升级奖励候选数配置不合法。");
            if (!useGlobalOrdinaryCatalog && requiredCardCount < maxChoiceCount)
                errors.Add("升级奖励必需卡牌数不能小于最大候选数。");

            var uniqueIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (!useGlobalOrdinaryCatalog && (cardContentIds == null || cardContentIds.Count == 0))
            {
                errors.Add("升级奖励卡池不能为空。");
                return errors;
            }

            for (int i = 0; i < (cardContentIds?.Count ?? 0); i++)
            {
                string cardContentId = cardContentIds[i];
                if (string.IsNullOrWhiteSpace(cardContentId))
                    errors.Add($"cardContentIds[{i}] 不能为空。");
                else if (!uniqueIds.Add(cardContentId))
                    errors.Add($"升级奖励卡牌 ID 重复：{cardContentId}。");
            }

            if (!useGlobalOrdinaryCatalog && uniqueCardChoices && uniqueIds.Count < maxChoiceCount)
            {
                errors.Add(
                    $"无放回抽样需要至少 {maxChoiceCount} 张不同卡牌，" +
                    $"当前只有 {uniqueIds.Count} 张。");
            }
            if (!useGlobalOrdinaryCatalog && requiredCardCount > 0 &&
                uniqueIds.Count != requiredCardCount)
            {
                errors.Add(
                    $"升级奖励规则必须配置 {requiredCardCount} 张不同卡牌，" +
                    $"当前只有 {uniqueIds.Count} 张。");
            }

            if (useNaturalCardSealPool)
            {
                if (!uniqueCardChoices)
                    errors.Add("自然升级奖励必须使用无放回抽样。");
                if (float.IsNaN(naturalSealOfferChance) ||
                    naturalSealOfferChance < 0f || naturalSealOfferChance > 1f)
                    errors.Add("自然升级灵印概率必须在 0 到 1 之间。");
                ValidateNaturalIds(naturalBaseCardContentIds,
                    nameof(naturalBaseCardContentIds), uniqueIds, errors);
                ValidateNaturalIds(naturalSealContentIds,
                    nameof(naturalSealContentIds), null, errors);
            }

            if (useLevelCostPolicy)
            {
                if (useNaturalCardSealPool && !useGlobalOrdinaryCatalog)
                    errors.Add("等级费用普通卡模式不能同时开启自然卡印模式。");
                if (!uniqueCardChoices || cardRewardChance != 1f)
                    errors.Add("等级费用普通奖励须为真实卡牌无放回抽样。");
                try
                {
                    LevelCostRewardPolicy policy = CreateLevelCostPolicy();
                    if (!useGlobalOrdinaryCatalog)
                        foreach (LevelCostRewardStage stage in policy.Stages)
                            foreach (string id in stage.CardContentIds)
                                if (!uniqueIds.Contains(id))
                                    errors.Add($"等级段成员不在共同普通范围：{id}。");
                }
                catch (Exception exception) { errors.Add(exception.Message); }
            }
            if (useGlobalOrdinaryCatalog)
            {
                if (!useLevelCostPolicy || !useNaturalCardSealPool)
                    errors.Add("全局普通奖励必须同时启用等级费用策略与自然卡印混合模式。");
                if ((cardContentIds?.Count ?? 0) > 0 || (naturalBaseCardContentIds?.Count ?? 0) > 0)
                    errors.Add("全局普通奖励不得声明cardContentIds或naturalBaseCardContentIds白名单。");
                foreach (LevelRewardStageData stage in levelCostStages ?? new List<LevelRewardStageData>())
                    if ((stage?.cardContentIds?.Count ?? 0) > 0)
                        errors.Add("全局等级段不得声明卡牌白名单，成员来自作者准入等级。");
            }
            return errors;
        }

        private static void ValidateNaturalIds(List<string> ids, string field,
            HashSet<string> allowedIds, List<string> errors)
        {
            if (ids == null) return;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id) || !unique.Add(id))
                    errors.Add($"{field} 含空 ID 或重复 ID：{id}。");
                else if (allowedIds != null && !allowedIds.Contains(id))
                    errors.Add($"{field} 不得包含卡池以外的 ID：{id}。");
            }
        }
    }

    /// <summary>按稳定 ID 加载升级奖励规则。</summary>
    public static class LevelUpRewardRuleProfileCatalog
    {
        private const string ResourceDirectory = "Content/LevelUpRewards";

        public static LevelUpRewardRuleProfile Get(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                return null;

            LevelUpRewardRuleProfile[] profiles =
                Resources.LoadAll<LevelUpRewardRuleProfile>(
                    ResourceDirectory);
            LevelUpRewardRuleProfile match = null;
            for (int i = 0; i < profiles.Length; i++)
            {
                LevelUpRewardRuleProfile profile = profiles[i];
                if (profile == null || !string.Equals(
                        profile.contentId,
                        contentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"升级奖励规则 contentId 重复：{contentId}。");
                }
                match = profile;
            }

            return match;
        }
    }

    /// <summary>已解析的升级奖励卡池及其 Core 抽样配置。</summary>
    public sealed class LevelUpRewardRuleResolution
    {
        public List<CardConfig> Cards { get; }
        public RewardConfig Config { get; }
        public NaturalLevelUpRewardPool NaturalPool { get; }

        public LevelUpRewardRuleResolution(
            List<CardConfig> cards,
            RewardConfig config,
            NaturalLevelUpRewardPool naturalPool = null)
        {
            Cards = cards ?? throw new ArgumentNullException(nameof(cards));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            NaturalPool = naturalPool;
        }
    }

    /// <summary>
    /// 将奖励规则中的稳定 ID 解析为局内可用卡池，并在进入
    /// Core 前拦截缺失、重复、未完成验收或未达最低候选数的配置。
    /// </summary>
    public static class LevelUpRewardRuleResolver
    {
        public static LevelUpRewardRuleResolution Resolve(
            LevelUpRewardRuleProfile profile,
            IReadOnlyList<CardConfig> cardPool,
            Func<string, bool> isEligible)
        {
            if (profile == null)
                return ResolveLegacy(cardPool);
            if (cardPool == null)
                throw new ArgumentNullException(nameof(cardPool));
            if (profile.useNaturalCardSealPool)
                throw new InvalidOperationException(
                    "自然升级规则必须经 Progression 的 NaturalLevelUpRewardCatalog.Resolve 解析账号与发布资格。");

            List<string> errors = profile.ValidateProfile();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"升级奖励规则 {profile.contentId} 不合法：" +
                    Environment.NewLine + string.Join(Environment.NewLine, errors));
            }

            var byId = new Dictionary<string, CardConfig>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cardPool.Count; i++)
            {
                CardConfig card = cardPool[i];
                if (card != null && !string.IsNullOrWhiteSpace(card.contentId))
                    byId[card.contentId] = card;
            }

            var cards = new List<CardConfig>(profile.cardContentIds.Count);
            var ineligibleContentIds = new List<string>();
            for (int i = 0; i < profile.cardContentIds.Count; i++)
            {
                string cardContentId = profile.cardContentIds[i];
                if (!byId.TryGetValue(cardContentId, out CardConfig card))
                {
                    throw new InvalidOperationException(
                        $"升级奖励规则 {profile.contentId} 缺少卡牌：" +
                        $"{cardContentId}。");
                }
                if (isEligible == null || isEligible(cardContentId))
                    cards.Add(card);
                else
                    ineligibleContentIds.Add(cardContentId);
            }

            // 显式白名单是正式内容合同，不是“可用几张算几张”
            // 的宽松候选集。任意列入卡牌未完成验收时，必须保持
            // 整个正式奖励规则阻断，避免悄然缩成五卡池。
            if (ineligibleContentIds.Count > 0)
            {
                throw new InvalidOperationException(
                    $"升级奖励规则 {profile.contentId} 包含尚未完成验收卡牌：" +
                    string.Join("、", ineligibleContentIds) + "。");
            }

            RewardConfig config = profile.CreateRuntimeConfig();
            if (cards.Count < config.baseChoiceCount)
            {
                throw new InvalidOperationException(
                    $"升级奖励规则 {profile.contentId} 需要至少 " +
                    $"{config.baseChoiceCount} 张已完成卡牌，当前只有 " +
                    $"{cards.Count} 张。");
            }

            return new LevelUpRewardRuleResolution(cards, config);
        }

        public static LevelUpRewardRuleResolution ResolveLegacy(
            IReadOnlyList<CardConfig> cardPool)
        {
            if (cardPool == null)
                throw new ArgumentNullException(nameof(cardPool));

            var cards = new List<CardConfig>();
            for (int i = cardPool.Count - 1;
                 i >= 0 && cards.Count < 3;
                 i--)
            {
                if (cardPool[i] != null && cardPool[i].manaCost >= 2)
                    cards.Add(cardPool[i]);
            }
            return new LevelUpRewardRuleResolution(
                cards,
                new RewardConfig());
        }
    }
}
