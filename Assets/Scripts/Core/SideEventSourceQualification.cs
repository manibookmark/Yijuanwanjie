using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WanjieScroll.Core
{
    public enum SideEventRequirementEligibility { Unknown, Eligible, Ineligible }

    /// <summary>A captured requirement result, never a live account query or an execution grant.</summary>
    public sealed class SideEventRequirementDecision
    {
        public string RootContentId { get; }
        public SideEventRequirementEligibility Eligibility { get; }
        public SideEventRequirementDecision(string rootContentId, SideEventRequirementEligibility eligibility)
        {
            QualifiedSideEventChoices.RequireId(rootContentId, nameof(rootContentId));
            if (!Enum.IsDefined(typeof(SideEventRequirementEligibility), eligibility))
                throw new ArgumentOutOfRangeException(nameof(eligibility));
            RootContentId = rootContentId; Eligibility = eligibility;
        }
    }

    /// <summary>
    /// Immutable complete eligibility input for one original source layer. The caller
    /// captures permanent gates and the legal unequipped ordinary-Arcana count.
    /// Counts are not a holding cap or the number shown by a choice modal. Explicit
    /// Unknown is retained; missing/extra/duplicate roots are malformed input.
    /// </summary>
    public sealed class SideEventQualificationSnapshot
    {
        public const int Version = 1;
        private readonly Dictionary<string, SideEventRequirementEligibility> _decisions;
        public string SourceContentId { get; }
        public string SourceFingerprint { get; }
        public string LayerContentId { get; }
        public IReadOnlyList<SideEventRequirementDecision> Decisions { get; }
        public int? UnequippedArcanaCount { get; }
        public string Fingerprint { get; }

        public SideEventQualificationSnapshot(SideEventSourceDefinition source, string layerContentId,
            IReadOnlyList<SideEventRequirementDecision> decisions, int? unequippedArcanaCount)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!source.TryGetLayer(layerContentId, out var layer))
                throw new ArgumentException("An explicit registered source layer is required.", nameof(layerContentId));
            if (decisions == null) throw new ArgumentNullException(nameof(decisions));
            if (unequippedArcanaCount < 0) throw new ArgumentOutOfRangeException(nameof(unequippedArcanaCount));
            var required = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in layer.Slots)
                foreach (var member in Members(source, slot)) required.Add(member.RootContentId);
            _decisions = new Dictionary<string, SideEventRequirementEligibility>(StringComparer.Ordinal);
            foreach (var decision in decisions)
            {
                if (decision == null || !required.Contains(decision.RootContentId) ||
                    _decisions.ContainsKey(decision.RootContentId))
                    throw new ArgumentException("Each referenced root needs exactly one explicit requirement decision.", nameof(decisions));
                source.TryGetRoot(decision.RootContentId, out var root);
                if (root.Requirement.Kind == SideEventRequirementKind.Base &&
                    decision.Eligibility != SideEventRequirementEligibility.Eligible)
                    throw new ArgumentException("A captured unconditional source requirement cannot be replaced with a project restriction.", nameof(decisions));
                _decisions.Add(decision.RootContentId, decision.Eligibility);
            }
            if (_decisions.Count != required.Count)
                throw new ArgumentException("Missing requirement decisions must be explicit Unknown, never absent.", nameof(decisions));
            var ordered = new List<SideEventRequirementDecision>();
            foreach (var root in source.Roots)
                if (_decisions.TryGetValue(root.ContentId, out var eligibility))
                    ordered.Add(new SideEventRequirementDecision(root.ContentId, eligibility));
            Decisions = new ReadOnlyCollection<SideEventRequirementDecision>(ordered);
            SourceContentId = source.ContentId; SourceFingerprint = source.Fingerprint;
            LayerContentId = layer.ContentId; UnequippedArcanaCount = unequippedArcanaCount;
            Fingerprint = SideEventQualificationIdentity.Snapshot(this);
        }

        public SideEventRequirementEligibility GetEligibility(string rootContentId)
        {
            if (rootContentId == null || !_decisions.TryGetValue(rootContentId, out var result))
                throw new ArgumentException("Root is outside this complete qualification snapshot.", nameof(rootContentId));
            return result;
        }

        internal static IReadOnlyList<SideEventSourceMember> Members(SideEventSourceDefinition source,
            SideEventSourceSlot slot)
        {
            if (slot.Mode == SideEventSourceSlotMode.Inline) return slot.InlineMembers;
            if (!source.TryGetGroup(slot.GroupContentId, out var group))
                throw new ArgumentException("A source slot references an unregistered group.");
            return group.Members;
        }
    }

    public enum SideEventQualificationOutcome { Kept, RequirementNotMet, ArcanaBudgetExhausted }

    /// <summary>One original array occurrence; the index is provenance, never a stable association ID.</summary>
    public sealed class SideEventQualifiedOccurrence
    {
        public SideEventSourceSlot SourceSlot { get; }
        public SideEventSourceMember SourceMember { get; }
        public SideEventSourceRoot Root { get; }
        public int SourceMemberIndex { get; }
        internal SideEventQualifiedOccurrence(SideEventSourceSlot slot, SideEventSourceMember member,
            SideEventSourceRoot root, int sourceMemberIndex)
        { SourceSlot = slot; SourceMember = member; Root = root; SourceMemberIndex = sourceMemberIndex; }
    }

    /// <summary>Reverse traversal proof, including rejected occurrences and exact Arcana budget changes.</summary>
    public sealed class SideEventQualificationStep
    {
        public SideEventQualifiedOccurrence Occurrence { get; }
        public SideEventRequirementEligibility Requirement { get; }
        public SideEventQualificationOutcome Outcome { get; }
        public int? ArcanaBudgetBefore { get; }
        public int? ArcanaBudgetAfter { get; }
        internal SideEventQualificationStep(SideEventQualifiedOccurrence occurrence,
            SideEventRequirementEligibility requirement, SideEventQualificationOutcome outcome,
            int? before, int? after)
        { Occurrence = occurrence; Requirement = requirement; Outcome = outcome; ArcanaBudgetBefore = before; ArcanaBudgetAfter = after; }
    }

    public sealed class SideEventQualifiedSourceSlot
    {
        public SideEventSourceSlot Source { get; }
        public IReadOnlyList<SideEventQualifiedOccurrence> Members { get; }
        internal SideEventQualifiedSourceSlot(SideEventSourceSlot source,
            IReadOnlyList<SideEventQualifiedOccurrence> members)
        { Source = source; Members = new ReadOnlyCollection<SideEventQualifiedOccurrence>(new List<SideEventQualifiedOccurrence>(members)); }
    }

    /// <summary>Pairs the old mathematical decision with this complete qualified source contract.</summary>
    public sealed class SideEventQualifiedChoiceProof
    {
        public const int CurrentAlgorithmVersion = 1;
        public int AlgorithmVersion { get; }
        public string QualifiedSourceFingerprint { get; }
        public SideEventChoiceProof MathematicalProof { get; }
        public SideEventQualifiedChoiceProof(int algorithmVersion, string qualifiedSourceFingerprint,
            SideEventChoiceProof mathematicalProof)
        { AlgorithmVersion = algorithmVersion; QualifiedSourceFingerprint = qualifiedSourceFingerprint; MathematicalProof = mathematicalProof; }
    }

    public sealed class SideEventQualifiedSelection
    {
        public SideEventQualifiedChoiceProof Proof { get; }
        public SideEventChoiceResult Choice { get; }
        public SideEventQualifiedSourceSlot Slot { get; }
        public SideEventQualifiedOccurrence Member { get; }
        internal SideEventQualifiedSelection(SideEventQualifiedChoiceProof proof, SideEventChoiceResult choice,
            SideEventQualifiedSourceSlot slot, SideEventQualifiedOccurrence member)
        { Proof = proof; Choice = choice; Slot = slot; Member = member; }
    }

    /// <summary>
    /// Full source payloads survive qualification and are paired positionally with
    /// the existing selector's mathematical inputs. SourceOnly and Unknown project
    /// mappings are retained; a source eligibility result does not authorize an
    /// unimplemented project effect, placement or reward. No RNG/state is owned.
    /// </summary>
    public sealed class SideEventQualifiedSourceLayer
    {
        public SideEventSourceDefinition Source { get; }
        public SideEventSourceLayer Layer { get; }
        public SideEventQualificationSnapshot Snapshot { get; }
        public IReadOnlyList<SideEventQualifiedSourceSlot> Slots { get; }
        public IReadOnlyList<SideEventQualificationStep> Steps { get; }
        public int? RemainingArcanaBudget { get; }
        public QualifiedSideEventChoices ChoiceInputs { get; }
        public string Fingerprint { get; }
        internal SideEventQualifiedSourceLayer(SideEventSourceDefinition source, SideEventSourceLayer layer,
            SideEventQualificationSnapshot snapshot, List<SideEventQualifiedSourceSlot> slots,
            List<SideEventQualificationStep> steps, int? remaining)
        {
            Source = source; Layer = layer; Snapshot = snapshot; RemainingArcanaBudget = remaining;
            Slots = new ReadOnlyCollection<SideEventQualifiedSourceSlot>(new List<SideEventQualifiedSourceSlot>(slots));
            Steps = new ReadOnlyCollection<SideEventQualificationStep>(new List<SideEventQualificationStep>(steps));
            var choices = new List<SideEventChoiceSlot>();
            foreach (var slot in Slots)
            {
                var members = new List<SideEventChoiceMember>();
                foreach (var occurrence in slot.Members)
                    members.Add(new SideEventChoiceMember(occurrence.Root.ContentId,
                        occurrence.SourceMember.Weight, occurrence.SourceMember.Guaranteed));
                // An inline array's explicit slot ID identifies its own bucket;
                // it is not a fabricated shared-group declaration or new ID.
                choices.Add(new SideEventChoiceSlot(slot.Source.ContentId,
                    slot.Source.GroupContentId ?? slot.Source.ContentId, members));
            }
            ChoiceInputs = new QualifiedSideEventChoices(choices);
            Fingerprint = SideEventQualificationIdentity.Qualified(this);
        }

        public SideEventQualifiedSelection Draw(IRandomSource random)
        {
            var choice = SideEventChoiceSelector.Draw(ChoiceInputs, random);
            return ResolveSelection(new SideEventQualifiedChoiceProof(SideEventQualifiedChoiceProof.CurrentAlgorithmVersion,
                Fingerprint, choice.Proof));
        }

        /// <summary>Reads a complete bound decision without RNG, account queries or payload regeneration.</summary>
        public SideEventQualifiedSelection ResolveSelection(SideEventQualifiedChoiceProof proof)
        {
            if (proof == null) throw new ArgumentNullException(nameof(proof));
            if (proof.AlgorithmVersion != SideEventQualifiedChoiceProof.CurrentAlgorithmVersion ||
                !string.Equals(proof.QualifiedSourceFingerprint, Fingerprint, StringComparison.Ordinal) ||
                proof.MathematicalProof == null)
                throw new ArgumentException("Frozen selection belongs to a different complete qualified source contract.", nameof(proof));
            var mathematical = proof.MathematicalProof;
            var choice = SideEventChoiceSelector.ResolveFrozen(ChoiceInputs, mathematical);
            if (choice.Status == SideEventChoiceStatus.NoEligibleSlots)
                return new SideEventQualifiedSelection(proof, choice, null, null);
            var slot = Slots[mathematical.SlotDraw];
            var member = choice.Status == SideEventChoiceStatus.Selected ? slot.Members[mathematical.MemberIndex] : null;
            return new SideEventQualifiedSelection(proof, choice, slot, member);
        }
    }

    public sealed class SideEventQualificationResult
    {
        public bool IsComplete => Qualified != null;
        public SideEventQualificationSnapshot Snapshot { get; }
        public SideEventQualifiedSourceLayer Qualified { get; }
        public IReadOnlyList<string> UnknownRootContentIds { get; }
        public bool ArcanaBudgetUnknown { get; }
        internal SideEventQualificationResult(SideEventQualificationSnapshot snapshot,
            SideEventQualifiedSourceLayer qualified, List<string> unknown, bool budgetUnknown)
        { Snapshot = snapshot; Qualified = qualified; UnknownRootContentIds = new ReadOnlyCollection<string>(unknown); ArcanaBudgetUnknown = budgetUnknown; }
    }

    /// <summary>
    /// Owns the captured source prefilter: slots reverse, members reverse,
    /// requirement first, then one ordinary-Arcana reservation per kept occurrence.
    /// Survivors retain forward source order and full references. Unknown blocks
    /// the whole preparation as project policy; it is never treated as source false.
    /// Qualification does not remove successful slots, place events or own attempts.
    /// </summary>
    public static class SideEventSourceQualifier
    {
        public const int AlgorithmVersion = 1;
        public static SideEventQualificationResult Qualify(SideEventSourceDefinition source,
            SideEventQualificationSnapshot snapshot)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (!string.Equals(source.ContentId, snapshot.SourceContentId, StringComparison.Ordinal) ||
                !string.Equals(source.Fingerprint, snapshot.SourceFingerprint, StringComparison.Ordinal) ||
                !source.TryGetLayer(snapshot.LayerContentId, out var layer))
                throw new ArgumentException("The frozen qualification input belongs to a different source contract.", nameof(snapshot));
            var unknown = new List<string>();
            bool needsArcanaBudget = false;
            foreach (var decision in snapshot.Decisions)
            {
                source.TryGetRoot(decision.RootContentId, out var root);
                if (decision.Eligibility == SideEventRequirementEligibility.Unknown) unknown.Add(decision.RootContentId);
                if (decision.Eligibility == SideEventRequirementEligibility.Eligible && root.Kind == SideEventSourceKind.ArcanaChoice)
                    needsArcanaBudget = true;
            }
            bool budgetUnknown = needsArcanaBudget && !snapshot.UnequippedArcanaCount.HasValue;
            if (unknown.Count > 0 || budgetUnknown)
                return new SideEventQualificationResult(snapshot, null, unknown, budgetUnknown);
            var working = new List<List<SideEventQualifiedOccurrence>>();
            foreach (var slot in layer.Slots)
            {
                var members = SideEventQualificationSnapshot.Members(source, slot);
                var copy = new List<SideEventQualifiedOccurrence>();
                for (int i = 0; i < members.Count; i++)
                {
                    // Native List.Remove(reference) has unverified edge behavior
                    // for aliases in the same list. Distinct equal-valued objects
                    // remain legal; rejecting aliases avoids silently normalizing it.
                    for (int earlier = 0; earlier < i; earlier++)
                        if (ReferenceEquals(members[earlier], members[i]))
                            throw new NotSupportedException("Repeated references within one source member array need a separate removal contract.");
                    source.TryGetRoot(members[i].RootContentId, out var root);
                    copy.Add(new SideEventQualifiedOccurrence(slot, members[i], root, i));
                }
                working.Add(copy);
            }
            int? budget = snapshot.UnequippedArcanaCount;
            var steps = new List<SideEventQualificationStep>();
            for (int slotIndex = working.Count - 1; slotIndex >= 0; slotIndex--)
                for (int memberIndex = working[slotIndex].Count - 1; memberIndex >= 0; memberIndex--)
                {
                    var occurrence = working[slotIndex][memberIndex];
                    var requirement = snapshot.GetEligibility(occurrence.Root.ContentId);
                    int? before = budget;
                    var outcome = SideEventQualificationOutcome.Kept;
                    if (requirement == SideEventRequirementEligibility.Ineligible)
                        outcome = SideEventQualificationOutcome.RequirementNotMet;
                    else if (occurrence.Root.Kind == SideEventSourceKind.ArcanaChoice)
                    {
                        if (budget.Value <= 0) outcome = SideEventQualificationOutcome.ArcanaBudgetExhausted;
                        else budget = budget.Value - 1;
                    }
                    if (outcome != SideEventQualificationOutcome.Kept) working[slotIndex].RemoveAt(memberIndex);
                    steps.Add(new SideEventQualificationStep(occurrence, requirement, outcome, before, budget));
                }
            var qualified = new List<SideEventQualifiedSourceSlot>();
            for (int i = 0; i < working.Count; i++)
                if (working[i].Count > 0) qualified.Add(new SideEventQualifiedSourceSlot(layer.Slots[i], working[i]));
            return new SideEventQualificationResult(snapshot,
                new SideEventQualifiedSourceLayer(source, layer, snapshot, qualified, steps, budget), unknown, false);
        }
    }

    /// <summary>Length-framed identities prove captured inputs and deterministic output, not account authenticity.</summary>
    internal static class SideEventQualificationIdentity
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        internal static string Snapshot(SideEventQualificationSnapshot value) => Hash(writer =>
        {
            writer.Write("side-event-qualification-input/v1"); writer.Write(SideEventQualificationSnapshot.Version);
            writer.Write(value.SourceContentId); writer.Write(value.SourceFingerprint); writer.Write(value.LayerContentId);
            Optional(writer, value.UnequippedArcanaCount); writer.Write(value.Decisions.Count);
            foreach (var decision in value.Decisions) { writer.Write(decision.RootContentId); writer.Write((int)decision.Eligibility); }
        });
        internal static string Qualified(SideEventQualifiedSourceLayer value) => Hash(writer =>
        {
            writer.Write("side-event-qualified-source-layer/v1"); writer.Write(SideEventSourceQualifier.AlgorithmVersion);
            writer.Write(value.Source.Fingerprint); writer.Write(value.Snapshot.Fingerprint);
            writer.Write(value.Layer.ContentId); writer.Write(value.ChoiceInputs.Fingerprint);
            Optional(writer, value.RemainingArcanaBudget); writer.Write(value.Steps.Count);
            foreach (var step in value.Steps)
            {
                writer.Write(step.Occurrence.SourceSlot.ContentId); writer.Write(step.Occurrence.SourceMemberIndex);
                writer.Write(step.Occurrence.Root.ContentId); writer.Write((int)step.Requirement); writer.Write((int)step.Outcome);
                Optional(writer, step.ArcanaBudgetBefore); Optional(writer, step.ArcanaBudgetAfter);
            }
        });
        private static void Optional(BinaryWriter writer, int? value)
        { writer.Write(value.HasValue); if (value.HasValue) writer.Write(value.Value); }
        private static string Hash(Action<BinaryWriter> write)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, Utf8, leaveOpen: true)) write(writer);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
        }
    }
}
