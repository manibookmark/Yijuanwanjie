using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace WanjieScroll.Core
{
    /// <summary>Owns an exact, closed JSON grammar for explicitly registered detached DTOs.
    /// No runtime graph or arbitrary type name is accepted. All declared fields are
    /// mandatory; nullable reference/scalar values remain explicit for the owning
    /// contract to validate. Field order is declaration order, integers are exact,
    /// escaped keys/duplicates/unknown fields are rejected, and nesting is bounded.
    /// This protocol supplies syntax and values, never source or reward policy.</summary>
    public sealed class ExactJsonWire
    {
        private readonly HashSet<Type> _dtos;
        // The registered grammar is immutable. Reflection and declaration-order
        // sorting belong to grammar construction, never to each nested value.
        private readonly Dictionary<Type, FieldInfo[]> _fields;

        public ExactJsonWire(params Type[] dtoTypes)
        {
            if (dtoTypes == null || dtoTypes.Length == 0)
                throw new ArgumentException("Explicit DTO types are required.", nameof(dtoTypes));
            _dtos = new HashSet<Type>();
            _fields = new Dictionary<Type, FieldInfo[]>();
            foreach (var type in dtoTypes)
            {
                if (type == null || !type.IsClass || !type.IsSealed || type.BaseType != typeof(object) ||
                    type.ContainsGenericParameters || !_dtos.Add(type))
                    throw new ArgumentException("Only unique sealed detached DTO types are admitted.", nameof(dtoTypes));
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                Array.Sort(fields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
                _fields.Add(type, fields);
            }
            foreach (var type in _dtos)
                foreach (var field in Fields(type)) RequireType(field.FieldType);
        }

        private void RequireType(Type type)
        {
            var optional = Nullable.GetUnderlyingType(type);
            if (optional != null)
            {
                if (optional != typeof(int) && optional != typeof(long))
                    throw new ArgumentException("Only exact integer nullable scalars are supported.");
                return;
            }
            if (type == typeof(string) || type == typeof(int) || type == typeof(long) || type == typeof(bool)) return;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            { Fields(type.GetGenericArguments()[0]); return; }
            Fields(type);
        }

        private static readonly Regex Integer = new Regex(@"^(?:0|-?[1-9][0-9]*)$", RegexOptions.CultureInvariant);

        public string Write<T>(T data) where T : class
        {
            Fields(typeof(T));
            var output = new StringBuilder(); WriteValue(output, data, typeof(T), 0);
            return output.ToString();
        }

        public T Read<T>(string json) where T : class
        {
            Fields(typeof(T));
            var reader = new Reader(json);
            object wire = reader.Value(0); reader.Finish();
            return (T)ConvertValue(wire, typeof(T));
        }

        private FieldInfo[] Fields(Type type)
        {
            if (!_fields.TryGetValue(type, out FieldInfo[] fields))
                throw new ArgumentException("Type is outside the exact DTO wire.");
            return fields;
        }

        private void WriteValue(StringBuilder output, object value, Type type, int depth)
        {
            if (depth > 32) throw new ArgumentException("Exact DTO nesting exceeded the wire limit.");
            var optional = Nullable.GetUnderlyingType(type);
            if (optional != null)
            {
                if (value == null) { output.Append("null"); return; }
                WriteValue(output, value, optional, depth); return;
            }
            if (value == null)
            {
                if (type.IsValueType) throw new ArgumentException("A wire scalar cannot be null.");
                output.Append("null"); return;
            }
            if (value.GetType() != type) throw new ArgumentException("Unexpected exact DTO wire type.");
            if (type == typeof(string)) { String(output, (string)value); return; }
            if (type == typeof(int)) { output.Append(((int)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (type == typeof(long)) { output.Append(((long)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (type == typeof(bool)) { output.Append((bool)value ? "true" : "false"); return; }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type item = type.GetGenericArguments()[0]; Fields(item);
                output.Append('['); bool first = true;
                foreach (object element in (IEnumerable)value)
                { if (!first) output.Append(','); first = false; WriteValue(output, element, item, depth + 1); }
                output.Append(']'); return;
            }
            output.Append('{'); bool initial = true;
            foreach (var field in Fields(type))
            {
                if (!initial) output.Append(','); initial = false;
                String(output, field.Name); output.Append(':'); WriteValue(output, field.GetValue(value), field.FieldType, depth + 1);
            }
            output.Append('}');
        }

        private static void String(StringBuilder output, string value)
        {
            // Reject unpaired UTF-16 instead of hash/JSON replacement fallback.
            new UTF8Encoding(false, true).GetByteCount(value);
            output.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (c < 32) output.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else output.Append(c);
                        break;
                }
            }
            output.Append('"');
        }

        private object ConvertValue(object wire, Type type)
        {
            var optional = Nullable.GetUnderlyingType(type);
            if (optional != null) return wire == null ? null : ConvertValue(wire, optional);
            if (wire == null)
            {
                if (type.IsValueType) throw new ArgumentException("An explicit scalar cannot be null.");
                return null;
            }
            if (type == typeof(string)) return wire is string text ? text : throw new ArgumentException("A wire string is required.");
            if (type == typeof(bool)) return wire is bool flag ? flag : throw new ArgumentException("A wire boolean is required.");
            if (type == typeof(int))
            {
                if (!(wire is Numeric number) || !Integer.IsMatch(number.Text) ||
                    !int.TryParse(number.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
                    throw new ArgumentException("An exact signed32 wire integer is required.");
                return value;
            }
            if (type == typeof(long))
            {
                if (!(wire is Numeric number) || !Integer.IsMatch(number.Text) ||
                    !long.TryParse(number.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
                    throw new ArgumentException("An exact signed64 wire integer is required.");
                return value;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!(wire is List<object> array)) throw new ArgumentException("A wire array is required.");
                Type item = type.GetGenericArguments()[0]; Fields(item);
                var list = (IList)Activator.CreateInstance(type, true);
                foreach (object element in array) list.Add(ConvertValue(element, item));
                return list;
            }
            FieldInfo[] fields = Fields(type);
            if (!(wire is Dictionary<string, object> obj) || obj.Count != fields.Length)
                throw new ArgumentException("Every declared wire field is required, with no extras.");
            object result = Activator.CreateInstance(type, true);
            foreach (var field in fields)
            {
                if (!obj.TryGetValue(field.Name, out object fieldValue)) throw new ArgumentException("Missing wire field: " + field.Name);
                field.SetValue(result, ConvertValue(fieldValue, field.FieldType));
            }
            return result;
        }

        private sealed class Numeric
        {
            internal readonly string Text;
            internal Numeric(string text) { Text = text; }
        }

        private sealed class Reader
        {
            private readonly string _json;
            private int _at;
            internal Reader(string json) { _json = json ?? throw new ArgumentNullException(nameof(json)); }
            internal void Finish() { Space(); if (_at != _json.Length) throw Bad(); }
            private void Space() { while (_at < _json.Length && " \t\r\n".IndexOf(_json[_at]) >= 0) _at++; }
            private bool Take(char c) { Space(); if (_at < _json.Length && _json[_at] == c) { _at++; return true; } return false; }
            private void Require(char c) { if (!Take(c)) throw Bad(); }
            private static ArgumentException Bad() => new ArgumentException("Invalid exact DTO JSON.");

            internal object Value(int depth)
            {
                if (depth > 32) throw Bad(); Space(); if (_at >= _json.Length) throw Bad();
                char c = _json[_at];
                if (c == '"') return ReadString(false);
                if (c == '{')
                {
                    _at++; var obj = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (Take('}')) return obj;
                    do
                    {
                        string key = ReadString(true); Require(':');
                        if (obj.ContainsKey(key)) throw Bad(); obj.Add(key, Value(depth + 1));
                        if (Take('}')) return obj;
                        Require(',');
                    } while (true);
                }
                if (c == '[')
                {
                    _at++; var list = new List<object>(); if (Take(']')) return list;
                    do
                    {
                        list.Add(Value(depth + 1)); if (Take(']')) return list; Require(',');
                    } while (true);
                }
                int start = _at;
                while (_at < _json.Length && ",]} \t\r\n".IndexOf(_json[_at]) < 0) _at++;
                string token = _json.Substring(start, _at - start);
                if (token == "null") return null;
                if (token == "true") return true;
                if (token == "false") return false;
                if (!Integer.IsMatch(token)) throw Bad();
                return new Numeric(token);
            }

            private string ReadString(bool key)
            {
                Require('"'); var text = new StringBuilder();
                while (_at < _json.Length)
                {
                    char c = _json[_at++];
                    if (c == '"')
                    {
                        string value = text.ToString();
                        try { new UTF8Encoding(false, true).GetByteCount(value); }
                        catch (EncoderFallbackException) { throw Bad(); }
                        return value;
                    }
                    if (c < 32) throw Bad();
                    if (c != '\\') { text.Append(c); continue; }
                    if (key || _at >= _json.Length) throw Bad();
                    c = _json[_at++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': text.Append(c); break;
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case 'u':
                            if (_at + 4 > _json.Length || !ushort.TryParse(_json.Substring(_at, 4),
                                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort hex)) throw Bad();
                            text.Append((char)hex); _at += 4; break;
                        default: throw Bad();
                    }
                }
                throw Bad();
            }
        }
    }
}
