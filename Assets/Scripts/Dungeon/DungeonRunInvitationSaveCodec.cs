using System;
using System.Collections.Generic;
using System.Linq;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Closed project invitation journal. Frozen authors and complete before/after physical rosters prove a free single-card birth.</summary>
    internal static class DungeonRunInvitationSaveCodec
    {
        private static ExactJsonWire Wire => new ExactJsonWire(typeof(InvitationSaveData), typeof(InvitationEntryData),
            typeof(InvitationCandidateData), typeof(InvitationStringData));
        internal static InvitationSaveData Empty(string runId) => new InvitationSaveData { version = 1, runId = runId, entries = new List<InvitationEntryData>() };
        internal static InvitationSaveData ReadRun(DungeonRunSaveData data, string payload)
        {
            var names = DungeonRunCrystalEvents.ReadFieldNames(payload);
            string[] keys = { "hasInvitationState", "invitationStateJson" };
            foreach (string name in names)
                foreach (string key in keys)
                    if (name != key && string.Equals(name, key, StringComparison.OrdinalIgnoreCase)) throw Bad("Run field alias.");
            if (data.schemaVersion < 40)
            {
                if (keys.Any(names.Contains) || data.hasInvitationState || !string.IsNullOrEmpty(data.invitationStateJson)) throw Bad("Legacy Run claims future invitation fields.");
                return Empty(data.runId);
            }
            if (!keys.All(names.Contains) || !data.hasInvitationState ||
                !JsonObjectShape.TryGetField(payload, keys[0], out var flag) || flag != "true" ||
                !JsonObjectShape.TryGetField(payload, keys[1], out var body)) throw Bad("Missing modern journal.");
            string json = Wire.Read<InvitationStringData>("{\"value\":" + body + "}").value;
            if (json != data.invitationStateJson) throw Bad("Run journal carrier differs.");
            var state = Read(json); if (state.runId != data.runId) throw Bad("Journal belongs to another Run."); return state;
        }
        internal static InvitationSaveData Read(string json)
        {
            var result = Wire.Read<InvitationSaveData>(json);
            if (Wire.Write(result) != json) throw Bad("Noncanonical wire."); Validate(result); return result;
        }
        internal static string Write(InvitationSaveData state) { Validate(state); return Wire.Write(state); }
        internal static InvitationSaveData Clone(InvitationSaveData state) => Read(Write(state));
        internal static string Hash(string value) => DungeonRunGunForHireSaveCodec.Hash(value);
        internal static void RequireId(string value) => DungeonRunGunForHireSaveCodec.RequireId(value);
        internal static InvitationCandidateData CaptureCandidate(InvitationCandidate candidate) => new InvitationCandidateData {
            crawlerId = candidate.CrawlerContentId, authorJson = candidate.Author.CanonicalJson, unlocked = candidate.PermanentlyUnlocked };
        internal static InvitationCandidate RestoreCandidate(InvitationCandidateData data)
        {
            if (data == null) throw Bad("Null candidate.");
            return new InvitationCandidate(data.crawlerId, CardAuthorDocument.Parse(data.authorJson), data.unlocked);
        }
        internal static List<InvitationStringData> CaptureStrings(IEnumerable<string> values) => values.Select(value => new InvitationStringData { value = value }).ToList();
        internal static IEnumerable<string> ReadStrings(IEnumerable<InvitationStringData> values)
        {
            foreach (var value in values)
            { if (value == null || value.value == null) throw Bad("Explicit string proof is required."); yield return value.value; }
        }
        internal static bool SameStrings(IEnumerable<InvitationStringData> left, IEnumerable<InvitationStringData> right)
            => ReadStrings(left).SequenceEqual(ReadStrings(right));
        internal static List<InvitationStringData> CaptureRoster(IReadOnlyList<DungeonRunCardSaveData> cards) => CaptureStrings(cards.Select(DungeonRunCardCodec.CanonicalJson));
        internal static void Validate(InvitationSaveData state)
        {
            if (state == null || state.version != 1 || state.entries == null) throw Bad("Unsupported journal.");
            RequireId(state.runId); var nodes = new HashSet<string>(); var effects = new HashSet<string>(); var births = new HashSet<int>(); int pending = 0;
            foreach (var e in state.entries)
            {
                if (e == null) throw Bad("Null entry."); RequireId(e.floorId); RequireId(e.nodeId); RequireId(e.effectId);
                DungeonRunGunForHireSaveCodec.RequireHash(e.effectFingerprint);
                if (!nodes.Add(e.floorId + "\n" + e.nodeId) || !effects.Add(e.floorId + "\n" + e.effectId) ||
                    !e.eventAdmitted || e.candidates == null || e.candidates.Count < 1 || e.candidates.Count > DungeonRunInvitations.MaximumChoices ||
                    e.openingCards == null || e.afterCards == null || e.ownedBefore == null || e.jadeBefore < 0 || e.forcedBefore < 0)
                    throw Bad("Invalid entry envelope.");
                if (e.legacyAdoption)
                {
                    RequireId(e.legacyRootContentId);
                    GunForHirePreparedContext.RequireSpecification(SideEventSourceWireDocument.Parse(e.legacySourceDocumentJson), e.legacyRootContentId);
                }
                else if (e.legacySourceDocumentJson != string.Empty || e.legacyRootContentId != string.Empty)
                    throw Bad("A modern invitation cannot claim legacy source adoption.");
                var ownedBefore = ReadStrings(e.ownedBefore).ToArray();
                if (ownedBefore.Length >= DungeonRunInvitations.MaximumRunCrawlers || ownedBefore.Distinct().Count() != ownedBefore.Length) throw Bad("Invalid opening crawler ownership.");
                foreach (string id in ownedBefore) RequireId(id);
                var candidates = e.candidates.Select(RestoreCandidate).ToArray();
                if (candidates.Any(c => !c.PermanentlyUnlocked || ownedBefore.Contains(c.CrawlerContentId)) ||
                    candidates.Select(c => c.CrawlerContentId).Distinct().Count() != candidates.Length ||
                    candidates.Select(c => c.Author.ContentId).Distinct().Count() != candidates.Length) throw Bad("Unqualified invitation candidate.");
                var before = ReadStrings(e.openingCards).Select(DungeonRunCardCodec.Parse).ToArray();
                if (before.Select(c => c.runtimeInstanceId).Distinct().Count() != before.Length ||
                    e.forcedBefore > 0 && !before.Any(c => c.runtimeInstanceId == e.forcedBefore)) throw Bad("Invalid opening roster.");
                foreach (var card in before)
                    if (DungeonRunCardCodec.IsKnown(card))
                        foreach (string id in CardAuthorDocument.Parse(card.currentAuthorJson).ReadSummonedCrawlerContentIds())
                            if (!ownedBefore.Contains(id)) throw Bad("Opening ownership omits a summon card.");
                if (candidates.Any(c => before.Any(card => card.contentId == c.Author.ContentId))) throw Bad("Invitation already owned in opening roster.");
                if (!e.finished)
                {
                    if (++pending > 1 || e.selectedIndex != -1 || e.grantedId != 0 || e.grantedContentId != null || e.afterCards.Count != 0 || e.openingConsumed)
                        throw Bad("Unfinished invitation claims a grant.");
                    continue;
                }
                if (e.selectedIndex < 0 || e.selectedIndex >= candidates.Length || e.grantedId <= 0 || !births.Add(e.grantedId) ||
                    e.grantedContentId != candidates[e.selectedIndex].Author.ContentId || before.Any(c => c.runtimeInstanceId == e.grantedId) ||
                    e.afterCards.Count != e.openingCards.Count + 1) throw Bad("Invalid granted identity.");
                var after = ReadStrings(e.afterCards).Select(DungeonRunCardCodec.Parse).ToArray();
                if (after.Select(c => c.runtimeInstanceId).Distinct().Count() != after.Length) throw Bad("Duplicate after identity.");
                var birth = after.SingleOrDefault(c => c.runtimeInstanceId == e.grantedId); var author = candidates[e.selectedIndex].Author;
                if (birth == null || !DungeonRunCardCodec.IsKnown(birth) || birth.contentId != author.ContentId || birth.baseAuthorJson != author.CanonicalJson ||
                    birth.currentAuthorJson != author.CanonicalJson || birth.limitBreakCount != 0 || birth.seals.Count != 0 || birth.gemSlotCapacity != author.ReadGemSlotCapacity() ||
                    !SameStrings(e.openingCards, e.afterCards.Where(c => DungeonRunCardCodec.Parse(c.value).runtimeInstanceId != e.grantedId)))
                    throw Bad("Invitation did not grant exactly one frozen birth.");
            }
        }
        internal static InvalidOperationException Bad(string text) => new InvalidOperationException("Crawler invitation: " + text);
    }
    internal sealed class InvitationSaveData { public int version; public string runId; public List<InvitationEntryData> entries; }
    internal sealed class InvitationEntryData
    {
        public string floorId, nodeId, effectId, effectFingerprint;
        public bool eventAdmitted, finished, openingConsumed;
        public bool legacyAdoption;
        public string legacySourceDocumentJson, legacyRootContentId;
        public List<InvitationCandidateData> candidates;
        public List<InvitationStringData> openingCards, afterCards, ownedBefore;
        public int jadeBefore, forcedBefore, selectedIndex, grantedId;
        public string grantedContentId;
    }
    internal sealed class InvitationCandidateData { public string crawlerId, authorJson; public bool unlocked; }
    internal sealed class InvitationStringData { public string value; }
}
