using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>Detached evidence from one physical card's original author template.
    /// CardContentId identifies the physical card's current definition; the original
    /// author can have a different stable identity. Empty ModifiedStats means known
    /// source effects supply no supported tuple; it
    /// does not authorize an offering, install a modifier, or change card admission.</summary>
    public sealed class SideEventCardStatProjection
    {
        public int CardRuntimeInstanceId { get; }
        public string CardContentId { get; }
        public string SourceConfigId { get; }
        public string SourceClass { get; }
        public int RawAllowInOfferingTable { get; }
        public long SourceObjectPathId { get; }
        public bool IsFcc => false;
        public string ProfileContractId => "card-stat-source-effects/v1";
        public IReadOnlyList<SideEventCardModifiedStat> ModifiedStats { get; }

        internal SideEventCardStatProjection(int instanceId, string contentId,
            CardOfferingAdmission admission, long pathId, SideEventCardModifiedStat[] tuples)
        {
            CardRuntimeInstanceId = instanceId; CardContentId = contentId;
            SourceConfigId = admission.SourceConfigId; SourceClass = admission.SourceClass;
            RawAllowInOfferingTable = admission.RawAllowInOfferingTable;
            SourceObjectPathId = pathId;
            var copy = new SideEventCardModifiedStat[tuples.Length];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = new SideEventCardModifiedStat(tuples[i].SourceStat, tuples[i].Amount);
            ModifiedStats = Array.AsReadOnly(copy);
        }
    }

    /// <summary>
    /// Owns the bounded source-effect projection, not a card, reward or progress ledger.
    /// Exact native asset identities select verified profiles; project IDs, names, colors,
    /// costs, draw counts and current player values never infer a source profile.
    /// The immutable CardBaseTemplate is captured before the first instance is bound.
    /// Only that snapshot is inspected: later breakthrough, combo, seals, facade edits
    /// and temporary bonuses cannot change these source base tuples. The temporary
    /// native object is always released; neither the physical card nor template changes.
    ///
    /// Bracer's original one DealCountEffect(1) maps to one Self/ShouPai(1) base effect;
    /// Armor(2)'s ArmorEffect(6) maps to GainArmor(6). Exact amounts and operative effect
    /// shapes are required so a new author mechanic cannot be hidden by fixed source
    /// constants. Damage effects have native ModifiedStat=None; their damage magnitude
    /// does not become a stat tuple. EmptyTome's GainMana(2) also has no ModifiedStat.
    /// Pummarola's native Recovery(8,1) is outside the captured support table. Its current
    /// project Heal(2) is a declared unresolved mechanism difference: only a known empty
    /// supported projection is returned, never a Recovery/healing-derived bonus.
    ///
    /// These eight identities are evidence capabilities. Formal Armor/Pummarola metadata
    /// remains Unknown until Content's author owner explicitly adopts the association.
    /// A valid known flag 0 is not a rejection: this source modal does not use that flag.
    /// This module does not apply the eleven native stats or reproduce their caps/reset.
    /// Reassess the profile boundary before adding an independently translated effect.
    /// </summary>
    public static class SideEventCardStatOfferingProjection
    {
        public static bool TryProject(CardConfig physicalCard,
            out SideEventCardStatProjection projection, out string error)
            => TryProjectCore(physicalCard, false, out projection, out _, out _, out error);

        internal static bool TryCaptureOriginal(CardConfig physicalCard,
            out SideEventCardStatProjection projection, out CardStatProjectionInputData originalInput,
            out string originalCardContentId, out string error)
            => TryProjectCore(physicalCard, true, out projection, out originalInput,
                out originalCardContentId, out error);

        private static bool TryProjectCore(CardConfig physicalCard, bool captureInput,
            out SideEventCardStatProjection projection, out CardStatProjectionInputData originalInput,
            out string originalCardContentId, out string error)
        {
            projection = null; originalInput = null; originalCardContentId = null;
            error = null; CardConfig original = null;
            try
            {
                Require(physicalCard != null && physicalCard.runtimeInstanceId > 0 &&
                    physicalCard.RuntimeInstance != null && !physicalCard.RuntimeInstance.IsDestroyed &&
                    physicalCard.Lifetime == CardLifetime.Persistent,
                    "InvalidPhysicalCard: a live persistent physical instance is required.");
                var instance = physicalCard.RuntimeInstance;
                Require(instance.HasKnownAuthorConfigurations,
                    "LegacyUnknownOriginal: new CardStat capture cannot infer historical authors.");
                Require(instance.BaseTemplate != null,
                    "MissingOriginalTemplate: current physical fields cannot reconstruct provenance.");
                Require(!string.IsNullOrWhiteSpace(instance.DefinitionId) &&
                    string.Equals(instance.DefinitionId, physicalCard.contentId, StringComparison.Ordinal) &&
                    instance.CurrentTemplate != null &&
                    string.Equals(instance.CurrentTemplate.ContentId, instance.DefinitionId, StringComparison.Ordinal),
                    "InvalidPhysicalIdentity: the facade differs from its owned definition ID.");
                original = instance.BaseTemplate.Instantiate();
                Require(!string.IsNullOrWhiteSpace(instance.BaseTemplate.ContentId) &&
                    string.Equals(original.contentId, instance.BaseTemplate.ContentId, StringComparison.Ordinal),
                    "InvalidOriginalIdentity: the original differs from its own frozen author identity.");
                var admission = original.OfferingAdmission;
                string admissionError = admission.Validate();
                Require(admissionError == null, "InvalidSourceIdentityEvidence: " + admissionError);
                Require(admission.State != CardOfferingAdmissionState.Unknown,
                    "UnknownSourceIdentity: no exact source identity/class is adopted.");
                Require(admission.SourceClass == "CardConfig",
                    "UnsupportedFcc: CardStatOffering excludes source FCC cards.");
                var tuples = ProjectOriginal(original, admission.SourceConfigId, out long pathId);
                projection = new SideEventCardStatProjection(instance.InstanceId,
                    instance.DefinitionId, admission, pathId, tuples);
                if (captureInput) originalInput = CardStatProjectionInputData.Capture(original);
                originalCardContentId = instance.BaseTemplate.ContentId;
                return true;
            }
            catch (ArgumentException exception) { error = exception.Message; return false; }
            catch (InvalidOperationException exception) { error = exception.Message; return false; }
            finally
            {
                if (original != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(original);
                    else UnityEngine.Object.DestroyImmediate(original);
                }
            }
        }

        internal static SideEventCardModifiedStat[] ProjectOriginal(CardConfig card,
            string sourceId, out long pathId)
        {
            switch (sourceId)
            {
                case "Card_S_2_Bracer":
                    pathId = 18125;
                    RequireSingle(card, EffectType.ModifyStat, 1, StatType.ShouPai);
                    return new[] { new SideEventCardModifiedStat(10, 1) };
                case "Card_D_2_Armor":
                    pathId = 18075;
                    RequireSingle(card, EffectType.GainArmor, 6);
                    return new[] { new SideEventCardModifiedStat(2, 6) };
                case "Card_B_1_EmptyTome":
                    pathId = 18104; RequireSingle(card, EffectType.GainMana, 2); break;
                case "Card_S_1_Pummarola":
                    pathId = 18122; RequireSingle(card, EffectType.Heal, 2); break;
                case "Card_A_0_Cross": pathId = 18061; RequireDamageOnly(card); break;
                case "Card_A_3_ThousandEdge": pathId = 18088; RequireDamageOnly(card); break;
                case "Card_A_4_LaBorra": pathId = 18090; RequireDamageOnly(card); break;
                case "Card_A_1_Runetracer": pathId = 18057; RequireDamageOnly(card); break;
                default:
                    throw new InvalidOperationException("UnknownSourceProfile: the exact source effect contract is not verified.");
            }
            return Array.Empty<SideEventCardModifiedStat>();
        }

        private static void RequireSingle(CardConfig card, EffectType kind, int amount,
            StatType stat = StatType.FengRui)
        {
            if (card.HasSpecEffects)
            {
                Require(card.effects.Count == 1, "SourceEffectContractMismatch: exactly one original base effect is required.");
                var effect = card.effects[0];
                // damageSpec is operative only for EffectType.Damage. Unity inline JSON can
                // materialize a null payload as an empty object; neither that shell nor an
                // unused populated payload adds a Damage effect to this non-Damage profile.
                Require(effect != null && effect.effectType == kind && effect.amount == amount &&
                    (kind != EffectType.ModifyStat || effect.statType == stat) &&
                    effect.target == TargetRule.Self && effect.count == 1 &&
                    !effect.repeatAtPlayerTurnEnd && !effect.replayColumnsAtPlayerTurnEnd &&
                    effect.statusId == StatusEffect.None && effect.chance == 1f && effect.duration == 0 &&
                    (effect.conditions == null || effect.conditions.Count == 0) && effect.costModifier == 0 &&
                    effect.randomCostMin == 0 && effect.randomCostMax == 0 && !effect.randomCostRelative &&
                    string.IsNullOrEmpty(effect.createCardDefinitionId) && string.IsNullOrEmpty(effect.crawlerContentId) &&
                    !effect.attackIntentOnly && !effect.removeAllIntents,
                    "SourceEffectContractMismatch: original base effect differs from the verified source projection.");
                return;
            }
            Require(kind != EffectType.ModifyStat && card.baseDamage == 0 &&
                card.statusEffect == StatusEffect.None && !card.destroyOnUse &&
                card.armor == (kind == EffectType.GainArmor ? amount : 0) &&
                card.heal == (kind == EffectType.Heal ? amount : 0) &&
                card.manaGain == (kind == EffectType.GainMana ? amount : 0),
                "SourceEffectContractMismatch: legacy base fields differ from the verified source projection.");
        }

        private static void RequireDamageOnly(CardConfig card)
        {
            if (card.HasSpecEffects)
            {
                foreach (var effect in card.effects)
                    Require(effect != null && effect.effectType == EffectType.Damage,
                        "SourceEffectContractMismatch: a native damage-only profile cannot acquire a stat effect.");
                return;
            }
            Require(card.baseDamage > 0 && card.armor == 0 && card.heal == 0 && card.manaGain == 0 &&
                card.statusEffect == StatusEffect.None && !card.destroyOnUse,
                "SourceEffectContractMismatch: native damage-only base fields differ.");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
