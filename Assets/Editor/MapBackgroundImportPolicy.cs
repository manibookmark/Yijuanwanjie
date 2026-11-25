using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WanjieScroll.Effects.Map;
using WanjieScroll.Core;

namespace WanjieScroll.EditorTools
{
    /// <summary>
    /// Editor/build gate for the production map and battle background contract.
    /// The JSON manifest owns the list of promoted assets; AssetDatabase owns
    /// the authoritative interpretation of opaque GUID references in Profiles.
    /// </summary>
    public static class MapBackgroundImportPolicy
    {
        public const string ManifestPath = "tools/assets/map-art.manifest.json";

        [Serializable]
        private sealed class Manifest
        {
            public int schemaVersion;
            public BackgroundContract backgroundContract;
            public BackgroundEntry[] productionBackgrounds;
        }

        [Serializable]
        private sealed class BackgroundContract
        {
            public int width;
            public int height;
            public int pixelsPerUnit;
            public int maxTextureSize;
            public bool mipmapEnabled;
            public int filterMode;
            public int wrapMode;
            public int npotScale;
            public int textureCompression;
            public bool crunchedCompression;
        }

        [Serializable]
        private sealed class BackgroundEntry
        {
            public string id;
            public string profile;
            public string map;
            public string mapAssetDatabaseGuid;
            public string battle;
            public string battleAssetDatabaseGuid;
            public BattleImportOverride battleImportOverride;
        }

        // The only admitted exception family: retain source dimensions on the
        // desktop battle background. It cannot change the paired map or bypass
        // PNG, GUID, Sprite, sampling or remaining global import checks.
        [Serializable]
        private sealed class BattleImportOverride
        {
            public int maxTextureSize;
            public string standaloneFormat;
            public int standaloneTextureCompression;
            public int importedWidth;
            public int importedHeight;
        }

        private static readonly ExactJsonWire OverrideWire = new ExactJsonWire(typeof(BattleImportOverride));
        private static readonly HashSet<string> EntryFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "id", "profile", "map", "mapAssetDatabaseGuid", "battle", "battleAssetDatabaseGuid",
            "semanticContract", "mapCameraContract", "battleCameraContract", "pairAuditPath",
            "cameraReferencePath", "cameraReferenceSha256", "pairReviewResult", "battleImportOverride"
        };

        /// <summary>Pure manifest/override admission used before AssetDatabase
        /// validation. No files, editor objects or rendering are accessed here.</summary>
        public static List<string> CollectConfigurationErrors(string json)
        {
            var errors = new List<string>();
            Manifest manifest;
            IReadOnlyList<string> rawEntries;
            try
            {
                manifest = ReadManifest(json);
                if (manifest == null || manifest.schemaVersion != 1 || manifest.backgroundContract == null ||
                    manifest.productionBackgrounds == null ||
                    !JsonObjectShape.TryGetField(json, "backgroundContract", out _) ||
                    !JsonObjectShape.TryGetField(json, "productionBackgrounds", out string rawArray) ||
                    !JsonObjectShape.TryGetArrayItems(rawArray, out rawEntries) ||
                    rawEntries.Count != manifest.productionBackgrounds.Length)
                    throw new ArgumentException("Manifest requires schemaVersion 1 and unique backgroundContract/productionBackgrounds fields.");
                ObjectKeys(json); // Reject duplicate root fields too.
            }
            catch (Exception exception)
            {
                errors.Add("Background configuration JSON is invalid: " + exception.Message);
                return errors;
            }
            BackgroundContract contract = manifest.backgroundContract;
            if (contract.width <= 0 || contract.height <= 0 || contract.pixelsPerUnit <= 0 ||
                contract.maxTextureSize <= 0 || (contract.maxTextureSize & (contract.maxTextureSize - 1)) != 0)
                errors.Add("Background source dimensions, PPU and default texture limit must be positive and valid.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var overriddenAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < rawEntries.Count; index++)
            {
                BackgroundEntry entry = manifest.productionBackgrounds[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) ||
                    string.IsNullOrWhiteSpace(entry.profile) || string.IsNullOrWhiteSpace(entry.map) ||
                    string.IsNullOrWhiteSpace(entry.battle))
                {
                    errors.Add("Background entry requires id/profile/map/battle.");
                    continue;
                }
                if (!ids.Add(entry.id)) errors.Add("Duplicate background entry ID: " + entry.id);
                try
                {
                    List<string> keys = ObjectKeys(rawEntries[index]);
                    foreach (string key in keys)
                        if (!EntryFields.Contains(key))
                            errors.Add("Unknown background entry field: " + entry.id + "." + key);
                    if (!keys.Contains("battleImportOverride")) continue;
                    if (!JsonObjectShape.TryGetField(rawEntries[index], "battleImportOverride", out string rawOverride))
                        throw new ArgumentException("battleImportOverride must be unique.");
                    // ExactJsonWire rejects missing/unknown/duplicate fields,
                    // nested values, numeric coercion and escaped field names.
                    BattleImportOverride value = OverrideWire.Read<BattleImportOverride>(rawOverride);
                    bool nativeBudget = value != null && value.maxTextureSize == 4096 &&
                        value.importedWidth == contract.width && value.importedHeight == contract.height;
                    bool closeupBudget = value != null && value.maxTextureSize == 8192 &&
                        value.importedWidth == (long)contract.width * 3 &&
                        value.importedHeight == (long)contract.height * 3;
                    if (value == null || (!nativeBudget && !closeupBudget) || value.maxTextureSize <= contract.maxTextureSize ||
                        value.standaloneFormat != "BC7" ||
                        value.standaloneTextureCompression != (int)TextureImporterCompression.CompressedHQ ||
                        value.importedWidth > value.maxTextureSize || value.importedHeight > value.maxTextureSize)
                        throw new ArgumentException("battleImportOverride requires native/max4096 or three-times-source/max8192 dimensions with Standalone BC7/HQ.");
                    if (!overriddenAssets.Add(entry.battle))
                        errors.Add("Duplicate battle import override for asset: " + entry.battle);
                }
                catch (Exception exception)
                {
                    errors.Add("Invalid background entry/override " + entry.id + ": " + exception.Message);
                }
            }
            return errors;
        }

        // Unity's serializer may materialize an absent nested serializable
        // class. Optional import settings follow JSON field presence, never
        // the presence of that synthesized zero-valued object.
        private static Manifest ReadManifest(string json)
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(json);
            if (manifest?.productionBackgrounds == null) return manifest;
            if (!JsonObjectShape.TryGetField(json, "productionBackgrounds", out string rawArray) ||
                !JsonObjectShape.TryGetArrayItems(rawArray, out IReadOnlyList<string> entries) ||
                entries.Count != manifest.productionBackgrounds.Length)
                throw new ArgumentException("Background entries could not be decoded.");
            for (int i = 0; i < entries.Count; i++)
            {
                BackgroundEntry entry = manifest.productionBackgrounds[i];
                if (entry == null) continue;
                entry.battleImportOverride = JsonObjectShape.TryGetField(
                    entries[i], "battleImportOverride", out string rawOverride)
                    ? OverrideWire.Read<BattleImportOverride>(rawOverride) : null;
            }
            return manifest;
        }

        /// <summary>Authoring tools resolve the same admitted battle limit as
        /// the actual asset gate; no asset loading or rendering is involved.</summary>
        public static int GetEffectiveBattleTextureLimit(string json, string entryId)
        {
            List<string> errors = CollectConfigurationErrors(json);
            if (errors.Count > 0) throw new ArgumentException(string.Join(";", errors));
            Manifest manifest = ReadManifest(json);
            foreach (BackgroundEntry entry in manifest.productionBackgrounds)
                if (string.Equals(entry.id, entryId, StringComparison.Ordinal))
                    return entry.battleImportOverride?.maxTextureSize ?? manifest.backgroundContract.maxTextureSize;
            throw new ArgumentException("Unknown background entry: " + entryId);
        }

        private static List<string> ObjectKeys(string json)
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int depth = 0;
            for (int index = 0; index < json.Length; index++)
            {
                char current = json[index];
                if (current == '{' || current == '[') { depth++; continue; }
                if (current == '}' || current == ']') { depth--; continue; }
                if (current != '"') continue;
                int start = ++index;
                bool escaped = false;
                while (index < json.Length && json[index] != '"')
                {
                    if (json[index] == '\\') { escaped = true; index++; }
                    index++;
                }
                if (index >= json.Length) throw new ArgumentException("Unterminated JSON string.");
                int next = index + 1;
                while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                if (depth != 1 || next >= json.Length || json[next] != ':') continue;
                if (escaped) throw new ArgumentException("Escaped configuration field names are not supported.");
                string key = json.Substring(start, index - start);
                if (!seen.Add(key)) throw new ArgumentException("Duplicate configuration field: " + key);
                keys.Add(key);
            }
            return keys;
        }

        public static List<string> CollectErrors()
        {
            var errors = new List<string>();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string manifestAbsolutePath = Path.Combine(
                projectRoot,
                ManifestPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(manifestAbsolutePath))
            {
                errors.Add($"缺少地图底图清单：{ManifestPath}");
                return errors;
            }

            Manifest manifest;
            try
            {
                string json = File.ReadAllText(manifestAbsolutePath);
                errors.AddRange(CollectConfigurationErrors(json));
                if (errors.Count > 0) return errors;
                manifest = ReadManifest(json);
            }
            catch (Exception exception)
            {
                errors.Add($"地图底图清单解析失败：{exception.Message}");
                return errors;
            }

            if (manifest == null || manifest.backgroundContract == null ||
                manifest.productionBackgrounds == null)
            {
                errors.Add("地图底图清单缺少 backgroundContract 或 productionBackgrounds。");
                return errors;
            }

            foreach (BackgroundEntry entry in manifest.productionBackgrounds)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                {
                    errors.Add("地图底图清单存在空条目。");
                    continue;
                }

                MapPreviewProfile profile =
                    AssetDatabase.LoadAssetAtPath<MapPreviewProfile>(entry.profile);
                if (profile == null)
                {
                    errors.Add($"生产 MapPreviewProfile 无法由 AssetDatabase 加载：{entry.profile}");
                    continue;
                }

                ValidateProfileReference(
                    profile.background,
                    entry.map,
                    $"{entry.id}.map",
                    errors);
                ValidateProfileReference(
                    profile.battleBackground,
                    entry.battle,
                    $"{entry.id}.battle",
                    errors);
                ValidateAssetGuid(entry.map, entry.mapAssetDatabaseGuid, $"{entry.id}.map", errors);
                ValidateAssetGuid(entry.battle, entry.battleAssetDatabaseGuid, $"{entry.id}.battle", errors);
                ValidateTexture(
                    entry.map,
                    manifest.backgroundContract,
                    errors);
                ValidateTexture(
                    entry.battle,
                    manifest.backgroundContract,
                    errors,
                    entry.battleImportOverride);
            }

            return errors;
        }

        private static void ValidateAssetGuid(
            string assetPath,
            string expectedGuid,
            string label,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(expectedGuid))
            {
                errors.Add($"生产背景清单缺少 AssetDatabase GUID：{label}，路径 {assetPath}");
                return;
            }

            string actualGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (!string.Equals(actualGuid, expectedGuid, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"生产背景 GUID 不一致：{label}，路径 {assetPath}，" +
                    $"期望 {expectedGuid}，实际 {actualGuid}");
            }
        }

        public static void ValidateProductionBackgrounds()
        {
            List<string> errors = CollectErrors();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            Debug.Log("[MapBackgroundImportPolicy] 生产地图与战斗底图规格通过。");
        }

        private static void ValidateProfileReference(
            Sprite sprite,
            string expectedPath,
            string label,
            List<string> errors)
        {
            if (sprite == null)
            {
                errors.Add($"生产 Profile 缺少底图引用：{label}，期望 {expectedPath}");
                return;
            }

            string actualPath = AssetDatabase.GetAssetPath(sprite);
            if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"生产 Profile 底图引用不一致：{label}，期望 {expectedPath}，实际 {actualPath}");
            }
        }

        private static void ValidateTexture(
            string assetPath,
            BackgroundContract contract,
            List<string> errors,
            BattleImportOverride battleOverride = null)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                errors.Add("地图底图清单存在空资源路径。");
                return;
            }

            string absolutePath = ToAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
            {
                errors.Add($"生产底图文件不存在：{assetPath}");
                return;
            }

            try
            {
                Vector2Int dimensions = ReadPngDimensions(absolutePath);
                int expectedWidth = battleOverride?.importedWidth ?? contract.width;
                int expectedHeight = battleOverride?.importedHeight ?? contract.height;
                if (dimensions.x != expectedWidth || dimensions.y != expectedHeight)
                {
                    errors.Add(
                        $"生产底图 PNG 尺寸不符合规格：{assetPath}，" +
                        $"期望 {expectedWidth}x{expectedHeight}，" +
                        $"实际 {dimensions.x}x{dimensions.y}");
                }
            }
            catch (Exception exception)
            {
                errors.Add($"生产底图 PNG 无法读取：{assetPath}；{exception.Message}");
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                errors.Add($"生产底图未被 TextureImporter 接管：{assetPath}");
                return;
            }
            if (importer.textureType != TextureImporterType.Sprite)
                errors.Add($"生产底图必须是 Sprite：{assetPath}");
            if (importer.spriteImportMode != SpriteImportMode.Single)
                errors.Add($"生产底图必须是 Single Sprite：{assetPath}");
            if (Mathf.Abs(importer.spritePixelsPerUnit - contract.pixelsPerUnit) > 0.01f)
                errors.Add($"生产底图 PPU 不符合规格：{assetPath}");
            if (importer.maxTextureSize != (battleOverride?.maxTextureSize ?? contract.maxTextureSize))
                errors.Add($"生产底图 Max Texture Size 不符合规格：{assetPath}");
            if (importer.mipmapEnabled != contract.mipmapEnabled)
                errors.Add($"生产底图 Mipmap 设置不符合规格：{assetPath}");
            if ((int)importer.filterMode != contract.filterMode)
                errors.Add($"生产底图 Filter 设置不符合规格：{assetPath}");
            if ((int)importer.wrapModeU != contract.wrapMode ||
                (int)importer.wrapModeV != contract.wrapMode ||
                (int)importer.wrapModeW != contract.wrapMode)
                errors.Add($"生产底图 Wrap 设置不符合规格：{assetPath}");
            if ((int)importer.npotScale != contract.npotScale)
                errors.Add($"生产底图 NPOT 设置不符合规格：{assetPath}");
            if ((int)importer.textureCompression != contract.textureCompression)
                errors.Add($"生产底图压缩模式不符合规格：{assetPath}");
            if (importer.crunchedCompression != contract.crunchedCompression)
                errors.Add($"生产底图 Crunch 设置不符合规格：{assetPath}");
            if (battleOverride == null) return;
            TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings("Standalone");
            if (!platform.overridden || platform.maxTextureSize != battleOverride.maxTextureSize ||
                platform.format != TextureImporterFormat.BC7 ||
                (int)platform.textureCompression != battleOverride.standaloneTextureCompression ||
                platform.crunchedCompression)
                errors.Add($"生产底图 Standalone 原生导入设置不符合逐资产合同：{assetPath}");
            if (BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget) == BuildTargetGroup.Standalone)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture == null || texture.width != battleOverride.importedWidth ||
                    texture.height != battleOverride.importedHeight || texture.format != TextureFormat.BC7)
                    errors.Add($"生产底图实际原生尺寸或BC7格式不符合逐资产合同：{assetPath}");
            }
        }

        private static Vector2Int ReadPngDimensions(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 ||
                bytes[2] != 78 || bytes[3] != 71 || bytes[4] != 13 ||
                bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10 ||
                bytes[12] != (byte)'I' || bytes[13] != (byte)'H' ||
                bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
                throw new InvalidDataException("PNG signature or IHDR is invalid.");

            return new Vector2Int(
                ReadBigEndianInt(bytes, 16),
                ReadBigEndianInt(bytes, 20));
        }

        private static int ReadBigEndianInt(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) |
                (bytes[offset + 1] << 16) |
                (bytes[offset + 2] << 8) |
                bytes[offset + 3];
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
