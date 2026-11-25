using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    [Serializable]
    public sealed class LevelInteractionCardStatOfferingContract
    {
        public string sourceEventId;
        public string rootContentId;
        public string sourceFingerprint;
        public string poolPolicyId;
        public bool IsSpecified => !string.IsNullOrEmpty(sourceEventId) || !string.IsNullOrEmpty(rootContentId) ||
            !string.IsNullOrEmpty(sourceFingerprint) || !string.IsNullOrEmpty(poolPolicyId);
    }

    /// <summary>
    /// Explicit caller-admitted physical subset. Capturing freezes the original
    /// source projection and full physical state; it does not authorize a whole
    /// Run deck, account gate, source adapter or today's content query. The owner
    /// independently binds every supplied instance to its actual Run roster.
    /// </summary>
    public sealed class CardStatOfferingPreparedContext
    {
        public const string ExplicitSubsetPolicyId = "pool.card-stat.explicit-subset.v1";
        public SideEventSourceWireDocument Document { get; }
        public string RootContentId { get; }
        public string PoolPolicyId { get; }
        public string PoolPolicyCanonicalJson { get; }
        private readonly CardStatOfferingCandidateData[] _candidates;
        internal IReadOnlyList<CardStatOfferingCandidateData> CaptureCandidates()
            => _candidates.Select(candidate => DungeonRunCardStatOfferingSaveCodec.CloneCandidate(candidate)).ToArray();

        public CardStatOfferingPreparedContext(SideEventSourceWireDocument document,
            string rootContentId, string poolPolicyId, IReadOnlyList<CardConfig> explicitPhysicalCandidates,
            ICardAuthorReferenceResolver resolver = null)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            var specification = RequireSpecification(document, rootContentId);
            if (poolPolicyId != ExplicitSubsetPolicyId || explicitPhysicalCandidates == null)
                throw new ArgumentException("An explicit physical subset policy is required.");
            if (explicitPhysicalCandidates.Count > 0 && resolver == null)
                throw new ArgumentException("A nonempty physical subset requires explicit frozen-author reference bindings.");
            var ids = new HashSet<int>(); var candidates = new List<CardStatOfferingCandidateData>();
            foreach (var card in explicitPhysicalCandidates)
            {
                if (card == null || card.IsEncounterTemporary || !ids.Add(card.runtimeInstanceId))
                    throw new ArgumentException("Candidates must be unique persistent physical cards.");
                var projection = SideEventCardStatProjectionProof.Capture(card);
                var result = SideEventCardStatOfferingRules.Prepare(specification, projection.Projection.IsFcc,
                    projection.Projection.RawAllowInOfferingTable == 1, projection.Projection.ModifiedStats);
                if (result.Rewards.Count == 0) throw new ArgumentException("A candidate has no supported original stat tuple.");
                var saved = DungeonRunCardCodec.Capture(card, resolver);
                if (!DungeonRunCardCodec.IsKnown(saved)) throw new ArgumentException("New candidates require frozen known original authors.");
                projection.RequireMatchesOriginalAuthor(CardAuthorDocument.Parse(saved.baseAuthorJson));
                candidates.Add(new CardStatOfferingCandidateData { card = DungeonRunCardStatOfferingSaveCodec.CaptureCard(saved),
                    projectionProofJson = projection.CanonicalJson });
            }
            RootContentId = rootContentId; PoolPolicyId = poolPolicyId;
            PoolPolicyCanonicalJson = PolicyWire.Write(new PoolPolicyData { formatVersion = 1, poolPolicyId = poolPolicyId,
                selection = "ExplicitPhysicalSubset" });
            _candidates = candidates.ToArray();
        }

        internal static SideEventCardStatOfferingSpecification RequireSpecification(SideEventSourceWireDocument document, string rootId)
        {
            if (document == null || !document.Definition.TryGetRoot(rootId, out var root) ||
                !(root.Mechanic is SideEventCardStatOfferingSpecification specification))
                throw new InvalidOperationException("The frozen root must contain the complete CardStatOffering source specification.");
            // Source arithmetic owns the support table and independent fixed cash branch.
            SideEventCardStatOfferingRules.PrepareCashOut(specification);
            return specification;
        }
        private static ExactJsonWire PolicyWire => new ExactJsonWire(typeof(PoolPolicyData));
        internal static void ValidatePoolPolicy(string id, string json)
        {
            var data = PolicyWire.Read<PoolPolicyData>(json);
            if (id != ExplicitSubsetPolicyId || data == null || data.formatVersion != 1 || data.poolPolicyId != id ||
                data.selection != "ExplicitPhysicalSubset" || PolicyWire.Write(data) != json)
                throw new InvalidOperationException("Unknown or noncanonical CardStat offering pool policy.");
        }
        private sealed class PoolPolicyData { public int formatVersion; public string poolPolicyId; public string selection; }
    }

    public enum CardStatOfferingDisposition { Pending, Stats, Cash }

    public sealed class CardStatOfferingCandidate
    {
        public int RuntimeInstanceId { get; }
        public string ContentId { get; }
        public IReadOnlyList<SideEventCardStatOfferingReward> RequestedRewards { get; }
        internal CardStatOfferingCandidate(CardStatOfferingCandidateData candidate, SideEventCardStatOfferingSpecification specification)
        {
            RuntimeInstanceId = candidate.card.runtimeInstanceId; ContentId = candidate.card.contentId;
            var projection = SideEventCardStatProjectionProof.Parse(candidate.projectionProofJson).Projection;
            RequestedRewards = SideEventCardStatOfferingRules.Prepare(specification, projection.IsFcc,
                projection.RawAllowInOfferingTable == 1, projection.ModifiedStats).Rewards;
        }
    }

    /// <summary>A read-only view of the durable pending or completed decision.</summary>
    public sealed class CardStatOfferingOffer
    {
        public bool HasPendingChoice { get; }
        public bool AlreadyCompleted { get; }
        public IReadOnlyList<CardStatOfferingCandidate> Candidates { get; }
        public CardStatOfferingReceipt Receipt { get; }
        internal CardStatOfferingOffer(CardStatOfferingEntryData entry, CardStatOfferingReceipt receipt = null)
        {
            var document = SideEventSourceWireDocument.Parse(entry.sourceDocumentJson);
            var specification = CardStatOfferingPreparedContext.RequireSpecification(document, entry.sourceRootId);
            HasPendingChoice = entry.active; AlreadyCompleted = entry.finished; Receipt = receipt;
            Candidates = Array.AsReadOnly(entry.candidates.Select(candidate => new CardStatOfferingCandidate(candidate, specification)).ToArray());
        }
    }

    public sealed class CardStatOfferingReceipt
    {
        public LevelInteractionReceipt InteractionReceipt { get; }
        public CardStatOfferingDisposition Disposition { get; }
        public int SelectedRuntimeInstanceId { get; }
        public string StatApplicationProofJson { get; }
        public bool NewlyApplied => InteractionReceipt.NewlyApplied;
        internal CardStatOfferingReceipt(CardStatOfferingEntryData entry, LevelInteractionReceipt receipt)
        { InteractionReceipt = receipt; Disposition = (CardStatOfferingDisposition)entry.disposition;
          SelectedRuntimeInstanceId = entry.selectedRuntimeInstanceId; StatApplicationProofJson = entry.statApplicationProofJson; }
    }
}
