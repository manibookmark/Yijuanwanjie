using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    [Serializable]
    public sealed class LevelInteractionGunForHireContract
    {
        public string sourceEventId, rootContentId, sourceFingerprint, poolPolicyId;
        public bool IsSpecified => !string.IsNullOrEmpty(sourceEventId) || !string.IsNullOrEmpty(rootContentId) ||
            !string.IsNullOrEmpty(sourceFingerprint) || !string.IsNullOrEmpty(poolPolicyId);
    }

    /// <summary>Payment capability, independent of income collection. DebitExact
    /// must subtract exactly the requested balance or throw without partial debit.
    /// RestoreBalance restores the balance checkpoint; Run income belongs to Run.</summary>
    public interface IDungeonRunHireWallet : IDungeonRunFlatCashOutWallet
    {
        void DebitExact(int amount);
    }

    /// <summary>An explicitly identified source FCC and detached current author.
    /// This is not an ordinary-card mapping, permanent unlock query or publication
    /// approval. Admission and original asset identity must agree exactly.</summary>
    public sealed class GunForHireCandidate
    {
        public CardAuthorDocument Author { get; }
        public string SourceAssetId { get; }
        public SideEventSourceProvenance Provenance { get; }
        public bool AdmittedUnlock { get; }
        public GunForHireCandidate(CardConfig author, string sourceAssetId, SideEventSourceProvenance provenance,
            bool admittedUnlock, ICardAuthorReferenceResolver resolver)
            : this(CardAuthorDocument.Capture(author, resolver), sourceAssetId, provenance, admittedUnlock) { }
        internal GunForHireCandidate(CardAuthorDocument author, string sourceAssetId,
            SideEventSourceProvenance provenance, bool admittedUnlock)
        {
            Author = author ?? throw new ArgumentNullException(nameof(author));
            SourceAssetId = sourceAssetId; Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            AdmittedUnlock = admittedUnlock;
            DungeonRunGunForHireSaveCodec.RequireId(sourceAssetId);
            var admission = author.ReadOfferingAdmission();
            if (admission.State != CardOfferingAdmissionState.ExcludedFcc || admission.SourceClass != "FccConfig" ||
                admission.SourceConfigId != sourceAssetId || admission.Validate() != null)
                throw new ArgumentException("Hire requires explicit matching source FccConfig evidence.");
        }
    }

    public sealed class GunForHirePreparedContext
    {
        public const string ExplicitSubsetPolicyId = "pool.hire.explicit-fcc-subset.v1";
        public SideEventSourceWireDocument Document { get; }
        public string RootContentId { get; }
        public int CompletedSourceFloorCount { get; }
        public bool EventAdmitted { get; }
        public IReadOnlyList<GunForHireCandidate> Candidates { get; }
        public GunForHirePreparedContext(SideEventSourceWireDocument document, string rootContentId,
            int completedSourceFloorCount, IReadOnlyList<GunForHireCandidate> candidates, bool eventAdmitted)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document)); RootContentId = rootContentId;
            RequireSpecification(document, rootContentId);
            if (completedSourceFloorCount < 0 || candidates == null) throw new ArgumentException("Explicit source-floor count and FCC subset are required.");
            CompletedSourceFloorCount = completedSourceFloorCount;
            if (!eventAdmitted) throw new ArgumentException("An explicit admitted Hire event gate is required.");
            EventAdmitted = eventAdmitted;
            var ids = new HashSet<string>(StringComparer.Ordinal); var sources = new HashSet<string>(StringComparer.Ordinal);
            var objects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
                if (candidate == null || !ids.Add(candidate.Author.ContentId) || !sources.Add(candidate.SourceAssetId) ||
                    !objects.Add(candidate.Provenance.Alias + "\n" + candidate.Provenance.SourceFile + "\n" + candidate.Provenance.PathId))
                    throw new ArgumentException("FCC subset contains null or duplicated project/source identity.");
            Candidates = new ReadOnlyCollection<GunForHireCandidate>(candidates.ToArray());
        }
        internal static SideEventGunForHireSpecification RequireSpecification(SideEventSourceWireDocument document, string rootId)
        {
            if (document == null || !document.Definition.TryGetRoot(rootId, out var root) || root.Kind != SideEventSourceKind.GunForHire ||
                root.SourceAssetId != "PassiveEvent_GunForHire" || !(root.Mechanic is SideEventGunForHireSpecification specification))
                throw new ArgumentException("The complete GunForHire source root is required.");
            SideEventGunForHirePriceRules.ValidateSpecification(specification); return specification;
        }
    }

    public sealed class GunForHireOffer
    {
        public string NodeContentId { get; }
        public GunForHireCandidate Candidate { get; }
        public SideEventGunForHireQuote Quote { get; }
        public bool Active { get; }
        public bool AlreadyCompleted { get; }
        internal GunForHireOffer(GunForHireEntryData entry)
        {
            NodeContentId = entry.nodeId; Active = entry.active; AlreadyCompleted = entry.finished;
            Candidate = entry.selectedIndex < 0 ? null : DungeonRunGunForHireSaveCodec.RestoreCandidate(entry.candidates[entry.selectedIndex]);
            Quote = DungeonRunGunForHireSaveCodec.Quote(entry);
        }
    }
    public enum GunForHireDisposition { None, Buy, Pity }
    public sealed class GunForHireReceipt
    {
        public GunForHireDisposition Disposition { get; }
        public int GrantedRuntimeInstanceId { get; }
        public string GrantedContentId { get; }
        public int BuyPrice { get; }
        public int PityPrice { get; }
        public LevelInteractionReceipt InteractionReceipt { get; }
        public bool NewlyApplied => InteractionReceipt.NewlyApplied;
        internal GunForHireReceipt(GunForHireEntryData entry, LevelInteractionReceipt receipt)
        {
            Disposition = (GunForHireDisposition)entry.disposition; GrantedRuntimeInstanceId = entry.grantedId;
            GrantedContentId = entry.grantedContentId; var quote = DungeonRunGunForHireSaveCodec.Quote(entry);
            BuyPrice = quote.BuyPrice; PityPrice = quote.PityPrice; InteractionReceipt = receipt;
        }
    }
}
