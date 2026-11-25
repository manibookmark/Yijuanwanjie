using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// The one Content-to-runtime adapter for deterministic level-up offers.
    /// Live combat, map transactions and simulation receive the same resolved
    /// schedule. Progression owns this immutable cross-module projection; Core
    /// and Dungeon retain their separate authoritative reward transactions.
    /// </summary>
    public static class LevelUpOfferScheduleAdapter
    {
        public static ResolvedLevelUpOfferSchedule ResolveForFloor(
            LevelRunProfile floor,
            IReadOnlyList<CardConfig> cards,
            Func<string, bool> isEligible,
            Func<bool> canReceiveSeals = null)
        {
            if (floor == null) throw new ArgumentNullException(nameof(floor));
            string id = floor.levelUpOfferScheduleProfileId;
            if (string.IsNullOrWhiteSpace(id)) return null;
            LevelUpOfferScheduleProfile profile = LevelUpOfferScheduleProfileCatalog.Get(id);
            if (profile == null)
                throw new InvalidOperationException("Missing fixed level-up offer schedule: " + id);
            return Resolve(profile, cards, isEligible, canReceiveSeals);
        }

        public static ResolvedLevelUpOfferSchedule Resolve(
            LevelUpOfferScheduleProfile profile,
            IReadOnlyList<CardConfig> cards,
            Func<string, bool> isEligible,
            Func<bool> canReceiveSeals = null)
        {
            if (profile == null)
                return null;
            if (cards == null)
                throw new ArgumentNullException(nameof(cards));

            List<string> profileErrors = profile.ValidateProfile();
            if (profileErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"固定升级 Offer 排程 {profile.contentId} 不合法：" +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, profileErrors));
            }

            var cardsById = new Dictionary<string, CardConfig>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cards.Count; i++)
            {
                CardConfig card = cards[i];
                if (card == null || string.IsNullOrWhiteSpace(card.contentId))
                    continue;
                if (!cardsById.TryAdd(card.contentId, card))
                {
                    throw new InvalidOperationException(
                        $"固定升级 Offer 排程 {profile.contentId} 的卡池 " +
                        $"存在重复 contentId：{card.contentId}。" );
                }
            }

            var entries = new Dictionary<int, ResolvedLevelUpOfferEntry>();
            for (int entryIndex = 0;
                 entryIndex < profile.offers.Count;
                 entryIndex++)
            {
                LevelUpOfferScheduleEntry source =
                    profile.offers[entryIndex];
                var combatRewards = new List<LevelUpSystem.Reward>(
                    source.candidates.Count);
                var dungeonCandidates = new List<
                    DungeonRunLevelRewardCandidate>(
                    source.candidates.Count);
                for (int candidateIndex = 0;
                     candidateIndex < source.candidates.Count;
                     candidateIndex++)
                {
                    LevelUpOfferScheduleCandidate candidate =
                        source.candidates[candidateIndex];
                    switch (candidate.kind)
                    {
                        case LevelUpOfferScheduleCandidateKind.Card:
                            if (!cardsById.TryGetValue(
                                    candidate.contentId,
                                    out CardConfig card))
                            {
                                throw new InvalidOperationException(
                                    $"固定升级 Offer {source.offerContentId} " +
                                    $"缺少卡牌：{candidate.contentId}。" );
                            }
                            if (isEligible != null &&
                                !isEligible(candidate.contentId))
                            {
                                throw new InvalidOperationException(
                                    $"固定升级 Offer {source.offerContentId} " +
                                    $"引用了尚未完成验收的卡牌：" +
                                    $"{candidate.contentId}。" );
                            }
                            combatRewards.Add(new LevelUpSystem.Reward
                            {
                                type = LevelUpSystem.RewardType.NewCard,
                                name = card.cardName,
                                description = card.description,
                                card = card
                            });
                            dungeonCandidates.Add(
                                new DungeonRunLevelRewardCandidate(
                                    DungeonRunLevelRewardCandidateKind.Card,
                                    candidate.contentId));
                            break;

                        case LevelUpOfferScheduleCandidateKind.Seal:
                            if (!LingyinCatalog.TryGet(
                                    candidate.contentId,
                                    out LingyinConfig seal) ||
                                seal == null)
                            {
                                throw new InvalidOperationException(
                                    $"固定升级 Offer {source.offerContentId} " +
                                    $"缺少灵印：{candidate.contentId}。" );
                            }
                            if (!seal.IsValid(out string sealError))
                            {
                                throw new InvalidOperationException(
                                    $"固定升级 Offer {source.offerContentId} " +
                                    $"的灵印无效：{sealError}" );
                            }
                            combatRewards.Add(new LevelUpSystem.Reward
                            {
                                type = LevelUpSystem.RewardType.Gem,
                                name = seal.displayName,
                                description = seal.description,
                                seal = seal.ToCardSealSpec()
                            });
                            dungeonCandidates.Add(
                                new DungeonRunLevelRewardCandidate(
                                    DungeonRunLevelRewardCandidateKind.Seal,
                                    candidate.contentId));
                            break;

                        default:
                            throw new InvalidOperationException(
                                $"固定升级 Offer {source.offerContentId} 的候选 " +
                                $"{candidateIndex} 类型无效。" );
                    }
                }

                var combatOffer = new LevelUpRewardOffer(
                    source.offerContentId,
                    combatRewards,
                    source.allowCashOut);
                var dungeonOffer = new DungeonRunLevelRewardOffer(
                    source.offerContentId,
                    dungeonCandidates);
                entries.Add(source.reachedLevel,
                    new ResolvedLevelUpOfferEntry(
                        combatOffer,
                        dungeonOffer));
            }
            return new ResolvedLevelUpOfferSchedule(entries, canReceiveSeals);
        }
    }

    /// <summary>
    /// Immutable authored table shared by both callers. A live capability query
    /// controls new seal offers; a locked entry falls through to ordinary rewards.
    /// It never reads Resources or mutates an account or Run.
    /// </summary>
    public sealed class ResolvedLevelUpOfferSchedule :
        ILevelUpRewardOfferResolver,
        IDungeonRunLevelRewardOfferResolver
    {
        private readonly IReadOnlyDictionary<int, ResolvedLevelUpOfferEntry>
            _entries;
        private readonly Func<bool> _canReceiveSeals;

        internal ResolvedLevelUpOfferSchedule(
            IReadOnlyDictionary<int, ResolvedLevelUpOfferEntry> entries,
            Func<bool> canReceiveSeals)
        {
            _entries = entries ?? throw new ArgumentNullException(
                nameof(entries));
            _canReceiveSeals = canReceiveSeals;
        }

        public bool TryResolve(
            int reachedLevel,
            out LevelUpRewardOffer offer,
            out string error)
        {
            error = null;
            if (_entries.TryGetValue(reachedLevel,
                    out ResolvedLevelUpOfferEntry entry) &&
                (!entry.RequiresSealPermission || _canReceiveSeals?.Invoke() == true))
            {
                offer = entry.CombatOffer;
                return true;
            }
            offer = null;
            return false;
        }

        bool IDungeonRunLevelRewardOfferResolver.TryResolve(
            DungeonRunLevelRewardOfferRequest request,
            out DungeonRunLevelRewardOffer offer,
            out string error)
        {
            error = null;
            if (_entries.TryGetValue(request.ReachedLevel,
                    out ResolvedLevelUpOfferEntry entry) &&
                (!entry.RequiresSealPermission || _canReceiveSeals?.Invoke() == true))
            {
                offer = entry.DungeonOffer;
                return true;
            }
            offer = null;
            return false;
        }
    }

    /// <summary>One level's paired Core/Dungeon projections.</summary>
    public sealed class ResolvedLevelUpOfferEntry
    {
        public LevelUpRewardOffer CombatOffer { get; }
        public DungeonRunLevelRewardOffer DungeonOffer { get; }
        public bool RequiresSealPermission { get; }

        public ResolvedLevelUpOfferEntry(
            LevelUpRewardOffer combatOffer,
            DungeonRunLevelRewardOffer dungeonOffer)
        {
            CombatOffer = combatOffer ?? throw new ArgumentNullException(
                nameof(combatOffer));
            DungeonOffer = dungeonOffer ?? throw new ArgumentNullException(
                nameof(dungeonOffer));
            foreach (LevelUpSystem.Reward reward in CombatOffer.Rewards)
                if (reward.type == LevelUpSystem.RewardType.Gem)
                    RequiresSealPermission = true;
        }
    }
}
