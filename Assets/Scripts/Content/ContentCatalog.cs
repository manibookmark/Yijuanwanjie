using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// 游戏内容的唯一读取入口。运行时只读取由权威 JSON 生成并校验过的
    /// Resources 内容包；ContentDatabase 不参与运行时回退。
    /// </summary>
    // Scale review: this remains the compiled Content index; each extension owns
    // its wire and semantic invariants. It does not own Gem qualification or Run state.
    public sealed class ContentCatalog
    {
        public const int CurrentSchemaVersion = 6;

        private static Lazy<ContentCatalog> Shared =
            new Lazy<ContentCatalog>(CreateFromGeneratedPackage);

        private readonly Dictionary<string, VolumeContent> _volumes;
        private readonly List<VolumeContent> _volumeList;
        private readonly Dictionary<string, CommonEncounterContentDefinition> _commonEncounters;
        private readonly Dictionary<string, SideEventSourceWireDocument> _sideEventSources;
        private readonly Dictionary<string, GemSourceRegistryDocument> _gemSources;
        private readonly Dictionary<string, DungeonSideEventTimelineDefinition> _sideEventTimelines;
        private readonly IReadOnlyList<DungeonSideEventTimelineDefinition> _timelineList;
        private readonly Dictionary<string, CardSourceIdentity> _cardSourceIdentities;

        private ContentCatalog(IEnumerable<VolumeContent> volumes)
        {
            if (volumes == null)
                throw new ArgumentNullException(nameof(volumes));

            _volumes = new Dictionary<string, VolumeContent>(StringComparer.OrdinalIgnoreCase);
            _volumeList = new List<VolumeContent>();
            _commonEncounters = new Dictionary<string, CommonEncounterContentDefinition>(StringComparer.Ordinal);
            _sideEventSources = new Dictionary<string, SideEventSourceWireDocument>(StringComparer.Ordinal);
            _gemSources = new Dictionary<string, GemSourceRegistryDocument>(StringComparer.Ordinal);
            _sideEventTimelines = new Dictionary<string, DungeonSideEventTimelineDefinition>(StringComparer.Ordinal);
            _cardSourceIdentities = new Dictionary<string, CardSourceIdentity>(StringComparer.Ordinal);

            foreach (VolumeContent volume in volumes)
            {
                Normalize(volume);
                _volumeList.Add(volume);
            }

            ValidationReport = ContentValidator.Validate(_volumeList);
            if (ValidationReport.HasErrors)
            {
                string details = string.Join(
                    Environment.NewLine,
                    ValidationReport.Issues
                        .Where(issue => issue.Severity == ContentValidationSeverity.Error)
                        .Select(issue => issue.ToString()));
                throw new InvalidOperationException(
                    "运行时内容包校验失败：" + Environment.NewLine + details);
            }

            foreach (VolumeContent volume in _volumeList)
                _volumes.Add(volume.volumeId, volume);

            foreach (VolumeContent volume in _volumeList)
                foreach (CardData card in volume.cards)
                    if (!CardSourceIdentityData.IsUnknown(card.sourceIdentity))
                        _cardSourceIdentities.Add(card.contentId, CardSourceIdentity.Compile(card.sourceIdentity));

            foreach (VolumeContent volume in _volumeList)
            {
                if (volume.commonEncounters == null) continue;
                IReadOnlyDictionary<string, EnemyData> enemies =
                    CommonEncounterContentCompiler.VisibleEnemies(volume, _volumeList);
                foreach (CommonEncounterContentData data in volume.commonEncounters)
                {
                    if (!CommonEncounterContentCompiler.TryCompile(data, enemies,
                            out CommonEncounterContentDefinition definition, out IReadOnlyList<string> errors))
                        throw new InvalidOperationException("已验证普通遭遇内容编译失败：" + string.Join("; ", errors));
                    _commonEncounters.Add(definition.ContentId, definition);
                }
            }

            foreach (VolumeContent volume in _volumeList)
            {
                if (volume.sideEventSources == null) continue;
                foreach (SideEventSourceContentData data in volume.sideEventSources)
                {
                    SideEventSourceWireDocument document = SideEventSourceWireDocument.Create(data);
                    _sideEventSources.Add(document.Definition.ContentId, document);
                }
            }

            foreach (VolumeContent volume in _volumeList)
            {
                if (volume.gemSources == null) continue;
                foreach (GemSourceRegistryData data in volume.gemSources)
                {
                    GemSourceRegistryDocument document = GemSourceRegistryDocument.Create(data);
                    _gemSources.Add(document.Definition.ContentId, document);
                }
            }
            var timelines = new List<DungeonSideEventTimelineDefinition>();
            foreach (VolumeContent volume in _volumeList)
                foreach (var data in volume.sideEventTimelines ?? new List<DungeonSideEventTimelineContentData>())
                {
                    _sideEventSources.TryGetValue(data.sourceDocumentContentId, out var source);
                    if (!DungeonSideEventTimelineContentCompiler.TryCompile(data, source, out var definition, out var errors))
                        throw new InvalidOperationException("Validated source timeline failed compilation: " + string.Join("; ", errors));
                    _sideEventTimelines.Add(definition.ContentId, definition);
                    timelines.Add(definition);
                }
            _timelineList = timelines.AsReadOnly();
        }

        public static ContentCatalog Current => Shared.Value;
        /// <summary>Called only after official package publication and import succeed.
        /// The next lookup loads that package; existing immutable catalog instances
        /// and the contexts that captured them keep their original authority.</summary>
        internal static void InvalidateGeneratedPackageCache()
            => Shared = new Lazy<ContentCatalog>(CreateFromGeneratedPackage);
        public IReadOnlyList<DungeonSideEventTimelineDefinition> SideEventTimelines => _timelineList;
        public bool TryGetSideEventTimeline(string contentId, out DungeonSideEventTimelineDefinition definition)
        {
            definition = null;
            return contentId != null && _sideEventTimelines.TryGetValue(contentId, out definition);
        }
        public IReadOnlyList<VolumeContent> Volumes => _volumeList;
        public ContentValidationReport ValidationReport { get; }

        /// <summary>Returns copied native identity evidence by exact stable card ID; absence stays unknown.</summary>
        public bool TryGetCardSourceIdentity(string cardContentId, out CardSourceIdentity identity)
        {
            identity = null;
            return cardContentId != null && _cardSourceIdentities.TryGetValue(cardContentId, out identity);
        }

        public bool TryGetSideEventSource(string contentId, out SideEventSourceDefinition definition)
        {
            definition = null;
            if (!TryGetSideEventSourceDocument(contentId, out SideEventSourceWireDocument document)) return false;
            definition = document.Definition;
            return true;
        }

        /// <summary>Returns an immutable complete source document, without qualifying or executing it.</summary>
        public bool TryGetSideEventSourceDocument(string contentId, out SideEventSourceWireDocument document)
        {
            document = null;
            return contentId != null && _sideEventSources.TryGetValue(contentId, out document);
        }

        /// <summary>Returns a complete immutable Gem source document without qualification or randomness.</summary>
        public bool TryGetGemSourceDocument(string contentId, out GemSourceRegistryDocument document)
        {
            document = null;
            return contentId != null && _gemSources.TryGetValue(contentId, out document);
        }

        public bool TryGetGemSourceDefinition(string contentId, out GemSourceRegistryDefinition definition)
        {
            definition = null;
            if (!TryGetGemSourceDocument(contentId, out GemSourceRegistryDocument document)) return false;
            definition = document.Definition;
            return true;
        }

        public bool TryGetCommonEncounter(string contentId, out CommonEncounterContentDefinition definition)
        {
            definition = null;
            return contentId != null && _commonEncounters.TryGetValue(contentId, out definition);
        }

        public VolumeContent GetVolume(string volumeId)
        {
            if (string.IsNullOrWhiteSpace(volumeId))
                throw new ArgumentException("volumeId 不能为空。", nameof(volumeId));
            if (_volumes.TryGetValue(volumeId, out VolumeContent volume))
                return volume;

            throw new KeyNotFoundException($"未找到卷内容：{volumeId}。");
        }

        public bool TryGetVolume(string volumeId, out VolumeContent volume)
        {
            if (string.IsNullOrWhiteSpace(volumeId))
            {
                volume = null;
                return false;
            }

            return _volumes.TryGetValue(volumeId, out volume);
        }

        /// <summary>按稳定 contentId 查询英灵数据。</summary>
        public bool TryGetCrawler(string contentId, out CrawlerData crawler)
        {
            if (!string.IsNullOrWhiteSpace(contentId))
            {
                foreach (var volume in _volumeList)
                {
                    if (volume?.crawlers == null) continue;
                    foreach (var item in volume.crawlers)
                    {
                        if (string.Equals(item.contentId, contentId,
                                System.StringComparison.OrdinalIgnoreCase))
                        {
                            crawler = item;
                            return true;
                        }
                    }
                }
            }
            crawler = null;
            return false;
        }

        /// <summary>按稳定 contentId 查询遗珍数据。</summary>
        public bool TryGetRelic(string contentId, out RelicData relic)
        {
            if (!string.IsNullOrWhiteSpace(contentId))
            {
                foreach (VolumeContent volume in _volumeList)
                {
                    if (volume?.relics == null) continue;
                    foreach (RelicData item in volume.relics)
                    {
                        if (string.Equals(
                                item.contentId,
                                contentId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            relic = item;
                            return true;
                        }
                    }
                }
            }

            relic = null;
            return false;
        }

        internal static ContentCatalog CreateForPackage(
            IEnumerable<VolumeContent> volumes)
        {
            return new ContentCatalog(volumes);
        }

        private static ContentCatalog CreateFromGeneratedPackage()
        {
            List<VolumeContent> volumes = ContentPackageLoader.LoadFromResources();
            ContentCatalog catalog = new ContentCatalog(volumes);
            Debug.Log(
                $"[ContentCatalog] 已加载生成内容包：{catalog.Volumes.Count} 个卷。");
            return catalog;
        }

        private static void Normalize(VolumeContent volume)
        {
            if (volume == null)
                throw new InvalidOperationException("内容数据源返回了 null 卷。");

            volume.crawlers ??= new List<CrawlerData>();
            volume.cards ??= new List<CardData>();
            volume.enemies ??= new List<EnemyData>();
            volume.relics ??= new List<RelicData>();
            volume.evolutions ??= new List<EvolutionData>();
            volume.dungeons ??= new List<DungeonData>();
            // commonEncounters is optional, and its IDs/members are never
            // synthesized from names or normalized into an empty valid dataset.
            // sideEventSources/gemSources/sideEventTimelines retain the same explicit absence and identity policy.

            foreach (CrawlerData item in volume.crawlers)
            {
                if (item == null) continue;
                item.volumeId = CoalesceVolumeId(item.volumeId, volume.volumeId);
                item.contentId = CoalesceId(item.contentId, "crawler", volume.volumeId, item.name);
                item.startingCardContentIds ??= new List<string>();
            }

            foreach (CardData item in volume.cards)
            {
                if (item == null) continue;
                item.volumeId = CoalesceVolumeId(item.volumeId, volume.volumeId);
                item.contentId = CoalesceId(item.contentId, "card", volume.volumeId, item.name);
            }

            foreach (EnemyData item in volume.enemies)
            {
                if (item == null) continue;
                item.volumeId = CoalesceVolumeId(item.volumeId, volume.volumeId);
                item.contentId = CoalesceId(item.contentId, "enemy", volume.volumeId, item.name);
            }

            foreach (RelicData item in volume.relics)
            {
                if (item == null) continue;
                item.contentId = CoalesceId(item.contentId, "relic", volume.volumeId, item.name);
            }

            foreach (EvolutionData item in volume.evolutions)
            {
                if (item == null) continue;
                item.secondaryCardContentIds ??= new List<string>();
                item.secondaryCards ??= new List<string>();
                string primaryIdentity = string.IsNullOrWhiteSpace(
                        item.primaryCardContentId)
                    ? item.primaryCard
                    : item.primaryCardContentId;
                string resultIdentity = string.IsNullOrWhiteSpace(
                        item.resultCardContentId)
                    ? item.resultCard
                    : item.resultCardContentId;
                string name = $"{primaryIdentity}->{resultIdentity}";
                item.contentId = CoalesceId(item.contentId, "evolution", volume.volumeId, name);
            }

            foreach (DungeonData item in volume.dungeons)
            {
                if (item == null) continue;
                item.volumeId = CoalesceVolumeId(item.volumeId, volume.volumeId);
                item.contentId = CoalesceId(item.contentId, "dungeon", volume.volumeId, item.name);
            }
        }

        private static string CoalesceVolumeId(string itemVolumeId, string parentVolumeId)
        {
            return string.IsNullOrWhiteSpace(itemVolumeId) ? parentVolumeId : itemVolumeId;
        }

        private static string CoalesceId(
            string contentId,
            string kind,
            string volumeId,
            string displayName)
        {
            if (!string.IsNullOrWhiteSpace(contentId))
                return contentId;

            return $"legacy.{kind}.{volumeId}.{Fnv1a(displayName):x8}";
        }

        private static uint Fnv1a(string value)
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint hash = offset;
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            foreach (byte item in bytes)
            {
                hash ^= item;
                hash *= prime;
            }
            return hash;
        }
    }
}
