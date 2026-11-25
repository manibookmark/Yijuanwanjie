using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace WanjieScroll.Core
{
    public enum SideEventSourceKind
    { AbandonedCart, EvoStatue, ManaStatue, ArcanaChoice, CardStatOffering, ChickenOffering,
      Crystal, DuplicateOffering, GemChest, SilverGemChest, GunForHire, ManaOffering }
    public enum SideEventProjectMapping { Unknown, SourceOnly }
    public enum SideEventSourceSlotMode { SharedGroup, Inline }
    public enum SideEventRequirementKind { Base, Relic, MetricAchievement, DungeonAchievement }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceProvenance
    {
        public string Alias { get; }
        public string SourceFile { get; }
        public long PathId { get; }
        public long ByteStart { get; }
        public int ByteSize { get; }
        public string Sha256 { get; }
        public SideEventSourceProvenance(string alias, string sourceFile, long pathId,
            long byteStart, int byteSize, string sha256)
        {
            Alias = SideEventSourceGuard.Text(alias, nameof(alias));
            SourceFile = SideEventSourceGuard.RelativeFile(sourceFile, nameof(sourceFile));
            if (pathId == 0 || byteStart < 0 || byteSize <= 0) throw new ArgumentOutOfRangeException(nameof(pathId));
            PathId = pathId; ByteStart = byteStart; ByteSize = byteSize;
            Sha256 = SideEventSourceGuard.Sha(sha256, nameof(sha256));
        }
    }

    /// <summary>A raw PPtr, relative to its owning source file. No external file is guessed.</summary>
    [SideEventSourceCanonical]
    public readonly struct SideEventSourceObjectReference
    {
        public string OwnerSourceFile { get; }
        public int FileIndex { get; }
        public long PathId { get; }
        public SideEventSourceObjectReference(string ownerSourceFile, int fileIndex, long pathId)
        {
            OwnerSourceFile = SideEventSourceGuard.RelativeFile(ownerSourceFile, nameof(ownerSourceFile));
            if (fileIndex < 0) throw new ArgumentOutOfRangeException(nameof(fileIndex));
            FileIndex = fileIndex; PathId = pathId;
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceAssetReference
    {
        public string AssetId { get; }
        public string EditorAssetGuid { get; }
        public SideEventSourceAssetReference(string assetId, string editorAssetGuid)
        {
            AssetId = SideEventSourceGuard.Text(assetId, nameof(assetId), allowEmpty: true);
            EditorAssetGuid = SideEventSourceGuard.Text(editorAssetGuid, nameof(editorAssetGuid), allowEmpty: true);
            if (AssetId.Length == 0 && EditorAssetGuid.Length != 0)
                throw new ArgumentException("An empty source asset reference cannot carry an editor GUID.");
        }
    }

    [SideEventSourceCanonical]
    public readonly struct SideEventSourceVector3
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public SideEventSourceVector3(float x, float y, float z)
        { X = SideEventSourceGuard.Finite(x, nameof(x)); Y = SideEventSourceGuard.Finite(y, nameof(y)); Z = SideEventSourceGuard.Finite(z, nameof(z)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceCamera
    {
        public bool OverrideRotation { get; }
        public SideEventSourceVector3 Rotation { get; }
        public bool OverrideFov { get; }
        public float Fov { get; }
        public SideEventSourceCamera(bool overrideRotation, SideEventSourceVector3 rotation, bool overrideFov, float fov)
        { OverrideRotation = overrideRotation; Rotation = rotation; OverrideFov = overrideFov; Fov = SideEventSourceGuard.Finite(fov, nameof(fov)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourcePlacement
    {
        public bool OverrideTileOffset { get; }
        public float TileOffset { get; }
        public bool? RequireNonExitEdge { get; }
        public SideEventSourcePlacement(bool overrideTileOffset, float tileOffset, bool? requireNonExitEdge)
        { OverrideTileOffset = overrideTileOffset; TileOffset = SideEventSourceGuard.Finite(tileOffset, nameof(tileOffset)); RequireNonExitEdge = requireNonExitEdge; }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceView
    {
        public SideEventSourceObjectReference DestructibleReference { get; }
        public SideEventSourceObjectReference? PassiveViewReference { get; }
        public string DestructibleEventName { get; }
        public string PassiveEventName { get; }
        public string EdgeBumpSoundKey { get; }
        public SideEventSourceView(SideEventSourceObjectReference destructibleReference,
            SideEventSourceObjectReference? passiveViewReference, string destructibleEventName,
            string passiveEventName, string edgeBumpSoundKey)
        {
            SideEventSourceGuard.Reference(destructibleReference); if (passiveViewReference.HasValue) SideEventSourceGuard.Reference(passiveViewReference.Value);
            DestructibleReference = destructibleReference; PassiveViewReference = passiveViewReference;
            DestructibleEventName = SideEventSourceGuard.OptionalText(destructibleEventName);
            PassiveEventName = SideEventSourceGuard.OptionalText(passiveEventName);
            EdgeBumpSoundKey = SideEventSourceGuard.OptionalText(edgeBumpSoundKey);
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventRootConfiguration
    {
        public bool AllowDetailsOverride { get; }
        public bool IsEdgeEvent { get; }
        public SideEventSourcePlacement Placement { get; }
        public SideEventSourceCamera Camera { get; }
        public SideEventSourceView View { get; }
        public SideEventRootConfiguration(bool allowDetailsOverride, bool isEdgeEvent,
            SideEventSourcePlacement placement, SideEventSourceCamera camera, SideEventSourceView view)
        { AllowDetailsOverride = allowDetailsOverride; IsEdgeEvent = isEdgeEvent;
          Placement = placement ?? throw new ArgumentNullException(nameof(placement));
          Camera = camera ?? throw new ArgumentNullException(nameof(camera)); View = view ?? throw new ArgumentNullException(nameof(view)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceDetails
    {
        public string SourceAssetId { get; }
        public SideEventSourceObjectReference IconReference { get; }
        public float IconScale { get; }
        public SideEventSourceObjectReference MarkerReference { get; }
        public bool ClearWhenClearingEncounters { get; }
        public SideEventSourceAssetReference DefaultReference { get; }
        public SideEventSourceProvenance Provenance { get; }
        public SideEventSourceDetails(string sourceAssetId, SideEventSourceObjectReference iconReference,
            float iconScale, SideEventSourceObjectReference markerReference, bool clearWhenClearingEncounters,
            SideEventSourceAssetReference defaultReference, SideEventSourceProvenance provenance)
        {
            SourceAssetId = SideEventSourceGuard.Text(sourceAssetId, nameof(sourceAssetId));
            SideEventSourceGuard.Reference(iconReference); SideEventSourceGuard.Reference(markerReference);
            IconReference = iconReference; MarkerReference = markerReference;
            IconScale = SideEventSourceGuard.Nonnegative(iconScale, nameof(iconScale));
            ClearWhenClearingEncounters = clearWhenClearingEncounters;
            DefaultReference = defaultReference ?? throw new ArgumentNullException(nameof(defaultReference));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            if (DefaultReference.AssetId != SourceAssetId) throw new ArgumentException("Default Details reference must match its actual source object.");
            if (IconReference.OwnerSourceFile != Provenance.SourceFile || MarkerReference.OwnerSourceFile != Provenance.SourceFile)
                throw new ArgumentException("Details PPtr owners must be the captured Details source file.");
        }
    }

    /// <summary>Configured source requirement only; no method evaluates an account or declares eligibility.</summary>
    [SideEventSourceCanonical]
    public abstract class SideEventSourceRequirement
    {
        public SideEventRequirementKind Kind { get; }
        public string SourceType { get; }
        internal SideEventSourceRequirement(SideEventRequirementKind kind, string sourceType)
        {
            string expected = "Nosebleed.Pancake.GameConfig." + (kind == SideEventRequirementKind.Base ? "EventRequirement" : kind == SideEventRequirementKind.Relic ? "RelicEventRequirement" : "AchievementEventRequirement") + ", Pancake";
            if (sourceType != expected) throw new ArgumentException("Source Odin requirement type must match the typed family.", nameof(sourceType));
            Kind = kind; SourceType = sourceType;
        }
    }
    [SideEventSourceCanonical]
    public sealed class SideEventBaseRequirement : SideEventSourceRequirement
    { public SideEventBaseRequirement(string sourceType) : base(SideEventRequirementKind.Base, sourceType) { } }
    [SideEventSourceCanonical]
    public sealed class SideEventRelicRequirement : SideEventSourceRequirement
    {
        public string SourceAssetId { get; }
        public SideEventSourceProvenance Provenance { get; }
        public SideEventRelicRequirement(string sourceAssetId, SideEventSourceProvenance provenance, string sourceType) : base(SideEventRequirementKind.Relic, sourceType)
        { SourceAssetId = SideEventSourceGuard.Text(sourceAssetId, nameof(sourceAssetId)); Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance)); }
    }
    [SideEventSourceCanonical]
    public sealed class SideEventMetricRequirement : SideEventSourceRequirement
    {
        public string SourceAssetId { get; }
        public string AchievementId { get; }
        public bool AllowInDemo { get; }
        public int RewardType { get; }
        public bool HasUnlockReward { get; }
        public string MetricId { get; }
        public int MetricType { get; }
        public int CollectionType { get; }
        public float UnlockValue { get; }
        public SideEventSourceProvenance Provenance { get; }
        public SideEventMetricRequirement(string sourceAssetId, string achievementId, bool allowInDemo,
            string metricId, int metricType, int collectionType, float unlockValue, SideEventSourceProvenance provenance, string sourceType,
            int rewardType, bool hasUnlockReward)
            : base(SideEventRequirementKind.MetricAchievement, sourceType)
        {
            SourceAssetId = SideEventSourceGuard.Text(sourceAssetId, nameof(sourceAssetId));
            AchievementId = SideEventSourceGuard.Text(achievementId, nameof(achievementId));
            MetricId = SideEventSourceGuard.Text(metricId, nameof(metricId));
            if (metricType < 0 || collectionType < 0 || collectionType > 2) throw new ArgumentOutOfRangeException(nameof(collectionType));
            if (rewardType < 0) throw new ArgumentOutOfRangeException(nameof(rewardType));
            AllowInDemo = allowInDemo; RewardType = rewardType; HasUnlockReward = hasUnlockReward; MetricType = metricType; CollectionType = collectionType;
            UnlockValue = SideEventSourceGuard.Nonnegative(unlockValue, nameof(unlockValue));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        }
    }
    [SideEventSourceCanonical]
    public sealed class SideEventSourceDungeonCondition
    {
        public bool CheckAnyDungeon { get; }
        public SideEventSourceObjectReference DungeonReference { get; }
        public bool RequiresFcc { get; }
        public SideEventSourceObjectReference FccReference { get; }
        public int AchievementType { get; }
        public float TargetPlayerLevel { get; }
        public int FloorNumberToComplete { get; }
        public bool CheckFloor { get; }
        public SideEventSourceObjectReference EncounterReference { get; }
        public SideEventSourceDungeonCondition(bool checkAnyDungeon, SideEventSourceObjectReference dungeonReference,
            bool requiresFcc, SideEventSourceObjectReference fccReference, int achievementType,
            float targetPlayerLevel, int floorNumberToComplete, bool checkFloor, SideEventSourceObjectReference encounterReference)
        {
            SideEventSourceGuard.Reference(dungeonReference); SideEventSourceGuard.Reference(fccReference); SideEventSourceGuard.Reference(encounterReference);
            if (achievementType < 0 || floorNumberToComplete < 0) throw new ArgumentOutOfRangeException(nameof(achievementType));
            CheckAnyDungeon = checkAnyDungeon; DungeonReference = dungeonReference; RequiresFcc = requiresFcc;
            FccReference = fccReference; AchievementType = achievementType;
            TargetPlayerLevel = SideEventSourceGuard.Nonnegative(targetPlayerLevel, nameof(targetPlayerLevel));
            FloorNumberToComplete = floorNumberToComplete; CheckFloor = checkFloor; EncounterReference = encounterReference;
        }
    }
    [SideEventSourceCanonical]
    public sealed class SideEventDungeonRequirement : SideEventSourceRequirement
    {
        public string SourceAssetId { get; }
        public string AchievementId { get; }
        public bool AllowInDemo { get; }
        public int RewardType { get; }
        public bool HasUnlockReward { get; }
        public SideEventSourceDungeonCondition Condition { get; }
        public SideEventSourceProvenance Provenance { get; }
        public SideEventDungeonRequirement(string sourceAssetId, string achievementId, bool allowInDemo,
            SideEventSourceDungeonCondition condition, SideEventSourceProvenance provenance, string sourceType,
            int rewardType, bool hasUnlockReward) : base(SideEventRequirementKind.DungeonAchievement, sourceType)
        { SourceAssetId = SideEventSourceGuard.Text(sourceAssetId, nameof(sourceAssetId));
          AchievementId = SideEventSourceGuard.Text(achievementId, nameof(achievementId));
          if (rewardType < 0) throw new ArgumentOutOfRangeException(nameof(rewardType));
          AllowInDemo = allowInDemo; RewardType = rewardType; HasUnlockReward = hasUnlockReward;
          Condition = condition ?? throw new ArgumentNullException(nameof(condition)); Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceRoot
    {
        public string ContentId { get; }
        public string SourceAssetId { get; }
        public SideEventSourceKind Kind => Mechanic.Kind;
        public SideEventRootConfiguration Configuration { get; }
        public SideEventSourceDetails Details { get; }
        public SideEventSourceRequirement Requirement { get; }
        public SideEventMechanicSpecification Mechanic { get; }
        public SideEventProjectMapping Mapping { get; }
        public SideEventSourceProvenance Provenance { get; }
        public SideEventSourceRoot(string contentId, string sourceAssetId, SideEventRootConfiguration configuration,
            SideEventSourceDetails details, SideEventSourceRequirement requirement,
            SideEventMechanicSpecification mechanic, SideEventProjectMapping mapping, SideEventSourceProvenance provenance)
        {
            ContentId = SideEventSourceGuard.ContentId(contentId, "sideevent");
            SourceAssetId = SideEventSourceGuard.Text(sourceAssetId, nameof(sourceAssetId));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Details = details ?? throw new ArgumentNullException(nameof(details)); Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
            Mechanic = mechanic ?? throw new ArgumentNullException(nameof(mechanic));
            if (!Enum.IsDefined(typeof(SideEventProjectMapping), mapping)) throw new ArgumentOutOfRangeException(nameof(mapping));
            Mapping = mapping; Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            if (Configuration.View.DestructibleReference.OwnerSourceFile != Provenance.SourceFile ||
                Configuration.View.PassiveViewReference.HasValue && Configuration.View.PassiveViewReference.Value.OwnerSourceFile != Provenance.SourceFile)
                throw new ArgumentException("Root raw PPtr owners must match the captured root source file.");
            bool destructible = Kind == SideEventSourceKind.AbandonedCart || Kind == SideEventSourceKind.EvoStatue || Kind == SideEventSourceKind.ManaStatue;
            if (destructible)
            {
                if (Configuration.View.PassiveViewReference.HasValue || Configuration.View.PassiveEventName != null ||
                    Configuration.View.DestructibleReference.PathId == 0 || !Configuration.Placement.RequireNonExitEdge.HasValue)
                    throw new ArgumentException("Destructible source roots preserve absent passive fields and explicit edge/prop fields.");
            }
            else if (!Configuration.View.PassiveViewReference.HasValue || Configuration.View.PassiveViewReference.Value.PathId == 0 ||
                Configuration.View.DestructibleEventName != null || Configuration.Placement.RequireNonExitEdge.HasValue)
                throw new ArgumentException("Passive source roots require their explicit view and preserve absent destructible-only fields.");
        }
    }

    /// <summary>Infinity is allowed only for this inactive source cache, never as a selector weight.</summary>
    [SideEventSourceCanonical]
    public readonly struct SideEventSourceCachedNumber
    {
        public bool IsPositiveInfinity { get; }
        public float FiniteValue { get; }
        public SideEventSourceCachedNumber(float finiteValue)
        { FiniteValue = SideEventSourceGuard.Finite(finiteValue, nameof(finiteValue)); IsPositiveInfinity = false; }
        private SideEventSourceCachedNumber(bool infinity) { IsPositiveInfinity = infinity; FiniteValue = 0f; }
        public static SideEventSourceCachedNumber PositiveInfinity => new SideEventSourceCachedNumber(true);
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceMember
    {
        public string RootContentId { get; }
        public int Weight { get; }
        public bool Guaranteed { get; }
        public bool Ambush { get; }
        public bool OverrideDetails { get; }
        public SideEventSourceAssetReference EventReference { get; }
        public SideEventSourceAssetReference DetailsOverrideReference { get; }
        public SideEventSourceCachedNumber CachedProbability { get; }
        public SideEventSourceMember(string rootContentId, int weight, bool guaranteed, bool ambush,
            bool overrideDetails, SideEventSourceAssetReference eventReference,
            SideEventSourceAssetReference detailsOverrideReference, SideEventSourceCachedNumber cachedProbability)
        {
            RootContentId = SideEventSourceGuard.ContentId(rootContentId, "sideevent");
            if (weight < 0) throw new ArgumentOutOfRangeException(nameof(weight));
            Weight = weight; Guaranteed = guaranteed; Ambush = ambush; OverrideDetails = overrideDetails;
            EventReference = eventReference ?? throw new ArgumentNullException(nameof(eventReference));
            if (EventReference.AssetId.Length == 0) throw new ArgumentException("Member source event identity is required.");
            DetailsOverrideReference = detailsOverrideReference ?? throw new ArgumentNullException(nameof(detailsOverrideReference));
            if (overrideDetails && DetailsOverrideReference.AssetId.Length == 0) throw new ArgumentException("A details override requires an explicit source identity.");
            CachedProbability = cachedProbability;
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceGroup
    {
        public string ContentId { get; }
        public SideEventSourceProvenance Provenance { get; }
        public float CachedTotalWeight { get; }
        public IReadOnlyList<SideEventSourceMember> Members { get; }
        public SideEventSourceGroup(string contentId, SideEventSourceProvenance provenance,
            float cachedTotalWeight, IReadOnlyList<SideEventSourceMember> members)
        { ContentId = SideEventSourceGuard.ContentId(contentId, "sidegroup"); Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
          CachedTotalWeight = SideEventSourceGuard.Finite(cachedTotalWeight, nameof(cachedTotalWeight)); Members = SideEventSourceGuard.Copy(members, nameof(members)); }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceOccurrence
    {
        public SideEventSourceProvenance LayerProvenance { get; }
        public string SerializedField { get; }
        public SideEventSourceObjectReference DungeonChoiceConfigReference { get; }
        public SideEventSourceOccurrence(SideEventSourceProvenance layerProvenance, string serializedField,
            SideEventSourceObjectReference dungeonChoiceConfigReference)
        { LayerProvenance = layerProvenance ?? throw new ArgumentNullException(nameof(layerProvenance));
          SerializedField = SideEventSourceGuard.Text(serializedField, nameof(serializedField));
          SideEventSourceGuard.Reference(dungeonChoiceConfigReference); DungeonChoiceConfigReference = dungeonChoiceConfigReference; }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceSlot
    {
        public string ContentId { get; }
        public SideEventSourceOccurrence Occurrence { get; }
        public SideEventSourceSlotMode Mode { get; }
        public string GroupContentId { get; }
        public IReadOnlyList<SideEventSourceMember> InlineMembers { get; }
        public float CachedTotalWeight { get; }
        public SideEventSourceSlot(string contentId, SideEventSourceOccurrence occurrence, SideEventSourceSlotMode mode,
            string groupContentId, IReadOnlyList<SideEventSourceMember> inlineMembers, float cachedTotalWeight)
        {
            ContentId = SideEventSourceGuard.ContentId(contentId, "sideslot"); Occurrence = occurrence ?? throw new ArgumentNullException(nameof(occurrence));
            if (!Enum.IsDefined(typeof(SideEventSourceSlotMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            InlineMembers = SideEventSourceGuard.Copy(inlineMembers, nameof(inlineMembers)); Mode = mode;
            if (mode == SideEventSourceSlotMode.SharedGroup)
            {
                GroupContentId = SideEventSourceGuard.ContentId(groupContentId, "sidegroup");
                if (InlineMembers.Count != 0 || occurrence.DungeonChoiceConfigReference.PathId == 0) throw new ArgumentException("Shared slots require a real group reference and an explicitly empty inline array.");
            }
            else
            {
                if (groupContentId != null || occurrence.DungeonChoiceConfigReference.PathId != 0) throw new ArgumentException("Inline slots cannot also bind a shared group.");
                GroupContentId = null;
            }
            CachedTotalWeight = SideEventSourceGuard.Finite(cachedTotalWeight, nameof(cachedTotalWeight));
        }
    }

    [SideEventSourceCanonical]
    public sealed class SideEventSourceLayer
    {
        public string ContentId { get; }
        public SideEventSourceProvenance Provenance { get; }
        public int AttemptTarget { get; }
        public IReadOnlyList<SideEventSourceSlot> Slots { get; }
        public int MaxAmbushes { get; }
        public bool OverrideAmbushChance { get; }
        public float AmbushChance { get; }
        public SideEventSourceLayer(string contentId, SideEventSourceProvenance provenance, int attemptTarget,
            IReadOnlyList<SideEventSourceSlot> slots, int maxAmbushes, bool overrideAmbushChance, float ambushChance)
        {
            ContentId = SideEventSourceGuard.ContentId(contentId, "sidelayer"); Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            if (attemptTarget < 0 || maxAmbushes < 0) throw new ArgumentOutOfRangeException(nameof(attemptTarget));
            AttemptTarget = attemptTarget; Slots = SideEventSourceGuard.Copy(slots, nameof(slots)); MaxAmbushes = maxAmbushes;
            OverrideAmbushChance = overrideAmbushChance; AmbushChance = SideEventSourceGuard.Finite(ambushChance, nameof(ambushChance));
            if (ambushChance < 0f || ambushChance > 1f) throw new ArgumentOutOfRangeException(nameof(ambushChance));
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in Slots)
                if (!SideEventSourceGuard.SameObject(Provenance, slot.Occurrence.LayerProvenance) ||
                    !fields.Add(slot.Occurrence.SerializedField)) throw new ArgumentException("Each slot must identify a unique occurrence in this source layer.");
        }
    }

    /// <summary>
    /// Owns the complete immutable source registration graph and its specification hash.
    /// Project IDs are supplied once; source PathIDs remain provenance. It evaluates
    /// no account, eligibility, RNG, placement, reward, Run or effective LevelNode.
    /// </summary>
    public sealed class SideEventSourceDefinition
    {
        private readonly Dictionary<string, SideEventSourceRoot> _roots;
        private readonly Dictionary<string, SideEventSourceGroup> _groups;
        private readonly Dictionary<string, SideEventSourceLayer> _layers;
        public string ContentId { get; }
        public string Fingerprint { get; }
        public IReadOnlyList<SideEventSourceRoot> Roots { get; }
        public IReadOnlyList<SideEventSourceGroup> Groups { get; }
        public IReadOnlyList<SideEventSourceLayer> Layers { get; }
        public SideEventSourceDefinition(string contentId, IReadOnlyList<SideEventSourceRoot> roots,
            IReadOnlyList<SideEventSourceGroup> groups, IReadOnlyList<SideEventSourceLayer> layers)
        {
            ContentId = SideEventSourceGuard.ContentId(contentId, "sidesource");
            Roots = SideEventSourceGuard.Copy(roots, nameof(roots)); Groups = SideEventSourceGuard.Copy(groups, nameof(groups)); Layers = SideEventSourceGuard.Copy(layers, nameof(layers));
            _roots = new Dictionary<string, SideEventSourceRoot>(StringComparer.Ordinal);
            _groups = new Dictionary<string, SideEventSourceGroup>(StringComparer.Ordinal);
            _layers = new Dictionary<string, SideEventSourceLayer>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal) { ContentId };
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Roots)
            { if (!ids.Add(item.ContentId) || !sourceIds.Add(item.SourceAssetId)) throw new ArgumentException("Root project/source identities must be unique."); _roots.Add(item.ContentId, item); }
            ValidateFrequencyIdentity();
            ValidateCartSourceObjectIdentity();
            foreach (var item in Groups)
            { if (!ids.Add(item.ContentId)) throw new ArgumentException("Group identity is duplicated."); _groups.Add(item.ContentId, item); ValidateMembers(item.Members); }
            foreach (var layer in Layers)
            {
                if (!ids.Add(layer.ContentId)) throw new ArgumentException("Layer identity is duplicated."); _layers.Add(layer.ContentId, layer);
                foreach (var slot in layer.Slots)
                {
                    if (!ids.Add(slot.ContentId)) throw new ArgumentException("Slot occurrence identities must be globally unique; group references may repeat.");
                    if (slot.Mode == SideEventSourceSlotMode.Inline) ValidateMembers(slot.InlineMembers);
                    else if (!_groups.TryGetValue(slot.GroupContentId, out var group) ||
                        group.Provenance.PathId != slot.Occurrence.DungeonChoiceConfigReference.PathId ||
                        group.Provenance.Alias != layer.Provenance.Alias || group.Provenance.SourceFile != layer.Provenance.SourceFile ||
                        slot.Occurrence.DungeonChoiceConfigReference.FileIndex != 0 ||
                        !string.Equals(slot.Occurrence.DungeonChoiceConfigReference.OwnerSourceFile, layer.Provenance.SourceFile, StringComparison.Ordinal))
                        throw new ArgumentException("A shared slot must resolve its exact captured group identity.");
                }
            }
            Fingerprint = SideEventSourceFingerprint.Compute(ContentId, Roots, Groups, Layers);
        }
        private enum CartSourceObjectRole { DropConfiguration, DropTag }
        private void ValidateCartSourceObjectIdentity()
        {
            var byId = new Dictionary<string, Tuple<CartSourceObjectRole, string>>(StringComparer.Ordinal);
            var bySource = new Dictionary<Tuple<string, string, long>, Tuple<string, CartSourceObjectRole>>();
            void Register(string contentId, SideEventSourceProvenance provenance, CartSourceObjectRole role)
            {
                string fingerprint = SideEventSourceFingerprint.ComputeValue(provenance);
                if (byId.TryGetValue(contentId, out var prior) && (prior.Item1 != role || prior.Item2 != fingerprint))
                    throw new ArgumentException("A repeated Cart source-object identity must preserve its role and complete captured provenance.");
                byId[contentId] = Tuple.Create(role, fingerprint);
                var identity = Tuple.Create(provenance.Alias, provenance.SourceFile, provenance.PathId);
                if (bySource.TryGetValue(identity, out var priorSource) && (priorSource.Item1 != contentId || priorSource.Item2 != role))
                    throw new ArgumentException("One captured Cart source object cannot receive multiple project identities or roles.");
                bySource[identity] = Tuple.Create(contentId, role);
            }
            foreach (var root in Roots)
            {
                if (!(root.Mechanic is SideEventAbandonedCartSpecification cart)) continue;
                Register(cart.DropConfigContentId, cart.DropConfigProvenance, CartSourceObjectRole.DropConfiguration);
                Register(cart.DropTagContentId, cart.DropTagProvenance, CartSourceObjectRole.DropTag);
            }
        }
        private void ValidateFrequencyIdentity()
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var sourceIds = new Dictionary<Tuple<string, string, long>, string>();
            foreach (var root in Roots)
            {
                SideEventGemOffer offer = root.Mechanic is SideEventGemChestSpecification chest ? chest.Offer :
                    root.Mechanic is SideEventSilverGemChestSpecification silver ? silver.Offer :
                    root.Mechanic is SideEventEvoStatueSpecification evo ? evo.Offer : null;
                if (offer == null) continue;
                foreach (var frequency in offer.Frequencies)
                {
                    string fingerprint = SideEventSourceFingerprint.ComputeValue(frequency);
                    if (values.TryGetValue(frequency.ContentId, out var prior) && prior != fingerprint)
                        throw new ArgumentException("A repeated Gem frequency identity must preserve its complete captured payload.");
                    values[frequency.ContentId] = fingerprint;
                    var source = frequency.Provenance;
                    var sourceKey = Tuple.Create(source.Alias, source.SourceFile, source.PathId);
                    if (sourceIds.TryGetValue(sourceKey, out var priorId) && priorId != frequency.ContentId)
                        throw new ArgumentException("One captured Gem frequency object cannot receive multiple project identities.");
                    sourceIds[sourceKey] = frequency.ContentId;
                }
            }
        }
        private void ValidateMembers(IReadOnlyList<SideEventSourceMember> members)
        {
            bool guaranteed = false; int total = 0;
            foreach (var member in members)
            {
                if (!_roots.TryGetValue(member.RootContentId, out var root) || !string.Equals(root.SourceAssetId, member.EventReference.AssetId, StringComparison.Ordinal))
                    throw new ArgumentException("Member project/source event references do not match a registered root.");
                guaranteed |= member.Guaranteed;
            }
            if (!guaranteed)
            {
                try { foreach (var member in members) total = checked(total + member.Weight); }
                catch (OverflowException e) { throw new ArgumentException("Side-event choice total weight overflows its source integer domain.", nameof(members), e); }
            }
        }
        public bool TryGetRoot(string id, out SideEventSourceRoot root) { root = null; return id != null && _roots.TryGetValue(id, out root); }
        public bool TryGetGroup(string id, out SideEventSourceGroup group) { group = null; return id != null && _groups.TryGetValue(id, out group); }
        public bool TryGetLayer(string id, out SideEventSourceLayer layer) { layer = null; return id != null && _layers.TryGetValue(id, out layer); }
    }

    internal static class SideEventSourceGuard
    {
        private static readonly Encoding Encoding = new UTF8Encoding(false, true);
        internal static string Text(string value, string parameter, bool allowEmpty = false)
        {
            if (value == null || (!allowEmpty && string.IsNullOrWhiteSpace(value)) || value != value.Trim()) throw new ArgumentException("Explicit unpadded text is required.", parameter);
            try { Encoding.GetByteCount(value); } catch (EncoderFallbackException e) { throw new ArgumentException("Text must contain valid Unicode.", parameter, e); }
            return value;
        }
        internal static string OptionalText(string value) => value == null ? null : Text(value, nameof(value), true);
        internal static string ContentId(string value, string type)
        {
            Text(value, nameof(value)); string[] parts = value.Split('.');
            if (parts.Length < 3 || parts[0] != type) throw new ArgumentException("Explicit stable " + type + " identity is required.");
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 || (i < 2 && (parts[i][0] < 'a' || parts[i][0] > 'z'))) throw new ArgumentException("Stable identity segments are required.");
                foreach (char c in parts[i]) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) throw new ArgumentException("Invalid stable identity character.");
            }
            return value;
        }
        internal static string RelativeFile(string value, string parameter)
        {
            Text(value, parameter); if (value.IndexOf(':') >= 0 || value.StartsWith("/") || value.StartsWith("\\") || value.IndexOf('\\') >= 0) throw new ArgumentException("Source files must be relative slash paths.", parameter);
            foreach (string part in value.Split('/')) if (part.Length == 0 || part == "." || part == "..") throw new ArgumentException("Source paths cannot escape their alias.", parameter);
            return value;
        }
        internal static string Sha(string value, string parameter)
        { Text(value, parameter); if (value.Length != 64) throw new ArgumentException("SHA256 must have 64 lowercase hex characters.", parameter);
          foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) throw new ArgumentException("SHA256 must be lowercase hex.", parameter); return value; }
        internal static float Finite(float value, string parameter) { if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(parameter); return value; }
        internal static float Nonnegative(float value, string parameter) { Finite(value, parameter); if (value < 0f) throw new ArgumentOutOfRangeException(parameter); return value; }
        internal static void Reference(SideEventSourceObjectReference value) { RelativeFile(value.OwnerSourceFile, nameof(value)); if (value.FileIndex < 0) throw new ArgumentException("Invalid raw PPtr."); }
        internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameter) where T : class
        { if (values == null) throw new ArgumentNullException(parameter); var copy = new T[values.Count];
          for (int i = 0; i < copy.Length; i++) copy[i] = values[i] ?? throw new ArgumentException("A configured array cannot contain null.", parameter); return Array.AsReadOnly(copy); }
        internal static bool SameSourceIdentity(SideEventSourceProvenance a, SideEventSourceProvenance b)
            => a.Alias == b.Alias && a.SourceFile == b.SourceFile && a.PathId == b.PathId;
        internal static bool SameObject(SideEventSourceProvenance a, SideEventSourceProvenance b)
            => a.Alias == b.Alias && a.SourceFile == b.SourceFile && a.PathId == b.PathId && a.ByteStart == b.ByteStart && a.ByteSize == b.ByteSize && a.Sha256 == b.Sha256;
    }
}
