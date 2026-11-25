using System;
using WanjieScroll.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Content
{
    [Serializable]
    internal sealed class ContentPackageManifest
    {
        public int schemaVersion;
        public int contentSchemaVersion;
        public string contentVersion;
        public List<ContentPackageEntry> entries = new List<ContentPackageEntry>();
    }

    [Serializable]
    internal sealed class ContentPackageEntry
    {
        public string sourceFileName;
        public string resourcePath;
        public string volumeId;
        public string sha256;
    }

    /// <summary>
    /// Owns the generated content package contract. Runtime callers only ask for
    /// volumes; manifest shape, resource paths, hashing and structural checks stay
    /// hidden in this module.
    /// Scale review: the loader owns package admission only. Extension codecs own
    /// their closed field/type/bit contracts; no Gem selection or Run state enters here.
    /// </summary>
    internal static class ContentPackageLoader
    {
        internal const int CurrentPackageSchemaVersion = 1;
        internal const string ManifestResourcePath =
            "Content/Generated/content-manifest";
        internal const string GeneratedVolumeResourcePrefix =
            "Content/Generated/cards/";

        private static readonly string[] RequiredVolumeFields =
        {
            "schemaVersion",
            "volumeId",
            "crawlers",
            "cards",
            "enemies",
            "relics",
            "evolutions",
            "dungeons"
        };

        private static readonly string[] RequiredVolumeArrayFields =
        {
            "crawlers",
            "cards",
            "enemies",
            "relics",
            "evolutions",
            "dungeons"
        };

        internal static List<VolumeContent> LoadFromResources()
        {
            TextAsset manifestAsset =
                Resources.Load<TextAsset>(ManifestResourcePath);
            if (manifestAsset == null)
            {
                throw new InvalidOperationException(
                    $"运行时内容包清单不存在：Resources/{ManifestResourcePath}.json。" +
                    "请先执行“万界长卷/内容/生成运行时内容包”。");
            }

            return LoadPackage(
                manifestAsset.text,
                resourcePath =>
                {
                    TextAsset asset = Resources.Load<TextAsset>(resourcePath);
                    return asset == null ? null : asset.text;
                });
        }

        internal static List<VolumeContent> LoadPackage(
            string manifestJson,
            Func<string, string> readResourceText)
        {
            if (readResourceText == null)
                throw new ArgumentNullException(nameof(readResourceText));

            ContentPackageManifest manifest = ParseManifest(manifestJson);
            var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var volumeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var volumes = new List<VolumeContent>(manifest.entries.Count);

            string previousSourceFile = null;
            foreach (ContentPackageEntry entry in manifest.entries)
            {
                ValidateEntry(entry);
                if (previousSourceFile != null &&
                    StringComparer.Ordinal.Compare(
                        previousSourceFile,
                        entry.sourceFileName) >= 0)
                {
                    throw new InvalidOperationException(
                        "运行时内容包清单 entries 必须按 sourceFileName 严格升序排列。");
                }
                previousSourceFile = entry.sourceFileName;

                if (!sourceFiles.Add(entry.sourceFileName))
                    throw new InvalidOperationException(
                        $"运行时内容包清单包含重复源文件：{entry.sourceFileName}。");
                if (!resourcePaths.Add(entry.resourcePath))
                    throw new InvalidOperationException(
                        $"运行时内容包清单包含重复资源路径：{entry.resourcePath}。");
                if (!volumeIds.Add(entry.volumeId))
                    throw new InvalidOperationException(
                        $"运行时内容包清单包含重复 volumeId：{entry.volumeId}。");

                string volumeJson = readResourceText(entry.resourcePath);
                if (volumeJson == null)
                {
                    throw new InvalidOperationException(
                        $"运行时内容包缺少资源：Resources/{entry.resourcePath}.json " +
                        $"（来源 {entry.sourceFileName}）。");
                }

                string actualHash = ComputeSha256(volumeJson);
                if (!string.Equals(
                        actualHash,
                        entry.sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"运行时内容资源哈希不一致：{entry.sourceFileName}；" +
                        $"清单={entry.sha256}，实际={actualHash}。");
                }

                VolumeContent volume = ParseVolume(
                    volumeJson,
                    entry.sourceFileName);
                if (!string.Equals(
                        volume.volumeId,
                        entry.volumeId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"内容包 volumeId 不一致：{entry.sourceFileName}；" +
                        $"清单={entry.volumeId}，JSON={volume.volumeId}。");
                }

                volumes.Add(volume);
            }

            string actualVersion = ComputeContentVersion(manifest.entries);
            if (!string.Equals(
                    actualVersion,
                    manifest.contentVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"运行时内容包 contentVersion 不一致：" +
                    $"清单={manifest.contentVersion}，条目={actualVersion}。");
            }

            return volumes;
        }

        internal static ContentPackageManifest ParseManifest(string manifestJson)
        {
            if (string.IsNullOrWhiteSpace(manifestJson))
                throw new InvalidOperationException("运行时内容包清单为空。");

            RequireTopLevelField(manifestJson, "schemaVersion", "内容包清单");
            RequireTopLevelField(manifestJson, "contentSchemaVersion", "内容包清单");
            RequireTopLevelField(manifestJson, "contentVersion", "内容包清单");
            RequireTopLevelField(manifestJson, "entries", "内容包清单");

            ContentPackageManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<ContentPackageManifest>(manifestJson);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"无法解析运行时内容包清单：{exception.Message}",
                    exception);
            }

            if (manifest == null)
                throw new InvalidOperationException("无法解析运行时内容包清单。");
            if (manifest.schemaVersion != CurrentPackageSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"内容包 schemaVersion={manifest.schemaVersion}，" +
                    $"当前要求 {CurrentPackageSchemaVersion}。");
            }
            if (manifest.contentSchemaVersion != ContentCatalog.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"内容包 contentSchemaVersion={manifest.contentSchemaVersion}，" +
                    $"当前要求 {ContentCatalog.CurrentSchemaVersion}。");
            }
            if (string.IsNullOrWhiteSpace(manifest.contentVersion))
                throw new InvalidOperationException("内容包 contentVersion 不能为空。");
            if (manifest.entries == null || manifest.entries.Count == 0)
                throw new InvalidOperationException("内容包 entries 不能为空。");

            return manifest;
        }

        internal static VolumeContent ParseVolume(string json, string sourceLabel)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException($"内容文件为空：{sourceLabel}。");

            foreach (string fieldName in RequiredVolumeFields)
                RequireTopLevelField(json, fieldName, sourceLabel);
            foreach (string fieldName in RequiredVolumeArrayFields)
            {
                if (IsTopLevelFieldNull(json, fieldName))
                {
                    throw new InvalidOperationException(
                        $"{sourceLabel} 的顶层数组 {fieldName} 不能为 null。");
                }
            }

            bool hasCommonEncounters = TryFindTopLevelFieldValue(
                json, "commonEncounters", out int commonEncountersStart);
            if (hasCommonEncounters && (commonEncountersStart >= json.Length ||
                    json[commonEncountersStart] != '['))
                throw new InvalidOperationException(
                    $"{sourceLabel} 的可选顶层字段 commonEncounters 必须为数组，不能为 null 或其它类型。");

            bool hasSideEventSources = SideEventSourceWireCodec.ValidateOptionalPresence(json, sourceLabel);
            if (hasSideEventSources && (!TryFindTopLevelFieldValue(
                    json, "sideEventSources", out int sideEventSourcesStart) || sideEventSourcesStart >= json.Length ||
                    json[sideEventSourcesStart] != '['))
                throw new InvalidOperationException(
                    $"{sourceLabel} 的可选顶层字段 sideEventSources 必须为数组，不能为 null 或其它类型。");

            VolumeContent volume;
            try
            {
                volume = JsonUtility.FromJson<VolumeContent>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"无法解析内容文件 {sourceLabel}：{exception.Message}",
                    exception);
            }

            if (volume == null)
                throw new InvalidOperationException($"无法解析内容文件：{sourceLabel}。");
            if (volume.schemaVersion != ContentCatalog.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"内容文件 {sourceLabel} 的 schemaVersion={volume.schemaVersion}，" +
                    $"当前要求 {ContentCatalog.CurrentSchemaVersion}。");
            }
            if (string.IsNullOrWhiteSpace(volume.volumeId))
                throw new InvalidOperationException(
                    $"内容文件缺少非空 volumeId：{sourceLabel}。");

            RequireCollection(volume.crawlers, "crawlers", sourceLabel);
            RequireCollection(volume.cards, "cards", sourceLabel);
            RequireCollection(volume.enemies, "enemies", sourceLabel);
            RequireCollection(volume.relics, "relics", sourceLabel);
            RequireCollection(volume.evolutions, "evolutions", sourceLabel);
            RequireCollection(volume.dungeons, "dungeons", sourceLabel);
            if (hasCommonEncounters)
            {
                RequireCollection(volume.commonEncounters, "commonEncounters", sourceLabel);
                CommonEncounterJsonCodec.RestoreAuthorFloatBits(json, volume, sourceLabel);
            }
            if (hasSideEventSources)
            {
                RequireCollection(volume.sideEventSources, "sideEventSources", sourceLabel);
                SideEventSourceWireCodec.ValidateWireAndRestoreBits(json, volume.sideEventSources, sourceLabel);
            }
            else
                // JsonUtility synthesizes missing reference fields. Absence was
                // checked from the actual canonical wire, not DTO defaults.
                volume.sideEventSources = null;
            // This recursive author extension is deliberately outside JsonUtility.
            // The closed wire reader creates every DTO, preserves primitive bits and
            // distinguishes actual absence from an explicit collection.
            volume.gemSources = SideEventSourceWireCodec.ReadGemSourcesFromVolume(json, sourceLabel);
            volume.sideEventTimelines = DungeonSideEventTimelineContentCompiler.ReadFromVolume(json, sourceLabel);
            return volume;
        }

        internal static string NormalizeTextForHash(string text)
        {
            if (text == null)
                return string.Empty;

            string normalized = text.Length > 0 && text[0] == '\ufeff'
                ? text.Substring(1)
                : text;
            return normalized.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        internal static string ComputeSha256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(NormalizeTextForHash(text));
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        internal static string ComputeContentVersion(
            IEnumerable<ContentPackageEntry> entries)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));

            var payload = new StringBuilder();
            foreach (ContentPackageEntry entry in entries.OrderBy(
                         item => item.sourceFileName,
                         StringComparer.Ordinal))
            {
                payload.Append(entry.sourceFileName).Append('\n')
                    .Append(entry.resourcePath).Append('\n')
                    .Append(entry.volumeId).Append('\n')
                    .Append(entry.sha256?.ToLowerInvariant()).Append('\n');
            }

            return ComputeSha256(payload.ToString());
        }

        private static void ValidateEntry(ContentPackageEntry entry)
        {
            if (entry == null)
                throw new InvalidOperationException("内容包 entries 不能包含 null。");
            if (string.IsNullOrWhiteSpace(entry.sourceFileName) ||
                !entry.sourceFileName.EndsWith(".json", StringComparison.Ordinal) ||
                entry.sourceFileName.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
                !string.Equals(
                    Path.GetFileName(entry.sourceFileName),
                    entry.sourceFileName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"内容包 sourceFileName 非法：{entry.sourceFileName ?? "<null>"}。");
            }

            string expectedResourcePath = GeneratedVolumeResourcePrefix +
                Path.GetFileNameWithoutExtension(entry.sourceFileName);
            if (!string.Equals(
                    entry.resourcePath,
                    expectedResourcePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"内容包 resourcePath 非法：{entry.resourcePath ?? "<null>"}；" +
                    $"期望 {expectedResourcePath}。");
            }
            if (string.IsNullOrWhiteSpace(entry.volumeId))
                throw new InvalidOperationException(
                    $"内容包条目 {entry.sourceFileName} 缺少 volumeId。");
            if (!IsSha256(entry.sha256))
                throw new InvalidOperationException(
                    $"内容包条目 {entry.sourceFileName} 的 sha256 非法：" +
                    $"{entry.sha256 ?? "<null>"}。");
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                return false;
            foreach (char character in value)
            {
                bool isDigit = character >= '0' && character <= '9';
                bool isLowerHex = character >= 'a' && character <= 'f';
                bool isUpperHex = character >= 'A' && character <= 'F';
                if (!isDigit && !isLowerHex && !isUpperHex)
                    return false;
            }
            return true;
        }

        private static void RequireTopLevelField(
            string json,
            string fieldName,
            string sourceLabel)
        {
            if (!HasTopLevelField(json, fieldName))
            {
                throw new InvalidOperationException(
                    $"{sourceLabel} 缺少顶层字段 {fieldName}。");
            }
        }

        private static void RequireCollection<T>(
            IList<T> items,
            string fieldName,
            string sourceLabel)
            where T : class
        {
            if (items == null)
            {
                throw new InvalidOperationException(
                    $"{sourceLabel} 的顶层数组 {fieldName} 不能为 null。");
            }

            for (int index = 0; index < items.Count; index++)
            {
                if (items[index] == null)
                {
                    throw new InvalidOperationException(
                        $"{sourceLabel}.{fieldName}[{index}] 不能为 null。");
                }
            }
        }

        private static bool HasTopLevelField(string json, string fieldName)
        {
            return TryFindTopLevelFieldValue(
                json,
                fieldName,
                out _);
        }

        private static bool IsTopLevelFieldNull(string json, string fieldName)
        {
            if (!TryFindTopLevelFieldValue(json, fieldName, out int valueStart))
                return false;
            return valueStart + 4 <= json.Length &&
                string.CompareOrdinal(json, valueStart, "null", 0, 4) == 0;
        }

        private static bool TryFindTopLevelFieldValue(
            string json,
            string fieldName,
            out int valueStart)
        {
            valueStart = -1;
            int depth = 0;
            for (int index = 0; index < json.Length; index++)
            {
                char character = json[index];
                if (character == '{' || character == '[')
                {
                    depth++;
                    continue;
                }
                if (character == '}' || character == ']')
                {
                    depth--;
                    continue;
                }
                if (character != '"')
                    continue;

                int stringStart = index + 1;
                bool escaped = false;
                index++;
                while (index < json.Length)
                {
                    char valueCharacter = json[index];
                    if (valueCharacter == '\\')
                    {
                        escaped = true;
                        index += 2;
                        continue;
                    }
                    if (valueCharacter == '"')
                        break;
                    index++;
                }

                int valueLength = index - stringStart;
                int next = index + 1;
                while (next < json.Length && char.IsWhiteSpace(json[next]))
                    next++;
                if (depth == 1 && !escaped && next < json.Length &&
                    json[next] == ':' && valueLength == fieldName.Length &&
                    string.CompareOrdinal(
                        json,
                        stringStart,
                        fieldName,
                        0,
                        fieldName.Length) == 0)
                {
                    valueStart = next + 1;
                    while (valueStart < json.Length &&
                        char.IsWhiteSpace(json[valueStart]))
                    {
                        valueStart++;
                    }
                    return true;
                }
            }

            return false;
        }
    }
}
