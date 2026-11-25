using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns one complete effective-floor checkpoint format. The closed mutable
    /// author DTO family is reconstructed by a strict managed reader; neither
    /// Unity's default synthesis nor an immutable Core graph defines this wire.
    /// Every field, optional null, collection order and binary32 bit is explicit.
    /// Restoration delegates topology and payload legality to the directory and
    /// never rebuilds saved payloads from today's author. A fingerprint detects
    /// drift; source provenance, Run progress and checkpoint authentication belong
    /// to the future Run owner. No ScriptableObject is cloned or owned here.
    /// Parser, writer and DTO closure remain together as one format boundary;
    /// reconsider this boundary before adding a second checkpoint family.
    /// </summary>
    internal sealed class LevelRunNodeDirectoryCheckpoint
    {
        internal const int CurrentFormatVersion = 1;
        private static readonly Regex Number = new Regex(
            @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$",
            RegexOptions.CultureInvariant);
        private static readonly Regex Integer = new Regex(@"^-?(?:0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly HashSet<Type> DtoTypes = new HashSet<Type>
        {
            typeof(Envelope), typeof(LevelNode), typeof(LevelCommonEncounterBinding),
            typeof(LevelRewardGrant), typeof(LevelInteractionEffect), typeof(CardSealSpec),
            typeof(LevelInteractionFixedEvolutionContract), typeof(LevelInteractionTargetLevelContract),
            typeof(LevelInteractionDoubleKeyContract), typeof(LevelInteractionArcanaChoiceContract),
            typeof(LevelInteractionChickenOfferingContract), typeof(LevelInteractionManaOfferingContract),
            typeof(LevelInteractionManaStatueContract), typeof(LevelInteractionAbandonedCartContract),
            typeof(LevelInteractionCrystalContract), typeof(LevelInteractionCardStatOfferingContract),
            typeof(LevelInteractionDuplicateOfferingContract), typeof(LevelInteractionGunForHireContract), typeof(LevelInteractionInvitationContract)
        };
        private readonly Envelope _snapshot;

        internal int FormatVersion => _snapshot.formatVersion;
        internal string AuthorContentId => _snapshot.authorContentId;
        internal string Fingerprint => _snapshot.fingerprint;
        internal string CanonicalJson { get; }

        // This is the only additional DTO. The floor payload remains the actual
        // mutable LevelNode author family, not another maintained node schema.
        private sealed class Envelope
        {
            public int formatVersion;
            public string authorContentId;
            public string fingerprint;
            public List<LevelNode> nodes;
            public List<string> disabledInteractionNodeIds;
        }

        private LevelRunNodeDirectoryCheckpoint(Envelope snapshot)
        {
            _snapshot = snapshot;
            var text = new StringBuilder();
            Write(text, snapshot, typeof(Envelope), 0);
            CanonicalJson = text.ToString();
        }

        /// <summary>Captures a frozen directory without retaining an exported
        /// mutable node. Live-author directories have no complete freeze proof.</summary>
        internal static LevelRunNodeDirectoryCheckpoint Capture(LevelRunNodeDirectory directory)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (!directory.HasFrozenInteractions) throw Invalid("Only a complete frozen directory can be captured.");
            var snapshot = new Envelope
            {
                formatVersion = CurrentFormatVersion,
                authorContentId = directory.AuthorContentId,
                fingerprint = directory.Fingerprint,
                nodes = new List<LevelNode>(directory.Nodes),
                disabledInteractionNodeIds = new List<string>(directory.DisabledInteractionNodeIds)
            };
            // One strict read establishes the same private DTO isolation as an
            // on-disk restore and verifies the writer's binary32 representation.
            var text = new StringBuilder(); Write(text, snapshot, typeof(Envelope), 0);
            return Parse(text.ToString());
        }

        /// <summary>Reads format and explicit payload shape only. The target
        /// author's topology and directory semantic validators run in Restore.</summary>
        internal static LevelRunNodeDirectoryCheckpoint Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw Invalid("A complete checkpoint JSON object is required.");
            var snapshot = (Envelope)new Reader(json).Read(typeof(Envelope));
            if (snapshot == null || snapshot.formatVersion != CurrentFormatVersion)
                throw Invalid("A supported explicit checkpoint version is required.");
            RequireId(snapshot.authorContentId);
            if (snapshot.fingerprint == null || !Regex.IsMatch(snapshot.fingerprint, "^[0-9a-f]{64}$"))
                throw Invalid("The complete directory fingerprint must be explicit lowercase SHA256.");
            if (snapshot.nodes == null || snapshot.disabledInteractionNodeIds == null)
                throw Invalid("Complete nodes and disabled interaction arrays must be nonnull.");
            var disabled = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in snapshot.disabledInteractionNodeIds)
            {
                RequireId(id);
                if (!disabled.Add(id)) throw Invalid("Disabled interaction identities cannot be duplicated.");
            }
            return new LevelRunNodeDirectoryCheckpoint(snapshot);
        }

        /// <summary>Creates a validated detached directory before any shared
        /// Run state can be changed. Full saved payloads, not author replacements,
        /// are passed to the existing directory's topology and fingerprint guard.</summary>
        internal LevelRunNodeDirectory Restore(LevelRunProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!string.Equals(profile.contentId, AuthorContentId, StringComparison.Ordinal))
                throw Invalid("The checkpoint belongs to another author floor.");
            return LevelRunNodeDirectory.RestoreFrozen(profile, _snapshot.nodes,
                _snapshot.disabledInteractionNodeIds, Fingerprint);
        }

        private static void RequireId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) throw Invalid("Checkpoint identity is missing or padded.");
            try { StrictUtf8.GetByteCount(value); }
            catch (EncoderFallbackException error) { throw new InvalidOperationException("Checkpoint identity has invalid Unicode.", error); }
        }

        private static FieldInfo[] Fields(Type type)
        {
            if (!DtoTypes.Contains(type)) throw Invalid("An undeclared type cannot enter the directory checkpoint.");
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            foreach (FieldInfo field in fields) RequireFieldType(field.FieldType);
            return fields;
        }

        private static void RequireFieldType(Type type)
        {
            if (type == typeof(string) || type == typeof(int) || type == typeof(bool) || type == typeof(float)) return;
            if (type.IsEnum && Enum.GetUnderlyingType(type) == typeof(int)) return;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            { RequireFieldType(type.GetGenericArguments()[0]); return; }
            if (!DtoTypes.Contains(type)) throw Invalid("The mutable author DTO closure contains an unsupported field type.");
        }

        private static void Write(StringBuilder text, object value, Type type, int depth)
        {
            if (depth > 64) throw Invalid("Checkpoint exceeds the structural depth limit.");
            if (value == null) { text.Append("null"); return; }
            if (value.GetType() != type) throw Invalid("Runtime DTO subclasses or alternate collection types cannot enter the checkpoint.");
            if (type == typeof(string)) { WriteString(text, (string)value); return; }
            if (type == typeof(bool)) { text.Append((bool)value ? "true" : "false"); return; }
            if (type == typeof(int)) { text.Append(((int)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (type.IsEnum)
            {
                if (!Enum.IsDefined(type, value)) throw Invalid("An unknown enum value cannot enter the checkpoint.");
                text.Append(Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture)); return;
            }
            if (type == typeof(float))
            {
                float number = (float)value;
                if (float.IsNaN(number) || float.IsInfinity(number)) throw Invalid("Checkpoint floats must be finite binary32.");
                text.Append(Bits(number) == int.MinValue ? "-0" : number.ToString("R", CultureInfo.InvariantCulture)); return;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)value; Type element = type.GetGenericArguments()[0]; text.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == null) throw Invalid("Checkpoint arrays cannot contain null members.");
                    if (i != 0) text.Append(','); Write(text, list[i], element, depth + 1);
                }
                text.Append(']'); return;
            }
            FieldInfo[] fields = Fields(type);
            if (value is LevelInteractionEffect effect && !effect.HasCrystalThrowingContract)
                fields = Array.FindAll(fields, field => field.Name != "crystalThrowing");
            if (value is LevelInteractionEffect statEffect && !statEffect.HasCardStatOfferingContract)
                fields = Array.FindAll(fields, field => field.Name != "cardStatOffering");
            if (value is LevelInteractionEffect duplicateEffect && !duplicateEffect.HasDuplicateOfferingContract)
                fields = Array.FindAll(fields, field => field.Name != "duplicateOffering");
            if (value is LevelInteractionEffect invitationEffect && !invitationEffect.HasCrawlerInvitationContract)
                fields = Array.FindAll(fields, field => field.Name != "crawlerInvitation");
            if (value is LevelInteractionEffect hireEffect && !hireEffect.HasGunForHireContract)
                fields = Array.FindAll(fields, field => field.Name != "gunForHire");
            text.Append('{');
            for (int i = 0; i < fields.Length; i++)
            {
                if (i != 0) text.Append(','); WriteString(text, fields[i].Name); text.Append(':');
                Write(text, fields[i].GetValue(value), fields[i].FieldType, depth + 1);
            }
            text.Append('}');
        }

        private static void WriteString(StringBuilder text, string value)
        {
            try { StrictUtf8.GetByteCount(value); }
            catch (EncoderFallbackException error) { throw new InvalidOperationException("Checkpoint string has invalid Unicode.", error); }
            text.Append('"');
            foreach (char c in value)
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 0x20) text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            text.Append('"');
        }

        private static float Float(string token)
        {
            if (!Number.IsMatch(token)) throw Invalid("An explicit finite JSON number is required.");
            bool zero = true;
            foreach (char c in token)
            { if (c == 'e' || c == 'E') break; if (c >= '1' && c <= '9') { zero = false; break; } }
            if (zero) return token[0] == '-' ? BitConverter.ToSingle(BitConverter.GetBytes(int.MinValue), 0) : 0f;
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value) || value == 0f)
                throw Invalid("Nonfinite floats and nonzero underflow are invalid.");
            return value;
        }

        private static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        private static InvalidOperationException Invalid(string message) => new InvalidOperationException("Directory checkpoint: " + message);

        // This reader consumes the declared type directly. It does not retain a
        // generic JSON tree or allow a missing field to become a DTO default.
        private sealed class Reader
        {
            private readonly string _json;
            private int _index;
            internal Reader(string json) { _json = json; }
            internal object Read(Type type)
            {
                object value = Value(type, 0); WhiteSpace();
                if (_index != _json.Length) throw Invalid("Trailing checkpoint data is invalid.");
                return value;
            }

            private object Value(Type type, int depth)
            {
                if (depth > 64) throw Invalid("Checkpoint exceeds the structural depth limit.");
                WhiteSpace(); if (_index == _json.Length) throw Invalid("Unexpected end of checkpoint.");
                bool nullable = !type.IsValueType;
                if (Peek('n'))
                {
                    if (!nullable || Token() != "null") throw Invalid("An explicit null is invalid for this field.");
                    return null;
                }
                if (type == typeof(string)) return String();
                if (type == typeof(bool))
                {
                    string token = Token();
                    if (token != "true" && token != "false") throw Invalid("A literal bool is required.");
                    return token == "true";
                }
                if (type == typeof(float)) return Float(Token());
                if (type == typeof(int) || type.IsEnum)
                {
                    string token = Token();
                    if (!Integer.IsMatch(token) || !int.TryParse(token, NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out int value)) throw Invalid("An exact ranged integer is required.");
                    if (!type.IsEnum) return value;
                    object result = Enum.ToObject(type, value);
                    if (!Enum.IsDefined(type, result)) throw Invalid("An unknown enum value is invalid.");
                    return result;
                }
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    if (!Take('[')) throw Invalid("An explicit array is required.");
                    var list = (IList)Activator.CreateInstance(type); Type element = type.GetGenericArguments()[0];
                    WhiteSpace(); if (Take(']')) return list;
                    while (true)
                    {
                        object item = Value(element, depth + 1);
                        if (item == null) throw Invalid("Checkpoint arrays cannot contain null members.");
                        list.Add(item); WhiteSpace(); if (Take(']')) return list;
                        if (!Take(',')) throw Invalid("An array separator is required.");
                    }
                }
                FieldInfo[] fields = Fields(type);
                if (!Take('{')) throw Invalid("An explicit declared object is required.");
                var declared = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                foreach (FieldInfo field in fields) declared.Add(field.Name, field);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                object dto = Activator.CreateInstance(type, nonPublic: true); WhiteSpace();
                if (!Take('}'))
                    while (true)
                    {
                        WhiteSpace(); string name = String(allowEscapes: false);
                        if (!declared.TryGetValue(name, out FieldInfo field) || !seen.Add(name))
                            throw Invalid("Unknown or duplicate checkpoint field.");
                        WhiteSpace(); if (!Take(':')) throw Invalid("An object field separator is required.");
                        field.SetValue(dto, Value(field.FieldType, depth + 1)); WhiteSpace();
                        if (Take('}')) break;
                        if (!Take(',')) throw Invalid("An object separator is required.");
                    }
                foreach (FieldInfo field in fields)
                    if (!seen.Contains(field.Name) && !(type == typeof(LevelInteractionEffect) && (field.Name == "crystalThrowing" || field.Name == "cardStatOffering" || field.Name == "duplicateOffering" || field.Name == "gunForHire" || field.Name == "crawlerInvitation")))
                        throw Invalid("Every declared checkpoint field must be explicitly present.");
                // Nullable typed extensions are omitted in preceding v1 wire; kind validators require specified contracts.
                // The canonical effect validator still requires it on Crystal kind.
                return dto;
            }

            private string Token()
            {
                int begin = _index;
                while (_index < _json.Length && !char.IsWhiteSpace(_json[_index]) &&
                    _json[_index] != ',' && _json[_index] != ']' && _json[_index] != '}') _index++;
                if (begin == _index) throw Invalid("A scalar JSON value is required.");
                return _json.Substring(begin, _index - begin);
            }

            private string String(bool allowEscapes = true)
            {
                if (!Take('"')) throw Invalid("An explicit JSON string is required.");
                var text = new StringBuilder();
                while (_index < _json.Length)
                {
                    char c = _json[_index++];
                    if (c == '"')
                    {
                        string result = text.ToString();
                        try { StrictUtf8.GetByteCount(result); }
                        catch (EncoderFallbackException error) { throw new InvalidOperationException("Checkpoint string has invalid Unicode.", error); }
                        return result;
                    }
                    if (c < 0x20) throw Invalid("Unescaped control characters are invalid.");
                    if (c != '\\') { text.Append(c); continue; }
                    if (!allowEscapes) throw Invalid("Checkpoint field names must use their unescaped canonical spelling.");
                    if (_index == _json.Length) break;
                    switch (_json[_index++])
                    {
                        case '"': text.Append('"'); break;
                        case '\\': text.Append('\\'); break;
                        case '/': text.Append('/'); break;
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _json.Length || !ushort.TryParse(_json.Substring(_index, 4),
                                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                                throw Invalid("Invalid Unicode escape.");
                            text.Append((char)code); _index += 4; break;
                        default: throw Invalid("Invalid JSON string escape.");
                    }
                }
                throw Invalid("Unterminated JSON string.");
            }

            private bool Peek(char c) => _index < _json.Length && _json[_index] == c;
            private bool Take(char c) { if (!Peek(c)) return false; _index++; return true; }
            private void WhiteSpace()
            {
                while (_index < _json.Length && (_json[_index] == ' ' || _json[_index] == '\r' ||
                    _json[_index] == '\n' || _json[_index] == '\t')) _index++;
            }
        }
    }
}
