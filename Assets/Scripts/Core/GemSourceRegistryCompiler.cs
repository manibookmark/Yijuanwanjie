using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WanjieScroll.Core
{
    /// <summary>One conversion/validation boundary for complete native Gem evidence.
    /// Identity, PPtr resolution and the captured Odin token grammar are validated here;
    /// no source type is loaded and no account eligibility or project policy is evaluated.
    /// Reassess this single wire knowledge boundary when another serializer family is added.</summary>
    public static class GemSourceRegistryCompiler
    {
        public static bool TryCompile(GemSourceRegistryData data,
            out GemSourceRegistryDefinition definition, out IReadOnlyList<string> errors)
        {
            definition = null;
            try
            {
                definition = new Compilation().Compile(Required(data, "registry"));
                errors = Array.AsReadOnly(Array.Empty<string>()); return true;
            }
            catch (ArgumentException error)
            { errors = Array.AsReadOnly(new[] { error.Message }); return false; }
            catch (OverflowException error)
            { errors = Array.AsReadOnly(new[] { error.Message }); return false; }
        }
        private static T Required<T>(T value, string field) where T : class
            => value ?? throw new ArgumentException(field + ": explicit nonnull value required.");
        private static void Require(bool value, string message)
        { if (!value) throw new ArgumentException(message); }
        private static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        private static string Identity(SideEventSourceProvenance value)
            => value.Alias + "\n" + value.SourceFile + "\n" + value.PathId.ToString(CultureInfo.InvariantCulture);
        private static string FileKey(string alias, string owner, int index)
            => alias + "\n" + owner + "\n" + index.ToString(CultureInfo.InvariantCulture);

        private sealed class Compilation
        {
            private readonly Dictionary<string, SideEventSourceProvenance> _objects = new Dictionary<string, SideEventSourceProvenance>(StringComparer.Ordinal);
            private readonly Dictionary<string, GemSourceExternalFile> _files = new Dictionary<string, GemSourceExternalFile>(StringComparer.Ordinal);
            private readonly Dictionary<string, GemSourceFrequency> _frequencies = new Dictionary<string, GemSourceFrequency>(StringComparer.Ordinal);
            private readonly HashSet<string> _frequencyObjects = new HashSet<string>(StringComparer.Ordinal);
            private readonly Dictionary<string, GemSourceTagGroup> _groups = new Dictionary<string, GemSourceTagGroup>(StringComparer.Ordinal);

            internal GemSourceRegistryDefinition Compile(GemSourceRegistryData data)
            {
                string id = SideEventSourceGuard.ContentId(data.contentId, "gemsource");
                var files = new List<GemSourceExternalFile>();
                foreach (var raw in Required(data.externalFiles, "externalFiles"))
                {
                    Required(raw, "externalFile");
                    string alias = AtomText(raw.alias, "externalFile.alias");
                    string owner = SideEventSourceGuard.RelativeFile(raw.ownerSourceFile, "ownerSourceFile");
                    string target = SideEventSourceGuard.RelativeFile(raw.targetSourceFile, "targetSourceFile");
                    AtomText(owner, "ownerSourceFile"); AtomText(target, "targetSourceFile");
                    Require(raw.fileIndex > 0, "External file declarations require a positive index.");
                    var file = new GemSourceExternalFile(alias, owner, raw.fileIndex, target);
                    Require(!_files.ContainsKey(FileKey(alias, owner, raw.fileIndex)), "Duplicate external file declaration.");
                    _files.Add(FileKey(alias, owner, raw.fileIndex), file); files.Add(file);
                }
                var frequencies = new List<GemSourceFrequency>();
                foreach (var raw in Required(data.frequencies, "frequencies"))
                {
                    Required(raw, "frequency"); string frequencyId = SideEventSourceGuard.ContentId(raw.contentId, "sidefrequency");
                    var provenance = Provenance(raw.provenance);
                    Require(!_frequencies.ContainsKey(frequencyId) && _frequencyObjects.Add(Identity(provenance)), "Frequency identity must be one-to-one.");
                    var frequency = new GemSourceFrequency(frequencyId, provenance,
                        SideEventSourceGuard.Nonnegative(raw.chestWeight, "chestWeight"),
                        SideEventSourceGuard.Nonnegative(raw.levelUpWeight, "levelUpWeight"),
                        SideEventSourceGuard.Finite(raw.levelUpLuckFactor, "levelUpLuckFactor"));
                    _frequencies.Add(frequencyId, frequency); frequencies.Add(frequency);
                }
                Require(frequencies.Count > 0, "At least one captured frequency is required.");
                var groups = new List<GemSourceTagGroup>();
                foreach (var raw in Required(data.tagGroups, "tagGroups"))
                {
                    Required(raw, "tagGroup"); var reference = Reference(raw.reference);
                    var group = new GemSourceTagGroup(reference, References(raw.tags, reference.Provenance));
                    Require(!_groups.ContainsKey(Identity(reference.Provenance)), "Duplicate tag group declaration.");
                    _groups.Add(Identity(reference.Provenance), group); groups.Add(group);
                }
                var entries = new List<GemSourceEntry>();
                var sealIds = new HashSet<string>(StringComparer.Ordinal);
                var assetIds = new HashSet<string>(StringComparer.Ordinal);
                var sourceObjects = new HashSet<string>(StringComparer.Ordinal);
                foreach (var raw in Required(data.entries, "entries"))
                {
                    Required(raw, "entry"); var entry = Entry(raw);
                    Require(sealIds.Add(entry.ProjectSealContentId) && assetIds.Add(entry.SourceAssetId) && sourceObjects.Add(Identity(entry.Provenance)), "Gem seal, asset and native identities must each be unique.");
                    entries.Add(entry);
                }
                Require(entries.Count > 0, "A Gem source registry cannot be empty.");
                return new GemSourceRegistryDefinition(id, frequencies, groups, files, entries);
            }
            private SideEventSourceProvenance Provenance(SideEventSourceProvenanceData data)
            {
                Required(data, "provenance"); AtomText(data.alias, "provenance.alias"); AtomText(data.sourceFile, "provenance.sourceFile");
                var result = new SideEventSourceProvenance(data.alias, data.sourceFile, data.pathId, data.byteStart, data.byteSize, data.sha256);
                string key = Identity(result);
                if (_objects.TryGetValue(key, out var previous))
                    Require(SideEventSourceGuard.SameObject(previous, result), "One native object has conflicting provenance.");
                else _objects.Add(key, result);
                return result;
            }
            private GemSourceReference Reference(GemSourceReferenceData data)
            {
                Required(data, "reference"); var raw = Required(data.pointer, "pointer");
                AtomText(raw.ownerSourceFile, "pointer.ownerSourceFile");
                var pointer = new SideEventSourceObjectReference(raw.ownerSourceFile, raw.fileIndex, raw.pathId);
                var target = Provenance(data.provenance);
                Require(pointer.PathId != 0 && pointer.PathId == target.PathId, "Resolved PPtr must name its nonempty target.");
                string targetFile = pointer.OwnerSourceFile;
                if (pointer.FileIndex > 0)
                {
                    Require(_files.TryGetValue(FileKey(target.Alias, pointer.OwnerSourceFile, pointer.FileIndex), out var external), "Unresolved external PPtr.");
                    targetFile = external.TargetSourceFile;
                }
                Require(targetFile == target.SourceFile, "PPtr target file mismatch.");
                return new GemSourceReference(pointer, target);
            }
            private List<GemSourceReference> References(List<GemSourceReferenceData> values, SideEventSourceProvenance owner)
            {
                var result = new List<GemSourceReference>();
                foreach (var raw in Required(values, "reference list"))
                {
                    var reference = Reference(raw);
                    Require(reference.Pointer.OwnerSourceFile == owner.SourceFile && reference.Provenance.Alias == owner.Alias, "PPtr owner file/alias mismatch.");
                    result.Add(reference);
                }
                return result;
            }
            private GemSourceEntry Entry(GemSourceEntryData raw)
            {
                string seal = SideEventSourceGuard.ContentId(raw.projectSealContentId, "seal");
                string asset = AtomText(raw.sourceAssetId, "sourceAssetId");
                var provenance = Provenance(raw.provenance);
                float weight = SideEventSourceGuard.Nonnegative(raw.ownWeight, "ownWeight");
                var frequency = Reference(raw.baseFrequency);
                Require(frequency.Pointer.OwnerSourceFile == provenance.SourceFile && frequency.Provenance.Alias == provenance.Alias, "Frequency PPtr owner mismatch.");
                var quality = Reference(raw.quality);
                Require(quality.Pointer.OwnerSourceFile == provenance.SourceFile && quality.Provenance.Alias == provenance.Alias, "Quality PPtr owner mismatch.");
                Require(_frequencies.TryGetValue(raw.baseFrequencyContentId ?? "", out var declared) &&
                    SideEventSourceGuard.SameObject(declared.Provenance, frequency.Provenance), "Base frequency stable identity/target mismatch.");
                var arrays = new[] { References(raw.tags, provenance), References(raw.dynamicExclusions, provenance),
                    References(raw.dynamicExclusionGroups, provenance), References(raw.dynamicInclusions, provenance), References(raw.dynamicInclusionGroups, provenance) };
                foreach (int index in new[] { 2, 4 }) foreach (var reference in arrays[index])
                    Require(_groups.ContainsKey(Identity(reference.Provenance)), "Dynamic group lacks its complete ordered declaration.");
                var referenced = References(raw.odinReferencedObjects, provenance);
                Require(raw.serializedBytesLength > 0, "Complete Odin byte length is required.");
                var tree = new OdinCompilation(raw.serializedBytesLength, referenced.Count);
                var nodes = tree.Nodes(Required(raw.odinEntries, "odinEntries"), 0, raw.serializedBytesLength, 0);
                Require(nodes.Count == 2 && nodes[0].Kind == "reference_node" && nodes[0].Name == "_gemEffects" && nodes[1].Kind == "reference_node" && nodes[1].Name == "_unlockType" &&
                    nodes[0].Offset == 0 && nodes[0].EndOffset == nodes[1].Offset && nodes[1].EndOffset == raw.serializedBytesLength, "Complete ordered effects/unlock roots must cover all Odin bytes.");
                Require(nodes[0].Type.Name == "System.Collections.Generic.List`1[[Nosebleed.Pancake.GameConfig.GemEffect, Pancake]], mscorlib" && nodes[0].Children.Count == 1 && nodes[0].Children[0].Kind == "array", "Captured Gem effects require their typed list/array structure.");
                var colors = new List<GemSourceReference>(); bool known = true, nonColor = false, evolution = false;
                foreach (var effect in nodes[0].Children[0].Items)
                {
                    Require(effect.Kind == "reference_node" && effect.Type != null, "Gem effects require complete typed reference nodes.");
                    string name = effect.Type.Name;
                    if (name == "Nosebleed.Pancake.GameConfig.AddCardTypeGemEffect, Pancake")
                    {
                        GemSourceOdinNode color = null;
                        foreach (var child in effect.Children) if (child.Name == "_cardTypeToAdd")
                        { Require(color == null, "Duplicate color trigger reference."); color = child; }
                        Require(color != null && color.Value != null && color.Value.Kind == "ExternalReference", "Color trigger target is required.");
                        colors.Add(referenced[color.Value.ExternalReferenceIndex]);
                    }
                    else if (KnownNonColorEffect(name))
                    { nonColor = true; evolution |= name == "Nosebleed.Pancake.GameConfig.EvolveGemEffect, Pancake"; }
                    else known = false;
                }
                return new GemSourceEntry(seal, asset, provenance, weight, raw.isUnique, raw.baseFrequencyContentId,
                    frequency, quality, arrays, raw.serializedBytesLength, nodes, referenced, known, nonColor, evolution, colors);
            }
        }
        private static bool KnownNonColorEffect(string name)
        {
            switch (name)
            {
                case "Nosebleed.Pancake.GameConfig.CardEffectGemEffect, Pancake":
                case "Nosebleed.Pancake.GameConfig.DamageMultiplierGemEffect, Pancake":
                case "Nosebleed.Pancake.GameConfig.EvolveGemEffect, Pancake":
                case "Nosebleed.Pancake.GameConfig.LeaderGemEffect, Pancake":
                case "Nosebleed.Pancake.GameConfig.MidasGemEffect, Pancake":
                case "Nosebleed.Pancake.GameConfig.NullGemEffect, Pancake":
                case "Nosebleed.Pancake.GameConfig.RetainGemEffect, Pancake":
                case "Nosebleed.Pancake.GameConfig.ReturnToHandGemEffect, Pancake":
                case "Nosebleed.Pancake.GameConfig.ReverseComboGemEffect, Pancake":
                case "Nosebleed.Pancake.GameConfig.SpikeGemEffect, Pancake": return true;
                default: return false;
            }
        }
        private static string AtomText(string value, string field)
        {
            SideEventSourceGuard.Text(value, field);
            foreach (char character in value) Require(!char.IsControl(character), field + ": control characters are invalid.");
            return value;
        }
        private sealed class OdinCompilation
        {
            private readonly int _length, _references;
            private int _count;
            private readonly Dictionary<int, string> _types = new Dictionary<int, string>();
            private readonly HashSet<int> _referenceIds = new HashSet<int>();
            internal OdinCompilation(int length, int references) { _length = length; _references = references; }
            internal List<GemSourceOdinNode> Nodes(List<GemSourceOdinNodeData> source, int start, int end, int depth)
            {
                Required(source, "Odin list"); Require(depth <= 64, "Odin tree exceeds the closed wire depth limit.");
                var result = new List<GemSourceOdinNode>(); int previousEnd = start;
                foreach (var raw in source)
                {
                    Required(raw, "Odin node"); Require(++_count <= 100000, "Odin tree is too large.");
                    SideEventSourceGuard.OptionalText(raw.name);
                    Require(raw.offset >= previousEnd && raw.offset < end && raw.offset < _length, "Odin offsets must be ordered within their parent.");
                    Required(raw.children, "Odin children"); Required(raw.items, "Odin items");
                    GemSourceOdinType type = null; GemSourceOdinValue value = null;
                    var children = new List<GemSourceOdinNode>(); var items = new List<GemSourceOdinNode>();
                    if (raw.kind == "reference_node" || raw.kind == "struct_node")
                    {
                        Require(raw.endOffset > raw.offset && raw.endOffset <= end && raw.declaredLength == -1 && raw.entryType == null && raw.value == null && raw.items.Count == 0, "Invalid typed Odin node union.");
                        var typeData = Required(raw.type, "Odin type"); AtomText(typeData.name, "Odin source type"); Require(typeData.id >= 0, "Invalid Odin type id.");
                        if (typeData.reused) Require(_types.TryGetValue(typeData.id, out var prior) && prior == typeData.name, "Reused Odin type must match a prior definition.");
                        else { Require(!_types.ContainsKey(typeData.id), "Duplicate Odin type declaration."); _types.Add(typeData.id, typeData.name); }
                        if (raw.kind == "reference_node") Require(raw.referenceId >= 0 && _referenceIds.Add(raw.referenceId), "Duplicate or missing Odin reference id.");
                        else Require(raw.referenceId == -1, "Struct cannot declare a reference id.");
                        type = new GemSourceOdinType(typeData);
                        children = Nodes(raw.children, raw.offset, raw.endOffset, depth + 1);
                    }
                    else if (raw.kind == "array")
                    {
                        Require(raw.type == null && raw.referenceId == -1 && raw.entryType == null && raw.value == null && raw.children.Count == 0 && raw.declaredLength >= 0 && raw.declaredLength == raw.items.Count && raw.endOffset > raw.offset && raw.endOffset <= end, "Invalid Odin array union or length.");
                        items = Nodes(raw.items, raw.offset, raw.endOffset, depth + 1);
                    }
                    else if (raw.kind == "value")
                    {
                        Require(raw.type == null && raw.referenceId == -1 && raw.declaredLength == -1 && raw.endOffset == -1 && raw.children.Count == 0 && raw.items.Count == 0, "Invalid Odin value node union.");
                        value = Value(raw);
                    }
                    else throw new ArgumentException("Unknown Odin node kind.");
                    result.Add(new GemSourceOdinNode(raw, type, value, children, items));
                    previousEnd = raw.endOffset == -1 ? checked(raw.offset + 1) : raw.endOffset;
                }
                return result;
            }
            private GemSourceOdinValue Value(GemSourceOdinNodeData node)
            {
                var data = Required(node.value, "Odin value"); string expected;
                switch (node.entryType)
                {
                    case "NamedBoolean": expected = "Boolean"; break;
                    case "NamedInt": expected = "Int32"; break;
                    case "NamedULong": expected = "UInt64"; break;
                    case "NamedFloat": case "UnnamedFloat": expected = "Single"; break;
                    case "NamedString": expected = "String"; break;
                    case "NamedNull": expected = "Null"; break;
                    case "NamedExternalReferenceByIndex": expected = "ExternalReference"; break;
                    default: throw new ArgumentException("Unknown captured Odin value entry type.");
                }
                Require(node.entryType == "UnnamedFloat" ? node.name == null : node.name != null && node.name.Length > 0, "Odin named/unnamed token mismatch.");
                Require(data.kind == expected, "Odin entry type/value atom mismatch.");
                Require(expected == "Boolean" || !data.boolValue, "Inactive Boolean atom must be false.");
                Require(expected == "Int32" || data.intValue == -1, "Inactive int atom must be -1.");
                Require(expected == "UInt64" || data.unsignedDecimal == null, "Inactive UInt64 atom must be null.");
                Require(expected == "Single" || Bits(data.floatValue) == 0, "Inactive float atom must be positive zero.");
                Require(expected == "String" || data.stringValue == null, "Inactive string atom must be null.");
                Require(expected == "ExternalReference" || data.externalReferenceIndex == -1, "Inactive external-reference atom must be -1.");
                if (expected == "UInt64") Require(ulong.TryParse(data.unsignedDecimal, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number) && number.ToString(CultureInfo.InvariantCulture) == data.unsignedDecimal, "UInt64 requires its canonical decimal spelling.");
                if (expected == "Single") SideEventSourceGuard.Finite(data.floatValue, "Odin Single");
                if (expected == "String")
                {
                    Required(data.stringValue, "Odin string");
                    try { new UTF8Encoding(false, true).GetByteCount(data.stringValue); }
                    catch (EncoderFallbackException error) { throw new ArgumentException("Odin string must contain valid Unicode.", error); }
                }
                if (expected == "ExternalReference") Require(data.externalReferenceIndex >= 0 && data.externalReferenceIndex < _references, "Odin external reference index is out of range.");
                return new GemSourceOdinValue(data);
            }
        }
    }
}
