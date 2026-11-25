using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Owns the Run's complete frozen per-node encounter choices. Generated
    /// species and source cells, evaluated float bits and content revisions are
    /// chosen once in detached floor data. Existing authored/preset modes keep
    /// their legacy contracts. Core owns selection legality and live geometry;
    /// reads/restores/preloads never evaluate curves or consume a random stream.
    /// This remains one state/persistence boundary despite its review threshold;
    /// no actor, UI, resource lifetime or physical placement state is owned here.
    /// </summary>
    internal sealed class DungeonRunFormationState
    {
        private const int SaveVersion = 2;
        private readonly Func<string, CommonEncounterSourceContract> _commonSources;
        internal Func<string, CommonEncounterSourceContract> CommonSources => _commonSources;
        private readonly Func<int, IRandomSource> _randomFactory;
        private readonly int _seed;
        private readonly Dictionary<string, DungeonRunFormationFloorSaveData> _floors =
            new Dictionary<string, DungeonRunFormationFloorSaveData>(StringComparer.Ordinal);

        internal DungeonRunFormationState(int seed,
            Func<string, CommonEncounterSourceContract> commonSources = null,
            Func<int, IRandomSource> randomFactory = null)
        { _seed = seed; _commonSources = commonSources; _randomFactory = randomFactory ?? (value => new SeededRandomSource(value)); }

        internal DungeonRunFormationState Clone()
        {
            var copy = new DungeonRunFormationState(_seed, _commonSources, _randomFactory);
            foreach (var pair in _floors) copy._floors.Add(pair.Key, Copy(pair.Value));
            return copy;
        }

        internal void PrepareFloor(LevelRunProfile profile)
        {
            string contract = Contract(profile);
            if (_floors.TryGetValue(profile.contentId, out var existing))
            {
                RequireMatches(profile, existing);
                return;
            }
            if (contract == null) return;
            _floors.Add(profile.contentId, CreateFloor(profile, contract, false));
        }

        /// <summary>Strict read-only launch lookup. A generated encounter always
        /// needs the actual node or active launch, even if its Scenario is unique.</summary>
        internal DungeonRunEncounterSnapshot ResolveRoster(LevelRunSession floor,
            string scenarioId, string nodeId = null, bool allowLegacyUnique = false)
            => ResolveRoster(floor, scenarioId, nodeId, allowLegacyUnique, null);

        private DungeonRunEncounterSnapshot ResolveRoster(LevelRunSession floor,
            string scenarioId, string nodeId, bool allowLegacyUnique,
            Dictionary<string, CommonEncounterSourceContract> sources)
        {
            if (floor == null) throw new ArgumentNullException(nameof(floor));
            if (floor.ActiveEncounter.HasValue)
            {
                LevelEncounterLaunch launch = floor.ActiveEncounter.Value;
                if (!string.Equals(launch.ScenarioId, scenarioId, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(nodeId) && nodeId != launch.NodeContentId))
                    throw Invalid("Frozen roster request differs from the active encounter.");
                nodeId = launch.NodeContentId;
            }
            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                LevelNode node = floor.FindNode(nodeId);
                if (node == null || node.scenarioId != scenarioId ||
                    (node.nodeType != LevelNodeType.Encounter && node.nodeType != LevelNodeType.Finale))
                    throw Invalid("Frozen roster request does not identify a current-floor encounter.");
            }
            DungeonRunFormationFloorSaveData saved = ReadFloor(floor.Profile, sources);
            if (saved == null) return null;
            DungeonRunFormationEncounterSaveData selected = null;
            if (!string.IsNullOrWhiteSpace(nodeId)) selected = saved.encounters.SingleOrDefault(item => item.nodeId == nodeId);
            else if (allowLegacyUnique)
            {
                var matches = saved.encounters.Where(item => item.scenarioId == scenarioId).ToArray();
                if (matches.Length == 1 && matches[0].mode != DungeonRunEncounterMode.Generated) selected = matches[0];
            }
            if (selected == null || selected.scenarioId != scenarioId)
                throw Invalid("A frozen generated roster requires its exact node identity; guessing is forbidden.");
            return new DungeonRunEncounterSnapshot(selected.nodeId, selected.scenarioId, selected.mode,
                (CommonFormationPreset)selected.preset, selected.mode == DungeonRunEncounterMode.Generated ? ReadGenerated(selected) : null);
        }

        internal EnemyFormationPlan Resolve(LevelRunSession floor, string scenarioId,
            IReadOnlyList<EnemyStats> enemies, string nodeId = null)
        {
            DungeonRunEncounterSnapshot snapshot = ResolveRoster(floor, scenarioId, nodeId, allowLegacyUnique: true);
            return CreatePlan(snapshot, enemies);
        }

        /// <summary>One synchronous materialization validates the source once per
        /// dataset, then rechecks the floor and launch before publishing actors.
        /// The source dictionary never escapes this call or survives a frame.</summary>
        internal EnemyFormationPlan Materialize(LevelRunSession floor, string scenarioId,
            string nodeId, Func<DungeonRunEncounterSnapshot, List<EnemyStats>> materializeEnemies,
            Func<bool> isCurrent, out List<EnemyStats> enemies)
        {
            enemies = null;
            if (floor == null) throw new ArgumentNullException(nameof(floor));
            if (materializeEnemies == null) throw new ArgumentNullException(nameof(materializeEnemies));
            if (isCurrent == null) throw new ArgumentNullException(nameof(isCurrent));
            LevelEncounterLaunch? launch = floor.ActiveEncounter;
            var sources = new Dictionary<string, CommonEncounterSourceContract>(StringComparer.Ordinal);
            DungeonRunEncounterSnapshot snapshot = ResolveRoster(floor, scenarioId, nodeId, true, sources);
            List<EnemyStats> prepared = materializeEnemies(snapshot);
            if (!isCurrent() || !SameLaunch(launch, floor.ActiveEncounter))
                throw Invalid("Encounter materialization changed the current floor or active launch.");
            // The callback is allowed to create actors, never to bypass profile,
            // frozen-roster or live-source identity validation.
            ResolveRoster(floor, scenarioId, nodeId, true, sources);
            if (prepared == null) throw Invalid("Encounter materialization returned no enemy list.");
            EnemyFormationPlan plan = CreatePlan(snapshot, prepared);
            enemies = prepared;
            return plan;
        }

        private static bool SameLaunch(LevelEncounterLaunch? first, LevelEncounterLaunch? second)
            => first.HasValue == second.HasValue && (!first.HasValue ||
                (first.Value.RunId == second.Value.RunId &&
                 first.Value.NodeContentId == second.Value.NodeContentId &&
                 first.Value.ScenarioId == second.Value.ScenarioId));

        private static EnemyFormationPlan CreatePlan(DungeonRunEncounterSnapshot snapshot,
            IReadOnlyList<EnemyStats> enemies)
        {
            if (snapshot == null || snapshot.Mode == DungeonRunEncounterMode.LegacyAuthored) return null;
            if (snapshot.Mode == DungeonRunEncounterMode.Generated)
                return CommonEnemyFormation.CreateGenerated(enemies, snapshot.GeneratedRoster);
            // A typed Floor/Stage boss retains the Scenario's authored formation.
            // Ability-tag elites (isBoss + None) still use the frozen common preset.
            // Validate the complete roster before choosing the authored path.
            return CommonEnemyFormation.HasLevelBoss(enemies)
                ? null : CommonEnemyFormation.Create(enemies, snapshot.Preset);
        }

        /// <summary>Admission only: no request curve, RNG or floor mutation.</summary>
        internal void ValidatePreparation(LevelRunProfile profile)
            => ValidatePreparation(profile, new Dictionary<string, CommonEncounterSourceContract>(StringComparer.Ordinal));

        private void ValidatePreparation(LevelRunProfile profile,
            Dictionary<string, CommonEncounterSourceContract> sources)
        {
            if (Contract(profile) == null) return;
            foreach (LevelNode node in Encounters(profile))
                if (node.HasCommonEncounter) ResolveSource(node, sources);
        }

        internal string FrozenFingerprint
        {
            get
            {
                var parts = new List<string> { SaveVersion.ToString(CultureInfo.InvariantCulture) };
                foreach (var pair in _floors.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    DungeonRunFormationFloorSaveData floor = pair.Value;
                    parts.Add(floor.floorId); parts.Add(floor.contractHash);
                    parts.Add(floor.legacyAuthored ? "legacy" : "current");
                    foreach (var item in floor.encounters.OrderBy(value => value.nodeId, StringComparer.Ordinal))
                    {
                        parts.Add(item.nodeId); parts.Add(item.scenarioId);
                        parts.Add(((int)item.mode).ToString(CultureInfo.InvariantCulture));
                        parts.Add(item.preset.ToString(CultureInfo.InvariantCulture));
                        parts.Add(item.mode == DungeonRunEncounterMode.Generated ? RosterHash(item) : string.Empty);
                    }
                }
                return Digest("frozen-run-encounters/v2", parts);
            }
        }

        internal DungeonRunFormationSaveData Capture()
            => new DungeonRunFormationSaveData
            {
                schemaVersion = SaveVersion,
                floors = _floors.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => Copy(pair.Value)).ToList()
            };

        internal void RequireFloor(LevelRunProfile profile) => ReadFloor(profile);

        private DungeonRunFormationFloorSaveData ReadFloor(LevelRunProfile profile,
            Dictionary<string, CommonEncounterSourceContract> sources = null)
        {
            if (Contract(profile) == null)
            {
                if (_floors.ContainsKey(profile.contentId))
                    throw Invalid("已冻结阵型的楼层不能关闭规则。");
                return null;
            }
            if (!_floors.TryGetValue(profile.contentId, out var saved))
                throw Invalid("已进入的楼层缺少阵型记录，禁止补抽。");
            RequireMatches(profile, saved, sources);
            return saved;
        }

        /// <summary>Preserves v24 wire presence before JsonUtility can default
        /// a missing zero-valued scalar or collection. DTO value validation is
        /// still performed by Restore, using the same persistence owner.</summary>
        internal static void RequireWireShape(string payload)
        {
            if (!JsonObjectShape.TryGetField(payload, "formations", out string formations))
                throw Invalid("Run v24 must explicitly save frozen formations.");
            RequireFields(formations, typeof(DungeonRunFormationSaveData));
            if (!JsonObjectShape.TryGetField(formations, "floors", out string floors) ||
                !JsonObjectShape.TryGetArrayItems(floors, out var floorItems))
                throw Invalid("Frozen floor collection must be an explicit array.");
            foreach (string floor in floorItems)
            {
                RequireFields(floor, typeof(DungeonRunFormationFloorSaveData));
                JsonObjectShape.TryGetField(floor, "encounters", out string encounters);
                if (!JsonObjectShape.TryGetArrayItems(encounters, out var encounterItems))
                    throw Invalid("Frozen encounter collection must be an explicit array.");
                foreach (string encounter in encounterItems)
                {
                    RequireFields(encounter, typeof(DungeonRunFormationEncounterSaveData));
                    JsonObjectShape.TryGetField(encounter, "cells", out string cells);
                    if (!JsonObjectShape.TryGetArrayItems(cells, out var cellItems))
                        throw Invalid("Frozen matrix collection must be an explicit array.");
                    foreach (string cell in cellItems)
                        RequireFields(cell, typeof(DungeonRunFormationCellSaveData));
                }
            }
        }

        private static void RequireFields(string objectJson, Type dto)
        {
            foreach (var field in dto.GetFields(System.Reflection.BindingFlags.Instance |
                         System.Reflection.BindingFlags.Public))
                if (!JsonObjectShape.TryGetField(objectJson, field.Name, out string raw) || raw == "null")
                    throw Invalid("Frozen save must explicitly contain " + dto.Name + "." + field.Name + ".");
        }

        internal static DungeonRunFormationState Restore(DungeonRunFormationSaveData saved,
            int seed, IReadOnlyList<LevelRunProfile> visited, bool requireState,
            Func<string, CommonEncounterSourceContract> commonSources = null, bool requireVersionTwo = false)
        {
            if (visited == null) throw new ArgumentNullException(nameof(visited));
            var expected = new Dictionary<string, LevelRunProfile>(StringComparer.Ordinal);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (LevelRunProfile profile in visited)
            {
                string contract = Contract(profile);
                if (!identities.Add(profile.contentId)) throw Invalid("阵型恢复含重复楼层。");
                if (contract != null) expected.Add(profile.contentId, profile);
            }
            if (!requireVersionTwo)
                foreach (LevelRunProfile profile in visited) RequireLegacyProfile(profile);
            var restored = new DungeonRunFormationState(seed, commonSources);
            bool legacyShell = !requireState && saved != null && saved.schemaVersion == 0 &&
                (saved.floors == null || saved.floors.Count == 0);
            if (saved == null || legacyShell)
            {
                if (requireState) throw Invalid("新版Run缺少阵型状态。");
                foreach (LevelRunProfile profile in expected.Values)
                {
                    RequireLegacyProfile(profile);
                    restored._floors.Add(profile.contentId, restored.CreateFloor(profile, Contract(profile), true));
                }
                return restored;
            }
            if ((saved.schemaVersion != 1 && saved.schemaVersion != SaveVersion) || saved.floors == null ||
                (requireVersionTwo && saved.schemaVersion != SaveVersion))
                throw Invalid("Unsupported or incomplete frozen encounter save version.");
            var sources = new Dictionary<string, CommonEncounterSourceContract>(StringComparer.Ordinal);
            foreach (DungeonRunFormationFloorSaveData floor in saved.floors)
            {
                if (floor == null || string.IsNullOrWhiteSpace(floor.floorId) ||
                    !expected.TryGetValue(floor.floorId, out LevelRunProfile profile) ||
                    restored._floors.ContainsKey(floor.floorId))
                    throw Invalid("阵型存档含空、重复、多余或未访问楼层。");
                DungeonRunFormationFloorSaveData copy = Copy(floor);
                if (saved.schemaVersion == 1)
                {
                    RequireLegacyProfile(profile);
                    if (floor.contractHash != LegacyContract(profile))
                        throw Invalid("Legacy authored/preset formation contract changed.");
                    copy.contractHash = Contract(profile);
                    foreach (var item in copy.encounters)
                        item.mode = copy.legacyAuthored ? DungeonRunEncounterMode.LegacyAuthored : DungeonRunEncounterMode.CommonPreset;
                }
                restored.RequireMatches(profile, copy, sources);
                restored._floors.Add(floor.floorId, copy);
            }
            if (restored._floors.Count != expected.Count)
                throw Invalid("已访问楼层的阵型状态不完整，禁止补抽。");
            return restored;
        }

        internal static void ValidateProfile(LevelRunProfile profile, List<string> errors)
        {
            try { Contract(profile); }
            catch (Exception exception) { errors.Add(exception.Message); }
        }

        private DungeonRunFormationFloorSaveData CreateFloor(LevelRunProfile profile,
            string contract, bool legacy)
        {
            var sources = new Dictionary<string, CommonEncounterSourceContract>(StringComparer.Ordinal);
            ValidatePreparation(profile, sources);
            var encounters = new List<DungeonRunFormationEncounterSaveData>();
            foreach (LevelNode node in Encounters(profile))
            {
                if (!legacy && node.HasCommonEncounter) encounters.Add(CreateGenerated(profile, node, sources));
                else encounters.Add(new DungeonRunFormationEncounterSaveData
                {
                    nodeId = node.contentId, scenarioId = node.scenarioId,
                    mode = legacy || profile.commonFormationRevision == 0 ? DungeonRunEncounterMode.LegacyAuthored : DungeonRunEncounterMode.CommonPreset,
                    preset = legacy || profile.commonFormationRevision == 0 ? 0 : 1 + (Hash("common-formation-selection/v1",
                        _seed.ToString(CultureInfo.InvariantCulture), profile.contentId,
                        profile.commonFormationRevision.ToString(CultureInfo.InvariantCulture), node.contentId, node.scenarioId)[0] & 3)
                });
            }
            // All nodes finish into detached data before this floor is adopted.
            return new DungeonRunFormationFloorSaveData
            {
                floorId = profile.contentId, revision = profile.commonFormationRevision,
                algorithmVersion = CommonEnemyFormation.AlgorithmVersion, contractHash = contract,
                legacyAuthored = legacy, encounters = encounters
            };
        }

        private DungeonRunFormationEncounterSaveData CreateGenerated(LevelRunProfile profile, LevelNode node,
            Dictionary<string, CommonEncounterSourceContract> sources)
        {
            CommonEncounterSourceContract source = ResolveSource(node, sources);
            var binding = node.commonEncounter;
            if (!source.Source.TryCreateRequest(binding.difficultyContentId, binding.tileDifficulty,
                out CommonEncounterRequest request, out string error) || request == null ||
                Bits(request.TileDifficulty) != Bits(binding.tileDifficulty))
                throw Invalid(error ?? "Common encounter request does not match the explicit tile difficulty.");
            byte[] seed = Hash("common-encounter-selection/project-lcg-v1",
                _seed.ToString(CultureInfo.InvariantCulture), profile.contentId, node.contentId, node.scenarioId,
                source.ContentId, source.GenerationFingerprint, source.RuntimeContentFingerprint,
                binding.difficultyContentId, Bits(binding.tileDifficulty).ToString(CultureInfo.InvariantCulture),
                CommonEncounterGenerator.AlgorithmVersion.ToString(CultureInfo.InvariantCulture));
            int localSeed = unchecked(seed[0] | (seed[1] << 8) | (seed[2] << 16) | (seed[3] << 24));
            IRandomSource random = _randomFactory(localSeed) ?? throw Invalid("Common encounter random stream is missing.");
            CommonEncounterResult result = CommonEncounterGenerator.Generate(source.Source.Templates, source.Source.Enemies, request, random);
            CommonEncounterFrozenRoster frozen = CommonEncounterFrozenRoster.FromGenerated(result, source);
            CommonEncounterGenerator.ValidateFrozen(source.Source.Templates, source.Source.Enemies, request, frozen);
            var item = new DungeonRunFormationEncounterSaveData
            {
                nodeId = node.contentId, scenarioId = node.scenarioId, mode = DungeonRunEncounterMode.Generated,
                datasetContentId = source.ContentId, difficultyContentId = binding.difficultyContentId,
                generationFingerprint = source.GenerationFingerprint, runtimeContentFingerprint = source.RuntimeContentFingerprint,
                templateContentId = frozen.TemplateContentId, firstAxisLength = frozen.FirstAxisLength, secondAxisLength = frozen.SecondAxisLength,
                generatorVersion = frozen.AlgorithmVersion, adapterVersion = EnemyFormationLayout.SourcePlanAdapterVersion,
                tileDifficultyBits = Bits(request.TileDifficulty), enemyMinBits = Bits(request.EnemyVariance.Min), enemyMaxBits = Bits(request.EnemyVariance.Max),
                encounterMinBits = Bits(request.EncounterVariance.Min), encounterMaxBits = Bits(request.EncounterVariance.Max),
                cells = frozen.Cells.Select(id => new DungeonRunFormationCellSaveData { isEmpty = id == null, enemyContentId = id ?? string.Empty }).ToList()
            };
            item.rosterHash = RosterHash(item);
            return item;
        }

        // One synchronous operation observes one resolved source per stable dataset.
        // Never retain this dictionary on the Run: the next read/preflight/restore
        // must resolve current content again. Every binding still checks difficulty,
        // revisions and its own frozen roster; requests/RNG are not memoized.
        private CommonEncounterSourceContract ResolveSource(LevelNode node,
            Dictionary<string, CommonEncounterSourceContract> sources)
        {
            string id = node.commonEncounter.datasetContentId;
            if (!sources.TryGetValue(id, out CommonEncounterSourceContract source))
            {
                source = _commonSources?.Invoke(id);
                if (source == null || source.ContentId != id)
                    throw Invalid("Explicit common encounter dataset cannot be resolved.");
                sources.Add(id, source);
            }
            source.RequireCurrent();
            if (!source.Source.HasDifficulty(node.commonEncounter.difficultyContentId))
                throw Invalid("Explicit common encounter difficulty profile cannot be resolved.");
            return source;
        }

        /// <summary>Validate the whole frozen floor before admitting the sole
        /// v1/v2/v3-to-v4 geometry upgrade. Identity, choices and generated proofs stay
        /// authoritative; migration never reevaluates a curve or consumes RNG.</summary>
        private void RequireMatches(LevelRunProfile profile, DungeonRunFormationFloorSaveData saved,
            Dictionary<string, CommonEncounterSourceContract> sources = null)
        {
            sources ??= new Dictionary<string, CommonEncounterSourceContract>(StringComparer.Ordinal);
            string contract = Contract(profile);
            bool upgradeGeometry = (saved.algorithmVersion == 1 || saved.algorithmVersion == 2 ||
                saved.algorithmVersion == 3) && CommonEnemyFormation.AlgorithmVersion == 4;
            if (contract == null || saved.floorId != profile.contentId ||
                saved.revision != profile.commonFormationRevision ||
                (saved.algorithmVersion != CommonEnemyFormation.AlgorithmVersion && !upgradeGeometry) ||
                saved.contractHash != contract || saved.encounters == null)
                throw Invalid("已冻结阵型的配置或算法版本发生变化。");
            if (saved.legacyAuthored && profile.nodes.Any(node => node.HasCommonEncounter))
                throw Invalid("Legacy floor state cannot invent a generated encounter history.");
            Dictionary<string, LevelNode> expected = Encounters(profile)
                .ToDictionary(node => node.contentId, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DungeonRunFormationEncounterSaveData item in saved.encounters)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.nodeId) || !seen.Add(item.nodeId) ||
                    !expected.TryGetValue(item.nodeId, out LevelNode node) || item.scenarioId != node.scenarioId)
                    throw Invalid("Frozen encounter identity is missing, duplicate or changed.");
                DungeonRunEncounterMode mode = node.HasCommonEncounter ? DungeonRunEncounterMode.Generated :
                    saved.legacyAuthored || profile.commonFormationRevision == 0 ? DungeonRunEncounterMode.LegacyAuthored : DungeonRunEncounterMode.CommonPreset;
                if (item.mode != mode) throw Invalid("Frozen encounter mode differs from its entered contract.");
                if (mode == DungeonRunEncounterMode.Generated) RequireGenerated(node, item, sources);
                else if ((mode == DungeonRunEncounterMode.LegacyAuthored ? item.preset != 0 :
                    !Enum.IsDefined(typeof(CommonFormationPreset), item.preset)) || !LegacyShape(item))
                    throw Invalid("Frozen legacy formation contains invalid preset or invented generated data.");
            }
            if (seen.Count != expected.Count) throw Invalid("Frozen encounter state is incomplete; reroll is forbidden.");
            if (upgradeGeometry) saved.algorithmVersion = CommonEnemyFormation.AlgorithmVersion;
        }

        private void RequireGenerated(LevelNode node, DungeonRunFormationEncounterSaveData item,
            Dictionary<string, CommonEncounterSourceContract> sources)
        {
            CommonEncounterSourceContract source = ResolveSource(node, sources);
            if (item.preset != 0 || item.datasetContentId != source.ContentId ||
                item.difficultyContentId != node.commonEncounter.difficultyContentId ||
                item.tileDifficultyBits != Bits(node.commonEncounter.tileDifficulty) ||
                item.generationFingerprint != source.GenerationFingerprint || item.runtimeContentFingerprint != source.RuntimeContentFingerprint ||
                item.generatorVersion != CommonEncounterGenerator.AlgorithmVersion || item.adapterVersion != EnemyFormationLayout.SourcePlanAdapterVersion ||
                item.rosterHash != RosterHash(item))
                throw Invalid("Frozen species, source content, curve inputs or algorithm revision changed.");
            CommonEncounterFrozenRoster frozen = ReadGenerated(item);
            IReadOnlyList<string> enemyTags = source.Source.EnemyRequestedLeafTags;
            if (source.Source is ICommonEncounterDifficultyEnemyTagSource scoped &&
                (!scoped.TryGetEnemyRequestedLeafTags(item.difficultyContentId, out enemyTags) || enemyTags == null))
                throw Invalid("Frozen encounter difficulty enemy filter metadata is missing.");
            var request = new CommonEncounterRequest(Float(item.tileDifficultyBits),
                new CommonEncounterVariance(Float(item.encounterMinBits), Float(item.encounterMaxBits)),
                new CommonEncounterVariance(Float(item.enemyMinBits), Float(item.enemyMaxBits)),
                source.Source.TemplateTagRequests, enemyTags, source.Source.DefaultTemplateLeafTags);
            CommonEncounterGenerator.ValidateFrozen(source.Source.Templates, source.Source.Enemies, request, frozen);
        }

        private static CommonEncounterFrozenRoster ReadGenerated(DungeonRunFormationEncounterSaveData item)
        {
            if (item.cells == null) throw Invalid("Frozen source matrix is missing.");
            var cells = new List<string>();
            foreach (var cell in item.cells)
            {
                if (cell == null || (cell.isEmpty ? !string.IsNullOrEmpty(cell.enemyContentId) : string.IsNullOrWhiteSpace(cell.enemyContentId)))
                    throw Invalid("Frozen source matrix has an ambiguous empty or missing cell.");
                cells.Add(cell.isEmpty ? null : cell.enemyContentId);
            }
            return new CommonEncounterFrozenRoster(item.templateContentId, item.firstAxisLength, item.secondAxisLength,
                cells, item.generatorVersion, item.generationFingerprint, item.runtimeContentFingerprint);
        }

        private static bool LegacyShape(DungeonRunFormationEncounterSaveData item)
            => string.IsNullOrEmpty(item.datasetContentId) && string.IsNullOrEmpty(item.difficultyContentId) &&
               string.IsNullOrEmpty(item.generationFingerprint) && string.IsNullOrEmpty(item.runtimeContentFingerprint) &&
               string.IsNullOrEmpty(item.templateContentId) && string.IsNullOrEmpty(item.rosterHash) &&
               item.firstAxisLength == 0 && item.secondAxisLength == 0 && item.generatorVersion == 0 && item.adapterVersion == 0 &&
               item.tileDifficultyBits == 0 && item.enemyMinBits == 0 && item.enemyMaxBits == 0 &&
               item.encounterMinBits == 0 && item.encounterMaxBits == 0 && (item.cells == null || item.cells.Count == 0);

        private static void RequireLegacyProfile(LevelRunProfile profile)
        {
            if (profile.nodes != null && profile.nodes.Any(node => node != null && node.HasCommonEncounter))
                throw Invalid("Legacy saves contain no generated species evidence; newly configured generated nodes cannot be invented.");
        }

        private static string RosterHash(DungeonRunFormationEncounterSaveData item)
        {
            if (item.cells == null) throw Invalid("Frozen source matrix is missing.");
            var parts = new List<string> { item.nodeId ?? string.Empty, item.scenarioId ?? string.Empty,
                item.datasetContentId ?? string.Empty, item.difficultyContentId ?? string.Empty,
                item.generationFingerprint ?? string.Empty, item.runtimeContentFingerprint ?? string.Empty,
                item.templateContentId ?? string.Empty };
            foreach (int value in new[] { item.firstAxisLength, item.secondAxisLength, item.generatorVersion, item.adapterVersion,
                item.tileDifficultyBits, item.enemyMinBits, item.enemyMaxBits, item.encounterMinBits, item.encounterMaxBits })
                parts.Add(value.ToString(CultureInfo.InvariantCulture));
            foreach (var cell in item.cells)
            {
                if (cell == null) throw Invalid("Frozen matrix contains a null cell record.");
                parts.Add(cell.isEmpty ? "empty" : "actor"); parts.Add(cell.enemyContentId ?? string.Empty);
            }
            return Digest("frozen-common-encounter-result/v1", parts);
        }

        private static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        private static float Float(int bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        private static string Digest(string domain, List<string> values)
            => BitConverter.ToString(Hash(domain, values.ToArray())).Replace("-", string.Empty).ToLowerInvariant();

        private static string Contract(LevelRunProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.contentId)) throw Invalid("Formation floor identity is missing.");
            bool generated = profile.nodes != null && profile.nodes.Any(node => node != null && node.HasCommonEncounter);
            if (profile.commonFormationRevision == 0 && !generated) return null;
            if (profile.commonFormationRevision != 0 && profile.commonFormationRevision != 1)
                throw Invalid("Unsupported common formation revision.");
            if (profile.nodes == null || profile.nodes.Any(node => node == null)) throw Invalid("Formation nodes are missing or null.");
            LevelNode[] encounters = Encounters(profile).ToArray();
            if (encounters.Length == 0 || encounters.Any(node => string.IsNullOrWhiteSpace(node.contentId) || string.IsNullOrWhiteSpace(node.scenarioId)) ||
                encounters.Select(node => node.contentId).Distinct(StringComparer.Ordinal).Count() != encounters.Length)
                throw Invalid("Frozen encounters require unique nodes and explicit Scenarios.");
            var parts = new List<string> { profile.contentId, profile.commonFormationRevision.ToString(CultureInfo.InvariantCulture) };
            foreach (LevelNode node in profile.nodes)
                if (node.HasCommonEncounter && (node.nodeType != LevelNodeType.Encounter || node.encounterRole != LevelEncounterRole.Ordinary))
                    throw Invalid("Common generation binding requires an explicit ordinary encounter.");
            foreach (LevelNode node in encounters)
            {
                parts.Add(node.contentId); parts.Add(node.scenarioId); parts.Add(((int)node.nodeType).ToString(CultureInfo.InvariantCulture));
                parts.Add(((int)node.encounterRole).ToString(CultureInfo.InvariantCulture));
                if (node.HasCommonEncounter)
                {
                    var binding = node.commonEncounter;
                    if (string.IsNullOrWhiteSpace(binding.datasetContentId) || string.IsNullOrWhiteSpace(binding.difficultyContentId) ||
                        !binding.hasTileDifficulty || float.IsNaN(binding.tileDifficulty) || float.IsInfinity(binding.tileDifficulty))
                        throw Invalid("Generated encounters require explicit dataset, difficulty and finite tile evidence.");
                    parts.Add("generated"); parts.Add(binding.datasetContentId); parts.Add(binding.difficultyContentId);
                    parts.Add(Bits(binding.tileDifficulty).ToString(CultureInfo.InvariantCulture));
                }
                else parts.Add("authored-or-preset");
            }
            return Digest("frozen-encounter-floor-contract/v2", parts);
        }

        private static string LegacyContract(LevelRunProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.contentId))
                throw Invalid("阵型规则缺少楼层身份。");
            if (profile.commonFormationRevision == 0) return null;
            if (profile.commonFormationRevision != 1)
                throw Invalid("不支持的常见阵型规则版本：" + profile.commonFormationRevision);
            if (profile.nodes == null || profile.nodes.Any(node => node == null))
                throw Invalid("阵型规则缺少节点或包含空节点。");
            LevelNode[] encounters = Encounters(profile).ToArray();
            if (encounters.Length == 0 || encounters.Any(node =>
                    string.IsNullOrWhiteSpace(node.contentId) || string.IsNullOrWhiteSpace(node.scenarioId)) ||
                encounters.Select(node => node.contentId).Distinct(StringComparer.Ordinal).Count() != encounters.Length)
                throw Invalid("常见阵型需要唯一身份和明确Scenario的遭遇。");
            var parts = new List<string> { profile.contentId,
                profile.commonFormationRevision.ToString(CultureInfo.InvariantCulture) };
            foreach (LevelNode node in encounters)
            { parts.Add(node.contentId); parts.Add(node.scenarioId); parts.Add(((int)node.nodeType).ToString(CultureInfo.InvariantCulture)); }
            return BitConverter.ToString(Hash("common-formation-contract/v1", parts.ToArray()))
                .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static IEnumerable<LevelNode> Encounters(LevelRunProfile profile)
            => profile.nodes.Where(node => node.nodeType == LevelNodeType.Encounter ||
                    node.nodeType == LevelNodeType.Finale).OrderBy(node => node.contentId, StringComparer.Ordinal);

        private static byte[] Hash(string domain, params string[] values)
        {
            var text = new StringBuilder(domain);
            foreach (string value in values)
                text.Append('|').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
            using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
        }

        private static DungeonRunFormationFloorSaveData Copy(DungeonRunFormationFloorSaveData source)
            => new DungeonRunFormationFloorSaveData
            {
                floorId = source.floorId, revision = source.revision, algorithmVersion = source.algorithmVersion,
                contractHash = source.contractHash, legacyAuthored = source.legacyAuthored,
                encounters = source.encounters == null ? null : source.encounters.Select(CopyEncounter).ToList()
            };

        private static DungeonRunFormationEncounterSaveData CopyEncounter(DungeonRunFormationEncounterSaveData item)
        {
            if (item == null) return null;
            return new DungeonRunFormationEncounterSaveData
            {
                nodeId = item.nodeId, scenarioId = item.scenarioId, preset = item.preset, mode = item.mode,
                datasetContentId = item.datasetContentId, difficultyContentId = item.difficultyContentId,
                generationFingerprint = item.generationFingerprint, runtimeContentFingerprint = item.runtimeContentFingerprint,
                templateContentId = item.templateContentId, rosterHash = item.rosterHash,
                firstAxisLength = item.firstAxisLength, secondAxisLength = item.secondAxisLength,
                generatorVersion = item.generatorVersion, adapterVersion = item.adapterVersion,
                tileDifficultyBits = item.tileDifficultyBits, enemyMinBits = item.enemyMinBits, enemyMaxBits = item.enemyMaxBits,
                encounterMinBits = item.encounterMinBits, encounterMaxBits = item.encounterMaxBits,
                cells = item.cells == null ? null : item.cells.Select(cell => cell == null ? null : new DungeonRunFormationCellSaveData
                { isEmpty = cell.isEmpty, enemyContentId = cell.enemyContentId }).ToList()
            };
        }

        private static InvalidOperationException Invalid(string text) => new InvalidOperationException(text);
    }

    [Serializable]
    internal sealed class DungeonRunFormationSaveData
    {
        public int schemaVersion;
        public List<DungeonRunFormationFloorSaveData> floors = new List<DungeonRunFormationFloorSaveData>();
    }

    [Serializable]
    internal sealed class DungeonRunFormationFloorSaveData
    {
        public string floorId, contractHash;
        public int revision, algorithmVersion;
        public bool legacyAuthored;
        public List<DungeonRunFormationEncounterSaveData> encounters = new List<DungeonRunFormationEncounterSaveData>();
    }

    [Serializable]
    internal sealed class DungeonRunFormationEncounterSaveData
    {
        public string nodeId, scenarioId;
        public int preset;
        public DungeonRunEncounterMode mode;
        public string datasetContentId, difficultyContentId, generationFingerprint, runtimeContentFingerprint, templateContentId, rosterHash;
        public int firstAxisLength, secondAxisLength, generatorVersion, adapterVersion;
        public int tileDifficultyBits, enemyMinBits, enemyMaxBits, encounterMinBits, encounterMaxBits;
        public List<DungeonRunFormationCellSaveData> cells = new List<DungeonRunFormationCellSaveData>();
    }

    [Serializable]
    internal sealed class DungeonRunFormationCellSaveData
    {
        public bool isEmpty;
        public string enemyContentId;
    }
}
