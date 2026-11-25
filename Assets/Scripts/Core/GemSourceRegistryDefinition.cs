using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WanjieScroll.Core
{
    /// <summary>Immutable native Gem evidence and identity directory. It owns no account state,
    /// grants no eligibility and applies no project cash-out, host or evolution policy.</summary>
    public sealed class GemSourceRegistryDefinition
    {
        public string ContentId { get; }
        public string Fingerprint { get; }
        public IReadOnlyList<GemSourceFrequency> Frequencies { get; }
        public IReadOnlyList<GemSourceTagGroup> TagGroups { get; }
        public IReadOnlyList<GemSourceExternalFile> ExternalFiles { get; }
        public IReadOnlyList<GemSourceEntry> Entries { get; }
        internal GemSourceRegistryDefinition(string id, List<GemSourceFrequency> frequencies,
            List<GemSourceTagGroup> groups, List<GemSourceExternalFile> files, List<GemSourceEntry> entries)
        {
            ContentId = id; Frequencies = Freeze(frequencies); TagGroups = Freeze(groups);
            ExternalFiles = Freeze(files); Entries = Freeze(entries);
            Fingerprint = SideEventSourceFingerprint.ComputeValue(new CanonicalRegistry(this));
        }
        public bool TryGetBySealId(string id, out GemSourceEntry entry)
        {
            foreach (var candidate in Entries)
                if (string.Equals(candidate.ProjectSealContentId, id, StringComparison.Ordinal))
                { entry = candidate; return true; }
            entry = null; return false;
        }
        public bool TryGetBySourceAssetId(string id, out GemSourceEntry entry)
        {
            foreach (var candidate in Entries)
                if (string.Equals(candidate.SourceAssetId, id, StringComparison.Ordinal))
                { entry = candidate; return true; }
            entry = null; return false;
        }
        internal static IReadOnlyList<T> Freeze<T>(IList<T> values)
        {
            var copy = new T[values.Count]; values.CopyTo(copy, 0);
            return new ReadOnlyCollection<T>(copy);
        }
        [SideEventSourceCanonical]
        private sealed class CanonicalRegistry
        {
            public string Domain { get; } = "gem-source-registry/v1";
            public string ContentId { get; }
            public IReadOnlyList<GemSourceFrequency> Frequencies { get; }
            public IReadOnlyList<GemSourceTagGroup> TagGroups { get; }
            public IReadOnlyList<GemSourceExternalFile> ExternalFiles { get; }
            public IReadOnlyList<GemSourceEntry> Entries { get; }
            internal CanonicalRegistry(GemSourceRegistryDefinition value)
            {
                ContentId = value.ContentId; Frequencies = value.Frequencies;
                TagGroups = value.TagGroups; ExternalFiles = value.ExternalFiles; Entries = value.Entries;
            }
        }
    }

    [SideEventSourceCanonical]
    public sealed class GemSourceEntry
    {
        public string ProjectSealContentId { get; }
        public string SourceAssetId { get; }
        public SideEventSourceProvenance Provenance { get; }
        public float OwnWeight { get; }
        public bool IsUnique { get; }
        public string BaseFrequencyContentId { get; }
        public GemSourceReference BaseFrequency { get; }
        public GemSourceReference Quality { get; }
        public IReadOnlyList<GemSourceReference> Tags { get; }
        public IReadOnlyList<GemSourceReference> DynamicExclusions { get; }
        public IReadOnlyList<GemSourceReference> DynamicExclusionGroups { get; }
        public IReadOnlyList<GemSourceReference> DynamicInclusions { get; }
        public IReadOnlyList<GemSourceReference> DynamicInclusionGroups { get; }
        public int SerializedBytesLength { get; }
        public IReadOnlyList<GemSourceOdinNode> OdinEntries { get; }
        public IReadOnlyList<GemSourceReference> OdinReferencedObjects { get; }
        // Classification is evidence, not permission to equip or unlock.
        public bool IsEffectClassificationKnown { get; }
        public bool HasNonColorEffects { get; }
        public bool IsEvolution { get; }
        public IReadOnlyList<GemSourceReference> ColorTriggerReferences { get; }
        internal GemSourceEntry(string seal, string asset, SideEventSourceProvenance provenance,
            float weight, bool unique, string frequencyId, GemSourceReference frequency, GemSourceReference quality,
            List<GemSourceReference>[] arrays, int length, List<GemSourceOdinNode> nodes,
            List<GemSourceReference> referenced, bool classified, bool nonColor, bool evolution,
            List<GemSourceReference> colorReferences)
        {
            ProjectSealContentId = seal; SourceAssetId = asset; Provenance = provenance;
            OwnWeight = weight; IsUnique = unique; BaseFrequencyContentId = frequencyId; BaseFrequency = frequency;
            Quality = quality;
            Tags = GemSourceRegistryDefinition.Freeze(arrays[0]);
            DynamicExclusions = GemSourceRegistryDefinition.Freeze(arrays[1]);
            DynamicExclusionGroups = GemSourceRegistryDefinition.Freeze(arrays[2]);
            DynamicInclusions = GemSourceRegistryDefinition.Freeze(arrays[3]);
            DynamicInclusionGroups = GemSourceRegistryDefinition.Freeze(arrays[4]);
            SerializedBytesLength = length; OdinEntries = GemSourceRegistryDefinition.Freeze(nodes);
            OdinReferencedObjects = GemSourceRegistryDefinition.Freeze(referenced);
            IsEffectClassificationKnown = classified; HasNonColorEffects = nonColor; IsEvolution = evolution;
            ColorTriggerReferences = GemSourceRegistryDefinition.Freeze(colorReferences);
        }
    }

    [SideEventSourceCanonical]
    public sealed class GemSourceFrequency
    {
        public string ContentId { get; }
        public SideEventSourceProvenance Provenance { get; }
        public float ChestWeight { get; }
        public float LevelUpWeight { get; }
        public float LevelUpLuckFactor { get; }
        internal GemSourceFrequency(string id, SideEventSourceProvenance provenance, float chest, float level, float luck)
        { ContentId = id; Provenance = provenance; ChestWeight = chest; LevelUpWeight = level; LevelUpLuckFactor = luck; }
    }
    [SideEventSourceCanonical]
    public sealed class GemSourceReference
    {
        public SideEventSourceObjectReference Pointer { get; }
        public SideEventSourceProvenance Provenance { get; }
        internal GemSourceReference(SideEventSourceObjectReference pointer, SideEventSourceProvenance provenance)
        { Pointer = pointer; Provenance = provenance; }
    }
    [SideEventSourceCanonical]
    public sealed class GemSourceTagGroup
    {
        public GemSourceReference Reference { get; }
        public IReadOnlyList<GemSourceReference> Tags { get; }
        internal GemSourceTagGroup(GemSourceReference reference, List<GemSourceReference> tags)
        { Reference = reference; Tags = GemSourceRegistryDefinition.Freeze(tags); }
    }
    [SideEventSourceCanonical]
    public sealed class GemSourceExternalFile
    {
        public string Alias { get; }
        public string OwnerSourceFile { get; }
        public int FileIndex { get; }
        public string TargetSourceFile { get; }
        internal GemSourceExternalFile(string alias, string owner, int index, string target)
        { Alias = alias; OwnerSourceFile = owner; FileIndex = index; TargetSourceFile = target; }
    }
    [SideEventSourceCanonical]
    public sealed class GemSourceOdinNode
    {
        public string Kind { get; }
        public string Name { get; }
        public GemSourceOdinType Type { get; }
        public int ReferenceId { get; }
        public int DeclaredLength { get; }
        public int Offset { get; }
        public int EndOffset { get; }
        public string EntryType { get; }
        public GemSourceOdinValue Value { get; }
        public IReadOnlyList<GemSourceOdinNode> Children { get; }
        public IReadOnlyList<GemSourceOdinNode> Items { get; }
        internal GemSourceOdinNode(GemSourceOdinNodeData data, GemSourceOdinType type,
            GemSourceOdinValue value, List<GemSourceOdinNode> children, List<GemSourceOdinNode> items)
        {
            Kind = data.kind; Name = data.name; Type = type; ReferenceId = data.referenceId;
            DeclaredLength = data.declaredLength; Offset = data.offset; EndOffset = data.endOffset;
            EntryType = data.entryType; Value = value;
            Children = GemSourceRegistryDefinition.Freeze(children); Items = GemSourceRegistryDefinition.Freeze(items);
        }
    }
    [SideEventSourceCanonical]
    public sealed class GemSourceOdinType
    {
        public int Id { get; }
        public string Name { get; }
        public bool Reused { get; }
        internal GemSourceOdinType(GemSourceOdinTypeData data)
        { Id = data.id; Name = data.name; Reused = data.reused; }
    }
    [SideEventSourceCanonical]
    public sealed class GemSourceOdinValue
    {
        public string Kind { get; }
        public bool BoolValue { get; }
        public int IntValue { get; }
        public string UnsignedDecimal { get; }
        public float FloatValue { get; }
        public string StringValue { get; }
        public int ExternalReferenceIndex { get; }
        internal GemSourceOdinValue(GemSourceOdinValueData data)
        {
            Kind = data.kind; BoolValue = data.boolValue; IntValue = data.intValue;
            UnsignedDecimal = data.unsignedDecimal; FloatValue = data.floatValue;
            StringValue = data.stringValue; ExternalReferenceIndex = data.externalReferenceIndex;
        }
    }
}
