using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WanjieScroll.Core
{
    /// <summary>One qualified source occurrence. Repeated event IDs remain distinct occurrences.</summary>
    public sealed class SideEventChoiceMember
    {
        public string EventContentId { get; }
        public int Weight { get; }
        public bool IsGuaranteed { get; }
        public SideEventChoiceMember(string eventContentId, int weight, bool isGuaranteed = false)
        {
            QualifiedSideEventChoices.RequireId(eventContentId, nameof(eventContentId));
            if (weight < 0) throw new ArgumentOutOfRangeException(nameof(weight), "Negative authored weights are unsupported.");
            EventContentId = eventContentId; Weight = weight; IsGuaranteed = isGuaranteed;
        }
    }

    /// <summary>One slot instance, not a globally unique group. Qualification is supplied by its owner.</summary>
    public sealed class SideEventChoiceSlot
    {
        public string SlotContentId { get; }
        public string GroupContentId { get; }
        public IReadOnlyList<SideEventChoiceMember> Members { get; }
        internal int GuaranteedIndex { get; }
        internal int TotalWeight { get; }

        public SideEventChoiceSlot(string slotContentId, string groupContentId,
            IReadOnlyList<SideEventChoiceMember> qualifiedMembers)
        {
            QualifiedSideEventChoices.RequireId(slotContentId, nameof(slotContentId));
            QualifiedSideEventChoices.RequireId(groupContentId, nameof(groupContentId));
            if (qualifiedMembers == null) throw new ArgumentNullException(nameof(qualifiedMembers));
            SlotContentId = slotContentId; GroupContentId = groupContentId;
            var copy = new List<SideEventChoiceMember>(qualifiedMembers.Count);
            int guaranteed = -1;
            foreach (var member in qualifiedMembers)
            {
                if (member == null) throw new ArgumentException("A qualified member cannot be null.", nameof(qualifiedMembers));
                if (guaranteed < 0 && member.IsGuaranteed) guaranteed = copy.Count;
                copy.Add(member);
            }
            GuaranteedIndex = guaranteed;
            // The native chooser returns its first guaranteed member before summing.
            // Overflowing unguaranteed author data is rejected as a project guard.
            if (guaranteed < 0)
            {
                int total = 0;
                foreach (var member in copy) total = checked(total + member.Weight);
                TotalWeight = total;
            }
            Members = new ReadOnlyCollection<SideEventChoiceMember>(copy);
        }
    }

    /// <summary>
    /// Immutable, ordered, already qualified slot snapshot. Empty slots are removed
    /// as in source prefiltering; shared groups and repeated members are not collapsed.
    /// This snapshot proves selection inputs only. Party/account qualification,
    /// Arcana generation budget, placement and effect contracts belong to Dungeon.
    /// </summary>
    public sealed class QualifiedSideEventChoices
    {
        private static readonly Encoding StableIdEncoding = new UTF8Encoding(false, true);
        public IReadOnlyList<SideEventChoiceSlot> Slots { get; }
        public string Fingerprint { get; }

        public QualifiedSideEventChoices(IReadOnlyList<SideEventChoiceSlot> qualifiedSlots)
        {
            if (qualifiedSlots == null) throw new ArgumentNullException(nameof(qualifiedSlots));
            var copy = new List<SideEventChoiceSlot>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in qualifiedSlots)
            {
                if (slot == null) throw new ArgumentException("A qualified slot cannot be null.", nameof(qualifiedSlots));
                if (!ids.Add(slot.SlotContentId)) throw new ArgumentException("Slot identities must be unique; group identities may repeat.", nameof(qualifiedSlots));
                if (slot.Members.Count > 0) copy.Add(slot);
            }
            Slots = new ReadOnlyCollection<SideEventChoiceSlot>(copy);
            Fingerprint = Hash(copy);
        }

        internal static void RequireId(string id, string parameter)
        {
            if (string.IsNullOrWhiteSpace(id) || !string.Equals(id, id.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("An explicit, unpadded stable content ID is required.", parameter);
            // Replacement fallback would map distinct malformed UTF-16 IDs to
            // identical fingerprint bytes. Valid Unicode IDs remain supported.
            try { StableIdEncoding.GetByteCount(id); }
            catch (EncoderFallbackException error)
            { throw new ArgumentException("A stable content ID must contain valid Unicode.", parameter, error); }
        }

        private static string Hash(IReadOnlyList<SideEventChoiceSlot> slots)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, StableIdEncoding, leaveOpen: true))
            {
                writer.Write("side-event-selection-input/v1"); writer.Write(slots.Count);
                foreach (var slot in slots)
                {
                    writer.Write(slot.SlotContentId); writer.Write(slot.GroupContentId); writer.Write(slot.Members.Count);
                    foreach (var member in slot.Members)
                    { writer.Write(member.EventContentId); writer.Write(member.Weight); writer.Write(member.IsGuaranteed); }
                }
            }
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
        }
    }

    public enum SideEventChoiceStatus { Selected, NoEligibleSlots, NoWeightedMember }

    /// <summary>Pure wire proof. ResolveFrozen validates every scalar against the immutable inputs.</summary>
    public sealed class SideEventChoiceProof
    {
        public int AlgorithmVersion { get; }
        public string InputFingerprint { get; }
        public int SlotDraw { get; }
        public int? MemberDraw { get; }
        public int MemberIndex { get; }
        public SideEventChoiceProof(int algorithmVersion, string inputFingerprint,
            int slotDraw, int? memberDraw, int memberIndex)
        {
            AlgorithmVersion = algorithmVersion; InputFingerprint = inputFingerprint;
            SlotDraw = slotDraw; MemberDraw = memberDraw; MemberIndex = memberIndex;
        }
    }

    public sealed class SideEventChoiceResult
    {
        public SideEventChoiceStatus Status { get; }
        public SideEventChoiceProof Proof { get; }
        public SideEventChoiceSlot Slot { get; }
        public SideEventChoiceMember Member { get; }
        internal SideEventChoiceResult(SideEventChoiceProof proof, SideEventChoiceSlot slot,
            SideEventChoiceMember member, SideEventChoiceStatus status)
        { Proof = proof; Slot = slot; Member = member; Status = status; }
    }

    /// <summary>
    /// Owns one source selection step and its frozen mathematical proof: uniform
    /// slot draw, then first guaranteed member or integer cumulative > draw.
    /// Probability fields never replace Weight. Injected project RNG is not a
    /// native PRNG/seed equivalence claim. Empty/zero-total pools consume one
    /// integer decision via [0,1), preserving a cursor step without Range(0,0).
    /// Placement success/removal, failure termination and attempt budget are
    /// deliberately outside this pure selector; drawing never removes a slot.
    /// </summary>
    public static class SideEventChoiceSelector
    {
        public const int AlgorithmVersion = 1;

        public static SideEventChoiceResult Draw(QualifiedSideEventChoices choices, IRandomSource random)
        {
            if (choices == null) throw new ArgumentNullException(nameof(choices));
            if (random == null) throw new ArgumentNullException(nameof(random));
            int slotDraw = Next(random, Math.Max(1, choices.Slots.Count));
            int? memberDraw = null;
            int memberIndex = -1;
            if (choices.Slots.Count > 0)
            {
                var slot = choices.Slots[slotDraw];
                memberIndex = slot.GuaranteedIndex;
                if (memberIndex < 0)
                {
                    memberDraw = Next(random, Math.Max(1, slot.TotalWeight));
                    memberIndex = FindWeighted(slot, memberDraw.Value);
                }
            }
            return ResolveFrozen(choices, new SideEventChoiceProof(AlgorithmVersion,
                choices.Fingerprint, slotDraw, memberDraw, memberIndex));
        }

        /// <summary>No RNG, qualification, allocation, reward or mutable state is consulted.</summary>
        public static SideEventChoiceResult ResolveFrozen(QualifiedSideEventChoices choices,
            SideEventChoiceProof proof)
        {
            if (choices == null) throw new ArgumentNullException(nameof(choices));
            if (proof == null) throw new ArgumentNullException(nameof(proof));
            if (proof.AlgorithmVersion != AlgorithmVersion ||
                !string.Equals(proof.InputFingerprint, choices.Fingerprint, StringComparison.Ordinal))
                throw new ArgumentException("Frozen side-event algorithm or qualified inputs changed.", nameof(proof));
            RequireDraw(proof.SlotDraw, Math.Max(1, choices.Slots.Count));
            if (choices.Slots.Count == 0)
            {
                if (proof.MemberDraw.HasValue || proof.MemberIndex != -1)
                    throw new ArgumentException("An empty slot pool cannot contain a member decision.", nameof(proof));
                return new SideEventChoiceResult(proof, null, null, SideEventChoiceStatus.NoEligibleSlots);
            }
            var slot = choices.Slots[proof.SlotDraw];
            int expected = slot.GuaranteedIndex;
            if (expected >= 0)
            {
                if (proof.MemberDraw.HasValue)
                    throw new ArgumentException("A guaranteed member must not consume a weighted draw.", nameof(proof));
            }
            else
            {
                if (!proof.MemberDraw.HasValue)
                    throw new ArgumentException("An unguaranteed slot needs its integer member draw.", nameof(proof));
                RequireDraw(proof.MemberDraw.Value, Math.Max(1, slot.TotalWeight));
                expected = FindWeighted(slot, proof.MemberDraw.Value);
            }
            if (proof.MemberIndex != expected)
                throw new ArgumentException("Frozen member differs from its recorded integer decision.", nameof(proof));
            return expected < 0
                ? new SideEventChoiceResult(proof, slot, null, SideEventChoiceStatus.NoWeightedMember)
                : new SideEventChoiceResult(proof, slot, slot.Members[expected], SideEventChoiceStatus.Selected);
        }

        private static int FindWeighted(SideEventChoiceSlot slot, int draw)
        {
            int cumulative = 0;
            for (int i = 0; i < slot.Members.Count; i++)
            {
                cumulative = checked(cumulative + slot.Members[i].Weight);
                if (cumulative > draw) return i;
            }
            return -1;
        }

        private static int Next(IRandomSource random, int maximum)
        {
            int draw = random.Range(0, maximum); RequireDraw(draw, maximum); return draw;
        }

        private static void RequireDraw(int draw, int maximum)
        {
            if (draw < 0 || draw >= maximum)
                throw new ArgumentOutOfRangeException(nameof(draw), "Integer random decision is outside its requested range.");
        }
    }
}
