using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>Closed author/checkpoint wire for native Gem evidence. Every collection is explicit.
    /// Source tree types are metadata, never CLR types or permission to unlock a project reward.</summary>
    [Serializable]
    public sealed class GemSourceRegistryData
    {
        public string contentId;
        public List<GemSourceFrequencyData> frequencies;
        public List<GemSourceTagGroupData> tagGroups;
        public List<GemSourceExternalFileData> externalFiles;
        public List<GemSourceEntryData> entries;
    }

    [Serializable]
    public sealed class GemSourceEntryData
    {
        public string projectSealContentId;
        public string sourceAssetId;
        public SideEventSourceProvenanceData provenance;
        public float ownWeight;
        public bool isUnique;
        public string baseFrequencyContentId;
        public GemSourceReferenceData baseFrequency;
        public GemSourceReferenceData quality;
        public List<GemSourceReferenceData> tags;
        public List<GemSourceReferenceData> dynamicExclusions;
        public List<GemSourceReferenceData> dynamicExclusionGroups;
        public List<GemSourceReferenceData> dynamicInclusions;
        public List<GemSourceReferenceData> dynamicInclusionGroups;
        public int serializedBytesLength;
        public List<GemSourceOdinNodeData> odinEntries;
        public List<GemSourceReferenceData> odinReferencedObjects;
    }

    [Serializable]
    public sealed class GemSourceFrequencyData
    {
        public string contentId;
        public SideEventSourceProvenanceData provenance;
        public float chestWeight;
        public float levelUpWeight;
        public float levelUpLuckFactor;
    }

    [Serializable]
    public sealed class GemSourceReferenceData
    {
        public SideEventSourceObjectReferenceData pointer;
        public SideEventSourceProvenanceData provenance;
    }

    [Serializable]
    public sealed class GemSourceTagGroupData
    {
        public GemSourceReferenceData reference;
        public List<GemSourceReferenceData> tags;
    }

    [Serializable]
    public sealed class GemSourceExternalFileData
    {
        public string alias;
        public string ownerSourceFile;
        public int fileIndex;
        public string targetSourceFile;
    }

    /// <summary>One captured Odin token. Inactive integers are -1, strings/DTOs null,
    /// lists explicit empty, and inactive float is positive zero. Kind is closed.</summary>
    [Serializable]
    public sealed class GemSourceOdinNodeData
    {
        public string kind;
        public string name;
        public GemSourceOdinTypeData type;
        public int referenceId = -1;
        public int declaredLength = -1;
        public int offset;
        public int endOffset = -1;
        public string entryType;
        public GemSourceOdinValueData value;
        public List<GemSourceOdinNodeData> children;
        public List<GemSourceOdinNodeData> items;
    }

    [Serializable]
    public sealed class GemSourceOdinTypeData
    {
        public int id;
        public string name;
        public bool reused;
    }

    [Serializable]
    public sealed class GemSourceOdinValueData
    {
        public string kind;
        public bool boolValue;
        public int intValue = -1;
        public string unsignedDecimal;
        public float floatValue;
        public string stringValue;
        public int externalReferenceIndex = -1;
    }
}
