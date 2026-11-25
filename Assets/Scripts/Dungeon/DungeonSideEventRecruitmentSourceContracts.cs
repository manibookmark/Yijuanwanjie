using System;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns source recognition and frozen-document binding for Duplicate/Hire.
    /// A matching source is not an approved card pool, breakthrough strategy or
    /// FCC-account projection. The existing owners still require explicit prepared
    /// contexts; no policy, eligibility, candidate or price draw is synthesized here.
    /// Native post-Gem breakthrough and invalid-choice reroll remain uncaptured.
    /// </summary>
    internal static class DungeonSideEventRecruitmentSourceContracts
    {
        private const string AchievementType = "Nosebleed.Pancake.GameConfig.AchievementEventRequirement, Pancake";

        internal static bool MatchesDuplicate(SideEventSourceRoot root, SideEventDuplicateOfferingSpecification spec)
        {
            var selection = spec.Selection; var copy = spec.CopyPolicy;
            return root.SourceAssetId == "PassiveEvent_DuplicateOfferingTable" &&
                MatchesGate(root, "MetricAchievement_Event_DuplicationTableEvent", "Event_DuplicationTableEvent", "CardGroup_Duplicator_Played", 1, 25f) &&
                selection.CardsRequired == 1 && !selection.DestroySelected && selection.ExcludeFcc &&
                !selection.RequireOfferingFlag && !selection.OutsideClickCloses && !selection.RequireSupportedModifiedStat &&
                copy.PreserveOriginal && copy.CopyBaseAndCurrentConfig && copy.CopySlotCountAndGemConfigs &&
                !copy.CopyManaCostModifier && !copy.CopyTemporaryManaCostModifier && copy.InitialBreakthroughCount == 0 &&
                copy.FinalBreakthroughState == SideEventCopyFinalState.UncapturedAfterGemCallbacks && !copy.FinalIsEvolved.IsKnown;
        }

        internal static bool MatchesHire(SideEventSourceRoot root, SideEventGunForHireSpecification spec)
        {
            if (root.SourceAssetId != "PassiveEvent_GunForHire" ||
                !MatchesGate(root, "MetricAchievement_Event_GFH", "Event_GFH", "FCC_Actions_Triggered", 0, 200f) ||
                !spec.ExcludeOwnedFcc || !spec.RequireUnlockedFcc || !spec.SaveChosenFccAndCostAndPity || !spec.KeepValidSavedChoice ||
                spec.RerollInvalidSavedChoice.IsKnown) return false;
            // The existing deep price module owns both rounding passes, integer
            // bounds and binary32 arithmetic. Do not reproduce them in an adapter.
            try { SideEventGunForHirePriceRules.ValidateSpecification(spec); return true; }
            catch (ArgumentException) { return false; }
        }

        internal static LevelInteractionDuplicateOfferingContract CaptureDuplicate(SideEventSourceAttempt attempt, SideEventSourceWireDocument document)
        {
            var root = RequireDocument(attempt, document, SideEventSourceKind.DuplicateOffering);
            return new LevelInteractionDuplicateOfferingContract { sourceEventId = DungeonRunDuplicateOfferings.SourceEventId,
                rootContentId = root.ContentId, sourceFingerprint = document.Fingerprint, poolPolicyId = DuplicateOfferingPreparedContext.ExplicitSubsetPolicyId };
        }
        internal static LevelInteractionGunForHireContract CaptureHire(SideEventSourceAttempt attempt, SideEventSourceWireDocument document)
        {
            var root = RequireDocument(attempt, document, SideEventSourceKind.GunForHire);
            return new LevelInteractionGunForHireContract { sourceEventId = DungeonRunGunForHire.SourceEventId,
                rootContentId = root.ContentId, sourceFingerprint = document.Fingerprint, poolPolicyId = GunForHirePreparedContext.ExplicitSubsetPolicyId };
        }
        internal static void RequireBinding(SideEventSourceAttempt attempt, SideEventSourceWireDocument document, LevelInteractionEffect effect)
        {
            if (attempt.Selection.Member.Root.Kind == SideEventSourceKind.DuplicateOffering)
            {
                var expected = CaptureDuplicate(attempt, document); var actual = effect.duplicateOffering;
                if (actual == null || actual.sourceEventId != expected.sourceEventId || actual.rootContentId != expected.rootContentId ||
                    actual.sourceFingerprint != expected.sourceFingerprint || actual.poolPolicyId != expected.poolPolicyId)
                    throw Invalid("Duplicate source/root/full-document/explicit-policy binding differs from its frozen qualification.");
            }
            else
            {
                var expected = CaptureHire(attempt, document); var actual = effect.gunForHire;
                if (actual == null || actual.sourceEventId != expected.sourceEventId || actual.rootContentId != expected.rootContentId ||
                    actual.sourceFingerprint != expected.sourceFingerprint || actual.poolPolicyId != expected.poolPolicyId)
                    throw Invalid("Hire source/root/full-document/explicit-policy binding differs from its frozen qualification.");
            }
        }

        private static SideEventSourceRoot RequireDocument(SideEventSourceAttempt attempt, SideEventSourceWireDocument document, SideEventSourceKind kind)
        {
            var root = attempt?.Selection?.Member?.Root;
            if (root == null || root.Kind != kind || document == null || !document.Definition.TryGetRoot(root.ContentId, out var registered) ||
                !ReferenceEquals(root, registered)) throw Invalid("Recruitment requires the actual complete immutable qualification document.");
            return root;
        }
        private static bool MatchesGate(SideEventSourceRoot root, string sourceId, string achievement, string metric, int collection, float threshold)
            => root.Requirement.SourceType == AchievementType && root.Requirement is SideEventMetricRequirement value &&
                value.SourceAssetId == sourceId && value.AchievementId == achievement && value.MetricId == metric &&
                value.MetricType == 1 && value.CollectionType == collection && value.UnlockValue == threshold &&
                !value.AllowInDemo && value.RewardType == 6 && value.HasUnlockReward;
        private static InvalidOperationException Invalid(string message) => new InvalidOperationException("Recruitment source adapter: " + message);
    }
}
