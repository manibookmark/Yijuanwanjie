using System;

namespace WanjieScroll.Core
{
    /// <summary>Owns the first frozen compiled-author configuration and its complete
    /// evolution closure. Memory restoration borrows the originally captured assets;
    /// portable export checks their stable bindings and saved payloads. No native
    /// template object, live facade recapture or additional physical state is owned.</summary>
    internal sealed class CardBaseTemplate
    {
        private readonly CardAuthorSchema.Snapshot _snapshot;
        internal string ContentId => _snapshot.Data.rootContentId;

        private CardBaseTemplate(CardConfig definition)
        {
            _snapshot = CardAuthorSchema.Capture(definition);
        }

        private CardBaseTemplate(CardAuthorSchema.Snapshot snapshot)
        { _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot)); }

        internal static CardBaseTemplate FromDocument(CardAuthorDocument document, ICardAuthorReferenceResolver resolver)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return new CardBaseTemplate(document.ToSnapshot(resolver));
        }

        internal void RequireUnchangedBindings() => _snapshot.RequireUnchangedBindings();
        internal void RequireCompatibleBindings(CardBaseTemplate other) => _snapshot.RequireCompatibleBindings(other._snapshot);

        internal static CardBaseTemplate Capture(CardConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.RuntimeInstance != null)
                return source.RuntimeInstance.BaseTemplate ?? throw new InvalidOperationException(
                    "LegacyUnknown has no original author configuration to capture.");
            return new CardBaseTemplate(source);
        }

        internal static CardBaseTemplate CaptureCurrent(CardConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.RuntimeInstance != null)
                return source.RuntimeInstance.CurrentTemplate ?? throw new InvalidOperationException("Missing frozen current configuration.");
            return new CardBaseTemplate(source);
        }

        /// <summary>The caller owns this single root; referenced author assets are borrowed.</summary>
        internal CardConfig Instantiate() => _snapshot.Instantiate();

        internal CardAuthorDocument Export(ICardAuthorReferenceResolver resolver)
            => CardAuthorDocument.FromSnapshot(_snapshot, resolver);
    }
}
