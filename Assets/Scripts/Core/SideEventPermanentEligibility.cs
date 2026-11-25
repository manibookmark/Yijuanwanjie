using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace WanjieScroll.Core
{
    /// <summary>Project-adopted cumulative actual crawler reactions; no native attempted-call equivalence.</summary>
    public sealed class GunForHireEligibility
    {
        public const string CanonicalSourceEventId = "event.xiyou.gun-for-hire.v1";
        public string SourceEventId => CanonicalSourceEventId;
        public bool IsUnlocked { get; }
        public string QualifyingRunId { get; }
        public GunForHireEligibility(bool isUnlocked, string qualifyingRunId = null)
        {
            QualifyingRunId = SideEventPermanentProof.ValidateGateRun(isUnlocked, qualifyingRunId);
            IsUnlocked = isUnlocked;
        }
    }

    /// <summary>A permanent event permission, separate from card admission and card unlocks.</summary>
    public sealed class DuplicateOfferingEligibility
    {
        public const string CanonicalSourceEventId = "event.xiyou.duplicate-offering.v1";
        public string SourceEventId => CanonicalSourceEventId;
        public bool IsUnlocked { get; }
        public string QualifyingRunId { get; }
        public DuplicateOfferingEligibility(bool isUnlocked, string qualifyingRunId = null)
        {
            QualifyingRunId = SideEventPermanentProof.ValidateGateRun(isUnlocked, qualifyingRunId);
            IsUnlocked = isUnlocked;
        }
    }

    /// <summary>
    /// Explicit adopted mapping of VC Madforest_01's CompleteFloors(2). A proof
    /// contains the first two successfully exited floors, not a level number,
    /// today's node order or a final-clear boolean. Dungeon's history owner is
    /// the production authority; this immutable value has no mutable statistics.
    /// </summary>
    public sealed class EvoOfferingRunProof
    {
        public const string SourceDungeonAssetId = "Dungeon_Madforest_01";
        public const string ProjectDungeonContentId = "dungeon.xiyou.shuangchaling";
        public const string FirstCompletedFloorContentId = "level.xiyou.shuangchaling.v1";
        public const string SecondCompletedFloorContentId = "floor.xiyou.shuangchaling.f02";
        public string RunId { get; }
        public string DungeonContentId { get; }
        public IReadOnlyList<string> CompletedFloorContentIds { get; }

        public EvoOfferingRunProof(string runId, string dungeonContentId,
            IReadOnlyList<string> completedFloorContentIds)
        {
            RunId = SideEventPermanentProof.ValidateRun(runId);
            if (dungeonContentId != ProjectDungeonContentId || completedFloorContentIds == null ||
                completedFloorContentIds.Count != 2 ||
                completedFloorContentIds[0] != FirstCompletedFloorContentId ||
                completedFloorContentIds[1] != SecondCompletedFloorContentId)
                throw new ArgumentException("Evo permission requires the adopted dungeon's two successful floor transitions.");
            DungeonContentId = dungeonContentId;
            CompletedFloorContentIds = new ReadOnlyCollection<string>(new[]
                { completedFloorContentIds[0], completedFloorContentIds[1] });
        }

        /// <summary>Read-only projection of already committed history. No floor transition is performed here.</summary>
        public static EvoOfferingRunProof FromCompletedHistory(string runId, string dungeonContentId,
            IReadOnlyList<string> completedFloorContentIds)
        {
            if (completedFloorContentIds == null) throw new ArgumentNullException(nameof(completedFloorContentIds));
            if (dungeonContentId != ProjectDungeonContentId || completedFloorContentIds.Count < 2) return null;
            if (completedFloorContentIds[0] != FirstCompletedFloorContentId ||
                completedFloorContentIds[1] != SecondCompletedFloorContentId) return null;
            return new EvoOfferingRunProof(runId, dungeonContentId, new[]
                { FirstCompletedFloorContentId, SecondCompletedFloorContentId });
        }
    }

    public sealed class EvoOfferingEligibility
    {
        public const string CanonicalSourceEventId = "event.xiyou.evo-offering.v1";
        public string SourceEventId => CanonicalSourceEventId;
        public bool IsUnlocked { get; }
        public EvoOfferingRunProof Proof { get; }
        public EvoOfferingEligibility(bool isUnlocked, EvoOfferingRunProof proof = null)
        {
            if (isUnlocked != (proof != null))
                throw new ArgumentException("Evo permission and its exact qualifying Run proof must agree.");
            IsUnlocked = isUnlocked;
            Proof = proof;
        }
    }

    internal static class SideEventPermanentProof
    {
        internal static string ValidateGateRun(bool unlocked, string runId)
        {
            if (!unlocked)
            {
                if (!string.IsNullOrEmpty(runId)) throw new ArgumentException("A locked permission cannot carry a Run proof.");
                return string.Empty;
            }
            return ValidateRun(runId);
        }

        internal static string ValidateRun(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A qualifying Run needs a stable ID.");
            foreach (char value in runId)
                if (char.IsWhiteSpace(value) || char.IsControl(value))
                    throw new ArgumentException("A qualifying Run ID cannot contain whitespace or controls.");
            try { _ = new UTF8Encoding(false, true).GetByteCount(runId); }
            catch (EncoderFallbackException exception) { throw new ArgumentException("A qualifying Run ID must be valid Unicode.", exception); }
            return runId;
        }
    }
}
