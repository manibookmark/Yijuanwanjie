using System;

namespace WanjieScroll.Core
{
    // Shared immutable offering qualification contracts. The existing filename
    // remains for compatibility; neither contract owns mutable account or Run state.
    /// <summary>Typed payout of the same one-card Offering lifecycle; no second ledger.</summary>
    public enum CardOfferingKind { Chicken = 1, Mana = 2 }

    public static class ManaOfferingContracts
    {
        public const string SourceEventId = ManaOfferingEligibility.CanonicalSourceEventId;
        public const int ManaAmount = 1;
    }

    /// <summary>Real permanent qualification and author-backed admission, never a permissive default.</summary>
    public interface IManaOfferingContext
    {
        ManaOfferingEligibility QueryEligibility();
        CardOfferingAdmission ResolveAdmission(string cardContentId);
    }

    /// <summary>Detached permanent qualification; it neither unlocks an event nor permits card admission.</summary>
    public sealed class ManaOfferingEligibility
    {
        public const string CanonicalSourceEventId = "event.xiyou.mana-offering.v1";
        public string SourceEventId => CanonicalSourceEventId;
        public bool IsUnlocked { get; }
        public string QualifyingRunId { get; }

        public ManaOfferingEligibility(bool isUnlocked, string qualifyingRunId = null)
        {
            if (isUnlocked ? string.IsNullOrWhiteSpace(qualifyingRunId) : !string.IsNullOrEmpty(qualifyingRunId))
                throw new ArgumentException("Mana Offering qualification requires an exact qualifying Run only when unlocked.");
            foreach (char value in qualifyingRunId ?? string.Empty)
                if (char.IsWhiteSpace(value) || char.IsControl(value))
                    throw new ArgumentException("Qualification Run ID contains invalid characters.");
            IsUnlocked = isUnlocked;
            QualifyingRunId = qualifyingRunId ?? string.Empty;
        }
    }

    /// <summary>
    /// Stable event identity and source-fixed reward quantities. Content resolves
    /// each card's source admission; the account adapter queries the permanent
    /// event gate. Neither a missing service nor Unknown is an unlocked default.
    /// This contract contains no mutable Run, account, UI or payment state.
    /// </summary>
    public static class ChickenOfferingContracts
    {
        public const string SourceEventId = "event.xiyou.chicken-offering.v1";
        public const string SourceAssetId = "PassiveEvent_ChickenOfferingTable";
        public const int FoodCount = 3;
        public const int BaseHealingPerFood = 10;
        public static int HealingPerFood(bool experimentalMedicineEnabled)
            => experimentalMedicineEnabled ? 20 : BaseHealingPerFood;
    }

    public sealed class ChickenOfferingEligibility
    {
        public string SourceEventId { get; }
        public bool IsUnlocked { get; }
        public ChickenOfferingEligibility(string sourceEventId, bool isUnlocked)
        {
            if (sourceEventId != ChickenOfferingContracts.SourceEventId)
                throw new ArgumentException("ChickenOffering eligibility has no canonical event identity.", nameof(sourceEventId));
            SourceEventId = sourceEventId;
            IsUnlocked = isUnlocked;
        }
    }

    /// <summary>
    /// Explicit production query boundary. Implementations query the real account
    /// gate, author-backed admission and current combat-rule projection. A test
    /// implementation is not proof that a production gate was installed.
    /// </summary>
    public interface IChickenOfferingContext
    {
        ChickenOfferingEligibility QueryEligibility();
        CardOfferingAdmission ResolveAdmission(string cardContentId);
        bool ExperimentalMedicineEnabled { get; }
    }
}
