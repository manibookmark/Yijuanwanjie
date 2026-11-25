using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>Owns the ordinary encounter author contract and compilation into
    /// immutable Core inputs. Validation and fingerprints are pure metadata work;
    /// native curves, Run freezing, placement and randomness are separate owners.</summary>
    public static class CommonEncounterContentCompiler
    {
        public static bool TryCompile(CommonEncounterContentData data,
            IReadOnlyDictionary<string, EnemyData> availableEnemies,
            out CommonEncounterContentDefinition definition, out IReadOnlyList<string> errors)
        {
            definition = null;
            var issues = new List<string>();
            if (data == null) issues.Add("dataset: cannot be null.");
            if (availableEnemies == null) issues.Add("availableEnemies: an explicit enemy index is required.");
            if (data != null) Validate(data, availableEnemies, issues);
            errors = Array.AsReadOnly(issues.ToArray());
            if (issues.Count != 0) return false;

            var templates = new List<CommonEncounterTemplate>();
            foreach (CommonEncounterTemplateData template in data.templates)
            {
                var cells = new List<CommonEncounterCellRule>();
                foreach (int cell in template.cells) cells.Add((CommonEncounterCellRule)cell);
                templates.Add(new CommonEncounterTemplate(template.contentId, template.difficulty,
                    template.firstAxisLength, template.secondAxisLength, cells,
                    template.preSelectEnemies, template.allowedLeafTagContentIds));
            }
            var enemies = new List<CommonEncounterEnemy>();
            foreach (CommonEncounterEnemyData enemy in data.enemies)
                enemies.Add(new CommonEncounterEnemy(enemy.enemyContentId, enemy.sourceDifficulty,
                    enemy.allowInTemplate, enemy.dungeonLeafTagContentIds));
            var difficulties = new List<CommonEncounterDifficultyDefinition>();
            foreach (CommonEncounterDifficultyData difficulty in data.difficultyProfiles)
                difficulties.Add(new CommonEncounterDifficultyDefinition(difficulty));
            var requests = new List<CommonEncounterTagRequest>();
            foreach (CommonEncounterTagRequestData request in data.templateTagRequests)
                requests.Add(new CommonEncounterTagRequest(request.contentId, request.invertFilter));
            string fingerprint = Fingerprint(data.contentId, templates, enemies, difficulties,
                requests, data.enemyRequestedLeafTagContentIds, data.defaultTemplateLeafTagContentIds);
            definition = new CommonEncounterContentDefinition(data.contentId, fingerprint,
                templates, enemies, difficulties, requests,
                data.enemyRequestedLeafTagContentIds, data.defaultTemplateLeafTagContentIds);
            return true;
        }

        private static void Validate(CommonEncounterContentData data,
            IReadOnlyDictionary<string, EnemyData> enemies, List<string> errors)
        {
            RequireId(data.contentId, "encounter.", "contentId", errors);
            ValidateTags(data.defaultTemplateLeafTagContentIds, "defaultTemplateLeafTagContentIds", errors);
            ValidateTags(data.enemyRequestedLeafTagContentIds, "enemyRequestedLeafTagContentIds", errors);
            RequireList(data.templates, "templates", errors);
            RequireList(data.enemies, "enemies", errors);
            RequireList(data.difficultyProfiles, "difficultyProfiles", errors);
            RequireList(data.templateTagRequests, "templateTagRequests", errors);
            if (data.difficultyProfiles != null && data.difficultyProfiles.Count == 0)
                errors.Add("difficultyProfiles: at least one explicit difficulty profile is required.");
            var declarations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (data.contentId != null) declarations.Add(data.contentId);
            var templates = new Dictionary<string, CommonEncounterTemplateData>(StringComparer.OrdinalIgnoreCase);
            if (data.templates != null)
                for (int i = 0; i < data.templates.Count; i++)
                {
                    string path = "templates[" + i + "]";
                    CommonEncounterTemplateData template = data.templates[i];
                    if (template == null) { errors.Add(path + ": cannot be null."); continue; }
                    RequireId(template.contentId, "encounter.", path + ".contentId", errors);
                    RequireFinite(template.difficulty, path + ".difficulty", errors);
                    long cellCount = (long)template.firstAxisLength * template.secondAxisLength;
                    if (template.firstAxisLength < 1 || template.secondAxisLength < 1 || cellCount > int.MaxValue)
                        errors.Add(path + ": matrix dimensions must be positive and representable.");
                    RequireList(template.cells, path + ".cells", errors);
                    if (template.cells != null)
                    {
                        if (template.cells.Count != cellCount)
                            errors.Add(path + ".cells: dimensions and cell count differ.");
                        for (int cell = 0; cell < template.cells.Count; cell++)
                            if (template.cells[cell] < 0 || template.cells[cell] > 3)
                                errors.Add(path + ".cells[" + cell + "]: unknown source cell rule.");
                    }
                    ValidateTags(template.allowedLeafTagContentIds, path + ".allowedLeafTagContentIds", errors);
                    if (template.contentId == null) continue;
                    if (templates.TryGetValue(template.contentId, out CommonEncounterTemplateData prior))
                    {
                        if (!SameTemplate(prior, template))
                            errors.Add(path + ": conflicting repeated template contentId " + template.contentId + ".");
                    }
                    else
                    {
                        templates.Add(template.contentId, template);
                        if (!declarations.Add(template.contentId)) errors.Add(path + ": duplicate declared contentId.");
                    }
                }
            var seenEnemies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (data.enemies != null)
                for (int i = 0; i < data.enemies.Count; i++)
                {
                    string path = "enemies[" + i + "]";
                    CommonEncounterEnemyData enemy = data.enemies[i];
                    if (enemy == null) { errors.Add(path + ": cannot be null."); continue; }
                    // Migration IDs stay canonical when they resolve to the
                    // same actual catalog actor; never synthesize an alias.
                    string prefix = enemy.enemyContentId != null &&
                        enemy.enemyContentId.StartsWith("legacy.enemy.", StringComparison.Ordinal)
                        ? "legacy.enemy." : "enemy.";
                    RequireId(enemy.enemyContentId, prefix, path + ".enemyContentId", errors);
                    RequireFinite(enemy.sourceDifficulty, path + ".sourceDifficulty", errors);
                    ValidateTags(enemy.dungeonLeafTagContentIds, path + ".dungeonLeafTagContentIds", errors);
                    if (enemy.enemyContentId == null) continue;
                    if (!seenEnemies.Add(enemy.enemyContentId))
                        errors.Add(path + ": duplicate enemy identity violates the one-source-one-ID pool contract.");
                    if (enemies != null && (!enemies.TryGetValue(enemy.enemyContentId, out EnemyData resolved) ||
                        resolved == null || !string.Equals(resolved.contentId, enemy.enemyContentId, StringComparison.Ordinal)))
                        errors.Add(path + ": enemy reference is absent or is not its canonical contentId: " + enemy.enemyContentId + ".");
                }
            if (data.difficultyProfiles != null)
                for (int i = 0; i < data.difficultyProfiles.Count; i++)
                {
                    string path = "difficultyProfiles[" + i + "]";
                    CommonEncounterDifficultyData difficulty = data.difficultyProfiles[i];
                    if (difficulty == null) { errors.Add(path + ": cannot be null."); continue; }
                    RequireId(difficulty.contentId, "encounter.", path + ".contentId", errors);
                    if (difficulty.contentId != null && !declarations.Add(difficulty.contentId))
                        errors.Add(path + ": duplicate declared contentId.");
                    if (difficulty.enemyRequestedLeafTagContentIds != null)
                        ValidateTags(difficulty.enemyRequestedLeafTagContentIds,
                            path + ".enemyRequestedLeafTagContentIds", errors);
                    RequireFinite(difficulty.minDifficulty, path + ".minDifficulty", errors);
                    RequireFinite(difficulty.maxDifficulty, path + ".maxDifficulty", errors);
                    if (difficulty.minDifficulty > difficulty.maxDifficulty)
                        errors.Add(path + ": minimum difficulty exceeds maximum difficulty.");
                    ValidateCurve(difficulty.minEnemyVariance, path + ".minEnemyVariance", errors);
                    ValidateCurve(difficulty.maxEnemyVariance, path + ".maxEnemyVariance", errors);
                    ValidateCurve(difficulty.minEncounterVariance, path + ".minEncounterVariance", errors);
                    ValidateCurve(difficulty.maxEncounterVariance, path + ".maxEncounterVariance", errors);
                }
            if (data.templateTagRequests != null)
                for (int i = 0; i < data.templateTagRequests.Count; i++)
                {
                    CommonEncounterTagRequestData request = data.templateTagRequests[i];
                    if (request == null) errors.Add("templateTagRequests[" + i + "]: cannot be null.");
                    else RequireId(request.contentId, "tag.", "templateTagRequests[" + i + "].contentId", errors);
                }
        }

        private static void ValidateCurve(CommonEncounterCurveData curve, string path, List<string> errors)
        {
            if (curve == null) { errors.Add(path + ": complete curve is required."); return; }
            // The captured key datasets use these exact native serialized
            // values. Supporting other serialized infinity/rotation conventions
            // requires evidence and a native roundtrip, not public enum casting.
            if (curve.m_PreInfinity != 2 || curve.m_PostInfinity != 2 || curve.m_RotationOrder != 4)
                errors.Add(path + ": supported source serialized infinity/rotation fields are 2/2/4.");
            RequireList(curve.m_Curve, path + ".m_Curve", errors);
            if (curve.m_Curve == null) return;
            if (curve.m_Curve.Count == 0) errors.Add(path + ": at least one explicit key is required.");
            for (int i = 0; i < curve.m_Curve.Count; i++)
            {
                CommonEncounterCurveKeyData key = curve.m_Curve[i];
                string keyPath = path + ".m_Curve[" + i + "]";
                if (key == null) { errors.Add(keyPath + ": cannot be null."); continue; }
                RequireFinite(key.time, keyPath + ".time", errors);
                RequireFinite(key.value, keyPath + ".value", errors);
                RequireTangent(key.inSlope, keyPath + ".inSlope", errors);
                RequireTangent(key.outSlope, keyPath + ".outSlope", errors);
                RequireFinite(key.inWeight, keyPath + ".inWeight", errors);
                RequireFinite(key.outWeight, keyPath + ".outWeight", errors);
                if (key.inWeight < 0f || key.inWeight > 1f || key.outWeight < 0f || key.outWeight > 1f)
                    errors.Add(keyPath + ": weights must be in [0,1].");
                if (key.weightedMode < 0 || key.weightedMode > 3)
                    errors.Add(keyPath + ".weightedMode: unknown weighted mode.");
                if (i > 0 && curve.m_Curve[i - 1] != null && !(key.time > curve.m_Curve[i - 1].time))
                    errors.Add(keyPath + ".time: key times must be strictly increasing; they need not span exactly [0,1].");
            }
        }

        private static void RequireId(string value, string prefix, string path, List<string> errors)
        {
            string[] segments = value?.Split('.');
            bool valid = segments != null && segments.Length >= 3 &&
                value.StartsWith(prefix, StringComparison.Ordinal);
            if (valid)
                for (int i = 0; i < segments.Length; i++)
                {
                    string segment = segments[i];
                    if (segment.Length == 0 || (i < 2 && (segment[0] < 'a' || segment[0] > 'z')))
                    { valid = false; break; }
                    foreach (char character in segment)
                        if (!((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') ||
                            character == '_' || character == '-')) { valid = false; break; }
                    if (!valid) break;
                }
            if (!valid) errors.Add(path + ": explicit canonical stable " + prefix + " identity is required.");
        }

        private static void ValidateTags(List<string> tags, string path, List<string> errors)
        {
            RequireList(tags, path, errors);
            if (tags == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tags.Count; i++)
            {
                RequireId(tags[i], "tag.", path + "[" + i + "]", errors);
                if (tags[i] != null && !seen.Add(tags[i])) errors.Add(path + ": duplicate leaf tag identity.");
            }
        }

        private static void RequireList<T>(List<T> list, string path, List<string> errors)
        { if (list == null) errors.Add(path + ": an explicit array is required."); }
        private static void RequireFinite(float value, string path, List<string> errors)
        { if (!IsFinite(value)) errors.Add(path + ": finite source float is required."); }
        // Unity uses an infinite tangent for a step segment. It is a valid
        // source interpolation atom, not a nonfinite key time/value/weight.
        private static void RequireTangent(float value, string path, List<string> errors)
        { if (float.IsNaN(value)) errors.Add(path + ": tangent cannot be NaN."); }
        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static int FloatBits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        private static bool SameTemplate(CommonEncounterTemplateData left, CommonEncounterTemplateData right)
        {
            if (!string.Equals(left.contentId, right.contentId, StringComparison.Ordinal) ||
                FloatBits(left.difficulty) != FloatBits(right.difficulty) ||
                left.firstAxisLength != right.firstAxisLength || left.secondAxisLength != right.secondAxisLength ||
                left.preSelectEnemies != right.preSelectEnemies || !SameList(left.cells, right.cells) ||
                !SameList(left.allowedLeafTagContentIds, right.allowedLeafTagContentIds)) return false;
            return true;
        }
        private static bool SameList<T>(List<T> left, List<T> right)
        {
            if (left == null || right == null) return left == right;
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
                if (!EqualityComparer<T>.Default.Equals(left[i], right[i])) return false;
            return true;
        }

        internal static IReadOnlyDictionary<string, EnemyData> VisibleEnemies(
            VolumeContent owner, IReadOnlyList<VolumeContent> volumes)
        {
            var result = new Dictionary<string, EnemyData>(StringComparer.Ordinal);
            foreach (VolumeContent volume in volumes)
            {
                if (volume == null || (volume != owner && !string.Equals(volume.volumeId, "common", StringComparison.OrdinalIgnoreCase)))
                    continue;
                foreach (EnemyData enemy in volume.enemies ?? new List<EnemyData>())
                    if (enemy != null && enemy.contentId != null && !result.ContainsKey(enemy.contentId))
                        result.Add(enemy.contentId, enemy);
            }
            return result;
        }

        internal static void ValidateVolume(VolumeContent volume, IReadOnlyList<VolumeContent> volumes,
            HashSet<string> allIds, ContentValidationReport report)
        {
            if (volume.commonEncounters == null) return; // absent schema-5 optional collection
            IReadOnlyDictionary<string, EnemyData> enemies = VisibleEnemies(volume, volumes);
            for (int i = 0; i < volume.commonEncounters.Count; i++)
            {
                CommonEncounterContentData data = volume.commonEncounters[i];
                string path = "volume[" + volume.volumeId + "].commonEncounters[" + i + "]";
                if (!TryCompile(data, enemies, out _, out IReadOnlyList<string> errors))
                    foreach (string error in errors) report.Error(path, error);
                if (data == null) continue;
                var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Register(data.contentId, local, allIds, path, report);
                foreach (CommonEncounterTemplateData template in data.templates ?? new List<CommonEncounterTemplateData>())
                    if (template != null) Register(template.contentId, local, allIds, path, report);
                foreach (CommonEncounterDifficultyData difficulty in data.difficultyProfiles ?? new List<CommonEncounterDifficultyData>())
                    if (difficulty != null) Register(difficulty.contentId, local, allIds, path, report);
            }
        }

        private static void Register(string contentId, HashSet<string> local,
            HashSet<string> allIds, string path, ContentValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(contentId) || !local.Add(contentId)) return;
            if (!allIds.Add(contentId)) report.Error(path, "contentId 重复：" + contentId + "。");
        }

        private static string Fingerprint(string id, List<CommonEncounterTemplate> templates,
            List<CommonEncounterEnemy> enemies, List<CommonEncounterDifficultyDefinition> difficulties,
            List<CommonEncounterTagRequest> requests, List<string> enemyTags, List<string> defaultTags)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write(1); writer.Write(id);
                    WriteStrings(writer, defaultTags); WriteStrings(writer, enemyTags);
                    writer.Write(requests.Count);
                    foreach (CommonEncounterTagRequest request in requests)
                    { writer.Write(request.ContentId); writer.Write(request.InvertFilter); }
                    writer.Write(templates.Count);
                    foreach (CommonEncounterTemplate template in templates)
                    {
                        writer.Write(template.ContentId); writer.Write(FloatBits(template.Difficulty));
                        writer.Write(template.FirstAxisLength); writer.Write(template.SecondAxisLength);
                        writer.Write(template.PreSelectEnemies); WriteStrings(writer, template.AllowedLeafTags);
                        writer.Write(template.Cells.Count);
                        foreach (CommonEncounterCellRule cell in template.Cells) writer.Write((int)cell);
                    }
                    writer.Write(enemies.Count);
                    foreach (CommonEncounterEnemy enemy in enemies)
                    {
                        writer.Write(enemy.ContentId); writer.Write(FloatBits(enemy.Difficulty));
                        writer.Write(enemy.AllowInTemplate); WriteStrings(writer, enemy.DungeonLeafTags);
                    }
                    writer.Write(difficulties.Count);
                    foreach (CommonEncounterDifficultyDefinition difficulty in difficulties)
                    {
                        writer.Write(difficulty.ContentId); writer.Write(FloatBits(difficulty.MinDifficulty));
                        writer.Write(FloatBits(difficulty.MaxDifficulty));
                        WriteCurve(writer, difficulty.MinEnemyVariance); WriteCurve(writer, difficulty.MaxEnemyVariance);
                        WriteCurve(writer, difficulty.MinEncounterVariance); WriteCurve(writer, difficulty.MaxEncounterVariance);
                    }
                    // Absent overrides retain the exact v1 stream used by old
                    // frozen rosters. Explicit empty filters must be signed too.
                    int overrideCount = 0;
                    foreach (CommonEncounterDifficultyDefinition difficulty in difficulties)
                        if (difficulty.EnemyRequestedLeafTags != null) overrideCount++;
                    if (overrideCount != 0)
                    {
                        writer.Write("difficulty-enemy-tag-overrides"); writer.Write(1);
                        writer.Write(overrideCount);
                        foreach (CommonEncounterDifficultyDefinition difficulty in difficulties)
                            if (difficulty.EnemyRequestedLeafTags != null)
                            {
                                writer.Write(difficulty.ContentId);
                                WriteStrings(writer, difficulty.EnemyRequestedLeafTags);
                            }
                    }
                }
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream.ToArray());
                    var text = new StringBuilder(hash.Length * 2);
                    foreach (byte value in hash) text.Append(value.ToString("x2"));
                    return text.ToString();
                }
            }
        }

        private static void WriteStrings(BinaryWriter writer, IReadOnlyList<string> values)
        { writer.Write(values.Count); foreach (string value in values) writer.Write(value); }
        private static void WriteCurve(BinaryWriter writer, CommonEncounterCurveDefinition curve)
        {
            writer.Write(curve.SerializedPreInfinity); writer.Write(curve.SerializedPostInfinity);
            writer.Write(curve.RotationOrder); writer.Write(curve.Keys.Count);
            foreach (CommonEncounterCurveKeyDefinition key in curve.Keys)
            {
                writer.Write(FloatBits(key.Time)); writer.Write(FloatBits(key.Value));
                writer.Write(FloatBits(key.InSlope)); writer.Write(FloatBits(key.OutSlope));
                writer.Write(key.WeightedMode); writer.Write(FloatBits(key.InWeight)); writer.Write(FloatBits(key.OutWeight));
            }
        }
    }
}
