using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the adopted source-to-executable-event projection. A source choice,
    /// SourceOnly mapping or matching display name never grants execution.
    /// Only a complete supported source mechanism creates its existing typed
    /// project contract. The Run source owner owns placement, proof and commit.
    /// Recovery validates saved contracts without today's author presentation.
    /// Crystal uses the approved whole-Run roster projection; this domain adapter
    /// does not itself accept Global XP or PickupCrystal account statistics.
    /// </summary>
    internal static class DungeonSideEventSourceAdapter
    {
        internal const string ArcanaPoolContentId = "pool.xiyou.arcana.default.v1";
        internal const int Version = 1;

        /// <summary>Checks admission even for a failed placement, which has no
        /// executable saved node to inspect. Unknown mechanics cannot masquerade
        /// as a supported adapter that merely found no lawful location.</summary>
        internal static bool TryResolveAdapterIdentity(SideEventSourceAttempt attempt,
            out string adapterContentId, out string error)
        {
            adapterContentId = null; error = null;
            try { RequireSupported(attempt, out adapterContentId); return true; }
            catch (Exception exception) { adapterContentId = null; error = exception.Message; return false; }
        }

        internal static bool TryCreateNode(SideEventSourceAttempt attempt, LevelRunProfile profile,
            string nodeId, string effectId, out LevelNode node, out string adapterContentId, out string error)
            => TryCreateNode(attempt, null, profile, nodeId, effectId, out node, out adapterContentId, out error);

        internal static bool TryCreateNode(SideEventSourceAttempt attempt, SideEventSourceWireDocument document,
            LevelRunProfile profile, string nodeId, string effectId, out LevelNode node, out string adapterContentId, out string error)
        {
            node = null; adapterContentId = null; error = null;
            try
            {
                RequireId(effectId);
                if (profile == null) throw Invalid("An explicit author floor is required.");
                LevelNode candidate = LevelRunNodeDirectory.ForAuthor(profile).FindNode(nodeId);
                if (candidate?.nodeType != LevelNodeType.Interaction)
                    throw Invalid("Only an existing author Interaction slot can receive a source event.");
                var root = RequireSupported(attempt, out adapterContentId);
                LevelInteractionEffect effect = CreateEffect(root, effectId);
                if (root.Kind == SideEventSourceKind.CardStatOffering)
                    effect.cardStatOffering = DungeonSideEventOfferingSourceContracts.CaptureCardStatContract(attempt, document);
                if (root.Kind == SideEventSourceKind.DuplicateOffering)
                    effect.duplicateOffering = DungeonSideEventRecruitmentSourceContracts.CaptureDuplicate(attempt, document);
                if (root.Kind == SideEventSourceKind.GunForHire)
                    effect.crawlerInvitation = new LevelInteractionInvitationContract { policyId = DungeonRunInvitations.PolicyId };
                candidate.completionEffects = new List<LevelInteractionEffect> { effect };
                if (!TryValidateNode(attempt, document, candidate, adapterContentId, out error))
                    throw Invalid(error);
                node = candidate;
                return true;
            }
            catch (Exception exception)
            {
                node = null; adapterContentId = null; error = exception.Message; return false;
            }
        }

        internal static bool TryValidateNode(SideEventSourceAttempt attempt, LevelNode savedNode,
            string adapterContentId, out string error)
            => TryValidateNode(attempt, null, savedNode, adapterContentId, out error);

        internal static bool TryValidateNode(SideEventSourceAttempt attempt, SideEventSourceWireDocument document,
            LevelNode savedNode, string adapterContentId, out string error)
        {
            error = null;
            try
            {
                SideEventSourceRoot root = RequireSupported(attempt, out string expected);
                if (adapterContentId != expected || savedNode?.nodeType != LevelNodeType.Interaction ||
                    savedNode.completionEffects?.Count != 1)
                    throw Invalid("The adapter identity and single Interaction contract must match the selected source.");
                RequireId(savedNode.contentId);
                LevelInteractionEffect effect = savedNode.completionEffects[0];
                if (effect == null) throw Invalid("A complete saved event effect is required.");
                RequireId(effect.effectId);
                var errors = new List<string>();
                switch (root.Kind)
                {
                    case SideEventSourceKind.AbandonedCart:
                        DungeonRunAbandonedCarts.ValidateEffect(effect, errors); break;
                    case SideEventSourceKind.ManaStatue:
                        DungeonRunManaStatues.ValidateEffect(effect, errors); break;
                    case SideEventSourceKind.GemChest:
                        DungeonRunGemChests.ValidateEffect(effect, errors);
                        if (effect.sealRewardPoolId != DungeonRunGemChests.OrdinaryOfferContractId || effect.amount != 3 || effect.cashOutJade != 200)
                            errors.Add("Saved ordinary Gem Chest differs from the adopted execution contract.");
                        break;
                    case SideEventSourceKind.SilverGemChest:
                        DungeonRunGemChests.ValidateEffect(effect, errors);
                        if (effect.sealRewardPoolId != DungeonRunGemChests.SilverOfferContractId || effect.amount != 3 || effect.cashOutJade != 200)
                            errors.Add("Saved Silver chest differs from the complete two-lock/T2 contract.");
                        break;
                    case SideEventSourceKind.CardStatOffering:
                        DungeonRunCardStatOfferings.ValidateEffect(effect, errors);
                        DungeonSideEventOfferingSourceContracts.RequireCardStatContract(attempt, document, effect);
                        break;
                    case SideEventSourceKind.DuplicateOffering:
                        DungeonRunDuplicateOfferings.ValidateEffect(effect, errors);
                        DungeonSideEventRecruitmentSourceContracts.RequireBinding(attempt, document, effect);
                        break;
                    case SideEventSourceKind.GunForHire:
                        // Previously frozen paid source offers stay readable.
                        // New publication explicitly adopts the free project policy;
                        // it never mutates the original source prices or wire.
                        if (effect.kind == LevelInteractionEffectKind.GunForHire)
                        {
                            DungeonRunGunForHire.ValidateEffect(effect, errors);
                            DungeonSideEventRecruitmentSourceContracts.RequireBinding(attempt, document, effect);
                        }
                        else DungeonRunInvitations.ValidateEffect(effect, errors);
                        break;
                    case SideEventSourceKind.EvoStatue:
                        DungeonRunGemChests.ValidateEffect(effect, errors);
                        if (effect.sealRewardPoolId != DungeonRunGemChests.EvoStatueOfferContractId || effect.amount != 3 || effect.cashOutJade != 200)
                            errors.Add("Saved Evo statue differs from the approved single-primary source projection.");
                        break;
                    case SideEventSourceKind.Crystal:
                        CrystalEventContracts.ValidateEffect(effect, errors); break;
                    case SideEventSourceKind.ChickenOffering:
                    case SideEventSourceKind.ManaOffering:
                        if (effect.kind != (root.Kind == SideEventSourceKind.ChickenOffering
                            ? LevelInteractionEffectKind.ChickenOffering : LevelInteractionEffectKind.ManaOffering))
                            throw Invalid("The saved Offering kind differs from its source selection.");
                        DungeonRunChickenOffering.ValidateEffect(effect, errors); break;
                    case SideEventSourceKind.ArcanaChoice:
                        DungeonRunArcanas.ValidateEffect(effect, errors);
                        if (effect.arcanaChoice?.poolContentId != ArcanaPoolContentId ||
                            effect.arcanaChoice.candidateCount != ((SideEventArcanaChoiceSpecification)root.Mechanic).ChoicesToShow ||
                            effect.HasChickenOfferingContract || effect.HasManaOfferingContract ||
                            effect.HasManaStatueContract || effect.HasAbandonedCartContract)
                            errors.Add("The saved Arcana pool/count or payload differs from the adopted source projection.");
                        break;
                }
                if (errors.Count != 0) throw Invalid(string.Join(" ", errors));
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        internal static bool TryResolveMemberAdapterIdentity(SideEventSourceDefinition source,
            SideEventSourceMember member, out string adapterId, out string error)
        {
            adapterId = null; error = null;
            try
            {
                if (source == null || member == null || !source.TryGetRoot(member.RootContentId, out var root))
                    throw Invalid("Preflight requires an actual registered source occurrence.");
                RequireSupported(root, member.OverrideDetails, out adapterId); return true;
            }
            catch (Exception exception) { adapterId = null; error = exception.Message; return false; }
        }

        private static SideEventSourceRoot RequireSupported(SideEventSourceAttempt attempt, out string adapterId)
        {
            adapterId = null;
            SideEventQualifiedOccurrence occurrence = attempt?.Selection?.Member;
            if (attempt?.Selection?.Choice.Status != SideEventChoiceStatus.Selected || occurrence == null)
                throw Invalid("A selected full source member is required; null weighted results have no event adapter.");
            return RequireSupported(occurrence.Root, occurrence.SourceMember.OverrideDetails, out adapterId);
        }

        private static SideEventSourceRoot RequireSupported(SideEventSourceRoot root, bool overrideDetails, out string adapterId)
        {
            adapterId = null;
            if (overrideDetails)
                throw Invalid("Source Details overrides require their complete resolved contract, not just a source reference.");
            if (root.Mapping != SideEventProjectMapping.SourceOnly)
                throw Invalid("An unknown source projection cannot authorize an event.");
            bool supported;
            string key;
            switch (root.Mechanic)
            {
                case SideEventAbandonedCartSpecification cart:
                    key = "abandoned-cart";
                    supported = root.SourceAssetId == "DestructibleEventConfig_AbandonedCart" &&
                        cart.AcceptedBumps == DungeonRunAbandonedCarts.RequiredAcceptedBumps &&
                        cart.AllowSpamBump && cart.SourceStat == 10 &&
                        cart.StatAmount == DungeonRunAbandonedCarts.DrawCountBonus && !cart.AlwaysManaVariant &&
                        ((cart.DropConfigContentId == "sourceobject.xiyou.g4_cart_drop" &&
                          cart.DropTagContentId == "sourceobject.xiyou.g4_cart_drop_tag") ||
                         (cart.DropConfigContentId == "sourceobject.xiyou.library01.g4_cart_drop" &&
                          cart.DropTagContentId == "sourceobject.xiyou.library01.g4_cart_drop_tag"));
                    break;
                case SideEventManaStatueSpecification statue:
                    key = "mana-statue";
                    supported = root.SourceAssetId == "DestructibleEventConfig_ManaStatue" &&
                        statue.AcceptedBumps == DungeonRunManaStatues.RequiredAcceptedBumps &&
                        statue.AllowSpamBump && IsAdoptedMana(statue.Reward);
                    break;
                case SideEventArcanaChoiceSpecification arcana:
                    key = "arcana-choice";
                    supported = root.SourceAssetId == "PassiveEvent_ArcanaChoice" &&
                        arcana.ChoicesToShow == 3 && arcana.SourceArcanaType == 0 &&
                        !arcana.OutsideClickCloses && arcana.ViewIsEdgeEvent.IsKnown && !arcana.ViewIsEdgeEvent.Value;
                    break;
                case SideEventChickenOfferingSpecification chicken:
                    key = "chicken-offering";
                    SideEventHealingReward healing = chicken.Healing;
                    supported = root.SourceAssetId == ChickenOfferingContracts.SourceAssetId &&
                        IsAdoptedOffering(chicken.Selection) && chicken.FoodCount == ChickenOfferingContracts.FoodCount &&
                        chicken.FoodMetricId == "FloorChicken" && AllCollections(chicken.FoodCollectionTypes) &&
                        healing.Iterations == ChickenOfferingContracts.FoodCount &&
                        healing.BaseAmount == ChickenOfferingContracts.BaseHealingPerFood &&
                        healing.ModifierSourceAssetId == "ArcanaConfig_ExperimentalMedicine" && healing.Modifier == 2f &&
                        healing.ReloadBasePerIteration && healing.ClampToCurrentHealthBounds && !healing.IncreaseMaxHealth &&
                        healing.ActualHealMetricId == "PlayerHealthRestored" && healing.CountOnlyPositiveActualDelta;
                    break;
                case SideEventManaOfferingSpecification mana:
                    key = "mana-offering";
                    supported = root.SourceAssetId == "PassiveEvent_ManaOfferingTable" &&
                        IsAdoptedOffering(mana.Selection) && IsAdoptedMana(mana.Reward);
                    break;
                case SideEventGemChestSpecification chest:
                    key = "gem-chest";
                    supported = root.SourceAssetId == "PassiveEvent_GemChest" &&
                        MatchesOrdinaryChest(chest.Offer) && root.Requirement is SideEventRelicRequirement hammer &&
                        hammer.SourceAssetId == "RelicConfig_GemHammer";
                    break;
                case SideEventCardStatOfferingSpecification stats:
                    key = "card-stat-offering";
                    supported = DungeonSideEventOfferingSourceContracts.MatchesCardStat(root, stats);
                    break;
                case SideEventSilverGemChestSpecification silver:
                    key = "silver-gem-chest";
                    supported = DungeonSideEventOfferingSourceContracts.MatchesSilver(root, silver);
                    break;
                case SideEventDuplicateOfferingSpecification duplicate:
                    key = "duplicate-offering";
                    supported = DungeonSideEventRecruitmentSourceContracts.MatchesDuplicate(root, duplicate);
                    break;
                case SideEventGunForHireSpecification hire:
                    key = "gun-for-hire";
                    supported = DungeonSideEventRecruitmentSourceContracts.MatchesHire(root, hire);
                    break;
                case SideEventEvoStatueSpecification evo:
                    key = "evo-statue";
                    supported = root.SourceAssetId == "DestructibleEventConfig_EvoStatue" &&
                        root.Requirement is SideEventDungeonRequirement achievement &&
                        MatchesEvoGate(achievement) &&
                        MatchesEvoStatue(evo);
                    break;
                case SideEventCrystalSpecification crystal:
                    key = "crystal";
                    supported = root.SourceAssetId == CrystalEventContracts.SourceEventId &&
                        root.Requirement.Kind == SideEventRequirementKind.Base &&
                        CrystalEventContracts.MatchesSourceSpecification(crystal);
                    break;
                default:
                    throw Invalid("This source mechanism has no complete adopted execution contract.");
            }
            if (!supported) throw Invalid("Source parameters differ from the complete adopted event contract.");
            adapterId = "sideadapter.xiyou." + key + ".v1";
            return root;
        }

        private static LevelInteractionEffect CreateEffect(SideEventSourceRoot root, string effectId)
        {
            var effect = new LevelInteractionEffect { effectId = effectId };
            switch (root.Kind)
            {
                case SideEventSourceKind.AbandonedCart:
                    effect.kind = LevelInteractionEffectKind.AbandonedCart;
                    effect.abandonedCart = new LevelInteractionAbandonedCartContract {
                        version = Version, sourceEventId = DungeonRunAbandonedCarts.SourceEventId,
                        requiredAcceptedBumps = DungeonRunAbandonedCarts.RequiredAcceptedBumps,
                        drawCountBonus = DungeonRunAbandonedCarts.DrawCountBonus,
                        pickupContentId = DungeonRunAbandonedCarts.BracerContentId };
                    break;
                case SideEventSourceKind.ManaStatue:
                    effect.kind = LevelInteractionEffectKind.ManaStatue;
                    effect.manaStatue = new LevelInteractionManaStatueContract {
                        version = Version, sourceEventId = DungeonRunManaStatues.SourceEventId,
                        requiredAcceptedBumps = DungeonRunManaStatues.RequiredAcceptedBumps,
                        manaAmount = DungeonRunManaStatues.ManaAmount };
                    break;
                case SideEventSourceKind.ArcanaChoice:
                    effect.kind = LevelInteractionEffectKind.ChooseArcana;
                    effect.arcanaChoice = new LevelInteractionArcanaChoiceContract {
                        poolContentId = ArcanaPoolContentId,
                        candidateCount = ((SideEventArcanaChoiceSpecification)root.Mechanic).ChoicesToShow };
                    break;
                case SideEventSourceKind.ChickenOffering:
                    effect.kind = LevelInteractionEffectKind.ChickenOffering;
                    effect.chickenOffering = new LevelInteractionChickenOfferingContract {
                        sourceEventId = ChickenOfferingContracts.SourceEventId };
                    break;
                case SideEventSourceKind.ManaOffering:
                    effect.kind = LevelInteractionEffectKind.ManaOffering;
                    effect.manaOffering = new LevelInteractionManaOfferingContract {
                        sourceEventId = ManaOfferingContracts.SourceEventId };
                    break;
                case SideEventSourceKind.GemChest:
                    effect.kind = LevelInteractionEffectKind.GemChest; effect.amount = 3; effect.cashOutJade = 200;
                    effect.sealRewardPoolId = DungeonRunGemChests.OrdinaryOfferContractId;
                    break;
                case SideEventSourceKind.CardStatOffering:
                    effect.kind = LevelInteractionEffectKind.CardStatOffering;
                    // The complete source-document contract is attached by TryCreateNode.
                    break;
                case SideEventSourceKind.SilverGemChest:
                    effect.kind = LevelInteractionEffectKind.GemChest; effect.amount = 3; effect.cashOutJade = 200;
                    effect.sealRewardPoolId = DungeonRunGemChests.SilverOfferContractId;
                    break;
                case SideEventSourceKind.DuplicateOffering:
                    effect.kind = LevelInteractionEffectKind.DuplicateOffering; effect.amount = 1;
                    // Full document identity is attached without choosing breakthrough policy.
                    break;
                case SideEventSourceKind.GunForHire:
                    effect.kind = LevelInteractionEffectKind.CrawlerInvitation;
                    effect.summaryText = "请灵台";
                    // Source placement/200-action eligibility remain; the project
                    // invitation owner freezes up to three free unlocked summons.
                    break;
                case SideEventSourceKind.EvoStatue:
                    effect.kind = LevelInteractionEffectKind.GemChest; effect.amount = 3; effect.cashOutJade = 200;
                    effect.sealRewardPoolId = DungeonRunGemChests.EvoStatueOfferContractId;
                    break;
                case SideEventSourceKind.Crystal:
                    effect.kind = LevelInteractionEffectKind.CrystalThrowing;
                    effect.crystalThrowing = new LevelInteractionCrystalContract {
                        version = Version, sourceEventId = CrystalEventContracts.SourceEventId };
                    break;
            }
            return effect;
        }

        // The physical GemChest call supplies three true arguments. Optional raw
        // source fields stay Unknown; contradictory known values cannot borrow this projection.
        private static bool MatchesOrdinaryChest(SideEventGemOffer offer)
        {
            if (offer.MinimumChoiceCount != 3 || offer.MaximumChoiceCount != 3 || offer.GuaranteeEvo ||
                offer.Frequencies.Count != 3 || !CallFlag(offer.CanCashOut) || !CallFlag(offer.AllowFallbacks) ||
                !CallFlag(offer.EnsureNonEvo) || offer.CashOutAmount.IsKnown && offer.CashOutAmount.Value != 200)
                return false;
            // Two explicitly adopted declaration namespaces refer to the same
            // native objects. No suffix matching or mixed family is admitted.
            bool library = offer.Frequencies[0].ContentId == "sidefrequency.xiyou.library01.g4_alpha";
            string[] ids = library ? new[] { "sidefrequency.xiyou.library01.g4_alpha", "sidefrequency.xiyou.library01.g4_beta",
                "sidefrequency.xiyou.library01.g4_gamma" } : new[] { "sidefrequency.xiyou.g4_alpha", "sidefrequency.xiyou.g4_beta", "sidefrequency.xiyou.g4_gamma" };
            long[] paths = { 19652, 19656, 19655 }; float[] weights = { 160, 120, 80 };
            for (int i = 0; i < ids.Length; i++)
                if (offer.Frequencies[i].ContentId != ids[i] || offer.Frequencies[i].ChestWeight != weights[i] ||
                    offer.Frequencies[i].Provenance.Alias != "vc-local-full-copy" ||
                    offer.Frequencies[i].Provenance.SourceFile != "sharedassets0.assets" ||
                    offer.Frequencies[i].Provenance.PathId != paths[i]) return false;
            return true;
        }
        // The source recipe is retained for provenance; the approved project policy removes
        // component sacrifice and uses ordinary return/cash-out instead of component-card rewards.
        private static bool MatchesEvoStatue(SideEventEvoStatueSpecification statue)
        {
            var offer = statue.Offer;
            if (statue.AcceptedBumps.IsKnown || offer.MinimumChoiceCount != 3 || offer.MaximumChoiceCount != 3 ||
                !offer.GuaranteeEvo || offer.Frequencies.Count != 5 || !KnownFalse(offer.CanCashOut) ||
                !KnownFalse(offer.AllowFallbacks) || !KnownFalse(offer.EnsureNonEvo) ||
                offer.CashOutAmount.IsKnown || offer.NoTargetFallbackSourceAssetId != "Card_E_BagOfCoins" ||
                offer.NoTargetFallbackQuantity.IsKnown || !statue.Recipe.WeaponGroupsFirst ||
                !statue.Recipe.ConsumeSelectedInstances || !statue.Recipe.FirstHostNeedsLawfulGemSlot ||
                !statue.Recipe.InheritOnlyFirstHostGems) return false;
            bool library = offer.Frequencies[0].ContentId == "sidefrequency.xiyou.library01.g4_alpha";
            string[] ids = library ? new[] { "sidefrequency.xiyou.library01.g4_alpha", "sidefrequency.xiyou.library01.g4_beta",
                "sidefrequency.xiyou.library01.g4_gamma", "sidefrequency.xiyou.library01.g4_delta", "sidefrequency.xiyou.library01.g4_epsilon" } :
                new[] { "sidefrequency.xiyou.g4_alpha", "sidefrequency.xiyou.g4_beta", "sidefrequency.xiyou.g4_gamma",
                    "sidefrequency.xiyou.g4_delta", "sidefrequency.xiyou.g4_epsilon" };
            long[] paths = { 19652, 19656, 19655, 19653, 19654 }; float[] weights = { 160, 120, 80, 60, 40 };
            for (int i = 0; i < ids.Length; i++)
                if (offer.Frequencies[i].ContentId != ids[i] || offer.Frequencies[i].ChestWeight != weights[i] ||
                    offer.Frequencies[i].Provenance.Alias != "vc-local-full-copy" ||
                    offer.Frequencies[i].Provenance.SourceFile != "sharedassets0.assets" ||
                    offer.Frequencies[i].Provenance.PathId != paths[i]) return false;
            return true;
        }
        // Reject a different effective source gate at the execution boundary. Progression
        // separately resolves this gate to real permanent account qualification. The source's
        // inactive target-level/FCC/encounter fields remain preserved by its complete fingerprint.
        private static bool MatchesEvoGate(SideEventDungeonRequirement value)
        {
            var condition = value.Condition;
            return value.SourceAssetId == "DungeonAchievement_Gems_01_CompMF1F1" &&
                value.AchievementId == "Gems_01_CompMF1F1" && value.AllowInDemo &&
                value.RewardType == 7 && value.HasUnlockReward && !condition.CheckAnyDungeon &&
                !condition.RequiresFcc && condition.AchievementType == 1 && condition.FloorNumberToComplete == 2 &&
                condition.DungeonReference.OwnerSourceFile == "sharedassets0.assets" &&
                condition.DungeonReference.FileIndex == 1 && condition.DungeonReference.PathId == 7558 &&
                value.Provenance.Alias == "vc-local-full-copy" && value.Provenance.SourceFile == "sharedassets0.assets" &&
                value.Provenance.PathId == 17693;
        }
        private static bool KnownFalse(SideEventOptionalBool value) => value.IsKnown && !value.Value;

        private static bool CallFlag(SideEventOptionalBool value) => !value.IsKnown || value.Value;

        private static bool IsAdoptedOffering(SideEventOfferingSelection selection)
            => selection.CardsRequired == 1 && selection.DestroySelected && selection.ExcludeFcc &&
               selection.RequireOfferingFlag && !selection.OutsideClickCloses && !selection.RequireSupportedModifiedStat;

        private static bool IsAdoptedMana(SideEventManaReward reward)
            => reward.RequestedAmount == ManaOfferingContracts.ManaAmount && reward.IncreaseStarting &&
               reward.CurrentMinimum == 0 && reward.CurrentMaximum == DungeonRunManaStatues.SourceMaximumMana &&
               reward.MetricId == "PickupMana" && AllCollections(reward.CollectionTypes) &&
               !reward.AccountPermanentMana && !reward.ModifiesTemporaryMana;

        private static bool AllCollections(IReadOnlyList<int> collections)
            => collections.Count == 3 && collections[0] == 0 && collections[1] == 1 && collections[2] == 2;

        private static void RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw Invalid("Stable identities must be explicit.");
            try { new System.Text.UTF8Encoding(false, true).GetBytes(id); }
            catch (System.Text.EncoderFallbackException) { throw Invalid("Stable identities contain invalid Unicode."); }
            foreach (char value in id)
                if (char.IsWhiteSpace(value) || char.IsControl(value)) throw Invalid("Stable identities contain invalid characters.");
        }

        private static InvalidOperationException Invalid(string message)
            => new InvalidOperationException("Source adapter: " + message);
    }
}
