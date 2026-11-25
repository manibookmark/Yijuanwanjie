using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Resolves authored reward boundaries against read-only account/release eligibility.
    /// No unlocks, Run state or offers are committed here. Core owns sampling and the
    /// caller supplies live seal-host eligibility when rolling the returned pool.
    /// </summary>
    public static class NaturalLevelUpRewardCatalog
    {
        /// <summary>Formal combat, map experience and simulations share one rule.
        /// A level never chooses an ordinary reward whitelist or fallback policy.</summary>
        public static LevelUpRewardRuleResolution ResolveGlobal(
            IReadOnlyList<CardConfig> cardPool, string volumeId,
            MetaProgression account, Func<string, bool> isEligible,
            IEnumerable<string> excludedCardContentIds = null, CardUnlockRunContext runContext = null)
            => Resolve(GlobalLevelUpRewardRuleProfileCatalog.GetRequired(),
                cardPool, volumeId, account, isEligible, excludedCardContentIds, runContext);

        /// <summary>Permanent qualification is independent of manual publication.</summary>
        public static CardUnlockRequirements ResolveUnlockRequirements(IReadOnlyList<CardConfig> cardPool)
        {
            if (cardPool == null) throw new ArgumentNullException(nameof(cardPool));
            var levels = new List<CardUnlockLevelRequirement>();
            var behaviors = new List<CardUnlockBehaviorRequirement>();
            foreach (CardConfig card in cardPool)
            {
                if (card == null || !card.ordinaryRewardEnabled) continue;
                if (card.rewardUnlockPlayerLevel > 0)
                    levels.Add(new CardUnlockLevelRequirement(card.contentId, card.rewardUnlockPlayerLevel,
                        card.rewardUnlockCrawlerContentId));
                if (card.rewardUnlockCondition?.IsConfigured == true)
                    behaviors.Add(new CardUnlockBehaviorRequirement(card.contentId, card.rewardUnlockCondition));
            }
            return new CardUnlockRequirements(levels, behaviors);
        }

        /// <summary>Project the published ordinary-card level conditions without
        /// hardcoding card names or IDs in scene and persistence adapters.</summary>
        public static IReadOnlyList<CardUnlockLevelRequirement> ResolveLevelUnlockRequirements(
            IReadOnlyList<CardConfig> cardPool, Func<string, bool> isEligible)
        {
            if (cardPool == null) throw new ArgumentNullException(nameof(cardPool));
            if (isEligible == null) throw new ArgumentNullException(nameof(isEligible));
            var result = new List<CardUnlockLevelRequirement>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardConfig card in cardPool)
            {
                if (card == null || !card.ordinaryRewardEnabled || card.rewardUnlockPlayerLevel <= 0)
                    continue;
                if (card.rewardPoolCost < 0 || !ids.Add(card.contentId))
                    throw new InvalidOperationException("等级解锁作者配置缺失费用或包含重复ID。");
                if (isEligible(card.contentId))
                    result.Add(new CardUnlockLevelRequirement(card.contentId, card.rewardUnlockPlayerLevel,
                        card.rewardUnlockCrawlerContentId));
            }
            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.CardContentId, right.CardContentId));
            return result;
        }

        /// <param name="cardPool">Complete current-volume + common definitions, including
        /// evolution links (ContentRuntimeFactory.CreateCardPool), not the player's deck.</param>
        /// <param name="isEligible">Required card publication gate in natural mode;
        /// seal definitions are resolved separately through LingyinCatalog.</param>
        public static LevelUpRewardRuleResolution Resolve(
            LevelUpRewardRuleProfile profile,
            IReadOnlyList<CardConfig> cardPool,
            string volumeId,
            MetaProgression account,
            Func<string, bool> isEligible,
            IEnumerable<string> excludedCardContentIds = null, CardUnlockRunContext runContext = null)
        {
            if (profile?.useLevelCostPolicy == true)
                return ResolveLevelCost(profile, cardPool, volumeId, account,
                    isEligible, excludedCardContentIds, runContext);
            // Old profiles retain their strict all-or-error contract, including null
            // profile fallback. New account/exclusion policy must not shrink G1/G2.
            if (profile == null || !profile.useNaturalCardSealPool)
                return LevelUpRewardRuleResolver.Resolve(profile, cardPool, isEligible);
            if (cardPool == null) throw new ArgumentNullException(nameof(cardPool));
            if (string.IsNullOrWhiteSpace(volumeId))
                throw new ArgumentException("自然升级需要当前卷 ID。", nameof(volumeId));
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (isEligible == null) throw new ArgumentNullException(nameof(isEligible));

            List<string> errors = profile.ValidateProfile();
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"自然升级规则 {profile.contentId} 不合法：" +
                    string.Join(Environment.NewLine, errors));

            var byId = new Dictionary<string, CardConfig>(StringComparer.OrdinalIgnoreCase);
            var evolutionResults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardConfig card in cardPool)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.contentId)) continue;
                if (!byId.TryAdd(card.contentId, card))
                    throw new InvalidOperationException($"自然升级输入卡池 ID 重复：{card.contentId}。");
                if (card.evolutionTarget != null)
                    evolutionResults.Add(card.evolutionTarget.contentId);
            }

            // Validate every declared ID before any eligibility filter: a locked,
            // unpublished or banished card must not conceal a broken author reference.
            foreach (string id in profile.cardContentIds)
                if (!byId.ContainsKey(id))
                    throw new InvalidOperationException(
                        $"自然升级规则 {profile.contentId} 缺少卡牌：{id}。");

            RewardConfig config = profile.CreateRuntimeConfig();
            List<LevelUpSealCandidate> seals = ResolveSeals(profile, account, config);
            var basic = IdSet(profile.naturalBaseCardContentIds);
            var unlocked = IdSet(account.unlockedCards);
            var excluded = IdSet(excludedCardContentIds);
            var cards = new List<CardConfig>();
            foreach (string id in profile.cardContentIds)
            {
                CardConfig card = byId[id];
                if (!IsOrdinaryCard(card, volumeId, evolutionResults) ||
                    excluded.Contains(id) ||
                    (!basic.Contains(id) && !unlocked.Contains(id)) ||
                    !isEligible(card.contentId)) continue;
                cards.Add(card);
            }

            // Even when a seal may replace a card, a full card-only offer must remain
            // possible if the current real deck has no legal seal host.
            if (cards.Count < config.maxChoiceCount)
                throw new InvalidOperationException(
                    $"自然升级规则 {profile.contentId} 需要至少 {config.maxChoiceCount} " +
                    $"张不同合法卡牌，账号/发布/排除筛选后只有 {cards.Count} 张。");
            cards.Sort((left, right) => StringComparer.Ordinal.Compare(left.contentId, right.contentId));
            return new LevelUpRewardRuleResolution(cards, config,
                new NaturalLevelUpRewardPool(cards, seals));
        }

        private static LevelUpRewardRuleResolution ResolveLevelCost(
            LevelUpRewardRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            string volumeId, MetaProgression account, Func<string, bool> isEligible,
            IEnumerable<string> excludedCardContentIds, CardUnlockRunContext runContext)
        {
            if (cardPool == null) throw new ArgumentNullException(nameof(cardPool));
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (isEligible == null) throw new ArgumentNullException(nameof(isEligible));
            if (string.IsNullOrWhiteSpace(volumeId))
                throw new ArgumentException("共同奖励需要当前卷ID。", nameof(volumeId));
            List<string> errors = profile.ValidateProfile();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            var byId = new Dictionary<string, CardConfig>(StringComparer.OrdinalIgnoreCase);
            var evolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardConfig card in cardPool)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.contentId)) continue;
                if (!byId.TryAdd(card.contentId, card))
                    throw new InvalidOperationException("普通奖励对象ID重复：" + card.contentId);
                if (card.evolutionTarget != null) evolved.Add(card.evolutionTarget.contentId);
            }
            var ordinary = new List<CardConfig>();
            if (profile.useGlobalOrdinaryCatalog)
            {
                foreach (CardConfig card in byId.Values)
                {
                    if (!card.ordinaryRewardEnabled) continue;
                    if (!IsOrdinaryCard(card, volumeId, evolved))
                        throw new InvalidOperationException("非普通对象不得声明普通奖励资格：" + card.contentId);
                    if (card.rewardUnlockCondition?.IsConfigured == true &&
                        (card.rewardUnlockCondition.Validate() != null || card.defaultRewardUnlocked || card.rewardUnlockPlayerLevel > 0))
                        throw new InvalidOperationException("普通奖励行为条件不合法：" + card.contentId);
                    ordinary.Add(card);
                }
                errors = profile.ValidateOrdinaryCatalog(ordinary);
                if (errors.Count > 0)
                    throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }
            else foreach (string id in profile.cardContentIds)
            {
                if (!byId.TryGetValue(id, out CardConfig card) ||
                    !card.ordinaryRewardEnabled || card.rewardPoolCost < 0 ||
                    !IsOrdinaryCard(card, volumeId, evolved))
                    throw new InvalidOperationException("共同普通奖励引用缺失或不合法：" + id);
                ordinary.Add(card);
            }
            ordinary.Sort((left, right) => StringComparer.Ordinal.Compare(left.contentId, right.contentId));
            // Frozen pre-migration offers retain their exact IDs. This compatibility
            // directory is never passed to new-offer eligibility or cost sampling.
            var directory = new List<CardConfig>(ordinary);
            if (profile.useGlobalOrdinaryCatalog)
            {
                var directoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (CardConfig card in ordinary) directoryIds.Add(card.contentId);
                foreach (string id in GlobalLevelUpRewardRuleProfileCatalog.GetLegacyFrozenCardContentIds())
                    if (byId.TryGetValue(id, out CardConfig card) &&
                        IsOrdinaryCard(card, volumeId, evolved) && directoryIds.Add(id))
                        directory.Add(card);
            }
            directory.Sort((left, right) => StringComparer.Ordinal.Compare(left.contentId, right.contentId));
            var excluded = IdSet(excludedCardContentIds);
            IReadOnlyList<CardConfig> Eligible(LevelUpRewardRequest request)
            {
                var eligible = new List<CardConfig>();
                // Capture once: Run conditions must not receive the projected lifetime
                // snapshot, and reward generation remains a read-only qualification.
                CardUnlockFactsSnapshot currentRunFacts = runContext?.Capture();
                CardUnlockFactsSnapshot globalFacts = runContext != null
                    ? account.CardUnlocks.ProjectRun(runContext.RunId, currentRunFacts)
                    : account.CardUnlocks.LifetimeFacts;
                foreach (CardConfig card in ordinary)
                    if ((!profile.useGlobalOrdinaryCatalog || request.ReachedLevel >= card.rewardMinPlayerLevel) &&
                        !excluded.Contains(card.contentId) && isEligible(card.contentId) &&
                        (card.defaultRewardUnlocked || account.IsCardUnlocked(card.contentId) ||
                         (card.rewardUnlockPlayerLevel > 0 &&
                          new CardUnlockLevelRequirement(card.contentId, card.rewardUnlockPlayerLevel,
                              card.rewardUnlockCrawlerContentId).IsSatisfied(account.CardUnlocks,
                                  request.ReachedLevel, runContext?.LeaderCrawlerContentId)) ||
                         (card.rewardUnlockCondition?.IsConfigured == true &&
                          card.rewardUnlockCondition.IsSatisfied(globalFacts, currentRunFacts))))
                        eligible.Add(card);
                return eligible;
            }
            RewardConfig config = profile.CreateRuntimeConfig();
            return new LevelUpRewardRuleResolution(directory, config,
                new NaturalLevelUpRewardPool(directory,
                    profile.useNaturalCardSealPool ? ResolveSeals(profile, account, config) :
                        new List<LevelUpSealCandidate>(),
                    profile.useGlobalOrdinaryCatalog ? profile.CreateLevelCostPolicy(ordinary) :
                        profile.CreateLevelCostPolicy(), Eligible));
        }

        private static bool IsOrdinaryCard(CardConfig card, string volumeId,
            HashSet<string> evolutionResults)
        {
            // Jin is the hero category; Yin is the curse/enemy-junk category.
            // Neither is an ordinary natural reward, even if an author lists it.
            if (card.color == CardColor.Jin || card.color == CardColor.Yin ||
                card.IsEncounterTemporary || evolutionResults.Contains(card.contentId) ||
                (!string.Equals(card.volumeId, volumeId, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(card.volumeId, "common", StringComparison.OrdinalIgnoreCase)))
                return false;
            if (card.effects != null)
                foreach (CardEffectSpec effect in card.effects)
                    if (effect != null && effect.effectType == EffectType.SummonCrawler)
                        return false;
            // Source metadata or a legacy frozen-offer directory explicitly nominates
            // ordinary definitions; structural exclusions apply to both.
            return true;
        }

        private static List<LevelUpSealCandidate> ResolveSeals(
            LevelUpRewardRuleProfile profile, MetaProgression account, RewardConfig config)
        {
            var candidates = new List<LevelUpSealCandidate>();
            if (profile.naturalSealContentIds == null || profile.naturalSealContentIds.Count == 0)
                return candidates;
            List<string> errors = LingyinCatalog.ValidateAll();
            if (errors.Count > 0)
                throw new InvalidOperationException("自然升级灵印目录不合法：" +
                    string.Join(Environment.NewLine, errors));

            bool canReceiveSeals = SealRewardEligibility.CanReceiveSeals(account);
            var unlocked = IdSet(account.unlockedSeals);
            foreach (string id in profile.naturalSealContentIds)
            {
                if (!LingyinCatalog.TryGet(id, out LingyinConfig seal))
                    throw new InvalidOperationException(
                        $"自然升级规则 {profile.contentId} 缺少灵印配置：{id}。");
                if (!Enum.IsDefined(typeof(LingyinGrade), seal.grade))
                    throw new InvalidOperationException($"自然升级灵印 {id} 品阶不合法。");
                // Grade names are shared vocabulary, not an assumption that two
                // independently serialized enum ordinals will forever coincide.
                var rarity = (RewardRarity)Enum.Parse(typeof(RewardRarity), seal.grade.ToString());
                var candidate = new LevelUpSealCandidate(seal.ToCardSealSpec(), rarity,
                    seal.displayName, seal.description);
                if (canReceiveSeals && unlocked.Contains(id) && config.GetWeight(rarity, 0, true) > 0f)
                    candidates.Add(candidate);
            }
            return candidates;
        }

        private static HashSet<string> IdSet(IEnumerable<string> ids)
            => new HashSet<string>(ids ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }
}
