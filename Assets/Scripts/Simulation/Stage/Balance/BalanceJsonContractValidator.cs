using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Strict schema-v1/v2 guard in front of JsonUtility. JsonUtility intentionally
    /// ignores unknown fields and cannot distinguish an omitted primitive from
    /// its default value; experiment inputs must fail instead of changing
    /// meaning silently.
    /// </summary>
    internal static class BalanceJsonContractValidator
    {
        public static void Validate(string json)
        {
            JsonValue root = new Parser(json).Parse();
            RequireObject(root, "$");
            ValidateKeys(
                root,
                "$",
                new[]
                {
                    "schemaVersion", "suiteId", "revision", "floorContentId",
                    "floorResourcePath", "snapshotId", "coverageMode",
                    "entryState", "entryRule", "agent", "rewardPolicy",
                    "isolatedEncounters", "routes", "coverageExclusions",
                    "checks", "routeExecution"
                },
                new[]
                {
                    "schemaVersion", "suiteId", "revision", "floorContentId",
                    "floorResourcePath", "snapshotId", "coverageMode",
                    "entryState", "entryRule", "agent", "rewardPolicy",
                    "isolatedEncounters", "routes", "coverageExclusions",
                    "checks"
                });
            RequireNumber(root["schemaVersion"], "$.schemaVersion");
            RequireString(root["suiteId"], "$.suiteId");
            RequireNumber(root["revision"], "$.revision");
            RequireString(root["floorContentId"], "$.floorContentId");
            RequireString(root["floorResourcePath"], "$.floorResourcePath");
            RequireString(root["snapshotId"], "$.snapshotId");
            RequireString(root["coverageMode"], "$.coverageMode");
            if (root.Has("routeExecution"))
                RequireString(root["routeExecution"], "$.routeExecution");
            ValidateEntryState(root["entryState"], "$.entryState");
            ValidateEntry(root["entryRule"], "$.entryRule");
            ValidateVersioned(root["agent"], "$.agent");
            ValidateVersioned(root["rewardPolicy"], "$.rewardPolicy");
            ValidateArray(
                root["isolatedEncounters"],
                "$.isolatedEncounters",
                ValidateEncounter);
            ValidateArray(root["routes"], "$.routes", ValidateRoute);
            ValidateArray(
                root["coverageExclusions"],
                "$.coverageExclusions",
                ValidateExclusion);
            ValidateArray(root["checks"], "$.checks", ValidateCheck);
        }

        private static void ValidateEntryState(JsonValue value, string path)
        {
            RequireObject(value, path);
            ValidateKeys(
                value,
                path,
                new[] { "kind", "revision", "checkpointFile" },
                new[] { "kind", "revision" });
            RequireString(value["kind"], path + ".kind");
            RequireNumber(value["revision"], path + ".revision");
            if (value.Has("checkpointFile"))
                RequireString(value["checkpointFile"], path + ".checkpointFile");
            if (string.Equals(
                    value["kind"].StringValue,
                    BalanceEntryStateKind.Checkpoint.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                RequirePresent(value, "checkpointFile", path);
            }
        }

        private static void ValidateEntry(JsonValue value, string path)
        {
            RequireObject(value, path);
            ValidateKeys(
                value,
                path,
                new[] { "id", "revision", "selectedHeroContentId", "rules" },
                new[] { "id", "revision", "selectedHeroContentId", "rules" });
            RequireString(value["id"], path + ".id");
            RequireNumber(value["revision"], path + ".revision");
            RequireString(
                value["selectedHeroContentId"],
                path + ".selectedHeroContentId");

            JsonValue rules = value["rules"];
            string rulesPath = path + ".rules";
            RequireObject(rules, rulesPath);
            string[] ruleKeys =
            {
                "knockbackBaseChance", "disarmBaseChance",
                "preservePlayerArmorBetweenTurns", "shieldBashEnabled",
                "comboEnabled", "wuxingEnabled", "bossOverkillEnabled",
                "normalOverkillEnabled", "ultraOverkillEnabled"
            };
            ValidateKeys(rules, rulesPath, ruleKeys, ruleKeys);
            RequireNumber(
                rules["knockbackBaseChance"],
                rulesPath + ".knockbackBaseChance");
            RequireNumber(
                rules["disarmBaseChance"],
                rulesPath + ".disarmBaseChance");
            for (int i = 2; i < ruleKeys.Length; i++)
                RequireBoolean(rules[ruleKeys[i]], rulesPath + "." + ruleKeys[i]);
        }

        private static void ValidateVersioned(JsonValue value, string path)
        {
            RequireObject(value, path);
            ValidateKeys(
                value,
                path,
                new[] { "id", "revision" },
                new[] { "id", "revision" });
            RequireString(value["id"], path + ".id");
            RequireNumber(value["revision"], path + ".revision");
        }

        private static void ValidateEncounter(JsonValue value, string path)
        {
            RequireObject(value, path);
            ValidateKeys(
                value,
                path,
                new[] { "id", "nodeContentId", "quick" },
                new[] { "id", "nodeContentId", "quick" });
            RequireString(value["id"], path + ".id");
            RequireString(value["nodeContentId"], path + ".nodeContentId");
            RequireBoolean(value["quick"], path + ".quick");
        }

        private static void ValidateRoute(JsonValue value, string path)
        {
            RequireObject(value, path);
            ValidateKeys(
                value,
                path,
                new[]
                {
                    "id", "revision", "purpose", "quick", "nodeContentIds",
                    "steps"
                },
                new[]
                {
                    "id", "revision", "purpose", "quick"
                });
            RequireString(value["id"], path + ".id");
            RequireNumber(value["revision"], path + ".revision");
            RequireString(value["purpose"], path + ".purpose");
            RequireBoolean(value["quick"], path + ".quick");
            if (!value.Has("nodeContentIds") && !value.Has("steps"))
            {
                throw ContractError(
                    path + " must declare nodeContentIds or steps.");
            }
            if (value.Has("nodeContentIds"))
            {
                ValidateStringArray(
                    value["nodeContentIds"],
                    path + ".nodeContentIds");
            }
            if (value.Has("steps"))
            {
                ValidateArray(
                    value["steps"],
                    path + ".steps",
                    ValidateRouteStep);
            }
        }

        private static void ValidateRouteStep(JsonValue value, string path)
        {
            RequireObject(value, path);
            ValidateKeys(
                value,
                path,
                new[]
                {
                    "kind", "nodeContentId", "targetCardContentId",
                    "targetCardOccurrence", "victorySealDisposition", "victorySealContentId",
                    "cardConversionDisposition"
                },
                new[] { "kind", "nodeContentId" });
            RequireString(value["kind"], path + ".kind");
            RequireString(value["nodeContentId"], path + ".nodeContentId");
            if (value.Has("targetCardContentId"))
            {
                RequireString(
                    value["targetCardContentId"],
                    path + ".targetCardContentId");
            }
            if (value.Has("targetCardOccurrence"))
            {
                RequireNumber(
                    value["targetCardOccurrence"],
                    path + ".targetCardOccurrence");
            }
            if (value.Has("victorySealDisposition"))
            {
                RequireString(
                    value["victorySealDisposition"],
                    path + ".victorySealDisposition");
            }
            if (value.Has("victorySealContentId"))
            {
                RequireString(value["victorySealContentId"], path + ".victorySealContentId");
            }
            if (value.Has("cardConversionDisposition"))
            {
                RequireString(
                    value["cardConversionDisposition"],
                    path + ".cardConversionDisposition");
            }
        }

        private static void ValidateExclusion(JsonValue value, string path)
        {
            RequireObject(value, path);
            ValidateKeys(
                value,
                path,
                new[] { "nodeContentId", "reason" },
                new[] { "nodeContentId", "reason" });
            RequireString(value["nodeContentId"], path + ".nodeContentId");
            RequireString(value["reason"], path + ".reason");
        }

        private static void ValidateCheck(JsonValue value, string path)
        {
            RequireObject(value, path);
            ValidateKeys(
                value,
                path,
                new[] { "id", "mode", "severity", "description", "conditions" },
                new[] { "id", "mode", "severity", "description", "conditions" });
            RequireString(value["id"], path + ".id");
            RequireString(value["mode"], path + ".mode");
            RequireString(value["severity"], path + ".severity");
            RequireString(value["description"], path + ".description");
            ValidateArray(value["conditions"], path + ".conditions",
                ValidateCondition);
        }

        private static void ValidateCondition(JsonValue value, string path)
        {
            RequireObject(value, path);
            string[] allowed =
            {
                "kind", "scope", "metric", "comparison", "value",
                "upperValue", "targetIds", "baselineId", "candidateId"
            };
            string[] required =
            {
                "kind", "scope", "metric", "comparison", "value"
            };
            ValidateKeys(value, path, allowed, required);
            RequireString(value["kind"], path + ".kind");
            RequireString(value["scope"], path + ".scope");
            RequireString(value["metric"], path + ".metric");
            RequireString(value["comparison"], path + ".comparison");
            RequireNumber(value["value"], path + ".value");

            string comparison = value["comparison"].StringValue;
            if (string.Equals(
                    comparison,
                    BalanceComparisonOperator.Between.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                RequirePresent(value, "upperValue", path);
            }
            else if (value.Has("upperValue"))
            {
                throw ContractError(
                    path + ".upperValue is only valid when comparison is " +
                    BalanceComparisonOperator.Between + ".");
            }
            if (value.Has("upperValue"))
                RequireNumber(value["upperValue"], path + ".upperValue");

            string kind = value["kind"].StringValue;
            if (string.Equals(
                    kind,
                    BalanceCheckKind.Delta.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                RequirePresent(value, "baselineId", path);
                RequirePresent(value, "candidateId", path);
                RequireString(value["baselineId"], path + ".baselineId");
                RequireString(value["candidateId"], path + ".candidateId");
            }
            else
            {
                RequirePresent(value, "targetIds", path);
                ValidateStringArray(value["targetIds"], path + ".targetIds");
            }
        }

        private static void ValidateArray(
            JsonValue value,
            string path,
            Action<JsonValue, string> validateItem)
        {
            RequireKind(value, JsonKind.Array, path);
            for (int i = 0; i < value.Items.Count; i++)
                validateItem(value.Items[i], path + "[" + i + "]");
        }

        private static void ValidateStringArray(JsonValue value, string path)
        {
            RequireKind(value, JsonKind.Array, path);
            for (int i = 0; i < value.Items.Count; i++)
                RequireString(value.Items[i], path + "[" + i + "]");
        }

        private static void ValidateKeys(
            JsonValue value,
            string path,
            IReadOnlyList<string> allowed,
            IReadOnlyList<string> required)
        {
            var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (string key in value.Properties.Keys)
            {
                if (!allowedSet.Contains(key))
                {
                    throw ContractError(
                        path + " contains unknown field '" + key + "'.");
                }
            }
            for (int i = 0; i < required.Count; i++)
                RequirePresent(value, required[i], path);
        }

        private static void RequirePresent(
            JsonValue value,
            string key,
            string path)
        {
            if (!value.Has(key))
                throw ContractError(path + " is missing required field '" + key + "'.");
        }

        private static void RequireObject(JsonValue value, string path)
        {
            RequireKind(value, JsonKind.Object, path);
        }

        private static void RequireString(JsonValue value, string path)
        {
            RequireKind(value, JsonKind.String, path);
        }

        private static void RequireNumber(JsonValue value, string path)
        {
            RequireKind(value, JsonKind.Number, path);
        }

        private static void RequireBoolean(JsonValue value, string path)
        {
            RequireKind(value, JsonKind.Boolean, path);
        }

        private static void RequireKind(
            JsonValue value,
            JsonKind expected,
            string path)
        {
            if (value == null || value.Kind != expected)
            {
                throw ContractError(
                    path + " must be " +
                    expected.ToString().ToLowerInvariant() + ".");
            }
        }

        private static BalanceSuiteException ContractError(string message)
        {
            return new BalanceSuiteException(
                "Suite JSON contract is invalid: " + message);
        }

        private enum JsonKind
        {
            Object,
            Array,
            String,
            Number,
            Boolean,
            Null
        }

        private sealed class JsonValue
        {
            public JsonKind Kind;
            public string StringValue;
            public Dictionary<string, JsonValue> Properties;
            public List<JsonValue> Items;

            public JsonValue this[string key] => Properties[key];
            public bool Has(string key) => Properties.ContainsKey(key);
        }

        private sealed class Parser
        {
            private readonly string _json;
            private int _index;

            public Parser(string json)
            {
                _json = json ?? string.Empty;
            }

            public JsonValue Parse()
            {
                SkipWhitespace();
                JsonValue value = ParseValue();
                SkipWhitespace();
                if (_index != _json.Length)
                    throw Error("Unexpected trailing input.");
                return value;
            }

            private JsonValue ParseValue()
            {
                SkipWhitespace();
                if (_index >= _json.Length) throw Error("Unexpected end of JSON.");
                char current = _json[_index];
                if (current == '{') return ParseObject();
                if (current == '[') return ParseArray();
                if (current == '"')
                {
                    return new JsonValue
                    {
                        Kind = JsonKind.String,
                        StringValue = ParseString()
                    };
                }
                if (current == 't')
                {
                    Expect("true");
                    return new JsonValue { Kind = JsonKind.Boolean };
                }
                if (current == 'f')
                {
                    Expect("false");
                    return new JsonValue { Kind = JsonKind.Boolean };
                }
                if (current == 'n')
                {
                    Expect("null");
                    return new JsonValue { Kind = JsonKind.Null };
                }
                return ParseNumber();
            }

            private JsonValue ParseObject()
            {
                _index++;
                var properties = new Dictionary<string, JsonValue>(
                    StringComparer.Ordinal);
                SkipWhitespace();
                if (Consume('}'))
                {
                    return new JsonValue
                    {
                        Kind = JsonKind.Object,
                        Properties = properties
                    };
                }
                while (true)
                {
                    SkipWhitespace();
                    if (_index >= _json.Length || _json[_index] != '"')
                        throw Error("Object property name must be a string.");
                    string key = ParseString();
                    SkipWhitespace();
                    if (!Consume(':')) throw Error("Expected ':' after property.");
                    JsonValue value = ParseValue();
                    if (!properties.TryAdd(key, value))
                        throw Error("Duplicate property '" + key + "'.");
                    SkipWhitespace();
                    if (Consume('}')) break;
                    if (!Consume(',')) throw Error("Expected ',' or '}'.");
                }
                return new JsonValue
                {
                    Kind = JsonKind.Object,
                    Properties = properties
                };
            }

            private JsonValue ParseArray()
            {
                _index++;
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (Consume(']'))
                {
                    return new JsonValue
                    {
                        Kind = JsonKind.Array,
                        Items = items
                    };
                }
                while (true)
                {
                    items.Add(ParseValue());
                    SkipWhitespace();
                    if (Consume(']')) break;
                    if (!Consume(',')) throw Error("Expected ',' or ']'.");
                }
                return new JsonValue { Kind = JsonKind.Array, Items = items };
            }

            private JsonValue ParseNumber()
            {
                int start = _index;
                if (Consume('-')) { }
                while (_index < _json.Length &&
                       char.IsDigit(_json[_index]))
                {
                    _index++;
                }
                if (Consume('.'))
                {
                    while (_index < _json.Length &&
                           char.IsDigit(_json[_index]))
                    {
                        _index++;
                    }
                }
                if (_index < _json.Length &&
                    (_json[_index] == 'e' || _json[_index] == 'E'))
                {
                    _index++;
                    if (_index < _json.Length &&
                        (_json[_index] == '+' || _json[_index] == '-'))
                    {
                        _index++;
                    }
                    while (_index < _json.Length &&
                           char.IsDigit(_json[_index]))
                    {
                        _index++;
                    }
                }
                string token = _json.Substring(start, _index - start);
                if (token.Length == 0 ||
                    !double.TryParse(
                        token,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double parsed) ||
                    double.IsNaN(parsed) ||
                    double.IsInfinity(parsed))
                {
                    throw Error("Invalid JSON number.");
                }
                return new JsonValue { Kind = JsonKind.Number };
            }

            private string ParseString()
            {
                if (!Consume('"')) throw Error("Expected string.");
                var value = new StringBuilder();
                while (_index < _json.Length)
                {
                    char current = _json[_index++];
                    if (current == '"') return value.ToString();
                    if (current < 0x20)
                        throw Error("Unescaped control character in string.");
                    if (current != '\\')
                    {
                        value.Append(current);
                        continue;
                    }
                    if (_index >= _json.Length)
                        throw Error("Incomplete string escape.");
                    char escape = _json[_index++];
                    switch (escape)
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u': value.Append(ParseUnicode()); break;
                        default: throw Error("Unknown string escape.");
                    }
                }
                throw Error("Unterminated string.");
            }

            private char ParseUnicode()
            {
                if (_index + 4 > _json.Length)
                    throw Error("Incomplete Unicode escape.");
                string token = _json.Substring(_index, 4);
                _index += 4;
                if (!ushort.TryParse(
                        token,
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out ushort value))
                {
                    throw Error("Invalid Unicode escape.");
                }
                return (char)value;
            }

            private void Expect(string token)
            {
                if (_index + token.Length > _json.Length ||
                    !string.Equals(
                        _json.Substring(_index, token.Length),
                        token,
                        StringComparison.Ordinal))
                {
                    throw Error("Expected '" + token + "'.");
                }
                _index += token.Length;
            }

            private bool Consume(char expected)
            {
                if (_index >= _json.Length || _json[_index] != expected)
                    return false;
                _index++;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_index < _json.Length &&
                       char.IsWhiteSpace(_json[_index]))
                {
                    _index++;
                }
            }

            private BalanceSuiteException Error(string message)
            {
                return ContractError(message + " Offset=" + _index + ".");
            }
        }
    }
}
