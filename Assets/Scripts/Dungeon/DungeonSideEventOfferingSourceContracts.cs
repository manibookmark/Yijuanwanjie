using System;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the two captured Offering execution boundaries, not candidate policy,
    /// payment, stat mathematics or a journal. Complete source facts are checked
    /// before an adapter is admitted. The physical Silver call supplies three true
    /// flags; raw optional fields remain Unknown and contradictory known facts fail.
    /// No whole-Run pool, project color/FCC mapping or production placement is granted.
    /// CardStat binds the exact immutable qualification document; its existing owner
    /// still requires an explicit physical subset and validates every frozen author.
    /// </summary>
    internal static class DungeonSideEventOfferingSourceContracts
    {
        private const string BaseRequirementType = "Nosebleed.Pancake.GameConfig.EventRequirement, Pancake";
        private static readonly int[] StatIds = { 0, 1, 2, 4, 5, 6, 7, 10, 11, 13, 17 };
        private static readonly float[] StatModifiers = { 1, 1, .5f, 1, 2, 2, 2, 1, 1, 1, 1 };

        internal static bool MatchesCardStat(SideEventSourceRoot root, SideEventCardStatOfferingSpecification spec)
        {
            if (root.SourceAssetId != "PassiveEvent_CardStatOfferingTable" || !IsBase(root) ||
                !MatchesSelection(spec.Selection, 1, true) || !spec.Selection.RequireSupportedModifiedStat ||
                spec.CashOutAmount != 200 || spec.CashOutUsesGreed || spec.StatSettings.Count != StatIds.Length)
                return false;
            for (int i = 0; i < StatIds.Length; i++)
            {
                var setting = spec.StatSettings[i];
                if (setting.SourceStat != StatIds[i] || setting.AdditionalChangeAmount != 0 ||
                    setting.BaseChangeAmount != 0 || !SameBits(setting.Modifier, StatModifiers[i])) return false;
            }
            return true;
        }

        internal static bool MatchesSilver(SideEventSourceRoot root, SideEventSilverGemChestSpecification spec)
        {
            if (root.SourceAssetId != "PassiveEvent_GemChest_Silver" || !IsBase(root) ||
                !MatchesSelection(spec.Selection, 2, true) || spec.Selection.RequireSupportedModifiedStat ||
                spec.AvoidDuplicateCardTypes || spec.RequestedCardTypes.Count != 3) return false;
            long[] types = { 7550, 7552, 7554 };
            for (int i = 0; i < types.Length; i++)
            {
                var reference = spec.RequestedCardTypes[i];
                if (reference.OwnerSourceFile != "sharedassets0.assets" || reference.FileIndex != 1 || reference.PathId != types[i])
                    return false;
            }
            var offer = spec.Offer;
            if (offer.MinimumChoiceCount != 3 || offer.MaximumChoiceCount != 3 || offer.GuaranteeEvo ||
                !CallFlag(offer.CanCashOut) || !CallFlag(offer.AllowFallbacks) || !CallFlag(offer.EnsureNonEvo) ||
                offer.CashOutAmount.IsKnown && offer.CashOutAmount.Value != 200 || offer.Frequencies.Count != 3)
                return false;
            long[] paths = { 19655, 19653, 19654 };
            float[] weights = { 80, 60, 40 }, levelWeights = { 20, 6, 0 }, luck = { .065f, .3f, 0 };
            for (int i = 0; i < paths.Length; i++)
            {
                var frequency = offer.Frequencies[i];
                if (frequency.Provenance.Alias != "vc-local-full-copy" ||
                    frequency.Provenance.SourceFile != "sharedassets0.assets" || frequency.Provenance.PathId != paths[i] ||
                    !SameBits(frequency.ChestWeight, weights[i]) || !SameBits(frequency.LevelUpWeight, levelWeights[i]) ||
                    !SameBits(frequency.LevelUpLuckFactor, luck[i])) return false;
            }
            // BagOfCoins is not this Silver execution path. Preserve its captured
            // inactive reference/unknown quantity in the source document, never grant it.
            return true;
        }

        internal static LevelInteractionCardStatOfferingContract CaptureCardStatContract(
            SideEventSourceAttempt attempt, SideEventSourceWireDocument document)
        {
            RequireDocument(attempt, document);
            return new LevelInteractionCardStatOfferingContract {
                sourceEventId = DungeonRunCardStatOfferings.SourceEventId,
                rootContentId = attempt.Selection.Member.Root.ContentId,
                sourceFingerprint = document.Fingerprint,
                poolPolicyId = CardStatOfferingPreparedContext.ExplicitSubsetPolicyId };
        }

        internal static void RequireCardStatContract(SideEventSourceAttempt attempt,
            SideEventSourceWireDocument document, LevelInteractionEffect effect)
        {
            var expected = CaptureCardStatContract(attempt, document);
            var configured = effect.cardStatOffering;
            if (configured == null || configured.sourceEventId != expected.sourceEventId ||
                configured.rootContentId != expected.rootContentId || configured.sourceFingerprint != expected.sourceFingerprint ||
                configured.poolPolicyId != expected.poolPolicyId)
                throw new InvalidOperationException("CardStat source/root/full-document/pool binding differs from its frozen qualification.");
        }

        private static void RequireDocument(SideEventSourceAttempt attempt, SideEventSourceWireDocument document)
        {
            var root = attempt?.Selection?.Member?.Root;
            if (document == null || root == null || !document.Definition.TryGetRoot(root.ContentId, out var registered) ||
                !ReferenceEquals(root, registered))
                throw new InvalidOperationException("CardStat requires the actual complete immutable qualification document, not a root/state fingerprint.");
        }

        private static bool IsBase(SideEventSourceRoot root) =>
            root.Requirement.Kind == SideEventRequirementKind.Base && root.Requirement.SourceType == BaseRequirementType;
        private static bool MatchesSelection(SideEventOfferingSelection value, int count, bool destroys) =>
            value.CardsRequired == count && value.DestroySelected == destroys && value.ExcludeFcc &&
            !value.RequireOfferingFlag && !value.OutsideClickCloses;
        private static bool CallFlag(SideEventOptionalBool value) => !value.IsKnown || value.Value;
        private static bool SameBits(float a, float b) =>
            BitConverter.ToInt32(BitConverter.GetBytes(a), 0) == BitConverter.ToInt32(BitConverter.GetBytes(b), 0);
    }
}
