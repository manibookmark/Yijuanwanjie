using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Frozen input and recomputed output of the bounded original-effect projection.
    /// Capture reads the immutable author template, never today's physical facade.
    /// Parse uses only frozen inputs and the same projection profiles, without a
    /// catalog lookup or any live card/stat mutation. It freezes every field that
    /// projection reads; damage payloads, combo settings, visuals and conditions'
    /// contents are outside this projection (only condition count is consulted).
    /// It is neither a complete card save nor proof of admission to a Run pool.
    /// A fingerprint proves consistency, not authenticity of saved author claims;
    /// the payment journal must separately bind full base/current authors, physical
    /// identity and pool policy. V2 separates the original author's stable ID from
    /// the physical current definition. V1 retains its historical same-ID contract.
    /// </summary>
    public sealed class SideEventCardStatProjectionProof
    {
        public const int CurrentFormatVersion = 2;
        // Validate DTO metadata only when explicitly capturing or parsing a proof.
        // Domain reload must not execute reflection work for the offering journal.
        private static ExactJsonWire Wire => new ExactJsonWire(
            typeof(CardStatProjectionProofData), typeof(CardStatProjectionProofV2Data),
            typeof(CardStatProjectionInputData),
            typeof(CardStatProjectionEffectData), typeof(CardStatProjectionTupleData));

        public int FormatVersion { get; }
        public string OriginalCardContentId { get; }
        public string CanonicalJson { get; }
        public string Fingerprint { get; }
        public SideEventCardStatProjection Projection { get; }

        private SideEventCardStatProjectionProof(int formatVersion, string canonicalJson,
            string originalCardContentId, SideEventCardStatProjection projection)
        {
            FormatVersion = formatVersion;
            OriginalCardContentId = originalCardContentId;
            Projection = projection;
            CanonicalJson = canonicalJson;
            Fingerprint = SideEventQualificationCheckpoint.Identity(writer =>
            {
                writer.Write("card-stat-projection-proof/v" + formatVersion.ToString(CultureInfo.InvariantCulture));
                writer.Write(CanonicalJson);
            });
        }

        public static SideEventCardStatProjectionProof Capture(CardConfig physicalCard)
        {
            if (!SideEventCardStatOfferingProjection.TryCaptureOriginal(physicalCard,
                out var projection, out var input, out string originalCardContentId, out string error))
                throw new InvalidOperationException(error);
            var data = new CardStatProjectionProofV2Data
            {
                formatVersion = CurrentFormatVersion, profileContractId = projection.ProfileContractId,
                physicalInstanceId = projection.CardRuntimeInstanceId, cardContentId = projection.CardContentId,
                originalCardContentId = originalCardContentId,
                lifetime = (int)CardLifetime.Persistent,
                admissionState = (int)(projection.RawAllowInOfferingTable == 1 ?
                    CardOfferingAdmissionState.Allowed : CardOfferingAdmissionState.ExcludedByFlag),
                sourceConfigId = projection.SourceConfigId, sourceClass = projection.SourceClass,
                rawAllowInOfferingTable = projection.RawAllowInOfferingTable,
                sourceObjectPathId = projection.SourceObjectPathId, originalInput = input,
                tuples = new List<CardStatProjectionTupleData>()
            };
            foreach (var tuple in projection.ModifiedStats)
                data.tuples.Add(new CardStatProjectionTupleData
                { sourceStat = tuple.SourceStat.Value, amount = tuple.Amount.Value });
            // Capture and recovery share the same closed semantic boundary.
            return Parse(Wire.Write(data));
        }

        /// <summary>Binds a validated projection to the original author's own identity,
        /// source admission and exact projected fields. V1 preserves its same-ID binding;
        /// v2 permits an independently identified current definition. This reads managed
        /// values only. The journal remains responsible for the full physical fingerprint
        /// and both complete author documents; this is not an authenticity assertion.</summary>
        public void RequireMatchesOriginalAuthor(CardAuthorDocument original)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            CardStatProjectionProofData saved = ReadEnvelope(CanonicalJson, out _, out string originalId);
            CardStatProjectionInputData expected = original.ReadProjectionInput(out CardOfferingAdmission admission);
            Require(original.ContentId == originalId && admission.State == (CardOfferingAdmissionState)saved.admissionState &&
                admission.SourceConfigId == saved.sourceConfigId && admission.SourceClass == saved.sourceClass &&
                admission.RawAllowInOfferingTable == saved.rawAllowInOfferingTable,
                "The original author identity or source admission differs from the projection proof.");
            Require(Wire.Write(expected) == Wire.Write(saved.originalInput),
                "The complete original author differs from the exact projected input.");
        }

        public static SideEventCardStatProjectionProof Parse(string canonicalJson)
        {
            var data = ReadEnvelope(canonicalJson, out int formatVersion, out string originalCardContentId);
            Require(data.physicalInstanceId > 0 && data.lifetime == (int)CardLifetime.Persistent,
                "A persistent physical identity is required.");
            Require(!string.IsNullOrWhiteSpace(data.cardContentId), "Card content identity is missing.");
            foreach (char c in data.cardContentId)
                Require(!char.IsWhiteSpace(c) && !char.IsControl(c), "Invalid card content identity.");
            var admission = new CardOfferingAdmission((CardOfferingAdmissionState)data.admissionState,
                data.sourceConfigId, data.sourceClass, data.rawAllowInOfferingTable);
            Require(admission.State != CardOfferingAdmissionState.Unknown && admission.SourceClass == "CardConfig",
                "Known non-FCC original source evidence is required.");
            Require(data.originalInput != null && data.tuples != null, "Original input and tuples are required.");
            CardConfig original = null;
            try
            {
                original = data.originalInput.Instantiate();
                var tuples = SideEventCardStatOfferingProjection.ProjectOriginal(original,
                    admission.SourceConfigId, out long pathId);
                var projection = new SideEventCardStatProjection(data.physicalInstanceId,
                    data.cardContentId, admission, pathId, tuples);
                Require(data.profileContractId == projection.ProfileContractId && data.sourceObjectPathId == pathId,
                    "Captured source profile or path differs from the verified projection.");
                Require(data.tuples.Count == tuples.Length, "Captured projection tuples differ.");
                for (int i = 0; i < tuples.Length; i++)
                    Require(data.tuples[i] != null && data.tuples[i].sourceStat == tuples[i].SourceStat &&
                        data.tuples[i].amount == tuples[i].Amount, "Captured projection tuple changed.");
                return new SideEventCardStatProjectionProof(formatVersion, canonicalJson,
                    originalCardContentId, projection);
            }
            finally
            {
                if (original != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(original);
                    else UnityEngine.Object.DestroyImmediate(original);
                }
            }
        }

        private static CardStatProjectionProofData ReadEnvelope(string canonicalJson,
            out int formatVersion, out string originalCardContentId)
        {
            var wire = Wire;
            CardStatProjectionProofV2Data modern;
            try { modern = wire.Read<CardStatProjectionProofV2Data>(canonicalJson); }
            catch (ArgumentException)
            {
                // Each version has a complete independent field set. Falling back
                // only retries the strict old shape; it cannot ignore new fields.
                var legacy = wire.Read<CardStatProjectionProofData>(canonicalJson);
                Require(legacy != null && legacy.formatVersion == 1,
                    "Unsupported card stat projection proof version or field set.");
                Require(string.Equals(wire.Write(legacy), canonicalJson, StringComparison.Ordinal),
                    "The projection proof must use its canonical wire representation.");
                formatVersion = 1;
                originalCardContentId = legacy.cardContentId;
                return legacy;
            }
            Require(modern != null && modern.formatVersion == CurrentFormatVersion,
                "Unsupported card stat projection proof version or field set.");
            Require(string.Equals(wire.Write(modern), canonicalJson, StringComparison.Ordinal),
                "The projection proof must use its canonical wire representation.");
            Require(!string.IsNullOrWhiteSpace(modern.originalCardContentId),
                "The original author content identity is required.");
            foreach (char c in modern.originalCardContentId)
                Require(!char.IsWhiteSpace(c) && !char.IsControl(c), "Invalid original author content identity.");
            formatVersion = CurrentFormatVersion;
            originalCardContentId = modern.originalCardContentId;
            return modern.ProjectionInput();
        }

        internal static void Require(bool condition, string error)
        { if (!condition) throw new ArgumentException(error); }
    }

    internal sealed class CardStatProjectionProofData
    {
        public int formatVersion = 0;
        public string profileContractId = null;
        public int physicalInstanceId = 0;
        public string cardContentId = null;
        public int lifetime = 0;
        public int admissionState = 0;
        public string sourceConfigId = null;
        public string sourceClass = null;
        public int rawAllowInOfferingTable = 0;
        public long sourceObjectPathId = 0;
        public CardStatProjectionInputData originalInput = null;
        public List<CardStatProjectionTupleData> tuples = null;
    }

    // Separate from the exact v1 DTO so old canonical bytes and closed field
    // admission remain unchanged. Both envelopes use the same projection kernel.
    internal sealed class CardStatProjectionProofV2Data
    {
        public int formatVersion = 0;
        public string profileContractId = null;
        public int physicalInstanceId = 0;
        public string cardContentId = null;
        public string originalCardContentId = null;
        public int lifetime = 0;
        public int admissionState = 0;
        public string sourceConfigId = null;
        public string sourceClass = null;
        public int rawAllowInOfferingTable = 0;
        public long sourceObjectPathId = 0;
        public CardStatProjectionInputData originalInput = null;
        public List<CardStatProjectionTupleData> tuples = null;

        internal CardStatProjectionProofData ProjectionInput()
            => new CardStatProjectionProofData
            {
                formatVersion = formatVersion, profileContractId = profileContractId,
                physicalInstanceId = physicalInstanceId, cardContentId = cardContentId,
                lifetime = lifetime, admissionState = admissionState,
                sourceConfigId = sourceConfigId, sourceClass = sourceClass,
                rawAllowInOfferingTable = rawAllowInOfferingTable,
                sourceObjectPathId = sourceObjectPathId, originalInput = originalInput, tuples = tuples
            };
    }

    internal sealed class CardStatProjectionTupleData
    {
        public int sourceStat = 0;
        public int amount = 0;
    }

    // Detached minimum input. Existing CardBaseTemplate remains the original-card
    // owner; this DTO is never written into a runtime physical card or its template.
    internal sealed class CardStatProjectionInputData
    {
        public int baseDamage = 0;
        public int armor = 0;
        public int heal = 0;
        public int manaGain = 0;
        public int statusEffect = 0;
        public bool destroyOnUse = false;
        public List<CardStatProjectionEffectData> effects = null;

        internal static CardStatProjectionInputData Capture(CardConfig original)
        {
            var data = new CardStatProjectionInputData
            {
                baseDamage = original.baseDamage, armor = original.armor, heal = original.heal,
                manaGain = original.manaGain, statusEffect = (int)original.statusEffect,
                destroyOnUse = original.destroyOnUse, effects = new List<CardStatProjectionEffectData>()
            };
            if (original.effects != null)
                foreach (var effect in original.effects)
                    data.effects.Add(CardStatProjectionEffectData.Capture(effect));
            return data;
        }

        internal CardConfig Instantiate()
        {
            SideEventCardStatProjectionProof.Require(effects != null && Enum.IsDefined(typeof(StatusEffect), statusEffect),
                "Explicit original effects and a known status ID are required.");
            var restoredEffects = new List<CardEffectSpec>();
            foreach (var effect in effects)
            {
                SideEventCardStatProjectionProof.Require(effect != null, "An original effect cannot be null.");
                restoredEffects.Add(effect.Restore());
            }
            // Never instantiate a type named by the wire. Only the projection's
            // known author DTO is materialized, and its caller releases it.
            var card = ScriptableObject.CreateInstance<CardConfig>();
            card.baseDamage = baseDamage; card.armor = armor; card.heal = heal; card.manaGain = manaGain;
            card.statusEffect = (StatusEffect)statusEffect; card.destroyOnUse = destroyOnUse;
            card.effects = restoredEffects;
            return card;
        }
    }

    internal sealed class CardStatProjectionEffectData
    {
        public int effectType = 0;
        public int target = 0;
        public int amount = 0;
        public int count = 0;
        public bool repeatAtPlayerTurnEnd = false;
        public bool replayColumnsAtPlayerTurnEnd = false;
        public int statusId = 0;
        public string chanceBits = null;
        public int duration = 0;
        public int conditionCount = 0;
        public int statType = 0;
        public int costModifier = 0;
        public int randomCostMin = 0;
        public int randomCostMax = 0;
        public bool randomCostRelative = false;
        public string createCardDefinitionId = null;
        public string crawlerContentId = null;
        public bool attackIntentOnly = false;
        public bool removeAllIntents = false;

        internal static CardStatProjectionEffectData Capture(CardEffectSpec effect)
        {
            SideEventCardStatProjectionProof.Require(effect != null, "An original effect cannot be null.");
            return new CardStatProjectionEffectData
            {
                effectType = (int)effect.effectType, target = (int)effect.target, amount = effect.amount,
                count = effect.count, repeatAtPlayerTurnEnd = effect.repeatAtPlayerTurnEnd,
                replayColumnsAtPlayerTurnEnd = effect.replayColumnsAtPlayerTurnEnd, statusId = (int)effect.statusId,
                chanceBits = BitConverter.ToUInt32(BitConverter.GetBytes(effect.chance), 0).ToString("x8", CultureInfo.InvariantCulture),
                duration = effect.duration, conditionCount = effect.conditions?.Count ?? 0,
                statType = (int)effect.statType, costModifier = effect.costModifier,
                randomCostMin = effect.randomCostMin, randomCostMax = effect.randomCostMax,
                randomCostRelative = effect.randomCostRelative, createCardDefinitionId = effect.createCardDefinitionId ?? "",
                crawlerContentId = effect.crawlerContentId ?? "", attackIntentOnly = effect.attackIntentOnly,
                removeAllIntents = effect.removeAllIntents
            };
        }

        internal CardEffectSpec Restore()
        {
            SideEventCardStatProjectionProof.Require(Enum.IsDefined(typeof(EffectType), effectType) &&
                Enum.IsDefined(typeof(TargetRule), target) && Enum.IsDefined(typeof(StatusEffect), statusId) &&
                Enum.IsDefined(typeof(StatType), statType) && conditionCount >= 0 &&
                createCardDefinitionId != null && crawlerContentId != null,
                "Invalid original effect evidence.");
            SideEventCardStatProjectionProof.Require(chanceBits != null && Regex.IsMatch(chanceBits, "^[0-9a-f]{8}$"),
                "An exact canonical binary32 chance is required.");
            uint bits = uint.Parse(chanceBits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            float chance = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
            SideEventCardStatProjectionProof.Require(!float.IsNaN(chance) && !float.IsInfinity(chance),
                "Original effect chance must be finite.");
            var conditions = new List<ConditionRule>();
            // Projection only tests empty versus nonempty. Do not allocate using
            // an untrusted captured count or interpret unknown condition contents.
            if (conditionCount > 0) conditions.Add(null);
            return new CardEffectSpec
            {
                effectType = (EffectType)effectType, target = (TargetRule)target, amount = amount, count = count,
                repeatAtPlayerTurnEnd = repeatAtPlayerTurnEnd, replayColumnsAtPlayerTurnEnd = replayColumnsAtPlayerTurnEnd,
                statusId = (StatusEffect)statusId, chance = chance, duration = duration, conditions = conditions,
                statType = (StatType)statType, costModifier = costModifier, randomCostMin = randomCostMin,
                randomCostMax = randomCostMax, randomCostRelative = randomCostRelative,
                createCardDefinitionId = createCardDefinitionId, crawlerContentId = crawlerContentId,
                attackIntentOnly = attackIntentOnly, removeAllIntents = removeAllIntents
            };
        }
    }
}
