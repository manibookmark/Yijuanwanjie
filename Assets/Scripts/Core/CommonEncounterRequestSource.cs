using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Content owns curve parsing/evaluation. Dungeon can inspect its immutable
    /// metadata without evaluating a curve, or request values on real floor
    /// preparation. Inspection never selects a template or consumes randomness.
    /// </summary>
    public interface ICommonEncounterRequestSource
    {
        string ContentId { get; }
        string Fingerprint { get; }
        IReadOnlyList<CommonEncounterTemplate> Templates { get; }
        IReadOnlyList<CommonEncounterEnemy> Enemies { get; }
        IReadOnlyList<CommonEncounterTagRequest> TemplateTagRequests { get; }
        IReadOnlyList<string> EnemyRequestedLeafTags { get; }
        IReadOnlyList<string> DefaultTemplateLeafTags { get; }
        bool HasDifficulty(string difficultyContentId);
        bool TryCreateRequest(string difficultyContentId, float tileDifficulty,
            out CommonEncounterRequest request, out string error);
    }

    /// <summary>
    /// Optional immutable metadata for sources with per-difficulty enemy filters.
    /// Returns the effective filter (inheritance already resolved), including an
    /// explicit empty filter. Reads never evaluate curves or select enemies.
    /// Providers without this extension retain their dataset-wide filter.
    /// </summary>
    public interface ICommonEncounterDifficultyEnemyTagSource
    {
        bool TryGetEnemyRequestedLeafTags(string difficultyContentId,
            out IReadOnlyList<string> tags);
    }

    /// <summary>
    /// Binds generation metadata to the complete runtime content revision used
    /// to create actors. A generation fingerprint alone cannot identify health,
    /// behavior, cards or footprints. Both revisions must match on every read
    /// and restore; consumers cannot silently substitute another catalog.
    /// </summary>
    public sealed class CommonEncounterSourceContract
    {
        public ICommonEncounterRequestSource Source { get; }
        public string ContentId { get; }
        public string GenerationFingerprint { get; }
        public string RuntimeContentFingerprint { get; }

        public CommonEncounterSourceContract(ICommonEncounterRequestSource source,
            string runtimeContentFingerprint)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            CommonEncounterGenerator.RequireId(source.ContentId, nameof(source));
            RequireFingerprint(source.Fingerprint, nameof(source));
            RequireFingerprint(runtimeContentFingerprint, nameof(runtimeContentFingerprint));
            if (source.Templates == null || source.Templates.Count == 0 ||
                source.Enemies == null || source.Enemies.Count == 0 ||
                source.TemplateTagRequests == null || source.EnemyRequestedLeafTags == null ||
                source.DefaultTemplateLeafTags == null)
                throw new ArgumentException("Formal common encounters require complete nonempty source data.", nameof(source));
            ContentId = source.ContentId;
            GenerationFingerprint = source.Fingerprint;
            RuntimeContentFingerprint = runtimeContentFingerprint;
        }

        /// <summary>Checks the provider did not mutate its identity after binding.</summary>
        public void RequireCurrent()
        {
            if (!string.Equals(Source.ContentId, ContentId, StringComparison.Ordinal) ||
                !string.Equals(Source.Fingerprint, GenerationFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Common encounter source identity changed after binding.");
        }

        internal static void RequireFingerprint(string value, string argument)
        {
            if (value == null || value.Length != 64)
                throw new ArgumentException("A SHA-256 content revision is required.", argument);
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    throw new ArgumentException("Content revisions must use lowercase SHA-256 hex.", argument);
        }
    }
}
