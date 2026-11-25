using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the historical Offering save format: explicit wire evidence, legacy
    /// envelope migration, detached DTO copies, ordered fingerprints and local
    /// payout arithmetic. It neither owns a live journal nor consults Run state,
    /// account eligibility, content, a floor directory or RNG. The Offering
    /// lifecycle performs those cross-checks after Read returns a detached body.
    /// </summary>
    internal static class DungeonRunCardOfferingSaveCodec
    {
        internal const int CurrentVersion = 3;
        internal static DungeonRunChickenOfferingSaveData Read(DungeonRunSaveData data, string payload)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion < 25)
            {
                if (data.hasChickenOfferingState || data.chickenOffering?.hasPendingChoice == true ||
                    data.chickenOffering?.completed?.Count > 0 || data.chickenOffering?.version > 1 ||
                    (data.chickenOffering?.pending?.kind ?? 0) != 0)
                    throw Invalid("Legacy schema cannot carry Offering proof.");
                return CreateEmpty();
            }
            if (!data.hasChickenOfferingState || data.chickenOffering == null)
                throw Invalid("Schema 25 requires explicit Offering state.");
            bool legacy = data.schemaVersion < 27;
            int requiredVersion = legacy ? 1 : data.schemaVersion < 38 ? 2 : CurrentVersion;
            ValidateWire(data.chickenOffering, payload, requiredVersion);
            var state = Clone(data.chickenOffering);
            if (state.version != requiredVersion) throw Invalid("Run and Offering envelope versions disagree.");
            // Candidate hashes in old saves describe six physical fields only. Validate
            // those exact bytes before marking this evidence as explicitly legacy.
            ValidateLocal(state, requiredVersion);
            if (legacy)
            {
                if (state.version != 1) throw Invalid("Legacy Run cannot carry typed Offering envelope two.");
                RequireLegacyOffer(state.pending);
                foreach (var decision in state.completed)
                {
                    RequireLegacyOffer(decision.offer);
                    if (!EmptyManaProof(decision.manaReward)) throw Invalid("Legacy Run contains Mana Offering reward proof.");
                    decision.offer.kind = CardOfferingKind.Chicken;
                }
                if (state.hasPendingChoice) state.pending.kind = CardOfferingKind.Chicken;
            }
            if (requiredVersion < CurrentVersion)
            {
                foreach (var offer in state.completed.Select(item => item.offer).Concat(new[] { state.pending }))
                {
                    foreach (var candidate in offer.candidates)
                    { candidate.physicalProofVersion = 1; candidate.cardStateJson = string.Empty; }
                    offer.candidateFingerprint = CandidateFingerprint(offer.candidates);
                }
                state.version = CurrentVersion;
            }
            ValidateLocal(state);
            return state;
        }
        private static void RequireLegacyOffer(DungeonRunChickenOfferingPendingSaveData offer)
        {
            if (offer == null || offer.kind != 0 || (!string.IsNullOrEmpty(offer.sourceEventId) && offer.sourceEventId != ChickenOfferingContracts.SourceEventId))
                throw Invalid("Legacy Offering carries a new typed payout identity.");
        }
        private static bool EmptyManaProof(DungeonRunManaOfferingRewardSaveData reward)
            => reward == null || (reward.currentBefore == 0 && reward.currentAfter == 0 && reward.baseBonusBefore == 0 &&
                reward.baseBonusAfter == 0 && reward.requestedManaAmount == 0);

        internal static void ValidateLocal(DungeonRunChickenOfferingSaveData state, int requiredVersion = CurrentVersion)
        {
            if (state == null || state.version != requiredVersion || state.pending == null || state.completed == null)
                throw Invalid("Offering state envelope is missing or unsupported.");
            var keys = new HashSet<string>(StringComparer.Ordinal); var consumed = new HashSet<int>();
            foreach (var decision in state.completed)
            {
                if (decision == null) throw Invalid("Null Offering decision.");
                ValidateOffer(decision.offer, requiredVersion);
                var selected = decision.offer.candidates.SingleOrDefault(c => c.runtimeInstanceId == decision.selectedRuntimeInstanceId);
                if (selected == null || selected.contentId != decision.selectedContentId || !consumed.Add(decision.selectedRuntimeInstanceId) ||
                    !keys.Add(decision.offer.floorContentId + "\n" + decision.offer.effectId))
                    throw Invalid("Offering exact consumption proof is invalid.");
                if (decision.offer.kind == CardOfferingKind.Chicken || requiredVersion == 1 && decision.offer.kind == 0)
                {
                    if (!EmptyManaProof(decision.manaReward) ||
                        decision.healingPerFood != ChickenOfferingContracts.HealingPerFood(decision.experimentalMedicineEnabled) ||
                        decision.actualHealing == null || decision.actualHealing.Count != ChickenOfferingContracts.FoodCount ||
                        decision.actualHealing.Any(a => a < 0 || a > decision.healingPerFood) ||
                        decision.maxHealthAtCommit <= 0 || decision.valueBefore < 0 || decision.valueBefore > decision.maxHealthAtCommit ||
                        decision.valueAfter < decision.valueBefore || decision.valueAfter > decision.maxHealthAtCommit ||
                        (long)decision.valueAfter - decision.valueBefore != decision.actualHealing.Sum())
                        throw Invalid("Offering consumption/healing proof is invalid.");
                    int current = decision.valueBefore;
                    foreach (int actual in decision.actualHealing)
                    {
                        int expected = Math.Min(decision.healingPerFood, decision.maxHealthAtCommit - current);
                        if (actual != expected) throw Invalid("Offering portions do not match sequential clamped healing.");
                        current += actual;
                    }
                    if (current != decision.valueAfter) throw Invalid("Offering final health does not match its portions.");
                }
                else
                {
                    var reward = decision.manaReward;
                    if (decision.experimentalMedicineEnabled || decision.healingPerFood != 0 || decision.maxHealthAtCommit != 0 ||
                        decision.actualHealing == null || decision.actualHealing.Count != 0 || reward == null ||
                        reward.requestedManaAmount != ManaOfferingContracts.ManaAmount || reward.currentBefore < 0 || reward.baseBonusBefore < 0 ||
                        reward.currentAfter != (int)Math.Min(DungeonRunManaStatues.SourceMaximumMana, (long)reward.currentBefore + ManaOfferingContracts.ManaAmount) ||
                        reward.baseBonusAfter != (int)Math.Min(int.MaxValue, (long)reward.baseBonusBefore + ManaOfferingContracts.ManaAmount) ||
                        decision.valueBefore != reward.currentBefore || decision.valueAfter != reward.currentAfter)
                        throw Invalid("Offering Mana current/base/requested pickup proof is invalid.");
                }
            }
            if (state.hasPendingChoice)
            {
                ValidateOffer(state.pending, requiredVersion);
                if (keys.Contains(state.pending.floorContentId + "\n" + state.pending.effectId) ||
                    state.pending.candidates.Any(c => consumed.Contains(c.runtimeInstanceId))) throw Invalid("Pending Offering conflicts with a completed decision.");
            }
            else if (state.pending.kind != 0 || !string.IsNullOrEmpty(state.pending.floorContentId) || !string.IsNullOrEmpty(state.pending.nodeContentId) ||
                !string.IsNullOrEmpty(state.pending.effectId) || !string.IsNullOrEmpty(state.pending.sourceEventId) ||
                state.pending.candidates == null || state.pending.candidates.Count != 0 ||
                state.pending.candidateFingerprint != CandidateFingerprint(state.pending.candidates, requiredVersion)) throw Invalid("Inactive Offering hides pending progress.");
        }
        internal static void ValidateOffer(DungeonRunChickenOfferingPendingSaveData offer, int requiredVersion = CurrentVersion)
        {
            if (offer == null || !Stable(offer.floorContentId) || !Stable(offer.nodeContentId) || !Stable(offer.effectId) ||
                ((offer.kind != CardOfferingKind.Chicken && offer.kind != CardOfferingKind.Mana && !(requiredVersion == 1 && offer.kind == 0)) ||
                 offer.sourceEventId != (offer.kind == CardOfferingKind.Chicken || requiredVersion == 1 ? ChickenOfferingContracts.SourceEventId : ManaOfferingContracts.SourceEventId)) || offer.candidates == null) throw Invalid("Frozen Offering is invalid.");
            var ids = new HashSet<int>();
            foreach (var c in offer.candidates)
            {
                if (c == null || c.runtimeInstanceId <= 0 || !ids.Add(c.runtimeInstanceId) || !Stable(c.contentId) ||
                    c.cardFingerprint == null || c.cardFingerprint.Length != 64 || c.cardFingerprint.Any(ch => !"0123456789abcdef".Contains(ch)))
                    throw Invalid("Frozen Offering candidate is invalid.");
                var admission = new CardOfferingAdmission(CardOfferingAdmissionState.Allowed, c.sourceConfigId, c.sourceClass, c.rawAllowInOfferingTable);
                if (!admission.IsAllowed) throw Invalid("Candidate lacks source admission.");
                if (requiredVersion < CurrentVersion)
                {
                    if (c.physicalProofVersion != 0 || !string.IsNullOrEmpty(c.cardStateJson))
                        throw Invalid("Legacy candidate hides future physical evidence.");
                }
                else if (c.physicalProofVersion == 1)
                {
                    if (c.cardStateJson != string.Empty) throw Invalid("Legacy candidate cannot invent an author snapshot.");
                }
                else if (c.physicalProofVersion == 2)
                {
                    var card = DungeonRunCardCodec.Parse(c.cardStateJson);
                    if (card.contentId != c.contentId ||
                        card.runtimeInstanceId != c.runtimeInstanceId || DungeonRunCardCodec.Fingerprint(card) != c.cardFingerprint)
                        throw Invalid("Full candidate and frozen physical evidence differ.");
                }
                else throw Invalid("Unknown Offering physical proof version.");
            }
            if (offer.candidateFingerprint != CandidateFingerprint(offer.candidates, requiredVersion))
                throw Invalid("Frozen Offering candidate list differs from its ordered proof.");
        }

        private static void ValidateWire(DungeonRunChickenOfferingSaveData state, string payload, int requiredVersion)
        {
            if (!JsonObjectShape.TryGetField(payload, "hasChickenOfferingState", out var presence) || presence != "true" ||
                !JsonObjectShape.TryGetField(payload, "chickenOffering", out var raw) || raw == "null") throw Invalid("Explicit Offering wire state is missing.");
            RequireClosedShape(raw, typeof(DungeonRunChickenOfferingSaveData), requiredVersion);
            bool legacy = requiredVersion == 1;
            RequireInt(raw, "version", state.version); RequireBool(raw, "hasPendingChoice", state.hasPendingChoice);
            if (!JsonObjectShape.TryGetField(raw, "pending", out var pending) || pending == "null") throw Invalid("Pending envelope is missing.");
            RequireOfferWire(pending, state.pending, requiredVersion);
            var completed = RequireArray(raw, "completed", state.completed?.Count ?? -1);
            for (int i = 0; i < completed.Count; i++)
            {
                var item = state.completed[i]; if (item == null) throw Invalid("Null decision.");
                if (!JsonObjectShape.TryGetField(completed[i], "offer", out var offer) || offer == "null") throw Invalid("Decision offer is missing.");
                RequireOfferWire(offer, item.offer, requiredVersion);
                RequireStrings(completed[i], "selectedContentId"); RequireInt(completed[i], "selectedRuntimeInstanceId", item.selectedRuntimeInstanceId);
                if (!legacy)
                {
                    if (!JsonObjectShape.TryGetField(completed[i], "manaReward", out string reward)) throw Invalid("Typed payout wire is missing.");
                    if (item.offer.kind == CardOfferingKind.Mana)
                    {
                        if (item.manaReward == null || reward == "null") throw Invalid("Mana Offering reward wire is missing.");
                        RequireInt(reward, "requestedManaAmount", item.manaReward.requestedManaAmount);
                        RequireInt(reward, "currentBefore", item.manaReward.currentBefore); RequireInt(reward, "currentAfter", item.manaReward.currentAfter);
                        RequireInt(reward, "baseBonusBefore", item.manaReward.baseBonusBefore); RequireInt(reward, "baseBonusAfter", item.manaReward.baseBonusAfter);
                    }
                }
                RequireInt(completed[i], "healingPerFood", item.healingPerFood); RequireInt(completed[i], "valueBefore", item.valueBefore);
                RequireInt(completed[i], "maxHealthAtCommit", item.maxHealthAtCommit);
                RequireInt(completed[i], "valueAfter", item.valueAfter); RequireBool(completed[i], "experimentalMedicineEnabled", item.experimentalMedicineEnabled);
                var actual = RequireArray(completed[i], "actualHealing", item.actualHealing?.Count ?? -1);
                for (int j = 0; j < actual.Count; j++)
                    if (!int.TryParse(actual[j], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n != item.actualHealing[j])
                        throw Invalid("Healing portion wire value is invalid.");
            }
        }
        private static void RequireOfferWire(string raw, DungeonRunChickenOfferingPendingSaveData offer, int requiredVersion)
        {
            if (offer == null) throw Invalid("Offer envelope is missing.");
            if (requiredVersion != 1) RequireInt(raw, "kind", (int)offer.kind);
            RequireStrings(raw, "floorContentId", "nodeContentId", "effectId", "sourceEventId", "candidateFingerprint");
            var items = RequireArray(raw, "candidates", offer.candidates?.Count ?? -1);
            for (int i = 0; i < items.Count; i++)
            {
                var c = offer.candidates[i]; if (c == null) throw Invalid("Null frozen candidate.");
                RequireStrings(items[i], "contentId", "cardFingerprint", "sourceConfigId", "sourceClass");
                RequireInt(items[i], "runtimeInstanceId", c.runtimeInstanceId); RequireInt(items[i], "rawAllowInOfferingTable", c.rawAllowInOfferingTable);
                if (requiredVersion == CurrentVersion)
                {
                    RequireInt(items[i], "physicalProofVersion", c.physicalProofVersion); RequireStrings(items[i], "cardStateJson");
                    if (!JsonObjectShape.TryGetField(items[i], "cardStateJson", out var carrier) || carrier == "null")
                        throw Invalid("A physical author carrier must be an explicit string.");
                }
            }
        }
        // The scanner is shared; the allowed DTO closure and version-specific fields
        // are owned here. No value supplied by a save selects a CLR type.
        private static void RequireClosedShape(string raw, Type type, int version)
        {
            if (raw == "null")
            {
                if (type == typeof(string) || type == typeof(DungeonRunManaOfferingRewardSaveData)) return;
                throw Invalid("Missing Offering wire object.");
            }
            if (type == typeof(string))
            { if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"') throw Invalid("Invalid Offering string."); return; }
            if (type == typeof(bool))
            { if (raw != "true" && raw != "false") throw Invalid("Invalid Offering bool."); return; }
            if (type == typeof(int) || type.IsEnum)
            {
                if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) || n.ToString(CultureInfo.InvariantCulture) != raw)
                    throw Invalid("Invalid exact Offering integer.");
                return;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!JsonObjectShape.TryGetArrayItems(raw, out var items)) throw Invalid("Explicit Offering array is required.");
                foreach (var item in items) RequireClosedShape(item, type.GetGenericArguments()[0], version);
                return;
            }
            if (type != typeof(DungeonRunChickenOfferingSaveData) && type != typeof(DungeonRunChickenOfferingPendingSaveData) &&
                type != typeof(DungeonRunChickenOfferingCandidateSaveData) && type != typeof(DungeonRunChickenOfferingCompletedSaveData) &&
                type != typeof(DungeonRunManaOfferingRewardSaveData)) throw Invalid("Unknown Offering DTO.");
            var actual = GemChestRunCodec.ReadFields(raw);
            var expected = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(field =>
                !(version == 1 && (field.Name == "kind" && type == typeof(DungeonRunChickenOfferingPendingSaveData) ||
                    field.Name == "manaReward" && type == typeof(DungeonRunChickenOfferingCompletedSaveData))) &&
                !(version < CurrentVersion && type == typeof(DungeonRunChickenOfferingCandidateSaveData) &&
                    (field.Name == "physicalProofVersion" || field.Name == "cardStateJson"))).ToArray();
            if (actual.Count != expected.Length) throw Invalid("Missing or future Offering wire field.");
            foreach (var field in expected)
            {
                if (!actual.TryGetValue(field.Name, out var value)) throw Invalid("Missing Offering field: " + field.Name);
                RequireClosedShape(value, field.FieldType, version);
            }
        }
        private static void RequireStrings(string raw, params string[] fields)
        {
            foreach (var field in fields)
                if (!JsonObjectShape.TryGetField(raw, field, out string value) ||
                    (value != "null" && (value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"')))
                    throw Invalid("Offering wire string is missing or invalid: " + field);
        }
        private static IReadOnlyList<string> RequireArray(string raw, string field, int count)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out var array) || !JsonObjectShape.TryGetArrayItems(array, out var items) || items.Count != count)
                throw Invalid("Offering wire array is missing or inconsistent: " + field);
            return items;
        }
        private static void RequireInt(string raw, string field, int expected)
        {
            if (!JsonObjectShape.TryGetField(raw, field, out var value) || !int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed) || parsed != expected || parsed.ToString(CultureInfo.InvariantCulture) != value) throw Invalid("Offering wire integer is missing: " + field);
        }
        private static void RequireBool(string raw, string field, bool expected)
        { if (!JsonObjectShape.TryGetField(raw, field, out var value) || value != (expected ? "true" : "false")) throw Invalid("Offering wire boolean is missing: " + field); }

        internal static DungeonRunChickenOfferingCandidateSaveData SaveCandidate(DungeonRunCardSaveData card, CardOfferingAdmission admission)
            => new DungeonRunChickenOfferingCandidateSaveData { runtimeInstanceId = card.runtimeInstanceId, contentId = card.contentId,
                cardFingerprint = DungeonRunCardCodec.Fingerprint(card), sourceConfigId = admission.SourceConfigId, sourceClass = admission.SourceClass,
                physicalProofVersion = 2, cardStateJson = DungeonRunCardCodec.CanonicalJson(card),
                rawAllowInOfferingTable = admission.RawAllowInOfferingTable };
        internal static bool MatchesCard(DungeonRunChickenOfferingCandidateSaveData candidate, DungeonRunCardSaveData card)
            => card != null && candidate.contentId == card.contentId && candidate.runtimeInstanceId == card.runtimeInstanceId &&
                candidate.cardFingerprint == (candidate.physicalProofVersion == 1 ? CardFingerprint(card) : DungeonRunCardCodec.Fingerprint(card));
        internal static string CardFingerprint(DungeonRunCardSaveData card)
        {
            var parts = new List<string> { card.contentId ?? string.Empty, card.runtimeInstanceId.ToString(CultureInfo.InvariantCulture),
                card.limitBreakCount.ToString(CultureInfo.InvariantCulture), card.hasGemSlotCapacity ? "1" : "0",
                card.gemSlotCapacity.ToString(CultureInfo.InvariantCulture) };
            foreach (var seal in card.seals ?? new List<CardSealSpec>())
            {
                if (seal == null) throw Invalid("Null seal in frozen Run card.");
                parts.Add(seal.contentId ?? string.Empty); parts.Add(((int)seal.effect).ToString(CultureInfo.InvariantCulture));
                parts.Add(seal.value.ToString(CultureInfo.InvariantCulture)); parts.Add(seal.comboScalable ? "1" : "0");
            }
            return FingerprintParts(parts);
        }
        // Preserves the frozen list, including an originally empty offer, without
        // re-querying a changing admission pool on restore. This is an integrity
        // proof for structural drift, not authentication of an untrusted save.
        internal static string CandidateFingerprint(IReadOnlyList<DungeonRunChickenOfferingCandidateSaveData> candidates, int version = CurrentVersion)
        {
            var parts = new List<string> { candidates.Count.ToString(CultureInfo.InvariantCulture) };
            foreach (var candidate in candidates)
            {
                parts.Add(candidate.runtimeInstanceId.ToString(CultureInfo.InvariantCulture));
                parts.Add(candidate.contentId ?? string.Empty); parts.Add(candidate.cardFingerprint ?? string.Empty);
                parts.Add(candidate.sourceConfigId ?? string.Empty); parts.Add(candidate.sourceClass ?? string.Empty);
                parts.Add(candidate.rawAllowInOfferingTable.ToString(CultureInfo.InvariantCulture));
                if (version == CurrentVersion)
                { parts.Add(candidate.physicalProofVersion.ToString(CultureInfo.InvariantCulture)); parts.Add(candidate.cardStateJson ?? string.Empty); }
            }
            return FingerprintParts(parts);
        }
        private static string FingerprintParts(IEnumerable<string> parts)
        {
            string canonical = string.Concat(parts.Select(p => p.Length.ToString(CultureInfo.InvariantCulture) + ":" + p));
            using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
        private static bool Stable(string id) => !string.IsNullOrWhiteSpace(id) && id == id.Trim();
        private static InvalidOperationException Invalid(string text) => new InvalidOperationException("Run ChickenOffering: " + text);
        internal static DungeonRunChickenOfferingSaveData CreateEmpty() => new DungeonRunChickenOfferingSaveData
            { version = CurrentVersion, pending = CreateEmptyPending(), completed = new List<DungeonRunChickenOfferingCompletedSaveData>() };
        internal static DungeonRunChickenOfferingPendingSaveData CreateEmptyPending()
        {
            var candidates = new List<DungeonRunChickenOfferingCandidateSaveData>();
            return new DungeonRunChickenOfferingPendingSaveData { candidates = candidates, candidateFingerprint = CandidateFingerprint(candidates) };
        }
        internal static DungeonRunChickenOfferingSaveData Clone(DungeonRunChickenOfferingSaveData source)
            => JsonUtility.FromJson<DungeonRunChickenOfferingSaveData>(JsonUtility.ToJson(source));
        internal static DungeonRunChickenOfferingPendingSaveData ClonePending(DungeonRunChickenOfferingPendingSaveData source)
            => JsonUtility.FromJson<DungeonRunChickenOfferingPendingSaveData>(JsonUtility.ToJson(source));
    }
    [Serializable] internal sealed class DungeonRunChickenOfferingSaveData
    {
        public int version;
        public bool hasPendingChoice;
        public DungeonRunChickenOfferingPendingSaveData pending;
        public List<DungeonRunChickenOfferingCompletedSaveData> completed;
    }
    [Serializable] internal sealed class DungeonRunChickenOfferingPendingSaveData
    {
        public CardOfferingKind kind;
        public string floorContentId;
        public string nodeContentId;
        public string effectId;
        public string sourceEventId;
        public string candidateFingerprint;
        public List<DungeonRunChickenOfferingCandidateSaveData> candidates;
    }
    [Serializable] internal sealed class DungeonRunChickenOfferingCandidateSaveData
    {
        public int runtimeInstanceId;
        public string contentId;
        public string cardFingerprint;
        public string sourceConfigId;
        public string sourceClass;
        public int rawAllowInOfferingTable;
        public int physicalProofVersion;
        public string cardStateJson = string.Empty;
    }
    [Serializable] internal sealed class DungeonRunChickenOfferingCompletedSaveData
    {
        public DungeonRunChickenOfferingPendingSaveData offer;
        public int selectedRuntimeInstanceId;
        public string selectedContentId;
        public bool experimentalMedicineEnabled;
        public int healingPerFood;
        public int maxHealthAtCommit;
        public List<int> actualHealing;
        public int valueBefore;
        public int valueAfter;
        public DungeonRunManaOfferingRewardSaveData manaReward;
    }
    [Serializable] internal sealed class DungeonRunManaOfferingRewardSaveData
    {
        public int currentBefore;
        public int currentAfter;
        public int baseBonusBefore;
        public int baseBonusAfter;
        public int requestedManaAmount;
    }
}
