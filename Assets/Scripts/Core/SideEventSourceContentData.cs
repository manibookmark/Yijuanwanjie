using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Shared author/checkpoint source wire. Missing collections remain null;
    /// source identity, occurrence order and source-only mappings are explicit.
    /// This DTO does not qualify, select, place or execute an event.</summary>
    [Serializable]
    public sealed class SideEventSourceContentData
    {
        public string contentId;
        public List<SideEventSourceRootData> roots;
        public List<SideEventSourceGroupData> groups;
        public List<SideEventSourceLayerData> layers;
    }

    [Serializable]
    public sealed class SideEventSourceProvenanceData
    {
        public string alias;
        public string sourceFile;
        public long pathId;
        public long byteStart;
        public int byteSize;
        public string sha256;
    }

    [Serializable]
    public sealed class SideEventSourceObjectReferenceData
    {
        public string ownerSourceFile;
        public int fileIndex;
        public long pathId;
    }

    [Serializable]
    public sealed class SideEventSourceAssetReferenceData
    {
        public string assetId;
        public string editorAssetGuid;
    }

    [Serializable]
    public sealed class SideEventSourceVectorData
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public sealed class SideEventSourceCameraData
    {
        public bool overrideRotation;
        public SideEventSourceVectorData rotation;
        public bool overrideFov;
        public float fov;
    }

    [Serializable]
    public sealed class SideEventSourcePlacementData
    {
        public bool overrideTileOffset;
        public float tileOffset;
        // JSON-friendly, explicit tri-state; no nullable-value DTO defaults.
        public string requireNonExitEdge;
    }

    [Serializable]
    public sealed class SideEventSourceViewData
    {
        public SideEventSourceObjectReferenceData destructible;
        public SideEventSourceObjectReferenceData passiveView;
        public string destructibleEventName;
        public string passiveEventName;
        public string edgeBumpSoundKey;
    }

    [Serializable]
    public sealed class SideEventRootConfigurationData
    {
        public bool allowDetailsOverride;
        public bool isEdgeEvent;
        public SideEventSourcePlacementData placement;
        public SideEventSourceCameraData camera;
        public SideEventSourceViewData view;
    }

    [Serializable]
    public sealed class SideEventSourceDetailsData
    {
        public string sourceAssetId;
        public SideEventSourceObjectReferenceData icon;
        public float iconScale;
        public SideEventSourceObjectReferenceData marker;
        public bool clearWhenClearingEncounters;
        public SideEventSourceAssetReferenceData defaultReference;
        public SideEventSourceProvenanceData provenance;
    }

    [Serializable]
    public sealed class SideEventSourceRootData
    {
        public string contentId;
        public string sourceAssetId;
        public SideEventRootConfigurationData configuration;
        public SideEventSourceDetailsData details;
        public SideEventSourceRequirementData requirement;
        public SideEventMechanicSpecificationData mechanic;
        public string mapping;
        public SideEventSourceProvenanceData provenance;
    }

    [Serializable]
    public sealed class SideEventSourceCachedNumberData
    {
        public string kind;
        public float finiteValue;
    }

    [Serializable]
    public sealed class SideEventSourceMemberData
    {
        public string rootContentId;
        public int weight;
        public bool guaranteed;
        public bool ambush;
        public bool overrideDetails;
        public SideEventSourceAssetReferenceData eventReference;
        public SideEventSourceAssetReferenceData detailsOverrideReference;
        public SideEventSourceCachedNumberData cachedProbability;
    }

    [Serializable]
    public sealed class SideEventSourceGroupData
    {
        public string contentId;
        public SideEventSourceProvenanceData provenance;
        public float cachedTotalWeight;
        public List<SideEventSourceMemberData> members;
    }

    [Serializable]
    public sealed class SideEventSourceOccurrenceData
    {
        public SideEventSourceProvenanceData layerProvenance;
        public string serializedField;
        public SideEventSourceObjectReferenceData dungeonChoiceConfigReference;
    }

    [Serializable]
    public sealed class SideEventSourceSlotData
    {
        public string contentId;
        public SideEventSourceOccurrenceData occurrence;
        public string mode;
        public string groupContentId;
        public List<SideEventSourceMemberData> inlineMembers;
        public float cachedTotalWeight;
    }

    [Serializable]
    public sealed class SideEventSourceLayerData
    {
        public string contentId;
        public SideEventSourceProvenanceData provenance;
        public int attemptTarget;
        public List<SideEventSourceSlotData> slots;
        public int maxAmbushes;
        public bool overrideAmbushChance;
        public float ambushChance;
    }

    [Serializable]
    public sealed class SideEventSourceRequirementData
    {
        public string kind;
        public string sourceType;
        public SideEventSourceRelicRequirementData relic;
        public SideEventSourceMetricRequirementData metric;
        public SideEventSourceDungeonRequirementData dungeon;
    }

    [Serializable]
    public sealed class SideEventSourceRelicRequirementData
    {
        public string sourceAssetId;
        public SideEventSourceProvenanceData provenance;
    }

    [Serializable]
    public sealed class SideEventSourceMetricRequirementData
    {
        public string sourceAssetId;
        public string achievementId;
        public bool allowInDemo;
        public string metricId;
        public int metricType;
        public int collectionType;
        public float unlockValue;
        public SideEventSourceProvenanceData provenance;
        public int rewardType;
        public bool hasUnlockReward;
    }

    [Serializable]
    public sealed class SideEventSourceDungeonRequirementData
    {
        public string sourceAssetId;
        public string achievementId;
        public bool allowInDemo;
        public SideEventSourceDungeonConditionData condition;
        public SideEventSourceProvenanceData provenance;
        public int rewardType;
        public bool hasUnlockReward;
    }

    [Serializable]
    public sealed class SideEventSourceDungeonConditionData
    {
        public bool checkAnyDungeon;
        public SideEventSourceObjectReferenceData dungeon;
        public bool requiresFcc;
        public SideEventSourceObjectReferenceData fcc;
        public int achievementType;
        public float targetPlayerLevel;
        public int floorNumber;
        public bool checkFloor;
        public SideEventSourceObjectReferenceData encounter;
    }

    [Serializable]
    public sealed class SideEventOptionalIntData { public bool isKnown; public int value; }
    [Serializable]
    public sealed class SideEventOptionalBoolData { public bool isKnown; public bool value; }

    /// <summary>Exactly one kind branch is nonnull. Inactive branches are explicit
    /// null, not omitted/default payloads that could masquerade as captured facts.</summary>
    [Serializable]
    public sealed class SideEventMechanicSpecificationData
    {
        public string kind;
        public SideEventAbandonedCartData abandonedCart;
        public SideEventEvoStatueData evoStatue;
        public SideEventManaStatueData manaStatue;
        public SideEventArcanaChoiceData arcanaChoice;
        public SideEventCardStatOfferingData cardStatOffering;
        public SideEventChickenOfferingData chickenOffering;
        public SideEventCrystalData crystal;
        public SideEventDuplicateOfferingData duplicateOffering;
        public SideEventGemChestData gemChest;
        public SideEventSilverGemChestData silverGemChest;
        public SideEventGunForHireData gunForHire;
        public SideEventManaOfferingData manaOffering;
    }

    [Serializable]
    public sealed class SideEventOfferingSelectionData
    {
        public int cardsRequired;
        public bool destroySelected;
        public bool excludeFcc;
        public bool requireOfferingFlag;
        public bool outsideClickCloses;
        public bool requireSupportedModifiedStat;
    }
    [Serializable]
    public sealed class SideEventManaRewardData
    {
        public int requestedAmount;
        public bool increaseStarting;
        public int currentMinimum;
        public int currentMaximum;
        public string metricId;
        public List<int> collectionTypes;
        public bool accountPermanentMana;
        public bool modifiesTemporaryMana;
    }
    [Serializable]
    public sealed class SideEventHealingRewardData
    {
        public int iterations;
        public int baseAmount;
        public string modifierSourceAssetId;
        public float modifier;
        public bool reloadBasePerIteration;
        public bool clampToCurrentHealthBounds;
        public bool increaseMaxHealth;
        public string actualHealMetricId;
        public bool countOnlyPositiveActualDelta;
    }
    [Serializable]
    public sealed class SideEventStatSettingData
    {
        public int sourceStat;
        public int additionalChangeAmount;
        public float modifier;
        public int baseChangeAmount;
    }
    [Serializable]
    public sealed class SideEventGemFrequencyData
    {
        public string contentId;
        public SideEventSourceProvenanceData provenance;
        public float chestWeight;
        public float levelUpWeight;
        public float levelUpLuckFactor;
    }
    [Serializable]
    public sealed class SideEventGemOfferData
    {
        public int minimumChoiceCount;
        public int maximumChoiceCount;
        public bool guaranteeEvo;
        public List<SideEventGemFrequencyData> frequencies;
        public SideEventOptionalBoolData canCashOut;
        public SideEventOptionalBoolData allowFallbacks;
        public SideEventOptionalBoolData ensureNonEvo;
        public SideEventOptionalIntData cashOutAmount;
        public string noTargetFallbackSourceAssetId;
        public SideEventOptionalIntData noTargetFallbackQuantity;
    }
    [Serializable]
    public sealed class SideEventEvoRecipeData
    {
        public bool weaponGroupsFirst;
        public bool consumeSelectedInstances;
        public bool firstHostNeedsLawfulGemSlot;
        public bool inheritOnlyFirstHostGems;
    }
    [Serializable]
    public sealed class SideEventDuplicateCopyData
    {
        public bool preserveOriginal;
        public bool copyBaseAndCurrentConfig;
        public bool copySlotCountAndGemConfigs;
        public bool copyManaCostModifier;
        public bool copyTemporaryManaCostModifier;
        public int initialBreakthroughCount;
        public string finalBreakthroughState;
        public SideEventOptionalBoolData finalIsEvolved;
    }
    [Serializable]
    public sealed class SideEventCrystalTimingData
    {
        public float eventTime;
        public float baseRunSpeed;
        public float runSpeedIncrease;
        public float minimumWait;
        public float maximumWait;
        public float exitTime;
        public float periodicDropInterval;
        public float autoThrowDelay;
        public float jumpTime;
        public float jumpHeight;
    }
    [Serializable]
    public sealed class SideEventCrystalRewardData
    {
        public int minimumHitXpInclusive;
        public int maximumHitXpExclusive;
        public bool multiplyByCompletedFloorPlusOneCappedToFloorCount;
        public bool applyGrowthThroughPlayerOwner;
        public float treasureChestChanceMultiplier;
        public int minimumFloorForChest;
        public SideEventOptionalIntData totalXp;
    }
    [Serializable]
    public sealed class SideEventHirePriceData
    {
        public int minimumInclusive;
        public int maximumInclusive;
        public int perCompletedFloor;
        public int roundMultiple;
        public bool roundBeforeFloorAddition;
        public bool roundAfterFloorAddition;
    }

    [Serializable]
    public sealed class SideEventAbandonedCartData
    {
        public int acceptedBumps;
        public bool allowSpamBump;
        public string dropConfigContentId;
        public SideEventSourceProvenanceData dropConfigProvenance;
        public string dropTagContentId;
        public SideEventSourceProvenanceData dropTagProvenance;
        public int sourceStat;
        public int statAmount;
        public bool alwaysManaVariant;
    }
    [Serializable]
    public sealed class SideEventEvoStatueData
    {
        public SideEventOptionalIntData acceptedBumps;
        public SideEventGemOfferData offer;
        public SideEventEvoRecipeData recipe;
    }
    [Serializable]
    public sealed class SideEventManaStatueData
    {
        public int acceptedBumps;
        public bool allowSpamBump;
        public SideEventManaRewardData reward;
    }
    [Serializable]
    public sealed class SideEventArcanaChoiceData
    {
        public int choicesToShow;
        public int sourceArcanaType;
        public bool outsideClickCloses;
        public SideEventOptionalBoolData viewIsEdgeEvent;
    }
    [Serializable]
    public sealed class SideEventCardStatOfferingData
    {
        public SideEventOfferingSelectionData selection;
        public List<SideEventStatSettingData> statSettings;
        public int cashOutAmount;
        public bool cashOutUsesGreed;
    }
    [Serializable]
    public sealed class SideEventChickenOfferingData
    {
        public SideEventOfferingSelectionData selection;
        public int foodCount;
        public string foodMetricId;
        public List<int> foodCollectionTypes;
        public SideEventHealingRewardData healing;
    }
    [Serializable]
    public sealed class SideEventCrystalData
    {
        public SideEventCrystalTimingData timing;
        public SideEventCrystalRewardData reward;
    }
    [Serializable]
    public sealed class SideEventDuplicateOfferingData
    {
        public SideEventOfferingSelectionData selection;
        public SideEventDuplicateCopyData copyPolicy;
    }
    [Serializable]
    public sealed class SideEventGemChestData { public SideEventGemOfferData offer; }
    [Serializable]
    public sealed class SideEventSilverGemChestData
    {
        public SideEventOfferingSelectionData selection;
        public List<SideEventSourceObjectReferenceData> requestedCardTypes;
        public bool avoidDuplicateCardTypes;
        public SideEventGemOfferData offer;
    }
    [Serializable]
    public sealed class SideEventGunForHireData
    {
        public SideEventHirePriceData buyPrice;
        public SideEventHirePriceData pityPrice;
        public bool pityUsesGreed;
        public SideEventOptionalBoolData rerollInvalidSavedChoice;
    }
    [Serializable]
    public sealed class SideEventManaOfferingData
    {
        public SideEventOfferingSelectionData selection;
        public SideEventManaRewardData reward;
    }
}
