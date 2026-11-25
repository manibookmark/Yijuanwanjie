using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Owns one closed, detached CardStat journal format and local evidence
    /// replay. Complete source and original-card proofs survive author changes;
    /// no live Run, wallet, baseline, eligibility or payment is owned here. The
    /// transaction must bind these consistent claims to its real effective node,
    /// Run owners, exact consumed instances and floor receipt before adoption.
    /// DTO metadata is inspected only by explicit calls, never a static initializer.</summary>
    internal static class DungeonRunCardStatOfferingSaveCodec
    {
        internal const int CurrentVersion = 2;
        private static ExactJsonWire Wire => new ExactJsonWire(typeof(DungeonRunCardStatOfferingSaveData),
            typeof(CardStatOfferingEntryData), typeof(CardStatOfferingCardData), typeof(CardStatOfferingSealData),
            typeof(CardStatOfferingCandidateData), typeof(CardStatOfferingCashData), typeof(CardStatOfferingStringData));
        private static ExactJsonWire LegacyWire => new ExactJsonWire(typeof(LegacyCardStatSaveData),
            typeof(LegacyCardStatEntryData), typeof(LegacyCardStatCardData), typeof(CardStatOfferingSealData),
            typeof(LegacyCardStatCandidateData), typeof(CardStatOfferingCashData));

        internal static DungeonRunCardStatOfferingSaveData CreateEmpty(string runId)
            => new DungeonRunCardStatOfferingSaveData { version = CurrentVersion, runId = runId ?? string.Empty,
                entries = new List<CardStatOfferingEntryData>() };

        internal static DungeonRunCardStatOfferingSaveData ReadRun(DungeonRunSaveData data, string payload)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrEmpty(payload)) throw Bad("Run wire is missing.");
            var names = DungeonRunCrystalEvents.ReadFieldNames(payload);
            string[] fields = { "hasCardStatOfferingState", "cardStatOfferingStateJson" };
            foreach (string name in names)
                foreach (string field in fields)
                    if (string.Equals(name, field, StringComparison.OrdinalIgnoreCase) && name != field)
                        throw Bad("A CardStat Run field uses an alias.");
            if (data.schemaVersion <= 36)
            {
                if (fields.Any(names.Contains) || data.hasCardStatOfferingState || !string.IsNullOrEmpty(data.cardStatOfferingStateJson))
                    throw Bad("Legacy Run cannot carry CardStat journal fields.");
                return CreateEmpty(data.runId);
            }
            if (!fields.All(names.Contains) || !data.hasCardStatOfferingState ||
                !JsonObjectShape.TryGetField(payload, fields[0], out string flag) || flag != "true" ||
                !JsonObjectShape.TryGetField(payload, fields[1], out string raw))
                throw Bad("Modern Run requires explicit CardStat state.");
            string json;
            try { json = Wire.Read<CardStatOfferingStringData>("{\"value\":" + raw + "}").value; }
            catch (ArgumentException exception) { throw Bad("CardStat journal must be a wire string.", exception); }
            if (string.IsNullOrEmpty(json) || json != data.cardStatOfferingStateJson)
                throw Bad("CardStat Run string differs from its parsed DTO.");
            var state = Read(json, data.schemaVersion == 37 ? 1 : CurrentVersion);
            if (state.runId != data.runId) throw Bad("Journal and saved Run identities differ.");
            return state;
        }

        internal static DungeonRunCardStatOfferingSaveData Read(string canonicalJson, int requiredVersion = CurrentVersion)
        {
            try
            {
                DungeonRunCardStatOfferingSaveData state;
                if (requiredVersion == 1)
                {
                    var old = LegacyWire.Read<LegacyCardStatSaveData>(canonicalJson);
                    if (old.version != 1 || LegacyWire.Write(old) != canonicalJson) throw Bad("Legacy journal shape/version/canonical wire differs.");
                    state = MigrateLegacy(old);
                }
                else if (requiredVersion == CurrentVersion)
                {
                    state = Wire.Read<DungeonRunCardStatOfferingSaveData>(canonicalJson);
                    if (Wire.Write(state) != canonicalJson) throw Bad("Journal wire is not canonical.");
                }
                else throw Bad("Unsupported required CardStat journal version.");
                ValidateLocal(state); return state;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException || exception is FormatException)
            { throw Bad("Invalid closed CardStat journal.", exception); }
        }

        internal static string Write(DungeonRunCardStatOfferingSaveData state)
        { ValidateLocal(state); return Wire.Write(state); }
        internal static DungeonRunCardStatOfferingSaveData Clone(DungeonRunCardStatOfferingSaveData state)
            => Wire.Read<DungeonRunCardStatOfferingSaveData>(Wire.Write(state));
        internal static CardStatOfferingEntryData CloneEntry(CardStatOfferingEntryData entry)
            => Wire.Read<CardStatOfferingEntryData>(Wire.Write(entry));
        internal static CardStatOfferingCandidateData CloneCandidate(CardStatOfferingCandidateData candidate)
            => Wire.Read<CardStatOfferingCandidateData>(Wire.Write(candidate));
        internal static string Hash(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(text)))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        internal static void ValidateLocal(DungeonRunCardStatOfferingSaveData state)
        {
            if (state == null || state.version != CurrentVersion || state.entries == null || state.runId == null)
                throw Bad("Journal envelope is missing or unsupported.");
            if (state.entries.Count > 0 || state.runId.Length > 0) RequireId(state.runId);
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            var effects = new HashSet<string>(StringComparer.Ordinal);
            var consumed = new HashSet<int>(); int active = 0;
            foreach (var entry in state.entries)
            {
                if (entry == null) throw Bad("Null journal entry.");
                RequireId(entry.floorId); RequireId(entry.nodeId); RequireId(entry.effectId); RequireHash(entry.effectFingerprint);
                if (!nodes.Add(entry.floorId + "\n" + entry.nodeId) || !effects.Add(entry.floorId + "\n" + entry.effectId))
                    throw Bad("Duplicate journal floor/node or effect.");
                var specification = ReadSpecification(entry);
                if (entry.physicalProofVersion != 1 && entry.physicalProofVersion != 2)
                    throw Bad("Unknown physical proof version.");
                RequireRoster(entry.openingRoster); RequireRoster(entry.rosterAfter);
                if (entry.physicalProofVersion == 1 && entry.openingRoster.Concat(entry.rosterAfter)
                    .Any(card => card.lineageState != (int)CardAuthorProvenance.LegacyUnknown))
                    throw Bad("Legacy CardStat proof cannot invent frozen known authors.");
                if (entry.candidates == null || entry.jadeBefore < 0 || entry.jadeAfter < 0 ||
                    entry.forcedOpeningBefore < 0 || entry.forcedOpeningAfter < 0)
                    throw Bad("Missing candidates or invalid owned counters.");
                if (entry.forcedOpeningBefore != 0 && !entry.openingRoster.Any(c => c.runtimeInstanceId == entry.forcedOpeningBefore))
                    throw Bad("Forced opening proof is outside the frozen roster.");
                var candidates = new Dictionary<int, SideEventCardStatProjectionProof>();
                foreach (var candidate in entry.candidates)
                {
                    if (candidate == null) throw Bad("Null frozen candidate.");
                    RequireCard(candidate.card);
                    var card = entry.openingRoster.SingleOrDefault(c => c.runtimeInstanceId == candidate.card.runtimeInstanceId);
                    if (card == null || CardFingerprint(card) != CardFingerprint(candidate.card) ||
                        candidates.ContainsKey(candidate.card.runtimeInstanceId)) throw Bad("Candidate is not one exact frozen roster instance.");
                    var projection = SideEventCardStatProjectionProof.Parse(candidate.projectionProofJson);
                    if (projection.Projection.CardRuntimeInstanceId != candidate.card.runtimeInstanceId ||
                        projection.Projection.CardContentId != candidate.card.contentId) throw Bad("Projection physical identity differs.");
                    if (entry.physicalProofVersion == 2)
                    {
                        var physical = RestoreCard(candidate.card);
                        if (!DungeonRunCardCodec.IsKnown(physical)) throw Bad("New CardStat candidates require frozen known authors.");
                        projection.RequireMatchesOriginalAuthor(CardAuthorDocument.Parse(physical.baseAuthorJson));
                    }
                    // A known empty/unsupported projection is evidence, not a payment candidate.
                    var supported = SideEventCardStatOfferingRules.Prepare(specification, projection.Projection.IsFcc, null, projection.Projection.ModifiedStats);
                    if (supported.Rewards.Count == 0) throw Bad("A candidate has no supported original stat tuple.");
                    candidates.Add(candidate.card.runtimeInstanceId, projection);
                }
                if (!entry.finished)
                {
                    if (!entry.active || ++active > 1 || entry.disposition != 0 || entry.selectedRuntimeInstanceId != 0 ||
                        entry.selectedContentId != null || entry.statApplicationProofJson != null || entry.cashGain != null ||
                        entry.walletBefore.HasValue || entry.walletAfter.HasValue || entry.jadeAfter != 0 ||
                        entry.forcedOpeningAfter != 0 || entry.rosterAfter.Count != 0)
                        throw Bad("Pending entry hides a terminal proof or activity.");
                    continue;
                }
                if (entry.active) throw Bad("Finished entry remains active.");
                if (entry.disposition == 1)
                {
                    if (!candidates.TryGetValue(entry.selectedRuntimeInstanceId, out var projection) ||
                        entry.selectedContentId != projection.Projection.CardContentId || !consumed.Add(entry.selectedRuntimeInstanceId) ||
                        entry.walletBefore.HasValue || entry.walletAfter.HasValue || entry.cashGain != null ||
                        entry.jadeBefore != entry.jadeAfter || entry.statApplicationProofJson == null)
                        throw Bad("Stats branch does not prove one exact instance payment.");
                    var expected = SideEventCardStatOfferingRules.Prepare(specification, projection.Projection.IsFcc, null, projection.Projection.ModifiedStats);
                    var application = DungeonRunStatApplications.ParseAndValidateProof(entry.statApplicationProofJson);
                    RequireRewards(expected, application);
                    if (application.Before.Progression.JadeEarned != entry.jadeBefore || application.After.Progression.JadeEarned != entry.jadeAfter)
                        throw Bad("Stat application changed or detached Run Jade.");
                    var remaining = entry.openingRoster.Where(c => c.runtimeInstanceId != entry.selectedRuntimeInstanceId).ToList();
                    RequireSameRoster(remaining, entry.rosterAfter);
                    int forcedAfter = entry.forcedOpeningBefore == entry.selectedRuntimeInstanceId ? 0 : entry.forcedOpeningBefore;
                    if (entry.forcedOpeningAfter != forcedAfter) throw Bad("Stat payment did not clear only its forced physical card.");
                }
                else if (entry.disposition == 2)
                {
                    SideEventCardStatOfferingRules.PrepareCashOut(specification);
                    var gain = entry.cashGain;
                    if (entry.selectedRuntimeInstanceId != 0 || entry.selectedContentId != null || entry.statApplicationProofJson != null ||
                        !entry.walletBefore.HasValue || !entry.walletAfter.HasValue || entry.walletBefore.Value < 0 || entry.walletAfter.Value < 0 ||
                        (long)entry.walletAfter.Value - entry.walletBefore.Value != 200 || (long)entry.jadeAfter - entry.jadeBefore != 200 ||
                        gain == null || gain.source != (int)JadeSource.CashOut || gain.baseJade != 200 || gain.finalJade != 200 || gain.affectedByGreed ||
                        entry.forcedOpeningBefore != entry.forcedOpeningAfter) throw Bad("Cash branch lacks exact flat-200 owned income proof.");
                    RequireSameRoster(entry.openingRoster, entry.rosterAfter);
                }
                else throw Bad("Finished entry has no typed disposition.");
            }
            foreach (var entry in state.entries.Where(e => e.active))
                if (entry.candidates.Any(c => consumed.Contains(c.card.runtimeInstanceId)))
                    throw Bad("Pending candidate was already consumed by this journal.");
        }

        private static SideEventCardStatOfferingSpecification ReadSpecification(CardStatOfferingEntryData entry)
        {
            var document = SideEventSourceWireDocument.Parse(entry.sourceDocumentJson);
            if (document.CanonicalJson != entry.sourceDocumentJson || document.Fingerprint != entry.sourceFingerprint ||
                !document.Definition.TryGetRoot(entry.sourceRootId, out var root) || root.Kind != SideEventSourceKind.CardStatOffering)
                throw Bad("Complete frozen CardStat source identity is inconsistent.");
            CardStatOfferingPreparedContext.ValidatePoolPolicy(entry.poolPolicyId, entry.poolPolicyJson);
            if (entry.poolPolicyFingerprint != Hash(entry.poolPolicyJson)) throw Bad("Frozen pool policy fingerprint differs.");
            var specification = (SideEventCardStatOfferingSpecification)root.Mechanic;
            SideEventCardStatOfferingRules.PrepareCashOut(specification); // validates the same source selection/support contract
            return specification;
        }

        private static void RequireRewards(SideEventCardStatOfferingResult expected, DungeonRunStatApplicationProof application)
        {
            if (expected.Rewards.Count != application.OrderedRewards.Count) throw Bad("Frozen source and application reward counts differ.");
            for (int i = 0; i < expected.Rewards.Count; i++)
            {
                var a = expected.Rewards[i]; var b = application.OrderedRewards[i];
                if (a.ModifiedStatIndex != b.ModifiedStatIndex || a.SourceStat != b.SourceStat || a.ModifiedAmount != b.ModifiedAmount ||
                    a.BaseChangeAmount != b.BaseChangeAmount || Bits(a.Modifier) != b.ModifierBits ||
                    Bits(a.Binary32Product) != b.ProductBits || a.RequestedAmount != b.RequestedAmount)
                    throw Bad("Application rewards do not match frozen source and original-card projection.");
            }
        }
        private static string Bits(float value) => BitConverter.ToUInt32(BitConverter.GetBytes(value), 0).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);

        internal static CardStatOfferingCardData CaptureCard(DungeonRunCardSaveData card)
        {
            card = DungeonRunCardCodec.Clone(card);
            var captured = new CardStatOfferingCardData { contentId = card.contentId, runtimeInstanceId = card.runtimeInstanceId,
                limitBreakCount = card.limitBreakCount, hasGemSlotCapacity = card.hasGemSlotCapacity, gemSlotCapacity = card.gemSlotCapacity,
                lineageVersion = card.lineageVersion, lineageState = card.lineageState,
                baseAuthorJson = card.baseAuthorJson, currentAuthorJson = card.currentAuthorJson,
                seals = card.seals.Select(s => s == null ? null : new CardStatOfferingSealData {
                    contentId = s.contentId, effect = (int)s.effect, value = s.value, comboScalable = s.comboScalable }).ToList() };
            RequireCard(captured); return captured;
        }
        internal static DungeonRunCardSaveData RestoreCard(CardStatOfferingCardData card)
        {
            RequireCard(card);
            return new DungeonRunCardSaveData { contentId = card.contentId, runtimeInstanceId = card.runtimeInstanceId,
                limitBreakCount = card.limitBreakCount, hasGemSlotCapacity = card.hasGemSlotCapacity, gemSlotCapacity = card.gemSlotCapacity,
                lineageVersion = card.lineageVersion, lineageState = card.lineageState,
                baseAuthorJson = card.baseAuthorJson, currentAuthorJson = card.currentAuthorJson,
                seals = card.seals.Select(s => new CardSealSpec { contentId = s.contentId, effect = (CardSealEffect)s.effect,
                    value = s.value, comboScalable = s.comboScalable }).ToList() };
        }
        internal static string CardFingerprint(CardStatOfferingCardData card)
        { RequireCard(card); return Hash(Wire.Write(card)); }
        internal static string CardFingerprint(DungeonRunCardSaveData card) => CardFingerprint(CaptureCard(card));
        private static void RequireCard(CardStatOfferingCardData card)
        {
            if (card == null || card.runtimeInstanceId <= 0 || card.limitBreakCount < 0 || card.gemSlotCapacity < 0 || card.seals == null ||
                card.seals.Count > CardSealRules.MaximumSealSlots || card.gemSlotCapacity > CardSealRules.MaximumSealSlots ||
                card.hasGemSlotCapacity && card.seals.Count > card.gemSlotCapacity) throw Bad("Invalid frozen physical card.");
            RequireId(card.contentId);
            // This captures installed state, not admission of a newly awarded seal.
            // Repeated IDs and signed values are retained exactly as in the Run card snapshot.
            foreach (var seal in card.seals)
            {
                if (seal == null || !Enum.IsDefined(typeof(CardSealEffect), seal.effect))
                    throw Bad("Invalid frozen installed seal.");
                RequireId(seal.contentId);
            }
            DungeonRunCardCodec.Validate(new DungeonRunCardSaveData { contentId = card.contentId, runtimeInstanceId = card.runtimeInstanceId,
                limitBreakCount = card.limitBreakCount, hasGemSlotCapacity = card.hasGemSlotCapacity, gemSlotCapacity = card.gemSlotCapacity,
                seals = card.seals.Select(s => new CardSealSpec { contentId = s.contentId, effect = (CardSealEffect)s.effect,
                    value = s.value, comboScalable = s.comboScalable }).ToList(), lineageVersion = card.lineageVersion,
                lineageState = card.lineageState, baseAuthorJson = card.baseAuthorJson, currentAuthorJson = card.currentAuthorJson });
        }
        private static void RequireRoster(IReadOnlyList<CardStatOfferingCardData> roster)
        {
            if (roster == null) throw Bad("A roster must be an explicit array.");
            var ids = new HashSet<int>(); foreach (var card in roster) { RequireCard(card); if (!ids.Add(card.runtimeInstanceId)) throw Bad("Duplicate physical roster identity."); }
        }
        private static void RequireSameRoster(IReadOnlyList<CardStatOfferingCardData> expected, IReadOnlyList<CardStatOfferingCardData> actual)
        {
            if (expected.Count != actual.Count) throw Bad("Physical roster membership changed outside the payment.");
            for (int i = 0; i < expected.Count; i++) if (CardFingerprint(expected[i]) != CardFingerprint(actual[i])) throw Bad("Physical roster order or payload changed outside the payment.");
        }
        internal static void RequireId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw Bad("A stable identity is required.");
            foreach (char c in value) if (char.IsControl(c) || char.IsWhiteSpace(c)) throw Bad("Invalid stable identity.");
            new UTF8Encoding(false, true).GetByteCount(value);
        }
        internal static void RequireHash(string value)
        { if (value == null || value.Length != 64 || value.Any(c => c < '0' || c > '9' && (c < 'a' || c > 'f'))) throw Bad("Canonical SHA256 required."); }
        private static InvalidOperationException Bad(string message, Exception cause = null)
            => new InvalidOperationException("Run CardStatOffering: " + message, cause);

        // A separate closed old carrier preserves the exact v1 shape. No reflection
        // writer containing new fields is used to verify or reinterpret old evidence.
        private static DungeonRunCardStatOfferingSaveData MigrateLegacy(LegacyCardStatSaveData old)
        {
            if (old.entries == null) throw Bad("Missing legacy entries.");
            return new DungeonRunCardStatOfferingSaveData { version = CurrentVersion, runId = old.runId,
                entries = old.entries.Select(entry => entry == null ? null : new CardStatOfferingEntryData {
                    floorId = entry.floorId, nodeId = entry.nodeId, effectId = entry.effectId, effectFingerprint = entry.effectFingerprint,
                    active = entry.active, finished = entry.finished, sourceDocumentJson = entry.sourceDocumentJson,
                    sourceFingerprint = entry.sourceFingerprint, sourceRootId = entry.sourceRootId, poolPolicyId = entry.poolPolicyId,
                    poolPolicyJson = entry.poolPolicyJson, poolPolicyFingerprint = entry.poolPolicyFingerprint,
                    openingRoster = entry.openingRoster?.Select(MigrateCard).ToList(), rosterAfter = entry.rosterAfter?.Select(MigrateCard).ToList(),
                    candidates = entry.candidates?.Select(candidate => candidate == null ? null : new CardStatOfferingCandidateData {
                        card = MigrateCard(candidate.card), projectionProofJson = candidate.projectionProofJson }).ToList(),
                    disposition = entry.disposition, selectedRuntimeInstanceId = entry.selectedRuntimeInstanceId,
                    selectedContentId = entry.selectedContentId, statApplicationProofJson = entry.statApplicationProofJson,
                    walletBefore = entry.walletBefore, walletAfter = entry.walletAfter, cashGain = entry.cashGain,
                    jadeBefore = entry.jadeBefore, jadeAfter = entry.jadeAfter, forcedOpeningBefore = entry.forcedOpeningBefore,
                    forcedOpeningAfter = entry.forcedOpeningAfter, physicalProofVersion = 1 }).ToList() };
        }
        private static CardStatOfferingCardData MigrateCard(LegacyCardStatCardData card)
        {
            if (card == null) return null;
            var saved = new DungeonRunCardSaveData { contentId = card.contentId, runtimeInstanceId = card.runtimeInstanceId,
                limitBreakCount = card.limitBreakCount, hasGemSlotCapacity = card.hasGemSlotCapacity, gemSlotCapacity = card.gemSlotCapacity,
                seals = card.seals?.Select(s => s == null ? null : new CardSealSpec { contentId = s.contentId,
                    effect = (CardSealEffect)s.effect, value = s.value, comboScalable = s.comboScalable }).ToList() };
            return CaptureCard(DungeonRunCardCodec.MigrateLegacy(saved));
        }
        private sealed class LegacyCardStatSaveData
        { public int version; public string runId; public List<LegacyCardStatEntryData> entries; }
        private sealed class LegacyCardStatEntryData
        {
            public string floorId, nodeId, effectId, effectFingerprint;
            public bool active, finished;
            public string sourceDocumentJson, sourceFingerprint, sourceRootId, poolPolicyId, poolPolicyJson, poolPolicyFingerprint;
            public List<LegacyCardStatCardData> openingRoster;
            public List<LegacyCardStatCandidateData> candidates;
            public List<LegacyCardStatCardData> rosterAfter;
            public int disposition, selectedRuntimeInstanceId;
            public string selectedContentId, statApplicationProofJson;
            public int? walletBefore, walletAfter;
            public CardStatOfferingCashData cashGain;
            public int jadeBefore, jadeAfter, forcedOpeningBefore, forcedOpeningAfter;
        }
        private sealed class LegacyCardStatCandidateData
        { public LegacyCardStatCardData card; public string projectionProofJson; }
        private sealed class LegacyCardStatCardData
        {
            public string contentId; public int runtimeInstanceId, limitBreakCount;
            public bool hasGemSlotCapacity; public int gemSlotCapacity;
            public List<CardStatOfferingSealData> seals;
        }
    }

    // This sole mutable DTO family is detached from the live journal. The codec
    // never publishes it as a readonly view or treats caller-written claims as authorization.
    internal sealed class DungeonRunCardStatOfferingSaveData
    { public int version = 0; public string runId = null; public List<CardStatOfferingEntryData> entries = null; }
    internal sealed class CardStatOfferingEntryData
    {
        public string floorId = null, nodeId = null, effectId = null, effectFingerprint = null;
        public bool active = false, finished = false;
        public int physicalProofVersion = 0;
        public string sourceDocumentJson = null, sourceFingerprint = null, sourceRootId = null;
        public string poolPolicyId = null, poolPolicyJson = null, poolPolicyFingerprint = null;
        public List<CardStatOfferingCardData> openingRoster = null;
        public List<CardStatOfferingCandidateData> candidates = null;
        public List<CardStatOfferingCardData> rosterAfter = null;
        public int disposition = 0, selectedRuntimeInstanceId = 0;
        public string selectedContentId = null, statApplicationProofJson = null;
        public int? walletBefore = null, walletAfter = null;
        public CardStatOfferingCashData cashGain = null;
        public int jadeBefore = 0, jadeAfter = 0, forcedOpeningBefore = 0, forcedOpeningAfter = 0;
    }
    internal sealed class CardStatOfferingCandidateData
    { public CardStatOfferingCardData card = null; public string projectionProofJson = null; }
    internal sealed class CardStatOfferingCardData
    {
        public string contentId = null; public int runtimeInstanceId = 0, limitBreakCount = 0;
        public bool hasGemSlotCapacity = false; public int gemSlotCapacity = 0; public List<CardStatOfferingSealData> seals = null;
        public int lineageVersion = 0, lineageState = 0;
        public string baseAuthorJson = null, currentAuthorJson = null;
    }
    internal sealed class CardStatOfferingSealData
    { public string contentId = null; public int effect = 0, value = 0; public bool comboScalable = false; }
    internal sealed class CardStatOfferingCashData
    { public int source = 0, baseJade = 0, finalJade = 0; public bool affectedByGreed = false; }
    internal sealed class CardStatOfferingStringData
    { public string value = null; }
}
