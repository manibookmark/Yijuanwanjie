using System;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns the nullable checkpoint wire contract. JsonUtility remains the value
    /// serializer (and hash format), but materializes null inline classes as empty
    /// DTOs. Replace only the known top-level trace/steps/after values, using a JSON
    /// structural scan rather than text replacement inside player-authored strings.
    /// </summary>
    internal static class AgentGameJson
    {
        internal static string Response(AgentGameResponse response)
        {
            string json = JsonUtility.ToJson(response);
            return response.trace == null ? json :
                ReplaceProperty(json, "trace", Trace(response.trace));
        }

        internal static string Trace(AgentGameTraceDocument trace)
        {
            var steps = new StringBuilder("[");
            for (int i = 0; i < trace.steps.Count; i++)
            {
                if (i > 0) steps.Append(',');
                steps.Append(TraceStep(trace.steps[i]));
            }
            steps.Append(']');
            return ReplaceProperty(JsonUtility.ToJson(trace), "steps", steps.ToString());
        }

        internal static string TraceStep(AgentGameTraceStep step)
        {
            string json = JsonUtility.ToJson(step);
            return step.after == null ? ReplaceProperty(json, "after", "null") : json;
        }

        private static string ReplaceProperty(string json, string property, string replacement)
        {
            int cursor = 1;
            while (cursor < json.Length)
            {
                SkipWhitespace(json, ref cursor);
                if (json[cursor] == '}') break;
                if (json[cursor] == ',') { cursor++; continue; }
                int keyStart = cursor;
                SkipString(json, ref cursor);
                bool matches = string.Equals(json.Substring(keyStart + 1, cursor - keyStart - 2),
                    property, StringComparison.Ordinal);
                SkipWhitespace(json, ref cursor);
                if (json[cursor++] != ':') throw new FormatException("Invalid serialized property.");
                SkipWhitespace(json, ref cursor);
                int valueStart = cursor;
                SkipValue(json, ref cursor);
                if (matches)
                    return json.Substring(0, valueStart) + replacement + json.Substring(cursor);
            }
            throw new FormatException("Serialized property not found: " + property);
        }

        private static void SkipValue(string json, ref int cursor)
        {
            if (json[cursor] == '"') { SkipString(json, ref cursor); return; }
            if (json[cursor] != '{' && json[cursor] != '[')
            {
                while (cursor < json.Length && json[cursor] != ',' && json[cursor] != '}') cursor++;
                return;
            }
            int depth = 0;
            do
            {
                char value = json[cursor];
                if (value == '"') { SkipString(json, ref cursor); continue; }
                if (value == '{' || value == '[') depth++;
                else if (value == '}' || value == ']') depth--;
                cursor++;
            } while (depth > 0 && cursor < json.Length);
            if (depth != 0) throw new FormatException("Incomplete serialized value.");
        }

        private static void SkipString(string json, ref int cursor)
        {
            if (json[cursor++] != '"') throw new FormatException("Expected serialized string.");
            while (cursor < json.Length)
            {
                char value = json[cursor++];
                if (value == '"') return;
                if (value == '\\') cursor++;
            }
            throw new FormatException("Incomplete serialized string.");
        }

        private static void SkipWhitespace(string json, ref int cursor)
        { while (cursor < json.Length && char.IsWhiteSpace(json[cursor])) cursor++; }
    }
}
