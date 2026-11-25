using System;
using System.Collections.Generic;

namespace WanjieScroll.Dungeon
{
    /// <summary>Only rules already supported for acquisition during a Run.</summary>
    public enum DungeonRunArcanaRule { ShieldBash, ExperimentalMedicine }

    public sealed class DungeonRunArcanaDefinition
    {
        public string ContentId { get; }
        public DungeonRunArcanaRule Rule { get; }

        public DungeonRunArcanaDefinition(string contentId, DungeonRunArcanaRule rule)
        {
            if (!DungeonRunArcanas.IsStableId(contentId) || !Enum.IsDefined(typeof(DungeonRunArcanaRule), rule))
                throw new ArgumentException("Arcana definition requires a stable ID and a supported rule.");
            ContentId = contentId;
            Rule = rule;
        }
    }

    /// <summary>Resolved content/admission input; owns no mutable Run or account state.</summary>
    public sealed class DungeonRunArcanaCatalog
    {
        public string PoolContentId { get; }
        public IReadOnlyList<DungeonRunArcanaDefinition> EligibleCandidates { get; }
        public bool AcquisitionUnlocked { get; }
        public string EquippedContentId { get; }

        public DungeonRunArcanaCatalog(string poolContentId,
            IReadOnlyList<DungeonRunArcanaDefinition> eligibleCandidates,
            bool acquisitionUnlocked, string equippedContentId = null)
        {
            if (!DungeonRunArcanas.IsStableId(poolContentId))
                throw new ArgumentException("Arcana pool requires a stable ID.", nameof(poolContentId));
            if (!string.IsNullOrEmpty(equippedContentId) && !DungeonRunArcanas.IsStableId(equippedContentId))
                throw new ArgumentException("Equipped Arcana identity is invalid.", nameof(equippedContentId));
            var copy = new List<DungeonRunArcanaDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in eligibleCandidates ?? Array.Empty<DungeonRunArcanaDefinition>())
            {
                if (candidate == null || !ids.Add(candidate.ContentId))
                    throw new ArgumentException("Arcana pool contains a missing or duplicate definition.");
                copy.Add(new DungeonRunArcanaDefinition(candidate.ContentId, candidate.Rule));
            }
            PoolContentId = poolContentId;
            EligibleCandidates = copy.AsReadOnly();
            AcquisitionUnlocked = acquisitionUnlocked;
            EquippedContentId = equippedContentId ?? string.Empty;
        }
    }

    public sealed class DungeonRunArcanaOffer
    {
        public bool Pending { get; }
        public bool CanSkip => Pending;
        public IReadOnlyList<DungeonRunArcanaDefinition> Candidates { get; }
        public bool HasReceipt { get; }
        public LevelInteractionReceipt Receipt { get; }
        public bool Skipped { get; }

        internal DungeonRunArcanaOffer(bool pending,
            IReadOnlyList<DungeonRunArcanaDefinition> candidates,
            bool hasReceipt = false, LevelInteractionReceipt receipt = default, bool skipped = false)
        {
            Pending = pending;
            Candidates = new List<DungeonRunArcanaDefinition>(candidates ??
                Array.Empty<DungeonRunArcanaDefinition>()).AsReadOnly();
            HasReceipt = hasReceipt;
            Receipt = receipt;
            Skipped = skipped;
        }
    }

    // Wire data only. DungeonRunArcanas copies and validates it at every owner boundary.
    [Serializable]
    internal sealed class DungeonRunArcanaSaveData
    {
        public int version;
        public List<DungeonRunArcanaActiveSaveData> active;
        public bool hasPendingChoice;
        public DungeonRunArcanaPendingSaveData pending;
        public List<DungeonRunArcanaCompletedSaveData> completed;
    }

    [Serializable]
    internal sealed class DungeonRunArcanaCandidateSaveData
    {
        public string contentId;
        public DungeonRunArcanaRule rule;
    }

    [Serializable]
    internal sealed class DungeonRunArcanaActiveSaveData
    {
        public string contentId;
        public DungeonRunArcanaRule rule;
        public string floorContentId;
        public string nodeContentId;
        public string effectId;
    }

    [Serializable]
    internal sealed class DungeonRunArcanaPendingSaveData
    {
        public string floorContentId;
        public string nodeContentId;
        public string effectId;
        public string poolContentId;
        public int candidateCount;
        public string equippedContentId;
        public List<DungeonRunArcanaCandidateSaveData> candidates;
    }

    [Serializable]
    internal sealed class DungeonRunArcanaCompletedSaveData
    {
        public string floorContentId;
        public string nodeContentId;
        public string effectId;
        public string poolContentId;
        public int candidateCount;
        public string equippedContentId;
        public List<DungeonRunArcanaCandidateSaveData> candidates;
        public bool skipped;
        public string selectedContentId;
        public int valueBefore;
        public int valueAfter;
    }
}
