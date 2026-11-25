using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>An authored seal definition, detached from asset and offer mutation.</summary>
    public sealed class LevelUpSealCandidate
    {
        private readonly CardSealSpec _seal;
        public string ContentId => _seal.contentId;
        public RewardRarity Rarity { get; }
        public string Name { get; }
        public string Description { get; }

        public LevelUpSealCandidate(CardSealSpec seal, RewardRarity rarity,
            string name, string description)
        {
            if (seal == null || string.IsNullOrWhiteSpace(seal.contentId) ||
                seal.value <= 0 || !Enum.IsDefined(typeof(CardSealEffect), seal.effect))
                throw new ArgumentException("自然升级灵印缺少有效的稳定规格。", nameof(seal));
            if (!Enum.IsDefined(typeof(RewardRarity), rarity))
                throw new ArgumentOutOfRangeException(nameof(rarity));
            _seal = seal.Clone();
            Rarity = rarity;
            Name = name;
            Description = description;
        }

        public CardSealSpec CreateSpec() => _seal.Clone();

        internal LevelUpSystem.Reward CreateReward() => new LevelUpSystem.Reward
        {
            type = LevelUpSystem.RewardType.Gem,
            name = Name,
            description = Description,
            seal = CreateSpec()
        };
    }

    /// <summary>
    /// Owns natural-offer sampling: stable ordering, unique cards, weighted seal
    /// rarity and host filtering before RNG. Account/release eligibility belongs
    /// to the caller's qualification projection, optionally refreshed per request.
    /// An explicit level-cost policy changes ordinary sampling only. It stores no pending choice or
    /// deck state; callers freeze the returned offer in their existing transaction.
    /// </summary>
    public sealed class NaturalLevelUpRewardPool
    {
        private readonly List<CardConfig> _cards;
        private readonly List<LevelUpSealCandidate> _seals;
        private readonly Func<LevelUpRewardRequest, IReadOnlyList<CardConfig>> _eligibleCardsProvider;
        public LevelCostRewardPolicy LevelCostPolicy { get; }
        public int CardCount => _cards.Count;
        public int SealCount => _seals.Count;

        /// <summary>
        /// Revalidate a frozen choice against the resolved catalogue boundary.
        /// Mutable qualification affects new rolls; it never filters an already frozen group.
        /// </summary>
        public bool ContainsCard(string contentId)
            => !string.IsNullOrWhiteSpace(contentId) && _cards.Exists(card =>
                string.Equals(card.contentId, contentId, StringComparison.OrdinalIgnoreCase));

        public bool ContainsSeal(CardSealSpec seal)
        {
            if (seal == null) return false;
            foreach (LevelUpSealCandidate candidate in _seals)
            {
                if (!string.Equals(candidate.ContentId, seal.contentId, StringComparison.OrdinalIgnoreCase)) continue;
                CardSealSpec expected = candidate.CreateSpec();
                return seal.effect == expected.effect && seal.value == expected.value &&
                    seal.comboScalable == expected.comboScalable;
            }
            return false;
        }

        public NaturalLevelUpRewardPool(IReadOnlyList<CardConfig> cards,
            IReadOnlyList<LevelUpSealCandidate> seals,
            LevelCostRewardPolicy levelCostPolicy = null,
            Func<LevelUpRewardRequest, IReadOnlyList<CardConfig>> eligibleCardsProvider = null)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            _cards = new List<CardConfig>(cards.Count);
            _seals = new List<LevelUpSealCandidate>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardConfig card in cards)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.contentId) ||
                    card.IsEncounterTemporary || !ids.Add(card.contentId))
                    throw new ArgumentException("自然升级卡池含空项、临时牌或重复内容ID。", nameof(cards));
                _cards.Add(card);
            }
            ids.Clear();
            foreach (LevelUpSealCandidate seal in seals ?? Array.Empty<LevelUpSealCandidate>())
            {
                if (seal == null || !ids.Add(seal.ContentId))
                    throw new ArgumentException("自然升级灵印池含空项或重复内容ID。", nameof(seals));
                _seals.Add(seal);
            }
            _cards.Sort((a, b) => StringComparer.Ordinal.Compare(a.contentId, b.contentId));
            _seals.Sort((a, b) => StringComparer.Ordinal.Compare(a.ContentId, b.ContentId));
            LevelCostPolicy = levelCostPolicy;
            _eligibleCardsProvider = eligibleCardsProvider;
        }

        /// <summary>
        /// At most one seal replaces a card in the group. Lack of a legal seal
        /// leaves a complete card-only offer, never a stat/repeated-card fallback.
        /// The four-choice and seal-rate rolls are independent of reached level.
        /// </summary>
        public List<LevelUpSystem.Reward> Roll(RewardConfig config, int luck,
            IRandomSource random, int count, Func<CardSealSpec, bool> hasLegalSealHost,
            LevelUpRewardRequest request = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (hasLegalSealHost == null) throw new ArgumentNullException(nameof(hasLegalSealHost));
            if (config.baseChoiceCount <= 0 || config.maxChoiceCount < config.baseChoiceCount ||
                float.IsNaN(config.naturalSealOfferChance) ||
                config.naturalSealOfferChance < 0f || config.naturalSealOfferChance > 1f)
                throw new InvalidOperationException("自然升级抽样配置不合法。");
            int required = count > 0 ? count : config.maxChoiceCount;
            if (LevelCostPolicy != null) request = LevelCostPolicy.BindRequest(request);
            List<CardConfig> available = ResolveEligibleCards(request);
            LevelCostRewardPolicy.CardDraw costDraw =
                LevelCostPolicy?.Prepare(request, available, required);
            if (available.Count < required)
                throw new InvalidOperationException(
                    $"自然升级需要至少 {required} 张不同合法卡牌，当前只有 {available.Count} 张。");

            var legalSeals = new List<LevelUpSealCandidate>();
            var legalRarities = new bool[5];
            foreach (LevelUpSealCandidate seal in _seals)
            {
                // Zero-weight UltraRare cannot leak in via host filtering or
                // the last-element fallback of a weighted sampler.
                if (config.GetWeight(seal.Rarity, luck, true) <= 0f ||
                    !hasLegalSealHost(seal.CreateSpec())) continue;
                legalSeals.Add(seal);
                legalRarities[(int)seal.Rarity] = true;
            }
            if (count <= 0) count = config.RollChoiceCount(luck, random);
            LevelUpSealCandidate chosenSeal = null;
            if (legalSeals.Count > 0 && config.naturalSealOfferChance > 0f &&
                random.Value01() < config.naturalSealOfferChance)
            {
                RewardRarity rarity = config.RollRarity(luck, legalRarities, random);
                List<LevelUpSealCandidate> inRarity = legalSeals.FindAll(s => s.Rarity == rarity);
                chosenSeal = inRarity[random.Range(0, inRarity.Count)];
            }
            int sealIndex = chosenSeal == null ? -1 : random.Range(0, count);
            var remaining = available;
            var result = new List<LevelUpSystem.Reward>(count);
            for (int i = 0; i < count; i++)
            {
                if (i == sealIndex)
                {
                    result.Add(chosenSeal.CreateReward());
                    continue;
                }
                CardConfig card;
                if (costDraw != null) card = costDraw.Draw(random);
                else
                {
                    int index = random.Range(0, remaining.Count);
                    card = remaining[index];
                    remaining.RemoveAt(index);
                }
                result.Add(new LevelUpSystem.Reward
                {
                    type = LevelUpSystem.RewardType.NewCard,
                    name = card.cardName,
                    description = card.description,
                    card = card
                });
            }
            return result;
        }

        private List<CardConfig> ResolveEligibleCards(LevelUpRewardRequest request)
        {
            if (_eligibleCardsProvider == null) return new List<CardConfig>(_cards);
            IReadOnlyList<CardConfig> qualified = _eligibleCardsProvider(request);
            if (qualified == null)
                throw new InvalidOperationException("普通奖励资格查询没有返回候选快照。");
            var result = new List<CardConfig>(qualified.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardConfig card in qualified)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.contentId) || !seen.Add(card.contentId))
                    throw new InvalidOperationException("普通奖励资格查询含空项或重复ID。");
                CardConfig authored = _cards.Find(candidate => string.Equals(candidate.contentId,
                    card.contentId, StringComparison.OrdinalIgnoreCase));
                if (authored == null)
                    throw new InvalidOperationException($"卡牌 {card.contentId} 超出正式普通奖励目录。");
                result.Add(authored);
            }
            result.Sort((a, b) => StringComparer.Ordinal.Compare(a.contentId, b.contentId));
            return result;
        }
    }
}
