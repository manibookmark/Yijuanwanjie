using System;
using System.Collections.Generic;
using System.Linq;

namespace WanjieScroll.Core
{
    public enum SideEventGemFrequencyShift { Unknown, Unchanged, Raised, Lowered }
    public enum SideEventGemRejection
    { NoLawfulEvolutionBase, Locked, FrequencyUnavailable, Sealed, Banished, NoCompatibleHost, ColorUnavailable }

    /// <summary>Resolved native frequency identity and its comparison to the base index; names and weights cannot infer the shift.</summary>
    public sealed class SideEventGemFrequencyEvidence
    {
        public string BaseContentId { get; }
        public string CurrentContentId { get; }
        public SideEventGemFrequencyShift Shift { get; }
        public SideEventGemFrequencyEvidence(string baseContentId, string currentContentId, SideEventGemFrequencyShift shift)
        {
            BaseContentId = SideEventGemSelectionRules.Id(baseContentId);
            CurrentContentId = currentContentId == null ? null : SideEventGemSelectionRules.Id(currentContentId);
            if (!Enum.IsDefined(typeof(SideEventGemFrequencyShift), shift)) throw new ArgumentOutOfRangeException(nameof(shift));
            if (shift == SideEventGemFrequencyShift.Unchanged && CurrentContentId != BaseContentId ||
                (shift == SideEventGemFrequencyShift.Raised || shift == SideEventGemFrequencyShift.Lowered) &&
                (CurrentContentId == null || CurrentContentId == BaseContentId))
                throw new ArgumentException("Frequency comparison contradicts the resolved identities.");
            Shift = shift;
        }
    }

    /// <summary>
    /// One passed-player host's evidence for this Gem. Exclusion groups and installed-Gem tag groups must be fully resolved
    /// into the supplied tag sets. EffectsExcluded is the concrete source-effect predicate, never a CardSealRules approximation.
    /// </summary>
    public sealed class SideEventGemHostEvidence
    {
        public int RuntimeInstanceId { get; }
        public int? EmptySlots { get; }
        public IReadOnlyList<string> InstalledGemSourceIds { get; }
        public IReadOnlyList<string> CardExcludedTags { get; }
        public IReadOnlyList<string> DynamicIncludedTags { get; }
        public IReadOnlyList<string> DynamicExcludedTags { get; }
        public SideEventOptionalBool EffectsExcluded { get; }
        public SideEventGemHostEvidence(int runtimeInstanceId, int? emptySlots, IReadOnlyList<string> installedGemSourceIds,
            IReadOnlyList<string> cardExcludedTags, IReadOnlyList<string> dynamicIncludedTags,
            IReadOnlyList<string> dynamicExcludedTags, SideEventOptionalBool effectsExcluded)
        {
            if (runtimeInstanceId <= 0 || emptySlots < 0) throw new ArgumentOutOfRangeException(nameof(runtimeInstanceId));
            RuntimeInstanceId = runtimeInstanceId; EmptySlots = emptySlots; EffectsExcluded = effectsExcluded;
            InstalledGemSourceIds = SideEventGemSelectionRules.Ids(installedGemSourceIds);
            CardExcludedTags = SideEventGemSelectionRules.Ids(cardExcludedTags);
            DynamicIncludedTags = SideEventGemSelectionRules.Ids(dynamicIncludedTags);
            DynamicExcludedTags = SideEventGemSelectionRules.Ids(dynamicExcludedTags);
        }
    }

    /// <summary>Global-player evolution prerequisites. Component counts are owned representatives, not card levels.</summary>
    public sealed class SideEventGemEvolutionEvidence
    {
        public string BaseSourceCardId { get; }
        public string OutcomeContentId { get; }
        public SideEventOptionalBool IsWeapon { get; }
        public SideEventOptionalBool OutcomeUnlocked { get; }
        public IReadOnlyList<int?> ComponentCounts { get; }
        public SideEventOptionalBool RequiredWeaponHasEmptySlot { get; }
        public SideEventGemEvolutionEvidence(string baseSourceCardId, string outcomeContentId, SideEventOptionalBool isWeapon,
            SideEventOptionalBool outcomeUnlocked, IReadOnlyList<int?> componentCounts, SideEventOptionalBool requiredWeaponHasEmptySlot)
        {
            BaseSourceCardId = SideEventGemSelectionRules.Id(baseSourceCardId);
            OutcomeContentId = SideEventGemSelectionRules.Id(outcomeContentId);
            IsWeapon = isWeapon; OutcomeUnlocked = outcomeUnlocked; RequiredWeaponHasEmptySlot = requiredWeaponHasEmptySlot;
            ComponentCounts = SideEventGemSelectionRules.Copy(componentCounts);
            if (ComponentCounts.Any(value => value < 0)) throw new ArgumentOutOfRangeException(nameof(componentCounts));
        }
    }

    /// <summary>Explicit immutable evidence; null weight and optional Unknown flags remain unknown, never default eligibility.</summary>
    public sealed class SideEventGemCandidateEvidence
    {
        public string ContentId { get; }
        public string SourceAssetId { get; }
        public float? Weight { get; }
        public bool IsEvolution { get; }
        public bool IsUnique { get; }
        public SideEventOptionalBool Unlocked { get; }
        public SideEventOptionalBool Sealed { get; }
        public SideEventOptionalBool Banished { get; }
        public SideEventGemFrequencyEvidence Frequency { get; }
        public IReadOnlyList<string> Tags { get; }
        public SideEventOptionalBool HasNonColorEffect { get; }
        public IReadOnlyList<string> ColorTriggerTypeIds { get; }
        public IReadOnlyList<SideEventGemHostEvidence> Hosts { get; }
        public SideEventGemCandidateEvidence(string contentId, string sourceAssetId, float? weight, bool isEvolution,
            bool isUnique, SideEventOptionalBool unlocked, SideEventOptionalBool sealedGem, SideEventOptionalBool banished,
            SideEventGemFrequencyEvidence frequency, IReadOnlyList<string> tags, SideEventOptionalBool hasNonColorEffect,
            IReadOnlyList<string> colorTriggerTypeIds, IReadOnlyList<SideEventGemHostEvidence> hosts)
        {
            ContentId = SideEventGemSelectionRules.Id(contentId); SourceAssetId = SideEventGemSelectionRules.Id(sourceAssetId);
            if (weight.HasValue) SideEventGemSelectionRules.Weight(weight.Value);
            Weight = weight; IsEvolution = isEvolution; IsUnique = isUnique; Unlocked = unlocked; Sealed = sealedGem; Banished = banished;
            Frequency = frequency ?? throw new ArgumentNullException(nameof(frequency));
            Tags = SideEventGemSelectionRules.Ids(tags); HasNonColorEffect = hasNonColorEffect;
            ColorTriggerTypeIds = SideEventGemSelectionRules.Ids(colorTriggerTypeIds); Hosts = SideEventGemSelectionRules.Copy(hosts);
            if (Hosts.Select(host => host.RuntimeInstanceId).Distinct().Count() != Hosts.Count)
                throw new ArgumentException("A host instance appears more than once.");
        }
    }

    public sealed class SideEventGemEligibilityContext
    {
        public SideEventOptionalBool JewellerDisabled { get; }
        public IReadOnlyList<string> AllTagIds { get; }
        public IReadOnlyList<string> GlobalFccTriggerTypeIds { get; }
        public IReadOnlyList<SideEventGemEvolutionEvidence> EvolutionBases { get; }
        public SideEventGemEligibilityContext(SideEventOptionalBool jewellerDisabled, IReadOnlyList<string> allTagIds,
            IReadOnlyList<string> globalFccTriggerTypeIds, IReadOnlyList<SideEventGemEvolutionEvidence> evolutionBases)
        {
            JewellerDisabled = jewellerDisabled; AllTagIds = SideEventGemSelectionRules.Ids(allTagIds);
            GlobalFccTriggerTypeIds = SideEventGemSelectionRules.Ids(globalFccTriggerTypeIds);
            EvolutionBases = SideEventGemSelectionRules.Copy(evolutionBases);
            if (EvolutionBases.Select(item => item.BaseSourceCardId).Distinct(StringComparer.Ordinal).Count() != EvolutionBases.Count)
                throw new ArgumentException("Evolution base identities must be unique.");
        }
    }

    public sealed class SideEventGemEligibleCandidate
    {
        public SideEventGemCandidateEvidence Evidence { get; }
        public IReadOnlyList<int> HostRuntimeInstanceIds { get; }
        internal SideEventGemEligibleCandidate(SideEventGemCandidateEvidence evidence, IReadOnlyList<int> hosts)
        { Evidence = evidence; HostRuntimeInstanceIds = SideEventGemSelectionRules.Copy(hosts); }
    }
    public readonly struct SideEventGemRejectedCandidate
    {
        public string ContentId { get; }
        public SideEventGemRejection Reason { get; }
        internal SideEventGemRejectedCandidate(string id, SideEventGemRejection reason) { ContentId = id; Reason = reason; }
    }
    public sealed class SideEventGemSelectionPool
    {
        public SideEventGemOffer Source { get; }
        public SideEventGemEligibilityContext Context { get; }
        public IReadOnlyList<SideEventGemEligibleCandidate> Candidates { get; }
        public IReadOnlyList<SideEventGemRejectedCandidate> Rejected { get; }
        public IReadOnlyList<SideEventGemEvolutionEvidence> LawfulEvolutionBases { get; }
        internal SideEventGemSelectionPool(SideEventGemOffer source, SideEventGemEligibilityContext context,
            IReadOnlyList<SideEventGemEligibleCandidate> candidates, IReadOnlyList<SideEventGemRejectedCandidate> rejected,
            IReadOnlyList<SideEventGemEvolutionEvidence> evolutionBases)
        {
            Source = source; Context = context; Candidates = SideEventGemSelectionRules.Copy(candidates);
            Rejected = SideEventGemSelectionRules.Copy(rejected); LawfulEvolutionBases = SideEventGemSelectionRules.Copy(evolutionBases);
        }
    }
    public sealed class SideEventGemChoice
    {
        public string GemContentId { get; }
        public string GemSourceAssetId { get; }
        public string EvolutionOutcomeContentId { get; }
        public SideEventGemChoice(string gemContentId, string gemSourceAssetId, string evolutionOutcomeContentId = null)
        {
            GemContentId = SideEventGemSelectionRules.Id(gemContentId); GemSourceAssetId = SideEventGemSelectionRules.Id(gemSourceAssetId);
            EvolutionOutcomeContentId = evolutionOutcomeContentId == null ? null : SideEventGemSelectionRules.Id(evolutionOutcomeContentId);
        }
    }
    public sealed class SideEventGemSelectionResult
    {
        public IReadOnlyList<SideEventGemChoice> Choices { get; }
        public int RequestedCount { get; }
        public int WeightedAttempts { get; }
        public bool HasShortfall => Choices.Count < RequestedCount;
        internal SideEventGemSelectionResult(IReadOnlyList<SideEventGemChoice> choices, int requestedCount, int weightedAttempts)
        { Choices = SideEventGemSelectionRules.Copy(choices); RequestedCount = requestedCount; WeightedAttempts = weightedAttempts; }
    }

    /// <summary>
    /// Owns source Gem eligibility and base-frequency selection; holds no Run/account, mutable reward ledger, UI or Unity object.
    /// Filter follows GemConfig.FilterGemPool; source global evolution/FCC evidence is separate from passed-player hosts/banish.
    /// Select consumes an explicitly resolved count (Luck/count calculation belongs to its future source adapter). Source
    /// AllowFallbacks also gates normal weighted draws; this module never expands fallback frequencies or grants component aid.
    /// Unknown evidence needed by an executed predicate fails closed. Sampling evidence is preflighted before any RNG.
    /// Canonical stable-ID inputs must map one-to-one to native objects; input frequency order is a project adaptation of native
    /// HashSet iteration. Injected RNG is mandatory; same native seed/state equivalence is not claimed. Negative/nonfinite weights
    /// and overflow are rejected project guards. Each accumulation rounds to binary32, matching native addss.
    /// Scope is one pure algorithm plus its immutable input/result vocabulary; no second mutable state owner is introduced.
    /// </summary>
    public static class SideEventGemSelectionRules
    {
        public static SideEventGemSelectionPool Filter(SideEventGemOffer source, SideEventGemEligibilityContext context,
            IReadOnlyList<SideEventGemCandidateEvidence> candidates)
        {
            if (source == null || context == null || candidates == null) throw new ArgumentNullException();
            var input = Copy(candidates);
            if (input.Select(gem => gem.ContentId).Distinct(StringComparer.Ordinal).Count() != input.Count ||
                input.Select(gem => gem.SourceAssetId).Distinct(StringComparer.Ordinal).Count() != input.Count || input.Count(gem => gem.IsEvolution) > 1)
                throw new ArgumentException("Candidates require one-to-one identities and at most the one native Evo Gem.");
            var bases = context.EvolutionBases.Where(LawfulEvolution).ToList();
            var eligible = new List<SideEventGemEligibleCandidate>(); var rejected = new List<SideEventGemRejectedCandidate>();
            foreach (var gem in input)
            {
                SideEventGemRejection? reason = null;
                if (gem.IsEvolution && bases.Count == 0) reason = SideEventGemRejection.NoLawfulEvolutionBase;
                else if (!Known(gem.Unlocked)) reason = SideEventGemRejection.Locked;
                else if (!source.Frequencies.Any(frequency => frequency.ContentId == gem.Frequency.BaseContentId) &&
                    (Known(context.JewellerDisabled) || gem.Frequency.CurrentContentId == null ||
                     !source.Frequencies.Any(frequency => frequency.ContentId == gem.Frequency.CurrentContentId)))
                {
                    if (!Known(context.JewellerDisabled) && gem.Frequency.CurrentContentId == null) throw Unknown("current frequency");
                    reason = SideEventGemRejection.FrequencyUnavailable;
                }
                else if (!Known(context.JewellerDisabled) && Known(gem.Sealed)) reason = SideEventGemRejection.Sealed;
                else if (Known(gem.Banished)) reason = SideEventGemRejection.Banished;
                var hosts = new List<int>();
                if (!reason.HasValue)
                {
                    foreach (var host in gem.Hosts) if (Compatible(gem, host, context)) hosts.Add(host.RuntimeInstanceId);
                    if (hosts.Count == 0) reason = SideEventGemRejection.NoCompatibleHost;
                    else if (!Known(gem.HasNonColorEffect) && !gem.ColorTriggerTypeIds.Intersect(context.GlobalFccTriggerTypeIds, StringComparer.Ordinal).Any())
                        reason = SideEventGemRejection.ColorUnavailable;
                }
                if (reason.HasValue) rejected.Add(new SideEventGemRejectedCandidate(gem.ContentId, reason.Value));
                else eligible.Add(new SideEventGemEligibleCandidate(gem, hosts));
            }
            return new SideEventGemSelectionPool(source, context, eligible, rejected, bases);
        }

        /// <summary>
        /// alreadyDisplayed is this working offer, not the native conditional previous-reroll history. Normal Gems are unique;
        /// Evo can recur only with distinct outcomes. Exhausted supply stays empty; no fake Gem, cash-out or fallback is created.
        /// </summary>
        public static SideEventGemSelectionResult Select(SideEventGemSelectionPool pool, int resolvedChoiceCount,
            IReadOnlyList<SideEventGemChoice> alreadyDisplayed, IRandomSource random)
            => SelectCore(pool, resolvedChoiceCount, alreadyDisplayed, random, false);

        // A native frequency reload removes every displayed Gem object, including Evo.
        internal static SideEventGemSelectionResult Refill(SideEventGemSelectionPool pool, int resolvedChoiceCount,
            IReadOnlyList<SideEventGemChoice> alreadyDisplayed, IRandomSource random)
            => SelectCore(pool, resolvedChoiceCount, alreadyDisplayed, random, true);

        private static SideEventGemSelectionResult SelectCore(SideEventGemSelectionPool pool, int resolvedChoiceCount,
            IReadOnlyList<SideEventGemChoice> alreadyDisplayed, IRandomSource random, bool excludeDisplayedEvolution)
        {
            if (pool == null || random == null || alreadyDisplayed == null) throw new ArgumentNullException();
            if (resolvedChoiceCount < 0) throw new ArgumentOutOfRangeException(nameof(resolvedChoiceCount));
            var displayed = Copy(alreadyDisplayed);
            if (displayed.Count > resolvedChoiceCount) throw new ArgumentException("Displayed choices exceed the resolved offer size.");
            var normalIds = new HashSet<string>(StringComparer.Ordinal); var outcomes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in displayed)
            {
                var candidate = pool.Candidates.FirstOrDefault(item => item.Evidence.ContentId == choice.GemContentId &&
                    item.Evidence.SourceAssetId == choice.GemSourceAssetId);
                if (candidate == null || candidate.Evidence.IsEvolution != (choice.EvolutionOutcomeContentId != null) ||
                    candidate.Evidence.IsEvolution && !pool.LawfulEvolutionBases.Any(item => item.OutcomeContentId == choice.EvolutionOutcomeContentId))
                    throw new ArgumentException("Displayed choices must match the eligible Gem and evolution evidence.");
                if (!(choice.EvolutionOutcomeContentId == null ? normalIds.Add(choice.GemSourceAssetId) : outcomes.Add(choice.EvolutionOutcomeContentId)))
                    throw new ArgumentException("Already displayed choices contain duplicate identities.");
            }
            var working = pool.Candidates.Where(item => !normalIds.Contains(item.Evidence.SourceAssetId)).ToList();
            if (excludeDisplayedEvolution && outcomes.Count > 0) working.RemoveAll(item => item.Evidence.IsEvolution);
            var bases = pool.LawfulEvolutionBases.Where(item => !outcomes.Contains(item.OutcomeContentId)).ToList();
            if (bases.Count == 0) working.RemoveAll(item => item.Evidence.IsEvolution);
            var result = displayed.ToList(); int attempts = 0;
            if (result.Count >= resolvedChoiceCount || working.Count == 0) return new SideEventGemSelectionResult(result, resolvedChoiceCount, attempts);
            bool weighted = Known(pool.Source.AllowFallbacks), ensureNonEvo = Known(pool.Source.EnsureNonEvo);
            bool disabled = Known(pool.Context.JewellerDisabled);
            ValidateSampling(pool, working, disabled);
            int evoCount = displayed.Count(choice => choice.EvolutionOutcomeContentId != null);
            int evoTarget = resolvedChoiceCount - (ensureNonEvo ? 1 : 0);
            for (int slot = result.Count; slot < resolvedChoiceCount && working.Count > 0; slot++)
            {
                SideEventGemEligibleCandidate selected = null;
                var evo = working.Find(item => item.Evidence.IsEvolution);
                bool guaranteedEvolution = pool.Source.GuaranteeEvo && evo != null && bases.Count > 0 && evoCount <= evoTarget;
                if (guaranteedEvolution) selected = evo;
                else if (weighted)
                {
                    attempts++;
                    selected = ChooseWeighted(pool.Source, working, disabled, random);
                }
                if (selected == null) continue;
                if (!guaranteedEvolution) working.Remove(selected);
                string outcome = null;
                if (selected.Evidence.IsEvolution)
                {
                    if (bases.Count == 0) throw new InvalidOperationException("Evolution supply was exhausted.");
                    int index = random.Range(0, bases.Count);
                    if (index < 0 || index >= bases.Count) throw new InvalidOperationException("RNG returned an invalid evolution index.");
                    outcome = bases[index].OutcomeContentId; bases.RemoveAll(item => item.OutcomeContentId == outcome); evoCount++;
                    if (guaranteedEvolution && (bases.Count == 0 || evoCount >= evoTarget)) working.Remove(selected);
                }
                else working.Remove(selected);
                result.Add(new SideEventGemChoice(selected.Evidence.ContentId, selected.Evidence.SourceAssetId, outcome));
            }
            return new SideEventGemSelectionResult(result, resolvedChoiceCount, attempts);
        }

        // The full generator preflights every reachable frequency before consuming any RNG.
        internal static void Preflight(SideEventGemSelectionPool pool)
        {
            Known(pool.Source.AllowFallbacks); Known(pool.Source.EnsureNonEvo);
            ValidateSampling(pool, pool.Candidates, Known(pool.Context.JewellerDisabled));
        }
        private static void ValidateSampling(SideEventGemSelectionPool pool,
            IReadOnlyList<SideEventGemEligibleCandidate> candidates, bool disabled)
        {
            foreach (var item in candidates)
            {
                if (!item.Evidence.Weight.HasValue) throw Unknown("Gem weight");
                ValidateMatchingFrequency(item.Evidence.Frequency, disabled);
            }
            var represented = pool.Source.Frequencies.Where(frequency => candidates.Any(item => Matches(item.Evidence.Frequency, frequency.ContentId, disabled))).ToList();
            Sum(represented.Select(frequency => frequency.ChestWeight));
            foreach (var frequency in represented)
                Sum(candidates.Where(item => Matches(item.Evidence.Frequency, frequency.ContentId, disabled)).Select(item => item.Evidence.Weight.Value));
        }

        private static SideEventGemEligibleCandidate ChooseWeighted(SideEventGemOffer source,
            List<SideEventGemEligibleCandidate> pool, bool disabled, IRandomSource random)
        {
            var frequencies = source.Frequencies.Where(frequency => pool.Any(item => Matches(item.Evidence.Frequency, frequency.ContentId, disabled))).ToList();
            if (frequencies.Count == 0) return null;
            float draw = Draw(random, Sum(frequencies.Select(item => item.ChestWeight))), cumulative = 0f;
            SideEventGemFrequency frequencyChoice = null;
            foreach (var frequency in frequencies)
            {
                cumulative = Add(cumulative, frequency.ChestWeight);
                if (cumulative > draw) { frequencyChoice = frequency; break; }
            }
            // Native frequency exhaustion fails; the Gem-level exhaustion below deliberately returns the real last item.
            if (frequencyChoice == null) return null;
            var matching = pool.Where(item => Matches(item.Evidence.Frequency, frequencyChoice.ContentId, disabled)).ToList();
            draw = Draw(random, Sum(matching.Select(item => item.Evidence.Weight.Value))); cumulative = 0f;
            foreach (var item in matching)
            {
                cumulative = Add(cumulative, item.Evidence.Weight.Value);
                if (cumulative > draw) return item;
            }
            return matching[matching.Count - 1];
        }
        private static bool LawfulEvolution(SideEventGemEvolutionEvidence item)
        {
            if (!Known(item.IsWeapon) || !Known(item.OutcomeUnlocked)) return false;
            foreach (int? count in item.ComponentCounts)
            { if (!count.HasValue) throw Unknown("component Count"); if (count.Value == 0) return false; }
            return Known(item.RequiredWeaponHasEmptySlot);
        }
        private static bool Compatible(SideEventGemCandidateEvidence gem, SideEventGemHostEvidence host, SideEventGemEligibilityContext context)
        {
            if (!host.EmptySlots.HasValue) throw Unknown("empty slot count");
            if (host.EmptySlots.Value == 0 || gem.IsUnique && host.InstalledGemSourceIds.Contains(gem.SourceAssetId)) return false;
            var allowed = new HashSet<string>(context.AllTagIds, StringComparer.Ordinal);
            allowed.ExceptWith(host.CardExcludedTags); allowed.UnionWith(host.DynamicIncludedTags); allowed.ExceptWith(host.DynamicExcludedTags);
            var tags = gem.Tags.Count == 0 ? context.AllTagIds : gem.Tags;
            return tags.Any(allowed.Contains) && !Known(host.EffectsExcluded);
        }
        private static bool Matches(SideEventGemFrequencyEvidence frequency, string id, bool disabled)
            => disabled || frequency.Shift == SideEventGemFrequencyShift.Unchanged ? frequency.BaseContentId == id :
               frequency.CurrentContentId == id || frequency.Shift == SideEventGemFrequencyShift.Lowered && frequency.BaseContentId == id;
        private static void ValidateMatchingFrequency(SideEventGemFrequencyEvidence frequency, bool disabled)
        { if (!disabled && (frequency.CurrentContentId == null || frequency.Shift == SideEventGemFrequencyShift.Unknown)) throw Unknown("frequency comparison"); }
        private static float Draw(IRandomSource random, float total)
        {
            float value = random.Range(0f, total);
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > total)
                throw new InvalidOperationException("RNG returned an invalid weighted draw.");
            return value;
        }
        private static float Sum(IEnumerable<float> weights)
        { float total = 0f; foreach (float weight in weights) total = Add(total, Weight(weight)); return total; }
        private static float Add(float left, float right)
        { float result = (float)((double)left + right); if (float.IsInfinity(result)) throw new InvalidOperationException("Weight total overflow."); return result; }
        private static bool Known(SideEventOptionalBool value)
        { if (!value.IsKnown) throw Unknown("qualification flag"); return value.Value; }
        private static InvalidOperationException Unknown(string field) => new InvalidOperationException("Gem selection requires known " + field + " evidence.");
        internal static float Weight(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) throw new ArgumentOutOfRangeException(nameof(value)); return value; }
        internal static string Id(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)) throw new ArgumentException("Missing or invalid stable identity.");
            for (int i = 0; i < value.Length; i++) if (char.IsSurrogate(value[i]) &&
                (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length || !char.IsLowSurrogate(value[++i]))) throw new ArgumentException("Invalid identity Unicode.");
            return value;
        }
        internal static IReadOnlyList<string> Ids(IReadOnlyList<string> values)
        { var copy = Copy(values); foreach (string value in copy) Id(value); return copy; }
        internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new T[values.Count]; for (int i = 0; i < copy.Length; i++)
            { if (!typeof(T).IsValueType && ReferenceEquals(values[i], null)) throw new ArgumentException("An evidence collection contains null."); copy[i] = values[i]; }
            return Array.AsReadOnly(copy);
        }
    }
}
