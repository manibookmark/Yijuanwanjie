using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// Owns the exact binary32 boundary of common encounter author JSON. Unity
    /// still deserializes the existing DTO; this codec pairs its arrays with the
    /// original wire order and repairs only a literal negative zero read as +0.
    /// Optional enemy filters retain absent/null versus explicit array shape.
    /// Other lost bits, missing numbers and underflow fail before compilation.
    /// It retains no raw volume, source copy, cache, native curve or Run state.
    /// </summary>
    internal static class CommonEncounterJsonCodec
    {
        private static readonly Regex Number = new Regex(
            @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$",
            RegexOptions.CultureInvariant);
        private static readonly float NegativeZero =
            BitConverter.ToSingle(BitConverter.GetBytes(unchecked((int)0x80000000)), 0);
        private const string EnemyTagsField = "enemyRequestedLeafTagContentIds";
        private static readonly Regex ObjectKey = new Regex(
            @"""((?:\\.|[^""\\])*)""\s*:", RegexOptions.CultureInvariant);

        internal static void RestoreAuthorFloatBits(string volumeJson,
            VolumeContent parsed, string sourceLabel)
        {
            if (parsed == null) throw new ArgumentNullException(nameof(parsed));
            string path = (sourceLabel ?? "<content>") + ".commonEncounters";
            if (!JsonObjectShape.TryGetField(volumeJson, "commonEncounters", out string raw))
            {
                if (parsed.commonEncounters != null)
                    throw Error(path, "cannot find one explicit canonical array in the original JSON.");
                return; // Old volumes have no optional extension.
            }
            IReadOnlyList<string> datasets = RequireArray(raw, parsed.commonEncounters, path);
            for (int i = 0; i < datasets.Count; i++)
            {
                string itemPath = path + "[" + i + "]";
                CommonEncounterContentData data = parsed.commonEncounters[i];
                string json = datasets[i];
                IReadOnlyList<string> templates = RequireArrayField(json,
                    "templates", data.templates, itemPath);
                for (int t = 0; t < templates.Count; t++)
                    data.templates[t].difficulty = ReadFloat(templates[t], "difficulty",
                        data.templates[t].difficulty, itemPath + ".templates[" + t + "]");

                IReadOnlyList<string> enemies = RequireArrayField(json,
                    "enemies", data.enemies, itemPath);
                for (int e = 0; e < enemies.Count; e++)
                    data.enemies[e].sourceDifficulty = ReadFloat(enemies[e], "sourceDifficulty",
                        data.enemies[e].sourceDifficulty, itemPath + ".enemies[" + e + "]");

                IReadOnlyList<string> difficulties = RequireArrayField(json,
                    "difficultyProfiles", data.difficultyProfiles, itemPath);
                for (int d = 0; d < difficulties.Count; d++)
                {
                    string difficultyPath = itemPath + ".difficultyProfiles[" + d + "]";
                    CommonEncounterDifficultyData difficulty = data.difficultyProfiles[d];
                    string difficultyJson = difficulties[d];
                    ReadEnemyTagOverride(difficultyJson, difficulty, difficultyPath);
                    difficulty.minDifficulty = ReadFloat(difficultyJson, "minDifficulty",
                        difficulty.minDifficulty, difficultyPath);
                    difficulty.maxDifficulty = ReadFloat(difficultyJson, "maxDifficulty",
                        difficulty.maxDifficulty, difficultyPath);
                    ReadCurve(difficultyJson, "minEnemyVariance", difficulty.minEnemyVariance, difficultyPath);
                    ReadCurve(difficultyJson, "maxEnemyVariance", difficulty.maxEnemyVariance, difficultyPath);
                    ReadCurve(difficultyJson, "minEncounterVariance", difficulty.minEncounterVariance, difficultyPath);
                    ReadCurve(difficultyJson, "maxEncounterVariance", difficulty.maxEncounterVariance, difficultyPath);
                }
            }
        }

        private static void ReadEnemyTagOverride(string json,
            CommonEncounterDifficultyData difficulty, string parentPath)
        {
            string path = parentPath + "." + EnemyTagsField;
            int fields = 0;
            foreach (Match key in ObjectKey.Matches(json))
                if (string.Equals(Regex.Unescape(key.Groups[1].Value), EnemyTagsField,
                    StringComparison.Ordinal))
                {
                    if (++fields != 1 || key.Groups[1].Value != EnemyTagsField)
                        throw Error(path, "one canonical optional field is required when present.");
                }
            if (!JsonObjectShape.TryGetField(json, EnemyTagsField, out string raw))
            {
                // Unity can synthesize an empty collection when the field is
                // absent. Duplicate or escaped keys must not become inheritance.
                if (fields != 0)
                    throw Error(path, "one canonical optional field is required when present.");
                difficulty.enemyRequestedLeafTagContentIds = null;
                return;
            }
            if (string.Equals(raw, "null", StringComparison.Ordinal))
            {
                difficulty.enemyRequestedLeafTagContentIds = null;
                return;
            }
            List<string> values = difficulty.enemyRequestedLeafTagContentIds;
            if (values == null || !JsonObjectShape.TryGetArrayItems(raw, out IReadOnlyList<string> items) ||
                items.Count != values.Count)
                throw Error(path, "the explicit tag array must retain its parsed count and order.");
            for (int i = 0; i < items.Count; i++)
            {
                string item = items[i];
                if (item.Length < 2 || item[0] != '"' || item[item.Length - 1] != '"' ||
                    !string.Equals(Regex.Unescape(item.Substring(1, item.Length - 2)), values[i],
                        StringComparison.Ordinal))
                    throw Error(path + "[" + i + "]", "the parsed leaf tag differs from the original JSON.");
            }
        }

        private static void ReadCurve(string parentJson, string field,
            CommonEncounterCurveData curve, string parentPath)
        {
            string path = parentPath + "." + field;
            if (curve == null || !JsonObjectShape.TryGetField(parentJson, field, out string raw) ||
                string.IsNullOrWhiteSpace(raw) || raw[0] != '{')
                throw Error(path, "an explicit nonnull curve object is required.");
            IReadOnlyList<string> keys = RequireArrayField(raw, "m_Curve", curve.m_Curve, path);
            for (int i = 0; i < keys.Count; i++)
            {
                string keyPath = path + ".m_Curve[" + i + "]";
                CommonEncounterCurveKeyData key = curve.m_Curve[i];
                key.time = ReadFloat(keys[i], "time", key.time, keyPath);
                key.value = ReadFloat(keys[i], "value", key.value, keyPath);
                key.inSlope = ReadTangent(keys[i], "inSlope", key.inSlope, keyPath);
                key.outSlope = ReadTangent(keys[i], "outSlope", key.outSlope, keyPath);
                key.inWeight = ReadFloat(keys[i], "inWeight", key.inWeight, keyPath);
                key.outWeight = ReadFloat(keys[i], "outWeight", key.outWeight, keyPath);
            }
        }

        private static IReadOnlyList<string> RequireArrayField<T>(string parentJson,
            string field, IReadOnlyList<T> values, string parentPath) where T : class
        {
            string path = parentPath + "." + field;
            if (!JsonObjectShape.TryGetField(parentJson, field, out string raw))
                throw Error(path, "one explicit canonical array is required.");
            return RequireArray(raw, values, path);
        }

        private static IReadOnlyList<string> RequireArray<T>(string raw,
            IReadOnlyList<T> values, string path) where T : class
        {
            if (values == null || !JsonObjectShape.TryGetArrayItems(raw, out IReadOnlyList<string> items))
                throw Error(path, "a nonnull array is required.");
            if (items.Count != values.Count)
                throw Error(path, "raw and parsed array counts differ; positional pairing is required.");
            for (int i = 0; i < items.Count; i++)
                if (values[i] == null || string.IsNullOrWhiteSpace(items[i]) || items[i][0] != '{')
                    throw Error(path + "[" + i + "]", "a nonnull object is required.");
            return items;
        }

        private static float ReadTangent(string json, string field, float actual, string parentPath)
        {
            // Source exports encode step tangents as exact strings; Unity's own
            // JsonUtility checkpoints can emit the corresponding bare literals.
            // Only these two curve fields accept infinity. Numeric times, values,
            // weights and all other author floats still use finite bit checking.
            if (JsonObjectShape.TryGetField(json, field, out string raw))
            {
                if (raw == "\"Infinity\"" || raw == "Infinity") return float.PositiveInfinity;
                if (raw == "\"-Infinity\"" || raw == "-Infinity") return float.NegativeInfinity;
            }
            return ReadFloat(json, field, actual, parentPath);
        }

        private static float ReadFloat(string json, string field, float actual, string parentPath)
        {
            string path = parentPath + "." + field;
            if (!JsonObjectShape.TryGetField(json, field, out string raw) || !Number.IsMatch(raw))
                throw Error(path, "one explicit finite JSON number is required.");

            bool zeroLiteral = HasZeroSignificand(raw);
            float expected;
            if (zeroLiteral)
                expected = raw[0] == '-' ? NegativeZero : 0f;
            else if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out expected) ||
                     float.IsNaN(expected) || float.IsInfinity(expected))
                throw Error(path, "the JSON number must have a finite binary32 representation.");
            else if (expected == 0f)
                throw Error(path, "a nonzero JSON number underflowed; it is not a zero literal.");

            int expectedBits = CommonEncounterContentCompiler.FloatBits(expected);
            int actualBits = CommonEncounterContentCompiler.FloatBits(actual);
            if (expectedBits == actualBits) return actual;
            if (zeroLiteral && raw[0] == '-' && actualBits == 0)
                return NegativeZero;
            throw Error(path, "the parsed binary32 bits differ from the original author number.");
        }

        private static bool HasZeroSignificand(string raw)
        {
            // Number grammar was checked above. A signed exponent does not turn
            // a nonzero significand into a literal zero, even after underflow.
            foreach (char character in raw)
            {
                if (character == 'e' || character == 'E') break;
                if (character >= '1' && character <= '9') return false;
            }
            return true;
        }

        private static InvalidOperationException Error(string path, string detail)
            => new InvalidOperationException(path + ": " + detail);
    }
}
