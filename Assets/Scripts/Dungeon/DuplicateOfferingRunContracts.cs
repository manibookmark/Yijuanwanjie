using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    [Serializable]
    public sealed class LevelInteractionDuplicateOfferingContract
    {
        public string sourceEventId;
        public string rootContentId;
        public string sourceFingerprint;
        public string poolPolicyId;
        public bool IsSpecified => !string.IsNullOrEmpty(sourceEventId) || !string.IsNullOrEmpty(rootContentId) ||
            !string.IsNullOrEmpty(sourceFingerprint) || !string.IsNullOrEmpty(poolPolicyId);
    }

    /// <summary>Explicit caller-authorized subset and breakthrough choice, separate from
    /// production pool approval. Capture retains frozen original/current author evidence;
    /// no mutable card, reference resolver, account or random stream is retained.</summary>
    public sealed class DuplicateOfferingPreparedContext
    {
        public const string ExplicitSubsetPolicyId = "pool.duplicate.explicit-physical-subset.v1";
        public SideEventSourceWireDocument Document { get; }
        public string RootContentId { get; }
        public string PoolPolicyId => ExplicitSubsetPolicyId;
        public string PoolPolicyCanonicalJson { get; }
        public DuplicateOfferingEligibility Eligibility { get; }
        public PersistentDuplicateLimitBreakPolicy LimitBreakPolicy { get; }
        private readonly DuplicateOfferingCardData[] _candidates;
        internal List<DuplicateOfferingCardData> CaptureCandidates()
            => _candidates.Select(value => new DuplicateOfferingCardData { canonicalJson = value.canonicalJson }).ToList();

        public DuplicateOfferingPreparedContext(SideEventSourceWireDocument document, string rootContentId,
            string poolPolicyId, DuplicateOfferingEligibility eligibility, IReadOnlyList<CardConfig> explicitCandidates,
            PersistentDuplicateLimitBreakPolicy limitBreakPolicy, ICardAuthorReferenceResolver references)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            RequireSpecification(document, rootContentId);
            if (poolPolicyId != ExplicitSubsetPolicyId || explicitCandidates == null || eligibility == null || !eligibility.IsUnlocked)
                throw new ArgumentException("An unlocked permanent gate and explicit physical subset are required.");
            if (!Enum.IsDefined(typeof(PersistentDuplicateLimitBreakPolicy), limitBreakPolicy))
                throw new ArgumentException("An explicit supported breakthrough policy is required.");
            var ids = new HashSet<int>(); var captured = new List<DuplicateOfferingCardData>();
            foreach (CardConfig card in explicitCandidates)
            {
                if (card == null || card.IsEncounterTemporary)
                    throw new ArgumentException("Candidates must be unique live persistent non-injected physical cards.");
                // The sole physical capture boundary rejects unbound, destroyed,
                // seal-copy and injection state without exposing Core instance internals.
                var saved = DungeonRunCardCodec.Capture(card, references);
                if (!ids.Add(saved.runtimeInstanceId))
                    throw new ArgumentException("Candidates must be unique live persistent non-injected physical cards.");
                RequireCandidate(saved);
                captured.Add(new DuplicateOfferingCardData { canonicalJson = DungeonRunCardCodec.CanonicalJson(saved) });
            }
            RootContentId = rootContentId; Eligibility = new DuplicateOfferingEligibility(true, eligibility.QualifyingRunId);
            LimitBreakPolicy = limitBreakPolicy; PoolPolicyCanonicalJson = WritePolicy(limitBreakPolicy); _candidates = captured.ToArray();
        }

        internal static SideEventDuplicateOfferingSpecification RequireSpecification(SideEventSourceWireDocument document, string rootId)
        {
            if (document == null || !document.Definition.TryGetRoot(rootId, out var root) ||
                !(root.Mechanic is SideEventDuplicateOfferingSpecification specification))
                throw new InvalidOperationException("The complete frozen source root must be DuplicateOffering.");
            var selection = specification.Selection; var copy = specification.CopyPolicy;
            if (selection.CardsRequired != 1 || selection.DestroySelected || !selection.ExcludeFcc || selection.RequireOfferingFlag ||
                selection.OutsideClickCloses || selection.RequireSupportedModifiedStat || !copy.PreserveOriginal ||
                !copy.CopyBaseAndCurrentConfig || !copy.CopySlotCountAndGemConfigs || copy.CopyManaCostModifier ||
                copy.CopyTemporaryManaCostModifier || copy.InitialBreakthroughCount != 0 ||
                copy.FinalBreakthroughState != SideEventCopyFinalState.UncapturedAfterGemCallbacks || copy.FinalIsEvolved.IsKnown)
                throw new InvalidOperationException("The source copy configuration contradicts this explicit project construction contract.");
            return specification;
        }
        internal static void RequireCandidate(DungeonRunCardSaveData card)
        {
            DungeonRunCardCodec.Validate(card);
            if (!DungeonRunCardCodec.IsKnown(card)) throw new InvalidOperationException("LegacyUnknown cannot prove original copy authors.");
            CardOfferingAdmission admission = CardAuthorDocument.Parse(card.baseAuthorJson).ReadOfferingAdmission();
            if (admission.Validate() != null || admission.SourceClass != "CardConfig" ||
                (admission.State != CardOfferingAdmissionState.Allowed && admission.State != CardOfferingAdmissionState.ExcludedByFlag))
                throw new InvalidOperationException("Duplicate requires exact known original non-FCC admission; flag zero remains legal.");
        }
        private static ExactJsonWire PolicyWire => new ExactJsonWire(typeof(DuplicateOfferingPolicyData));
        internal static string WritePolicy(PersistentDuplicateLimitBreakPolicy policy) => PolicyWire.Write(new DuplicateOfferingPolicyData {
            formatVersion = 1, poolPolicyId = ExplicitSubsetPolicyId, selection = "ExplicitPhysicalSubset", limitBreakPolicy = (int)policy });
        internal static PersistentDuplicateLimitBreakPolicy ReadPolicy(string id, string json)
        {
            var data = PolicyWire.Read<DuplicateOfferingPolicyData>(json);
            if (data == null || data.formatVersion != 1 || id != ExplicitSubsetPolicyId || data.poolPolicyId != id ||
                data.selection != "ExplicitPhysicalSubset" || !Enum.IsDefined(typeof(PersistentDuplicateLimitBreakPolicy), data.limitBreakPolicy) ||
                PolicyWire.Write(data) != json) throw new InvalidOperationException("Unknown or noncanonical explicit duplicate policy.");
            return (PersistentDuplicateLimitBreakPolicy)data.limitBreakPolicy;
        }
        private sealed class DuplicateOfferingPolicyData
        { public int formatVersion; public string poolPolicyId; public string selection; public int limitBreakPolicy; }
    }

    public sealed class DuplicateOfferingCandidate
    {
        public int RuntimeInstanceId { get; }
        public string ContentId { get; }
        public bool IsAvailable { get; }
        internal DuplicateOfferingCandidate(DuplicateOfferingCardData frozen, IReadOnlyList<DungeonRunCardSaveData> live)
        {
            var card = DungeonRunCardCodec.Parse(frozen.canonicalJson); RuntimeInstanceId = card.runtimeInstanceId; ContentId = card.contentId;
            IsAvailable = live.Any(value => value.runtimeInstanceId == RuntimeInstanceId && DungeonRunCardCodec.CanonicalJson(value) == frozen.canonicalJson);
        }
    }
    public sealed class DuplicateOfferingOffer
    {
        public bool HasPendingChoice { get; }
        public bool AlreadyCompleted { get; }
        public bool IsDormant { get; }
        public PersistentDuplicateLimitBreakPolicy LimitBreakPolicy { get; }
        public IReadOnlyList<DuplicateOfferingCandidate> Candidates { get; }
        public DuplicateOfferingReceipt Receipt { get; }
        internal DuplicateOfferingOffer(DuplicateOfferingEntryData entry, IReadOnlyList<DungeonRunCardSaveData> live, DuplicateOfferingReceipt receipt)
        {
            HasPendingChoice = entry.active; AlreadyCompleted = entry.finished; IsDormant = !entry.active && !entry.finished;
            LimitBreakPolicy = DuplicateOfferingPreparedContext.ReadPolicy(entry.poolPolicyId, entry.poolPolicyJson);
            Candidates = Array.AsReadOnly(entry.candidates.Select(value => new DuplicateOfferingCandidate(value, live)).ToArray()); Receipt = receipt;
        }
    }
    public sealed class DuplicateOfferingReceipt
    {
        public int SourceRuntimeInstanceId { get; }
        public int CreatedRuntimeInstanceId { get; }
        public string CopiedCardCanonicalJson { get; }
        public LevelInteractionReceipt InteractionReceipt { get; }
        public bool NewlyApplied => InteractionReceipt.NewlyApplied;
        internal DuplicateOfferingReceipt(DuplicateOfferingEntryData entry, LevelInteractionReceipt receipt)
        { SourceRuntimeInstanceId = entry.selectedRuntimeInstanceId; CreatedRuntimeInstanceId = entry.createdRuntimeInstanceId;
          CopiedCardCanonicalJson = entry.createdCardJson; InteractionReceipt = receipt; }
    }
}
