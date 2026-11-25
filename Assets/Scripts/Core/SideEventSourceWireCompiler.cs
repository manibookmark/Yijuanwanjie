using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Converts the one shared source wire into closed immutable specifications.
    /// Core constructors remain the sole source semantic validators; this boundary
    /// owns enum spelling and explicit DTO conversion, not author-volume registration,
    /// qualification or execution. Its conversion method count triggers review but
    /// all methods belong to this single mapping boundary. Reassess on a new wire family.
    /// </summary>
    public static class SideEventSourceWireCompiler
    {
        public static bool TryCompile(SideEventSourceContentData data,
            out SideEventSourceDefinition definition, out IReadOnlyList<string> errors)
        {
            definition = null;
            try
            {
                Required(data, "dataset");
                definition = new SideEventSourceDefinition(data.contentId,
                    Map(data.roots, Root, "roots"), Map(data.groups, Group, "groups"),
                    Map(data.layers, Layer, "layers"));
                errors = Array.AsReadOnly(Array.Empty<string>());
                return true;
            }
            catch (ArgumentException error)
            {
                errors = Array.AsReadOnly(new[] { error.Message });
                return false;
            }
            catch (OverflowException error)
            {
                errors = Array.AsReadOnly(new[] { error.Message });
                return false;
            }
        }

        private static IReadOnlyList<TTarget> Map<TSource, TTarget>(IReadOnlyList<TSource> values,
            Func<TSource, TTarget> compile, string path) where TSource : class
        {
            Required(values, path);
            var result = new List<TTarget>(values.Count);
            for (int i = 0; i < values.Count; i++) result.Add(compile(Required(values[i], path + "[" + i + "]")));
            return result.AsReadOnly();
        }

        private static T Required<T>(T value, string path) where T : class
            => value ?? throw new ArgumentException(path + ": an explicit nonnull value is required.");

        private static TEnum EnumValue<TEnum>(string value, string path) where TEnum : struct
        {
            if (!Enum.TryParse(value, false, out TEnum result) || !Enum.IsDefined(typeof(TEnum), result) ||
                !string.Equals(result.ToString(), value, StringComparison.Ordinal))
                throw new ArgumentException(path + ": an explicit canonical enum name is required.");
            return result;
        }

        private static SideEventSourceProvenance Provenance(SideEventSourceProvenanceData data)
        {
            Required(data, "provenance");
            return new SideEventSourceProvenance(data.alias, data.sourceFile, data.pathId,
                data.byteStart, data.byteSize, data.sha256);
        }
        private static SideEventSourceObjectReference ObjectReference(SideEventSourceObjectReferenceData data)
        {
            Required(data, "objectReference");
            return new SideEventSourceObjectReference(data.ownerSourceFile, data.fileIndex, data.pathId);
        }
        private static SideEventSourceAssetReference AssetReference(SideEventSourceAssetReferenceData data)
        {
            Required(data, "assetReference");
            return new SideEventSourceAssetReference(data.assetId, data.editorAssetGuid);
        }
        private static SideEventSourceCachedNumber CachedNumber(SideEventSourceCachedNumberData data)
        {
            Required(data, "cachedNumber");
            if (data.kind == "Finite") return new SideEventSourceCachedNumber(data.finiteValue);
            if (data.kind == "PositiveInfinity" && BitConverter.ToInt32(BitConverter.GetBytes(data.finiteValue), 0) == 0)
                return SideEventSourceCachedNumber.PositiveInfinity;
            throw new ArgumentException("cachedNumber: Finite or PositiveInfinity with explicit +0 finiteValue is required.");
        }
        private static SideEventSourceMember Member(SideEventSourceMemberData data)
            => new SideEventSourceMember(data.rootContentId, data.weight, data.guaranteed,
                data.ambush, data.overrideDetails, AssetReference(data.eventReference),
                AssetReference(data.detailsOverrideReference), CachedNumber(data.cachedProbability));
        private static SideEventSourceGroup Group(SideEventSourceGroupData data)
            => new SideEventSourceGroup(data.contentId, Provenance(data.provenance), data.cachedTotalWeight,
                Map(data.members, Member, "group.members"));
        private static SideEventSourceSlot Slot(SideEventSourceSlotData data)
        {
            SideEventSourceOccurrenceData occurrence = Required(data.occurrence, "slot.occurrence");
            return new SideEventSourceSlot(data.contentId,
                new SideEventSourceOccurrence(Provenance(occurrence.layerProvenance), occurrence.serializedField,
                    ObjectReference(occurrence.dungeonChoiceConfigReference)),
                EnumValue<SideEventSourceSlotMode>(data.mode, "slot.mode"), data.groupContentId,
                Map(data.inlineMembers, Member, "slot.inlineMembers"), data.cachedTotalWeight);
        }
        private static SideEventSourceLayer Layer(SideEventSourceLayerData data)
            => new SideEventSourceLayer(data.contentId, Provenance(data.provenance), data.attemptTarget,
                Map(data.slots, Slot, "layer.slots"), data.maxAmbushes, data.overrideAmbushChance, data.ambushChance);

        private static SideEventRootConfiguration Configuration(SideEventRootConfigurationData data)
        {
            Required(data, "configuration");
            SideEventSourcePlacementData placement = Required(data.placement, "configuration.placement");
            bool? requireEdge;
            switch (placement.requireNonExitEdge)
            {
                case "Unknown": requireEdge = null; break;
                case "True": requireEdge = true; break;
                case "False": requireEdge = false; break;
                default: throw new ArgumentException("placement.requireNonExitEdge: Unknown, True or False is required.");
            }
            SideEventSourceCameraData camera = Required(data.camera, "configuration.camera");
            SideEventSourceVectorData rotation = Required(camera.rotation, "camera.rotation");
            SideEventSourceViewData view = Required(data.view, "configuration.view");
            return new SideEventRootConfiguration(data.allowDetailsOverride, data.isEdgeEvent,
                new SideEventSourcePlacement(placement.overrideTileOffset, placement.tileOffset, requireEdge),
                new SideEventSourceCamera(camera.overrideRotation, new SideEventSourceVector3(rotation.x, rotation.y, rotation.z),
                    camera.overrideFov, camera.fov),
                new SideEventSourceView(ObjectReference(view.destructible), OptionalObjectReference(view.passiveView),
                    view.destructibleEventName, view.passiveEventName, view.edgeBumpSoundKey));
        }

        private static SideEventSourceRoot Root(SideEventSourceRootData data)
            => new SideEventSourceRoot(data.contentId, data.sourceAssetId, Configuration(data.configuration),
                Details(Required(data.details, "details")), Requirement(Required(data.requirement, "requirement")),
                Mechanic(Required(data.mechanic, "mechanic")), EnumValue<SideEventProjectMapping>(data.mapping, "mapping"),
                Provenance(data.provenance));

        private static SideEventSourceObjectReference? OptionalObjectReference(SideEventSourceObjectReferenceData data)
            => data == null ? (SideEventSourceObjectReference?)null : ObjectReference(data);

        private static SideEventSourceDetails Details(SideEventSourceDetailsData data)
            => new SideEventSourceDetails(data.sourceAssetId, ObjectReference(data.icon), data.iconScale,
                ObjectReference(data.marker), data.clearWhenClearingEncounters,
                AssetReference(data.defaultReference), Provenance(data.provenance));

        private static SideEventSourceRequirement Requirement(SideEventSourceRequirementData data)
        {
            int branches = (data.relic == null ? 0 : 1) + (data.metric == null ? 0 : 1) + (data.dungeon == null ? 0 : 1);
            if (data.kind == "Base" && branches == 0) return new SideEventBaseRequirement(data.sourceType);
            if (branches != 1) throw new ArgumentException("requirement: exactly its declared kind branch is required.");
            if (data.kind == "Relic" && data.relic != null)
                return new SideEventRelicRequirement(data.relic.sourceAssetId,
                    Provenance(data.relic.provenance), data.sourceType);
            if (data.kind == "MetricAchievement" && data.metric != null)
            {
                SideEventSourceMetricRequirementData metric = data.metric;
                return new SideEventMetricRequirement(metric.sourceAssetId, metric.achievementId, metric.allowInDemo,
                    metric.metricId, metric.metricType, metric.collectionType, metric.unlockValue,
                    Provenance(metric.provenance), data.sourceType, metric.rewardType, metric.hasUnlockReward);
            }
            if (data.kind == "DungeonAchievement" && data.dungeon != null)
            {
                SideEventSourceDungeonRequirementData dungeon = data.dungeon;
                SideEventSourceDungeonConditionData condition = Required(dungeon.condition, "requirement.dungeon.condition");
                return new SideEventDungeonRequirement(dungeon.sourceAssetId, dungeon.achievementId, dungeon.allowInDemo,
                    new SideEventSourceDungeonCondition(condition.checkAnyDungeon, ObjectReference(condition.dungeon),
                        condition.requiresFcc, ObjectReference(condition.fcc), condition.achievementType,
                        condition.targetPlayerLevel, condition.floorNumber, condition.checkFloor,
                        ObjectReference(condition.encounter)), Provenance(dungeon.provenance), data.sourceType,
                    dungeon.rewardType, dungeon.hasUnlockReward);
            }
            throw new ArgumentException("requirement: a canonical kind matching its sole branch is required.");
        }

        private static SideEventOptionalInt OptionalInt(SideEventOptionalIntData data)
        {
            Required(data, "optionalInt");
            if (data.isKnown) return new SideEventOptionalInt(data.value);
            if (data.value != 0) throw new ArgumentException("An unknown integer cannot carry a hidden value.");
            return SideEventOptionalInt.Unknown;
        }
        private static SideEventOptionalBool OptionalBool(SideEventOptionalBoolData data)
        {
            Required(data, "optionalBool");
            if (data.isKnown) return new SideEventOptionalBool(data.value);
            if (data.value) throw new ArgumentException("An unknown flag cannot carry a hidden true value.");
            return SideEventOptionalBool.Unknown;
        }

        private static SideEventOfferingSelection Selection(SideEventOfferingSelectionData data)
        {
            Required(data, "selection");
            return new SideEventOfferingSelection(data.cardsRequired, data.destroySelected, data.excludeFcc,
                data.requireOfferingFlag, data.outsideClickCloses, data.requireSupportedModifiedStat);
        }
        private static SideEventManaReward ManaReward(SideEventManaRewardData data)
        {
            Required(data, "manaReward");
            return new SideEventManaReward(data.requestedAmount, data.increaseStarting, data.currentMinimum,
                data.currentMaximum, data.metricId, Required(data.collectionTypes, "manaReward.collectionTypes"),
                data.accountPermanentMana, data.modifiesTemporaryMana);
        }
        private static SideEventHealingReward Healing(SideEventHealingRewardData data)
        {
            Required(data, "healing");
            return new SideEventHealingReward(data.iterations, data.baseAmount, data.modifierSourceAssetId,
                data.modifier, data.reloadBasePerIteration, data.clampToCurrentHealthBounds, data.increaseMaxHealth,
                data.actualHealMetricId, data.countOnlyPositiveActualDelta);
        }
        private static SideEventStatSetting Stat(SideEventStatSettingData data)
            => new SideEventStatSetting(data.sourceStat, data.additionalChangeAmount, data.modifier, data.baseChangeAmount);
        private static SideEventGemFrequency Frequency(SideEventGemFrequencyData data)
            => new SideEventGemFrequency(data.contentId, Provenance(data.provenance), data.chestWeight, data.levelUpWeight, data.levelUpLuckFactor);
        private static SideEventGemOffer GemOffer(SideEventGemOfferData data)
        {
            Required(data, "gemOffer");
            return new SideEventGemOffer(data.minimumChoiceCount, data.maximumChoiceCount, data.guaranteeEvo,
                Map(data.frequencies, Frequency, "gemOffer.frequencies"), OptionalBool(data.canCashOut),
                OptionalBool(data.allowFallbacks), OptionalBool(data.ensureNonEvo), OptionalInt(data.cashOutAmount),
                data.noTargetFallbackSourceAssetId, OptionalInt(data.noTargetFallbackQuantity));
        }
        private static SideEventEvoRecipePolicy Recipe(SideEventEvoRecipeData data)
        {
            Required(data, "recipe");
            return new SideEventEvoRecipePolicy(data.weaponGroupsFirst, data.consumeSelectedInstances,
                data.firstHostNeedsLawfulGemSlot, data.inheritOnlyFirstHostGems);
        }
        private static SideEventDuplicateCopyPolicy CopyPolicy(SideEventDuplicateCopyData data)
        {
            Required(data, "copyPolicy");
            return new SideEventDuplicateCopyPolicy(data.preserveOriginal, data.copyBaseAndCurrentConfig,
                data.copySlotCountAndGemConfigs, data.copyManaCostModifier, data.copyTemporaryManaCostModifier,
                data.initialBreakthroughCount,
                EnumValue<SideEventCopyFinalState>(data.finalBreakthroughState, "copyPolicy.finalBreakthroughState"),
                OptionalBool(data.finalIsEvolved));
        }
        private static SideEventCrystalTiming Timing(SideEventCrystalTimingData data)
        {
            Required(data, "crystal.timing");
            return new SideEventCrystalTiming(data.eventTime, data.baseRunSpeed, data.runSpeedIncrease,
                data.minimumWait, data.maximumWait, data.exitTime, data.periodicDropInterval,
                data.autoThrowDelay, data.jumpTime, data.jumpHeight);
        }
        private static SideEventCrystalReward CrystalReward(SideEventCrystalRewardData data)
        {
            Required(data, "crystal.reward");
            return new SideEventCrystalReward(data.minimumHitXpInclusive, data.maximumHitXpExclusive,
                data.multiplyByCompletedFloorPlusOneCappedToFloorCount, data.applyGrowthThroughPlayerOwner,
                data.treasureChestChanceMultiplier, data.minimumFloorForChest, OptionalInt(data.totalXp));
        }
        private static SideEventHirePrice Price(SideEventHirePriceData data)
        {
            Required(data, "hirePrice");
            return new SideEventHirePrice(data.minimumInclusive, data.maximumInclusive, data.perCompletedFloor,
                data.roundMultiple, data.roundBeforeFloorAddition, data.roundAfterFloorAddition);
        }

        private static SideEventMechanicSpecification Mechanic(SideEventMechanicSpecificationData data)
        {
            SideEventSourceKind kind = EnumValue<SideEventSourceKind>(data.kind, "mechanic.kind");
            int count = (data.abandonedCart == null ? 0 : 1) + (data.evoStatue == null ? 0 : 1) +
                (data.manaStatue == null ? 0 : 1) + (data.arcanaChoice == null ? 0 : 1) +
                (data.cardStatOffering == null ? 0 : 1) + (data.chickenOffering == null ? 0 : 1) +
                (data.crystal == null ? 0 : 1) + (data.duplicateOffering == null ? 0 : 1) +
                (data.gemChest == null ? 0 : 1) + (data.silverGemChest == null ? 0 : 1) +
                (data.gunForHire == null ? 0 : 1) + (data.manaOffering == null ? 0 : 1);
            if (count != 1) throw new ArgumentException("mechanic: exactly one declared kind branch is required.");
            switch (kind)
            {
                case SideEventSourceKind.AbandonedCart:
                    SideEventAbandonedCartData cart = Required(data.abandonedCart, "abandonedCart");
                    return new SideEventAbandonedCartSpecification(cart.acceptedBumps, cart.allowSpamBump,
                        cart.dropConfigContentId, Provenance(cart.dropConfigProvenance),
                        cart.dropTagContentId, Provenance(cart.dropTagProvenance), cart.sourceStat, cart.statAmount, cart.alwaysManaVariant);
                case SideEventSourceKind.EvoStatue:
                    SideEventEvoStatueData evo = Required(data.evoStatue, "evoStatue");
                    return new SideEventEvoStatueSpecification(OptionalInt(evo.acceptedBumps), GemOffer(evo.offer), Recipe(evo.recipe));
                case SideEventSourceKind.ManaStatue:
                    SideEventManaStatueData statue = Required(data.manaStatue, "manaStatue");
                    return new SideEventManaStatueSpecification(statue.acceptedBumps, statue.allowSpamBump, ManaReward(statue.reward));
                case SideEventSourceKind.ArcanaChoice:
                    SideEventArcanaChoiceData arcana = Required(data.arcanaChoice, "arcanaChoice");
                    return new SideEventArcanaChoiceSpecification(arcana.choicesToShow, arcana.sourceArcanaType,
                        arcana.outsideClickCloses, OptionalBool(arcana.viewIsEdgeEvent));
                case SideEventSourceKind.CardStatOffering:
                    SideEventCardStatOfferingData stat = Required(data.cardStatOffering, "cardStatOffering");
                    return new SideEventCardStatOfferingSpecification(Selection(stat.selection),
                        Map(stat.statSettings, Stat, "cardStatOffering.statSettings"), stat.cashOutAmount, stat.cashOutUsesGreed);
                case SideEventSourceKind.ChickenOffering:
                    SideEventChickenOfferingData chicken = Required(data.chickenOffering, "chickenOffering");
                    return new SideEventChickenOfferingSpecification(Selection(chicken.selection), chicken.foodCount,
                        chicken.foodMetricId, Required(chicken.foodCollectionTypes, "foodCollectionTypes"), Healing(chicken.healing));
                case SideEventSourceKind.Crystal:
                    SideEventCrystalData crystal = Required(data.crystal, "crystal");
                    return new SideEventCrystalSpecification(Timing(crystal.timing), CrystalReward(crystal.reward));
                case SideEventSourceKind.DuplicateOffering:
                    SideEventDuplicateOfferingData duplicate = Required(data.duplicateOffering, "duplicateOffering");
                    return new SideEventDuplicateOfferingSpecification(Selection(duplicate.selection), CopyPolicy(duplicate.copyPolicy));
                case SideEventSourceKind.GemChest:
                    return new SideEventGemChestSpecification(GemOffer(Required(data.gemChest, "gemChest").offer));
                case SideEventSourceKind.SilverGemChest:
                    SideEventSilverGemChestData silver = Required(data.silverGemChest, "silverGemChest");
                    return new SideEventSilverGemChestSpecification(Selection(silver.selection),
                        Map(silver.requestedCardTypes, ObjectReference, "silverGemChest.requestedCardTypes"),
                        silver.avoidDuplicateCardTypes, GemOffer(silver.offer));
                case SideEventSourceKind.GunForHire:
                    SideEventGunForHireData hire = Required(data.gunForHire, "gunForHire");
                    return new SideEventGunForHireSpecification(Price(hire.buyPrice), Price(hire.pityPrice),
                        hire.pityUsesGreed, OptionalBool(hire.rerollInvalidSavedChoice));
                case SideEventSourceKind.ManaOffering:
                    SideEventManaOfferingData mana = Required(data.manaOffering, "manaOffering");
                    return new SideEventManaOfferingSpecification(Selection(mana.selection), ManaReward(mana.reward));
                default: throw new ArgumentException("mechanic: unsupported source kind.");
            }
        }
    }
}
