using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WanjieScroll.Content;

namespace WanjieScroll.EditorTools
{
    /// <summary>
    /// Builds the Player-readable content package from the repository JSON author
    /// source. The manifest is written last, so a partial generation cannot look
    /// current to the build validator.
    /// </summary>
    public static class ContentPackageGenerator
    {
        private const string SourceDirectoryRelative = "ContentSource/cards";
        private const string OutputDirectoryRelative =
            "Assets/Resources/Content/Generated/cards";
        private const string ManifestPathRelative =
            "Assets/Resources/Content/Generated/content-manifest.json";

        private static readonly UTF8Encoding Utf8WithoutBom =
            new UTF8Encoding(false);

        public static void GenerateFromMenu()
        {
            int count = GeneratePackage();
            Debug.Log($"[ContentPackageGenerator] 已生成 {count} 个卷的运行时内容包。");
        }

        /// <summary>Batch entry point for -executeMethod.</summary>
        public static void GenerateForBatch()
        {
            int count = GeneratePackage();
            Debug.Log($"[ContentPackageGenerator] 批量生成完成：{count} 个卷。");
        }

        /// <summary>
        /// Explicit build admission: current packages are read-only; missing or
        /// stale packages go through the official all-before-write generator
        /// once. A failed source validation or remaining mismatch stops the build.
        /// </summary>
        internal static bool EnsureCurrentPackage()
            => EnsureCurrentPackage(CollectErrors, () => GeneratePackage());

        // The seam tests this admission policy without changing production
        // author files, generated resources or AssetDatabase state.
        private static bool EnsureCurrentPackage(Func<List<string>> collectErrors, Action generate)
        {
            List<string> errors = collectErrors();
            if (errors.Count == 0) return false;
            generate();
            errors = collectErrors();
            if (errors.Count > 0)
                throw new InvalidOperationException("Official content generation did not produce a current runtime package: " +
                    string.Join(Environment.NewLine, errors));
            return true;
        }

        internal static int GeneratePackage()
        {
            string projectRoot = ResolveProjectRoot();
            string sourceDirectory = Path.Combine(
                projectRoot,
                SourceDirectoryRelative.Replace('/', Path.DirectorySeparatorChar));
            string outputDirectory = Path.Combine(
                projectRoot,
                OutputDirectoryRelative.Replace('/', Path.DirectorySeparatorChar));
            string manifestPath = Path.Combine(
                projectRoot,
                ManifestPathRelative.Replace('/', Path.DirectorySeparatorChar));

            string[] sourceFiles = FindSourceFiles(sourceDirectory);
            var entries = new List<ContentPackageEntry>(sourceFiles.Length);
            var sourceTexts = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            var volumes = new List<VolumeContent>(sourceFiles.Length);

            foreach (string sourcePath in sourceFiles)
            {
                string fileName = Path.GetFileName(sourcePath);
                string normalizedJson = ContentPackageLoader.NormalizeTextForHash(
                    File.ReadAllText(sourcePath, Encoding.UTF8));
                VolumeContent volume = ContentPackageLoader.ParseVolume(
                    normalizedJson,
                    sourcePath);
                string resourcePath =
                    ContentPackageLoader.GeneratedVolumeResourcePrefix +
                    Path.GetFileNameWithoutExtension(fileName);

                sourceTexts.Add(fileName, normalizedJson);
                volumes.Add(volume);
                entries.Add(new ContentPackageEntry
                {
                    sourceFileName = fileName,
                    resourcePath = resourcePath,
                    volumeId = volume.volumeId,
                    sha256 = ContentPackageLoader.ComputeSha256(normalizedJson)
                });
            }

            // Use the same normalization and semantic validator as runtime before
            // publishing any generated file.
            ContentCatalog.CreateForPackage(volumes);

            var manifest = new ContentPackageManifest
            {
                schemaVersion = ContentPackageLoader.CurrentPackageSchemaVersion,
                contentSchemaVersion = ContentCatalog.CurrentSchemaVersion,
                entries = entries
            };
            manifest.contentVersion =
                ContentPackageLoader.ComputeContentVersion(manifest.entries);

            Directory.CreateDirectory(outputDirectory);
            RemoveStaleGeneratedVolumes(
                outputDirectory,
                new HashSet<string>(
                    sourceTexts.Keys,
                    StringComparer.OrdinalIgnoreCase));
            foreach (ContentPackageEntry entry in entries)
            {
                string outputPath = Path.Combine(
                    outputDirectory,
                    entry.sourceFileName);
                File.WriteAllText(
                    outputPath,
                    sourceTexts[entry.sourceFileName],
                    Utf8WithoutBom);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
            string manifestJson = ContentPackageLoader.NormalizeTextForHash(
                JsonUtility.ToJson(manifest, true)) + "\n";
            File.WriteAllText(manifestPath, manifestJson, Utf8WithoutBom);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // A successfully published package becomes the next catalog authority;
            // existing owners retain their detached previously admitted instance.
            ContentCatalog.InvalidateGeneratedPackageCache();
            return entries.Count;
        }

        private static void RemoveStaleGeneratedVolumes(
            string outputDirectory,
            HashSet<string> expectedFileNames)
        {
            foreach (string generatedPath in Directory.GetFiles(
                         outputDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(generatedPath);
                if (expectedFileNames.Contains(fileName))
                    continue;

                string assetPath = OutputDirectoryRelative + "/" + fileName;
                if (!AssetDatabase.DeleteAsset(assetPath))
                {
                    throw new InvalidOperationException(
                        $"无法删除无作者源的旧运行时内容：{assetPath}。");
                }
            }
        }

        internal static List<string> CollectErrors()
        {
            var errors = new List<string>();
            try
            {
                CollectErrorsCore(errors);
            }
            catch (Exception exception)
            {
                errors.Add($"运行时内容包校验异常：{FlattenException(exception)}");
            }
            return errors;
        }

        private static void CollectErrorsCore(List<string> errors)
        {
            string projectRoot = ResolveProjectRoot();
            string sourceDirectory = Path.Combine(
                projectRoot,
                SourceDirectoryRelative.Replace('/', Path.DirectorySeparatorChar));
            string outputDirectory = Path.Combine(
                projectRoot,
                OutputDirectoryRelative.Replace('/', Path.DirectorySeparatorChar));
            string manifestPath = Path.Combine(
                projectRoot,
                ManifestPathRelative.Replace('/', Path.DirectorySeparatorChar));

            string[] sourceFiles = FindSourceFiles(sourceDirectory);
            if (!File.Exists(manifestPath))
            {
                errors.Add(
                    $"缺少运行时内容包清单：{ManifestPathRelative}。" +
                    "请执行“万界长卷/内容/生成运行时内容包”。");
                return;
            }

            string manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
            ContentPackageManifest manifest =
                ContentPackageLoader.ParseManifest(manifestJson);
            var entriesBySource = manifest.entries.ToDictionary(
                entry => entry.sourceFileName,
                StringComparer.OrdinalIgnoreCase);
            var resourceFiles = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            if (entriesBySource.Count != sourceFiles.Length)
            {
                errors.Add(
                    $"内容包条目数与作者源不一致：清单={entriesBySource.Count}，" +
                    $"作者源={sourceFiles.Length}。");
            }

            var expectedEntries = new List<ContentPackageEntry>(sourceFiles.Length);
            foreach (string sourcePath in sourceFiles)
            {
                string fileName = Path.GetFileName(sourcePath);
                string sourceJson = File.ReadAllText(sourcePath, Encoding.UTF8);
                VolumeContent sourceVolume = ContentPackageLoader.ParseVolume(
                    sourceJson,
                    sourcePath);
                string sourceHash = ContentPackageLoader.ComputeSha256(sourceJson);
                string expectedResourcePath =
                    ContentPackageLoader.GeneratedVolumeResourcePrefix +
                    Path.GetFileNameWithoutExtension(fileName);

                expectedEntries.Add(new ContentPackageEntry
                {
                    sourceFileName = fileName,
                    resourcePath = expectedResourcePath,
                    volumeId = sourceVolume.volumeId,
                    sha256 = sourceHash
                });

                if (!entriesBySource.TryGetValue(fileName, out ContentPackageEntry entry))
                {
                    errors.Add($"内容包清单缺少作者源条目：{fileName}。");
                    continue;
                }
                if (!string.Equals(entry.sha256, sourceHash, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"运行时内容包已过期：{fileName}；" +
                        $"清单={entry.sha256}，作者源={sourceHash}。");
                }
                if (!string.Equals(entry.volumeId, sourceVolume.volumeId, StringComparison.Ordinal))
                {
                    errors.Add(
                        $"内容包 volumeId 与作者源不一致：{fileName}；" +
                        $"清单={entry.volumeId}，作者源={sourceVolume.volumeId}。");
                }
                if (!string.Equals(entry.resourcePath, expectedResourcePath, StringComparison.Ordinal))
                {
                    errors.Add(
                        $"内容包资源路径与作者源不一致：{fileName}；" +
                        $"清单={entry.resourcePath}，期望={expectedResourcePath}。");
                }

                string generatedPath = Path.Combine(outputDirectory, fileName);
                resourceFiles[expectedResourcePath] = generatedPath;
                if (!File.Exists(generatedPath))
                {
                    errors.Add(
                        $"作者源 {fileName} 缺少 Player 运行时副本：" +
                        $"{OutputDirectoryRelative}/{fileName}。");
                    continue;
                }

                string generatedHash = ContentPackageLoader.ComputeSha256(
                    File.ReadAllText(generatedPath, Encoding.UTF8));
                if (!string.Equals(generatedHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Player 运行时副本与作者源不一致：{fileName}；" +
                        $"副本={generatedHash}，作者源={sourceHash}。");
                }
            }

            string expectedVersion =
                ContentPackageLoader.ComputeContentVersion(expectedEntries);
            if (!string.Equals(
                    manifest.contentVersion,
                    expectedVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"内容包 contentVersion 已过期：清单={manifest.contentVersion}，" +
                    $"作者源={expectedVersion}。");
            }

            string[] generatedFiles = Directory.Exists(outputDirectory)
                ? Directory.GetFiles(outputDirectory, "*.json", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            var sourceNames = new HashSet<string>(
                sourceFiles.Select(Path.GetFileName),
                StringComparer.OrdinalIgnoreCase);
            foreach (string generatedFile in generatedFiles)
            {
                string fileName = Path.GetFileName(generatedFile);
                if (!sourceNames.Contains(fileName))
                {
                    errors.Add(
                        $"运行时内容目录存在无作者源的陈旧文件：" +
                        $"{OutputDirectoryRelative}/{fileName}。");
                }
            }

            if (errors.Count > 0)
                return;

            List<VolumeContent> runtimeVolumes =
                ContentPackageLoader.LoadPackage(
                    manifestJson,
                    resourcePath =>
                    {
                        if (!resourceFiles.TryGetValue(resourcePath, out string path) ||
                            !File.Exists(path))
                        {
                            return null;
                        }
                        return File.ReadAllText(path, Encoding.UTF8);
                    });
            ContentCatalog.CreateForPackage(runtimeVolumes);
        }

        private static string[] FindSourceFiles(string sourceDirectory)
        {
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException(
                    $"内容作者源目录不存在：{sourceDirectory}。");

            string[] files = Directory.GetFiles(
                sourceDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly);
            Array.Sort(files, (left, right) => StringComparer.Ordinal.Compare(
                Path.GetFileName(left),
                Path.GetFileName(right)));
            if (files.Length == 0)
                throw new InvalidOperationException(
                    $"内容作者源目录没有 JSON：{sourceDirectory}。");
            return files;
        }

        private static string ResolveProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string FlattenException(Exception exception)
        {
            Exception current = exception;
            while (current.InnerException != null)
                current = current.InnerException;
            return current.Message;
        }
    }
}
