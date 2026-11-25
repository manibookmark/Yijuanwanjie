using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Owns the single physical-card persistence boundary: detached copies,
    /// exact modern wire, historical migration, frozen restoration and scoped native
    /// cleanup. Session owns inventory and commits; author references are borrowed
    /// from an explicit catalog and never become another durable card ledger.</summary>
    internal static class DungeonRunCardCodec
    {
        private const int Version = 1;
        private static ExactJsonWire ModernWire => new ExactJsonWire(typeof(CardWire), typeof(SealWire));
        private static ExactJsonWire LegacyWire => new ExactJsonWire(typeof(LegacyCardWire), typeof(SlottedLegacyCardWire), typeof(InitialCardWire), typeof(InitialSlottedCardWire), typeof(SealWire));

        internal static bool IsKnown(DungeonRunCardSaveData card)
            => card != null && card.lineageVersion == Version && card.lineageState == (int)CardAuthorProvenance.FrozenKnown;

        internal static DungeonRunCardSaveData Capture(CardConfig card, ICardAuthorReferenceResolver resolver)
        {
            if (card == null || card.IsEncounterTemporary) throw Bad("Only persistent physical cards can be saved.");
            CardPersistentState state = CardInstanceFactory.CapturePersistentState(card);
            var saved = new DungeonRunCardSaveData {
                contentId = card.contentId, runtimeInstanceId = state.InstanceId,
                limitBreakCount = state.LimitBreakCount, hasGemSlotCapacity = true,
                gemSlotCapacity = state.GemSlotCapacity, seals = CopySeals(state.Seals),
                lineageVersion = Version, lineageState = (int)card.AuthorProvenance,
                baseAuthorJson = string.Empty, currentAuthorJson = string.Empty
            };
            if (card.HasKnownAuthorConfigurations)
            {
                var pair = CardInstanceFactory.CaptureAuthorConfigurations(card, resolver);
                saved.baseAuthorJson = pair.Base.CanonicalJson;
                saved.currentAuthorJson = pair.Current.CanonicalJson;
            }
            Validate(saved);
            return saved;
        }

        internal static DungeonRunCardSaveData CaptureNew(CardConfig author, int id, ICardAuthorReferenceResolver resolver)
        {
            CardConfig physical = null;
            try
            {
                int capacity = CardSealRules.GetSlotCapacity(author);
                var document = CardAuthorDocument.Capture(author, resolver);
                physical = CardInstanceFactory.RestoreFrozenAuthors(document, document, resolver,
                    new CardPersistentState(id, 0, capacity, Array.Empty<CardSealSpec>()));
                return Capture(physical, resolver);
            }
            finally { Release(physical); }
        }

        internal static CardConfig Restore(DungeonRunCardSaveData saved, ICardAuthorReferenceResolver resolver)
        {
            Validate(saved);
            if (resolver == null) throw Bad("An explicit author reference catalog is required.");
            if (IsKnown(saved))
                return CardInstanceFactory.RestoreFrozenAuthors(CardAuthorDocument.Parse(saved.baseAuthorJson),
                    CardAuthorDocument.Parse(saved.currentAuthorJson), resolver,
                    new CardPersistentState(saved.runtimeInstanceId, saved.limitBreakCount, saved.gemSlotCapacity, saved.seals));
            CardConfig today = resolver.ResolveCard(saved.contentId);
            if (today == null || today.contentId != saved.contentId) throw Bad("Legacy card definition is unavailable: " + saved.contentId);
            int capacity = saved.hasGemSlotCapacity ? saved.gemSlotCapacity : CardSealRules.GetSlotCapacity(today);
            return CardInstanceFactory.RestoreLegacyUnknown(today,
                new CardPersistentState(saved.runtimeInstanceId, saved.limitBreakCount, capacity, saved.seals));
        }

        internal static bool TrySeedCards(IReadOnlyList<CardConfig> seedDeck, Func<int> allocateId,
            ICardAuthorReferenceResolver resolver, out List<CardConfig> prepared,
            out List<DungeonRunCardSaveData> saved, out string error)
        {
            prepared = new List<CardConfig>(); saved = null; error = null;
            try
            {
                if (seedDeck == null || seedDeck.Count == 0 || allocateId == null)
                    throw Bad("A new Run requires explicit author seeds and an instance allocator.");
                var documents = new List<CardAuthorDocument>();
                var capacities = new List<int>();
                foreach (var author in seedDeck)
                {
                    if (author == null || author.runtimeInstanceId != 0 || author.IsEncounterTemporary)
                        throw Bad("New Run seeds must be persistent unbound author definitions.");
                    // Freeze/validate every author before allocating any physical object.
                    capacities.Add(CardSealRules.GetSlotCapacity(author));
                    documents.Add(CardAuthorDocument.Capture(author, resolver));
                }
                var ids = new HashSet<int>();
                for (int i = 0; i < seedDeck.Count; i++)
                {
                    int id; do { id = allocateId(); } while (id <= 0 || !ids.Add(id));
                    var document = documents[i];
                    prepared.Add(CardInstanceFactory.RestoreFrozenAuthors(document, document, resolver,
                        new CardPersistentState(id, 0, capacities[i], Array.Empty<CardSealSpec>())));
                }
                saved = CaptureCards(prepared, resolver, out error);
                if (saved == null) throw Bad(error);
                return true;
            }
            catch (Exception exception)
            {
                foreach (var card in prepared) Release(card);
                prepared = null; saved = null; error = exception.Message; return false;
            }
        }

        internal static bool TryRestoreCards(IReadOnlyList<DungeonRunCardSaveData> saved,
            IReadOnlyList<CardConfig> authors, out List<CardConfig> restored, out string error)
        {
            restored = new List<CardConfig>(); error = null;
            try
            {
                ValidateCards(saved);
                var resolver = CreateResolver(authors);
                foreach (var card in saved ?? Array.Empty<DungeonRunCardSaveData>()) restored.Add(Restore(card, resolver));
                return true;
            }
            catch (Exception exception)
            {
                foreach (var card in restored) Release(card);
                restored = null; error = exception.Message; return false;
            }
        }

        internal static List<DungeonRunCardSaveData> CaptureCards(IReadOnlyList<CardConfig> cards,
            ICardAuthorReferenceResolver resolver, out string error)
        {
            error = null;
            try
            {
                var result = new List<DungeonRunCardSaveData>();
                foreach (var card in cards ?? Array.Empty<CardConfig>()) result.Add(Capture(card, resolver));
                ValidateCards(result);
                result.Sort((a, b) => a.runtimeInstanceId.CompareTo(b.runtimeInstanceId));
                return result;
            }
            catch (Exception exception) { error = exception.Message; return null; }
        }

        internal static void Validate(DungeonRunCardSaveData card)
        {
            ValidatePhysical(card);
            if (card.lineageVersion != Version || !Enum.IsDefined(typeof(CardAuthorProvenance), card.lineageState))
                throw Bad("Unsupported physical author provenance.");
            if (card.baseAuthorJson == null || card.currentAuthorJson == null) throw Bad("Author carriers must be explicit strings.");
            if (IsKnown(card))
            {
                if (!card.hasGemSlotCapacity) throw Bad("Known cards require an exact frozen slot capacity.");
                var basis = CardAuthorDocument.Parse(card.baseAuthorJson);
                var current = CardAuthorDocument.Parse(card.currentAuthorJson);
                if (current.ContentId != card.contentId || basis.CanonicalJson != card.baseAuthorJson || current.CanonicalJson != card.currentAuthorJson)
                    throw Bad("Frozen author identity or canonical representation is inconsistent.");
            }
            else if (card.baseAuthorJson.Length != 0 || card.currentAuthorJson.Length != 0)
                throw Bad("LegacyUnknown cannot claim a reconstructed historical author.");
        }

        private static void ValidatePhysical(DungeonRunCardSaveData card)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.contentId)) throw Bad("存在无 contentId 的卡牌记录。");
            if (card.limitBreakCount < 0) throw Bad("存在负数突破层数的卡牌记录。");
            if (card.contentId.Trim() != card.contentId || card.runtimeInstanceId <= 0 || card.seals == null ||
                card.seals.Count > CardSealRules.MaximumSealSlots || card.gemSlotCapacity < 0 ||
                card.gemSlotCapacity > CardSealRules.MaximumSealSlots ||
                (card.hasGemSlotCapacity && card.seals.Count > card.gemSlotCapacity) ||
                (!card.hasGemSlotCapacity && card.gemSlotCapacity != 0)) throw Bad("Invalid physical card state.");
            foreach (var seal in card.seals)
                if (seal == null || string.IsNullOrWhiteSpace(seal.contentId) ||
                    !Enum.IsDefined(typeof(CardSealEffect), seal.effect)) throw Bad("Invalid ordered physical seal.");
        }

        internal static void ValidateCards(IReadOnlyList<DungeonRunCardSaveData> cards)
        {
            if (cards == null) throw Bad("An explicit physical-card roster is required.");
            var ids = new HashSet<int>();
            foreach (var card in cards)
            { Validate(card); if (!ids.Add(card.runtimeInstanceId)) throw Bad("Duplicate physical card instance ID."); }
        }

        internal static void ValidateEvolutionTransition(DungeonRunCardSaveData before,
            DungeonRunCardSaveData after, string expectedOutcomeId)
        {
            Validate(before); Validate(after);
            if (before.runtimeInstanceId != after.runtimeInstanceId || after.contentId != expectedOutcomeId ||
                after.limitBreakCount != 0 || !before.hasGemSlotCapacity || !after.hasGemSlotCapacity ||
                after.gemSlotCapacity != before.gemSlotCapacity - 1 || after.lineageState != before.lineageState)
                throw Bad("Evolution changed physical identity, provenance or its exact slot contract.");
            if (before.seals.Count != after.seals.Count) throw Bad("Evolution must retain every installed seal.");
            for (int i = 0; i < before.seals.Count; i++)
            {
                var a = before.seals[i]; var b = after.seals[i];
                if (a.contentId != b.contentId || a.effect != b.effect || a.value != b.value || a.comboScalable != b.comboScalable)
                    throw Bad("Evolution changed an installed seal or its order.");
            }
            if (IsKnown(before))
            {
                if (after.baseAuthorJson != after.currentAuthorJson) throw Bad("Evolution target-as-base authors disagree.");
                CardAuthorDocument.Parse(before.currentAuthorJson).RequireEvolutionTarget(CardAuthorDocument.Parse(after.currentAuthorJson));
            }
        }

        internal static DungeonRunCardSaveData Clone(DungeonRunCardSaveData card)
        {
            Validate(card);
            return FromWire(ToWire(card));
        }

        internal static List<DungeonRunCardSaveData> CloneCards(IReadOnlyList<DungeonRunCardSaveData> cards)
        {
            ValidateCards(cards);
            var result = new List<DungeonRunCardSaveData>(cards.Count);
            foreach (var card in cards) result.Add(Clone(card));
            return result;
        }

        internal static string CanonicalJson(DungeonRunCardSaveData card) { Validate(card); return ModernWire.Write(ToWire(card)); }
        internal static DungeonRunCardSaveData Parse(string json)
        {
            var data = ModernWire.Read<CardWire>(json);
            if (data == null) throw Bad("A card wire cannot be null.");
            var card = FromWire(data); Validate(card);
            if (ModernWire.Write(data) != json) throw Bad("Physical card wire must be canonical.");
            return card;
        }
        internal static string Fingerprint(DungeonRunCardSaveData card)
        {
            using (var hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(new UTF8Encoding(false, true).GetBytes(CanonicalJson(card)));
                var text = new StringBuilder(64); foreach (byte value in digest) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        internal static DungeonRunCardSaveData MigrateLegacy(DungeonRunCardSaveData legacy)
        {
            if (legacy == null || legacy.lineageVersion != 0 || legacy.lineageState != 0 ||
                !string.IsNullOrEmpty(legacy.baseAuthorJson) || !string.IsNullOrEmpty(legacy.currentAuthorJson))
                throw Bad("Only a validated historical six-field card can migrate.");
            var migrated = new DungeonRunCardSaveData {
                contentId = legacy.contentId, runtimeInstanceId = legacy.runtimeInstanceId,
                limitBreakCount = legacy.limitBreakCount, hasGemSlotCapacity = legacy.hasGemSlotCapacity,
                gemSlotCapacity = legacy.gemSlotCapacity, seals = CopySeals(legacy.seals),
                lineageVersion = Version, lineageState = (int)CardAuthorProvenance.LegacyUnknown,
                baseAuthorJson = string.Empty, currentAuthorJson = string.Empty
            };
            Validate(migrated); return migrated;
        }

        /// <summary>Read raw rows before JsonUtility can erase presence/null information.
        /// Historical journals still validate their original DTO/hash before their owner
        /// receives this detached migrated roster.</summary>
        internal static List<DungeonRunCardSaveData> ReadRun(string payload, int schemaVersion)
        {
            if (!JsonObjectShape.TryGetField(payload, "cards", out string raw) || !JsonObjectShape.TryGetArrayItems(raw, out var rows))
                throw Bad("Run requires an explicit physical-card array.");
            var result = new List<DungeonRunCardSaveData>();
            foreach (string row in rows)
            {
                if (schemaVersion >= 38)
                {
                    var wire = ModernWire.Read<CardWire>(row);
                    if (wire == null) throw Bad("A physical row cannot be null.");
                    var modern = FromWire(wire); Validate(modern); result.Add(modern);
                }
                else
                {
                    bool hasFlag = JsonObjectShape.TryGetField(row, "hasGemSlotCapacity", out _);
                    bool hasCapacity = JsonObjectShape.TryGetField(row, "gemSlotCapacity", out _);
                    DungeonRunCardSaveData legacy;
                    if (hasFlag != hasCapacity) throw Bad("Partial historical slot state is invalid.");
                    bool hasBreakthrough = JsonObjectShape.TryGetField(row, "limitBreakCount", out _);
                    if (!hasBreakthrough && schemaVersion >= 6) throw Bad("Version six requires explicit breakthrough progress.");
                    if (!hasBreakthrough && hasFlag)
                    {
                        var data = LegacyWire.Read<InitialSlottedCardWire>(row);
                        if (data == null) throw Bad("Historical card cannot be null.");
                        legacy = new DungeonRunCardSaveData { contentId = data.contentId, runtimeInstanceId = data.runtimeInstanceId,
                            hasGemSlotCapacity = data.hasGemSlotCapacity, gemSlotCapacity = data.gemSlotCapacity, seals = FromSeals(data.seals) };
                    }
                    else if (!hasBreakthrough)
                    {
                        var data = LegacyWire.Read<InitialCardWire>(row);
                        if (data == null) throw Bad("Historical card cannot be null.");
                        legacy = new DungeonRunCardSaveData { contentId = data.contentId, runtimeInstanceId = data.runtimeInstanceId, seals = FromSeals(data.seals) };
                    }
                    else if (hasFlag)
                    {
                        var data = LegacyWire.Read<SlottedLegacyCardWire>(row);
                        if (data == null) throw Bad("Historical card cannot be null.");
                        legacy = new DungeonRunCardSaveData { contentId = data.contentId, runtimeInstanceId = data.runtimeInstanceId,
                            limitBreakCount = data.limitBreakCount, hasGemSlotCapacity = data.hasGemSlotCapacity,
                            gemSlotCapacity = data.gemSlotCapacity, seals = FromSeals(data.seals) };
                    }
                    else
                    {
                        var data = LegacyWire.Read<LegacyCardWire>(row);
                        if (data == null) throw Bad("Historical card cannot be null.");
                        legacy = new DungeonRunCardSaveData { contentId = data.contentId, runtimeInstanceId = data.runtimeInstanceId,
                            limitBreakCount = data.limitBreakCount, seals = FromSeals(data.seals) };
                    }
                    result.Add(MigrateLegacy(legacy));
                }
            }
            ValidateCards(result); return result;
        }

        internal static ICardAuthorReferenceResolver CreateResolver(IReadOnlyList<CardConfig> authors)
            => new AuthorReferences(authors);

        private sealed class AuthorReferences : ICardAuthorReferenceResolver
        {
            private readonly Dictionary<string, CardConfig> _cards = new Dictionary<string, CardConfig>(StringComparer.Ordinal);
            internal AuthorReferences(IReadOnlyList<CardConfig> authors)
            {
                var roots = new HashSet<string>(StringComparer.Ordinal);
                foreach (var author in authors ?? Array.Empty<CardConfig>())
                {
                    if (author != null && !roots.Add(author.contentId)) throw Bad("Duplicate explicit catalog author identity: " + author.contentId);
                    Add(author);
                }
            }
            private void Add(CardConfig author)
            {
                if (author == null) return;
                if (author.runtimeInstanceId != 0 || string.IsNullOrWhiteSpace(author.contentId)) throw Bad("Reference catalogs require unbound stable authors.");
                if (_cards.TryGetValue(author.contentId, out var old))
                { if (!ReferenceEquals(old, author)) throw Bad("Ambiguous stable author identity: " + author.contentId); return; }
                _cards.Add(author.contentId, author); Add(author.evolutionTarget);
            }
            public CardConfig ResolveCard(string contentId) => _cards.TryGetValue(contentId, out var author) ? author : null;
            public Sprite ResolveSprite(string ownerContentId, CardAuthorSpriteRole role)
            {
                CardConfig card = ResolveCard(ownerContentId);
                if (card == null) return null;
                switch (role)
                {
                    case CardAuthorSpriteRole.CardArt: return card.cardArt;
                    case CardAuthorSpriteRole.BorderArt: return card.borderArt;
                    case CardAuthorSpriteRole.IconArt: return card.iconArt;
                    default: throw Bad("Unknown author sprite role.");
                }
            }
        }

        internal static void Release(UnityEngine.Object card)
        { if (card == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(card); else UnityEngine.Object.DestroyImmediate(card); }
        private static InvalidOperationException Bad(string message) => new InvalidOperationException("Run physical card: " + message);
        private static List<CardSealSpec> CopySeals(IReadOnlyList<CardSealSpec> seals)
        {
            var result = new List<CardSealSpec>();
            foreach (var seal in seals ?? Array.Empty<CardSealSpec>()) result.Add(seal?.Clone());
            return result;
        }
        private static List<SealWire> ToSeals(IReadOnlyList<CardSealSpec> seals)
        {
            var result = new List<SealWire>();
            foreach (var seal in seals) result.Add(new SealWire { contentId = seal.contentId, effect = (int)seal.effect,
                value = seal.value, comboScalable = seal.comboScalable });
            return result;
        }
        private static List<CardSealSpec> FromSeals(List<SealWire> seals)
        {
            if (seals == null) return null;
            var result = new List<CardSealSpec>();
            foreach (var seal in seals) result.Add(seal == null ? null : new CardSealSpec { contentId = seal.contentId,
                effect = (CardSealEffect)seal.effect, value = seal.value, comboScalable = seal.comboScalable });
            return result;
        }
        private static CardWire ToWire(DungeonRunCardSaveData card) => new CardWire { contentId = card.contentId,
            runtimeInstanceId = card.runtimeInstanceId, limitBreakCount = card.limitBreakCount,
            hasGemSlotCapacity = card.hasGemSlotCapacity, gemSlotCapacity = card.gemSlotCapacity, seals = ToSeals(card.seals),
            lineageVersion = card.lineageVersion, lineageState = card.lineageState,
            baseAuthorJson = card.baseAuthorJson, currentAuthorJson = card.currentAuthorJson };
        private static DungeonRunCardSaveData FromWire(CardWire card) => new DungeonRunCardSaveData { contentId = card.contentId,
            runtimeInstanceId = card.runtimeInstanceId, limitBreakCount = card.limitBreakCount,
            hasGemSlotCapacity = card.hasGemSlotCapacity, gemSlotCapacity = card.gemSlotCapacity, seals = FromSeals(card.seals),
            lineageVersion = card.lineageVersion, lineageState = card.lineageState,
            baseAuthorJson = card.baseAuthorJson, currentAuthorJson = card.currentAuthorJson };
        private sealed class SealWire { public string contentId; public int effect; public int value; public bool comboScalable; }
        private sealed class InitialCardWire { public string contentId; public int runtimeInstanceId; public List<SealWire> seals; }
        private sealed class InitialSlottedCardWire { public string contentId; public int runtimeInstanceId;
            public bool hasGemSlotCapacity; public int gemSlotCapacity; public List<SealWire> seals; }
        private sealed class LegacyCardWire { public string contentId; public int runtimeInstanceId; public int limitBreakCount; public List<SealWire> seals; }
        private sealed class SlottedLegacyCardWire { public string contentId; public int runtimeInstanceId; public int limitBreakCount;
            public bool hasGemSlotCapacity; public int gemSlotCapacity; public List<SealWire> seals; }
        private sealed class CardWire { public string contentId; public int runtimeInstanceId; public int limitBreakCount;
            public bool hasGemSlotCapacity; public int gemSlotCapacity; public List<SealWire> seals;
            public int lineageVersion; public int lineageState; public string baseAuthorJson; public string currentAuthorJson; }
    }
}
