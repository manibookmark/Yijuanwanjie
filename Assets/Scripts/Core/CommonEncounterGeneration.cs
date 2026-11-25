using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum CommonEncounterCellRule
    {
        Empty = 0,
        Baseline = 1,
        LowerDifficulty = 2,
        HigherDifficulty = 3
    }

    public enum CommonEncounterGenerationStatus { Generated, NoLegalTemplate }

    /// <summary>Already evaluated curve values, not a symmetric radius. Either
    /// endpoint may be negative; the generator does not reinterpret its sign.</summary>
    public readonly struct CommonEncounterVariance
    {
        public float Min { get; }
        public float Max { get; }
        public CommonEncounterVariance(float min, float max)
        {
            CommonEncounterGenerator.RequireFinite(min, nameof(min));
            CommonEncounterGenerator.RequireFinite(max, nameof(max));
            Min = min; Max = max;
        }
    }

    public readonly struct CommonEncounterTagRequest
    {
        public string ContentId { get; }
        public bool InvertFilter { get; }
        public CommonEncounterTagRequest(string contentId, bool invertFilter = false)
        {
            CommonEncounterGenerator.RequireId(contentId, nameof(contentId));
            ContentId = contentId; InvertFilter = invertFilter;
        }
    }

    /// <summary>Immutable source matrix. Flattened cells use
    /// firstAxis * SecondAxisLength + secondAxis. Allowed tags must already be
    /// expanded to leaf IDs by the content boundary; an empty set uses the
    /// request's explicit default leaves, not a hard-coded dungeon identity.</summary>
    public sealed class CommonEncounterTemplate
    {
        public string ContentId { get; }
        public float Difficulty { get; }
        public int FirstAxisLength { get; }
        public int SecondAxisLength { get; }
        public bool PreSelectEnemies { get; }
        public IReadOnlyList<CommonEncounterCellRule> Cells { get; }
        public IReadOnlyList<string> AllowedLeafTags { get; }

        public CommonEncounterTemplate(string contentId, float difficulty,
            int firstAxisLength, int secondAxisLength,
            IReadOnlyList<CommonEncounterCellRule> cellsFirstAxisMajor,
            bool preSelectEnemies, IReadOnlyList<string> allowedLeafTags = null)
        {
            CommonEncounterGenerator.RequireId(contentId, nameof(contentId));
            CommonEncounterGenerator.RequireFinite(difficulty, nameof(difficulty));
            if (firstAxisLength < 1 || secondAxisLength < 1 ||
                (long)firstAxisLength * secondAxisLength > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(firstAxisLength), "Matrix dimensions must be positive and representable.");
            int count = firstAxisLength * secondAxisLength;
            if (cellsFirstAxisMajor == null || cellsFirstAxisMajor.Count != count)
                throw new ArgumentException("Matrix dimensions and cell count do not match.", nameof(cellsFirstAxisMajor));
            var cells = new CommonEncounterCellRule[count];
            for (int i = 0; i < count; i++)
            {
                CommonEncounterCellRule rule = cellsFirstAxisMajor[i];
                if (rule < CommonEncounterCellRule.Empty || rule > CommonEncounterCellRule.HigherDifficulty)
                    throw new ArgumentException("Unknown common encounter cell rule.", nameof(cellsFirstAxisMajor));
                cells[i] = rule;
            }
            ContentId = contentId; Difficulty = difficulty;
            FirstAxisLength = firstAxisLength; SecondAxisLength = secondAxisLength;
            PreSelectEnemies = preSelectEnemies;
            Cells = Array.AsReadOnly(cells);
            AllowedLeafTags = CommonEncounterGenerator.CopyTags(allowedLeafTags, nameof(allowedLeafTags));
        }
    }

    /// <summary>Generation difficulty is the source float, independent of the
    /// actor's integer difficulty, health, footprint and live combat state.
    /// This is a project canonical-ID descriptor: Content must verify that each
    /// ContentId maps to exactly one source asset before constructing the pool.
    /// Two distinct source assets mapping to one ID are ambiguous and must be
    /// rejected there, even if their fields match. Descriptor equality cannot
    /// prove Unity object identity; it is not native List.Contains semantics.</summary>
    public sealed class CommonEncounterEnemy
    {
        public string ContentId { get; }
        public float Difficulty { get; }
        public bool AllowInTemplate { get; }
        public IReadOnlyList<string> DungeonLeafTags { get; }
        public CommonEncounterEnemy(string contentId, float difficulty,
            bool allowInTemplate, IReadOnlyList<string> dungeonLeafTags = null)
        {
            CommonEncounterGenerator.RequireId(contentId, nameof(contentId));
            CommonEncounterGenerator.RequireFinite(difficulty, nameof(difficulty));
            ContentId = contentId; Difficulty = difficulty; AllowInTemplate = allowInTemplate;
            DungeonLeafTags = CommonEncounterGenerator.CopyTags(dungeonLeafTags, nameof(dungeonLeafTags));
        }
    }

    /// <summary>Template filters are conjunctive and may invert. Enemy filters
    /// are a separate Any-match leaf request; empty enemy requests do not filter.
    /// This separation preserves the two source contracts without inferring a
    /// physical Room position, a curve or a shared random stream.</summary>
    public sealed class CommonEncounterRequest
    {
        public float TileDifficulty { get; }
        public CommonEncounterVariance EncounterVariance { get; }
        public CommonEncounterVariance EnemyVariance { get; }
        public IReadOnlyList<CommonEncounterTagRequest> TemplateTagRequests { get; }
        public IReadOnlyList<string> EnemyRequestedLeafTags { get; }
        public IReadOnlyList<string> DefaultTemplateLeafTags { get; }
        public CommonEncounterRequest(float tileDifficulty,
            CommonEncounterVariance encounterVariance, CommonEncounterVariance enemyVariance,
            IReadOnlyList<CommonEncounterTagRequest> templateTagRequests = null,
            IReadOnlyList<string> enemyRequestedLeafTags = null,
            IReadOnlyList<string> defaultTemplateLeafTags = null)
        {
            CommonEncounterGenerator.RequireFinite(tileDifficulty, nameof(tileDifficulty));
            var tags = new CommonEncounterTagRequest[templateTagRequests?.Count ?? 0];
            for (int i = 0; i < tags.Length; i++)
            {
                CommonEncounterGenerator.RequireId(templateTagRequests[i].ContentId, nameof(templateTagRequests));
                tags[i] = templateTagRequests[i];
            }
            TileDifficulty = tileDifficulty; EncounterVariance = encounterVariance; EnemyVariance = enemyVariance;
            TemplateTagRequests = Array.AsReadOnly(tags);
            EnemyRequestedLeafTags = CommonEncounterGenerator.CopyTags(enemyRequestedLeafTags, nameof(enemyRequestedLeafTags));
            DefaultTemplateLeafTags = CommonEncounterGenerator.CopyTags(defaultTemplateLeafTags, nameof(defaultTemplateLeafTags));
        }
    }

    public readonly struct CommonEncounterRosterEntry
    {
        public string ContentId { get; }
        public int FirstAxis { get; }
        public int SecondAxis { get; }
        internal CommonEncounterRosterEntry(string contentId, int firstAxis, int secondAxis)
        { ContentId = contentId; FirstAxis = firstAxis; SecondAxis = secondAxis; }
    }

    /// <summary>Logical output, not an executable battle or a physical placement.
    /// Empty cells remain null, even when a template was found but no enemy is
    /// eligible. Roster order is second axis descending, then first ascending.
    /// Consumers must reject an empty formal battle at their admission boundary.</summary>
    public sealed class CommonEncounterResult
    {
        public CommonEncounterGenerationStatus Status { get; }
        public string TemplateContentId { get; }
        public int FirstAxisLength { get; }
        public int SecondAxisLength { get; }
        public IReadOnlyList<string> Cells { get; }
        public IReadOnlyList<CommonEncounterRosterEntry> Roster { get; }
        internal CommonEncounterResult(CommonEncounterTemplate template, string[] cells,
            List<CommonEncounterRosterEntry> roster)
        {
            Status = template == null ? CommonEncounterGenerationStatus.NoLegalTemplate : CommonEncounterGenerationStatus.Generated;
            TemplateContentId = template?.ContentId;
            FirstAxisLength = template?.FirstAxisLength ?? 0;
            SecondAxisLength = template?.SecondAxisLength ?? 0;
            Cells = Array.AsReadOnly((string[])cells.Clone());
            Roster = Array.AsReadOnly(roster.ToArray());
        }

        public string GetCell(int firstAxis, int secondAxis)
        {
            if (firstAxis < 0 || firstAxis >= FirstAxisLength)
                throw new ArgumentOutOfRangeException(nameof(firstAxis));
            if (secondAxis < 0 || secondAxis >= SecondAxisLength)
                throw new ArgumentOutOfRangeException(nameof(secondAxis));
            return Cells[firstAxis * SecondAxisLength + secondAxis];
        }
    }

    /// <summary>
    /// Pure common encounter selection: owns tag admission, template/baseline
    /// selection, inclusive pools, weighted draws and logical traversal. Models
    /// are in this file because their invariants are the algorithm's contract;
    /// no Run, Content lookup, physical formation, persistence or mutable actor
    /// state is owned here. All source order is supplied explicitly and retained,
    /// including every repeated template occurrence and its sampling weight.
    /// Enemy ID deduplication is a project canonical-ID input adaptation, valid
    /// only with Content's one-source-asset-per-ID guarantee; it does not claim
    /// native reference deduplication for arbitrary distinct Unity assets.
    ///
    /// The caller must supply an IRandomSource. The project adapter's sequence is
    /// not asserted equivalent to native Unity.Mathematics.Random, value-copy or
    /// cross-call advancement. Curves are already evaluated; source leaf-tag
    /// expansion, production locations/capacity and floor freezing are external.
    /// Re-review this single knowledge boundary if new placement, Run mutation
    /// or an independent random lifecycle is proposed; do not add it here.
    /// </summary>
    public static class CommonEncounterGenerator
    {
        public const int AlgorithmVersion = 1;
        private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>Consumes randomness only for an in-range template choice and
        /// nonempty weighted enemy pools. Matching repeated templates retain all
        /// occurrences in input order. Canonical enemy IDs collapse to their first
        /// occurrence under the unique-source mapping premise above; conflicting
        /// definitions fail before any draw. No legal template returns an explicit
        /// status without a draw.</summary>
        public static CommonEncounterResult Generate(
            IReadOnlyList<CommonEncounterTemplate> templates,
            IReadOnlyList<CommonEncounterEnemy> enemies,
            CommonEncounterRequest request, IRandomSource random)
        {
            if (templates == null) throw new ArgumentNullException(nameof(templates));
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (random == null) throw new ArgumentNullException(nameof(random));
            List<CommonEncounterTemplate> orderedTemplates = ValidateTemplates(templates);
            List<CommonEncounterEnemy> orderedEnemies = DistinctEnemies(enemies);
            var legalTemplates = new List<CommonEncounterTemplate>();
            foreach (CommonEncounterTemplate template in orderedTemplates)
                if (MeetsTemplateTags(template, request)) legalTemplates.Add(template);
            CommonEncounterTemplate selected = SelectTemplate(legalTemplates, request, random);
            if (selected == null)
                return new CommonEncounterResult(null, Array.Empty<string>(), new List<CommonEncounterRosterEntry>());

            var pool = new List<CommonEncounterEnemy>();
            foreach (CommonEncounterEnemy enemy in orderedEnemies)
                if (enemy.AllowInTemplate && MeetsEnemyTags(enemy, request.EnemyRequestedLeafTags)) pool.Add(enemy);
            var cells = new string[selected.FirstAxisLength * selected.SecondAxisLength];
            var roster = new List<CommonEncounterRosterEntry>();
            if (pool.Count == 0) return new CommonEncounterResult(selected, cells, roster);

            CommonEncounterEnemy baseline = SelectBaseline(pool, request.TileDifficulty);
            List<CommonEncounterEnemy> lower = FindPool(pool,
                Add(request.TileDifficulty, request.EnemyVariance.Min), request.TileDifficulty);
            List<CommonEncounterEnemy> higher = FindPool(pool,
                request.TileDifficulty, Add(request.TileDifficulty, request.EnemyVariance.Max));
            CommonEncounterEnemy cachedLower = null, cachedHigher = null;
            for (int second = selected.SecondAxisLength - 1; second >= 0; second--)
            {
                for (int first = 0; first < selected.FirstAxisLength; first++)
                {
                    int index = first * selected.SecondAxisLength + second;
                    CommonEncounterEnemy enemy;
                    switch (selected.Cells[index])
                    {
                        case CommonEncounterCellRule.Empty: continue;
                        case CommonEncounterCellRule.Baseline: enemy = baseline; break;
                        case CommonEncounterCellRule.LowerDifficulty:
                            enemy = selected.PreSelectEnemies && cachedLower != null ? cachedLower :
                                SelectWeighted(lower, baseline, request.TileDifficulty, request.EnemyVariance.Min, random);
                            if (selected.PreSelectEnemies) cachedLower = enemy;
                            break;
                        case CommonEncounterCellRule.HigherDifficulty:
                            enemy = selected.PreSelectEnemies && cachedHigher != null ? cachedHigher :
                                SelectWeighted(higher, baseline, request.TileDifficulty, request.EnemyVariance.Max, random);
                            if (selected.PreSelectEnemies) cachedHigher = enemy;
                            break;
                        default: throw new InvalidOperationException("Unknown immutable cell rule.");
                    }
                    cells[index] = enemy.ContentId;
                    roster.Add(new CommonEncounterRosterEntry(enemy.ContentId, first, second));
                }
            }
            return new CommonEncounterResult(selected, cells, roster);
        }

        /// <summary>
        /// Validates a persisted logical result without selecting, evaluating
        /// curves or consuming randomness. Reuses this owner's admission and
        /// difficulty pools; stored request values are already frozen binary32.
        /// This checks legality/integrity, not native RNG equivalence or a roll.
        /// </summary>
        public static void ValidateFrozen(IReadOnlyList<CommonEncounterTemplate> templates,
            IReadOnlyList<CommonEncounterEnemy> enemies, CommonEncounterRequest request,
            CommonEncounterFrozenRoster frozen)
        {
            if (templates == null || enemies == null || request == null || frozen == null)
                throw new ArgumentNullException("Frozen validation inputs must be explicit.");
            List<CommonEncounterTemplate> validated = ValidateTemplates(templates);
            var legal = new List<CommonEncounterTemplate>();
            CommonEncounterTemplate selected = null;
            foreach (CommonEncounterTemplate template in validated)
            {
                if (!MeetsTemplateTags(template, request)) continue;
                legal.Add(template);
                if (IdComparer.Equals(template.ContentId, frozen.TemplateContentId)) selected = template;
            }
            if (selected == null || selected.FirstAxisLength != frozen.FirstAxisLength ||
                selected.SecondAxisLength != frozen.SecondAxisLength)
                throw new ArgumentException("Frozen template identity, tags or dimensions differ.", nameof(frozen));
            float templateMin = Add(request.TileDifficulty, request.EncounterVariance.Min);
            float templateMax = Add(request.TileDifficulty, request.EncounterVariance.Max);
            bool hasWithin = false;
            foreach (CommonEncounterTemplate template in legal)
                if (template.Difficulty >= templateMin && template.Difficulty <= templateMax) hasWithin = true;
            if (hasWithin)
            {
                if (selected.Difficulty < templateMin || selected.Difficulty > templateMax)
                    throw new ArgumentException("Frozen template was outside the selectable range.", nameof(frozen));
            }
            else if (!IdComparer.Equals(SelectTemplate(legal, request, null).ContentId, selected.ContentId))
                throw new ArgumentException("Frozen fallback template differs from the first nearest source occurrence.", nameof(frozen));
            var pool = new List<CommonEncounterEnemy>();
            foreach (CommonEncounterEnemy enemy in DistinctEnemies(enemies))
                if (enemy.AllowInTemplate && MeetsEnemyTags(enemy, request.EnemyRequestedLeafTags)) pool.Add(enemy);
            if (pool.Count == 0) throw new ArgumentException("Frozen formal encounter has no eligible enemy pool.", nameof(frozen));
            CommonEncounterEnemy baseline = SelectBaseline(pool, request.TileDifficulty);
            List<CommonEncounterEnemy> lower = FindPool(pool, Add(request.TileDifficulty, request.EnemyVariance.Min), request.TileDifficulty);
            List<CommonEncounterEnemy> higher = FindPool(pool, request.TileDifficulty, Add(request.TileDifficulty, request.EnemyVariance.Max));
            string sharedLower = null, sharedHigher = null;
            for (int i = 0; i < selected.Cells.Count; i++)
            {
                string id = frozen.Cells[i];
                CommonEncounterCellRule rule = selected.Cells[i];
                if (rule == CommonEncounterCellRule.Empty)
                {
                    if (id != null) throw new ArgumentException("An empty source cell acquired an actor.", nameof(frozen));
                    continue;
                }
                if (id == null) throw new ArgumentException("An occupied source rule lost its actor.", nameof(frozen));
                bool allowed = rule == CommonEncounterCellRule.Baseline
                    ? IdComparer.Equals(id, baseline.ContentId)
                    : ContainsEnemy(rule == CommonEncounterCellRule.LowerDifficulty ? lower : higher, baseline, id);
                if (!allowed) throw new ArgumentException("Frozen enemy is outside its source rule pool.", nameof(frozen));
                if (!selected.PreSelectEnemies) continue;
                if (rule == CommonEncounterCellRule.LowerDifficulty)
                {
                    if (sharedLower != null && !IdComparer.Equals(sharedLower, id))
                        throw new ArgumentException("Frozen preselected lower enemies differ.", nameof(frozen));
                    sharedLower = id;
                }
                if (rule == CommonEncounterCellRule.HigherDifficulty)
                {
                    if (sharedHigher != null && !IdComparer.Equals(sharedHigher, id))
                        throw new ArgumentException("Frozen preselected higher enemies differ.", nameof(frozen));
                    sharedHigher = id;
                }
            }
        }

        private static bool ContainsEnemy(List<CommonEncounterEnemy> pool, CommonEncounterEnemy fallback, string id)
        {
            if (pool.Count == 0) return IdComparer.Equals(id, fallback.ContentId);
            foreach (CommonEncounterEnemy enemy in pool) if (IdComparer.Equals(enemy.ContentId, id)) return true;
            return false;
        }

        private static CommonEncounterTemplate SelectTemplate(List<CommonEncounterTemplate> legal,
            CommonEncounterRequest request, IRandomSource random)
        {
            if (legal.Count == 0) return null;
            float min = Add(request.TileDifficulty, request.EncounterVariance.Min);
            float max = Add(request.TileDifficulty, request.EncounterVariance.Max);
            var within = new List<CommonEncounterTemplate>();
            foreach (CommonEncounterTemplate template in legal)
                if (template.Difficulty >= min && template.Difficulty <= max) within.Add(template);
            if (within.Count > 0)
            {
                int index = random.Range(0, within.Count);
                if (index < 0 || index >= within.Count)
                    throw new InvalidOperationException("Random source returned an invalid template index.");
                return within[index];
            }
            float span = Subtract(max, min);
            float midpoint = Add(min, (float)((double)span * 0.5));
            CommonEncounterTemplate nearest = legal[0];
            float distance = Math.Abs(Subtract(nearest.Difficulty, midpoint));
            for (int i = 1; i < legal.Count; i++)
            {
                float candidateDistance = Math.Abs(Subtract(legal[i].Difficulty, midpoint));
                if (candidateDistance < distance) { nearest = legal[i]; distance = candidateDistance; }
            }
            return nearest;
        }

        private static CommonEncounterEnemy SelectBaseline(List<CommonEncounterEnemy> pool, float difficulty)
        {
            CommonEncounterEnemy nearest = pool[0];
            float distance = Math.Abs(Subtract(nearest.Difficulty, difficulty));
            for (int i = 1; i < pool.Count; i++)
            {
                float candidateDistance = Math.Abs(Subtract(pool[i].Difficulty, difficulty));
                if (candidateDistance < distance) { nearest = pool[i]; distance = candidateDistance; }
            }
            return nearest;
        }

        private static List<CommonEncounterEnemy> FindPool(List<CommonEncounterEnemy> pool, float min, float max)
        {
            var result = new List<CommonEncounterEnemy>();
            foreach (CommonEncounterEnemy enemy in pool)
                if (enemy.Difficulty >= min && enemy.Difficulty <= max) result.Add(enemy);
            return result;
        }

        private static CommonEncounterEnemy SelectWeighted(List<CommonEncounterEnemy> pool,
            CommonEncounterEnemy baseline, float difficulty, float maxDelta, IRandomSource random)
        {
            if (pool.Count == 0) return baseline;
            var weights = new float[pool.Count];
            float total = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                weights[i] = Subtract(Math.Abs(maxDelta), Math.Abs(Subtract(pool[i].Difficulty, difficulty)));
                total = Add(total, weights[i]);
            }
            float draw = random.Range(0f, total);
            RequireFinite(draw, nameof(random));
            float cumulative = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                cumulative = Add(cumulative, weights[i]);
                // This is inclusive, including draw==0 with zero cumulative;
                // health-action selection's strict greater-than is different.
                if (cumulative >= draw) return pool[i];
            }
            return pool[pool.Count - 1];
        }

        private static bool MeetsTemplateTags(CommonEncounterTemplate template, CommonEncounterRequest request)
        {
            IReadOnlyList<string> tags = template.AllowedLeafTags.Count == 0
                ? request.DefaultTemplateLeafTags : template.AllowedLeafTags;
            foreach (CommonEncounterTagRequest filter in request.TemplateTagRequests)
                if (HasTag(tags, filter.ContentId) == filter.InvertFilter) return false;
            return true;
        }

        private static bool MeetsEnemyTags(CommonEncounterEnemy enemy, IReadOnlyList<string> requested)
        {
            if (requested.Count == 0) return true;
            foreach (string tag in requested) if (HasTag(enemy.DungeonLeafTags, tag)) return true;
            return false;
        }

        private static bool HasTag(IReadOnlyList<string> tags, string id)
        {
            foreach (string tag in tags) if (IdComparer.Equals(tag, id)) return true;
            return false;
        }

        private static List<CommonEncounterTemplate> ValidateTemplates(IReadOnlyList<CommonEncounterTemplate> source)
        {
            var byId = new Dictionary<string, CommonEncounterTemplate>(IdComparer);
            var result = new List<CommonEncounterTemplate>();
            foreach (CommonEncounterTemplate item in source)
            {
                if (item == null) throw new ArgumentException("Template list contains null.", nameof(source));
                if (byId.TryGetValue(item.ContentId, out CommonEncounterTemplate prior))
                {
                    if (!SameTemplate(prior, item))
                        throw new ArgumentException("Conflicting template identity: " + item.ContentId, nameof(source));
                }
                else byId.Add(item.ContentId, item);
                // Native MultiDatabase aggregation/filtering retains duplicates;
                // each occurrence contributes an independent random-index slot.
                result.Add(item);
            }
            return result;
        }

        private static List<CommonEncounterEnemy> DistinctEnemies(IReadOnlyList<CommonEncounterEnemy> source)
        {
            var byId = new Dictionary<string, CommonEncounterEnemy>(IdComparer);
            var result = new List<CommonEncounterEnemy>();
            foreach (CommonEncounterEnemy item in source)
            {
                if (item == null) throw new ArgumentException("Enemy list contains null.", nameof(source));
                if (byId.TryGetValue(item.ContentId, out CommonEncounterEnemy prior))
                {
                    if (prior.Difficulty != item.Difficulty || prior.AllowInTemplate != item.AllowInTemplate ||
                        !SameTags(prior.DungeonLeafTags, item.DungeonLeafTags))
                        throw new ArgumentException("Conflicting enemy identity: " + item.ContentId, nameof(source));
                    continue;
                }
                byId.Add(item.ContentId, item); result.Add(item);
            }
            return result;
        }

        private static bool SameTemplate(CommonEncounterTemplate left, CommonEncounterTemplate right)
        {
            if (left.Difficulty != right.Difficulty || left.FirstAxisLength != right.FirstAxisLength ||
                left.SecondAxisLength != right.SecondAxisLength || left.PreSelectEnemies != right.PreSelectEnemies ||
                !SameTags(left.AllowedLeafTags, right.AllowedLeafTags)) return false;
            for (int i = 0; i < left.Cells.Count; i++) if (left.Cells[i] != right.Cells[i]) return false;
            return true;
        }

        private static bool SameTags(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count) return false;
            foreach (string tag in left) if (!HasTag(right, tag)) return false;
            return true;
        }

        internal static IReadOnlyList<string> CopyTags(IReadOnlyList<string> source, string argument)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(IdComparer);
            foreach (string tag in source ?? Array.Empty<string>())
            {
                RequireId(tag, argument);
                if (seen.Add(tag)) result.Add(tag);
            }
            return Array.AsReadOnly(result.ToArray());
        }

        internal static void RequireId(string value, string argument)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Stable content identity is required.", argument);
            foreach (char character in value)
                if (char.IsWhiteSpace(character) || char.IsControl(character))
                    throw new ArgumentException("Stable identity cannot contain whitespace or control characters.", argument);
        }

        internal static void RequireFinite(float value, string argument)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(argument, "Common encounter values must be finite.");
        }

        // Explicit binary32 rounding after each source add/subtract; a widened
        // accumulator would alter inclusive boundaries and small weight sums.
        private static float Add(float left, float right)
        {
            float result = (float)((double)left + right);
            RequireFinite(result, "computed difficulty or weight");
            return result;
        }

        private static float Subtract(float left, float right)
        {
            float result = (float)((double)left - right);
            RequireFinite(result, "computed difficulty or weight");
            return result;
        }
    }
}
