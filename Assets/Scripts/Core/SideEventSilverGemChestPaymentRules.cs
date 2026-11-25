using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Detached physical-card input for the source Silver chest payment rule.
    /// SourceCardTypes are exact VC asset IDs, never project card colors or display names.
    /// IsFcc must be resolved by the caller; this module does not infer card admission.
    /// </summary>
    public sealed class SilverGemChestPaymentCard
    {
        public int RuntimeInstanceId { get; }
        public bool IsFcc { get; }
        public IReadOnlyList<string> SourceCardTypes { get; }

        public SilverGemChestPaymentCard(int runtimeInstanceId,
            IReadOnlyList<string> sourceCardTypes, bool isFcc)
        {
            if (runtimeInstanceId <= 0)
                throw new ArgumentOutOfRangeException(nameof(runtimeInstanceId));
            if (sourceCardTypes == null)
                throw new ArgumentNullException(nameof(sourceCardTypes));
            var types = new List<string>(sourceCardTypes.Count);
            foreach (string type in sourceCardTypes)
            {
                SideEventSilverGemChestPaymentRules.RequireSourceCardType(type);
                // Native CardModel.AddCardType appends without deduplication.
                // Preserve item order and multiplicity: each item pays at most
                // one matching lock. Do not manufacture a project color list.
                types.Add(type);
            }
            RuntimeInstanceId = runtimeInstanceId;
            IsFcc = isFcc;
            SourceCardTypes = new ReadOnlyCollection<string>(types);
        }
    }

    /// <summary>
    /// Immutable two-lock payment snapshot. Selection creates a new snapshot;
    /// it never destroys cards, grants Gems, saves a Run or opens an adapter.
    /// This module owns the ordered remaining locks and exact selected instances.
    /// </summary>
    public sealed class SideEventSilverGemChestPayment
    {
        public IReadOnlyList<string> Requirements { get; }
        public IReadOnlyList<string> RemainingRequirements { get; }
        public IReadOnlyList<SilverGemChestPaymentCard> SelectedCards { get; }
        public bool IsComplete => RemainingRequirements.Count == 0;

        internal SideEventSilverGemChestPayment(IReadOnlyList<string> requirements,
            IReadOnlyList<string> remaining, IReadOnlyList<SilverGemChestPaymentCard> selected)
        {
            Requirements = new ReadOnlyCollection<string>(new List<string>(requirements));
            RemainingRequirements = new ReadOnlyCollection<string>(new List<string>(remaining));
            SelectedCards = new ReadOnlyCollection<SilverGemChestPaymentCard>(
                new List<SilverGemChestPaymentCard>(selected));
        }

        public bool CanSelect(SilverGemChestPaymentCard card)
            => TrySelect(card, out _, out _);

        public bool TrySelect(SilverGemChestPaymentCard card,
            out SideEventSilverGemChestPayment next, out string error)
        {
            next = this;
            error = null;
            if (IsComplete) { error = "Silver chest color requirements are already satisfied."; return false; }
            if (card == null) { error = "A physical card is required."; return false; }
            if (card.IsFcc) { error = "FCC cards cannot pay Silver chest requirements."; return false; }
            foreach (SilverGemChestPaymentCard selected in SelectedCards)
                if (selected.RuntimeInstanceId == card.RuntimeInstanceId)
                { error = "The physical card has already been selected."; return false; }

            var remaining = new List<string>(RemainingRequirements);
            foreach (string type in card.SourceCardTypes)
            {
                int index = remaining.FindIndex(value => StringComparer.Ordinal.Equals(value, type));
                // Native List.Remove removes the first matching requirement once
                // for each CardTypes item. Different colors may satisfy both locks.
                if (index >= 0) remaining.RemoveAt(index);
            }
            if (remaining.Count == RemainingRequirements.Count)
            { error = "The physical card does not satisfy a remaining color requirement."; return false; }

            var cards = new List<SilverGemChestPaymentCard>(SelectedCards) { card };
            next = new SideEventSilverGemChestPayment(Requirements, remaining, cards);
            return true;
        }
    }

    /// <summary>
    /// Version 1 reproduces the captured Silver modal's configured two requests:
    /// ordered Attack/Defense/Special pool, two integer Range(0,3) draws with
    /// replacement. It does not claim VC seed equivalence or project color mapping.
    /// An already frozen request must use CreateFrozen instead of drawing again.
    /// </summary>
    public static class SideEventSilverGemChestPaymentRules
    {
        public const int SourcePolicyVersion = 1;
        public const int RequirementCount = 2;
        public const string AttackSourceAssetId = "CardType_Attack";
        public const string DefenseSourceAssetId = "CardType_Defense";
        public const string SpecialSourceAssetId = "CardType_Special";
        private static readonly IReadOnlyList<string> Pool = new ReadOnlyCollection<string>(
            new[] { AttackSourceAssetId, DefenseSourceAssetId, SpecialSourceAssetId });
        public static IReadOnlyList<string> SourceCardTypePool => Pool;

        public static IReadOnlyList<string> GenerateRequirements(IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var requirements = new List<string>(RequirementCount);
            for (int i = 0; i < RequirementCount; i++)
            {
                int index = random.Range(0, Pool.Count);
                if (index < 0 || index >= Pool.Count)
                    throw new InvalidOperationException("Random source returned an invalid Silver color index.");
                requirements.Add(Pool[index]);
            }
            return new ReadOnlyCollection<string>(requirements);
        }

        public static SideEventSilverGemChestPayment CreateFrozen(IReadOnlyList<string> requirements)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (requirements.Count != RequirementCount)
                throw new ArgumentException("The captured Silver source policy requires exactly two color locks.", nameof(requirements));
            foreach (string type in requirements) RequireSourceCardType(type);
            return new SideEventSilverGemChestPayment(requirements, requirements,
                Array.Empty<SilverGemChestPaymentCard>());
        }

        internal static void RequireSourceCardType(string type)
        {
            if (type != AttackSourceAssetId && type != DefenseSourceAssetId && type != SpecialSourceAssetId)
                throw new ArgumentException("Unknown source CardType asset ID.", nameof(type));
        }
    }
}
