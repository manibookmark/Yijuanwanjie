using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    [Serializable]
    public sealed class LevelInteractionInvitationContract
    {
        public string policyId;
        public bool IsSpecified => !string.IsNullOrEmpty(policyId);
    }

    /// <summary>A project summon author and the explicit permanent-unlock decision. No native FCC mapping or price is implied.</summary>
    public sealed class InvitationCandidate
    {
        public string CrawlerContentId { get; }
        public CardAuthorDocument Author { get; }
        public bool PermanentlyUnlocked { get; }
        public InvitationCandidate(string crawlerContentId, CardConfig summonAuthor, bool permanentlyUnlocked,
            IReadOnlyList<CardConfig> authorCatalog)
            : this(crawlerContentId, CardAuthorDocument.Capture(summonAuthor, DungeonRunCardCodec.CreateResolver(authorCatalog)), permanentlyUnlocked) { }
        internal InvitationCandidate(string crawlerContentId, CardAuthorDocument author, bool permanentlyUnlocked)
        {
            DungeonRunInvitationSaveCodec.RequireId(crawlerContentId);
            Author = author ?? throw new ArgumentNullException(nameof(author));
            if (author.ReadSummonedCrawlerContentIds().Count != 1 || author.ReadSummonedCrawlerContentIds()[0] != crawlerContentId)
                throw new ArgumentException("Invitation author must summon exactly its identified crawler.");
            CrawlerContentId = crawlerContentId; PermanentlyUnlocked = permanentlyUnlocked;
        }
    }
    public sealed class InvitationPreparedContext
    {
        public IReadOnlyList<InvitationCandidate> Candidates { get; }
        public bool EventAdmitted { get; }
        public SideEventSourceWireDocument LegacySourceDocument { get; }
        public InvitationPreparedContext(IReadOnlyList<InvitationCandidate> candidates, bool eventAdmitted,
            SideEventSourceWireDocument legacySourceDocument = null)
        {
            if (candidates == null || candidates.Any(c => c == null) ||
                candidates.Select(c => c.CrawlerContentId).Distinct().Count() != candidates.Count ||
                candidates.Select(c => c.Author.ContentId).Distinct().Count() != candidates.Count)
                throw new ArgumentException("An explicit unique invitation catalog is required.");
            Candidates = new ReadOnlyCollection<InvitationCandidate>(candidates.ToArray()); EventAdmitted = eventAdmitted;
            LegacySourceDocument = legacySourceDocument;
        }
    }
    public sealed class InvitationOffer
    {
        public string NodeContentId { get; }
        public IReadOnlyList<InvitationCandidate> Candidates { get; }
        public bool Active { get; }
        public bool AlreadyCompleted { get; }
        public InvitationReceipt Receipt { get; }
        internal InvitationOffer(InvitationEntryData entry, InvitationReceipt receipt = null)
        {
            NodeContentId = entry.nodeId; Active = !entry.finished; AlreadyCompleted = entry.finished;
            Receipt = receipt;
            Candidates = new ReadOnlyCollection<InvitationCandidate>(entry.candidates.Select(DungeonRunInvitationSaveCodec.RestoreCandidate).ToArray());
        }
    }
    public sealed class InvitationReceipt
    {
        public int GrantedRuntimeInstanceId { get; }
        public string GrantedContentId { get; }
        public string CrawlerContentId { get; }
        public LevelInteractionReceipt InteractionReceipt { get; }
        public bool NewlyApplied => InteractionReceipt.NewlyApplied;
        internal InvitationReceipt(InvitationEntryData entry, LevelInteractionReceipt receipt)
        {
            GrantedRuntimeInstanceId = entry.grantedId; GrantedContentId = entry.grantedContentId;
            CrawlerContentId = entry.candidates[entry.selectedIndex].crawlerId; InteractionReceipt = receipt;
        }
    }

    /// <summary>Owns detached frozen-author preview allocation and cleanup; it never creates a persistent Run card.</summary>
    public static class InvitationCardProjection
    {
        public static List<CardConfig> InstantiateCandidates(InvitationOffer offer, IReadOnlyList<CardConfig> authorCatalog)
        {
            if (offer == null) throw new ArgumentNullException(nameof(offer));
            var result = new List<CardConfig>(); var resolver = DungeonRunCardCodec.CreateResolver(authorCatalog);
            try { foreach (var candidate in offer.Candidates) result.Add(candidate.Author.Instantiate(resolver)); return result; }
            catch { Release(result); throw; }
        }
        public static void Release(IReadOnlyList<CardConfig> cards)
        { foreach (var card in cards ?? Array.Empty<CardConfig>()) DungeonRunCardCodec.Release(card); }
    }
}
