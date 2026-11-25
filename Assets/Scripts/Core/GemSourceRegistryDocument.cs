using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Complete native Gem checkpoint using the one shared closed DTO reader/writer.
    /// The reconstructed DTO stays private; no mutable author object or account is retained.</summary>
    public sealed class GemSourceRegistryDocument
    {
        public const int CurrentFormatVersion = 1;
        public int FormatVersion { get; }
        public GemSourceRegistryDefinition Definition { get; }
        public string CanonicalJson { get; }
        public string Fingerprint => Definition.Fingerprint;
        private GemSourceRegistryDocument(int version, GemSourceRegistryData data, GemSourceRegistryDefinition definition)
        {
            FormatVersion = version; Definition = definition;
            CanonicalJson = SideEventSourceWireCodec.WriteGemDocument(data);
        }
        public static GemSourceRegistryDocument Create(GemSourceRegistryData data)
            => Parse(SideEventSourceWireCodec.WriteGemDocument(data));
        public static GemSourceRegistryDocument Parse(string json)
        {
            var data = SideEventSourceWireCodec.ReadGemDocument(json, out int version);
            if (!GemSourceRegistryCompiler.TryCompile(data, out var definition, out IReadOnlyList<string> errors))
                throw new InvalidOperationException("Invalid complete Gem source registry: " + string.Join("; ", errors));
            return new GemSourceRegistryDocument(version, data, definition);
        }
    }
}
