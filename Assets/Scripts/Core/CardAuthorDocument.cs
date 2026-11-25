using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Core
{
    public enum CardAuthorSpriteRole { CardArt = 0, BorderArt = 1, IconArt = 2 }

    /// <summary>Resolves stable author bindings, never an engine instance ID or display name.
    /// Evolution definitions are borrowed, unbound author objects. The document verifies
    /// their complete saved configuration closure before creating its one returned object.</summary>
    public interface ICardAuthorReferenceResolver
    {
        Sprite ResolveSprite(string ownerContentId, CardAuthorSpriteRole role);
        CardConfig ResolveCard(string contentId);
    }

    /// <summary>Owns a portable, closed compiled-author configuration. It owns no physical
    /// card, reward, Run state or native template. Parsing verifies syntax and configuration
    /// integrity; fingerprints do not prove historical authority or grant publication.
    /// Sprite bindings are stable owner/role identities. Evolution payloads are frozen in
    /// a complete graph; restoring against changed external targets fails before allocation.</summary>
    public sealed class CardAuthorDocument
    {
        public const int CurrentFormatVersion = 1;
        private readonly CardAuthorDocumentData _data;
        public int FormatVersion => CurrentFormatVersion;
        public string ContentId => _data.rootContentId;
        public string CanonicalJson { get; }
        public string Fingerprint { get; }

        private static readonly ExactJsonWire Wire = new ExactJsonWire(typeof(CardAuthorDocumentData),
            typeof(CardAuthorDefinitionData), typeof(CardAuthorNodeData),
            typeof(CardAuthorFieldData), typeof(CardAuthorValueData));

        // Only fully validated immutable managed documents are retained. Physical
        // cards, resolver bindings and admission decisions are never cached here.
        // Full canonical text is the key; a content ID or fingerprint cannot
        // substitute another configuration with the same declared identity.
        internal const int MaximumCachedDocumentCount = 128;
        private const int MaximumCachedDocumentCharacters = 4 * 1024 * 1024;
        private static readonly object ParseCacheGate = new object();
        private static readonly Dictionary<string, CardAuthorDocument> ParsedDocuments =
            new Dictionary<string, CardAuthorDocument>(StringComparer.Ordinal);
        private static readonly Queue<string> ParsedDocumentOrder = new Queue<string>();
        private static int _cachedDocumentCharacters;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ClearParsedDocumentCache()
        {
            lock (ParseCacheGate)
            {
                ParsedDocuments.Clear();
                ParsedDocumentOrder.Clear();
                _cachedDocumentCharacters = 0;
            }
        }

        internal CardAuthorDocument(CardAuthorDocumentData data)
        {
            CardAuthorSchema.Validate(data);
            CanonicalJson = Wire.Write(data);
            _data = Wire.Read<CardAuthorDocumentData>(CanonicalJson);
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = new UTF8Encoding(false, true).GetBytes("card-author-document/v1\n" + CanonicalJson);
                Fingerprint = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>Captures an unbound author only. Bound cards must export their already
        /// frozen templates through CardInstanceFactory.CaptureAuthorConfigurations.</summary>
        public static CardAuthorDocument Capture(CardConfig author, ICardAuthorReferenceResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            CardAuthorSchema.Snapshot snapshot = CardAuthorSchema.Capture(author);
            snapshot.RequireCaptureBindings(resolver);
            return new CardAuthorDocument(snapshot.Data);
        }

        public static CardAuthorDocument Parse(string canonicalJson)
        {
            if (canonicalJson == null) throw new ArgumentNullException(nameof(canonicalJson));
            lock (ParseCacheGate)
                if (ParsedDocuments.TryGetValue(canonicalJson, out CardAuthorDocument cached))
                    return cached;

            CardAuthorDocumentData data = Wire.Read<CardAuthorDocumentData>(canonicalJson);
            var result = new CardAuthorDocument(data);
            if (!string.Equals(canonicalJson, result.CanonicalJson, StringComparison.Ordinal))
                throw new ArgumentException("Card author wire must be canonical, with exact field and value order.");
            if (canonicalJson.Length > MaximumCachedDocumentCharacters)
                return result;
            lock (ParseCacheGate)
            {
                if (ParsedDocuments.TryGetValue(canonicalJson, out CardAuthorDocument cached))
                    return cached;
                while (ParsedDocuments.Count >= MaximumCachedDocumentCount ||
                       _cachedDocumentCharacters > MaximumCachedDocumentCharacters - canonicalJson.Length)
                {
                    string expired = ParsedDocumentOrder.Dequeue();
                    ParsedDocuments.Remove(expired);
                    _cachedDocumentCharacters -= expired.Length;
                }
                ParsedDocuments.Add(canonicalJson, result);
                ParsedDocumentOrder.Enqueue(canonicalJson);
                _cachedDocumentCharacters += canonicalJson.Length;
            }
            return result;
        }

        /// <summary>Creates exactly one caller-owned unbound root. All sprite and evolution
        /// references are checked first; target definitions are borrowed from the resolver.
        /// No fallback to today's payload, hidden target clone or partial native graph exists.</summary>
        public CardConfig Instantiate(ICardAuthorReferenceResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            CardAuthorSchema.ResolvedReferences references = CardAuthorSchema.Resolve(_data, resolver);
            return CardAuthorSchema.Instantiate(_data, references);
        }

        /// <summary>Requires the complete target payload and closure frozen by this author.
        /// This managed comparison performs no resolution or allocation and does not grant
        /// evolution eligibility or replace a physical card's provenance classification.</summary>
        public void RequireEvolutionTarget(CardAuthorDocument target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            CardAuthorDocument expected = CardAuthorSchema.GetEvolutionTarget(_data);
            if (target.CanonicalJson != expected.CanonicalJson)
                throw new ArgumentException("Evolution target differs from the complete frozen configuration closure.");
        }

        /// <summary>Reads immutable source admission from the complete frozen author. No native
        /// object, current catalog query or supported-stat filter is involved.</summary>
        public CardOfferingAdmission ReadOfferingAdmission() => CardAuthorSchema.ReadOfferingAdmission(_data);

        /// <summary>Innate new-card slot capacity from this frozen author, using the sole Core slot rule.</summary>
        public int ReadGemSlotCapacity() => CardAuthorSchema.ReadGemSlotCapacity(_data);

        /// <summary>Stable summon identities from this frozen root, without allocating a card or consulting today's catalog.</summary>
        public IReadOnlyList<string> ReadSummonedCrawlerContentIds() => CardAuthorSchema.ReadSummonedCrawlerContentIds(_data);

        internal CardStatProjectionInputData ReadProjectionInput(out CardOfferingAdmission admission)
            => CardAuthorSchema.ReadProjectionInput(_data, out admission);

        /// <summary>Rehydrates immutable managed configuration and borrowed bindings, without
        /// creating a native template. The physical factory rechecks both halves after callbacks.</summary>
        internal CardAuthorSchema.Snapshot ToSnapshot(ICardAuthorReferenceResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            CardAuthorDocumentData data = CloneData(_data);
            return new CardAuthorSchema.Snapshot(data, CardAuthorSchema.Resolve(data, resolver));
        }

        internal static CardAuthorDocument FromSnapshot(CardAuthorSchema.Snapshot snapshot,
            ICardAuthorReferenceResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            // This exports the original frozen graph. It never recaptures the mutable
            // root or a target that changed after the first instance was constructed.
            snapshot.RequireCaptureBindings(resolver);
            var result = new CardAuthorDocument(snapshot.Data);
            CardAuthorSchema.Resolve(snapshot.Data, resolver);
            return result;
        }

        internal static CardAuthorDocumentData CloneData(CardAuthorDocumentData data)
            => Wire.Read<CardAuthorDocumentData>(Wire.Write(data));
    }

    /// <summary>Detached immutable configuration pair, not a physical-card save or a claim
    /// that an old Run without provenance has a known base configuration.</summary>
    public sealed class CardAuthorConfigurationPair
    {
        public CardAuthorDocument Base { get; }
        public CardAuthorDocument Current { get; }
        internal CardAuthorConfigurationPair(CardAuthorDocument basis, CardAuthorDocument current)
        {
            Base = basis ?? throw new ArgumentNullException(nameof(basis));
            Current = current ?? throw new ArgumentNullException(nameof(current));
        }
    }

    // The grammar is a closed typed value carrier, not an arbitrary JSON tree. Every
    // node type, field, primitive kind and reference role is fixed by CardAuthorSchema.
    [Serializable] internal sealed class CardAuthorDocumentData
    {
        public int formatVersion;
        public string rootContentId;
        public List<CardAuthorDefinitionData> definitions;
    }
    [Serializable] internal sealed class CardAuthorDefinitionData
    {
        public string contentId;
        public CardAuthorNodeData configuration;
    }
    [Serializable] internal sealed class CardAuthorNodeData
    {
        public string typeContractId;
        public List<CardAuthorFieldData> fields;
    }
    [Serializable] internal sealed class CardAuthorFieldData
    {
        public string name;
        public CardAuthorValueData value;
    }
    [Serializable] internal sealed class CardAuthorValueData
    {
        public int kind;
        public int integer;
        public bool boolean;
        public string text;
        public int role;
        public CardAuthorNodeData node;
        public List<CardAuthorValueData> items;
    }
}
