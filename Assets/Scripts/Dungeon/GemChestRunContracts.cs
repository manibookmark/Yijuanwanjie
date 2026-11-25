using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Resolves explicit project policy and complete source evidence. Unknown inputs must fail,
    /// never fall back to the historical Preferred/Shuffle pool. Called only on a new durable opening.</summary>
    public interface IDungeonRunGemChestOfferResolver
    {
        bool TryPrepare(string runId, LevelInteractionEffect effect, DungeonRunInteractionCatalog catalog,
            out GemChestPreparedOffer offer, out string error);
    }

    /// <summary>Explicit source-card evidence for Silver payment. The supplier must record
    /// unresolved cards in its input proof; it cannot infer FCC or native types from project colors.
    /// This interface supplies immutable inputs, never owns payment or removes a Run card.</summary>
    public interface IDungeonRunGemChestPaymentResolver
    {
        bool TryPreparePayment(string runId, LevelInteractionEffect effect, DungeonRunInteractionCatalog catalog,
            out GemChestPreparedPayment payment, out string error);
    }

    public sealed class GemChestPreparedPayment
    {
        public string ResolverContractId { get; }
        public string CanonicalInputJson { get; }
        public string InputFingerprint { get; }
        public IReadOnlyList<string> Requirements { get; }
        public IReadOnlyList<SilverGemChestPaymentCard> Cards { get; }
        public GemChestPreparedPayment(string resolverContractId, string canonicalInputJson,
            IReadOnlyList<string> requirements, IReadOnlyList<SilverGemChestPaymentCard> cards)
        {
            GemChestRunCodec.RequireId(resolverContractId); GemChestRunCodec.ReadFields(canonicalInputJson);
            var frozen = SideEventSilverGemChestPaymentRules.CreateFrozen(requirements);
            if (cards == null || cards.Any(card => card == null) ||
                cards.Select(card => card.RuntimeInstanceId).Distinct().Count() != cards.Count)
                throw new ArgumentException("Silver evidence requires explicit unique physical cards.");
            ResolverContractId = resolverContractId; CanonicalInputJson = canonicalInputJson;
            InputFingerprint = GemChestRunCodec.Hash(canonicalInputJson); Requirements = frozen.Requirements;
            Cards = Array.AsReadOnly(cards.Select(card => new SilverGemChestPaymentCard(
                card.RuntimeInstanceId, card.SourceCardTypes, card.IsFcc)).ToArray());
        }
    }

    /// <summary>An occurrence, not a seal ID: guaranteed evolution may repeat a Gem with distinct outcomes.
    /// Host IDs are an explicit project-policy projection, not an inferred native tag/recipe predicate.</summary>
    public sealed class GemChestPreparedChoice
    {
        private readonly CardSealSpec _seal;
        public SideEventGemChoice Choice { get; }
        public CardSealSpec Seal => _seal.Clone();
        public IReadOnlyList<int> HostRuntimeInstanceIds { get; }
        public GemChestPreparedChoice(SideEventGemChoice choice, CardSealSpec seal, IReadOnlyList<int> hostRuntimeInstanceIds)
        {
            Choice = choice ?? throw new ArgumentNullException(nameof(choice));
            GemChestRunCodec.RequireSeal(seal); _seal = seal.Clone();
            if (hostRuntimeInstanceIds == null || hostRuntimeInstanceIds.Any(id => id <= 0) ||
                hostRuntimeInstanceIds.Distinct().Count() != hostRuntimeInstanceIds.Count)
                throw new ArgumentException("Gem hosts must be explicit unique physical IDs.");
            HostRuntimeInstanceIds = Array.AsReadOnly(hostRuntimeInstanceIds.ToArray());
            if ((_seal.effect == CardSealEffect.Evolution) != !string.IsNullOrEmpty(choice.EvolutionOutcomeContentId))
                throw new ArgumentException("Gem outcome and project seal policy disagree.");
        }
    }

    /// <summary>Detached complete generation result and project seal projection. InputFingerprint proves
    /// preservation of the supplier's complete input text, not its completeness, account eligibility,
    /// native RNG origin or source placement. The resolver owns those claims; restore never reruns it.</summary>
    public sealed class GemChestPreparedOffer
    {
        internal GemChestPreparedData Data { get; }
        public string ResolverContractId => Data.resolverContractId;
        public string SourceRegistryContentId => Data.sourceRegistryContentId;
        public string SourceRegistryFingerprint => Data.sourceRegistryFingerprint;
        public string CanonicalInputJson => Data.canonicalInputJson;
        public string InputFingerprint => Data.inputFingerprint;
        public int RequestedCount => Data.requestedCount;
        public SideEventGemOfferGenerationResult GenerationResult { get; }
        public IReadOnlyList<GemChestPreparedChoice> Choices { get; }
        public GemChestPreparedOffer(string resolverContractId, string sourceRegistryContentId,
            string sourceRegistryFingerprint, string canonicalInputJson, SideEventGemOfferGenerationResult result,
            IReadOnlyList<GemChestPreparedChoice> choices)
        {
            if (result == null || choices == null || choices.Count != result.Choices.Count)
                throw new ArgumentException("Gem projection must preserve every generated occurrence.");
            GenerationResult = result;
            GemChestRunCodec.RequireId(resolverContractId); GemChestRunCodec.RequireId(sourceRegistryContentId);
            GemChestRunCodec.RequireHash(sourceRegistryFingerprint); GemChestRunCodec.RequireText(canonicalInputJson);
            GemChestRunCodec.ReadFields(canonicalInputJson);
            var copied = new List<GemChestPreparedChoice>();
            Data = new GemChestPreparedData { resolverContractId = resolverContractId,
                sourceRegistryContentId = sourceRegistryContentId, sourceRegistryFingerprint = sourceRegistryFingerprint,
                canonicalInputJson = canonicalInputJson, inputFingerprint = GemChestRunCodec.Hash(canonicalInputJson),
                requestedCount = result.RequestedCount, allFrequencyEligibleGemCount = result.AllFrequencyEligibleGemCount,
                initialPreviousGemsFiltered = result.InitialPreviousGemsFiltered, weightedAttempts = result.WeightedAttempts };
            for (int i = 0; i < choices.Count; i++)
            {
                var source = choices[i] ?? throw new ArgumentException("Null Gem occurrence.");
                var generated = result.Choices[i];
                if (source.Choice.GemContentId != generated.GemContentId || source.Choice.GemSourceAssetId != generated.GemSourceAssetId ||
                    source.Choice.EvolutionOutcomeContentId != generated.EvolutionOutcomeContentId)
                    throw new ArgumentException("Gem projection changed generation order or identity.");
                copied.Add(new GemChestPreparedChoice(generated, source.Seal, source.HostRuntimeInstanceIds));
                Data.choices.Add(new GemChestChoiceData { gemContentId = generated.GemContentId,
                    gemSourceAssetId = generated.GemSourceAssetId, evolutionOutcomeContentId = generated.EvolutionOutcomeContentId ?? string.Empty,
                    seal = source.Seal, hostIds = source.HostRuntimeInstanceIds.ToList() });
            }
            foreach (var expansion in result.Expansions) Data.expansions.Add(new GemChestExpansionData {
                frequencyContentId = expansion.AddedFrequencyContentId, raised = expansion.Raised, countAfter = expansion.ChoiceCountAfterRefill });
            GemChestRunCodec.ValidatePrepared(Data); Choices = copied.AsReadOnly();
        }
    }

    [Serializable] internal sealed class GemChestRunSaveData
    {
        public int version = 3;
        public string runId = string.Empty;
        public string fingerprint = string.Empty;
        public List<GemChestEntryData> entries = new List<GemChestEntryData>();
    }
    [Serializable] internal sealed class GemChestEntryData
    {
        public string floorId = string.Empty, nodeId = string.Empty, effectId = string.Empty, effectFingerprint = string.Empty;
        public GemChestPreparedData prepared = new GemChestPreparedData();
        // Only the active unfinished entry locks other Run commands. Dormant Silver caches remain durable.
        public bool active = true;
        public GemChestPaymentData payment = null;
        public int selectedOccurrence = -1;
        public bool finished;
        public string decisionId = string.Empty;
        public int before, after, amount, targetId;
        public string targetContentId = string.Empty;
        public int disposition; // 0 pending; 1 assignment; 2 fixed-200/Greed cash-out.
        public GemChestCardData cardBefore = null, cardAfter = null;
        public int physicalProofVersion = 0; // 1 validated legacy six-field proof; 2 full lineage.
        public List<GemChestCardData> hostCards = new List<GemChestCardData>();
    }
    [Serializable] internal sealed class GemChestPaymentData
    {
        public string resolverContractId = string.Empty, canonicalInputJson = string.Empty, inputFingerprint = string.Empty;
        public List<string> requirements = new List<string>(), remainingRequirements = new List<string>();
        public List<GemChestPaymentCardData> candidates = new List<GemChestPaymentCardData>();
        public List<int> selectedIds = new List<int>();
        public bool paid;
        // Opening evidence stays immutable across cancel/re-entry. The exact final removal proof
        // captures the actual roster immediately before payment, including any later legal additions.
        public List<GemChestCardData> openingRoster = new List<GemChestCardData>();
        public List<GemChestCardData> rosterBeforePayment = new List<GemChestCardData>(), rosterAfterPayment = new List<GemChestCardData>();
        public int forcedOpeningBefore, forcedOpeningAfter;
    }
    [Serializable] internal sealed class GemChestPaymentCardData
    {
        public GemChestCardData card = new GemChestCardData();
        public string cardFingerprint = string.Empty;
        public bool isFcc;
        public List<string> sourceCardTypes = new List<string>();
    }
    [Serializable] internal sealed class GemChestPreparedData
    {
        public string resolverContractId = string.Empty, sourceRegistryContentId = string.Empty, sourceRegistryFingerprint = string.Empty;
        public string canonicalInputJson = string.Empty, inputFingerprint = string.Empty;
        public int requestedCount, allFrequencyEligibleGemCount, weightedAttempts;
        public bool initialPreviousGemsFiltered;
        public List<GemChestChoiceData> choices = new List<GemChestChoiceData>();
        public List<GemChestExpansionData> expansions = new List<GemChestExpansionData>();
    }
    [Serializable] internal sealed class GemChestChoiceData
    {
        public string gemContentId = string.Empty, gemSourceAssetId = string.Empty, evolutionOutcomeContentId = string.Empty;
        public CardSealSpec seal = new CardSealSpec();
        public List<int> hostIds = new List<int>();
    }
    [Serializable] internal sealed class GemChestExpansionData
    { public string frequencyContentId = string.Empty; public bool raised; public int countAfter; }
    [Serializable] internal sealed class GemChestCardData
    {
        public string contentId = string.Empty;
        public int runtimeInstanceId, limitBreakCount, gemSlotCapacity;
        public bool hasGemSlotCapacity;
        public List<CardSealSpec> seals = new List<CardSealSpec>();
        public int lineageVersion, lineageState;
        public string baseAuthorJson = string.Empty, currentAuthorJson = string.Empty;
    }

    /// <summary>Closed Gem journal wire. Every field, null and array is explicit; integers are exact,
    /// IDs use strict UTF-8. This checkpoint hash detects drift, not forged-author authorization.
    /// JSON input text remains supplier-owned opaque evidence and is never used as execution protocol.</summary>
    internal static class GemChestRunCodec
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static string Write(object value)
        {
            var output = new StringBuilder(); WriteValue(output, value); return output.ToString();
        }
        internal static string WriteVersion(GemChestRunSaveData value, int version)
        {
            if (version < 1 || version > 3) throw Bad("Unsupported Gem wire version.");
            var output = new StringBuilder(); WriteValue(output, value, version); return output.ToString();
        }
        // requiredVersion is supplied by the outer Run schema: 33 reads only original v1,
        // 34..37 read only v2; 38 reads v3. Validate old shape/hash before migration.
        internal static GemChestRunSaveData Read(string raw, int requiredVersion = 3)
        {
            if (requiredVersion < 1 || requiredVersion > 3) throw Bad("Unsupported required Gem version.");
            RequireShape(raw, typeof(GemChestRunSaveData), false, requiredVersion);
            var data = JsonUtility.FromJson<GemChestRunSaveData>(raw);
            RestoreExplicitNulls(raw, data, requiredVersion);
            if (data == null || data.version != requiredVersion || data.entries == null) throw Bad("Unsupported Gem journal.");
            RequireId(data.runId); RequireHash(data.fingerprint);
            if (data.fingerprint != Fingerprint(data, requiredVersion == 1)) throw Bad("Gem journal fingerprint changed.");
            if (requiredVersion == 1)
            {
                foreach (var entry in data.entries)
                {
                    if (entry.prepared == null || (entry.prepared.resolverContractId == DungeonRunGemChests.SilverOfferContractId ||
                        entry.prepared.resolverContractId == DungeonRunGemChests.EvoStatueOfferContractId ||
                        entry.prepared.resolverContractId == DungeonRunGemChests.GoldOfferContractId))
                        throw Bad("Legacy Gem journal cannot carry later Silver, Evo or Gold source policies.");
                    entry.active = !entry.finished; entry.payment = null;
                }
            }
            if (requiredVersion < 3)
            {
                foreach (var entry in data.entries)
                {
                    entry.physicalProofVersion = 1; entry.hostCards = new List<GemChestCardData>();
                    MigrateCard(entry.cardBefore); MigrateCard(entry.cardAfter);
                    if (entry.payment == null) continue;
                    foreach (var card in entry.payment.openingRoster.Concat(entry.payment.rosterBeforePayment).Concat(entry.payment.rosterAfterPayment)) MigrateCard(card);
                    foreach (var candidate in entry.payment.candidates)
                    {
                        var original = new StringBuilder(); WriteValue(original, candidate.card, requiredVersion);
                        if (candidate.cardFingerprint != Hash(original.ToString())) throw Bad("Legacy Silver candidate fingerprint changed.");
                        MigrateCard(candidate.card); candidate.cardFingerprint = Hash(Write(candidate.card));
                    }
                }
                data.version = 3; data.fingerprint = Fingerprint(data);
            }
            return data;
        }
        internal static GemChestRunSaveData Clone(GemChestRunSaveData data)
        {
            string raw = Write(data); var copy = JsonUtility.FromJson<GemChestRunSaveData>(raw);
            RestoreExplicitNulls(raw, copy, data.version); return copy;
        }
        private static void RestoreExplicitNulls(string raw, GemChestRunSaveData data, int version)
        {
            if (data?.entries == null || !JsonObjectShape.TryGetArrayItems(ReadFields(raw)["entries"], out var entries) || entries.Count != data.entries.Count)
                throw Bad("Gem entries changed during DTO decoding.");
            for (int i = 0; i < entries.Count; i++)
            {
                var fields = ReadFields(entries[i]);
                if (fields["cardBefore"] == "null") data.entries[i].cardBefore = null;
                if (fields["cardAfter"] == "null") data.entries[i].cardAfter = null;
                if (version == 1) data.entries[i].payment = null;
                else
                {
                    if (fields["payment"] == "null") data.entries[i].payment = null;
                    if (fields["prepared"] == "null") data.entries[i].prepared = null;
                }
            }
        }
        internal static string Fingerprint(GemChestRunSaveData data, bool legacy = false)
        {
            string previous = data.fingerprint; data.fingerprint = string.Empty;
            try { var output = new StringBuilder(); WriteValue(output, data, legacy ? 1 : data.version); return Hash(output.ToString()); }
            finally { data.fingerprint = previous; }
        }
        internal static string Hash(string text)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Utf8.GetBytes(text))).Replace("-", string.Empty).ToLowerInvariant();
        }
        internal static void RequireId(string value)
        { RequireText(value); if (value.Length == 0 || value.Trim() != value || value.Any(char.IsControl)) throw Bad("Missing, padded or control-character Gem identity."); }
        internal static void RequireText(string value)
        { if (value == null) throw Bad("Missing Gem text."); try { Utf8.GetByteCount(value); } catch (EncoderFallbackException e) { throw new InvalidOperationException("Invalid Gem Unicode.", e); } }
        internal static void RequireHash(string value)
        { if (value == null || value.Length != 64 || value.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))) throw Bad("Invalid Gem fingerprint."); }
        internal static void RequireSeal(CardSealSpec seal)
        {
            if (seal == null || !Enum.IsDefined(typeof(CardSealEffect), seal.effect) || seal.value <= 0) throw Bad("Invalid Gem seal specification.");
            RequireId(seal.contentId);
        }
        internal static void ValidatePrepared(GemChestPreparedData data)
        {
            if (data == null || data.choices == null || data.expansions == null || data.requestedCount < 1 ||
                data.allFrequencyEligibleGemCount < 0 || data.weightedAttempts < 0 || data.choices.Count > data.requestedCount)
                throw Bad("Invalid complete Gem generation result.");
            RequireId(data.resolverContractId); RequireId(data.sourceRegistryContentId); RequireHash(data.sourceRegistryFingerprint);
            ReadFields(data.canonicalInputJson); RequireHash(data.inputFingerprint);
            if (Hash(data.canonicalInputJson) != data.inputFingerprint) throw Bad("Frozen Gem input changed.");
            var normal = new HashSet<string>(StringComparer.Ordinal); var outcomes = new HashSet<string>(StringComparer.Ordinal);
            var identities = new Dictionary<string, string>(StringComparer.Ordinal);
            var seals = new Dictionary<string, string>(StringComparer.Ordinal);
            var projectIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var choice in data.choices)
            {
                if (choice == null || choice.hostIds == null || choice.hostIds.Any(id => id <= 0) ||
                    choice.hostIds.Distinct().Count() != choice.hostIds.Count) throw Bad("Invalid Gem physical hosts.");
                RequireId(choice.gemContentId); RequireId(choice.gemSourceAssetId); RequireText(choice.evolutionOutcomeContentId); RequireSeal(choice.seal);
                if (identities.TryGetValue(choice.gemContentId, out string prior) && prior != choice.gemSourceAssetId ||
                    identities.Any(pair => pair.Value == choice.gemSourceAssetId && pair.Key != choice.gemContentId)) throw Bad("Gem source aliases disagree.");
                identities[choice.gemContentId] = choice.gemSourceAssetId;
                string specification = Write(choice.seal);
                if (seals.TryGetValue(choice.gemContentId, out string priorSeal) && priorSeal != specification ||
                    projectIds.TryGetValue(choice.seal.contentId, out string priorGem) && priorGem != choice.gemContentId)
                    throw Bad("Gem and project seal identities are not one-to-one complete specifications.");
                seals[choice.gemContentId] = specification; projectIds[choice.seal.contentId] = choice.gemContentId;
                bool evo = choice.seal.effect == CardSealEffect.Evolution;
                if (evo != (choice.evolutionOutcomeContentId.Length > 0) ||
                    evo && !outcomes.Add(choice.evolutionOutcomeContentId) || !evo && !normal.Add(choice.gemSourceAssetId))
                    throw Bad("Gem occurrence identity or outcome is duplicated.");
            }
            var frequencies = new HashSet<string>(StringComparer.Ordinal); int count = 0;
            foreach (var expansion in data.expansions)
            {
                if (expansion == null || !frequencies.Add(expansion.frequencyContentId) || expansion.countAfter < count ||
                    expansion.countAfter > data.requestedCount) throw Bad("Invalid ordered Gem expansion proof.");
                RequireId(expansion.frequencyContentId); count = expansion.countAfter;
            }
        }
        internal static Dictionary<string, string> ReadFields(string raw)
        {
            RequireText(raw); int index = 0; White(raw, ref index);
            if (index >= raw.Length || raw[index++] != '{') throw Bad("Gem wire requires an object.");
            var fields = new Dictionary<string, string>(StringComparer.Ordinal); White(raw, ref index);
            while (index < raw.Length && raw[index] != '}')
            {
                if (raw[index++] != '"') throw Bad("Gem wire requires literal field names.");
                int start = index; while (index < raw.Length && raw[index] != '"')
                { if (raw[index] == '\\' || raw[index] < 32) throw Bad("Escaped Gem field names are forbidden."); index++; }
                if (index >= raw.Length) throw Bad("Invalid Gem field name.");
                string name = raw.Substring(start, index++ - start); White(raw, ref index);
                if (index >= raw.Length || raw[index++] != ':' || fields.ContainsKey(name)) throw Bad("Duplicate or invalid Gem field.");
                White(raw, ref index); start = index;
                // The shared scanner determines exact value boundaries and rejects unbalanced arrays/objects.
                if (!JsonObjectShape.TryGetField(raw, name, out string value)) throw Bad("Malformed Gem field value.");
                index += value.Length; fields.Add(name, value); White(raw, ref index);
                if (index < raw.Length && raw[index] == ',') { index++; White(raw, ref index); if (index < raw.Length && raw[index] == '}') throw Bad("Trailing Gem comma."); }
                else break;
            }
            if (index >= raw.Length || raw[index++] != '}') throw Bad("Invalid Gem object ending.");
            White(raw, ref index); if (index != raw.Length) throw Bad("Trailing Gem wire data."); return fields;
        }
        private static void RequireShape(string raw, Type type, bool nullable, int version = 3)
        {
            if (raw == "null") { if (nullable) return; throw Bad("Missing Gem wire value."); }
            if (type == typeof(string))
            {
                if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"' || raw.Any(c => c < 32)) throw Bad("Invalid Gem wire string.");
                return;
            }
            if (type == typeof(bool)) { if (raw != "true" && raw != "false") throw Bad("Invalid Gem bool."); return; }
            if (type == typeof(int) || type.IsEnum)
            { if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) || number.ToString(CultureInfo.InvariantCulture) != raw) throw Bad("Invalid exact Gem integer."); return; }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!JsonObjectShape.TryGetArrayItems(raw, out var items)) throw Bad("Gem wire requires an explicit array.");
                foreach (string item in items) RequireShape(item, type.GetGenericArguments()[0], false, version); return;
            }
            var actual = ReadFields(raw); FieldInfo[] expected = type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => HasWireField(type, field.Name, version)).ToArray();
            if (actual.Count != expected.Length) throw Bad("Gem wire fields are missing or unknown.");
            foreach (var field in expected)
            {
                if (!actual.TryGetValue(field.Name, out string value)) throw Bad("Missing Gem field " + field.Name);
                RequireShape(value, field.FieldType, field.FieldType == typeof(GemChestCardData) ||
                    version != 1 && (field.FieldType == typeof(GemChestPaymentData) || field.FieldType == typeof(GemChestPreparedData)), version);
            }
        }
        private static void WriteValue(StringBuilder output, object value, int version = 3)
        {
            if (value == null) { output.Append("null"); return; }
            if (value is string text)
            {
                RequireText(text); output.Append('"'); foreach (char c in text)
                { if (c == '"' || c == '\\') output.Append('\\').Append(c); else if (c < 32) output.Append("\\u").Append(((int)c).ToString("x4")); else output.Append(c); }
                output.Append('"'); return;
            }
            if (value is bool boolean) { output.Append(boolean ? "true" : "false"); return; }
            if (value is int || value.GetType().IsEnum) { output.Append(Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture)); return; }
            if (value is IList list)
            { output.Append('['); for (int i = 0; i < list.Count; i++) { if (i > 0) output.Append(','); WriteValue(output, list[i], version); } output.Append(']'); return; }
            output.Append('{'); var fields = value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(field => HasWireField(value.GetType(), field.Name, version))
                .OrderBy(f => f.Name).ToArray();
            for (int i = 0; i < fields.Length; i++) { if (i > 0) output.Append(','); WriteValue(output, fields[i].Name, version); output.Append(':'); WriteValue(output, fields[i].GetValue(value), version); }
            output.Append('}');
        }
        private static bool HasWireField(Type type, string field, int version)
            => !(version == 1 && type == typeof(GemChestEntryData) && (field == "active" || field == "payment")) &&
               !(version < 3 && type == typeof(GemChestEntryData) && (field == "physicalProofVersion" || field == "hostCards")) &&
               !(version < 3 && type == typeof(GemChestCardData) && (field == "lineageVersion" || field == "lineageState" ||
                   field == "baseAuthorJson" || field == "currentAuthorJson"));
        private static void MigrateCard(GemChestCardData card)
        {
            if (card == null) return;
            var migrated = DungeonRunCardCodec.MigrateLegacy(new DungeonRunCardSaveData { contentId = card.contentId,
                runtimeInstanceId = card.runtimeInstanceId, limitBreakCount = card.limitBreakCount,
                hasGemSlotCapacity = card.hasGemSlotCapacity, gemSlotCapacity = card.gemSlotCapacity,
                seals = card.seals.Select(seal => seal.Clone()).ToList() });
            card.lineageVersion = migrated.lineageVersion; card.lineageState = migrated.lineageState;
            card.baseAuthorJson = migrated.baseAuthorJson; card.currentAuthorJson = migrated.currentAuthorJson;
        }
        internal static GemChestCardData CaptureCard(DungeonRunCardSaveData card)
        {
            var copy = DungeonRunCardCodec.Clone(card);
            return new GemChestCardData { contentId = copy.contentId, runtimeInstanceId = copy.runtimeInstanceId,
                limitBreakCount = copy.limitBreakCount, hasGemSlotCapacity = copy.hasGemSlotCapacity,
                gemSlotCapacity = copy.gemSlotCapacity, seals = copy.seals, lineageVersion = copy.lineageVersion,
                lineageState = copy.lineageState, baseAuthorJson = copy.baseAuthorJson, currentAuthorJson = copy.currentAuthorJson };
        }
        internal static DungeonRunCardSaveData RestoreCard(GemChestCardData card)
            => DungeonRunCardCodec.Clone(new DungeonRunCardSaveData { contentId = card.contentId,
                runtimeInstanceId = card.runtimeInstanceId, limitBreakCount = card.limitBreakCount,
                hasGemSlotCapacity = card.hasGemSlotCapacity, gemSlotCapacity = card.gemSlotCapacity,
                seals = card.seals, lineageVersion = card.lineageVersion, lineageState = card.lineageState,
                baseAuthorJson = card.baseAuthorJson, currentAuthorJson = card.currentAuthorJson });
        private static void White(string text, ref int index) { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
        internal static InvalidOperationException Bad(string message) => new InvalidOperationException("Run Gem chest: " + message);
    }
}
