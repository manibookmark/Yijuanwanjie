using System;
using System.Collections.Generic;
using System.Linq;

namespace WanjieScroll.Core
{
    /// <summary>One explicit new generation. The caller owns count resolution, frozen metadata and reroll history.</summary>
    public sealed class SideEventGemOfferGenerationRequest
    {
        public SideEventGemOffer Source { get; }
        public SideEventGemEligibilityContext Context { get; }
        public IReadOnlyList<SideEventGemCandidateEvidence> Candidates { get; }
        public IReadOnlyList<SideEventGemFrequency> OrderedFrequencies { get; }
        public int ResolvedChoiceCount { get; }
        public IReadOnlyList<SideEventGemChoice> PreviousChoices { get; }
        public SideEventGemOfferGenerationRequest(SideEventGemOffer source, SideEventGemEligibilityContext context,
            IReadOnlyList<SideEventGemCandidateEvidence> candidates, IReadOnlyList<SideEventGemFrequency> orderedFrequencies,
            int resolvedChoiceCount, IReadOnlyList<SideEventGemChoice> previousChoices)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Candidates = SideEventGemSelectionRules.Copy(candidates);
            OrderedFrequencies = SideEventGemSelectionRules.Copy(orderedFrequencies);
            PreviousChoices = SideEventGemSelectionRules.Copy(previousChoices);
            if (resolvedChoiceCount < 0) throw new ArgumentOutOfRangeException(nameof(resolvedChoiceCount));
            ResolvedChoiceCount = resolvedChoiceCount;
        }
    }

    public readonly struct SideEventGemFrequencyExpansion
    {
        public string AddedFrequencyContentId { get; }
        public bool Raised { get; }
        public int ChoiceCountAfterRefill { get; }
        internal SideEventGemFrequencyExpansion(string id, bool raised, int count)
        { AddedFrequencyContentId = id; Raised = raised; ChoiceCountAfterRefill = count; }
    }

    public sealed class SideEventGemOfferGenerationResult
    {
        public IReadOnlyList<SideEventGemChoice> Choices { get; }
        public IReadOnlyList<SideEventGemFrequencyExpansion> Expansions { get; }
        public int RequestedCount { get; }
        public int AllFrequencyEligibleGemCount { get; }
        public bool InitialPreviousGemsFiltered { get; }
        public int WeightedAttempts { get; }
        public bool HasShortfall => Choices.Count < RequestedCount;
        internal SideEventGemOfferGenerationResult(IReadOnlyList<SideEventGemChoice> choices,
            IReadOnlyList<SideEventGemFrequencyExpansion> expansions, int requestedCount, int allFrequencyCount,
            bool previousFiltered, int weightedAttempts)
        {
            Choices = SideEventGemSelectionRules.Copy(choices); Expansions = SideEventGemSelectionRules.Copy(expansions);
            RequestedCount = requestedCount; AllFrequencyEligibleGemCount = allFrequencyCount;
            InitialPreviousGemsFiltered = previousFiltered; WeightedAttempts = weightedAttempts;
        }
    }

    /// <summary>
    /// Owns the complete pure Gem generation algorithm: conditional previous-Gem filtering against full-frequency supply,
    /// distinct previous evolution outcomes, base selection, upward then downward frequency expansion and final shuffle.
    /// The full native database order is mandatory; neither names nor project grades infer it. Each expansion reloads and
    /// revalidates immutable evidence, removes every displayed Gem object and forces previous-normal-Gem exclusion.
    /// Unknown reachable sampling evidence is preflighted before RNG, an explicit project guard. Injected RNG and ordered
    /// frequency iteration are project adaptations; native seed equivalence is not claimed. Count/Luck, account policy,
    /// cash-out, component aid, Run persistence and card mutation belong to their owners, never to this stateless module.
    /// No display, animation or input code is executed. Calling Generate again means a new generation, never Back/restore.
    /// </summary>
    public static class SideEventGemOfferGenerationRules
    {
        public static SideEventGemOfferGenerationResult Generate(SideEventGemOfferGenerationRequest request, IRandomSource random)
        {
            if (request == null || random == null) throw new ArgumentNullException();
            ValidateFrequencies(request);
            ValidatePreviousIdentities(request);
            var expansions = new List<SideEventGemFrequencyExpansion>();
            var choices = new List<SideEventGemChoice>();
            if (request.ResolvedChoiceCount == 0) return Result(request, choices, expansions, 0, false, 0);

            var allSource = WithFrequencies(request.Source, request.OrderedFrequencies);
            var all = SideEventGemSelectionRules.Filter(allSource, request.Context, request.Candidates);
            if (all.Candidates.Count > 0) SideEventGemSelectionRules.Preflight(all);
            if (!request.Source.AllowFallbacks.IsKnown) throw new InvalidOperationException("Gem generation requires known fallback policy.");
            bool fallback = request.Source.AllowFallbacks.Value;
            bool filterPrevious = !fallback || all.Candidates.Count > request.ResolvedChoiceCount;
            var frequencies = request.Source.Frequencies.ToList();
            var pool = BuildPool(request, frequencies, filterPrevious, choices);
            var selection = SideEventGemSelectionRules.Select(pool, request.ResolvedChoiceCount, choices, random);
            choices = selection.Choices.ToList(); int attempts = selection.WeightedAttempts;
            while (fallback && choices.Count < request.ResolvedChoiceCount)
            {
                var added = Expand(request.OrderedFrequencies, frequencies, out bool raised);
                if (added == null) break;
                frequencies.Add(added);
                pool = BuildPool(request, frequencies, true, choices);
                selection = SideEventGemSelectionRules.Refill(pool, request.ResolvedChoiceCount, choices, random);
                choices = selection.Choices.ToList(); attempts = checked(attempts + selection.WeightedAttempts);
                expansions.Add(new SideEventGemFrequencyExpansion(added.ContentId, raised, choices.Count));
            }
            if (choices.Count > 1)
            {
                var original = choices.ToArray(); random.Shuffle(choices);
                if (choices.Count != original.Length || choices.Distinct().Count() != original.Length ||
                    !choices.All(original.Contains)) throw new InvalidOperationException("RNG shuffle changed the choice membership.");
                // Native shuffles Gem objects only. Its separate evolution-outcome cache keeps draw order.
                var outcomes = new Queue<string>(original.Where(choice => choice.EvolutionOutcomeContentId != null)
                    .Select(choice => choice.EvolutionOutcomeContentId));
                for (int i = 0; i < choices.Count; i++) if (choices[i].EvolutionOutcomeContentId != null)
                    choices[i] = new SideEventGemChoice(choices[i].GemContentId, choices[i].GemSourceAssetId, outcomes.Dequeue());
            }
            return Result(request, choices, expansions, all.Candidates.Count, filterPrevious, attempts);
        }

        private static SideEventGemSelectionPool BuildPool(SideEventGemOfferGenerationRequest request,
            IReadOnlyList<SideEventGemFrequency> frequencies, bool filterPrevious, IReadOnlyList<SideEventGemChoice> displayed)
        {
            var pool = SideEventGemSelectionRules.Filter(WithFrequencies(request.Source, frequencies), request.Context, request.Candidates);
            var previousNormal = new HashSet<string>(request.PreviousChoices.Where(choice => choice.EvolutionOutcomeContentId == null)
                .Select(choice => choice.GemSourceAssetId), StringComparer.Ordinal);
            var previousOutcomes = new HashSet<string>(request.PreviousChoices.Where(choice => choice.EvolutionOutcomeContentId != null)
                .Select(choice => choice.EvolutionOutcomeContentId), StringComparer.Ordinal);
            var bases = pool.LawfulEvolutionBases.Where(item => !previousOutcomes.Contains(item.OutcomeContentId)).ToList();
            var displayedIds = new HashSet<string>(displayed.Select(choice => choice.GemSourceAssetId), StringComparer.Ordinal);
            // Retain displayed evidence for selector validation; Refill excludes it from the working pool.
            var candidates = pool.Candidates.Where(item => (!filterPrevious || item.Evidence.IsEvolution ||
                !previousNormal.Contains(item.Evidence.SourceAssetId) || displayedIds.Contains(item.Evidence.SourceAssetId)) &&
                (!item.Evidence.IsEvolution || bases.Count > 0)).ToList();
            return new SideEventGemSelectionPool(pool.Source, pool.Context, candidates, pool.Rejected, bases);
        }

        private static SideEventGemFrequency Expand(IReadOnlyList<SideEventGemFrequency> ordered,
            IReadOnlyList<SideEventGemFrequency> current, out bool raised)
        {
            raised = true;
            if (current.Count == 0) return null;
            var included = new HashSet<string>(current.Select(item => item.ContentId), StringComparer.Ordinal);
            int first = -1, last = -1;
            for (int i = 0; i < ordered.Count; i++) if (included.Contains(ordered[i].ContentId))
            { if (first < 0) first = i; last = i; }
            for (int i = first; i < ordered.Count; i++) if (!included.Contains(ordered[i].ContentId)) return ordered[i];
            raised = false;
            for (int i = last; i >= 0; i--) if (!included.Contains(ordered[i].ContentId)) return ordered[i];
            return null;
        }

        private static void ValidateFrequencies(SideEventGemOfferGenerationRequest request)
        {
            var order = request.OrderedFrequencies.ToDictionary(item => item.ContentId, StringComparer.Ordinal);
            foreach (var item in request.Source.Frequencies)
                if (!order.TryGetValue(item.ContentId, out var canonical) || item.ChestWeight != canonical.ChestWeight ||
                    item.LevelUpWeight != canonical.LevelUpWeight || item.LevelUpLuckFactor != canonical.LevelUpLuckFactor)
                    throw new ArgumentException("Configured frequencies must match the complete source database.");
            foreach (var gem in request.Candidates)
                if (!order.ContainsKey(gem.Frequency.BaseContentId) || gem.Frequency.CurrentContentId != null &&
                    !order.ContainsKey(gem.Frequency.CurrentContentId)) throw new ArgumentException("Gem frequency identity is missing from the database.");
        }

        private static void ValidatePreviousIdentities(SideEventGemOfferGenerationRequest request)
        {
            foreach (var choice in request.PreviousChoices)
            {
                var candidate = request.Candidates.FirstOrDefault(item => item.ContentId == choice.GemContentId ||
                    item.SourceAssetId == choice.GemSourceAssetId);
                // Unresolved old cache entries cannot remove current supply. A partially matching alias must never do so.
                if (candidate != null && (candidate.ContentId != choice.GemContentId || candidate.SourceAssetId != choice.GemSourceAssetId ||
                    candidate.IsEvolution != (choice.EvolutionOutcomeContentId != null)))
                    throw new ArgumentException("Previous Gem identities contradict the frozen candidate registry.");
            }
        }

        private static SideEventGemOffer WithFrequencies(SideEventGemOffer source, IReadOnlyList<SideEventGemFrequency> frequencies)
            => new SideEventGemOffer(source.MinimumChoiceCount, source.MaximumChoiceCount, source.GuaranteeEvo, frequencies,
                source.CanCashOut, source.AllowFallbacks, source.EnsureNonEvo, source.CashOutAmount,
                source.NoTargetFallbackSourceAssetId, source.NoTargetFallbackQuantity);
        private static SideEventGemOfferGenerationResult Result(SideEventGemOfferGenerationRequest request,
            IReadOnlyList<SideEventGemChoice> choices, IReadOnlyList<SideEventGemFrequencyExpansion> expansions,
            int allCount, bool filtered, int attempts)
            => new SideEventGemOfferGenerationResult(choices, expansions, request.ResolvedChoiceCount, allCount, filtered, attempts);
    }
}
