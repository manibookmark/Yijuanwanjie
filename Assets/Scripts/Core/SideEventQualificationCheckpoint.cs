using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WanjieScroll.Core
{
    [Serializable]
    public sealed class SideEventRequirementDecisionData
    {
        public string rootContentId;
        public int eligibility;
    }

    /// <summary>Detached capture DTO. A Run JSON boundary must validate explicit
    /// field presence and types before Restore; DTO defaults cannot prove wire presence.</summary>
    [Serializable]
    public sealed class SideEventQualificationCheckpointData
    {
        public int version;
        public int sourceDocumentVersion;
        public int snapshotVersion;
        public int qualificationAlgorithmVersion;
        public string sourceDocumentJson;
        public string sourceFingerprint;
        public string layerContentId;
        public List<SideEventRequirementDecisionData> decisions;
        public bool hasUnequippedArcanaCount;
        public int unequippedArcanaCount;
        public string inputFingerprint;
        public bool qualificationComplete;
        public string qualifiedFingerprint;
        public string fingerprint;
    }

    /// <summary>
    /// Owns complete source and captured qualification evidence, never live account
    /// gates. Recovery validates saved inputs against the saved source document;
    /// it does not ask today's account or catalog to qualify them again. Explicit
    /// Unknown is a valid checkpoint but cannot begin generation. Fingerprints
    /// prove internal consistency, not authenticity of a caller's account claims.
    /// The mutable capture DTO is detached; this object only retains immutable data.
    /// </summary>
    public sealed class SideEventQualificationCheckpoint
    {
        public const int CurrentVersion = 1;
        public SideEventSourceWireDocument Document { get; }
        public SideEventQualificationSnapshot Snapshot { get; }
        public SideEventQualificationResult Result { get; }
        public bool IsComplete => Result.IsComplete;
        public string Fingerprint { get; }

        private SideEventQualificationCheckpoint(SideEventSourceWireDocument document,
            SideEventQualificationSnapshot snapshot)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Result = SideEventSourceQualifier.Qualify(Document.Definition, Snapshot);
            Fingerprint = Identity(writer =>
            {
                writer.Write("side-event-qualification-checkpoint/v1");
                writer.Write(CurrentVersion); writer.Write(Document.FormatVersion);
                writer.Write(SideEventQualificationSnapshot.Version);
                writer.Write(SideEventSourceQualifier.AlgorithmVersion);
                writer.Write(Document.CanonicalJson); writer.Write(Snapshot.Fingerprint);
                writer.Write(IsComplete);
                if (IsComplete) writer.Write(Result.Qualified.Fingerprint);
            });
        }

        public static SideEventQualificationCheckpoint Create(SideEventSourceWireDocument document,
            SideEventQualificationSnapshot snapshot) => new SideEventQualificationCheckpoint(document, snapshot);

        public SideEventQualificationCheckpointData Capture()
        {
            var decisions = new List<SideEventRequirementDecisionData>();
            foreach (var decision in Snapshot.Decisions)
                decisions.Add(new SideEventRequirementDecisionData
                { rootContentId = decision.RootContentId, eligibility = (int)decision.Eligibility });
            return new SideEventQualificationCheckpointData
            {
                version = CurrentVersion, sourceDocumentVersion = Document.FormatVersion,
                snapshotVersion = SideEventQualificationSnapshot.Version,
                qualificationAlgorithmVersion = SideEventSourceQualifier.AlgorithmVersion,
                sourceDocumentJson = Document.CanonicalJson, sourceFingerprint = Document.Fingerprint,
                layerContentId = Snapshot.LayerContentId, decisions = decisions,
                hasUnequippedArcanaCount = Snapshot.UnequippedArcanaCount.HasValue,
                unequippedArcanaCount = Snapshot.UnequippedArcanaCount ?? 0,
                inputFingerprint = Snapshot.Fingerprint, qualificationComplete = IsComplete,
                qualifiedFingerprint = IsComplete ? Result.Qualified.Fingerprint : null,
                fingerprint = Fingerprint
            };
        }

        /// <summary>Pure recovery: no catalog, account, randomness or placement.
        /// Rebuilding the proof uses saved decisions, not requirement callbacks.</summary>
        public static SideEventQualificationCheckpoint Restore(SideEventQualificationCheckpointData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.version != CurrentVersion || data.sourceDocumentVersion != SideEventSourceWireDocument.CurrentFormatVersion ||
                data.snapshotVersion != SideEventQualificationSnapshot.Version ||
                data.qualificationAlgorithmVersion != SideEventSourceQualifier.AlgorithmVersion)
                throw new ArgumentException("Unsupported source qualification checkpoint version.", nameof(data));
            if (data.decisions == null || (!data.hasUnequippedArcanaCount && data.unequippedArcanaCount != 0))
                throw new ArgumentException("Complete decisions and canonical optional Arcana input are required.", nameof(data));
            var document = SideEventSourceWireDocument.Parse(data.sourceDocumentJson);
            if (!Same(document.CanonicalJson, data.sourceDocumentJson) || !Same(document.Fingerprint, data.sourceFingerprint))
                throw new ArgumentException("Captured source document or fingerprint changed.", nameof(data));
            var decisions = new List<SideEventRequirementDecision>();
            foreach (var decision in data.decisions)
            {
                if (decision == null) throw new ArgumentException("A requirement decision cannot be null.", nameof(data));
                decisions.Add(new SideEventRequirementDecision(decision.rootContentId,
                    (SideEventRequirementEligibility)decision.eligibility));
            }
            var snapshot = new SideEventQualificationSnapshot(document.Definition, data.layerContentId, decisions,
                data.hasUnequippedArcanaCount ? data.unequippedArcanaCount : (int?)null);
            // Capture always emits source-root order; accepting a reordered proof
            // would hide corruption behind the snapshot constructor's normalization.
            for (int i = 0; i < decisions.Count; i++)
                if (!Same(decisions[i].RootContentId, snapshot.Decisions[i].RootContentId))
                    throw new ArgumentException("Captured decision order changed.", nameof(data));
            var restored = Create(document, snapshot);
            if (!Same(snapshot.Fingerprint, data.inputFingerprint) || restored.IsComplete != data.qualificationComplete ||
                !Same(restored.IsComplete ? restored.Result.Qualified.Fingerprint : null, data.qualifiedFingerprint) ||
                !Same(restored.Fingerprint, data.fingerprint))
                throw new ArgumentException("Captured qualification proof is inconsistent.", nameof(data));
            return restored;
        }

        internal static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);

        // Framed scalars only; no reflection or inverse serialization of Core graphs.
        internal static string Identity(Action<BinaryWriter> write)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, new UTF8Encoding(false, true), true)) write(writer);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
        }
    }
}
