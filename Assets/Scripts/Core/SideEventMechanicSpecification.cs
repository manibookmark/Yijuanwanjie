using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Unknown is a first-class source boundary. Reading Value without proof fails closed.</summary>
    public readonly struct SideEventOptionalInt
    {
        private readonly int _value;
        public bool IsKnown { get; }
        public int Value => IsKnown ? _value : throw new InvalidOperationException("Source integer is unknown.");
        public SideEventOptionalInt(int value) { _value = value; IsKnown = true; }
        public static SideEventOptionalInt Unknown => default;
    }
    public readonly struct SideEventOptionalBool
    {
        private readonly bool _value;
        public bool IsKnown { get; }
        public bool Value => IsKnown ? _value : throw new InvalidOperationException("Source flag is unknown.");
        public SideEventOptionalBool(bool value) { _value = value; IsKnown = true; }
        public static SideEventOptionalBool Unknown => default;
    }

    public enum SideEventStatRewardFormula { FloorMinimumOne }
    public enum SideEventCopyFinalState { UncapturedAfterGemCallbacks }
    public enum SideEventCollectionScope { Global = 0, Runtime = 1, PerFloor = 2 }

    /// <summary>
    /// Closed source specification union. These values describe captured mechanisms;
    /// they execute nothing and never turn SourceOnly mappings into lawful rewards.
    /// Each sealed kind carries its complete typed parameters and explicit unknowns.
    /// </summary>
    [SideEventSourceCanonical]
    public abstract class SideEventMechanicSpecification
    {
        public SideEventSourceKind Kind { get; }
        internal SideEventMechanicSpecification(SideEventSourceKind kind) { Kind = kind; }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventOfferingSelection
    {
        public int CardsRequired { get; }
        public bool DestroySelected { get; }
        public bool ExcludeFcc { get; }
        public bool RequireOfferingFlag { get; }
        public bool OutsideClickCloses { get; }
        public bool RequireSupportedModifiedStat { get; }
        public SideEventOfferingSelection(int cardsRequired, bool destroySelected, bool excludeFcc,
            bool requireOfferingFlag, bool outsideClickCloses, bool requireSupportedModifiedStat)
        {
            if (cardsRequired <= 0) throw new ArgumentOutOfRangeException(nameof(cardsRequired));
            CardsRequired = cardsRequired; DestroySelected = destroySelected; ExcludeFcc = excludeFcc;
            RequireOfferingFlag = requireOfferingFlag; OutsideClickCloses = outsideClickCloses; RequireSupportedModifiedStat = requireSupportedModifiedStat;
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventManaReward
    {
        public int RequestedAmount { get; }
        public bool IncreaseStarting { get; }
        public int CurrentMinimum { get; }
        public int CurrentMaximum { get; }
        public string MetricId { get; }
        public IReadOnlyList<int> CollectionTypes { get; }
        public bool AccountPermanentMana { get; }
        public bool ModifiesTemporaryMana { get; }
        public SideEventManaReward(int requestedAmount, bool increaseStarting, int currentMinimum,
            int currentMaximum, string metricId, IReadOnlyList<int> collectionTypes,
            bool accountPermanentMana, bool modifiesTemporaryMana)
        {
            if (requestedAmount < 0 || currentMinimum < 0 || currentMaximum < currentMinimum) throw new ArgumentOutOfRangeException(nameof(requestedAmount));
            RequestedAmount = requestedAmount; IncreaseStarting = increaseStarting; CurrentMinimum = currentMinimum; CurrentMaximum = currentMaximum;
            MetricId = SideEventSourceGuard.Text(metricId, nameof(metricId)); CollectionTypes = CopyCollections(collectionTypes);
            AccountPermanentMana = accountPermanentMana; ModifiesTemporaryMana = modifiesTemporaryMana;
        }
        internal static IReadOnlyList<int> CopyCollections(IReadOnlyList<int> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values)); var copy = new int[values.Count]; var unique = new HashSet<int>();
            for (int i = 0; i < copy.Length; i++)
            { if (values[i] < 0 || values[i] > 2 || !unique.Add(values[i])) throw new ArgumentException("Source collection IDs must be unique and within 0..2."); copy[i] = values[i]; }
            return Array.AsReadOnly(copy);
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventHealingReward
    {
        public int Iterations { get; }
        public int BaseAmount { get; }
        public string ModifierSourceAssetId { get; }
        public float Modifier { get; }
        public bool ReloadBasePerIteration { get; }
        public bool ClampToCurrentHealthBounds { get; }
        public bool IncreaseMaxHealth { get; }
        public string ActualHealMetricId { get; }
        public bool CountOnlyPositiveActualDelta { get; }
        public SideEventHealingReward(int iterations, int baseAmount, string modifierSourceAssetId, float modifier,
            bool reloadBasePerIteration, bool clampToCurrentHealthBounds, bool increaseMaxHealth,
            string actualHealMetricId, bool countOnlyPositiveActualDelta)
        {
            if (iterations <= 0 || baseAmount < 0) throw new ArgumentOutOfRangeException(nameof(iterations));
            Iterations = iterations; BaseAmount = baseAmount; ModifierSourceAssetId = SideEventSourceGuard.Text(modifierSourceAssetId, nameof(modifierSourceAssetId));
            Modifier = SideEventSourceGuard.Nonnegative(modifier, nameof(modifier)); ReloadBasePerIteration = reloadBasePerIteration;
            ClampToCurrentHealthBounds = clampToCurrentHealthBounds; IncreaseMaxHealth = increaseMaxHealth;
            ActualHealMetricId = SideEventSourceGuard.Text(actualHealMetricId, nameof(actualHealMetricId)); CountOnlyPositiveActualDelta = countOnlyPositiveActualDelta;
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventStatSetting
    {
        public int SourceStat { get; }
        public int AdditionalChangeAmount { get; }
        public float Modifier { get; }
        public int BaseChangeAmount { get; }
        public SideEventStatSetting(int sourceStat, int additionalChangeAmount, float modifier, int baseChangeAmount)
        { if (sourceStat < 0) throw new ArgumentOutOfRangeException(nameof(sourceStat)); SourceStat = sourceStat;
          AdditionalChangeAmount = additionalChangeAmount; Modifier = SideEventSourceGuard.Finite(modifier, nameof(modifier)); BaseChangeAmount = baseChangeAmount; }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventGemFrequency
    {
        public string ContentId { get; }
        public SideEventSourceProvenance Provenance { get; }
        public float ChestWeight { get; }
        public float LevelUpWeight { get; }
        public float LevelUpLuckFactor { get; }
        public SideEventGemFrequency(string contentId, SideEventSourceProvenance provenance,
            float chestWeight, float levelUpWeight, float levelUpLuckFactor)
        {
            ContentId = SideEventSourceGuard.ContentId(contentId, "sidefrequency");
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            ChestWeight = SideEventSourceGuard.Nonnegative(chestWeight, nameof(chestWeight));
            LevelUpWeight = SideEventSourceGuard.Nonnegative(levelUpWeight, nameof(levelUpWeight));
            LevelUpLuckFactor = SideEventSourceGuard.Finite(levelUpLuckFactor, nameof(levelUpLuckFactor));
        }
    }

    public enum SideEventGemFilterRule
    { RemoveEvoWithoutEvolvableBase, RequireUnlock, RequireBaseOrJewellerProgressionFrequency,
      RemoveSealedUnlessJewellerDisabled, RemoveRunBanished, RequireCompatibleCard, RequireColorEffectQualification }
    public enum SideEventGemTagRule
    { BeginWithAllTags, RemoveCardExclusionsAndExclusionGroups, AddSlottedGemDynamicInclusions, RemoveSlottedGemDynamicExclusions }
    public enum SideEventGemChoiceCountPolicy { LuckAdjustedConfiguredCountThenLimitedBySupplyAndPreviousChoices }
    public enum SideEventGemWeightPolicy { EligibleRepresentedFrequencyChestWeightThenMatchingGemOwnWeight }
    public enum SideEventGemFallbackOrder { FrequencyUpThenDownWithReloadAndRevalidation }
    public enum SideEventGemResumePolicy { RevalidateSavedChoicesAndRefillNotResumePartialTransaction }
    public enum SideEventGemCompletionProof { NotificationWithoutCheckedInsertionResultOrDurableReceipt }
    public enum SideEventGemBackRule
    { RequireAnimationFinished, RequireNoChosenCard, RequireSuccessfulModalClose, ReturnToExistingGemOrComponentChoice }
    public enum SideEventGemQuitRule
    { RequireNoModalLock, RequireNoGemAnimation, RequireNoSelectedComponent, RequireInputAccepted, ClearChosenGemThenClose }
    public enum SideEventEvoCommitStep
    { AwaitHammerAnimation, SnapshotFirstHostBaseConfig, BuildEvolvedModelWithFirstHostGemsAndEvoGem,
      AcquireRewardFromPlayerFactory, AddRewardToDrawOrActivePlayerHand, RemoveEachSelectedOriginalInstance,
      RemoveSelectedFromDeckAndQueuePoolReturn, PublishInsertionCompletedThenClose }

    /// <summary>Captured common Gem algorithm, not a live pool or an executable selector.</summary>
    [SideEventSourceCanonical]
    public sealed class SideEventGemSelectionPolicy
    {
        private static readonly SideEventGemSelectionPolicy Captured = new SideEventGemSelectionPolicy();
        internal static SideEventGemSelectionPolicy CapturedVersionOne => Captured;
        public IReadOnlyList<SideEventGemFilterRule> OrderedFilterRules { get; } = Array.AsReadOnly(new[]
        { SideEventGemFilterRule.RemoveEvoWithoutEvolvableBase, SideEventGemFilterRule.RequireUnlock,
          SideEventGemFilterRule.RequireBaseOrJewellerProgressionFrequency, SideEventGemFilterRule.RemoveSealedUnlessJewellerDisabled,
          SideEventGemFilterRule.RemoveRunBanished, SideEventGemFilterRule.RequireCompatibleCard, SideEventGemFilterRule.RequireColorEffectQualification });
        public bool BaseFrequencyOrEnabledJewellerProgressionFrequency => true;
        public bool RequireEmptySlotForCompatibleCard => true;
        public bool RequireUniqueGemAbsentWhenUnique => true;
        public bool RequireAnyAllowedTagIntersection => true;
        public bool RequireNoEffectsExcludedFromHost => true;
        public bool EmptyConfiguredGemTagsUseAllTags => true;
        public IReadOnlyList<SideEventGemTagRule> OrderedAllowedTagRules { get; } = Array.AsReadOnly(new[]
        { SideEventGemTagRule.BeginWithAllTags, SideEventGemTagRule.RemoveCardExclusionsAndExclusionGroups,
          SideEventGemTagRule.AddSlottedGemDynamicInclusions, SideEventGemTagRule.RemoveSlottedGemDynamicExclusions });
        public bool ColorEffectsReadGlobalFccCards => true;
        public bool ColorEffectRequiresMatchingFccTriggerType => true;
        public bool AnyNonColorEffectPassesColorGate => true;
        public bool EmptyEffectsOrOnlyUnmatchedColorEffectsFail => true;
        public bool EvoCountReadsGlobalPlayer => true;
        public bool BanishAndCompatibleCardsReadPassedPlayer => true;
        public SideEventGemChoiceCountPolicy ChoiceCountPolicy => SideEventGemChoiceCountPolicy.LuckAdjustedConfiguredCountThenLimitedBySupplyAndPreviousChoices;
        public SideEventGemWeightPolicy WeightPolicy => SideEventGemWeightPolicy.EligibleRepresentedFrequencyChestWeightThenMatchingGemOwnWeight;
        public bool FrequencyWeightSelectionUsesLuck => false;
        public bool RemoveSelectedGemFromWorkingPool => true;
        public bool GuaranteeRequiresLawfulBaseAndFilteredEvoAndCountBranch => true;
        public bool EnsureNonEvoSubtractsOneFromEvoTarget => true;
        public bool PreviousNonEvoFilteringIsConditionalOnSupply => true;
        public bool EvoGemRetainedSpeciallyInPreviousChoiceFiltering => true;
        public bool FilterCachedEvolvedOutcomesSeparately => true;
        public SideEventGemFallbackOrder FallbackOrder => SideEventGemFallbackOrder.FrequencyUpThenDownWithReloadAndRevalidation;
        public bool FallbackRequiresAllowFallbacks => true;
        public bool EmptyChoicesUseComponentAidOnlyInStatueMode => true;
        public bool StatueModeRequiresGuaranteeAndNoFallbackAndNoEnsureNonEvo => true;
        public bool ComponentAidWeaponRequiresEvolvedPointerAndEmptySlotAndOtherUnlockedComponents => true;
        public bool ComponentAidRejectsDisabledAndCaseSensitiveTestNames => true;
        public bool ComponentAidRemovesOwnedBaseConfigs => true;
        public bool ReturnToChoicesAloneRerolls => false;
        public IReadOnlyList<SideEventGemBackRule> OrderedBackRules { get; } = Array.AsReadOnly(new[]
        { SideEventGemBackRule.RequireAnimationFinished, SideEventGemBackRule.RequireNoChosenCard,
          SideEventGemBackRule.RequireSuccessfulModalClose, SideEventGemBackRule.ReturnToExistingGemOrComponentChoice });
        public IReadOnlyList<SideEventGemQuitRule> OrderedQuitRules { get; } = Array.AsReadOnly(new[]
        { SideEventGemQuitRule.RequireNoModalLock, SideEventGemQuitRule.RequireNoGemAnimation,
          SideEventGemQuitRule.RequireNoSelectedComponent, SideEventGemQuitRule.RequireInputAccepted,
          SideEventGemQuitRule.ClearChosenGemThenClose });
        public bool QuitCompletesOrDestroysChest => false;
        public bool EvoQuitProjectsChoicesToCurrentTile => true;
        public SideEventGemResumePolicy ResumePolicy => SideEventGemResumePolicy.RevalidateSavedChoicesAndRefillNotResumePartialTransaction;
        public SideEventGemCompletionProof CompletionProof => SideEventGemCompletionProof.NotificationWithoutCheckedInsertionResultOrDurableReceipt;
        public SideEventOptionalBool AllEffectOverrideSemanticsCaptured => SideEventOptionalBool.Unknown;
        public SideEventOptionalBool AsyncFailureIsAtomic => SideEventOptionalBool.Unknown;
        public SideEventOptionalBool NativeDiskDurabilityProven => SideEventOptionalBool.Unknown;
        public SideEventOptionalInt ActualCandidateCount => SideEventOptionalInt.Unknown;
        private SideEventGemSelectionPolicy() { }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventGemOffer
    {
        /// <summary>Configured base range; Luck and lawful supply determine actual choices.</summary>
        public int MinimumChoiceCount { get; }
        public int MaximumChoiceCount { get; }
        public bool GuaranteeEvo { get; }
        public SideEventGemSelectionPolicy SelectionPolicy => SideEventGemSelectionPolicy.CapturedVersionOne;
        public IReadOnlyList<SideEventGemFrequency> Frequencies { get; }
        public SideEventOptionalBool CanCashOut { get; }
        public SideEventOptionalBool AllowFallbacks { get; }
        public SideEventOptionalBool EnsureNonEvo { get; }
        public SideEventOptionalInt CashOutAmount { get; }
        public string NoTargetFallbackSourceAssetId { get; }
        public SideEventOptionalInt NoTargetFallbackQuantity { get; }
        public SideEventGemOffer(int minimumChoiceCount, int maximumChoiceCount, bool guaranteeEvo,
            IReadOnlyList<SideEventGemFrequency> frequencies, SideEventOptionalBool canCashOut,
            SideEventOptionalBool allowFallbacks, SideEventOptionalBool ensureNonEvo,
            SideEventOptionalInt cashOutAmount, string noTargetFallbackSourceAssetId, SideEventOptionalInt noTargetFallbackQuantity)
        {
            if (minimumChoiceCount < 0 || maximumChoiceCount < minimumChoiceCount) throw new ArgumentOutOfRangeException(nameof(minimumChoiceCount));
            MinimumChoiceCount = minimumChoiceCount; MaximumChoiceCount = maximumChoiceCount; GuaranteeEvo = guaranteeEvo;
            Frequencies = SideEventSourceGuard.Copy(frequencies, nameof(frequencies)); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var frequency in Frequencies) if (!ids.Add(frequency.ContentId)) throw new ArgumentException("Gem frequencies require unique source identities.");
            if (cashOutAmount.IsKnown && cashOutAmount.Value < 0 || noTargetFallbackQuantity.IsKnown && noTargetFallbackQuantity.Value < 0) throw new ArgumentOutOfRangeException(nameof(cashOutAmount));
            CanCashOut = canCashOut; AllowFallbacks = allowFallbacks; EnsureNonEvo = ensureNonEvo; CashOutAmount = cashOutAmount;
            NoTargetFallbackSourceAssetId = SideEventSourceGuard.OptionalText(noTargetFallbackSourceAssetId); NoTargetFallbackQuantity = noTargetFallbackQuantity;
            if (noTargetFallbackQuantity.IsKnown && string.IsNullOrWhiteSpace(noTargetFallbackSourceAssetId)) throw new ArgumentException("A known fallback quantity requires a source identity.");
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventEvoRecipePolicy
    {
        public bool WeaponGroupsFirst { get; }
        public bool ConsumeSelectedInstances { get; }
        public bool FirstHostNeedsLawfulGemSlot { get; }
        public bool InheritOnlyFirstHostGems { get; }
        public bool BaseRequiresWeaponGroupAndUnlockedEvolvedConfig => true;
        public bool RequiredGroupsNeedOwnedCountGreaterThanZero => true;
        public bool RequiredGroupLevelIsComparedToMaximum => false;
        public bool FirstHostNeedsUniqueGemAndAllowedTag => true;
        public bool LaterComponentsRequireTheirGroupAndNotPreviouslySelectedInstance => true;
        public bool LaterComponentsNeedFirstHostSlotAndTagGate => false;
        public bool BothRemainingWeaponAndPassiveGroupsMustBeEmpty => true;
        public bool RemoveBasesSharingSelectedEvolvedOutcome => true;
        public bool RewardUsesPlayerFactoryNotSurvivingHostMutation => true;
        public bool RewardGetsUnconditionallyNewGuid => false;
        public bool CopyGemsUsesCopyMode => true;
        public bool EvolveEffectAppliedWhenCopied => false;
        public IReadOnlyList<SideEventEvoCommitStep> OrderedCommitSteps { get; } = Array.AsReadOnly(new[]
        { SideEventEvoCommitStep.AwaitHammerAnimation, SideEventEvoCommitStep.SnapshotFirstHostBaseConfig,
          SideEventEvoCommitStep.BuildEvolvedModelWithFirstHostGemsAndEvoGem, SideEventEvoCommitStep.AcquireRewardFromPlayerFactory,
          SideEventEvoCommitStep.AddRewardToDrawOrActivePlayerHand, SideEventEvoCommitStep.RemoveEachSelectedOriginalInstance,
          SideEventEvoCommitStep.RemoveSelectedFromDeckAndQueuePoolReturn, SideEventEvoCommitStep.PublishInsertionCompletedThenClose });
        public SideEventOptionalBool AsyncConsumptionIsAtomic => SideEventOptionalBool.Unknown;
        public SideEventOptionalBool PartialHostAndComponentSelectionResumeProven => SideEventOptionalBool.Unknown;
        public SideEventEvoRecipePolicy(bool weaponGroupsFirst, bool consumeSelectedInstances,
            bool firstHostNeedsLawfulGemSlot, bool inheritOnlyFirstHostGems)
        { WeaponGroupsFirst = weaponGroupsFirst; ConsumeSelectedInstances = consumeSelectedInstances;
          FirstHostNeedsLawfulGemSlot = firstHostNeedsLawfulGemSlot; InheritOnlyFirstHostGems = inheritOnlyFirstHostGems; }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventDuplicateCopyPolicy
    {
        public bool PreserveOriginal { get; }
        public bool CopyBaseAndCurrentConfig { get; }
        public bool CopySlotCountAndGemConfigs { get; }
        public bool CopyManaCostModifier { get; }
        public bool CopyTemporaryManaCostModifier { get; }
        public int InitialBreakthroughCount { get; }
        public SideEventCopyFinalState FinalBreakthroughState { get; }
        public SideEventOptionalBool FinalIsEvolved { get; }
        public SideEventDuplicateCopyPolicy(bool preserveOriginal, bool copyBaseAndCurrentConfig,
            bool copySlotCountAndGemConfigs, bool copyManaCostModifier, bool copyTemporaryManaCostModifier,
            int initialBreakthroughCount, SideEventCopyFinalState finalBreakthroughState, SideEventOptionalBool finalIsEvolved)
        {
            if (initialBreakthroughCount < 0 || !Enum.IsDefined(typeof(SideEventCopyFinalState), finalBreakthroughState)) throw new ArgumentOutOfRangeException(nameof(initialBreakthroughCount));
            PreserveOriginal = preserveOriginal; CopyBaseAndCurrentConfig = copyBaseAndCurrentConfig; CopySlotCountAndGemConfigs = copySlotCountAndGemConfigs;
            CopyManaCostModifier = copyManaCostModifier; CopyTemporaryManaCostModifier = copyTemporaryManaCostModifier;
            InitialBreakthroughCount = initialBreakthroughCount; FinalBreakthroughState = finalBreakthroughState; FinalIsEvolved = finalIsEvolved;
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventCrystalTiming
    {
        public float EventTime { get; }
        public float BaseRunSpeed { get; }
        public float RunSpeedIncrease { get; }
        public float MinimumWait { get; }
        public float MaximumWait { get; }
        public float ExitTime { get; }
        public float PeriodicDropInterval { get; }
        public float AutoThrowDelay { get; }
        public float JumpTime { get; }
        public float JumpHeight { get; }
        public SideEventCrystalTiming(float eventTime, float baseRunSpeed, float runSpeedIncrease,
            float minimumWait, float maximumWait, float exitTime, float periodicDropInterval,
            float autoThrowDelay, float jumpTime, float jumpHeight)
        {
            EventTime = SideEventSourceGuard.Nonnegative(eventTime, nameof(eventTime)); BaseRunSpeed = SideEventSourceGuard.Finite(baseRunSpeed, nameof(baseRunSpeed));
            RunSpeedIncrease = SideEventSourceGuard.Finite(runSpeedIncrease, nameof(runSpeedIncrease)); MinimumWait = SideEventSourceGuard.Nonnegative(minimumWait, nameof(minimumWait));
            MaximumWait = SideEventSourceGuard.Nonnegative(maximumWait, nameof(maximumWait)); if (maximumWait < minimumWait) throw new ArgumentException("Wait interval is reversed.");
            ExitTime = SideEventSourceGuard.Nonnegative(exitTime, nameof(exitTime)); PeriodicDropInterval = SideEventSourceGuard.Nonnegative(periodicDropInterval, nameof(periodicDropInterval));
            AutoThrowDelay = SideEventSourceGuard.Nonnegative(autoThrowDelay, nameof(autoThrowDelay)); JumpTime = SideEventSourceGuard.Nonnegative(jumpTime, nameof(jumpTime)); JumpHeight = SideEventSourceGuard.Finite(jumpHeight, nameof(jumpHeight));
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventCrystalReward
    {
        public int MinimumHitXpInclusive { get; }
        public int MaximumHitXpExclusive { get; }
        public bool MultiplyByCompletedFloorPlusOneCappedToFloorCount { get; }
        public bool ApplyGrowthThroughPlayerOwner { get; }
        public float TreasureChestChanceMultiplier { get; }
        public int MinimumFloorForChest { get; }
        public SideEventOptionalInt TotalXp { get; }
        public SideEventCrystalReward(int minimumHitXpInclusive, int maximumHitXpExclusive,
            bool multiplyByCompletedFloorPlusOneCappedToFloorCount, bool applyGrowthThroughPlayerOwner,
            float treasureChestChanceMultiplier, int minimumFloorForChest, SideEventOptionalInt totalXp)
        {
            if (minimumHitXpInclusive < 0 || maximumHitXpExclusive <= minimumHitXpInclusive || minimumFloorForChest < 0) throw new ArgumentOutOfRangeException(nameof(minimumHitXpInclusive));
            if (totalXp.IsKnown && totalXp.Value < 0) throw new ArgumentOutOfRangeException(nameof(totalXp));
            MinimumHitXpInclusive = minimumHitXpInclusive; MaximumHitXpExclusive = maximumHitXpExclusive;
            MultiplyByCompletedFloorPlusOneCappedToFloorCount = multiplyByCompletedFloorPlusOneCappedToFloorCount;
            ApplyGrowthThroughPlayerOwner = applyGrowthThroughPlayerOwner;
            TreasureChestChanceMultiplier = SideEventSourceGuard.Nonnegative(treasureChestChanceMultiplier, nameof(treasureChestChanceMultiplier));
            MinimumFloorForChest = minimumFloorForChest; TotalXp = totalXp;
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventHirePrice
    {
        public int MinimumInclusive { get; }
        public int MaximumInclusive { get; }
        public int PerCompletedFloor { get; }
        public int RoundMultiple { get; }
        public bool RoundBeforeFloorAddition { get; }
        public bool RoundAfterFloorAddition { get; }
        public SideEventHirePrice(int minimumInclusive, int maximumInclusive, int perCompletedFloor,
            int roundMultiple, bool roundBeforeFloorAddition, bool roundAfterFloorAddition)
        {
            if (minimumInclusive < 0 || maximumInclusive < minimumInclusive || perCompletedFloor < 0 || roundMultiple <= 0) throw new ArgumentOutOfRangeException(nameof(minimumInclusive));
            MinimumInclusive = minimumInclusive; MaximumInclusive = maximumInclusive; PerCompletedFloor = perCompletedFloor;
            RoundMultiple = roundMultiple; RoundBeforeFloorAddition = roundBeforeFloorAddition; RoundAfterFloorAddition = roundAfterFloorAddition;
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventAbandonedCartSpecification : SideEventMechanicSpecification
    {
        public int AcceptedBumps { get; }
        public bool AllowSpamBump { get; }
        public string DropConfigContentId { get; }
        public SideEventSourceProvenance DropConfigProvenance { get; }
        public string DropTagContentId { get; }
        public SideEventSourceProvenance DropTagProvenance { get; }
        public int SourceStat { get; }
        public int StatAmount { get; }
        public bool AlwaysManaVariant { get; }
        public bool SeparatePickup => true;
        public SideEventAbandonedCartSpecification(int acceptedBumps, bool allowSpamBump, string dropConfigContentId,
            SideEventSourceProvenance dropConfigProvenance, string dropTagContentId,
            SideEventSourceProvenance dropTagProvenance, int sourceStat, int statAmount, bool alwaysManaVariant) : base(SideEventSourceKind.AbandonedCart)
        {
            if (acceptedBumps <= 0 || sourceStat < 0 || statAmount <= 0) throw new ArgumentOutOfRangeException(nameof(acceptedBumps));
            AcceptedBumps = acceptedBumps; AllowSpamBump = allowSpamBump;
            DropConfigContentId = SideEventSourceGuard.ContentId(dropConfigContentId, "sourceobject");
            DropConfigProvenance = dropConfigProvenance ?? throw new ArgumentNullException(nameof(dropConfigProvenance));
            DropTagContentId = SideEventSourceGuard.ContentId(dropTagContentId, "sourceobject");
            DropTagProvenance = dropTagProvenance ?? throw new ArgumentNullException(nameof(dropTagProvenance));
            if (DropConfigContentId == DropTagContentId || SideEventSourceGuard.SameSourceIdentity(DropConfigProvenance, DropTagProvenance))
                throw new ArgumentException("Drop configuration and drop tag need their distinct explicit identities and source objects.");
            SourceStat = sourceStat; StatAmount = statAmount; AlwaysManaVariant = alwaysManaVariant;
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventEvoStatueSpecification : SideEventMechanicSpecification
    {
        public SideEventOptionalInt AcceptedBumps { get; }
        public SideEventGemOffer Offer { get; }
        public SideEventEvoRecipePolicy Recipe { get; }
        public SideEventEvoStatueSpecification(SideEventOptionalInt acceptedBumps, SideEventGemOffer offer,
            SideEventEvoRecipePolicy recipe) : base(SideEventSourceKind.EvoStatue)
        {
            if (acceptedBumps.IsKnown && acceptedBumps.Value <= 0) throw new ArgumentOutOfRangeException(nameof(acceptedBumps));
            AcceptedBumps = acceptedBumps; Offer = offer ?? throw new ArgumentNullException(nameof(offer)); Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventManaStatueSpecification : SideEventMechanicSpecification
    {
        public int AcceptedBumps { get; }
        public bool AllowSpamBump { get; }
        public SideEventManaReward Reward { get; }
        public bool SeparatePickup => true;
        public SideEventManaStatueSpecification(int acceptedBumps, bool allowSpamBump, SideEventManaReward reward) : base(SideEventSourceKind.ManaStatue)
        { if (acceptedBumps <= 0) throw new ArgumentOutOfRangeException(nameof(acceptedBumps)); AcceptedBumps = acceptedBumps; AllowSpamBump = allowSpamBump; Reward = reward ?? throw new ArgumentNullException(nameof(reward)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventArcanaChoiceSpecification : SideEventMechanicSpecification
    {
        public int ChoicesToShow { get; }
        public int SourceArcanaType { get; }
        public bool OutsideClickCloses { get; }
        public SideEventOptionalBool ViewIsEdgeEvent { get; }
        public SideEventArcanaChoiceSpecification(int choicesToShow, int sourceArcanaType, bool outsideClickCloses,
            SideEventOptionalBool viewIsEdgeEvent) : base(SideEventSourceKind.ArcanaChoice)
        { if (choicesToShow <= 0 || sourceArcanaType < 0) throw new ArgumentOutOfRangeException(nameof(choicesToShow));
          ChoicesToShow = choicesToShow; SourceArcanaType = sourceArcanaType; OutsideClickCloses = outsideClickCloses; ViewIsEdgeEvent = viewIsEdgeEvent; }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventCardStatOfferingSpecification : SideEventMechanicSpecification
    {
        public SideEventOfferingSelection Selection { get; }
        public IReadOnlyList<SideEventStatSetting> StatSettings { get; }
        public int CashOutAmount { get; }
        public bool CashOutUsesGreed { get; }
        public SideEventStatRewardFormula Formula => SideEventStatRewardFormula.FloorMinimumOne;
        public bool ApplyEverySupportedStat => true;
        public SideEventCardStatOfferingSpecification(SideEventOfferingSelection selection,
            IReadOnlyList<SideEventStatSetting> statSettings, int cashOutAmount, bool cashOutUsesGreed) : base(SideEventSourceKind.CardStatOffering)
        {
            Selection = selection ?? throw new ArgumentNullException(nameof(selection)); StatSettings = SideEventSourceGuard.Copy(statSettings, nameof(statSettings));
            if (cashOutAmount < 0) throw new ArgumentOutOfRangeException(nameof(cashOutAmount)); var ids = new HashSet<int>();
            foreach (var stat in StatSettings) if (!ids.Add(stat.SourceStat)) throw new ArgumentException("Supported stat IDs must be unique.");
            CashOutAmount = cashOutAmount; CashOutUsesGreed = cashOutUsesGreed;
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventChickenOfferingSpecification : SideEventMechanicSpecification
    {
        public SideEventOfferingSelection Selection { get; }
        public int FoodCount { get; }
        public string FoodMetricId { get; }
        public IReadOnlyList<int> FoodCollectionTypes { get; }
        public SideEventHealingReward Healing { get; }
        public int GoldCost => 0;
        public bool CreditFoodBeforeHealing => true;
        public SideEventChickenOfferingSpecification(SideEventOfferingSelection selection, int foodCount,
            string foodMetricId, IReadOnlyList<int> foodCollectionTypes, SideEventHealingReward healing) : base(SideEventSourceKind.ChickenOffering)
        { Selection = selection ?? throw new ArgumentNullException(nameof(selection)); if (foodCount <= 0) throw new ArgumentOutOfRangeException(nameof(foodCount));
          FoodCount = foodCount; FoodMetricId = SideEventSourceGuard.Text(foodMetricId, nameof(foodMetricId)); FoodCollectionTypes = SideEventManaReward.CopyCollections(foodCollectionTypes);
          Healing = healing ?? throw new ArgumentNullException(nameof(healing)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventCrystalSpecification : SideEventMechanicSpecification
    {
        public SideEventCrystalTiming Timing { get; }
        public SideEventCrystalReward Reward { get; }
        public bool SacrificesPersistentCards => false;
        public SideEventCrystalSpecification(SideEventCrystalTiming timing, SideEventCrystalReward reward) : base(SideEventSourceKind.Crystal)
        { Timing = timing ?? throw new ArgumentNullException(nameof(timing)); Reward = reward ?? throw new ArgumentNullException(nameof(reward)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventDuplicateOfferingSpecification : SideEventMechanicSpecification
    {
        public SideEventOfferingSelection Selection { get; }
        public SideEventDuplicateCopyPolicy CopyPolicy { get; }
        public int GoldCost => 0;
        public SideEventDuplicateOfferingSpecification(SideEventOfferingSelection selection, SideEventDuplicateCopyPolicy copyPolicy) : base(SideEventSourceKind.DuplicateOffering)
        { Selection = selection ?? throw new ArgumentNullException(nameof(selection)); CopyPolicy = copyPolicy ?? throw new ArgumentNullException(nameof(copyPolicy)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventGemChestSpecification : SideEventMechanicSpecification
    {
        public SideEventGemOffer Offer { get; }
        public SideEventGemChestSpecification(SideEventGemOffer offer) : base(SideEventSourceKind.GemChest)
        { Offer = offer ?? throw new ArgumentNullException(nameof(offer)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSilverGemChestSpecification : SideEventMechanicSpecification
    {
        public SideEventOfferingSelection Selection { get; }
        public IReadOnlyList<SideEventSourceObjectReference> RequestedCardTypes { get; }
        public bool AvoidDuplicateCardTypes { get; }
        public SideEventGemOffer Offer { get; }
        public SideEventSilverGemChestSpecification(SideEventOfferingSelection selection,
            IReadOnlyList<SideEventSourceObjectReference> requestedCardTypes, bool avoidDuplicateCardTypes,
            SideEventGemOffer offer) : base(SideEventSourceKind.SilverGemChest)
        {
            Selection = selection ?? throw new ArgumentNullException(nameof(selection)); if (requestedCardTypes == null) throw new ArgumentNullException(nameof(requestedCardTypes));
            var copy = new SideEventSourceObjectReference[requestedCardTypes.Count];
            for (int i = 0; i < copy.Length; i++) { SideEventSourceGuard.Reference(requestedCardTypes[i]); if (requestedCardTypes[i].PathId == 0) throw new ArgumentException("Requested card type requires a source reference."); copy[i] = requestedCardTypes[i]; }
            RequestedCardTypes = Array.AsReadOnly(copy); AvoidDuplicateCardTypes = avoidDuplicateCardTypes; Offer = offer ?? throw new ArgumentNullException(nameof(offer));
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventGunForHireSpecification : SideEventMechanicSpecification
    {
        public SideEventHirePrice BuyPrice { get; }
        public SideEventHirePrice PityPrice { get; }
        public bool PityUsesGreed { get; }
        public bool ExcludeOwnedFcc => true;
        public bool RequireUnlockedFcc => true;
        public bool SaveChosenFccAndCostAndPity => true;
        public bool KeepValidSavedChoice => true;
        public SideEventOptionalBool RerollInvalidSavedChoice { get; }
        public SideEventGunForHireSpecification(SideEventHirePrice buyPrice, SideEventHirePrice pityPrice,
            bool pityUsesGreed, SideEventOptionalBool rerollInvalidSavedChoice) : base(SideEventSourceKind.GunForHire)
        { BuyPrice = buyPrice ?? throw new ArgumentNullException(nameof(buyPrice)); PityPrice = pityPrice ?? throw new ArgumentNullException(nameof(pityPrice));
          PityUsesGreed = pityUsesGreed; RerollInvalidSavedChoice = rerollInvalidSavedChoice; }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventManaOfferingSpecification : SideEventMechanicSpecification
    {
        public SideEventOfferingSelection Selection { get; }
        public SideEventManaReward Reward { get; }
        public int GoldCost => 0;
        public SideEventManaOfferingSpecification(SideEventOfferingSelection selection, SideEventManaReward reward) : base(SideEventSourceKind.ManaOffering)
        { Selection = selection ?? throw new ArgumentNullException(nameof(selection)); Reward = reward ?? throw new ArgumentNullException(nameof(reward)); }
    }
}
