using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WanjieScroll.EditorTools
{
    /// <summary>
    /// Owns lossless minification settings for independent card artwork only.
    /// Source pixels and authored Sprite identity/geometry remain untouched.
    /// </summary>
    public static class CardArtworkImportPolicy
    {
        private static readonly string[] Roots =
        {
            "Assets/Resources/Sprites/Cards/Illustrations",
            "Assets/Resources/Sprites/Cards/Cutout"
        };

        internal static bool IsManagedPath(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/');
            if (!normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/../") || normalized.Contains("/./")) return false;
            foreach (string root in Roots)
                if (normalized.StartsWith(root + "/", StringComparison.Ordinal)) return true;
            return false;
        }

        internal static void Apply(TextureImporter importer, string path)
        {
            if (!IsManagedPath(path)) return;
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            if (importer.textureType == TextureImporterType.Sprite &&
                importer.spriteImportMode != SpriteImportMode.Single)
                throw new InvalidOperationException($"卡图必须是独立 Single Sprite：{path}");
            Vector2Int dimensions = ReadPngDimensions(path);
            int maxSize = RequiredMaxSize(dimensions);
            HashSet<string> platforms = PlatformNames(importer);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipMapBias = 0f;
            importer.wrapModeU = importer.wrapModeV = importer.wrapModeW = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;

            foreach (string platform in platforms)
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                // Keep the existing override flag and all unrelated platform settings.
                settings.maxTextureSize = maxSize;
                settings.format = TextureImporterFormat.Automatic;
                settings.textureCompression = TextureImporterCompression.Uncompressed;
                settings.crunchedCompression = false;
                importer.SetPlatformTextureSettings(settings);
            }
        }

        [MenuItem("万界长卷/素材/应用卡牌插图导入策略")]
        public static void ConfigureAll()
        {
            List<string> paths = ManagedPaths();
            var changed = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                // Write every importer first, then queue imports for one batch.
                foreach (string path in paths)
                {
                    Apply(AssetImporter.GetAtPath(path) as TextureImporter, path);
                    if (AssetDatabase.WriteImportSettingsIfDirty(path)) changed.Add(path);
                }
                foreach (string path in changed)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }

            List<string> errors = CollectErrors();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            Debug.Log($"[CardArtworkImportPolicy] 已核对 {paths.Count} 张卡图，更新并导入 {changed.Count} 张。");
        }

        public static List<string> CollectErrors()
        {
            var errors = new List<string>();
            List<string> paths;
            try { paths = ManagedPaths(); }
            catch (Exception exception) { errors.Add(exception.Message); return errors; }
            foreach (string path in paths)
            {
                try { Validate(path, errors); }
                catch (Exception exception) { errors.Add($"{path}：{exception.Message}"); }
            }
            return errors;
        }

        private static void Validate(string path, List<string> errors)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { errors.Add($"卡图未导入：{path}"); return; }
            Vector2Int dimensions = ReadPngDimensions(path);
            int maxSize = RequiredMaxSize(dimensions);
            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single)
                errors.Add($"卡图必须是独立 Single Sprite：{path}");
            if (!importer.mipmapEnabled || importer.mipmapFilter != TextureImporterMipFilter.BoxFilter ||
                importer.filterMode != FilterMode.Trilinear || Mathf.Abs(importer.mipMapBias) > 0.001f ||
                importer.wrapModeU != TextureWrapMode.Clamp || importer.wrapModeV != TextureWrapMode.Clamp ||
                importer.wrapModeW != TextureWrapMode.Clamp)
                errors.Add($"卡图必须使用 Box Mipmap、Trilinear、零 Bias 与 Clamp：{path}");
            if (!importer.alphaIsTransparency || !importer.sRGBTexture || importer.isReadable ||
                importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize != maxSize ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || importer.crunchedCompression)
                errors.Add($"卡图透明、颜色、原始尺寸或无损导入参数不符合合同：{path}");
            foreach (string platform in PlatformNames(importer))
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                if (settings.maxTextureSize != maxSize || settings.format != TextureImporterFormat.Automatic ||
                    settings.textureCompression != TextureImporterCompression.Uncompressed || settings.crunchedCompression)
                    errors.Add($"卡图平台 {platform} 的原始尺寸或无损设置不符合合同：{path}");
            }
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null || texture.width != dimensions.x || texture.height != dimensions.y)
                errors.Add($"卡图实际导入尺寸必须保持 {dimensions.x}×{dimensions.y}：{path}");
            else if (texture.mipmapCount < 2)
                errors.Add($"卡图实际没有多层 Mipmap：{path}");
        }

        private static HashSet<string> PlatformNames(TextureImporter importer)
        {
            var names = new HashSet<string>(StringComparer.Ordinal)
                { "DefaultTexturePlatform", "Standalone", "Server" };
            using (var serialized = new SerializedObject(importer))
            {
                SerializedProperty platforms = serialized.FindProperty("m_PlatformSettings") ??
                    serialized.FindProperty("platformSettings");
                if (platforms == null || !platforms.isArray)
                    throw new InvalidOperationException("无法枚举实际纹理平台设置，拒绝遗漏平台覆盖。");
                for (int i = 0; i < platforms.arraySize; i++)
                {
                    SerializedProperty item = platforms.GetArrayElementAtIndex(i);
                    SerializedProperty name = item.FindPropertyRelative("m_BuildTarget") ??
                        item.FindPropertyRelative("buildTarget");
                    if (name == null || string.IsNullOrWhiteSpace(name.stringValue))
                        throw new InvalidOperationException("纹理平台设置缺少目标名称。");
                    names.Add(name.stringValue);
                }
            }
            return names;
        }

        private static Vector2Int ReadPngDimensions(string path)
        {
            // Source dimensions are unavailable from TextureImporter during first import.
            byte[] header;
            using (var reader = new BinaryReader(File.OpenRead(AbsolutePath(path))))
                header = reader.ReadBytes(24);
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (header.Length != 24) throw new InvalidDataException($"PNG 文件头不完整：{path}");
            for (int i = 0; i < signature.Length; i++)
                if (header[i] != signature[i]) throw new InvalidDataException($"PNG 签名错误：{path}");
            if (ReadBigEndian(header, 8) != 13 || header[12] != 73 || header[13] != 72 ||
                header[14] != 68 || header[15] != 82)
                throw new InvalidDataException($"PNG 首块不是合法 IHDR：{path}");
            var size = new Vector2Int(ReadBigEndian(header, 16), ReadBigEndian(header, 20));
            if (size.x <= 0 || size.y <= 0) throw new InvalidDataException($"PNG 尺寸无效：{path}");
            return size;
        }

        private static int ReadBigEndian(byte[] bytes, int offset) => checked((int)
            ((uint)bytes[offset] << 24 | (uint)bytes[offset + 1] << 16 |
             (uint)bytes[offset + 2] << 8 | bytes[offset + 3]));

        private static int RequiredMaxSize(Vector2Int dimensions)
        {
            int size = 2048;
            while (size < Math.Max(dimensions.x, dimensions.y)) size = checked(size * 2);
            return size;
        }

        private static List<string> ManagedPaths()
        {
            var paths = new List<string>();
            foreach (string root in Roots)
            {
                string directory = AbsolutePath(root);
                if (!Directory.Exists(directory))
                    throw new DirectoryNotFoundException($"卡图目录不存在：{root}");
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    string path = root + "/" + file.Substring(directory.Length + 1).Replace('\\', '/');
                    if (IsManagedPath(path)) paths.Add(path);
                }
            }
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        private static string AbsolutePath(string path) => Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, path.Replace('/', Path.DirectorySeparatorChar));
    }

    public sealed class CardArtworkImportPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (CardArtworkImportPolicy.IsManagedPath(assetPath))
                CardArtworkImportPolicy.Apply((TextureImporter)assetImporter, assetPath);
        }
    }
}
