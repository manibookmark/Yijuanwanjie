using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Closed Hire journal and detached price/roster mathematics. This
    /// validates consistent saved claims, not their historical authenticity or a
    /// production FCC mapping. Live node, wallet, source plan and inventory joint
    /// checks remain with the lifecycle owner. No native author is instantiated.</summary>
    internal static class DungeonRunGunForHireSaveCodec
    {
        private static ExactJsonWire Wire => new ExactJsonWire(typeof(GunForHireSaveData), typeof(GunForHireEntryData),
            typeof(GunForHireCandidateData), typeof(GunForHireProvenanceData), typeof(GunForHireCardData), typeof(GunForHireGainData), typeof(HireStringData));
        internal static GunForHireSaveData CreateEmpty(string runId)
            => new GunForHireSaveData { version = 1, runId = runId, entries = new List<GunForHireEntryData>() };
        internal static GunForHireSaveData ReadRun(DungeonRunSaveData data, string payload)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var names = DungeonRunCrystalEvents.ReadFieldNames(payload);
            string[] keys = { "hasGunForHireState", "gunForHireStateJson" };
            foreach (string name in names)
                foreach (string key in keys)
                    if (name != key && string.Equals(name, key, StringComparison.OrdinalIgnoreCase)) throw Bad("Hire Run key alias.");
            if (data.schemaVersion <= 38)
            {
                if (keys.Any(names.Contains) || data.hasGunForHireState || !string.IsNullOrEmpty(data.gunForHireStateJson))
                    throw Bad("Legacy Run cannot carry future Hire fields.");
                return CreateEmpty(data.runId);
            }
            if (data.schemaVersion < 39 || data.schemaVersion > 41) throw Bad("Unsupported Run schema for the Hire journal.");
            if (!keys.All(names.Contains) || !data.hasGunForHireState ||
                !JsonObjectShape.TryGetField(payload, keys[0], out var flag) || flag != "true" ||
                !JsonObjectShape.TryGetField(payload, keys[1], out var body)) throw Bad("Modern Run requires explicit Hire journal.");
            string json = Wire.Read<HireStringData>("{\"value\":" + body + "}").value;
            if (string.IsNullOrEmpty(json) || json != data.gunForHireStateJson) throw Bad("Hire string carrier differs from DTO.");
            var state = Read(json); if (state.runId != data.runId) throw Bad("Hire belongs to another saved Run."); return state;
        }
        internal static GunForHireSaveData Read(string json)
        {
            var state = Wire.Read<GunForHireSaveData>(json);
            if (Wire.Write(state) != json) throw Bad("Hire wire must be canonical."); ValidateLocal(state); return state;
        }
        internal static string Write(GunForHireSaveData state) { ValidateLocal(state); return Wire.Write(state); }
        internal static GunForHireSaveData Clone(GunForHireSaveData state) => Wire.Read<GunForHireSaveData>(Wire.Write(state));
        internal static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(value)))
                .Replace("-", "").ToLowerInvariant();
        }
        internal static void RequireId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) throw Bad("Exact nonempty identity required.");
            new UTF8Encoding(false, true).GetBytes(value);
        }
        internal static void RequireHash(string value)
        { if (value == null || value.Length != 64 || value.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))) throw Bad("Canonical SHA-256 required."); }
        internal static GunForHireCandidateData CaptureCandidate(GunForHireCandidate candidate)
            => new GunForHireCandidateData { authorJson = candidate.Author.CanonicalJson, authorFingerprint = candidate.Author.Fingerprint,
                sourceAssetId = candidate.SourceAssetId, admittedUnlock = candidate.AdmittedUnlock,
                provenance = new GunForHireProvenanceData { alias = candidate.Provenance.Alias, sourceFile = candidate.Provenance.SourceFile,
                    pathId = candidate.Provenance.PathId, byteStart = candidate.Provenance.ByteStart, byteSize = candidate.Provenance.ByteSize,
                    sha256 = candidate.Provenance.Sha256 } };
        internal static GunForHireCandidate RestoreCandidate(GunForHireCandidateData data)
        {
            if (data == null || data.provenance == null) throw Bad("FCC candidate evidence is missing.");
            var document = CardAuthorDocument.Parse(data.authorJson);
            if (document.Fingerprint != data.authorFingerprint) throw Bad("FCC author fingerprint differs.");
            var p = data.provenance;
            return new GunForHireCandidate(document, data.sourceAssetId,
                new SideEventSourceProvenance(p.alias, p.sourceFile, p.pathId, p.byteStart, p.byteSize, p.sha256), data.admittedUnlock);
        }
        internal static GunForHireCardData CaptureCard(DungeonRunCardSaveData card)
            => new GunForHireCardData { json = DungeonRunCardCodec.CanonicalJson(card), fingerprint = DungeonRunCardCodec.Fingerprint(card) };
        internal static DungeonRunCardSaveData RestoreCard(GunForHireCardData card)
        {
            if (card == null) throw Bad("Null physical card proof."); var data = DungeonRunCardCodec.Parse(card.json);
            if (DungeonRunCardCodec.Fingerprint(data) != card.fingerprint) throw Bad("Physical card fingerprint differs."); return data;
        }
        internal static List<GunForHireCardData> CaptureRoster(IReadOnlyList<DungeonRunCardSaveData> cards)
            => cards.Select(CaptureCard).ToList();
        internal static bool SameRoster(IReadOnlyList<GunForHireCardData> left, IReadOnlyList<GunForHireCardData> right)
            => left != null && right != null && left.Select(c => c.json).SequenceEqual(right.Select(c => c.json));
        internal static SideEventGunForHireQuote Quote(GunForHireEntryData entry)
        {
            var document = SideEventSourceWireDocument.Parse(entry.sourceDocumentJson);
            return SideEventGunForHirePriceRules.Quote(GunForHirePreparedContext.RequireSpecification(document, entry.sourceRootId),
                entry.rawBuyPrice, entry.rawPityPrice, entry.completedSourceFloors);
        }
        internal static bool IsOwned(GunForHireCandidateData candidate, IReadOnlyList<GunForHireCardData> roster)
        {
            var chosen = RestoreCandidate(candidate);
            foreach (var card in roster)
            {
                var physical = RestoreCard(card);
                if (physical.contentId == chosen.Author.ContentId) return true;
                if (DungeonRunCardCodec.IsKnown(physical))
                {
                    var admission = CardAuthorDocument.Parse(physical.currentAuthorJson).ReadOfferingAdmission();
                    if (admission.SourceClass == "FccConfig" && admission.SourceConfigId == chosen.SourceAssetId) return true;
                }
            }
            return false;
        }
        internal static void ValidateLocal(GunForHireSaveData state)
        {
            if (state == null || state.version != 1 || state.entries == null) throw Bad("Unsupported Hire envelope.");
            RequireId(state.runId); var nodes = new HashSet<string>(); var effects = new HashSet<string>(); var grants = new HashSet<int>(); int active = 0;
            foreach (var e in state.entries)
            {
                if (e == null) throw Bad("Null Hire entry."); RequireId(e.floorId); RequireId(e.nodeId); RequireId(e.effectId); RequireHash(e.effectFingerprint);
                if (!nodes.Add(e.floorId + "\n" + e.nodeId) || !effects.Add(e.floorId + "\n" + e.effectId)) throw Bad("Duplicate Hire node/effect.");
                var source = SideEventSourceWireDocument.Parse(e.sourceDocumentJson);
                if (source.Fingerprint != e.sourceFingerprint || e.poolPolicyId != GunForHirePreparedContext.ExplicitSubsetPolicyId)
                    throw Bad("Hire source or explicit FCC policy differs.");
                GunForHirePreparedContext.RequireSpecification(source, e.sourceRootId);
                if (!e.eventAdmitted || e.generation < 0 || e.completedSourceFloors < 0 || e.candidates == null || e.openingRoster == null || e.rosterAfter == null ||
                    e.jadeBefore < 0 || e.jadeAfter < 0) throw Bad("Incomplete Hire checkpoint.");
                var candidates = e.candidates.Select(RestoreCandidate).ToArray();
                new GunForHirePreparedContext(source, e.sourceRootId, e.completedSourceFloors, candidates, e.eventAdmitted);
                RequireRoster(e.openingRoster); RequireRoster(e.rosterAfter); RequireForced(e.openingRoster, e.forcedOpeningBefore);
                var quote = Quote(e);
                if (quote.BuyPrice < 0 || quote.PityPrice < 0 || e.buyPrice != quote.BuyPrice || e.pityPrice != quote.PityPrice)
                    throw Bad("Unsupported or rewritten Hire quotation.");
                var eligible = e.candidates.Select((c, i) => new { c, i }).Where(v => v.c.admittedUnlock && !IsOwned(v.c, e.openingRoster)).Select(v => v.i).ToArray();
                if (e.selectedIndex < -1 || e.selectedIndex >= candidates.Length ||
                    (e.selectedIndex == -1 ? eligible.Length != 0 : !eligible.Contains(e.selectedIndex))) throw Bad("Hire choice is outside its frozen legal pool.");
                if (e.active && ++active > 1 || e.active && e.finished) throw Bad("Multiple/finished active Hire entries.");
                if (!e.finished)
                {
                    if (e.disposition != 0 || e.grantedId != 0 || e.grantedContentId != null || e.walletBefore != null || e.walletAfter != null ||
                        e.pityGain != null || e.rosterAfter.Count != 0 || e.jadeAfter != e.jadeBefore || e.forcedOpeningAfter != 0) throw Bad("Unfinished Hire claims a grant.");
                    continue;
                }
                if (e.active || e.walletBefore == null || e.walletAfter == null || e.walletBefore < 0 || e.walletAfter < 0) throw Bad("Missing Hire payment proof.");
                if (e.disposition == 1)
                {
                    if (e.pityGain != null || e.selectedIndex < 0 || e.grantedId <= 0 || !grants.Add(e.grantedId) || e.grantedContentId != candidates[e.selectedIndex].Author.ContentId ||
                        (long)e.walletBefore.Value - quote.BuyPrice != e.walletAfter.Value || e.jadeAfter != e.jadeBefore ||
                        e.rosterAfter.Count != e.openingRoster.Count + 1 || e.forcedOpeningAfter != e.forcedOpeningBefore) throw Bad("Invalid exact Hire purchase.");
                    var openingIds = e.openingRoster.Select(c => RestoreCard(c).runtimeInstanceId).ToArray();
                    if (openingIds.Contains(e.grantedId)) throw Bad("Hire reuses an opening physical identity.");
                    var added = e.rosterAfter.SingleOrDefault(c => RestoreCard(c).runtimeInstanceId == e.grantedId);
                    if (added == null || !SameRoster(e.openingRoster, e.rosterAfter.Where(c => RestoreCard(c).runtimeInstanceId != e.grantedId).ToList())) throw Bad("Hire changed another card.");
                    var reward = RestoreCard(added); var author = candidates[e.selectedIndex].Author;
                    if (!DungeonRunCardCodec.IsKnown(reward) || reward.contentId != author.ContentId || reward.baseAuthorJson != author.CanonicalJson ||
                        reward.currentAuthorJson != author.CanonicalJson || reward.limitBreakCount != 0 || reward.seals.Count != 0 ||
                        !reward.hasGemSlotCapacity || reward.gemSlotCapacity != author.ReadGemSlotCapacity())
                        throw Bad("Hire reward does not use its complete frozen author birth.");
                }
                else if (e.disposition == 2)
                {
                    if (e.grantedId != 0 || e.grantedContentId != null || (long)e.walletBefore.Value + quote.PityPrice != e.walletAfter.Value ||
                        (long)e.jadeBefore + quote.PityPrice != e.jadeAfter || !SameRoster(e.openingRoster, e.rosterAfter) ||
                        e.forcedOpeningBefore != e.forcedOpeningAfter || e.pityGain == null || e.pityGain.source != (int)JadeSource.CashOut ||
                        e.pityGain.baseJade != quote.PityPrice || e.pityGain.finalJade != quote.PityPrice || e.pityGain.affectedByGreed)
                        throw Bad("Invalid flat Hire pity proof.");
                }
                else throw Bad("Unknown Hire completion branch.");
                RequireForced(e.rosterAfter, e.forcedOpeningAfter);
            }
        }
        private static void RequireRoster(List<GunForHireCardData> cards)
        { var ids = new HashSet<int>(); foreach (var card in cards) if (!ids.Add(RestoreCard(card).runtimeInstanceId)) throw Bad("Duplicate physical roster identity."); }
        private static void RequireForced(List<GunForHireCardData> cards, int id)
        { if (id < 0 || id > 0 && !cards.Any(c => RestoreCard(c).runtimeInstanceId == id)) throw Bad("Forced hand target is absent."); }
        internal static InvalidOperationException Bad(string message) => new InvalidOperationException(message);
    }
    internal sealed class GunForHireSaveData { public int version; public string runId; public List<GunForHireEntryData> entries; }
    internal sealed class GunForHireEntryData
    {
        public string floorId, nodeId, effectId, effectFingerprint, sourceDocumentJson, sourceFingerprint, sourceRootId, poolPolicyId;
        public bool active, finished, eventAdmitted;
        public int generation, completedSourceFloors, selectedIndex, rawBuyPrice, rawPityPrice, buyPrice, pityPrice;
        public List<GunForHireCandidateData> candidates;
        public List<GunForHireCardData> openingRoster, rosterAfter;
        public int disposition, grantedId; public string grantedContentId;
        public int? walletBefore, walletAfter;
        public GunForHireGainData pityGain;
        public int jadeBefore, jadeAfter, forcedOpeningBefore, forcedOpeningAfter;
    }
    internal sealed class GunForHireCandidateData
    { public string authorJson, authorFingerprint, sourceAssetId; public bool admittedUnlock; public GunForHireProvenanceData provenance; }
    internal sealed class GunForHireProvenanceData
    { public string alias, sourceFile; public long pathId, byteStart; public int byteSize; public string sha256; }
    internal sealed class GunForHireCardData { public string json, fingerprint; }
    internal sealed class GunForHireGainData { public int source, baseJade, finalJade; public bool affectedByGreed; }
    internal sealed class HireStringData { public string value; }
}
