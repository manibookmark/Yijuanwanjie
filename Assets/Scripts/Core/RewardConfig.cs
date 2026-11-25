using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    public enum RewardRarity
    {
        Common,
        Uncommon,
        Rare,
        VeryRare,
        UltraRare
    }

    [Serializable]
    public sealed class RarityWeightRule
    {
        public RewardRarity rarity;
        public float baseWeight;
        public float luckCoefficient;
    }

    /// <summary>天运白名单与奖励概率配置；只改变调用方提供的合法奖励池。</summary>
    [Serializable]
    public sealed class RewardConfig
    {
        public bool affectedByLuck = true;
        public bool luckAffectsRarity = true;
        public bool luckAffectsChoiceCount = true;
        public int baseChoiceCount = 3;
        public int maxChoiceCount = 4;
        public float fourChoiceLuckK = 100f;
        [Range(0f, 1f)] public float cardRewardChance = 0.6f;
        [Tooltip("同一次升级界面中，卡牌候选是否按 contentId 无放回抽样。")]
        public bool uniqueCardChoices;
        [Tooltip("显式使用自然卡牌／灵印池；不回退为旧属性奖励。")]
        public bool useNaturalCardSealPool;
        [Range(0f, 1f), Tooltip("每组自然升级候选出现一枚灵印的概率；其余候选为不同卡牌。")]
        public float naturalSealOfferChance = 0.4f;
        public List<RarityWeightRule> rarityWeights = CreateDefaultRarityWeights();

        public static List<RarityWeightRule> CreateDefaultRarityWeights()
        {
            return new List<RarityWeightRule>
            {
                new RarityWeightRule { rarity = RewardRarity.Common, baseWeight = 60f, luckCoefficient = 0f },
                new RarityWeightRule { rarity = RewardRarity.Uncommon, baseWeight = 30f, luckCoefficient = 0.035f },
                new RarityWeightRule { rarity = RewardRarity.Rare, baseWeight = 20f, luckCoefficient = 0.065f },
                new RarityWeightRule { rarity = RewardRarity.VeryRare, baseWeight = 6f, luckCoefficient = 0.30f },
                new RarityWeightRule { rarity = RewardRarity.UltraRare, baseWeight = 0f, luckCoefficient = 0f }
            };
        }

        public float GetWeight(RewardRarity rarity, int luck, bool isLegal)
        {
            if (!isLegal) return 0f;
            RarityWeightRule rule = rarityWeights?.Find(item => item != null && item.rarity == rarity);
            if (rule == null) return 0f;
            float bonus = affectedByLuck && luckAffectsRarity
                ? Math.Max(0, luck) * rule.luckCoefficient
                : 0f;
            return Math.Max(0f, rule.baseWeight + bonus);
        }

        public RewardRarity RollRarity(int luck, IReadOnlyList<bool> legalRarities, IRandomSource random)
        {
            float total = 0f;
            var weights = new float[5];
            for (int i = 0; i < weights.Length; i++)
            {
                bool legal = legalRarities == null || (i < legalRarities.Count && legalRarities[i]);
                weights[i] = GetWeight((RewardRarity)i, luck, legal);
                total += weights[i];
            }
            if (total <= 0f)
                throw new InvalidOperationException("合法奖励池没有可抽取的品阶权重。");

            float roll = (random?.Value01() ?? UnityEngine.Random.value) * total;
            int lastPositive = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (weights[i] > 0f) lastPositive = i;
                if (roll < 0f) return (RewardRarity)i;
            }
            return (RewardRarity)Math.Max(0, lastPositive);
        }

        public float GetFourChoiceChance(int luck)
        {
            if (!affectedByLuck || !luckAffectsChoiceCount || luck <= 0) return 0f;
            float k = Math.Max(0.0001f, fourChoiceLuckK);
            return luck / (luck + k);
        }

        public int RollChoiceCount(int luck, IRandomSource random)
        {
            if (maxChoiceCount <= baseChoiceCount) return baseChoiceCount;
            float roll = random?.Value01() ?? UnityEngine.Random.value;
            return roll < GetFourChoiceChance(luck) ? maxChoiceCount : baseChoiceCount;
        }
    }
}
