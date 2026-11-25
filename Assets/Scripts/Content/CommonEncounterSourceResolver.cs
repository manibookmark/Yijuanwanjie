using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// Resolves one immutable generation source against the current complete
    /// catalog and same-build behavior cache. It owns no Run state, actors or
    /// random stream, and never evaluates a native curve during resolution.
    /// </summary>
    public sealed class CommonEncounterSourceResolver
    {
        // Explicit adapter revisions, not hashes of Core implementation code.
        public const int RuntimeFactoryMappingVersion = 1;
        public const int SourcePlanAdapterVersion = EnemyFormationLayout.SourcePlanAdapterVersion;
        public const int SourceAlgorithmVersion = CommonEncounterGenerator.AlgorithmVersion;
        private const string ScenarioResourcePath = "Content/Battle";
        private readonly ContentCatalog _catalog;
        private readonly Func<IReadOnlyList<BattleScenarioProfile>> _scenarioReader;

        public CommonEncounterSourceResolver(ContentCatalog catalog,
            Func<IReadOnlyList<BattleScenarioProfile>> scenarioReader = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _scenarioReader = scenarioReader ?? (() => Resources.LoadAll<BattleScenarioProfile>(ScenarioResourcePath));
        }

        public CommonEncounterSourceContract Resolve(string datasetContentId)
        {
            if (!_catalog.TryGetCommonEncounter(datasetContentId,
                    out CommonEncounterContentDefinition definition))
                throw new InvalidOperationException("Unknown common encounter dataset: " + (datasetContentId ?? "<null>"));

            IReadOnlyList<ScenarioInput> scenarios = ReadCurrentScenarios();

            // Catalog DTOs remain mutable for legacy readers. Revalidate the
            // actual inputs, rather than relying on the constructor's old report.
            ContentValidationReport report = ContentValidator.Validate(_catalog.Volumes);
            if (report.HasErrors)
            {
                var details = new StringBuilder();
                foreach (ContentValidationIssue issue in report.Issues)
                    if (issue.Severity == ContentValidationSeverity.Error)
                        details.AppendLine(issue.ToString());
                throw new InvalidOperationException("Current common encounter content is invalid: " + details);
            }

            VolumeContent owner = null;
            CommonEncounterContentData current = null;
            foreach (VolumeContent volume in _catalog.Volumes)
                foreach (CommonEncounterContentData data in volume.commonEncounters ??
                    new List<CommonEncounterContentData>())
                    if (data != null && string.Equals(data.contentId, datasetContentId, StringComparison.Ordinal))
                    {
                        if (current != null)
                            throw new InvalidOperationException("Duplicate common encounter dataset: " + datasetContentId);
                        owner = volume;
                        current = data;
                    }
            if (current == null)
                throw new InvalidOperationException("Compiled common encounter dataset no longer exists: " + datasetContentId);

            IReadOnlyDictionary<string, EnemyData> availableEnemies =
                CommonEncounterContentCompiler.VisibleEnemies(owner, _catalog.Volumes);
            if (!CommonEncounterContentCompiler.TryCompile(current, availableEnemies,
                    out CommonEncounterContentDefinition rechecked, out IReadOnlyList<string> errors))
                throw new InvalidOperationException("Current common encounter dataset cannot compile: " + string.Join("; ", errors));
            if (!string.Equals(definition.Fingerprint, rechecked.Fingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Common encounter generation inputs changed after catalog compilation.");

            string runtimeFingerprint = CaptureRuntimeInput(definition, availableEnemies, scenarios);
            return new CommonEncounterSourceContract(definition, runtimeFingerprint);
        }

        private readonly struct ScenarioInput
        {
            internal readonly string Id;
            internal readonly int HealthOverride;
            internal readonly int ExperienceOverride;
            internal readonly string BossContentId;
            internal readonly BossRank BossRank;

            internal ScenarioInput(BattleScenarioProfile profile)
            {
                Id = profile.scenarioId;
                HealthOverride = profile.enemyHealthOverride;
                ExperienceOverride = profile.enemyExperienceOverride;
                BossContentId = profile.levelBossContentId;
                BossRank = profile.levelBossRank;
            }
        }

        private IReadOnlyList<ScenarioInput> ReadCurrentScenarios()
        {
            IReadOnlyList<BattleScenarioProfile> profiles = _scenarioReader();
            if (profiles == null || profiles.Count == 0)
                throw new InvalidOperationException("A current nonempty battle scenario catalog is required.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inputs = new List<ScenarioInput>(profiles.Count);
            foreach (BattleScenarioProfile profile in profiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.scenarioId))
                    throw new InvalidOperationException("Battle scenario profiles require nonempty stable scenarioIds.");
                if (!ids.Add(profile.scenarioId))
                    throw new InvalidOperationException("Duplicate battle scenarioId: " + profile.scenarioId);
                inputs.Add(new ScenarioInput(profile));
            }
            inputs.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
            return inputs.AsReadOnly();
        }

        private string CaptureRuntimeInput(CommonEncounterContentDefinition definition,
            IReadOnlyDictionary<string, EnemyData> availableEnemies, IReadOnlyList<ScenarioInput> scenarios)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("common-encounter-runtime-input-v2");
                writer.Write(RuntimeFactoryMappingVersion);
                writer.Write(SourcePlanAdapterVersion);
                writer.Write(SourceAlgorithmVersion);
                writer.Write(definition.ContentId);
                writer.Write(definition.Fingerprint);

                // Includes every actor/card and referenced source in every actual
                // volume; volume enumeration order cannot change the revision.
                var volumes = new List<VolumeContent>(_catalog.Volumes);
                volumes.Sort((left, right) => StringComparer.Ordinal.Compare(left.volumeId, right.volumeId));
                writer.Write(volumes.Count);
                foreach (VolumeContent volume in volumes)
                    WriteRuntimeVolumeInput(writer, volume);

                // Every actual Scenario participates, independently of which
                // node uses it. These four inputs can alter factory-created
                // health, experience, armor reset and Boss role. No override is
                // forbidden or applied here; only its current revision is bound.
                writer.Write(scenarios.Count);
                foreach (ScenarioInput scenario in scenarios)
                {
                    writer.Write(scenario.Id);
                    writer.Write(scenario.HealthOverride);
                    writer.Write(scenario.ExperienceOverride);
                    writer.Write(scenario.BossContentId != null);
                    writer.Write(scenario.BossContentId ?? string.Empty);
                    writer.Write((int)scenario.BossRank);
                }

                writer.Write(EnemyBehaviorProfileCatalog.CachedSignature);
                writer.Write(definition.Enemies.Count);
                foreach (CommonEncounterEnemy enemy in definition.Enemies)
                {
                    EnemyData actor = availableEnemies[enemy.ContentId];
                    if (!EnemyBehaviorProfileCatalog.TryResolveWithFingerprint(actor.behaviorProfileId,
                            out EnemyBehaviorDefinition _, out string fingerprint, out string error))
                        throw new InvalidOperationException("Common encounter actor behavior is invalid: " + actor.contentId + ": " + error);
                    writer.Write(actor.contentId);
                    writer.Write(actor.behaviorProfileId);
                    writer.Write(fingerprint);
                }
                writer.Flush();
                using (SHA256 algorithm = SHA256.Create())
                    return BitConverter.ToString(algorithm.ComputeHash(stream.ToArray()))
                        .Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void WriteRuntimeVolumeInput(BinaryWriter writer, VolumeContent volume)
        {
            writer.Write(volume.volumeId);
            string json = JsonUtility.ToJson(volume);
            // Adding an optional DTO field must not revise an existing frozen
            // Run. Remove only inherited overrides from their serialized object;
            // keep the serializer's original bytes for all preceding fields.
            foreach (CommonEncounterContentData data in volume.commonEncounters ??
                new List<CommonEncounterContentData>())
                foreach (CommonEncounterDifficultyData difficulty in data.difficultyProfiles)
                {
                    if (difficulty.enemyRequestedLeafTagContentIds != null) continue;
                    string profileJson = JsonUtility.ToJson(difficulty);
                    string legacyJson = RemoveInheritedEnemyTagField(profileJson);
                    if (string.Equals(profileJson, legacyJson, StringComparison.Ordinal)) continue;
                    int index = json.IndexOf(profileJson, StringComparison.Ordinal);
                    if (index < 0)
                        throw new InvalidOperationException("Serialized common encounter difficulty is absent from its volume.");
                    json = json.Remove(index, profileJson.Length).Insert(index, legacyJson);
                }
            writer.Write(json);
            // JSON may normalize negative zero. Preserve the original binary
            // bits too; skip the new optional field only when it inherits.
            WriteNumericBits(writer, volume, new HashSet<object>());
            // Gem is outside Unity serialization. Absence appends no bytes,
            // preserving the earlier catalog contract; a present empty list still
            // has an explicit domain/version/count. One complete strict checkpoint
            // captures every native identity, atom and ordered pointer/tree field.
            if (volume.gemSources != null)
            {
                writer.Write("gem-source-registry-checkpoints/v1");
                writer.Write(GemSourceRegistryDocument.CurrentFormatVersion);
                writer.Write(volume.gemSources.Count);
                foreach (GemSourceRegistryData source in volume.gemSources)
                {
                    GemSourceRegistryDocument document = GemSourceRegistryDocument.Create(source);
                    writer.Write(document.Definition.ContentId);
                    writer.Write(document.FormatVersion);
                    writer.Write(document.Fingerprint);
                    writer.Write(document.CanonicalJson);
                }
            }
            // Authored side-event timelines belong to the exact Run source plan,
            // not this common-enemy contract. Even an inactive Candidate addition
            // must not invalidate an already frozen common encounter. Both JSON
            // and the numeric companion omit this NonSerialized extension.
        }

        private static string RemoveInheritedEnemyTagField(string profileJson)
        {
            const string name = nameof(CommonEncounterDifficultyData.enemyRequestedLeafTagContentIds);
            if (!JsonObjectShape.TryGetField(profileJson, name, out string raw)) return profileJson;
            if (raw != "null" && raw != "[]")
                throw new InvalidOperationException("Inherited enemy tags serialized as a nonempty override.");
            string member = "\"" + name + "\":" + raw;
            int start = profileJson.IndexOf(member, StringComparison.Ordinal);
            if (start < 1)
                throw new InvalidOperationException("Cannot preserve legacy common encounter difficulty JSON.");
            int length = member.Length;
            if (start + length < profileJson.Length && profileJson[start + length] == ',') length++;
            else if (profileJson[start - 1] == ',') { start--; length++; }
            return profileJson.Remove(start, length);
        }

        // Private companion to full DTO JSON. Public fields are sorted by name,
        // lists retain author order, and shared acyclic objects retain their values.
        // Content DTOs have no Unity object references or runtime instance identity.
        private static void WriteNumericBits(BinaryWriter writer, object value, HashSet<object> visiting)
        {
            if (value == null) return;
            if (value is float single)
            {
                writer.Write(BitConverter.ToInt32(BitConverter.GetBytes(single), 0));
                return;
            }
            if (value is double precise)
            {
                writer.Write(BitConverter.DoubleToInt64Bits(precise));
                return;
            }
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string) return;
            if (value is UnityEngine.Object)
                throw new InvalidOperationException("Runtime catalog DTOs cannot contain Unity object references.");
            if (!visiting.Add(value))
                throw new InvalidOperationException("Runtime catalog DTOs cannot contain cycles.");
            try
            {
                if (value is IEnumerable sequence)
                {
                    foreach (object item in sequence) WriteNumericBits(writer, item, visiting);
                    return;
                }
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                Array.Sort(fields, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
                foreach (FieldInfo field in fields)
                {
                    // The separately framed strict Gem checkpoint owns all fields
                    // of this nonserialized extension, including nonnumeric atoms.
                    if (value is VolumeContent && (field.Name == nameof(VolumeContent.gemSources) || field.Name == nameof(VolumeContent.sideEventTimelines)))
                        continue;
                    if (value is CommonEncounterDifficultyData difficulty &&
                        difficulty.enemyRequestedLeafTagContentIds == null &&
                        field.Name == nameof(CommonEncounterDifficultyData.enemyRequestedLeafTagContentIds))
                        continue;
                    writer.Write(field.Name);
                    WriteNumericBits(writer, field.GetValue(value), visiting);
                }
            }
            finally { visiting.Remove(value); }
        }
    }
}
