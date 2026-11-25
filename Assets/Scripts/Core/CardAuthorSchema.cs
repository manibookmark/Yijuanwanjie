using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>Owns the finite compiled-author schema and typed copying. Reflection is
    /// restricted to this explicit registration at an explicit operation, never a data
    /// supplied CLR type. Unknown serialized fields fail instead of silently disappearing.
    /// Scale review: all mapping, shape checks and reconstruction describe one author
    /// format; no physical lifetime, Run journal or content publication is owned here.
    /// Review this boundary again before adding another author family or format version.</summary>
    internal static class CardAuthorSchema
    {
        private const int Null = 0, Integer = 1, Boolean = 2, Text = 3, FloatBits = 4,
            Node = 5, List = 6, SpriteReference = 7, CardReference = 8;
        private const int MaximumDefinitions = 128;

        private sealed class Contract
        {
            internal readonly Type Type;
            internal readonly string Id;
            internal readonly FieldInfo[] Fields;
            internal Contract(Type type, string id, params string[] names)
            {
                Type = type; Id = id; Fields = new FieldInfo[names.Length];
                var actual = new HashSet<string>(StringComparer.Ordinal);
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField), false) ||
                        field.IsDefined(typeof(SerializeReference), false)))
                        actual.Add(field.Name);
                if (!actual.SetEquals(names))
                    throw new ArgumentException("Unregistered or missing serialized author fields: " + id);
                for (int i = 0; i < names.Length; i++)
                    Fields[i] = type.GetField(names[i], BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly) ?? throw new ArgumentException(names[i]);
            }
        }

        // No schema/reflection initializer runs at assembly load. This exact registry is
        // built only on explicit capture, parse or reconstruction, and closes all paths.
        private static Dictionary<Type, Contract> Contracts()
        {
            var values = new[]
            {
                new Contract(typeof(CardConfig), "card.author.v1", "contentId", "cardName", "description", "detailDescription",
                    "volumeId", "cardArt", "borderArt", "iconArt", "color", "costType", "manaCost",
                    "ordinaryRewardEnabled", "defaultRewardUnlocked", "rewardPoolCost", "rewardMinPlayerLevel",
                    "rewardUnlockPlayerLevel", "rewardUnlockCrawlerContentId", "rewardUnlockCondition", "isFreeToPlay",
                    "hasReverse", "baseDamage", "attackPattern", "projectileCount", "targetPolicy", "directTargetsPerHit",
                    "canCrit", "critChance", "critMultiplier", "areaTriggerMode", "areaCoefficient", "armor", "heal",
                    "manaGain", "statusEffect", "statusChance", "destroyOnUse", "autoPlayOnDraw", "gemSlots", "seals",
                    "evolutionTarget", "effects", "_offeringAdmission"),
                new Contract(typeof(CardOfferingAdmission), "card.offering-admission.v1", "_state", "_sourceConfigId",
                    "_sourceClass", "_rawAllowInOfferingTable"),
                new Contract(typeof(CardUnlockCondition), "card.unlock-condition.v1", "metric", "scope", "requiredCount", "sourceCardContentIds"),
                new Contract(typeof(CardSealSpec), "card.seal.v1", "contentId", "effect", "value", "comboScalable"),
                new Contract(typeof(CardEffectSpec), "card.effect.v1", "effectType", "target", "amount", "count", "damageSpec",
                    "repeatAtPlayerTurnEnd", "replayColumnsAtPlayerTurnEnd", "statusId", "chance", "duration", "conditions",
                    "statType", "costModifier", "randomCostMin", "randomCostMax", "randomCostRelative", "createCardDefinitionId",
                    "crawlerContentId", "attackIntentOnly", "removeAllIntents", "comboScaling", "comboScalable"),
                new Contract(typeof(ConditionRule), "card.condition.v1", "type", "intValue", "floatValue", "stringValue"),
                new Contract(typeof(DamageSpec), "card.damage.v1", "baseHitDamage", "limitBreakAttackInstancesPerLevel", "pattern", "crit",
                    "area", "bounce", "specials", "onHitEffects", "terrainEffect", "ricochetProjectile", "birdAttack",
                    "cardLocalDamageMultiplier", "manaDamageCoefficient", "comboScalable", "amountScalable"),
                new Contract(typeof(AttackPatternSpec), "card.attack-pattern.v1", "patternType", "baseAttackInstanceCount", "targetPolicy",
                    "directTargetsPerSubHit", "comboMode", "amountMode", "subHits"),
                new Contract(typeof(SubHitSpec), "card.sub-hit.v1", "baseDamageOverride", "damageScale", "overrideTargetPolicy",
                    "targetPolicyOverride", "directTargetCountOverride", "critOverride", "areaOverride"),
                new Contract(typeof(CritSpec), "card.crit.v1", "enabled", "chance", "multiplier", "areaDamageBasis"),
                new Contract(typeof(AreaSpec), "card.area.v1", "enabled", "coefficient", "triggerMode"),
                new Contract(typeof(BounceSpec), "card.bounce.v1", "enabled", "sequenceMode", "hitsPerDirectHit", "damageIncrementPerBounce"),
                new Contract(typeof(SpecialAttackSpec), "card.special-attack.v1", "kind", "maxTriggersPerCard", "maxGeneration", "canCrit",
                    "canTriggerArea", "canTriggerOtherSpecials", "canTriggerSelf", "bounceTargetMode"),
                new Contract(typeof(OnHitEffectSpec), "card.on-hit.v1", "effectType", "baseChance", "useRuleDefaultChance"),
                new Contract(typeof(TerrainEffectSpec), "card.terrain.v1", "effectId", "effectKind", "triggerTiming", "damageKind",
                    "magnitudeScale", "triggerCount", "targetMode", "baseMagnitude", "areaCoefficient", "persistUntilCombatEnd"),
                new Contract(typeof(RicochetProjectileSpec), "card.ricochet.v1", "enabled", "minimumLifetime", "maximumLifetime", "amountSpeedCoefficient"),
                new Contract(typeof(BirdAttackSpec), "card.bird.v1", "missilesPerBird", "endTurnMissilesPerBird", "endTurnDamagePerHit", "endTurnAreaCoefficient")
            };
            var result = new Dictionary<Type, Contract>();
            foreach (Contract value in values) result.Add(value.Type, value);
            return result;
        }

        internal sealed class ResolvedReferences
        {
            internal readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            internal readonly Dictionary<string, CardConfig> Cards = new Dictionary<string, CardConfig>(StringComparer.Ordinal);
        }

        internal sealed class Snapshot
        {
            internal readonly CardAuthorDocumentData Data;
            private readonly ResolvedReferences _references;
            internal Snapshot(CardAuthorDocumentData data, ResolvedReferences references)
            { Data = data; _references = references; }
            internal CardConfig Instantiate() => CardAuthorSchema.Instantiate(Data, _references);
            internal void RequireUnchangedBindings() => CardAuthorSchema.RequireUnchangedBindings(Data, _references);
            internal void RequireCompatibleBindings(Snapshot other)
            {
                foreach (var row in _references.Sprites)
                    if (other._references.Sprites.TryGetValue(row.Key, out Sprite value) && value != row.Value)
                        throw new ArgumentException("The frozen pair resolved one sprite identity to two objects.");
                foreach (var row in _references.Cards)
                    if (other._references.Cards.TryGetValue(row.Key, out CardConfig value) && !ReferenceEquals(value, row.Value))
                        throw new ArgumentException("The frozen pair resolved one evolution identity to two objects.");
            }
            internal void RequireCaptureBindings(ICardAuthorReferenceResolver resolver)
            {
                foreach (var row in _references.Sprites)
                {
                    int split = row.Key.LastIndexOf('\n');
                    string id = row.Key.Substring(0, split);
                    var role = (CardAuthorSpriteRole)int.Parse(row.Key.Substring(split + 1), CultureInfo.InvariantCulture);
                    if (resolver.ResolveSprite(id, role) != row.Value)
                        throw new ArgumentException("Sprite does not have the declared stable author/role binding: " + id);
                }
            }
            internal void RequireResolvedBindings(ResolvedReferences resolved)
            {
                foreach (var row in _references.Sprites)
                    if (row.Value == null || !resolved.Sprites.TryGetValue(row.Key, out Sprite sprite) ||
                        sprite == null || row.Value != sprite)
                        throw new ArgumentException("Borrowed sprite identity changed during resolution.");
                foreach (var row in _references.Cards)
                    if (row.Value == null || !resolved.Cards.TryGetValue(row.Key, out CardConfig card) ||
                        card == null || !ReferenceEquals(row.Value, card))
                        throw new ArgumentException("Borrowed evolution object identity changed during resolution.");
            }
        }

        internal static Snapshot Capture(CardConfig author)
        {
            var context = new CaptureContext(Contracts());
            context.Add(author);
            var data = new CardAuthorDocumentData { formatVersion = 1, rootContentId = author.contentId,
                definitions = context.Definitions };
            Validate(data);
            return new Snapshot(data, context.References);
        }

        private sealed class CaptureContext
        {
            private readonly Dictionary<Type, Contract> _contracts;
            private readonly Dictionary<string, CardConfig> _authors = new Dictionary<string, CardConfig>(StringComparer.Ordinal);
            internal readonly List<CardAuthorDefinitionData> Definitions = new List<CardAuthorDefinitionData>();
            internal readonly ResolvedReferences References = new ResolvedReferences();
            internal CaptureContext(Dictionary<Type, Contract> contracts) { _contracts = contracts; }
            internal void Add(CardConfig card)
            {
                if (card == null) throw new ArgumentNullException(nameof(card));
                if (card.GetType() != typeof(CardConfig) || card.runtimeInstanceId != 0)
                    throw new ArgumentException("Only the registered unbound CardConfig author type can be captured.");
                RequireId(card.contentId);
                if (_authors.TryGetValue(card.contentId, out CardConfig previous))
                {
                    if (!ReferenceEquals(previous, card)) throw new ArgumentException("One stable author ID refers to different objects.");
                    return;
                }
                if (Definitions.Count >= MaximumDefinitions) throw new ArgumentException("Author reference closure is too large.");
                _authors.Add(card.contentId, card);
                References.Cards.Add(card.contentId, card);
                var definition = new CardAuthorDefinitionData { contentId = card.contentId };
                Definitions.Add(definition);
                definition.configuration = CaptureNode(card, typeof(CardConfig), card.contentId, 0);
            }
            private CardAuthorNodeData CaptureNode(object source, Type type, string owner, int depth)
            {
                if (depth > 16 || source.GetType() != type || !_contracts.TryGetValue(type, out Contract contract))
                    throw new ArgumentException("Unexpected or excessive typed author configuration.");
                var result = new CardAuthorNodeData { typeContractId = contract.Id, fields = new List<CardAuthorFieldData>() };
                foreach (FieldInfo field in contract.Fields)
                {
                    object value = field.GetValue(source);
                    if (type == typeof(CardConfig) && field.Name == "_offeringAdmission")
                    {
                        CardOfferingAdmission admission = ((CardConfig)source).OfferingAdmission;
                        value = admission.State == CardOfferingAdmissionState.Unknown && admission.SourceConfigId.Length == 0 &&
                            admission.SourceClass.Length == 0 && admission.RawAllowInOfferingTable == 0
                            ? CardOfferingAdmission.Unknown : new CardOfferingAdmission(admission.State,
                                admission.SourceConfigId, admission.SourceClass, admission.RawAllowInOfferingTable);
                    }
                    result.fields.Add(new CardAuthorFieldData { name = field.Name,
                        value = CaptureValue(value, field.FieldType, owner, field.Name, depth + 1) });
                }
                return result;
            }
            private CardAuthorValueData CaptureValue(object value, Type type, string owner, string field, int depth)
            {
                if (depth > 16) throw new ArgumentException("Author nesting is too deep.");
                if (value == null || (value is UnityEngine.Object asset && asset == null)) return new CardAuthorValueData();
                if (type.IsEnum) return new CardAuthorValueData { kind = Integer, integer = Convert.ToInt32(value, CultureInfo.InvariantCulture) };
                if (type == typeof(int)) return new CardAuthorValueData { kind = Integer, integer = (int)value };
                if (type == typeof(bool)) return new CardAuthorValueData { kind = Boolean, boolean = (bool)value };
                if (type == typeof(string)) return new CardAuthorValueData { kind = Text, text = (string)value };
                if (type == typeof(float)) return new CardAuthorValueData { kind = FloatBits,
                    text = BitConverter.ToUInt32(BitConverter.GetBytes((float)value), 0).ToString("x8", CultureInfo.InvariantCulture) };
                if (type == typeof(Sprite))
                {
                    int role = SpriteRole(field); References.Sprites.Add(SpriteKey(owner, role), (Sprite)value);
                    return new CardAuthorValueData { kind = SpriteReference, text = owner, role = role };
                }
                if (type == typeof(CardConfig))
                {
                    Add((CardConfig)value);
                    return new CardAuthorValueData { kind = CardReference, text = ((CardConfig)value).contentId };
                }
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    Type element = type.GetGenericArguments()[0];
                    var items = new List<CardAuthorValueData>();
                    foreach (object item in (IEnumerable)value) items.Add(CaptureValue(item, element, owner, field, depth + 1));
                    return new CardAuthorValueData { kind = List, items = items };
                }
                return new CardAuthorValueData { kind = Node, node = CaptureNode(value, type, owner, depth + 1) };
            }
        }

        internal static void Validate(CardAuthorDocumentData data)
        {
            if (data == null || data.formatVersion != 1 || data.definitions == null || data.definitions.Count == 0 ||
                data.definitions.Count > MaximumDefinitions) throw new ArgumentException("Invalid complete author document.");
            RequireId(data.rootContentId);
            var contracts = Contracts();
            var definitions = new Dictionary<string, CardAuthorDefinitionData>(StringComparer.Ordinal);
            foreach (CardAuthorDefinitionData definition in data.definitions)
            {
                if (definition == null) throw new ArgumentException("An author definition cannot be null.");
                RequireId(definition.contentId);
                if (!definitions.TryAdd(definition.contentId, definition)) throw new ArgumentException("Duplicate stable author ID.");
                ValidateNode(definition.configuration, typeof(CardConfig), definition.contentId, contracts, 0);
                if (Get(definition.configuration, "contentId").text != definition.contentId)
                    throw new ArgumentException("Author definition identity differs from its payload.");
                CardOfferingAdmission admission = (CardOfferingAdmission)RestoreNode(Get(definition.configuration,
                    "_offeringAdmission").node, typeof(CardOfferingAdmission), new ResolvedReferences(), contracts, 0);
                if (admission == null || admission.Validate() != null) throw new ArgumentException("Missing or malformed author admission.");
            }
            var ordered = new List<string>();
            Visit(data.rootContentId, definitions, new HashSet<string>(StringComparer.Ordinal), ordered);
            if (ordered.Count != data.definitions.Count) throw new ArgumentException("Unreachable extra author definitions.");
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i] != data.definitions[i].contentId) throw new ArgumentException("Author closure order is not canonical.");
        }

        private static void ValidateNode(CardAuthorNodeData node, Type type, string owner,
            Dictionary<Type, Contract> contracts, int depth)
        {
            if (depth > 16 || node == null || !contracts.TryGetValue(type, out Contract contract) ||
                node.typeContractId != contract.Id || node.fields == null || node.fields.Count != contract.Fields.Length)
                throw new ArgumentException("Unknown or incomplete author type contract.");
            for (int i = 0; i < contract.Fields.Length; i++)
            {
                CardAuthorFieldData field = node.fields[i]; FieldInfo expected = contract.Fields[i];
                if (field == null || field.name != expected.Name) throw new ArgumentException("Wrong, duplicate or reordered author field.");
                ValidateValue(field.value, expected.FieldType, owner, expected.Name, contracts, depth + 1);
            }
            // The existing unlock consumer explicitly treats this exact Unity
            // default shell as unconfigured. Preserve its shape, without accepting
            // zero as a metric for a configured or partially populated condition.
            if (type == typeof(CardUnlockCondition) && Get(node, "metric").integer == 0)
            {
                CardAuthorValueData ids = Get(node, "sourceCardContentIds");
                if (Get(node, "scope").integer != 0 || Get(node, "requiredCount").integer != 0 ||
                    !(ids.kind == Null || ids.kind == List && ids.items.Count == 0))
                    throw new ArgumentException("Zero unlock metric is only the exact unconfigured author shell.");
            }
        }

        private static void ValidateValue(CardAuthorValueData value, Type type, string owner, string field,
            Dictionary<Type, Contract> contracts, int depth)
        {
            if (depth > 16 || value == null) throw new ArgumentException("A complete typed author value is required.");
            int expected = type == typeof(int) || type.IsEnum ? Integer : type == typeof(bool) ? Boolean :
                type == typeof(string) ? Text : type == typeof(float) ? FloatBits : type == typeof(Sprite) ? SpriteReference :
                type == typeof(CardConfig) ? CardReference : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? List : Node;
            if (value.kind == Null)
            {
                if (type.IsValueType || value.integer != 0 || value.boolean || value.text != null || value.role != 0 ||
                    value.node != null || value.items != null) throw new ArgumentException("Invalid explicit null author value.");
                return;
            }
            if (value.kind != expected || (expected != Integer && value.integer != 0) ||
                (expected != Boolean && value.boolean) || (expected != SpriteReference && value.role != 0) ||
                (expected != Node && value.node != null) || (expected != List && value.items != null) ||
                (expected != Text && expected != FloatBits && expected != SpriteReference && expected != CardReference && value.text != null))
                throw new ArgumentException("Author value kind or unused payload is invalid.");
            if (type.IsEnum && !Enum.IsDefined(type, value.integer) &&
                !(type == typeof(CardUnlockMetric) && value.integer == 0))
                throw new ArgumentException("Unknown author enum value.");
            if (expected == Text)
            {
                if (value.text == null) throw new ArgumentException("An explicit text value cannot be null.");
                new UTF8Encoding(false, true).GetByteCount(value.text);
            }
            else if (expected == FloatBits) ReadFloat(value.text);
            else if (expected == SpriteReference)
            {
                if (value.text != owner || value.role != SpriteRole(field)) throw new ArgumentException("Wrong sprite owner or role.");
            }
            else if (expected == CardReference) RequireId(value.text);
            else if (expected == Node) ValidateNode(value.node, type, owner, contracts, depth + 1);
            else if (expected == List)
            {
                if (value.items == null) throw new ArgumentException("An explicit author list must have its complete array.");
                Type element = type.GetGenericArguments()[0];
                foreach (CardAuthorValueData item in value.items) ValidateValue(item, element, owner, field, contracts, depth + 1);
            }
        }

        private static void Visit(string id, Dictionary<string, CardAuthorDefinitionData> definitions,
            HashSet<string> visited, List<string> order)
        {
            if (visited.Contains(id)) return;
            if (!definitions.TryGetValue(id, out CardAuthorDefinitionData data)) throw new ArgumentException("Missing evolution configuration payload.");
            visited.Add(id); order.Add(id);
            CardAuthorValueData target = Get(data.configuration, "evolutionTarget");
            if (target.kind == CardReference) Visit(target.text, definitions, visited, order);
        }

        internal static ResolvedReferences Resolve(CardAuthorDocumentData data, ICardAuthorReferenceResolver resolver)
        {
            Validate(data);
            var result = new ResolvedReferences();
            var saved = new Dictionary<string, CardAuthorDefinitionData>(StringComparer.Ordinal);
            foreach (CardAuthorDefinitionData definition in data.definitions) saved.Add(definition.contentId, definition);
            foreach (CardAuthorDefinitionData definition in data.definitions)
            {
                foreach (string field in new[] { "cardArt", "borderArt", "iconArt" })
                {
                    CardAuthorValueData reference = Get(definition.configuration, field);
                    if (reference.kind == Null) continue;
                    Sprite sprite = resolver.ResolveSprite(reference.text, (CardAuthorSpriteRole)reference.role);
                    if (sprite == null) throw new ArgumentException("A required stable sprite binding is missing.");
                    result.Sprites.Add(SpriteKey(reference.text, reference.role), sprite);
                }
                CardAuthorValueData target = Get(definition.configuration, "evolutionTarget");
                if (target.kind != CardReference || result.Cards.ContainsKey(target.text)) continue;
                CardConfig actual = resolver.ResolveCard(target.text);
                if (actual == null || actual.runtimeInstanceId != 0 || actual.contentId != target.text || actual.GetType() != typeof(CardConfig))
                    throw new ArgumentException("A stable evolution binding is missing or not an unbound author.");
                Snapshot current = Capture(actual); current.RequireCaptureBindings(resolver);
                var expected = new CardAuthorDocument(Subgraph(target.text, saved));
                var observed = new CardAuthorDocument(current.Data);
                if (observed.CanonicalJson != expected.CanonicalJson)
                    throw new ArgumentException("Evolution configuration drifted from the complete frozen author closure.");
                result.Cards.Add(target.text, actual);
            }
            RequireUnchangedBindings(data, result);
            return result;
        }

        /// <summary>Final closure validation invokes no resolver and allocates no native object.
        /// The physical factory repeats it for both halves after the last pair callback.</summary>
        private static void RequireUnchangedBindings(CardAuthorDocumentData data, ResolvedReferences result)
        {
            var saved = new Dictionary<string, CardAuthorDefinitionData>(StringComparer.Ordinal);
            foreach (CardAuthorDefinitionData definition in data.definitions) saved.Add(definition.contentId, definition);
            foreach (Sprite sprite in result.Sprites.Values)
                if (sprite == null) throw new ArgumentException("A required sprite was destroyed during resolution.");
            foreach (var reference in result.Cards)
            {
                CardConfig actual = reference.Value;
                if (actual == null || actual.runtimeInstanceId != 0 || actual.contentId != reference.Key)
                    throw new ArgumentException("Evolution binding changed during resolution.");
                Snapshot final = Capture(actual);
                final.RequireResolvedBindings(result);
                var observed = new CardAuthorDocument(final.Data);
                var expected = new CardAuthorDocument(Subgraph(reference.Key, saved));
                if (observed.CanonicalJson != expected.CanonicalJson)
                    throw new ArgumentException("Evolution configuration changed during resolution.");
            }
        }

        private static CardAuthorDocumentData Subgraph(string root, Dictionary<string, CardAuthorDefinitionData> definitions)
        {
            var order = new List<string>(); Visit(root, definitions, new HashSet<string>(StringComparer.Ordinal), order);
            var nodes = new List<CardAuthorDefinitionData>();
            foreach (string id in order) nodes.Add(definitions[id]);
            return new CardAuthorDocumentData { formatVersion = 1, rootContentId = root, definitions = nodes };
        }

        internal static CardAuthorDocument GetEvolutionTarget(CardAuthorDocumentData data)
        {
            CardAuthorValueData target = Get(data.definitions[0].configuration, "evolutionTarget");
            if (target.kind != CardReference) throw new ArgumentException("The frozen current author has no configured evolution target.");
            var definitions = new Dictionary<string, CardAuthorDefinitionData>(StringComparer.Ordinal);
            foreach (CardAuthorDefinitionData row in data.definitions) definitions.Add(row.contentId, row);
            return new CardAuthorDocument(Subgraph(target.text, definitions));
        }

        /// <summary>Reads precisely the existing projection input from the complete registered
        /// author grammar. Nested effects are managed values; no root or asset is instantiated.</summary>
        internal static CardOfferingAdmission ReadOfferingAdmission(CardAuthorDocumentData data)
            => (CardOfferingAdmission)RestoreValue(Get(data.definitions[0].configuration, "_offeringAdmission"),
                typeof(CardOfferingAdmission), new ResolvedReferences(), Contracts(), 0) ?? CardOfferingAdmission.Unknown;

        internal static int ReadGemSlotCapacity(CardAuthorDocumentData data)
            => CardSealRules.GetSlotCapacity(Get(data.definitions[0].configuration, "gemSlots").integer);

        internal static IReadOnlyList<string> ReadSummonedCrawlerContentIds(CardAuthorDocumentData data)
        {
            var effects = (List<CardEffectSpec>)RestoreValue(Get(data.definitions[0].configuration, "effects"),
                typeof(List<CardEffectSpec>), new ResolvedReferences(), Contracts(), 0);
            var ids = new List<string>();
            foreach (var effect in effects ?? new List<CardEffectSpec>())
                if (effect.effectType == EffectType.SummonCrawler && !string.IsNullOrWhiteSpace(effect.crawlerContentId) && !ids.Contains(effect.crawlerContentId))
                    ids.Add(effect.crawlerContentId);
            return ids.AsReadOnly();
        }

        internal static CardStatProjectionInputData ReadProjectionInput(CardAuthorDocumentData data,
            out CardOfferingAdmission admission)
        {
            CardAuthorNodeData root = data.definitions[0].configuration;
            var references = new ResolvedReferences(); var contracts = Contracts();
            admission = ReadOfferingAdmission(data);
            var result = new CardStatProjectionInputData
            {
                baseDamage = Get(root, "baseDamage").integer, armor = Get(root, "armor").integer,
                heal = Get(root, "heal").integer, manaGain = Get(root, "manaGain").integer,
                statusEffect = Get(root, "statusEffect").integer, destroyOnUse = Get(root, "destroyOnUse").boolean,
                effects = new List<CardStatProjectionEffectData>()
            };
            var effects = (List<CardEffectSpec>)RestoreValue(Get(root, "effects"),
                typeof(List<CardEffectSpec>), references, contracts, 0);
            if (effects != null)
                foreach (CardEffectSpec effect in effects) result.effects.Add(CardStatProjectionEffectData.Capture(effect));
            return result;
        }

        internal static CardConfig Instantiate(CardAuthorDocumentData data, ResolvedReferences references)
        {
            Validate(data);
            CardConfig root = ScriptableObject.CreateInstance<CardConfig>();
            try
            {
                RestoreFields(root, data.definitions[0].configuration, typeof(CardConfig), references, Contracts(), 0);
                return root;
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        private static object RestoreNode(CardAuthorNodeData node, Type type, ResolvedReferences references,
            Dictionary<Type, Contract> contracts, int depth)
        {
            if (type == typeof(CardOfferingAdmission))
                return new CardOfferingAdmission((CardOfferingAdmissionState)Get(node, "_state").integer,
                    Get(node, "_sourceConfigId").text, Get(node, "_sourceClass").text,
                    Get(node, "_rawAllowInOfferingTable").integer);
            object value = Activator.CreateInstance(type);
            RestoreFields(value, node, type, references, contracts, depth); return value;
        }

        private static void RestoreFields(object output, CardAuthorNodeData node, Type type,
            ResolvedReferences references, Dictionary<Type, Contract> contracts, int depth)
        {
            Contract contract = contracts[type];
            for (int i = 0; i < contract.Fields.Length; i++)
            {
                FieldInfo field = contract.Fields[i];
                object value = RestoreValue(node.fields[i].value, field.FieldType, references, contracts, depth + 1);
                if (type == typeof(CardConfig) && field.Name == "_offeringAdmission")
                    ((CardConfig)output).SetOfferingAdmission((CardOfferingAdmission)value);
                else field.SetValue(output, value);
            }
        }

        private static object RestoreValue(CardAuthorValueData value, Type type, ResolvedReferences references,
            Dictionary<Type, Contract> contracts, int depth)
        {
            if (value.kind == Null) return null;
            if (type.IsEnum) return Enum.ToObject(type, value.integer);
            switch (value.kind)
            {
                case Integer: return value.integer;
                case Boolean: return value.boolean;
                case Text: return value.text;
                case FloatBits: return ReadFloat(value.text);
                case SpriteReference: return references.Sprites[SpriteKey(value.text, value.role)];
                case CardReference: return references.Cards[value.text];
                case Node: return RestoreNode(value.node, type, references, contracts, depth + 1);
                case List:
                    var list = (IList)Activator.CreateInstance(type);
                    Type element = type.GetGenericArguments()[0];
                    foreach (CardAuthorValueData item in value.items) list.Add(RestoreValue(item, element, references, contracts, depth + 1));
                    return list;
                default: throw new ArgumentException("Unknown author value kind.");
            }
        }

        private static CardAuthorValueData Get(CardAuthorNodeData node, string name)
        {
            if (node == null) throw new ArgumentException("Missing author node.");
            foreach (CardAuthorFieldData field in node.fields) if (field.name == name) return field.value;
            throw new ArgumentException("Missing registered author field: " + name);
        }
        private static int SpriteRole(string field) => field == "cardArt" ? 0 : field == "borderArt" ? 1 :
            field == "iconArt" ? 2 : throw new ArgumentException("Unregistered sprite field.");
        private static string SpriteKey(string id, int role) => id + "\n" + role.ToString(CultureInfo.InvariantCulture);
        internal static void RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim()) throw new ArgumentException("An exact stable author ID is required.");
            foreach (char character in id) if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new ArgumentException("Stable author IDs cannot contain whitespace or controls.");
            new UTF8Encoding(false, true).GetByteCount(id);
        }
        private static float ReadFloat(string bits)
        {
            if (bits == null || bits.Length != 8) throw new ArgumentException("Exact lowercase binary32 bits are required.");
            foreach (char c in bits) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))
                throw new ArgumentException("Exact lowercase binary32 bits are required.");
            uint value = uint.Parse(bits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            float result = BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
            if (float.IsNaN(result) || float.IsInfinity(result)) throw new ArgumentException("Author float must be finite.");
            return result;
        }
    }
}
