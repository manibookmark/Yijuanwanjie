using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// An immutable, self-contained source contract checkpoint. Authoring and Run
    /// recovery share one explicit DTO format and compiler. Capturing a document
    /// never qualifies a source, grants a project mapping or consumes randomness.
    /// The retained DTO is private and was reconstructed by the strict reader;
    /// caller mutations cannot alter the definition, bytes or fingerprint.
    /// </summary>
    public sealed class SideEventSourceWireDocument
    {
        public const int CurrentFormatVersion = 1;
        private readonly SideEventSourceContentData _source;
        public int FormatVersion { get; }
        public SideEventSourceDefinition Definition { get; }
        public string CanonicalJson { get; }
        public string Fingerprint => Definition.Fingerprint;

        private SideEventSourceWireDocument(int version, SideEventSourceContentData source,
            SideEventSourceDefinition definition)
        {
            FormatVersion = version;
            _source = source;
            Definition = definition;
            CanonicalJson = SideEventSourceWireCodec.WriteDocument(_source);
        }

        /// <summary>Captures every explicit source field, then validates the
        /// same standalone wire used by recovery. The caller's DTO is not retained.</summary>
        public static SideEventSourceWireDocument Create(SideEventSourceContentData source)
            => Parse(SideEventSourceWireCodec.WriteDocument(source));

        /// <summary>Restores the complete closed source family with no Content,
        /// account, native JSON, RNG or Run dependency. Unknown wire versions fail.</summary>
        public static SideEventSourceWireDocument Parse(string json)
        {
            SideEventSourceContentData source = SideEventSourceWireCodec.ReadDocument(json, out int version);
            if (!SideEventSourceWireCompiler.TryCompile(source,
                    out SideEventSourceDefinition definition, out IReadOnlyList<string> errors))
                throw new InvalidOperationException("Invalid complete source contract: " + string.Join("; ", errors));
            return new SideEventSourceWireDocument(version, source, definition);
        }
    }
}
