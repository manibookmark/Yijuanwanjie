using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WanjieScroll.Simulation.Host;

namespace WanjieScroll.EditorTools
{
    /// <summary>
    /// Explicit simulation build profile. It freezes all runtime Resources,
    /// rule sources/settings and account recipes, builds one empty scene with a
    /// per-build define, then binds the installed binaries into an immutable ID.
    /// It never replaces the production Build Settings or global player symbols.
    /// </summary>
    public static class AgentSimulationPlayerBuilder
    {
        private const string TemporaryFolder = "Assets/AgentSimulationBuild";
        private const string ExecutableName = "WanjieScroll.AgentPlayer.exe";
        public const string SimulationDefine = "WANJIE_SIMULATION_HOST";
        internal static bool IsBuilding { get; private set; }

        public static string Build(string outputRoot = null)
        {
            if (IsBuilding || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build only from a stable Edit-mode window.");
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone) != ScriptingImplementation.Mono2x)
                throw new InvalidOperationException("Dedicated simulation requires the existing Standalone Mono backend; player settings were not changed.");
            if (AssetDatabase.IsValidFolder(TemporaryFolder) || Directory.Exists(TemporaryFolder))
                throw new IOException("Reserved simulation build scene directory already exists.");
            string project = Path.GetDirectoryName(Application.dataPath);
            string collection = Path.GetFullPath(outputRoot ?? Path.Combine(project, "SimulationResults/AgentPlayer"));
            RequireOutput(project, collection);
            Directory.CreateDirectory(collection);
            string pending = Path.Combine(collection, ".building-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pending);
            string resultPath = Path.Combine(collection, "build-result.json");
            bool createdSceneFolder = false;
            try
            {
                ContentPackageGenerator.EnsureCurrentPackage();
                List<string> errors = ProjectBuildSettings.CollectSimulationErrors();
                if (errors.Count > 0) throw new BuildFailedException(string.Join(Environment.NewLine, errors));
                List<AgentSimulationPackageFile> sourceFiles = CaptureSourceFiles(project);
                string sourceDigest = SourceDigest(sourceFiles);
                string frozenRecipeFolder = Path.Combine(pending, "SimulationInputs/account-recipes");
                Directory.CreateDirectory(frozenRecipeFolder);
                foreach (string recipe in Directory.GetFiles(Path.Combine(project, "tools/simulation/account-recipes"), "*.json"))
                    File.Copy(recipe, Path.Combine(frozenRecipeFolder, Path.GetFileName(recipe)), false);

                AssetDatabase.CreateFolder("Assets", "AgentSimulationBuild");
                createdSceneFolder = true;
                string scenePath = TemporaryFolder + "/Empty.unity";
                Scene prior = SceneManager.GetActiveScene();
                // A cold batch Editor has an unsaved untitled scene; Unity
                // refuses an additive scene in that state. Persist only that
                // batch-owned empty scene before creating the build scene.
                // An interactive Editor's unsaved work is never saved here.
                if (Application.isBatchMode && prior.IsValid() && string.IsNullOrEmpty(prior.path))
                {
                    string startupPath = TemporaryFolder + "/BatchStartup.unity";
                    if (!EditorSceneManager.SaveScene(prior, startupPath))
                        throw new IOException("Could not save the batch-owned startup scene.");
                }
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                    if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Could not save the isolated empty build scene.");
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
                }

                IsBuilding = true;
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath }, locationPathName = Path.Combine(pending, ExecutableName),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development | BuildOptions.StrictMode | BuildOptions.CompressWithLz4,
                    extraScriptingDefines = new[] { SimulationDefine }
                });
                if (report == null || report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("Dedicated Player build did not succeed.");
                if (SourceDigest(CaptureSourceFiles(project)) != sourceDigest)
                    throw new BuildFailedException("Development inputs changed during the build; this package was not published.");

                var manifest = new AgentSimulationPackageManifest
                {
                    schemaVersion = AgentSimulationPackage.SchemaVersion, engineVersion = Application.unityVersion,
                    sourceDigest = sourceDigest, playerExecutable = ExecutableName, sourceFiles = sourceFiles,
                    files = CaptureFiles(pending, Directory.GetFiles(pending, "*", SearchOption.AllDirectories))
                };
                manifest.buildId = AgentSimulationPackage.ComputeBuildId(manifest);
                File.WriteAllText(Path.Combine(pending, AgentSimulationPackage.ManifestRelativePath),
                    JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
                AgentSimulationPackage.LoadVerified(pending);
                string published = Path.Combine(collection, manifest.buildId);
                if (Directory.Exists(published)) throw new IOException("This immutable build already exists; it was not overwritten.");
                Directory.Move(pending, published);
                WriteResult(resultPath, new BuildOutcome { success = true, buildId = manifest.buildId,
                    packageRoot = published, manifestPath = Path.Combine(published, AgentSimulationPackage.ManifestRelativePath),
                    executablePath = Path.Combine(published, ExecutableName), engineVersion = manifest.engineVersion });
                WriteResult(Path.Combine(collection, "latest.json"), new BuildOutcome { success = true,
                    buildId = manifest.buildId, packageRoot = published,
                    manifestPath = Path.Combine(published, AgentSimulationPackage.ManifestRelativePath),
                    executablePath = Path.Combine(published, ExecutableName), engineVersion = manifest.engineVersion });
                Debug.Log("[AgentPlayerBuild] Published immutable dedicated Player: " + manifest.buildId);
                return published;
            }
            catch (Exception exception)
            {
                WriteResult(resultPath, new BuildOutcome { success = false, error = exception.GetType().Name + ": " + exception.Message });
                throw;
            }
            finally
            {
                IsBuilding = false;
                // Only the literal reserved folder created by this invocation is removed.
                // Failed package evidence remains outside Assets for diagnosis.
                if (createdSceneFolder && !AssetDatabase.DeleteAsset(TemporaryFolder))
                    Debug.LogWarning("[AgentPlayerBuild] Temporary empty-scene cleanup needs attention.");
            }
        }

        public static void BuildForBatch()
        {
            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
                int position = Array.IndexOf(arguments, "-agentPlayerOutputRoot");
                Build(position >= 0 && position + 1 < arguments.Length ? arguments[position + 1] : null);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("[AgentPlayerBuild] " + exception.GetType().Name + ": " + exception.Message);
                EditorApplication.Exit(2);
            }
        }

        private static List<AgentSimulationPackageFile> CaptureSourceFiles(string project)
        {
            var paths = new List<string>();
            foreach (string folder in new[] { "Assets/Scripts", "Assets/Resources", "ContentSource", "ProjectSettings", "tools/simulation/account-recipes" })
                paths.AddRange(Directory.GetFiles(Path.Combine(project, folder), "*", SearchOption.AllDirectories));
            foreach (string filename in new[] { "Packages/manifest.json", "Packages/packages-lock.json" })
                if (File.Exists(Path.Combine(project, filename))) paths.Add(Path.Combine(project, filename));
            return CaptureFiles(project, paths);
        }

        private static List<AgentSimulationPackageFile> CaptureFiles(string root, IEnumerable<string> paths)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return paths.Select(path => new AgentSimulationPackageFile
            {
                path = Path.GetFullPath(path).Substring(prefix.Length).Replace('\\', '/'),
                bytes = new FileInfo(path).Length, sha256 = AgentSimulationPackage.HashFile(path)
            }).OrderBy(file => file.path, StringComparer.Ordinal).ToList();
        }

        private static string SourceDigest(IEnumerable<AgentSimulationPackageFile> files) => AgentSimulationPackage.ComputeSourceDigest(files);

        private static void RequireOutput(string project, string output)
        {
            string allowed = Path.GetFullPath(Path.Combine(project, "SimulationResults/AgentPlayer"));
            if (!output.Equals(allowed, StringComparison.OrdinalIgnoreCase) &&
                !output.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Simulation build output must stay within SimulationResults/AgentPlayer.");
        }

        private static void WriteResult(string path, BuildOutcome outcome)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(outcome, true), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        [Serializable]
        private sealed class BuildOutcome
        {
            public bool success;
            public string buildId, packageRoot, manifestPath, executablePath, engineVersion, error;
        }
    }
}
