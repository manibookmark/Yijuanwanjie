using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace WanjieScroll.EditorTools
{
    /// <summary>
    /// Owns the selected program-icon binding and its build-time identity contract.
    /// The brand selection record is authoritative; imports and assignments happen
    /// only through the explicit command, never on reload or during validation.
    /// This does not replace the independent in-game title artwork.
    /// </summary>
    public static class PlayerBrandingSettings
    {
        public const string SelectionPath = "docs/game-design/assets/branding/selection.json";
        public const string SelectedSourcePath = "SourceAssets/branding/20261027/app-icon-selected-1024.png";
        public const string IconAssetPath = "Assets/Branding/AppIcon.png";

        [Serializable] private sealed class Selection { public Symbol symbol = null; }
        [Serializable] private sealed class Symbol { public Export png1024 = null; }
        [Serializable] private sealed class Export { public string path = null; public string sha256 = null; }

        [MenuItem("万界长卷/素材/应用已选程序图标")]
        public static void ApplySelectedIcon()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("请在稳定的编辑模式应用程序图标。");
            var errors = new List<string>();
            ValidateSourceIdentity(errors);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (!File.Exists(IconAssetPath) || FileHash(IconAssetPath) != FileHash(SelectedSourcePath))
                throw new InvalidOperationException("程序图标未按选稿原字节导入：" + IconAssetPath);
            AssetDatabase.ImportAsset(IconAssetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(IconAssetPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("程序图标缺少 TextureImporter。");
            bool changed = importer.textureType != TextureImporterType.Default || importer.textureShape != TextureImporterShape.Texture2D || importer.mipmapEnabled ||
                !importer.alphaIsTransparency || importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.maxTextureSize != 1024 || importer.npotScale != TextureImporterNPOTScale.None ||
                importer.filterMode != FilterMode.Bilinear || importer.wrapMode != TextureWrapMode.Clamp;
            if (changed)
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 1024;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
            if (icon == null || icon.width != 1024 || icon.height != 1024)
                throw new InvalidOperationException("程序图标导入结果必须为1024×1024。");
            int defaultSlots = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Unknown, IconKind.Any).Length;
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown,
                Enumerable.Repeat(icon, Math.Max(1, defaultSlots)).ToArray(), IconKind.Any);
            int slots = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone, IconKind.Any).Length;
            if (slots > 0)
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone,
                    Enumerable.Repeat(icon, slots).ToArray(), IconKind.Any);
            var playerSettings = Resources.FindObjectsOfTypeAll<PlayerSettings>().Single();
            AssetDatabase.SaveAssetIfDirty(playerSettings);
            // PlayerSettings may retain its native dirty bit after SaveAssetIfDirty.
            // Check the actual serialized reference instead of treating that flag as disk state.
            if (!File.ReadAllText("ProjectSettings/ProjectSettings.asset").Contains(
                    "guid: " + AssetDatabase.AssetPathToGUID(IconAssetPath)))
                throw new InvalidOperationException("程序图标设置尚未保存到项目设置。");
            var result = CollectErrors();
            if (result.Count != 0) throw new InvalidOperationException(string.Join("\n", result));
        }

        public static List<string> CollectErrors()
        {
            var errors = new List<string>();
            ValidateSourceIdentity(errors);
            if (!File.Exists(IconAssetPath)) errors.Add("程序图标未导入：" + IconAssetPath);
            else if (File.Exists(SelectedSourcePath) && FileHash(IconAssetPath) != FileHash(SelectedSourcePath))
                errors.Add("程序图标与当前选稿字节不一致：" + IconAssetPath);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAssetPath);
            var importer = AssetImporter.GetAtPath(IconAssetPath) as TextureImporter;
            if (icon == null || icon.width != 1024 || icon.height != 1024 || importer == null ||
                importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.textureType != TextureImporterType.Default || importer.textureShape != TextureImporterShape.Texture2D || !importer.alphaIsTransparency)
                errors.Add("程序图标导入规格不正确。");
            string[] defaults = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Unknown, IconKind.Any)
                .Select(AssetDatabase.GetAssetPath).ToArray();
            string[] standalone = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Standalone, IconKind.Any)
                .Select(AssetDatabase.GetAssetPath).ToArray();
            int slots = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone, IconKind.Any).Length;
            errors.AddRange(ValidateIconReferences(defaults, standalone, slots));
            return errors;
        }

        // Pure rule boundary used by non-UI build-contract tests.
        internal static List<string> ValidateIconReferences(string[] defaults, string[] standalone, int standaloneSlots)
        {
            var errors = new List<string>();
            if (defaults == null || defaults.Length == 0 || defaults.Any(path => path != IconAssetPath))
                errors.Add("默认程序图标未绑定当前选稿。");
            if (standaloneSlots < 0 || standalone == null ||
                standaloneSlots > 0 && standalone.Length != standaloneSlots ||
                standalone.Any(path => path != IconAssetPath))
                errors.Add("Windows程序图标未完整绑定当前选稿。");
            return errors;
        }

        private static void ValidateSourceIdentity(List<string> errors)
        {
            try
            {
                if (!File.Exists(SelectionPath)) throw new InvalidOperationException("缺少品牌选稿记录。");
                var selected = JsonUtility.FromJson<Selection>(File.ReadAllText(SelectionPath));
                Export source = selected?.symbol?.png1024;
                if (source == null || source.path != SelectedSourcePath || string.IsNullOrEmpty(source.sha256) ||
                    !File.Exists(SelectedSourcePath) || !string.Equals(FileHash(SelectedSourcePath), source.sha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add("程序图标源稿与品牌选择记录不一致。");
            }
            catch (Exception exception) { errors.Add("程序图标选稿校验失败：" + exception.Message); }
        }

        private static string FileHash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
