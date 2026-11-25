using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WanjieScroll.EditorTools
{
    /// <summary>
    /// Single owner for the import contract of the paper-craft enemy sprite
    /// variants.  The battle and map folders intentionally have different
    /// pixels-per-unit values because their source images are authored at
    /// different display scales.
    /// </summary>
    public static class PapercraftSpriteImportPolicy
    {
        public const string BattleRoot =
            "Assets/Resources/Sprites/Enemies/Papercraft";
        public const string MapRoot = BattleRoot + "/Map";

        public const float BattlePixelsPerUnit = 50f;
        public const float MapPixelsPerUnit = 25f;
        public const int MaxTextureSize = 2048;

        public static bool IsManagedPath(string assetPath)
        {
            string normalized = Normalize(assetPath);
            return normalized.StartsWith(BattleRoot + "/", StringComparison.Ordinal) ||
                normalized.StartsWith(MapRoot + "/", StringComparison.Ordinal);
        }

        public static void Apply(TextureImporter importer, string assetPath)
        {
            if (importer == null)
                throw new ArgumentNullException(nameof(importer));

            float pixelsPerUnit = IsMapPath(assetPath)
                ? MapPixelsPerUnit
                : BattlePixelsPerUnit;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = ResolveMipmapFilter(assetPath);
            importer.filterMode = FilterMode.Trilinear;
            importer.mipMapBias = 0f;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.wrapModeU = TextureWrapMode.Clamp;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.wrapModeW = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.compressionQuality = 100;
            importer.crunchedCompression = false;
            importer.maxTextureSize = MaxTextureSize;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.spritePixelsPerUnit = pixelsPerUnit;
        }

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

            Debug.Log("[PapercraftSpriteImportPolicy] 纸偶贴图导入策略已应用。");
        }

        public static List<string> CollectErrors()
        {
            var errors = new List<string>();
            string battleDirectory = ToAbsolutePath(BattleRoot);
            string mapDirectory = ToAbsolutePath(MapRoot);

            if (!Directory.Exists(battleDirectory))
            {
                errors.Add($"纸偶战斗贴图目录不存在：{BattleRoot}");
                return errors;
            }

            if (!Directory.Exists(mapDirectory))
                errors.Add($"纸偶地图贴图目录不存在：{MapRoot}");

            string[] battleFiles = Directory.GetFiles(
                battleDirectory,
                "*.png",
                SearchOption.TopDirectoryOnly);
            if (battleFiles.Length == 0)
                errors.Add($"纸偶战斗贴图目录没有 PNG：{BattleRoot}");

            foreach (string battleFile in battleFiles)
            {
                string battlePath = ToAssetPath(battleFile);
                string mapPath = MapRoot + "/" + Path.GetFileName(battleFile);
                ValidateImporter(battlePath, BattlePixelsPerUnit, errors);

                if (!File.Exists(ToAbsolutePath(mapPath)))
                {
                    errors.Add($"缺少纸偶地图规格贴图：{mapPath}");
                    continue;
                }

                ValidateImporter(mapPath, MapPixelsPerUnit, errors);
                ValidateVariantDimensions(battlePath, mapPath, errors);
                ValidateMapProfileReferences(battlePath, errors);
            }

            return errors;
        }

        private static void ValidateImporter(
            string assetPath,
            float expectedPixelsPerUnit,
            List<string> errors)
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                errors.Add($"纸偶贴图未被 TextureImporter 接管：{assetPath}");
                return;
            }

            if (importer.textureType != TextureImporterType.Sprite)
                errors.Add($"纸偶贴图必须是 Sprite：{assetPath}");
            if (importer.spriteImportMode != SpriteImportMode.Single)
                errors.Add($"纸偶贴图必须是 Single Sprite：{assetPath}");
            if (importer.isReadable)
                errors.Add($"纸偶贴图禁止保留 CPU 可读副本：{assetPath}");
            if (!importer.mipmapEnabled)
                errors.Add($"纸偶贴图必须启用 Mipmap：{assetPath}");
            TextureImporterMipFilter expectedMipmapFilter = ResolveMipmapFilter(assetPath);
            if (importer.mipmapFilter != expectedMipmapFilter)
            {
                errors.Add(
                    $"纸偶贴图 Mipmap 生成过滤应为 {expectedMipmapFilter}：{assetPath}");
            }
            if (Mathf.Abs(importer.mipMapBias) > 0.001f)
                errors.Add($"纸偶贴图 Mipmap Bias 必须为 0：{assetPath}");
            if (importer.filterMode != FilterMode.Trilinear)
                errors.Add($"纸偶贴图必须使用 Trilinear：{assetPath}");
            if (importer.wrapMode != TextureWrapMode.Clamp ||
                importer.wrapModeU != TextureWrapMode.Clamp ||
                importer.wrapModeV != TextureWrapMode.Clamp ||
                importer.wrapModeW != TextureWrapMode.Clamp)
                errors.Add($"纸偶贴图必须使用 Clamp：{assetPath}");
            if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
                errors.Add($"纸偶贴图必须使用 HQ 压缩：{assetPath}");
            if (importer.compressionQuality != 100)
                errors.Add($"纸偶贴图压缩质量必须为 100：{assetPath}");
            if (importer.crunchedCompression)
                errors.Add($"纸偶贴图禁止 Crunch 压缩：{assetPath}");
            if (importer.maxTextureSize != MaxTextureSize)
                errors.Add($"纸偶贴图 Max Texture Size 应为 {MaxTextureSize}：{assetPath}");
            if (Mathf.Abs(importer.spritePixelsPerUnit - expectedPixelsPerUnit) > 0.01f)
            {
                errors.Add(
                    $"纸偶贴图 PPU 应为 {expectedPixelsPerUnit}，实际为 " +
                    $"{importer.spritePixelsPerUnit}：{assetPath}");
            }
        }

        private static void ValidateVariantDimensions(
            string battlePath,
            string mapPath,
            List<string> errors)
        {
            Texture2D battleTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(battlePath);
            Texture2D mapTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath);
            if (battleTexture == null || mapTexture == null)
            {
                errors.Add($"纸偶贴图尚未导入，无法校验尺寸：{battlePath} / {mapPath}");
                return;
            }

            if (Mathf.Abs(mapTexture.width * 2 - battleTexture.width) > 1 ||
                Mathf.Abs(mapTexture.height * 2 - battleTexture.height) > 1)
            {
                errors.Add(
                    $"纸偶地图规格应为战斗规格的一半：" +
                    $"battle={battleTexture.width}x{battleTexture.height}, " +
                    $"map={mapTexture.width}x{mapTexture.height}：{mapPath}");
            }
        }

        private static void ValidateMapProfileReferences(
            string battlePath,
            List<string> errors)
        {
            string battleGuid = AssetDatabase.AssetPathToGUID(battlePath);
            if (string.IsNullOrEmpty(battleGuid))
                return;

            string profileDirectory = ToAbsolutePath(
                "Assets/Resources/MapPreviewProfiles");
            if (!Directory.Exists(profileDirectory))
                return;

            foreach (string profilePath in Directory.GetFiles(
                profileDirectory,
                "*.asset",
                SearchOption.TopDirectoryOnly))
            {
                string profileText = File.ReadAllText(profilePath);
                if (profileText.IndexOf(
                    $"guid: {battleGuid}",
                    StringComparison.Ordinal) >= 0)
                {
                    errors.Add(
                        $"地图配置仍引用战斗规格纸偶贴图：" +
                        $"{ToAssetPath(profilePath)} -> {battlePath}");
                }
            }
        }

        private static IEnumerable<string> EnumerateManagedAssetPaths()
        {
            foreach (string root in new[] { BattleRoot, MapRoot })
            {
                string directory = ToAbsolutePath(root);
                if (!Directory.Exists(directory))
                    continue;

                foreach (string file in Directory.GetFiles(
                    directory,
                    "*.png",
                    SearchOption.TopDirectoryOnly))
                {
                    yield return ToAssetPath(file);
                }
            }
        }

        // Map silhouettes are heavily reduced on screen; Kaiser retains their
        // small-mip detail while battle artwork keeps the existing Box filter.
        private static TextureImporterMipFilter ResolveMipmapFilter(string assetPath)
        {
            return IsMapPath(assetPath)
                ? TextureImporterMipFilter.KaiserFilter
                : TextureImporterMipFilter.BoxFilter;
        }

        private static bool IsMapPath(string assetPath)
        {
            string normalized = Normalize(assetPath);
            return normalized.StartsWith(MapRoot + "/", StringComparison.Ordinal);
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, Normalize(assetPath).Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ToAssetPath(string absolutePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string normalizedRoot = Normalize(projectRoot).TrimEnd('/');
            string normalizedPath = Normalize(Path.GetFullPath(absolutePath));
            if (!normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"路径不在项目内：{absolutePath}");
            return normalizedPath.Substring(normalizedRoot.Length + 1);
        }
    }

    public sealed class PapercraftSpriteImportPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            string normalizedAssetPath = assetPath.Replace('\\', '/');
            if (!PapercraftSpriteImportPolicy.IsManagedPath(normalizedAssetPath))
                return;

            PapercraftSpriteImportPolicy.Apply(
                (TextureImporter)assetImporter,
                normalizedAssetPath);
        }
    }
}
