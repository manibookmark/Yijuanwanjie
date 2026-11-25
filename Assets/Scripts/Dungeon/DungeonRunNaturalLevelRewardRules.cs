using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the map level-reward checkpoint format and persistent-host legality.
    /// Sampling is delegated to Core; all reads and preparation use detached data.
    /// Only DungeonRunInteractionTransaction commits a draft or advances a level.
    /// Legacy card-only checkpoints remain readable without resampling or migration.
    /// </summary>
    internal static class DungeonRunNaturalLevelRewardRules
    {
        internal static bool HasChoices(DungeonRunInteractionSaveData state)
            => state.rewardCardIds.Count > 0 || state.naturalRewardCandidates.Count > 0;

        internal static void RequireConfiguration(DungeonRunInteractionCatalog catalog,
            bool restoringNatural = false)
        {
            bool usesPool = catalog.RewardConfig?.useNaturalCardSealPool == true ||
                catalog.NaturalRewardPool?.LevelCostPolicy != null;
            if (!restoringNatural && !usesPool) return;
            if (!usesPool || catalog.NaturalRewardPool == null)
                throw new InvalidOperationException("地图自然升级奖励缺少显式自然规则或 NaturalRewardPool；不能回退为固定卡牌或属性奖励。");
        }

        internal static void Freeze(DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData next,
            DungeonRunLevelRewardOfferRequest request, IRandomSource random)
        {
            Clear(next);
            RequireConfiguration(catalog);
            LevelUpRewardRequest rewardRequest = new LevelUpRewardRequest(request.ReachedLevel,
                request.IsReferenceTargetLevel ? LevelUpRewardSource.MapReferenceLevel : LevelUpRewardSource.MapExperience);
            if (catalog.NaturalRewardPool?.LevelCostPolicy != null)
                rewardRequest = catalog.NaturalRewardPool.LevelCostPolicy.BindRequest(rewardRequest);
            next.rewardReachedLevel = rewardRequest.ReachedLevel;
            next.rewardSource = rewardRequest.Source;
            next.rewardRuleContentId = rewardRequest.RuleContentId;
            next.rewardRuleVersion = rewardRequest.RuleVersion;
            // An authored offer wins even when a natural fallback is configured, as in Core.
            if (catalog.LevelRewardOfferResolver != null)
            {
                if (catalog.LevelRewardOfferResolver.TryResolve(request,
                        out DungeonRunLevelRewardOffer fixedOffer, out string resolverError))
                {
                    if (fixedOffer == null || string.IsNullOrWhiteSpace(fixedOffer.OfferContentId) ||
                        fixedOffer.Candidates == null || fixedOffer.Candidates.Count == 0)
                        throw new InvalidOperationException("固定地图升级 Offer 缺少稳定候选。");
                    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DungeonRunLevelRewardCandidate candidate in fixedOffer.Candidates)
                    {
                        if (candidate == null || candidate.Kind != DungeonRunLevelRewardCandidateKind.Card ||
                            string.IsNullOrWhiteSpace(candidate.ContentId) || !ids.Add(candidate.ContentId))
                            throw new InvalidOperationException("固定地图升级 Offer 只支持已验收的固定卡牌候选。");
                        RequireCard(catalog, candidate.ContentId);
                        next.rewardCardIds.Add(candidate.ContentId);
                    }
                    next.rewardOfferContentId = fixedOffer.OfferContentId;
                    return;
                }
                if (!string.IsNullOrWhiteSpace(resolverError))
                    throw new InvalidOperationException("固定地图升级 Offer 解析失败：" + resolverError);
            }
            if (catalog.RewardConfig?.useNaturalCardSealPool == true ||
                catalog.NaturalRewardPool?.LevelCostPolicy != null)
            {
                // Count zero lets Core roll the authored choice-count curve, not a fixed 3+1.
                List<LevelUpSystem.Reward> rewards = catalog.NaturalRewardPool.Roll(
                    catalog.RewardConfig, catalog.LuckPercent, random, 0,
                    seal => HasLegalHost(catalog, draft, seal), rewardRequest);
                foreach (LevelUpSystem.Reward reward in rewards)
                {
                    var candidate = new DungeonRunNaturalRewardCandidateSaveData
                    { title = reward.name, description = reward.description };
                    if (reward.type == LevelUpSystem.RewardType.NewCard && reward.card != null)
                    {
                        RequireNaturalCard(catalog, reward.card.contentId);
                        candidate.kind = DungeonRunLevelRewardCandidateKind.Card;
                        candidate.contentId = reward.card.contentId;
                    }
                    else if (reward.type == LevelUpSystem.RewardType.Gem && reward.seal != null)
                    {
                        RequireSeal(catalog, reward.seal);
                        candidate.kind = DungeonRunLevelRewardCandidateKind.Seal;
                        candidate.contentId = reward.seal.contentId;
                        candidate.seal = reward.seal.Clone();
                    }
                    else throw new InvalidOperationException("自然升级只支持真实卡牌或灵印规格，不能填充属性或占位奖励。");
                    next.naturalRewardCandidates.Add(candidate);
                }
                next.naturalRewardVersion = 1;
                ValidateCheckpoint(next);
                return;
            }

            if (catalog.RewardConfig == null || catalog.RewardConfig.cardRewardChance != 1f)
                throw new InvalidOperationException("地图升级奖励需要正式的卡牌专用奖励规则。");
            foreach (CardConfig card in catalog.RewardCards)
                RequireCard(catalog, card?.contentId);
            var generator = new LevelUpSystem(new PlayerStats(), catalog.RewardCards.ToList(),
                catalog.RewardConfig, () => catalog.LuckPercent, random);
            next.rewardCardIds = generator.GenerateRewardsForRequest(rewardRequest)
                .Select(reward => reward.card.contentId).ToList();
        }

        internal static void BuildOffer(DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData state,
            DungeonRunInteractionOffer offer)
        {
            ValidateCheckpoint(state);
            if (state.naturalRewardVersion == 1)
            {
                RequireConfiguration(catalog, restoringNatural: true);
                foreach (var candidate in state.naturalRewardCandidates)
                {
                    if (candidate.kind == DungeonRunLevelRewardCandidateKind.Card)
                        RequireNaturalCard(catalog, candidate.contentId);
                    else if (catalog.CanReceiveSeals) RequireSeal(catalog, candidate.seal);
                }
            }
            offer.Phase = DungeonRunInteractionPhase.RewardChoice;
            var options = new List<DungeonRunInteractionOption>();
            if (state.naturalRewardVersion == 0)
            {
                foreach (string id in state.rewardCardIds)
                {
                    CardConfig card = catalog.FindCard(id) ?? throw new InvalidOperationException("待选奖励的卡牌定义缺失。");
                    options.Add(new DungeonRunInteractionOption
                    {
                        Id = $"reward/{state.effectId}/{state.rewardIndex}/{id}",
                        CardContentId = id, Title = card.cardName, Description = card.description
                    });
                }
            }
            else if (string.IsNullOrEmpty(state.selectedSealContentId) || !catalog.CanReceiveSeals)
            {
                // Capability loss never mutates the frozen checkpoint on read.
                // Existing cards remain exact; the old selected seal is simply
                // not an assignable phase until the current owner commits a card.
                foreach (DungeonRunNaturalRewardCandidateSaveData candidate in VisibleCandidates(catalog, state))
                    options.Add(new DungeonRunInteractionOption
                    {
                        Id = ChoiceId(state, candidate),
                        CardContentId = candidate.kind == DungeonRunLevelRewardCandidateKind.Card ? candidate.contentId : null,
                        Seal = candidate.kind == DungeonRunLevelRewardCandidateKind.Seal ? candidate.seal.Clone() : null,
                        Title = candidate.title, Description = candidate.description
                    });
            }
            else
            {
                CardSealSpec seal = SelectedSeal(state);
                offer.Phase = DungeonRunInteractionPhase.SealTarget;
                offer.SelectedSeal = seal.Clone();
                foreach (DungeonRunCardSaveData saved in draft.Cards)
                {
                    if (!IsLegalHost(catalog, saved, seal)) continue;
                    options.Add(new DungeonRunInteractionOption
                    {
                        Id = HostId(state, seal, saved.runtimeInstanceId),
                        CardContentId = saved.contentId, CardRuntimeInstanceId = saved.runtimeInstanceId,
                        Seal = seal.Clone(), Title = catalog.FindCard(saved.contentId).cardName
                    });
                }
                if (options.Count == 0) offer.Message = "没有合法的持久卡牌宿主；可返回原升级候选，不能重抽或改发卡牌。";
            }
            offer.Options = options.AsReadOnly();
        }

        internal static void SelectSeal(DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData state,
            DungeonRunInteractionOption selected)
        {
            if (!catalog.CanReceiveSeals)
                throw new InvalidOperationException("尚未取得玉印，无法选择灵印奖励。");
            DungeonRunNaturalRewardCandidateSaveData candidate = state.naturalRewardCandidates
                .SingleOrDefault(item => ChoiceId(state, item) == selected?.Id);
            if (candidate?.kind != DungeonRunLevelRewardCandidateKind.Seal)
                throw new InvalidOperationException("灵印不属于当前冻结的升级候选。");
            RequireSeal(catalog, candidate.seal);
            if (!HasLegalHost(catalog, draft, candidate.seal))
                throw new InvalidOperationException("当前灵印已无合法持久宿主；原候选保留。");
            state.selectedSealContentId = candidate.contentId;
        }

        /// <summary>Returns an independent replacement, or null for a validated new-card award.</summary>
        internal static DungeonRunCardSaveData PrepareReward(DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft, DungeonRunInteractionSaveData state,
            DungeonRunInteractionOption selected)
        {
            if (selected == null) throw new InvalidOperationException("缺少当前升级选择。");
            if (state.naturalRewardVersion == 0)
            {
                if (!state.rewardCardIds.Contains(selected.CardContentId) ||
                    selected.Id != $"reward/{state.effectId}/{state.rewardIndex}/{selected.CardContentId}")
                    throw new InvalidOperationException("卡牌不属于旧升级检查点。");
                RequireCard(catalog, selected.CardContentId);
                return null;
            }
            if (string.IsNullOrEmpty(state.selectedSealContentId) || !catalog.CanReceiveSeals)
            {
                var candidate = VisibleCandidates(catalog, state).SingleOrDefault(item => ChoiceId(state, item) == selected.Id);
                if (candidate?.kind != DungeonRunLevelRewardCandidateKind.Card ||
                    candidate.contentId != selected.CardContentId)
                    throw new InvalidOperationException("卡牌不属于当前冻结的升级候选。");
                RequireNaturalCard(catalog, candidate.contentId);
                return null;
            }
            CardSealSpec seal = SelectedSeal(state);
            RequireSeal(catalog, seal);
            var saved = draft.Cards.SingleOrDefault(card => card.runtimeInstanceId == selected.CardRuntimeInstanceId);
            if (selected.Id != HostId(state, seal, selected.CardRuntimeInstanceId) ||
                !IsLegalHost(catalog, saved, seal))
                throw new InvalidOperationException("升级灵印宿主已失效或不属于当前持久牌组。");
            if (!DungeonRunSealAssignmentRules.TryPrepare(saved, catalog.FindCard(saved.contentId), seal,
                    id => IsPersistentCard(catalog, id), out var replacement, out _, out string error,
                    references: DungeonRunCardCodec.CreateResolver(catalog.Cards)))
                throw new InvalidOperationException(error);
            return replacement;
        }

        internal static void Clear(DungeonRunInteractionSaveData state)
        {
            state.rewardCardIds.Clear();
            state.rewardOfferContentId = null;
            state.naturalRewardVersion = 0;
            state.rewardReachedLevel = 0;
            state.rewardRuleContentId = null;
            state.rewardRuleVersion = 0;
            state.rewardSource = default;
            state.naturalRewardCandidates.Clear();
            state.selectedSealContentId = null;
        }

        internal static void ValidateCheckpoint(DungeonRunInteractionSaveData state)
        {
            if (state.naturalRewardVersion == 0)
            {
                if (state.naturalRewardCandidates.Count != 0 || state.rewardCardIds.Count == 0 ||
                    state.rewardCardIds.Any(string.IsNullOrWhiteSpace) ||
                    state.rewardCardIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.rewardCardIds.Count ||
                    !string.IsNullOrEmpty(state.selectedSealContentId))
                    throw new InvalidOperationException("旧地图升级检查点缺少合法固定卡牌候选。");
                return;
            }
            if (state.naturalRewardVersion != 1 || state.naturalRewardCandidates.Count == 0 ||
                state.rewardCardIds.Count != 0 || !string.IsNullOrEmpty(state.rewardOfferContentId))
                throw new InvalidOperationException("地图自然升级检查点版本或候选协议不合法。");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int seals = 0;
            foreach (var candidate in state.naturalRewardCandidates)
            {
                if (candidate == null || !Enum.IsDefined(typeof(DungeonRunLevelRewardCandidateKind), candidate.kind) ||
                    string.IsNullOrWhiteSpace(candidate.contentId) || !ids.Add(candidate.contentId))
                    throw new InvalidOperationException("自然升级检查点包含空、重复或未知类型候选。");
                if (candidate.kind == DungeonRunLevelRewardCandidateKind.Seal)
                {
                    if (!ValidSeal(candidate.seal) || candidate.contentId != candidate.seal.contentId || ++seals > 1)
                        throw new InvalidOperationException("自然升级检查点缺少真实灵印规格或包含多枚灵印。");
                }
                else if (!string.IsNullOrEmpty(candidate.seal?.contentId))
                    throw new InvalidOperationException("自然卡牌候选不能同时携带灵印奖励。");
            }
            if (!string.IsNullOrEmpty(state.selectedSealContentId)) SelectedSeal(state);
        }

        private static CardSealSpec SelectedSeal(DungeonRunInteractionSaveData state)
        {
            var candidate = state.naturalRewardCandidates.SingleOrDefault(item =>
                item.kind == DungeonRunLevelRewardCandidateKind.Seal && item.contentId == state.selectedSealContentId);
            return candidate?.seal ?? throw new InvalidOperationException("选中的灵印不属于冻结的升级候选。");
        }

        private static bool HasLegalHost(DungeonRunInteractionCatalog catalog,
            DungeonRunInteractionDraft draft, CardSealSpec seal)
            => draft.Cards.Any(saved => IsLegalHost(catalog, saved, seal));

        private static bool IsLegalHost(DungeonRunInteractionCatalog catalog,
            DungeonRunCardSaveData saved, CardSealSpec seal)
        {
            if (!catalog.CanReceiveSeals || saved == null || saved.runtimeInstanceId <= 0 || !ValidSeal(seal) ||
                !IsPersistentCard(catalog, saved.contentId)) return false;
            CardConfig source = catalog.FindCard(saved.contentId);
            if (seal.effect == CardSealEffect.Evolution && source.evolutionTarget?.IsEncounterTemporary == true) return false;
            CardConfig instance = DungeonRunCardCodec.Restore(saved, DungeonRunCardCodec.CreateResolver(catalog.Cards));
            try
            {
                return !instance.IsEncounterTemporary && CardSealRules.CanAssign(instance, seal,
                    isEvolutionResultEligible: id => IsPersistentCard(catalog, id) && catalog.CanEvolveTo(id));
            }
            finally
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
                else UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static bool IsPersistentCard(DungeonRunInteractionCatalog catalog, string id)
            => !string.IsNullOrWhiteSpace(id) && catalog.FindCard(id) != null &&
               !catalog.FindCard(id).IsEncounterTemporary && catalog.CanGrant(id);

        private static void RequireCard(DungeonRunInteractionCatalog catalog, string id)
        {
            if (!IsPersistentCard(catalog, id))
                throw new InvalidOperationException("升级卡牌未通过当前正式准入或属于遭遇临时牌。");
        }

        private static bool ValidSeal(CardSealSpec seal)
            => seal != null && !string.IsNullOrWhiteSpace(seal.contentId) && seal.value > 0 &&
               Enum.IsDefined(typeof(CardSealEffect), seal.effect);

        private static void RequireNaturalCard(DungeonRunInteractionCatalog catalog, string id)
        {
            RequireCard(catalog, id);
            if (catalog.NaturalRewardPool?.ContainsCard(id) != true)
                throw new InvalidOperationException("已冻结升级卡牌不再属于当前账号、焚卷排除与发布门禁后的自然池。");
        }

        private static void RequireSeal(DungeonRunInteractionCatalog catalog, CardSealSpec seal)
        {
            if (!catalog.CanReceiveSeals)
                throw new InvalidOperationException("尚未取得玉印，无法领取灵印奖励。");
            if (!ValidSeal(seal)) throw new InvalidOperationException("升级灵印缺少真实有效规格。");
            if (catalog.NaturalRewardPool?.ContainsSeal(seal) != true)
                throw new InvalidOperationException("已冻结升级灵印的资格或真实规格已失效；不能替换或重抽。");
        }

        /// <summary>Only pre-permission legacy checkpoints need a compatibility
        /// projection. It is deterministic and detached; choosing a card commits
        /// through the original XP owner, with no second level or fabricated receipt.</summary>
        private static IReadOnlyList<DungeonRunNaturalRewardCandidateSaveData> VisibleCandidates(
            DungeonRunInteractionCatalog catalog, DungeonRunInteractionSaveData state)
        {
            if (catalog.CanReceiveSeals) return state.naturalRewardCandidates;
            var cards = state.naturalRewardCandidates.Where(candidate =>
                candidate.kind == DungeonRunLevelRewardCandidateKind.Card).ToList();
            if (cards.Count > 0) return cards;
            uint hash = 2166136261;
            unchecked
            {
                foreach (char value in "locked-seal-recovery/v1/" + state.effectId + "/" +
                         state.rewardIndex + "/" + state.rewardReachedLevel)
                    hash = (hash ^ value) * 16777619;
            }
            var request = new LevelUpRewardRequest(Math.Max(2, state.rewardReachedLevel), state.rewardSource);
            if (catalog.NaturalRewardPool.LevelCostPolicy != null)
                request = catalog.NaturalRewardPool.LevelCostPolicy.BindRequest(request);
            foreach (LevelUpSystem.Reward reward in catalog.NaturalRewardPool.Roll(catalog.RewardConfig,
                         catalog.LuckPercent, new SeededRandomSource((int)(hash & int.MaxValue)),
                         0, _ => false, request))
            {
                if (reward.type != LevelUpSystem.RewardType.NewCard || reward.card == null)
                    throw new InvalidOperationException("旧灵印升级检查点无法恢复为合法卡牌候选。");
                RequireNaturalCard(catalog, reward.card.contentId);
                cards.Add(new DungeonRunNaturalRewardCandidateSaveData {
                    kind = DungeonRunLevelRewardCandidateKind.Card, contentId = reward.card.contentId,
                    title = reward.name, description = reward.description });
            }
            return cards;
        }

        private static string ChoiceId(DungeonRunInteractionSaveData state, DungeonRunNaturalRewardCandidateSaveData candidate)
            => $"reward/{state.effectId}/{state.rewardIndex}/{(candidate.kind == DungeonRunLevelRewardCandidateKind.Card ? "card" : "seal")}/{candidate.contentId}";

        private static string HostId(DungeonRunInteractionSaveData state, CardSealSpec seal, int runtimeId)
            => $"host/reward/{state.effectId}/{state.rewardIndex}/{seal.contentId}/{runtimeId}";
    }
}
