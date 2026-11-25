using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Owns the adopted source-to-project requirement projection. Captures real
    /// permanent Offering flags and exact authored relic identities/types, never
    /// temporary thresholds or card-unlock flags. GemHammer is the always-enabled
    /// Yuyin permission; Randomazzo follows the existing production owned-relic
    /// admission policy rather than inventing a source toggle. Missing authority,
    /// unmapped gates and contradictory Arcana inputs remain explicit Unknown.
    /// No account/Run writes, choice generation, RNG or event execution occur.
    /// </summary>
    public static class DungeonSideEventQualificationResolver
    {
        // The structural and live queries share this adopted projection. It is
        // not eligibility: future source times still capture the real account.
        private enum RequirementProjection { Unknown, Base, Chicken, Mana, Duplicate, Evo, GemHammer, Randomazzo, GunForHire }

        /// <summary>Shared permission for source slots and explicit post-elite Gem chests.
        /// An unlocked seal list cannot replace the exact owned GemHammer relic authority.</summary>
        public static SideEventRequirementEligibility ResolveGemHammerPermission(
            MetaProgression account, ContentCatalog content)
            => account == null || !HasRelicAuthority(content, RelicContentIds.Yuyin, RelicType.GemHammer)
                ? SideEventRequirementEligibility.Unknown
                : Decision(SealRewardEligibility.CanReceiveSeals(account));

        /// <summary>Diagnoses every referenced root over the complete source timeline,
        /// once in declaration order. No account, catalog, Arcana budget or future
        /// qualification is queried; an unmapped contract is never presumed locked.</summary>
        public static IReadOnlyList<string> InspectRequirementContracts(SideEventSourceDefinition source)
            => InspectRequirementContracts(source, null, false);

        /// <summary>Also validates exact adopted relic IDs/types against the supplied
        /// content authority. It neither checks ownership nor builds an
        /// Arcana pool; those inputs are captured only at the source phase time.</summary>
        public static IReadOnlyList<string> InspectRequirementContracts(SideEventSourceDefinition source,
            ContentCatalog content) => InspectRequirementContracts(source, content, true);

        public static IReadOnlyList<string> InspectRequirementContracts(SideEventSourceDefinition source,
            ContentCatalog content, int approvedSourceLayerCount)
            => InspectRequirementContracts(source, content, true, approvedSourceLayerCount);

        private static IReadOnlyList<string> InspectRequirementContracts(SideEventSourceDefinition source,
            ContentCatalog content, bool checkContent, int approvedSourceLayerCount = -1)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int layerCount = approvedSourceLayerCount == -1 ? source.Layers.Count : approvedSourceLayerCount;
            if (layerCount < 0 || layerCount > source.Layers.Count) throw new ArgumentOutOfRangeException(nameof(approvedSourceLayerCount));
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                var layer = source.Layers[layerIndex];
                foreach (var slot in layer.Slots)
                {
                    IReadOnlyList<SideEventSourceMember> members = slot.InlineMembers;
                    if (slot.Mode == SideEventSourceSlotMode.SharedGroup)
                    { source.TryGetGroup(slot.GroupContentId, out var group); members = group.Members; }
                    foreach (var member in members) referenced.Add(member.RootContentId);
                }
            }
            var errors = new List<string>();
            foreach (var root in source.Roots)
            {
                if (!referenced.Contains(root.ContentId)) continue;
                if (!TryValidateRequirementContract(root, out string error)) { errors.Add(error); continue; }
                if (!checkContent) continue;
                RequirementProjection projection = Projection(root);
                string relicId = projection == RequirementProjection.GemHammer ? RelicContentIds.Yuyin :
                    projection == RequirementProjection.Randomazzo ? DungeonRunArcanaCatalogResolver.LegacyAcquisitionRelicContentId : null;
                RelicType type = projection == RequirementProjection.GemHammer ? RelicType.GemHammer : RelicType.ArcanaFinder;
                if (relicId != null && !HasRelicAuthority(content, relicId, type))
                    errors.Add(root.ContentId + ": missing exact qualification content authority " + relicId + "/" + type + ".");
            }
            return errors.AsReadOnly();
        }

        public static bool TryValidateRequirementContract(SideEventSourceRoot root, out string error)
        {
            error = null;
            if (root == null) { error = "Qualification preflight requires a complete registered source root."; return false; }
            if (Projection(root) != RequirementProjection.Unknown) return true;
            error = root.ContentId + ": no adopted qualification projection for " + root.Kind + "/" +
                root.Requirement.Kind + " (" + root.Requirement.SourceType + ").";
            return false;
        }

        public static SideEventQualificationSnapshot Capture(SideEventSourceDefinition source,
            string sourceLayerContentId, MetaProgression account, ContentCatalog content,
            DungeonRunArcanaCatalog arcanaCatalog,
            IReadOnlyList<DungeonRunArcanaDefinition> activeArcanas)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!source.TryGetLayer(sourceLayerContentId, out var layer))
                throw new ArgumentException("A registered source layer must be captured explicitly.", nameof(sourceLayerContentId));
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in layer.Slots)
            {
                IReadOnlyList<SideEventSourceMember> members = slot.InlineMembers;
                if (slot.Mode == SideEventSourceSlotMode.SharedGroup)
                {
                    source.TryGetGroup(slot.GroupContentId, out var group);
                    members = group.Members;
                }
                foreach (var member in members) referenced.Add(member.RootContentId);
            }
            var decisions = new List<SideEventRequirementDecision>();
            foreach (var root in source.Roots)
                if (referenced.Contains(root.ContentId))
                    decisions.Add(new SideEventRequirementDecision(root.ContentId,
                        Resolve(root, account, content, arcanaCatalog)));
            return new SideEventQualificationSnapshot(source, layer.ContentId, decisions,
                CountLegalUnequippedArcanas(account, arcanaCatalog, activeArcanas));
        }

        private static SideEventRequirementEligibility Resolve(SideEventSourceRoot root,
            MetaProgression account, ContentCatalog content, DungeonRunArcanaCatalog arcanaCatalog)
        {
            RequirementProjection projection = Projection(root);
            if (projection == RequirementProjection.Base)
                return SideEventRequirementEligibility.Eligible;
            if (account == null) return SideEventRequirementEligibility.Unknown;
            if (projection == RequirementProjection.Chicken)
                return Decision(account.EventUnlocks.IsChickenShopEligible());
            if (projection == RequirementProjection.Mana)
                return Decision(account.EventUnlocks.QueryManaOfferingEligibility().IsUnlocked);
            if (projection == RequirementProjection.Duplicate)
                return Decision(account.EventUnlocks.QueryDuplicateOfferingEligibility().IsUnlocked);
            if (projection == RequirementProjection.Evo)
                return Decision(account.EventUnlocks.QueryEvoOfferingEligibility().IsUnlocked);
            if (projection == RequirementProjection.GunForHire)
                return Decision(account.EventUnlocks.QueryGunForHireEligibility().IsUnlocked);
            if (projection == RequirementProjection.GemHammer)
                return ResolveGemHammerPermission(account, content);
            if (projection == RequirementProjection.Randomazzo)
            {
                var result = OwnedRelic(account, content,
                    DungeonRunArcanaCatalogResolver.LegacyAcquisitionRelicContentId, RelicType.ArcanaFinder);
                if (arcanaCatalog == null) return result == SideEventRequirementEligibility.Ineligible
                    ? result : SideEventRequirementEligibility.Unknown;
                if (result != SideEventRequirementEligibility.Unknown &&
                    arcanaCatalog.AcquisitionUnlocked != (result == SideEventRequirementEligibility.Eligible))
                    return SideEventRequirementEligibility.Unknown;
                return result;
            }
            return SideEventRequirementEligibility.Unknown;
        }

        private static RequirementProjection Projection(SideEventSourceRoot root)
        {
            if (root.Requirement.Kind == SideEventRequirementKind.Base) return RequirementProjection.Base;
            if (root.Kind == SideEventSourceKind.ChickenOffering && root.Requirement is SideEventMetricRequirement chicken &&
                MatchesMetric(chicken, "MetricAchievement_ChickenShop", "ChickenShop", "FloorChicken", 0, 5f, true))
                return RequirementProjection.Chicken;
            if (root.Kind == SideEventSourceKind.ManaOffering && root.Requirement is SideEventMetricRequirement mana &&
                MatchesMetric(mana, "MetricAchievement_Event_ManaOfferingTable", "Event_ManaOfferingTable", "CardGroup_EmptyTome_Played", 1, 5f, true))
                return RequirementProjection.Mana;
            if (root.Kind == SideEventSourceKind.DuplicateOffering && root.Requirement is SideEventMetricRequirement duplicate &&
                MatchesMetric(duplicate, "MetricAchievement_Event_DuplicationTableEvent", "Event_DuplicationTableEvent", "CardGroup_Duplicator_Played", 1, 25f, false))
                return RequirementProjection.Duplicate;
            if (root.Kind == SideEventSourceKind.EvoStatue && root.Requirement is SideEventDungeonRequirement evo && MatchesEvo(evo))
                return RequirementProjection.Evo;
            // User confirmed 2026-10-16: retain the exact source signature, but
            // adopt accepted lifetime ACTUAL crawler actions >= 200 in this
            // project. Native TriggerFccReaction attempted calls are not credited.
            if (root.Kind == SideEventSourceKind.GunForHire && root.Requirement is SideEventMetricRequirement hire &&
                MatchesMetric(hire, "MetricAchievement_Event_GFH", "Event_GFH", "FCC_Actions_Triggered", 0, 200f, false))
                return RequirementProjection.GunForHire;
            if (root.Kind == SideEventSourceKind.GemChest && root.Requirement is SideEventRelicRequirement gem &&
                gem.SourceAssetId == "RelicConfig_GemHammer") return RequirementProjection.GemHammer;
            if (root.Kind == SideEventSourceKind.ArcanaChoice && root.Requirement is SideEventRelicRequirement arcana &&
                arcana.SourceAssetId == "RelicConfig_Randomazzo") return RequirementProjection.Randomazzo;
            return RequirementProjection.Unknown;
        }

        private static bool MatchesMetric(SideEventMetricRequirement value, string sourceId,
            string achievementId, string metricId, int collectionType, float threshold, bool allowInDemo)
            => value.SourceAssetId == sourceId && value.AchievementId == achievementId &&
               value.MetricId == metricId && value.MetricType == 1 && value.CollectionType == collectionType &&
               value.UnlockValue == threshold && value.RewardType == 6 && value.HasUnlockReward &&
               value.AllowInDemo == allowInDemo;

        private static bool MatchesEvo(SideEventDungeonRequirement value)
        {
            SideEventSourceDungeonCondition condition = value.Condition;
            return value.SourceAssetId == "DungeonAchievement_Gems_01_CompMF1F1" &&
                value.AchievementId == "Gems_01_CompMF1F1" && value.AllowInDemo &&
                value.RewardType == 7 && value.HasUnlockReward && !condition.CheckAnyDungeon &&
                !condition.RequiresFcc && condition.AchievementType == 1 && condition.FloorNumberToComplete == 2 &&
                condition.DungeonReference.OwnerSourceFile == "sharedassets0.assets" &&
                condition.DungeonReference.FileIndex == 1 && condition.DungeonReference.PathId == 7558 &&
                value.Provenance.Alias == "vc-local-full-copy" && value.Provenance.SourceFile == "sharedassets0.assets" &&
                value.Provenance.PathId == 17693;
        }

        private static SideEventRequirementEligibility OwnedRelic(MetaProgression account,
            ContentCatalog content, string contentId, RelicType type)
        {
            if (!HasRelicAuthority(content, contentId, type))
                return SideEventRequirementEligibility.Unknown;
            return Decision(account.IsRelicUnlocked(contentId) && account.HasRelicType(type));
        }

        private static bool HasRelicAuthority(ContentCatalog content, string contentId, RelicType type)
            => content != null && content.TryGetRelic(contentId, out var relic) &&
               string.Equals(relic.contentId, contentId, StringComparison.Ordinal) && relic.relicType == (int)type;

        private static int? CountLegalUnequippedArcanas(MetaProgression account, DungeonRunArcanaCatalog catalog,
            IReadOnlyList<DungeonRunArcanaDefinition> active)
        {
            if (catalog == null || active == null) return null; // Missing is never an empty owned set.
            if (catalog.PoolContentId != DungeonRunArcanaCatalogResolver.PoolContentId)
                throw new ArgumentException("The qualification budget requires the adopted legal Arcana pool.", nameof(catalog));
            var held = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in active)
            {
                DungeonRunArcanaCatalogResolver.RequireImplementedDefinition(definition);
                if (!held.Add(definition.ContentId))
                    throw new ArgumentException("Acquired Arcana identities cannot be duplicated.", nameof(active));
            }
            int count = 0;
            bool consistent = account != null && catalog.EquippedContentId ==
                (account.hasEquippedArcanaEffect ? account.equippedArcana ?? string.Empty : string.Empty);
            foreach (var definition in catalog.EligibleCandidates)
            {
                DungeonRunArcanaCatalogResolver.RequireImplementedDefinition(definition);
                consistent &= account?.IsArcanaUnlocked(definition.ContentId) == true;
                if (definition.ContentId != catalog.EquippedContentId && !held.Contains(definition.ContentId)) count++;
            }
            // This is the currently implemented project pool, not the whole VC
            // Arcana database. Capture requires a matching current projection;
            // a future Run owner must persist this input rather than recapture it.
            return consistent ? count : (int?)null;
        }

        private static SideEventRequirementEligibility Decision(bool eligible)
            => eligible ? SideEventRequirementEligibility.Eligible : SideEventRequirementEligibility.Ineligible;
    }
}
