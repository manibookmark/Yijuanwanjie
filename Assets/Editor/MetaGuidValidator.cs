using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WanjieScroll.EditorTools
{
    /// <summary>
    /// 使用 AssetDatabase 对 Assets 目录中的资源身份做权威往返校验。
    /// 此校验器不刷新、不保存也不重写资源，调用方应先等待编辑器完成导入。
    /// </summary>
    public static class MetaGuidValidator
    {
        private const string MenuPath = "万界长卷/工具/校验 .meta GUID";
        private const string ZeroGuid = "00000000000000000000000000000000";

        public static void ValidateFromMenu()
        {
            ValidateOrThrow();
            WriteEvidence();
        }

        /// <summary>
        /// 供团结编辑器 -executeMethod 调用；失败时抛异常，使批处理返回非零状态。
        /// </summary>
        public static void ValidateForBatch()
        {
            ValidateOrThrow();
            WriteEvidence();
        }

        // A newly imported Tuanjie script can receive an engine-owned opaque
        // GUID. Preserve that identity and prove it through the actual database,
        // never through a guessed GUID format or a parsed log message.
        private static void WriteEvidence()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            var evidence = new MetaEvidence { engineVersion = Application.unityVersion };
            foreach (string metaPath in Directory.EnumerateFiles(Application.dataPath, "*.meta", SearchOption.AllDirectories))
            {
                string assetPath = NormalizePath(metaPath.Substring(root.Length + 1));
                assetPath = assetPath.Substring(0, assetPath.Length - 5);
                if (IsIgnoredAssetPath(assetPath)) continue;
                string guid = AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets);
                if (string.IsNullOrEmpty(guid)) continue;
                string roundTrip = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
                if (!string.Equals(assetPath, roundTrip, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Meta identity changed during validation: " + assetPath);
                using SHA256 sha = SHA256.Create();
                evidence.metas.Add(new MetaEvidenceItem
                {
                    path = assetPath + ".meta",
                    metaSha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(metaPath)))
                        .Replace("-", string.Empty).ToLowerInvariant(),
                    assetDatabaseGuid = guid,
                    roundTripPath = roundTrip
                });
            }
            string destination = Path.Combine(root, "TestResults", "meta-guid-evidence.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(evidence, true), new UTF8Encoding(false));
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }

        [Serializable]
        private sealed class MetaEvidence
        {
            public int schemaVersion = 1;
            public string engineVersion;
            public List<MetaEvidenceItem> metas = new List<MetaEvidenceItem>();
        }

        [Serializable]
        private sealed class MetaEvidenceItem
        {
            public string path, metaSha256, assetDatabaseGuid, roundTripPath;
        }

        public static List<string> CollectErrors()
        {
            var errors = new List<string>();
            var assetPathByGuid = new Dictionary<string, string>(StringComparer.Ordinal);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                errors.Add("无法解析项目根目录。");
                return errors;
            }

            string normalizedProjectRoot = NormalizePath(Path.GetFullPath(projectRoot)).TrimEnd('/');
            foreach (string fullPath in Directory.EnumerateFileSystemEntries(
                         Application.dataPath,
                         "*",
                         SearchOption.AllDirectories))
            {
                string normalizedFullPath = NormalizePath(Path.GetFullPath(fullPath));
                if (!normalizedFullPath.StartsWith(normalizedProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                string assetPath = normalizedFullPath.Substring(normalizedProjectRoot.Length + 1);
                if (IsIgnoredAssetPath(assetPath) || assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                string metaPath = fullPath + ".meta";
                if (!File.Exists(metaPath))
                {
                    errors.Add($"[META005] 资源缺少 .meta：{assetPath}");
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(
                    assetPath,
                    AssetPathToGUIDOptions.OnlyExistingAssets);
                if (string.IsNullOrEmpty(guid) || string.Equals(guid, ZeroGuid, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"[META101] AssetDatabase 无法解析资源：{assetPath}");
                    continue;
                }

                string resolvedPath = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
                if (!string.Equals(resolvedPath, assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"[META102] GUID 回查路径不一致：期望 {assetPath}，实际 {resolvedPath}");
                }

                if (assetPathByGuid.TryGetValue(guid, out string existingPath) &&
                    !string.Equals(existingPath, assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"[META004] AssetDatabase GUID 重复：{existingPath} 与 {assetPath}");
                }
                else
                {
                    assetPathByGuid[guid] = assetPath;
                }
            }

            return errors;
        }

        private static void ValidateOrThrow()
        {
            List<string> errors = CollectErrors();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            Debug.Log("[MetaGuidValidator] AssetDatabase GUID 往返校验通过。");
        }

        private static bool IsIgnoredAssetPath(string assetPath)
        {
            string[] segments = assetPath.Split('/');
            foreach (string segment in segments)
            {
                if (segment.StartsWith(".", StringComparison.Ordinal) ||
                    segment.EndsWith("~", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
