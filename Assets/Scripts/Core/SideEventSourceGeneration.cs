using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace WanjieScroll.Core
{
    public enum SideEventSourceGenerationStatus { Ready, AttemptBudgetExhausted, PlacementFailed }
    public enum SideEventSourceAttemptOutcome { EmptyPool, Placed, PlacementFailed }

    [Serializable]
    public sealed class SideEventSourceAttemptData
    {
        public string stateFingerprint;
        public int qualifiedProofVersion;
        public string qualifiedFingerprint;
        public int selectionAlgorithmVersion;
        public string remainingPoolFingerprint;
        public int slotDraw;
        public bool hasMemberDraw;
        public int memberDraw;
        public int memberIndex;
        public int outcome;
    }

    [Serializable]
    public sealed class SideEventSourceGenerationData
    {
        public int version;
        public SideEventQualificationCheckpointData qualification;
        public List<SideEventSourceAttemptData> steps;
        public int attempts;
        public int status;
        public string fingerprint;
    }

    /// <summary>A frozen selection, including the full source root/details and the
    /// original member occurrence. SourceOnly/Unknown mapping remains unchanged;
    /// passing it to a placement policy never grants a project reward.</summary>
    public sealed class SideEventSourceAttempt
    {
        public string StateFingerprint { get; }
        public SideEventQualifiedSelection Selection { get; }
        internal SideEventSourceAttempt(string stateFingerprint, SideEventQualifiedSelection selection)
        { StateFingerprint = stateFingerprint; Selection = selection; }
    }

    public sealed class SideEventSourceAttemptStep
    {
        public SideEventSourceAttempt Attempt { get; }
        public SideEventSourceAttemptOutcome Outcome { get; }
        internal SideEventSourceAttemptStep(SideEventSourceAttempt attempt, SideEventSourceAttemptOutcome outcome)
        { Attempt = attempt; Outcome = outcome; }
    }

    /// <summary>
    /// Immutable source attempt allocator for ONE original layer. Success removes
    /// the selected slot occurrence and increments attempts; placement false stops
    /// without increment; an exhausted pool still advances one slot RNG and counts
    /// an attempt. The mathematical selector remains the only weight/guarantee owner.
    /// A Dungeon owner must commit the returned state and actual placement result
    /// together; this module owns no mutable Run state, locations or account gates.
    /// Exact occurrence removal safely adapts native List.Remove's ignored result.
    /// Project RNG is injected, with no native same-seed or cursor-equivalence claim.
    /// Restore validates evidence, not its external authenticity or physical placement.
    /// F5's two original source layers require two independent instances/checkpoints.
    /// </summary>
    public sealed class SideEventSourceGeneration
    {
        public const int CurrentVersion = 1;
        public SideEventQualificationCheckpoint Qualification { get; }
        public IReadOnlyList<SideEventQualifiedSourceSlot> RemainingSlots { get; }
        public IReadOnlyList<SideEventSourceAttemptStep> Steps { get; }
        public int Attempts { get; }
        public SideEventSourceGenerationStatus Status { get; }
        public string Fingerprint { get; }
        private readonly QualifiedSideEventChoices _choices;

        private SideEventSourceGeneration(SideEventQualificationCheckpoint qualification,
            IReadOnlyList<SideEventQualifiedSourceSlot> remaining, IReadOnlyList<SideEventSourceAttemptStep> steps,
            int attempts, SideEventSourceGenerationStatus status)
        {
            Qualification = qualification;
            RemainingSlots = new ReadOnlyCollection<SideEventQualifiedSourceSlot>(new List<SideEventQualifiedSourceSlot>(remaining));
            Steps = new ReadOnlyCollection<SideEventSourceAttemptStep>(new List<SideEventSourceAttemptStep>(steps));
            Attempts = attempts; Status = status;
            var choices = new List<SideEventChoiceSlot>();
            foreach (var slot in RemainingSlots)
            {
                var members = new List<SideEventChoiceMember>();
                foreach (var occurrence in slot.Members)
                    members.Add(new SideEventChoiceMember(occurrence.Root.ContentId,
                        occurrence.SourceMember.Weight, occurrence.SourceMember.Guaranteed));
                choices.Add(new SideEventChoiceSlot(slot.Source.ContentId,
                    slot.Source.GroupContentId ?? slot.Source.ContentId, members));
            }
            _choices = new QualifiedSideEventChoices(choices);
            Fingerprint = SideEventQualificationCheckpoint.Identity(writer =>
            {
                writer.Write("side-event-source-generation/v1"); writer.Write(CurrentVersion);
                writer.Write(Qualification.Fingerprint); writer.Write(_choices.Fingerprint);
                writer.Write(Attempts); writer.Write((int)Status); writer.Write(Steps.Count);
                foreach (var step in Steps)
                {
                    writer.Write(step.Attempt.StateFingerprint);
                    var proof = step.Attempt.Selection.Proof;
                    writer.Write(proof.AlgorithmVersion); writer.Write(proof.QualifiedSourceFingerprint);
                    var math = proof.MathematicalProof;
                    writer.Write(math.AlgorithmVersion); writer.Write(math.InputFingerprint); writer.Write(math.SlotDraw);
                    writer.Write(math.MemberDraw.HasValue); if (math.MemberDraw.HasValue) writer.Write(math.MemberDraw.Value);
                    writer.Write(math.MemberIndex); writer.Write((int)step.Outcome);
                }
            });
        }

        public static SideEventSourceGeneration Begin(SideEventQualificationCheckpoint qualification)
        {
            if (qualification == null) throw new ArgumentNullException(nameof(qualification));
            if (!qualification.IsComplete) throw new InvalidOperationException("Unknown qualification inputs cannot begin source generation.");
            var qualified = qualification.Result.Qualified;
            return new SideEventSourceGeneration(qualification, qualified.Slots, Array.Empty<SideEventSourceAttemptStep>(), 0,
                qualified.Layer.AttemptTarget == 0 ? SideEventSourceGenerationStatus.AttemptBudgetExhausted : SideEventSourceGenerationStatus.Ready);
        }

        /// <summary>One allocator iteration. The injected policy owns placement.
        /// True requires the caller's lawful project adapter and successful placement;
        /// SourceOnly/Unknown is never itself permission. Missing adapters must fail.
        /// It also sees NoWeightedMember with a null member: source placement of that
        /// exceptional result is not inferred here. EmptyPool never calls the policy.
        /// If RNG/policy throws, this immutable input is unchanged; consumed external
        /// randomness/side effects are not rolled back by this pure module.</summary>
        public SideEventSourceGeneration Advance(IRandomSource random, Func<SideEventSourceAttempt, bool> placement)
        {
            if (Status != SideEventSourceGenerationStatus.Ready)
                throw new InvalidOperationException("This source layer has already stopped generation.");
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (placement == null) throw new ArgumentNullException(nameof(placement));
            var choice = SideEventChoiceSelector.Draw(_choices, random);
            var proof = new SideEventQualifiedChoiceProof(SideEventQualifiedChoiceProof.CurrentAlgorithmVersion,
                Qualification.Result.Qualified.Fingerprint, choice.Proof);
            var attempt = ResolveAttempt(Fingerprint, proof);
            var outcome = choice.Status == SideEventChoiceStatus.NoEligibleSlots ? SideEventSourceAttemptOutcome.EmptyPool :
                placement(attempt) ? SideEventSourceAttemptOutcome.Placed : SideEventSourceAttemptOutcome.PlacementFailed;
            return Append(attempt, outcome);
        }

        private SideEventSourceAttempt ResolveAttempt(string stateFingerprint, SideEventQualifiedChoiceProof proof)
        {
            if (!SideEventQualificationCheckpoint.Same(stateFingerprint, Fingerprint) || proof == null ||
                proof.AlgorithmVersion != SideEventQualifiedChoiceProof.CurrentAlgorithmVersion ||
                !SideEventQualificationCheckpoint.Same(proof.QualifiedSourceFingerprint, Qualification.Result.Qualified.Fingerprint))
                throw new ArgumentException("Attempt belongs to a different complete source allocation state.");
            var choice = SideEventChoiceSelector.ResolveFrozen(_choices, proof.MathematicalProof);
            var slot = choice.Status == SideEventChoiceStatus.NoEligibleSlots ? null : RemainingSlots[proof.MathematicalProof.SlotDraw];
            var member = choice.Status == SideEventChoiceStatus.Selected ? slot.Members[proof.MathematicalProof.MemberIndex] : null;
            return new SideEventSourceAttempt(stateFingerprint, new SideEventQualifiedSelection(proof, choice, slot, member));
        }

        private SideEventSourceGeneration Append(SideEventSourceAttempt attempt, SideEventSourceAttemptOutcome outcome)
        {
            if (Status != SideEventSourceGenerationStatus.Ready || !Enum.IsDefined(typeof(SideEventSourceAttemptOutcome), outcome))
                throw new ArgumentException("Attempt history continues after termination or has an invalid outcome.");
            bool empty = attempt.Selection.Choice.Status == SideEventChoiceStatus.NoEligibleSlots;
            if (empty != (outcome == SideEventSourceAttemptOutcome.EmptyPool))
                throw new ArgumentException("An empty pool counts an attempt without a placement outcome.");
            var remaining = new List<SideEventQualifiedSourceSlot>(RemainingSlots);
            if (outcome == SideEventSourceAttemptOutcome.Placed)
                remaining.RemoveAt(attempt.Selection.Proof.MathematicalProof.SlotDraw);
            var steps = new List<SideEventSourceAttemptStep>(Steps) { new SideEventSourceAttemptStep(attempt, outcome) };
            int attempts = outcome == SideEventSourceAttemptOutcome.PlacementFailed ? Attempts : checked(Attempts + 1);
            var status = outcome == SideEventSourceAttemptOutcome.PlacementFailed ? SideEventSourceGenerationStatus.PlacementFailed :
                attempts == Qualification.Result.Qualified.Layer.AttemptTarget ? SideEventSourceGenerationStatus.AttemptBudgetExhausted : SideEventSourceGenerationStatus.Ready;
            return new SideEventSourceGeneration(Qualification, remaining, steps, attempts, status);
        }

        public SideEventSourceGenerationData Capture()
        {
            var steps = new List<SideEventSourceAttemptData>();
            foreach (var step in Steps)
            {
                var proof = step.Attempt.Selection.Proof; var math = proof.MathematicalProof;
                steps.Add(new SideEventSourceAttemptData
                {
                    stateFingerprint = step.Attempt.StateFingerprint,
                    qualifiedProofVersion = proof.AlgorithmVersion, qualifiedFingerprint = proof.QualifiedSourceFingerprint,
                    selectionAlgorithmVersion = math.AlgorithmVersion, remainingPoolFingerprint = math.InputFingerprint,
                    slotDraw = math.SlotDraw, hasMemberDraw = math.MemberDraw.HasValue,
                    memberDraw = math.MemberDraw ?? 0, memberIndex = math.MemberIndex, outcome = (int)step.Outcome
                });
            }
            return new SideEventSourceGenerationData { version = CurrentVersion, qualification = Qualification.Capture(),
                steps = steps, attempts = Attempts, status = (int)Status, fingerprint = Fingerprint };
        }

        public string CaptureCanonicalJson() => SideEventSourceGenerationWire.Write(Capture());

        public static SideEventSourceGeneration RestoreCanonicalJson(string json)
            => Restore(SideEventSourceGenerationWire.Read(json));

        /// <summary>Replays only recorded mathematics and outcomes, using each
        /// exact remaining pool. Never rerolls, requeries eligibility or places.</summary>
        public static SideEventSourceGeneration Restore(SideEventSourceGenerationData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.version != CurrentVersion || data.steps == null)
                throw new ArgumentException("A supported version and complete attempt history are required.", nameof(data));
            var state = Begin(SideEventQualificationCheckpoint.Restore(data.qualification));
            foreach (var row in data.steps)
            {
                if (row == null || (!row.hasMemberDraw && row.memberDraw != 0))
                    throw new ArgumentException("A complete canonical attempt proof is required.", nameof(data));
                var math = new SideEventChoiceProof(row.selectionAlgorithmVersion, row.remainingPoolFingerprint,
                    row.slotDraw, row.hasMemberDraw ? row.memberDraw : (int?)null, row.memberIndex);
                var proof = new SideEventQualifiedChoiceProof(row.qualifiedProofVersion, row.qualifiedFingerprint, math);
                var attempt = state.ResolveAttempt(row.stateFingerprint, proof);
                state = state.Append(attempt, (SideEventSourceAttemptOutcome)row.outcome);
            }
            if (state.Attempts != data.attempts || (int)state.Status != data.status ||
                !SideEventQualificationCheckpoint.Same(state.Fingerprint, data.fingerprint))
                throw new ArgumentException("Attempt count, terminal status or history fingerprint changed.", nameof(data));
            return state;
        }
    }

    /// <summary>Closed four-DTO checkpoint wire, not a general object serializer.
    /// The complete source uses its existing document codec. Core readonly graphs
    /// are never reflected. Every field is explicit; default values cannot conceal
    /// missing/duplicate fields, invalid integers or wire null versus an object.</summary>
    internal static class SideEventSourceGenerationWire
    {
        private static readonly HashSet<Type> Dtos = new HashSet<Type>
        {
            typeof(SideEventSourceGenerationData), typeof(SideEventSourceAttemptData),
            typeof(SideEventQualificationCheckpointData), typeof(SideEventRequirementDecisionData)
        };
        private static readonly Regex Integer = new Regex(@"^-?(?:0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);

        internal static string Write(SideEventSourceGenerationData data)
        {
            var output = new StringBuilder(); WriteValue(output, data, typeof(SideEventSourceGenerationData));
            return output.ToString();
        }

        internal static SideEventSourceGenerationData Read(string json)
        {
            var reader = new Reader(json);
            object wire = reader.Value(0); reader.Finish();
            return (SideEventSourceGenerationData)ConvertValue(wire, typeof(SideEventSourceGenerationData));
        }

        private static FieldInfo[] Fields(Type type)
        {
            if (!Dtos.Contains(type)) throw new ArgumentException("Type is outside the source generation wire.");
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            return fields;
        }

        private static void WriteValue(StringBuilder output, object value, Type type)
        {
            if (value == null)
            {
                if (type.IsValueType) throw new ArgumentException("A wire scalar cannot be null.");
                output.Append("null"); return;
            }
            if (value.GetType() != type) throw new ArgumentException("Unexpected source generation wire type.");
            if (type == typeof(string)) { String(output, (string)value); return; }
            if (type == typeof(int)) { output.Append(((int)value).ToString(CultureInfo.InvariantCulture)); return; }
            if (type == typeof(bool)) { output.Append((bool)value ? "true" : "false"); return; }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type item = type.GetGenericArguments()[0]; Fields(item);
                output.Append('['); bool first = true;
                foreach (object element in (IEnumerable)value)
                { if (!first) output.Append(','); first = false; WriteValue(output, element, item); }
                output.Append(']'); return;
            }
            output.Append('{'); bool initial = true;
            foreach (var field in Fields(type))
            {
                if (!initial) output.Append(','); initial = false;
                String(output, field.Name); output.Append(':'); WriteValue(output, field.GetValue(value), field.FieldType);
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

        private static object ConvertValue(object wire, Type type)
        {
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
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!(wire is List<object> array)) throw new ArgumentException("A wire array is required.");
                Type item = type.GetGenericArguments()[0]; Fields(item);
                var list = (IList)Activator.CreateInstance(type);
                foreach (object element in array) list.Add(ConvertValue(element, item));
                return list;
            }
            FieldInfo[] fields = Fields(type);
            if (!(wire is Dictionary<string, object> obj) || obj.Count != fields.Length)
                throw new ArgumentException("Every declared wire field is required, with no extras.");
            object result = Activator.CreateInstance(type);
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
            private static ArgumentException Bad() => new ArgumentException("Invalid complete source generation JSON.");

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
