using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Owns the closed duplicate journal and its detached copy mathematics.
    /// Physical authors remain DungeonRunCardCodec's grammar. Parsing never allocates
    /// native cards, resolves today's account/content or writes caller-owned Run state.
    /// Canonical hashes prove consistency, not historical authorization.</summary>
    internal static class DungeonRunDuplicateOfferingSaveCodec
    {
        internal const int CurrentVersion = 1;
        private static ExactJsonWire Wire => new ExactJsonWire(typeof(DungeonRunDuplicateOfferingSaveData),
            typeof(DuplicateOfferingEntryData), typeof(DuplicateOfferingCardData), typeof(DuplicateOfferingStringData));
        internal static DungeonRunDuplicateOfferingSaveData CreateEmpty(string runId) => new DungeonRunDuplicateOfferingSaveData {
            version = CurrentVersion, runId = runId ?? string.Empty, entries = new List<DuplicateOfferingEntryData>() };
        internal static DungeonRunDuplicateOfferingSaveData ReadRun(DungeonRunSaveData data, string payload)
        {
            if (data == null || string.IsNullOrEmpty(payload)) throw Bad("Missing Run wire.");
            var names = DungeonRunCrystalEvents.ReadFieldNames(payload);
            string[] fields = { "hasDuplicateOfferingState", "duplicateOfferingStateJson" };
            foreach (string name in names)
                foreach (string field in fields)
                    if (string.Equals(name, field, StringComparison.OrdinalIgnoreCase) && name != field) throw Bad("Duplicate Run field alias.");
            if (data.schemaVersion <= 38)
            {
                if (fields.Any(names.Contains) || data.hasDuplicateOfferingState || !string.IsNullOrEmpty(data.duplicateOfferingStateJson))
                    throw Bad("Legacy Run cannot carry duplicate journal fields.");
                return CreateEmpty(data.runId);
            }
            if (!fields.All(names.Contains) || !data.hasDuplicateOfferingState ||
                !JsonObjectShape.TryGetField(payload, fields[0], out string flag) || flag != "true" ||
                !JsonObjectShape.TryGetField(payload, fields[1], out string raw)) throw Bad("Modern Run requires explicit duplicate state.");
            string json = Wire.Read<DuplicateOfferingStringData>("{\"value\":" + raw + "}").value;
            if (string.IsNullOrEmpty(json) || json != data.duplicateOfferingStateJson) throw Bad("Duplicate wire string differs from the Run DTO.");
            var state = Read(json);
            if (state.runId != data.runId) throw Bad("Duplicate journal belongs to another saved Run.");
            return state;
        }
        internal static DungeonRunDuplicateOfferingSaveData Read(string canonicalJson)
        {
            try
            {
                var state = Wire.Read<DungeonRunDuplicateOfferingSaveData>(canonicalJson); ValidateLocal(state);
                if (Wire.Write(state) != canonicalJson) throw Bad("Duplicate journal must be canonical.");
                return state;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException || exception is OverflowException)
            { throw Bad("Invalid duplicate journal wire.", exception); }
        }
        internal static string Write(DungeonRunDuplicateOfferingSaveData state) { ValidateLocal(state); return Wire.Write(state); }
        internal static DungeonRunDuplicateOfferingSaveData Clone(DungeonRunDuplicateOfferingSaveData state) => Read(Write(state));
        internal static DuplicateOfferingCardData CaptureCard(DungeonRunCardSaveData card)
            => new DuplicateOfferingCardData { canonicalJson = DungeonRunCardCodec.CanonicalJson(card) };
        internal static List<DuplicateOfferingCardData> CaptureCards(IReadOnlyList<DungeonRunCardSaveData> cards)
            => cards.Select(CaptureCard).ToList();
        internal static List<DungeonRunCardSaveData> RestoreCards(IReadOnlyList<DuplicateOfferingCardData> cards)
            => cards.Select(value => DungeonRunCardCodec.Parse(value.canonicalJson)).ToList();
        internal static bool RosterEquals(IReadOnlyList<DuplicateOfferingCardData> left, IReadOnlyList<DuplicateOfferingCardData> right)
            => left.Count == right.Count && left.Select(value => value.canonicalJson).SequenceEqual(right.Select(value => value.canonicalJson));

        internal static void ValidateLocal(DungeonRunDuplicateOfferingSaveData state)
        {
            if (state == null || state.version != CurrentVersion || state.runId == null || state.entries == null) throw Bad("Missing duplicate journal envelope.");
            if (state.entries.Count > 0) RequireId(state.runId);
            var keys = new HashSet<string>(StringComparer.Ordinal); var effects = new HashSet<string>(StringComparer.Ordinal);
            var created = new HashSet<int>(); int active = 0;
            foreach (var entry in state.entries)
            {
                if (entry == null) throw Bad("Null duplicate entry.");
                RequireId(entry.floorId); RequireId(entry.nodeId); RequireId(entry.effectId); RequireHash(entry.effectFingerprint);
                if (!keys.Add(entry.floorId + "\n" + entry.nodeId) || !effects.Add(entry.floorId + "\n" + entry.effectId)) throw Bad("Duplicate node/effect journal key.");
                if (entry.active && (++active > 1 || entry.finished)) throw Bad("Only one unfinished duplicate offer may be active.");
                var document = SideEventSourceWireDocument.Parse(entry.sourceDocumentJson);
                if (document.CanonicalJson != entry.sourceDocumentJson || document.Fingerprint != entry.sourceFingerprint) throw Bad("Complete source document fingerprint differs.");
                DuplicateOfferingPreparedContext.RequireSpecification(document, entry.sourceRootId);
                var policy = DuplicateOfferingPreparedContext.ReadPolicy(entry.poolPolicyId, entry.poolPolicyJson);
                if (Hash(entry.poolPolicyJson) != entry.poolPolicyFingerprint) throw Bad("Explicit duplicate policy fingerprint differs.");
                var eligibility = new DuplicateOfferingEligibility(entry.eligibilityUnlocked, entry.qualifyingRunId);
                if (!eligibility.IsUnlocked) throw Bad("A frozen duplicate permission must be unlocked.");
                ValidateRoster(entry.openingRoster); ValidateRoster(entry.checkpointRoster); ValidateRoster(entry.rosterAfter);
                if (entry.candidates == null || entry.jadeBefore < 0 || entry.jadeAfter < 0 || entry.forcedOpeningBefore < 0 || entry.forcedOpeningAfter < 0)
                    throw Bad("Missing candidate roster or invalid checkpoint counters.");
                if (entry.forcedOpeningBefore != 0 && !ContainsId(entry.checkpointRoster, entry.forcedOpeningBefore)) throw Bad("Frozen opening-hand target is outside the checkpoint roster.");
                var candidates = new HashSet<int>();
                foreach (var candidate in entry.candidates)
                {
                    var card = ParseCard(candidate); DuplicateOfferingPreparedContext.RequireCandidate(card);
                    if (!candidates.Add(card.runtimeInstanceId) || !entry.openingRoster.Any(value => value.canonicalJson == candidate.canonicalJson))
                        throw Bad("Candidate is not an exact unique original roster member.");
                }
                if (!entry.finished)
                {
                    if (entry.selectedRuntimeInstanceId != 0 || entry.createdRuntimeInstanceId != 0 || entry.createdCardJson != null ||
                        entry.rosterAfter.Count != 0 || entry.jadeAfter != 0 || entry.forcedOpeningAfter != 0)
                        throw Bad("Unfinished duplicate cannot contain a grant.");
                    continue;
                }
                if (entry.selectedRuntimeInstanceId <= 0 || entry.createdRuntimeInstanceId <= 0 || !created.Add(entry.createdRuntimeInstanceId) ||
                    entry.jadeAfter != entry.jadeBefore || entry.forcedOpeningAfter != entry.forcedOpeningBefore)
                    throw Bad("Duplicate completion changed income/opening hand or reused a created identity.");
                var selected = entry.candidates.SingleOrDefault(value => ParseCard(value).runtimeInstanceId == entry.selectedRuntimeInstanceId);
                if (selected == null || !entry.checkpointRoster.Any(value => value.canonicalJson == selected.canonicalJson) ||
                    ContainsId(entry.openingRoster, entry.createdRuntimeInstanceId) || ContainsId(entry.checkpointRoster, entry.createdRuntimeInstanceId))
                    throw Bad("Copy source or fresh created identity differs from the checkpoint.");
                var copied = DungeonRunCardCodec.Parse(entry.createdCardJson);
                if (copied.runtimeInstanceId != entry.createdRuntimeInstanceId) throw Bad("Created card identity differs.");
                ValidateCopy(ParseCard(selected), copied, policy);
                if (entry.rosterAfter.Count != entry.checkpointRoster.Count + 1 ||
                    !entry.checkpointRoster.Select(value => value.canonicalJson).SequenceEqual(entry.rosterAfter.Take(entry.checkpointRoster.Count).Select(value => value.canonicalJson)) ||
                    entry.rosterAfter[entry.rosterAfter.Count - 1].canonicalJson != entry.createdCardJson)
                    throw Bad("Duplicate must retain the exact ordered roster and append exactly its one created card.");
            }
        }
        /// <summary>The factory owns construction; this verifies its durable outcome:
        /// only fresh instance ID and explicit breakthrough policy may differ. Full
        /// independent author documents, slots and ordered seals must be identical.</summary>
        internal static void ValidateCopy(DungeonRunCardSaveData source, DungeonRunCardSaveData copied, PersistentDuplicateLimitBreakPolicy policy)
        {
            DuplicateOfferingPreparedContext.RequireCandidate(source); DungeonRunCardCodec.Validate(copied);
            if (!Enum.IsDefined(typeof(PersistentDuplicateLimitBreakPolicy), policy) || copied.runtimeInstanceId <= 0 || copied.runtimeInstanceId == source.runtimeInstanceId)
                throw Bad("Invalid copy policy or fresh instance identity.");
            var expected = DungeonRunCardCodec.Clone(source); expected.runtimeInstanceId = copied.runtimeInstanceId;
            expected.limitBreakCount = policy == PersistentDuplicateLimitBreakPolicy.Preserve ? source.limitBreakCount : 0;
            if (DungeonRunCardCodec.CanonicalJson(expected) != DungeonRunCardCodec.CanonicalJson(copied))
                throw Bad("Copied physical state differs from its frozen original/current construction proof.");
        }
        internal static DungeonRunCardSaveData ParseCard(DuplicateOfferingCardData card)
        { if (card == null || string.IsNullOrEmpty(card.canonicalJson)) throw Bad("Missing full physical card wire."); return DungeonRunCardCodec.Parse(card.canonicalJson); }
        internal static bool ContainsId(IReadOnlyList<DuplicateOfferingCardData> cards, int id) => cards.Any(value => ParseCard(value).runtimeInstanceId == id);
        private static void ValidateRoster(List<DuplicateOfferingCardData> roster)
        {
            if (roster == null) throw Bad("Missing explicit physical roster.");
            var ids = new HashSet<int>(); foreach (var value in roster) if (!ids.Add(ParseCard(value).runtimeInstanceId)) throw Bad("Repeated physical roster ID.");
        }
        internal static string Hash(string value)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(new UTF8Encoding(false, true).GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        internal static void RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw Bad("A stable ID is required.");
            new UTF8Encoding(false, true).GetByteCount(id);
            foreach (char ch in id) if (char.IsWhiteSpace(ch) || char.IsControl(ch)) throw Bad("Stable IDs cannot contain whitespace/control characters.");
        }
        internal static void RequireHash(string value)
        { if (value == null || value.Length != 64 || value.Any(ch => !(ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'f'))) throw Bad("A lowercase SHA-256 is required."); }
        private static InvalidOperationException Bad(string message, Exception inner = null) => new InvalidOperationException(message, inner);
    }
    internal sealed class DungeonRunDuplicateOfferingSaveData
    { public int version; public string runId; public List<DuplicateOfferingEntryData> entries; }
    internal sealed class DuplicateOfferingEntryData
    {
        public string floorId, nodeId, effectId, effectFingerprint;
        public bool active, finished;
        public string sourceDocumentJson, sourceFingerprint, sourceRootId, poolPolicyId, poolPolicyJson, poolPolicyFingerprint;
        public bool eligibilityUnlocked;
        public string qualifyingRunId;
        public List<DuplicateOfferingCardData> openingRoster, candidates, checkpointRoster, rosterAfter;
        public int selectedRuntimeInstanceId, createdRuntimeInstanceId;
        public string createdCardJson;
        public int jadeBefore, jadeAfter, forcedOpeningBefore, forcedOpeningAfter;
    }
    internal sealed class DuplicateOfferingCardData { public string canonicalJson; }
    internal sealed class DuplicateOfferingStringData { public string value; }
}
