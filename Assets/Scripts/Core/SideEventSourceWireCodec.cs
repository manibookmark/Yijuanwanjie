using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns the closed author-source wire boundary before DTO defaults can conceal
    /// missing values. Every DTO field is explicit, canonical and type checked;
    /// the compiler owns semantic unions and references. Explicit wire nulls undo
    /// Unity's synthesized defaults; only literal negative zero repairs float bits.
    /// Missing fields never count as explicit null. The same closed DTO reader/writer
    /// owns checkpoint documents, preserving one schema without reflecting Core graphs.
    /// No account, RNG, execution or Run is cached. Reassess this single wire boundary
    /// Event sources and Gem metadata share this exact numeric/shape boundary; adding
    /// the Gem root does not alter any existing event DTO or canonical bytes. No arbitrary
    /// reflected type can enter either family. Reassess at the method/line threshold
    /// when another primitive, root family or persistent state is introduced.
    /// </summary>
    public static class SideEventSourceWireCodec
    {
        private static readonly Regex Number = new Regex(
            @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$",
            RegexOptions.CultureInvariant);
        private static readonly Regex Integer = new Regex(@"^-?(?:0|[1-9][0-9]*)$",
            RegexOptions.CultureInvariant);

        // This is called before JsonUtility: an absent List may become an empty
        // List in the parsed DTO, so parsed presence cannot establish wire presence.
        public static bool ValidateOptionalPresence(string volumeJson, string sourceLabel)
        {
            string path = (sourceLabel ?? "<content>") + ".sideEventSources";
            if (string.IsNullOrWhiteSpace(volumeJson)) throw Error(path, "a volume JSON object is required.");
            return new Reader(volumeJson, path).HasCanonicalRegistryField();
        }

        // Loader has already established that the optional collection is present.
        // TryGetField=false here therefore means invalid/duplicate, never absent.
        public static void ValidateWireAndRestoreBits(string volumeJson,
            IList<SideEventSourceContentData> sources, string sourceLabel)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            string path = (sourceLabel ?? "<content>") + ".sideEventSources";
            if (!ValidateOptionalPresence(volumeJson, sourceLabel))
                throw Error(path, "one canonical top-level registry field is required.");
            if (!JsonObjectShape.TryGetField(volumeJson, "sideEventSources", out string raw))
                throw Error(path, "one explicit canonical array is required; duplicate fields are invalid.");
            Wire value = new Reader(raw, path).Read();
            Validate(value, sources, typeof(List<SideEventSourceContentData>), path);
        }

        // The flat timeline joins the same closed author reader. Presence is read
        // from wire before Unity defaults; Candidate is never an absent extension.
        public static List<DungeonSideEventTimelineContentData> ReadTimelineSourcesFromVolume(string volumeJson, string sourceLabel)
        {
            string path = (sourceLabel ?? "<content>") + ".sideEventTimelines";
            if (string.IsNullOrWhiteSpace(volumeJson)) throw Error(path, "a volume JSON object is required.");
            if (!new Reader(volumeJson, path).HasCanonicalRegistryField("sideEventTimelines", true)) return null;
            if (!JsonObjectShape.TryGetField(volumeJson, "sideEventTimelines", out string raw))
                throw Error(path, "one explicit canonical timeline array is required.");
            Wire value = new Reader(raw, path).Read();
            if (value.Kind != 'a') throw Error(path, "a nonnull timeline array is required.");
            var timelines = (List<DungeonSideEventTimelineContentData>)ReadTyped(value, typeof(List<DungeonSideEventTimelineContentData>), path);
            foreach (var timeline in timelines) DungeonSideEventTimelineDefinition.Create(timeline);
            return timelines;
        }

        public static string WriteTimelineDocument(DungeonSideEventTimelineContentData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var text = new StringBuilder();
            WriteTyped(text, data, typeof(DungeonSideEventTimelineContentData), 0);
            return text.ToString();
        }

        public static bool ValidateGemOptionalPresence(string volumeJson, string sourceLabel)
        {
            string path = (sourceLabel ?? "<content>") + ".gemSources";
            if (string.IsNullOrWhiteSpace(volumeJson)) throw Error(path, "a volume JSON object is required.");
            return new Reader(volumeJson, path).HasCanonicalRegistryField("gemSources", true);
        }

        // Unity's recursive DTO serializer has a shallower type-graph limit than
        // the captured Odin tree. The source extension is nonserialized by Unity
        // and constructed only by this same closed checkpoint reader instead.
        public static List<GemSourceRegistryData> ReadGemSourcesFromVolume(string volumeJson, string sourceLabel)
        {
            string path = (sourceLabel ?? "<content>") + ".gemSources";
            if (!ValidateGemOptionalPresence(volumeJson, sourceLabel)) return null;
            if (!JsonObjectShape.TryGetField(volumeJson, "gemSources", out string raw))
                throw Error(path, "one explicit canonical registry array is required.");
            Wire value = new Reader(raw, path).Read();
            if (value.Kind != 'a') throw Error(path, "a nonnull registry array is required.");
            var sources = (List<GemSourceRegistryData>)ReadTyped(value, typeof(List<GemSourceRegistryData>), path);
            foreach (var source in sources)
                if (source == null) throw Error(path, "registry members cannot be null.");
            return sources;
        }

        public static void ValidateGemWireAndRestoreBits(string volumeJson,
            IList<GemSourceRegistryData> sources, string sourceLabel)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            string path = (sourceLabel ?? "<content>") + ".gemSources";
            if (!ValidateGemOptionalPresence(volumeJson, sourceLabel) ||
                !JsonObjectShape.TryGetField(volumeJson, "gemSources", out string raw))
                throw Error(path, "one explicit canonical registry array is required.");
            Wire value = new Reader(raw, path).Read();
            Validate(value, sources, typeof(List<GemSourceRegistryData>), path);
        }

        internal static GemSourceRegistryData ReadGemDocument(string json, out int version)
        {
            if (string.IsNullOrWhiteSpace(json)) throw Error("gemDocument", "a JSON object is required.");
            Wire envelope = new Reader(json, "gemDocument").Read();
            if (envelope.Kind != 'o' || envelope.Fields.Count != 2 ||
                !envelope.Fields.TryGetValue("formatVersion", out Wire rawVersion) ||
                !envelope.Fields.TryGetValue("registry", out Wire registry))
                throw Error("gemDocument", "exact formatVersion and registry fields are required.");
            version = (int)ReadTyped(rawVersion, typeof(int), "gemDocument.formatVersion");
            if (version != GemSourceRegistryDocument.CurrentFormatVersion || registry.Kind == 'n')
                throw Error("gemDocument", "a supported version and complete registry are required.");
            return (GemSourceRegistryData)ReadTyped(registry, typeof(GemSourceRegistryData), "gemDocument.registry");
        }

        internal static string WriteGemDocument(GemSourceRegistryData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var text = new StringBuilder();
            text.Append("{\"formatVersion\":").Append(GemSourceRegistryDocument.CurrentFormatVersion).Append(",\"registry\":");
            WriteTyped(text, data, typeof(GemSourceRegistryData), 0);
            return text.Append('}').ToString();
        }

        // Only these DTOs define the source wire. Neither arbitrary reflected
        // CLR objects nor the immutable Core graph may introduce another schema.
        private static readonly HashSet<Type> DeclaredDtoTypes = new HashSet<Type>
        {
            typeof(DungeonSideEventTimelineContentData), typeof(DungeonSideEventTimelinePhaseData),
            typeof(DungeonSideEventTimelineSlotData),
            typeof(GemSourceRegistryData), typeof(GemSourceEntryData), typeof(GemSourceFrequencyData),
            typeof(GemSourceReferenceData), typeof(GemSourceTagGroupData), typeof(GemSourceExternalFileData),
            typeof(GemSourceOdinNodeData), typeof(GemSourceOdinTypeData), typeof(GemSourceOdinValueData),
            typeof(SideEventSourceContentData),
            typeof(SideEventSourceProvenanceData),
            typeof(SideEventSourceObjectReferenceData),
            typeof(SideEventSourceAssetReferenceData),
            typeof(SideEventSourceVectorData),
            typeof(SideEventSourceCameraData),
            typeof(SideEventSourcePlacementData),
            typeof(SideEventSourceViewData),
            typeof(SideEventRootConfigurationData),
            typeof(SideEventSourceDetailsData),
            typeof(SideEventSourceRootData),
            typeof(SideEventSourceCachedNumberData),
            typeof(SideEventSourceMemberData),
            typeof(SideEventSourceGroupData),
            typeof(SideEventSourceOccurrenceData),
            typeof(SideEventSourceSlotData),
            typeof(SideEventSourceLayerData),
            typeof(SideEventSourceRequirementData),
            typeof(SideEventSourceRelicRequirementData),
            typeof(SideEventSourceMetricRequirementData),
            typeof(SideEventSourceDungeonRequirementData),
            typeof(SideEventSourceDungeonConditionData),
            typeof(SideEventOptionalIntData),
            typeof(SideEventOptionalBoolData),
            typeof(SideEventMechanicSpecificationData),
            typeof(SideEventOfferingSelectionData),
            typeof(SideEventManaRewardData),
            typeof(SideEventHealingRewardData),
            typeof(SideEventStatSettingData),
            typeof(SideEventGemFrequencyData),
            typeof(SideEventGemOfferData),
            typeof(SideEventEvoRecipeData),
            typeof(SideEventDuplicateCopyData),
            typeof(SideEventCrystalTimingData),
            typeof(SideEventCrystalRewardData),
            typeof(SideEventHirePriceData),
            typeof(SideEventAbandonedCartData),
            typeof(SideEventEvoStatueData),
            typeof(SideEventManaStatueData),
            typeof(SideEventArcanaChoiceData),
            typeof(SideEventCardStatOfferingData),
            typeof(SideEventChickenOfferingData),
            typeof(SideEventCrystalData),
            typeof(SideEventDuplicateOfferingData),
            typeof(SideEventGemChestData),
            typeof(SideEventSilverGemChestData),
            typeof(SideEventGunForHireData),
            typeof(SideEventManaOfferingData),
        };

        // The actual whitelist is the field closure of the single root DTO.
        // The declared family is a guard against arbitrary reflected CLR objects.
        private static readonly HashSet<Type> DtoTypes = BuildDtoClosure();

        private static HashSet<Type> BuildDtoClosure()
        {
            var closure = new HashSet<Type>();
            var pending = new Stack<Type>();
            pending.Push(typeof(SideEventSourceContentData));
            pending.Push(typeof(GemSourceRegistryData));
            pending.Push(typeof(DungeonSideEventTimelineContentData));
            while (pending.Count > 0)
            {
                Type type = pending.Pop();
                if (type == typeof(string) || type == typeof(bool) || type == typeof(int) ||
                    type == typeof(long) || type == typeof(float)) continue;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                { pending.Push(type.GetGenericArguments()[0]); continue; }
                if (!DeclaredDtoTypes.Contains(type) || !type.IsSealed || type.IsAbstract)
                    throw new InvalidOperationException("The source DTO field closure contains an undeclared type.");
                if (!closure.Add(type)) continue;
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                    pending.Push(field.FieldType);
            }
            return closure;
        }

        internal static SideEventSourceContentData ReadDocument(string json, out int version)
        {
            if (string.IsNullOrWhiteSpace(json)) throw Error("sourceDocument", "a JSON object is required.");
            Wire envelope = new Reader(json, "sourceDocument").Read();
            if (envelope.Kind != 'o' || envelope.Fields.Count != 2 ||
                !envelope.Fields.TryGetValue("formatVersion", out Wire rawVersion) ||
                !envelope.Fields.TryGetValue("source", out Wire source))
                throw Error("sourceDocument", "exact formatVersion and source fields are required.");
            version = (int)ReadTyped(rawVersion, typeof(int), "sourceDocument.formatVersion");
            if (version != SideEventSourceWireDocument.CurrentFormatVersion)
                throw Error("sourceDocument.formatVersion", "unsupported source wire version.");
            if (source.Kind == 'n') throw Error("sourceDocument.source", "a complete source is required.");
            return (SideEventSourceContentData)ReadTyped(source, typeof(SideEventSourceContentData), "sourceDocument.source");
        }

        internal static string WriteDocument(SideEventSourceContentData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var text = new StringBuilder();
            text.Append("{\"formatVersion\":").Append(SideEventSourceWireDocument.CurrentFormatVersion)
                .Append(",\"source\":");
            WriteTyped(text, data, typeof(SideEventSourceContentData), 0);
            return text.Append('}').ToString();
        }

        private static FieldInfo[] DtoFields(Type type, string path)
        {
            if (!DtoTypes.Contains(type)) throw Error(path, "an undeclared DTO type cannot enter the source wire.");
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(fields, (left, right) => string.CompareOrdinal(left.Name, right.Name));
            return fields;
        }

        private static object ReadTyped(Wire wire, Type type, string path)
        {
            if (type == typeof(string))
            {
                if (wire.Kind == 'n') return null;
                if (wire.Kind != 's') throw Error(path, "a string or explicit null is required.");
                return wire.Text;
            }
            if (type == typeof(bool))
            {
                if (wire.Kind != 'b') throw Error(path, "a literal bool is required.");
                return wire.Text == "true";
            }
            if (type == typeof(int) || type == typeof(long))
            {
                if (wire.Kind != 'v' || !Integer.IsMatch(wire.Text) ||
                    !long.TryParse(wire.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) ||
                    (type == typeof(int) && (value < int.MinValue || value > int.MaxValue)))
                    throw Error(path, "an exact ranged integer is required.");
                return type == typeof(int) ? (object)(int)value : value;
            }
            if (type == typeof(float)) return ExpectedFloat(wire, path);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (wire.Kind != 'a') throw Error(path, "a nonnull array is required.");
                var values = (IList)Activator.CreateInstance(type);
                Type element = type.GetGenericArguments()[0];
                for (int i = 0; i < wire.Items.Count; i++)
                {
                    if (wire.Items[i].Kind == 'n') throw Error(path, "null array members are invalid.");
                    values.Add(ReadTyped(wire.Items[i], element, path + "[" + i + "]"));
                }
                return values;
            }
            FieldInfo[] fields = DtoFields(type, path);
            if (wire.Kind == 'n') return null;
            // Schema 1 predates explicit prefix approval. Only that exact DTO
            // version may omit this field; schema 2 remains complete and closed.
            bool legacyTimeline = type == typeof(DungeonSideEventTimelineContentData) &&
                wire.Kind == 'o' && wire.Fields.TryGetValue("schemaVersion", out Wire schema) &&
                (int)ReadTyped(schema, typeof(int), path + ".schemaVersion") == DungeonSideEventTimelineDefinition.LegacySchemaVersion;
            bool omitLegacyScope = legacyTimeline && !wire.Fields.ContainsKey("approvedSourceLayerCount");
            if (wire.Kind != 'o' || wire.Fields.Count != fields.Length - (omitLegacyScope ? 1 : 0))
                throw Error(path, "one complete declared object is required.");
            object result = Activator.CreateInstance(type);
            foreach (FieldInfo field in fields)
            {
                if (omitLegacyScope && field.Name == "approvedSourceLayerCount") continue;
                if (!wire.Fields.TryGetValue(field.Name, out Wire child)) throw Error(path, "a declared field is missing.");
                field.SetValue(result, ReadTyped(child, field.FieldType, path + "." + field.Name));
            }
            return result;
        }

        private static void WriteTyped(StringBuilder text, object value, Type type, int depth)
        {
            if (depth > 64) throw Error("sourceDocument", "source DTO exceeds the structural depth limit.");
            if (value == null) { text.Append("null"); return; }
            if (type == typeof(string)) { WriteString(text, (string)value); return; }
            if (type == typeof(bool)) { text.Append((bool)value ? "true" : "false"); return; }
            if (type == typeof(int) || type == typeof(long))
            { text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            if (type == typeof(float))
            {
                float number = (float)value;
                if (float.IsNaN(number) || float.IsInfinity(number)) throw Error("sourceDocument", "nonfinite float is invalid.");
                text.Append(Bits(number) == int.MinValue ? "-0" : number.ToString("R", CultureInfo.InvariantCulture)); return;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                text.Append('['); Type element = type.GetGenericArguments()[0];
                var list = (IList)value;
                for (int i = 0; i < list.Count; i++)
                { if (i > 0) text.Append(','); WriteTyped(text, list[i], element, depth + 1); }
                text.Append(']'); return;
            }
            FieldInfo[] fields = DtoFields(type, "sourceDocument");
            text.Append('{');
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) text.Append(','); WriteString(text, fields[i].Name); text.Append(':');
                WriteTyped(text, fields[i].GetValue(value), fields[i].FieldType, depth + 1);
            }
            text.Append('}');
        }

        private static void WriteString(StringBuilder output, string value)
        {
            // A malformed UTF-16 string must not collapse through replacement encoding.
            new UTF8Encoding(false, true).GetByteCount(value);
            output.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (character < 0x20) output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else output.Append(character);
                        break;
                }
            }
            output.Append('"');
        }

        private static object Validate(Wire wire, object actual, Type type, string path)
        {
            if (type == typeof(string))
            {
                if (wire.Kind == 'n')
                {
                    return null; // Explicit author null reverses a synthesized empty string.
                }
                if (wire.Kind != 's' || !string.Equals((string)actual, wire.Text, StringComparison.Ordinal))
                    throw Error(path, "an explicit JSON string matching the parsed value is required.");
                return actual;
            }
            if (type == typeof(bool))
            {
                if (wire.Kind != 'b' || actual == null || (bool)actual != (wire.Text == "true"))
                    throw Error(path, "an explicit literal true or false is required.");
                return actual;
            }
            if (type == typeof(int) || type == typeof(long))
            {
                if (wire.Kind != 'v' || !Integer.IsMatch(wire.Text) ||
                    !long.TryParse(wire.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long expected) ||
                    (type == typeof(int) && (expected < int.MinValue || expected > int.MaxValue)) ||
                    actual == null || Convert.ToInt64(actual, CultureInfo.InvariantCulture) != expected)
                    throw Error(path, "one exact integer within the declared range is required.");
                return actual;
            }
            if (type == typeof(float)) return ReadFloat(wire, actual, path);
            if (typeof(IList).IsAssignableFrom(type))
            {
                if (wire.Kind != 'a' || !(actual is IList list) || wire.Items.Count != list.Count)
                    throw Error(path, "an explicit nonnull array with the original positional count is required.");
                Type element = type.GetGenericArguments()[0];
                for (int i = 0; i < list.Count; i++)
                {
                    if (wire.Items[i].Kind == 'n') throw Error(path + "[" + i + "]", "null array members are invalid.");
                    list[i] = Validate(wire.Items[i], list[i], element, path + "[" + i + "]");
                }
                return actual;
            }
            if (wire.Kind == 'n')
            {
                return null; // Explicit null reverses Unity's synthesized DTO defaults.
            }
            if (wire.Kind != 'o' || actual == null)
                throw Error(path, "an explicit object matching the parsed DTO is required.");
            FieldInfo[] fields = DtoFields(type, path);
            if (wire.Fields.Count != fields.Length)
                throw Error(path, "the complete declared field set is required; unknown fields are invalid.");
            foreach (FieldInfo field in fields)
            {
                if (!wire.Fields.TryGetValue(field.Name, out Wire child))
                    throw Error(path + "." + field.Name, "one explicit canonical field is required.");
                field.SetValue(actual, Validate(child, field.GetValue(actual), field.FieldType, path + "." + field.Name));
            }
            return actual;
        }

        private static float ReadFloat(Wire wire, object actual, string path)
        {
            float expected = ExpectedFloat(wire, path);
            if (!(actual is float parsed)) throw Error(path, "a parsed binary32 value is required.");
            int expectedBits = Bits(expected), actualBits = Bits(parsed);
            if (expectedBits == actualBits) return parsed;
            if (expectedBits == int.MinValue && actualBits == 0) return expected;
            throw Error(path, "the parsed binary32 bits differ from the author number.");
        }

        private static float ExpectedFloat(Wire wire, string path)
        {
            if (wire.Kind != 'v' || !Number.IsMatch(wire.Text))
                throw Error(path, "one explicit finite JSON number is required.");
            bool literalZero = true;
            foreach (char c in wire.Text)
            {
                if (c == 'e' || c == 'E') break;
                if (c >= '1' && c <= '9') { literalZero = false; break; }
            }
            if (literalZero) return wire.Text[0] == '-'
                ? BitConverter.ToSingle(BitConverter.GetBytes(int.MinValue), 0) : 0f;
            if (!float.TryParse(wire.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float expected) ||
                float.IsNaN(expected) || float.IsInfinity(expected) || expected == 0f)
                throw Error(path, "nonfinite numbers and nonzero underflow are invalid.");
            return expected;
        }

        private static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        private static InvalidOperationException Error(string path, string detail)
            => new InvalidOperationException(path + ": " + detail);

        private sealed class Wire
        {
            internal char Kind;
            internal string Text;
            internal Dictionary<string, Wire> Fields;
            internal List<Wire> Items;
        }

        // A registry-local syntax reader supplies type/duplicate checks that the
        // shared shape scanner intentionally does not claim to provide.
        private sealed class Reader
        {
            private readonly string _json;
            private readonly string _path;
            private int _index;
            internal Reader(string json, string path) { _json = json; _path = path; }
            internal bool HasCanonicalRegistryField(string fieldName = "sideEventSources", bool rejectCaseAliases = false)
            {
                WhiteSpace();
                if (_index >= _json.Length || _json[_index] != '{')
                    throw Error(_path, "a top-level JSON object is required.");
                int depth = 0, count = 0;
                while (_index < _json.Length)
                {
                    char current = _json[_index];
                    if (current == '{' || current == '[') { depth++; _index++; continue; }
                    if (current == '}' || current == ']') { depth--; _index++; continue; }
                    if (current != '"') { _index++; continue; }
                    string text = String(out bool escaped);
                    WhiteSpace();
                    if (depth == 1 && _index < _json.Length && _json[_index] == ':' &&
                        (text == fieldName || rejectCaseAliases && string.Equals(text, fieldName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (escaped || text != fieldName || ++count != 1)
                            throw Error(_path, "the top-level registry name must be unique and unescaped.");
                    }
                }
                return count == 1;
            }
            internal Wire Read()
            {
                Wire value = Value(0);
                WhiteSpace();
                if (_index != _json.Length) throw Error(_path, "trailing JSON data is invalid.");
                return value;
            }
            private Wire Value(int depth)
            {
                if (depth > 64) throw Error(_path, "registry JSON exceeds its structural depth limit.");
                WhiteSpace();
                if (_index >= _json.Length) throw Error(_path, "unexpected end of JSON.");
                char start = _json[_index];
                if (start == '{') return Object(depth + 1);
                if (start == '[') return Array(depth + 1);
                if (start == '"') return new Wire { Kind = 's', Text = String(out _) };
                int begin = _index;
                while (_index < _json.Length && !char.IsWhiteSpace(_json[_index]) &&
                    _json[_index] != ',' && _json[_index] != ']' && _json[_index] != '}') _index++;
                string token = _json.Substring(begin, _index - begin);
                if (token == "null") return new Wire { Kind = 'n' };
                if (token == "true" || token == "false") return new Wire { Kind = 'b', Text = token };
                if (!Number.IsMatch(token)) throw Error(_path, "invalid JSON literal.");
                return new Wire { Kind = 'v', Text = token };
            }
            private Wire Object(int depth)
            {
                _index++;
                var fields = new Dictionary<string, Wire>(StringComparer.Ordinal);
                WhiteSpace();
                if (Take('}')) return new Wire { Kind = 'o', Fields = fields };
                while (true)
                {
                    WhiteSpace();
                    if (_index >= _json.Length || _json[_index] != '"') throw Error(_path, "an object field name is required.");
                    string name = String(out bool escaped);
                    if (escaped) throw Error(_path, "field names must use their unescaped canonical spelling.");
                    WhiteSpace();
                    if (!Take(':')) throw Error(_path, "an object field separator is required.");
                    if (fields.ContainsKey(name)) throw Error(_path + "." + name, "duplicate fields are invalid.");
                    fields.Add(name, Value(depth));
                    WhiteSpace();
                    if (Take('}')) break;
                    if (!Take(',')) throw Error(_path, "an object separator is required.");
                }
                return new Wire { Kind = 'o', Fields = fields };
            }
            private Wire Array(int depth)
            {
                _index++;
                var items = new List<Wire>();
                WhiteSpace();
                if (Take(']')) return new Wire { Kind = 'a', Items = items };
                while (true)
                {
                    items.Add(Value(depth));
                    WhiteSpace();
                    if (Take(']')) break;
                    if (!Take(',')) throw Error(_path, "an array separator is required.");
                }
                return new Wire { Kind = 'a', Items = items };
            }
            private string String(out bool escaped)
            {
                escaped = false;
                _index++;
                var text = new StringBuilder();
                while (_index < _json.Length)
                {
                    char value = _json[_index++];
                    if (value == '"') return text.ToString();
                    if (value < 0x20) throw Error(_path, "unescaped control characters are invalid.");
                    if (value != '\\') { text.Append(value); continue; }
                    escaped = true;
                    if (_index >= _json.Length) break;
                    char code = _json[_index++];
                    switch (code)
                    {
                        case '"': case '\\': case '/': text.Append(code); break;
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _json.Length || !ushort.TryParse(_json.Substring(_index, 4),
                                    NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort character))
                                throw Error(_path, "invalid unicode escape.");
                            text.Append((char)character); _index += 4; break;
                        default: throw Error(_path, "invalid string escape.");
                    }
                }
                throw Error(_path, "unterminated JSON string.");
            }
            private bool Take(char value)
            {
                if (_index < _json.Length && _json[_index] == value) { _index++; return true; }
                return false;
            }
            private void WhiteSpace()
            {
                while (_index < _json.Length && (_json[_index] == ' ' || _json[_index] == '\r' ||
                    _json[_index] == '\n' || _json[_index] == '\t')) _index++;
            }
        }
    }
}
