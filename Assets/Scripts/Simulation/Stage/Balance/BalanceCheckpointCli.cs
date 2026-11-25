using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Batchmode adapter for generation up to the requested entry boundary. Files and
    /// outcome JSON are the process protocol; Unity logs are diagnostics only.
    /// </summary>
    public static class BalanceCheckpointCli
    {
        public static void Run()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            Dictionary<string, string> args = ParseArgs(
                Environment.GetCommandLineArgs());
            string outcomePath = null;
            try
            {
                outcomePath = ResolvePath(
                    projectRoot,
                    Require(args, "-outcomeFile"));
                string recipePath = ResolvePath(
                    projectRoot,
                    Require(args, "-recipeFile"));
                string outputPath = ResolvePath(
                    projectRoot,
                    Require(args, "-outputDirectory"));
                ValidateInputPath(projectRoot, recipePath, outputPath);

                string recipeJson = File.ReadAllText(recipePath);
                BalanceCheckpointRecipe recipe =
                    BalanceCheckpointRecipe.CompileJson(
                        recipeJson,
                        projectRoot);
                int seedSearchCount = ReadPositiveInt(
                    args,
                    "-seedSearchCount",
                    1);
                Debug.Log(
                    "[BalanceCheckpoint] recipe=" + recipe.RecipeId + "@" +
                    recipe.Revision + ", seed=" + recipe.Seed +
                    ", seedSearchCount=" + seedSearchCount +
                    ", output=" + outputPath);
                BalanceCheckpointGenerationResult result =
                    GenerateWithSeedSearch(recipe, seedSearchCount);
                WriteAtomic(
                    result,
                    recipeJson,
                    outputPath);
                WriteOutcome(
                    outcomePath,
                    0,
                    "completed",
                    outputPath,
                    recipe.RecipeId,
                    string.Empty);
                Debug.Log(
                    "[BalanceCheckpoint] generated=" +
                    result.Artifacts.Count + ", entrySeed=" + result.Seed +
                    ", scope=" + result.Scope +
                    ", output=" + outputPath);
            }
            catch (Exception exception)
            {
                WriteOutcome(
                    outcomePath,
                    2,
                    "error",
                    string.Empty,
                    string.Empty,
                    exception.Message);
                throw;
            }
        }

        internal static BalanceCheckpointGenerationResult
            GenerateWithSeedSearch(
                BalanceCheckpointRecipe recipe,
                int seedSearchCount)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (seedSearchCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seedSearchCount));
            }
            if (seedSearchCount == 1)
                return BalanceCheckpointGenerator.GenerateEntries(recipe);

            BalanceCheckpointException lastOutcome = null;
            for (int offset = 0; offset < seedSearchCount; offset++)
            {
                int seed;
                try
                {
                    seed = checked(recipe.Seed + offset);
                }
                catch (OverflowException exception)
                {
                    throw new BalanceCheckpointException(
                        "Checkpoint seed search range exceeds Int32.",
                        exception);
                }

                try
                {
                    return BalanceCheckpointGenerator.GenerateEntries(
                        recipe.WithSeed(seed));
                }
                catch (BalanceCheckpointException exception)
                    when (IsSearchableSeedOutcome(exception))
                {
                    lastOutcome = exception;
                    Debug.LogWarning(
                        "[BalanceCheckpoint] seed=" + seed +
                        " did not complete: " + exception.Message);
                }
            }

            throw new BalanceCheckpointException(
                "No seed reached every requested entry in range " + recipe.Seed +
                ".." + checked(recipe.Seed + seedSearchCount - 1) + ". " +
                "Last outcome: " + lastOutcome?.Message,
                lastOutcome);
        }

        internal static bool IsSearchableSeedOutcome(
            BalanceCheckpointException exception)
            => exception is BalanceCheckpointCombatOutcomeException outcome &&
               (outcome.Termination == SimulationTerminationReason.Defeat ||
                outcome.Termination == SimulationTerminationReason.MaxTurns);

        private static void WriteAtomic(
            BalanceCheckpointGenerationResult result,
            string recipeJson,
            string outputPath)
        {
            string parent = Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException(
                    "Checkpoint output has no parent directory.");
            Directory.CreateDirectory(parent);
            string temporary = outputPath + ".tmp." +
                Guid.NewGuid().ToString("N");
            if (Directory.Exists(temporary) || File.Exists(temporary))
            {
                throw new IOException(
                    "Temporary checkpoint output already exists: " + temporary);
            }
            Directory.CreateDirectory(temporary);
            try
            {
                var names = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                var entries = new List<BalanceCheckpointManifestEntry>();
                for (int i = 0; i < result.Artifacts.Count; i++)
                {
                    BalanceCheckpointArtifact artifact = result.Artifacts[i];
                    string name = Path.GetFileName(artifact.CheckpointFile);
                    if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
                    {
                        throw new InvalidOperationException(
                            "Checkpoint output filenames must be unique: " + name);
                    }
                    byte[] bytes = new UTF8Encoding(false).GetBytes(
                        artifact.Payload);
                    File.WriteAllBytes(Path.Combine(temporary, name), bytes);
                    entries.Add(new BalanceCheckpointManifestEntry
                    {
                        floorContentId = artifact.FloorContentId,
                        snapshotId = artifact.SnapshotId,
                        checkpointFile = artifact.CheckpointFile,
                        artifactPath = name,
                        sha256 = artifact.Sha256,
                        size = bytes.LongLength
                    });
                }
                var manifest = new BalanceCheckpointManifest
                {
                    schemaVersion = 1,
                    recipeId = result.RecipeId,
                    recipeRevision = result.RecipeRevision,
                    seed = result.Seed,
                    scope = result.Scope.ToString(),
                    recipeSha256 = ComputeSha256(recipeJson),
                    sourceSuiteSha256 = result.SourceSuiteSha256,
                    artifacts = entries.ToArray()
                };
                File.WriteAllText(
                    Path.Combine(temporary, "manifest.json"),
                    JsonUtility.ToJson(manifest, true),
                    new UTF8Encoding(false));
                Directory.Move(temporary, outputPath);
            }
            catch
            {
                if (Directory.Exists(temporary))
                    Directory.Delete(temporary, true);
                throw;
            }
        }

        private static void ValidateInputPath(
            string projectRoot,
            string recipePath,
            string outputPath)
        {
            if (!IsWithin(recipePath, projectRoot) || !File.Exists(recipePath))
            {
                throw new FileNotFoundException(
                    "Checkpoint recipe must be an existing project file.",
                    recipePath);
            }
            if (!IsWithin(outputPath, projectRoot))
            {
                throw new InvalidOperationException(
                    "Checkpoint output must stay inside projectRoot.");
            }
            foreach (string protectedName in new[]
                     {
                         "Assets", "Library", "Temp", "Logs", "UserSettings"
                     })
            {
                if (IsWithin(
                        outputPath,
                        Path.Combine(projectRoot, protectedName)))
                {
                    throw new InvalidOperationException(
                        "Checkpoint output cannot be under project " +
                        protectedName + ".");
                }
            }
            if (Directory.Exists(outputPath) || File.Exists(outputPath))
            {
                throw new IOException(
                    "Checkpoint output already exists: " + outputPath);
            }
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (!key.StartsWith("-", StringComparison.Ordinal)) continue;
                if (i + 1 >= args.Length ||
                    args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }
                result[key] = args[++i];
            }
            return result;
        }

        private static string Require(
            IReadOnlyDictionary<string, string> args,
            string key)
        {
            if (args.TryGetValue(key, out string value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
            throw new ArgumentException("Missing required argument: " + key + ".");
        }

        private static int ReadPositiveInt(
            IReadOnlyDictionary<string, string> args,
            string key,
            int fallback)
        {
            if (!args.TryGetValue(key, out string text)) return fallback;
            if (int.TryParse(
                    text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int value) && value > 0)
            {
                return value;
            }
            throw new ArgumentException(
                key + " must be a positive Int32, got " + text + ".");
        }

        private static string ResolvePath(string projectRoot, string value)
            => Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(projectRoot, value));

        private static bool IsWithin(string path, string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256(string value)
        {
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(
                new UTF8Encoding(false).GetBytes(value ?? string.Empty));
            var text = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
            {
                text.Append(digest[i].ToString(
                    "x2",
                    CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        private static void WriteOutcome(
            string path,
            int exitCode,
            string status,
            string outputDirectory,
            string recipeId,
            string message)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);
                File.WriteAllText(
                    path,
                    JsonUtility.ToJson(new BalanceCheckpointOutcome
                    {
                        schemaVersion = 1,
                        exitCode = exitCode,
                        status = status,
                        outputDirectory = outputDirectory,
                        recipeId = recipeId,
                        message = message
                    }, true),
                    new UTF8Encoding(false));
            }
            catch (Exception outcomeError)
            {
                Debug.LogError(
                    "[BalanceCheckpoint] Cannot write outcome: " +
                    outcomeError.Message);
            }
        }

        [Serializable]
        private sealed class BalanceCheckpointManifest
        {
            public int schemaVersion;
            public string recipeId;
            public int recipeRevision;
            public int seed;
            public string scope;
            public string recipeSha256;
            public string sourceSuiteSha256;
            public BalanceCheckpointManifestEntry[] artifacts;
        }

        [Serializable]
        private sealed class BalanceCheckpointManifestEntry
        {
            public string floorContentId;
            public string snapshotId;
            public string checkpointFile;
            public string artifactPath;
            public string sha256;
            public long size;
        }

        [Serializable]
        private sealed class BalanceCheckpointOutcome
        {
            public int schemaVersion;
            public int exitCode;
            public string status;
            public string outputDirectory;
            public string recipeId;
            public string message;
        }
    }
}
