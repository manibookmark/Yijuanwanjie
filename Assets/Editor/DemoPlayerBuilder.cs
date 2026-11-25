using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WanjieScroll.EditorTools
{
    /// <summary>
    /// Builds an immutable graphical demo with the production validation path.
    /// Demo symbols belong to this Player invocation, never the Editor project.
    /// Only BuildPipeline output is packaged; account data is never copied.
    /// </summary>
    public static class DemoPlayerBuilder
    {
        public const string DemoDefine = "WANJIE_DEMO";
        public const string ExecutableName = "WanjieScroll.Demo.exe";
        public const string ManifestName = "demo-build-manifest.json";
        private static readonly string[] NonShippingDirectorySuffixes =
        {
            "_BurstDebugInformation_DoNotShip", "_BackUpThisFolder_ButDontShipItWithYourGame"
        };
        private static bool isBuilding;

        public static List<string> CollectPreflightErrors()
        {
            var errors = new List<string>();
            if (isBuilding || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                errors.Add("Build only from a stable Edit-mode window.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                errors.Add("Windows64 Player build support is not installed.");
            string[] symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone).Split(';');
            if (symbols.Any(symbol => symbol.Trim() == DemoDefine || symbol.Trim() == "WANJIE_SIMULATION_HOST" || symbol.Trim() == "UNITY_SERVER"))
                errors.Add("Standalone global defines contain a demo, simulation or server profile; remove it before this per-build graphical demo.");
            errors.AddRange(ProjectBuildSettings.CollectErrors());
            return errors;
        }

        // Does not start the Player, save scenes, regenerate content or alter
        // product identity, bundle version, global symbols or Build Settings.
        public static string Build(string version = null)
        {
            List<string> errors = CollectPreflightErrors();
            if (errors.Count > 0)
                throw new BuildFailedException(string.Join(Environment.NewLine, errors));
            string project = Path.GetDirectoryName(Application.dataPath);
            string buildVersion = version ?? DefaultVersion(PlayerSettings.bundleVersion, DateTime.UtcNow);
            string output = ResolveOutputDirectory(project, buildVersion);
            RequireUnusedOutput(project, output);

            // App UI's package postprocessor removes all AppUISettings entries,
            // and a failed build may skip it entirely. Restore the exact prior
            // list on either path, rather than inheriting package side effects.
            UnityEngine.Object[] preloadedAssets = PlayerSettings.GetPreloadedAssets() ?? Array.Empty<UnityEngine.Object>();
            string productName = PlayerSettings.productName;
            string bundleVersion = PlayerSettings.bundleVersion;
            string globalSymbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone);
            Directory.CreateDirectory(output);
            isBuilding = true;
            try
            {
                BuildReport report = BuildPipeline.BuildPlayer(CreateBuildOptions(output));
                if (report == null || report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                    throw new BuildFailedException("Graphical demo build did not succeed; incomplete output was retained for diagnosis.");
                string runtimeLibrary = RequirePlayerFiles(output);
                var manifest = new BuildManifest
                {
                    buildVersion = buildVersion,
                    builtAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    productName = productName,
                    bundleVersion = bundleVersion,
                    engineVersion = Application.unityVersion,
                    buildGuid = report.summary.guid.ToString(),
                    playerRuntimeLibrary = runtimeLibrary,
                    totalErrors = (int)report.summary.totalErrors,
                    totalWarnings = (int)report.summary.totalWarnings,
                    buildOptions = CreateBuildOptions(output).options.ToString(),
                    productionScenes = ProjectBuildSettings.ProductionScenes.ToArray(),
                    contentManifestSha256 = FileSha256(Path.Combine(project,
                        "Assets/Resources/Content/Generated/content-manifest.json")),
                    files = CapturePackageFiles(output)
                };
                manifest.totalBytes = manifest.files.Sum(file => file.bytes);
                File.WriteAllText(Path.Combine(output, ManifestName), JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
                Debug.Log("[DemoPlayerBuilder] Windows64 demo built: " + output);
                return output;
            }
            finally
            {
                isBuilding = false;
                if (!(PlayerSettings.GetPreloadedAssets() ?? Array.Empty<UnityEngine.Object>()).SequenceEqual(preloadedAssets))
                    PlayerSettings.SetPreloadedAssets(preloadedAssets);
                if (PlayerSettings.productName != productName) PlayerSettings.productName = productName;
                if (PlayerSettings.bundleVersion != bundleVersion) PlayerSettings.bundleVersion = bundleVersion;
                if (PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone) != globalSymbols)
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, globalSymbols);
            }
        }

        internal static BuildPlayerOptions CreateBuildOptions(string outputDirectory)
        {
            return new BuildPlayerOptions
            {
                scenes = ProjectBuildSettings.ProductionScenes.ToArray(),
                locationPathName = Path.Combine(outputDirectory, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.CompressWithLz4HC,
                extraScriptingDefines = new[] { DemoDefine }
            };
        }

        internal static string ResolveOutputDirectory(string projectDirectory, string version)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory)) throw new ArgumentException("Project directory is required.");
            if (string.IsNullOrEmpty(version) || version.Length > 96 ||
                !Regex.IsMatch(version, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z") || version.EndsWith(".", StringComparison.Ordinal))
                throw new ArgumentException("Demo version must be one safe Windows directory name (letters, digits, dot, underscore, hyphen; at most 96 characters).");
            string stem = version.Split('.')[0];
            if (Regex.IsMatch(stem, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase))
                throw new ArgumentException("Demo version cannot be a reserved Windows device name.");
            return Path.GetFullPath(Path.Combine(Path.GetFullPath(projectDirectory), "Builds", "Demo", version));
        }

        internal static void RequireUnusedOutput(string projectDirectory, string outputDirectory)
        {
            string project = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string collection = Path.GetFullPath(Path.Combine(project, "Builds", "Demo")) + Path.DirectorySeparatorChar;
            string output = Path.GetFullPath(outputDirectory);
            if (!output.StartsWith(collection, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Demo output must remain inside this project's Builds/Demo directory.");
            for (var directory = new DirectoryInfo(output); directory != null; directory = directory.Parent)
            {
                if (File.Exists(directory.FullName) || (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0))
                    throw new IOException("Demo output cannot use files, symbolic links or junctions as directories.");
                if (string.Equals(directory.FullName, project, StringComparison.OrdinalIgnoreCase)) break;
            }
            if (Directory.Exists(output))
                throw new IOException("Demo version already exists; choose a new version instead of overwriting a prior build.");
        }

        internal static List<PackageFile> CapturePackageFiles(string outputDirectory)
        {
            string root = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var files = new List<PackageFile>();
            CaptureDirectory(new DirectoryInfo(root), root, files);
            files.Sort((left, right) => string.CompareOrdinal(left.path, right.path));
            return files;
        }

        private static void CaptureDirectory(DirectoryInfo directory, string root, List<PackageFile> result)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Package inventory cannot follow a symbolic link or junction.");
            foreach (FileInfo file in directory.GetFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Package inventory cannot include a symbolic link.");
                string relative = file.FullName.Substring(root.Length + 1).Replace('\\', '/');
                if (relative == ManifestName) continue;
                result.Add(new PackageFile { path = relative, bytes = file.Length });
            }
            foreach (DirectoryInfo child in directory.GetDirectories())
            {
                // Preserve engine diagnostic directories on disk, but do not
                // describe them as redistributable Player payload. This rule
                // applies only at the build root, never inside required Data.
                if (string.Equals(directory.FullName, root, StringComparison.OrdinalIgnoreCase) &&
                    NonShippingDirectorySuffixes.Any(suffix => child.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                    continue;
                CaptureDirectory(child, root, result);
            }
        }

        private static string DefaultVersion(string bundleVersion, DateTime utcNow)
        {
            string prefix = Regex.Replace(bundleVersion ?? string.Empty, "[^A-Za-z0-9._-]", "-").Trim('.', '-');
            if (prefix.Length > 48) prefix = prefix.Substring(0, 48).TrimEnd('.');
            if (string.IsNullOrEmpty(prefix)) prefix = "demo";
            return prefix + "-demo-" + utcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        }

        internal static string RequirePlayerFiles(string output)
        {
            string runtime = new[] { "TuanjiePlayer.dll", "UnityPlayer.dll" }
                .FirstOrDefault(name => IsNonEmptyFile(Path.Combine(output, name)));
            string data = Path.Combine(output, Path.GetFileNameWithoutExtension(ExecutableName) + "_Data");
            bool hasDataPayload = new[] { "data.tj3d", "data.unity3d", "globalgamemanagers" }
                .Any(name => IsNonEmptyFile(Path.Combine(data, name)));
            if (!IsNonEmptyFile(Path.Combine(output, ExecutableName)) || runtime == null || !hasDataPayload)
                throw new BuildFailedException("Windows Player output is incomplete (nonempty executable, TuanjiePlayer.dll or UnityPlayer.dll, and Data payload required).");
            return runtime;
        }

        private static bool IsNonEmptyFile(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

        private static string FileSha256(string path)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        [Serializable]
        internal sealed class PackageFile
        {
            public string path;
            public long bytes;
        }

        [Serializable]
        private sealed class BuildManifest
        {
            public int schemaVersion = 1;
            public string buildVersion;
            public string builtAtUtc;
            public string productName;
            public string bundleVersion;
            public string engineVersion;
            public string buildGuid;
            public string playerRuntimeLibrary;
            public int totalErrors;
            public int totalWarnings;
            public string target = "StandaloneWindows64";
            public string subtarget = "Player";
            public string buildOptions;
            public string executable = ExecutableName;
            public string[] playerOnlyDefines = { DemoDefine };
            public string[] productionScenes;
            public string[] excludedTopLevelDirectorySuffixes = NonShippingDirectorySuffixes.ToArray();
            public string contentManifestSha256;
            public long totalBytes;
            public List<PackageFile> files;
        }
    }
}
