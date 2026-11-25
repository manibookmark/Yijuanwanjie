using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WanjieScroll.EditorTools
{
    /// <summary>
    /// Owns the import contract for the standalone reward bodies used by map
    /// interaction nodes. These are not fixed-pixel UI icons: the same source
    /// sprites are rendered very small on the map and at a larger size in the
    /// reward reveal, so both consumers need a real mip chain and trilinear
    /// filtering. Keeping the rule here also prevents a future replacement
    /// asset from silently reintroducing the G2F2 I05 aliasing problem.
    /// </summary>
    public static class MapInteractionSpriteImportPolicy
    {
        public const string Root = "Assets/Resources/Sprites/UI/Rewards";

        private const string MenuPath =
            "万界长卷/素材/应用地图交互贴图导入策略";

        public static bool IsManagedPath(string assetPath)
        {
            string normalized = Normalize(assetPath);
            return normalized.StartsWith(Root + "/", StringComparison.Ordinal) &&
                normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        public static void Apply(TextureImporter importer, string assetPath)
        {
            if (importer == null)
                throw new ArgumentNullException(nameof(importer));

            if (!IsManagedPath(assetPath))
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipMapBias = 0f;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.wrapModeU = TextureWrapMode.Clamp;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.wrapModeW = TextureWrapMode.Clamp;
            importer.crunchedCompression = false;
        }

        /// <summary>
        /// Explicit migration entry point. Import settings are changed only
        /// for the standalone reward sprites in <see cref="Root"/>; PPU,
        /// rect, pivot, compression and existing GUIDs remain authored data.
        /// </summary>
        [MenuItem(MenuPath)]
        public static void ConfigureAll()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string assetPath in EnumerateManagedAssetPaths())
                {
                    TextureImporter importer =
                        AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer == null)
                        continue;

                    Apply(importer, assetPath);
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }

            List<string> errors = CollectErrors();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            Debug.Log("[MapInteractionSpriteImportPolicy] 地图交互奖励贴图导入策略已应用。");
        }

        public static List<string> CollectErrors()
        {
            var errors = new List<string>();
            string directory = ToAbsolutePath(Root);
            if (!Directory.Exists(directory))
            {
                errors.Add($"地图交互奖励贴图目录不存在：{Root}");
                return errors;
            }

            string[] files = Directory.GetFiles(
                directory,
                "*.png",
                SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                errors.Add($"地图交互奖励贴图目录没有 PNG：{Root}");
                return errors;
            }

            foreach (string file in files)
                ValidateImporter(ToAssetPath(file), errors);

            return errors;
        }

        private static void ValidateImporter(string assetPath, List<string> errors)
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                errors.Add($"地图交互奖励贴图未被 TextureImporter 接管：{assetPath}");
                return;
            }

            if (importer.textureType != TextureImporterType.Sprite)
                errors.Add($"地图交互奖励贴图必须是 Sprite：{assetPath}");
            if (importer.spriteImportMode != SpriteImportMode.Single)
                errors.Add($"地图交互奖励贴图必须是 Single Sprite：{assetPath}");
            if (!importer.alphaIsTransparency)
                errors.Add($"地图交互奖励贴图必须保留 Alpha Is Transparency：{assetPath}");
            if (importer.isReadable)
                errors.Add($"地图交互奖励贴图禁止保留 CPU 可读副本：{assetPath}");
            if (!importer.mipmapEnabled)
                errors.Add($"地图交互奖励贴图必须启用 Mipmap：{assetPath}");
            if (importer.filterMode != FilterMode.Trilinear)
                errors.Add($"地图交互奖励贴图必须使用 Trilinear：{assetPath}");
            if (Mathf.Abs(importer.mipMapBias) > 0.001f)
                errors.Add($"地图交互奖励贴图 Mipmap Bias 必须为 0：{assetPath}");
            if (importer.wrapMode != TextureWrapMode.Clamp ||
                importer.wrapModeU != TextureWrapMode.Clamp ||
                importer.wrapModeV != TextureWrapMode.Clamp ||
                importer.wrapModeW != TextureWrapMode.Clamp)
            {
                errors.Add($"地图交互奖励贴图必须使用 Clamp：{assetPath}");
            }
            if (importer.crunchedCompression)
                errors.Add($"地图交互奖励贴图禁止 Crunch 压缩：{assetPath}");

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                errors.Add($"地图交互奖励贴图尚未导入，无法检查实际 Mipmap：{assetPath}");
            }
            else if (texture.mipmapCount < 2)
            {
                errors.Add(
                    $"地图交互奖励贴图实际 Mipmap 层级不足（{texture.mipmapCount}）：{assetPath}");
            }
        }

        private static IEnumerable<string> EnumerateManagedAssetPaths()
        {
            string directory = ToAbsolutePath(Root);
            if (!Directory.Exists(directory))
                yield break;

            foreach (string file in Directory.GetFiles(
                directory,
                "*.png",
                SearchOption.TopDirectoryOnly))
            {
                yield return ToAssetPath(file);
            }
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(
                projectRoot,
                Normalize(assetPath).Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ToAssetPath(string absolutePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string normalizedRoot = Normalize(projectRoot).TrimEnd('/');
            string normalizedPath = Normalize(Path.GetFullPath(absolutePath));
            if (!normalizedPath.StartsWith(
                normalizedRoot + "/",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"路径不在项目内：{absolutePath}");
            }

            return normalizedPath.Substring(normalizedRoot.Length + 1);
        }
    }

    public sealed class MapInteractionSpriteImportPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            string normalizedAssetPath = assetPath.Replace('\\', '/');
            if (!MapInteractionSpriteImportPolicy.IsManagedPath(normalizedAssetPath))
                return;

            MapInteractionSpriteImportPolicy.Apply(
                (TextureImporter)assetImporter,
                normalizedAssetPath);
        }
    }
}
