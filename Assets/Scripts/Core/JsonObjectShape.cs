using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Finds explicitly serialized fields and array items before Unity can turn
    /// missing collections into empty ones. DTO deserializers still own JSON
    /// syntax and value validation; this scanner only preserves wire shape.
    /// </summary>
    public static class JsonObjectShape
    {
        public static bool TryGetField(string objectJson, string fieldName, out string rawValue)
        {
            rawValue = null;
            if (string.IsNullOrWhiteSpace(objectJson) || string.IsNullOrEmpty(fieldName)) return false;
            int start = SkipWhiteSpace(objectJson, 0);
            if (objectJson[start] != '{' || !TryValueEnd(objectJson, start, out int end) ||
                SkipWhiteSpace(objectJson, end) != objectJson.Length) return false;
            int depth = 0;
            bool found = false;
            for (int index = start; index < end; index++)
            {
                char current = objectJson[index];
                if (current == '{' || current == '[') { depth++; continue; }
                if (current == '}' || current == ']') { depth--; continue; }
                if (current != '"') continue;
                int stringStart = index + 1;
                if (!TryStringEnd(objectJson, index, out int stringEnd, out bool escaped)) return false;
                int next = SkipWhiteSpace(objectJson, stringEnd);
                if (depth == 1 && !escaped && next < end && objectJson[next] == ':' &&
                    stringEnd - stringStart - 1 == fieldName.Length &&
                    string.CompareOrdinal(objectJson, stringStart, fieldName, 0, fieldName.Length) == 0)
                {
                    if (found) { rawValue = null; return false; }
                    int valueStart = SkipWhiteSpace(objectJson, next + 1);
                    if (!TryValueEnd(objectJson, valueStart, out int valueEnd)) return false;
                    rawValue = objectJson.Substring(valueStart, valueEnd - valueStart);
                    found = true;
                }
                index = stringEnd - 1;
            }
            return found;
        }

        public static bool TryGetArrayItems(string arrayJson, out IReadOnlyList<string> rawItems)
        {
            rawItems = null;
            if (string.IsNullOrWhiteSpace(arrayJson)) return false;
            int index = SkipWhiteSpace(arrayJson, 0);
            if (arrayJson[index] != '[') return false;
            var items = new List<string>();
            index = SkipWhiteSpace(arrayJson, index + 1);
            if (index < arrayJson.Length && arrayJson[index] == ']')
            {
                if (SkipWhiteSpace(arrayJson, index + 1) != arrayJson.Length) return false;
                rawItems = items.AsReadOnly();
                return true;
            }
            while (index < arrayJson.Length)
            {
                if (!TryValueEnd(arrayJson, index, out int end)) return false;
                items.Add(arrayJson.Substring(index, end - index));
                index = SkipWhiteSpace(arrayJson, end);
                if (index >= arrayJson.Length) return false;
                if (arrayJson[index] == ']')
                {
                    if (SkipWhiteSpace(arrayJson, index + 1) != arrayJson.Length) return false;
                    rawItems = items.AsReadOnly();
                    return true;
                }
                if (arrayJson[index] != ',') return false;
                index = SkipWhiteSpace(arrayJson, index + 1);
            }
            return false;
        }

        private static bool TryValueEnd(string json, int start, out int end)
        {
            end = start;
            if (start >= json.Length) return false;
            if (json[start] == '"') return TryStringEnd(json, start, out end, out _);
            if (json[start] == '{' || json[start] == '[')
            {
                var containers = new Stack<char>();
                for (int index = start; index < json.Length; index++)
                {
                    char current = json[index];
                    if (current == '"')
                    {
                        if (!TryStringEnd(json, index, out int stringEnd, out _)) return false;
                        index = stringEnd - 1;
                    }
                    else if (current == '{' || current == '[') containers.Push(current);
                    else if (current == '}' || current == ']')
                    {
                        if (containers.Count == 0 || containers.Pop() != (current == '}' ? '{' : '[')) return false;
                        if (containers.Count == 0) { end = index + 1; return true; }
                    }
                }
                return false;
            }
            while (end < json.Length && !char.IsWhiteSpace(json[end]) &&
                json[end] != ',' && json[end] != '}' && json[end] != ']') end++;
            return end > start;
        }

        private static bool TryStringEnd(string json, int start, out int end, out bool escaped)
        {
            escaped = false;
            for (int index = start + 1; index < json.Length; index++)
            {
                if (json[index] == '\\') { escaped = true; index++; continue; }
                if (json[index] != '"') continue;
                end = index + 1;
                return true;
            }
            end = json.Length;
            return false;
        }

        private static int SkipWhiteSpace(string json, int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            return index;
        }
    }
}
