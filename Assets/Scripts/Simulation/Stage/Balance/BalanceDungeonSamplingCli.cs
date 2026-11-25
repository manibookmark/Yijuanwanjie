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
    /// Owns the sampling process protocol and atomic evidence bundle. The
    /// directory is published only after every payload and hash is written;
    /// outcome JSON, never log text, determines the launcher result.
    /// </summary>
    public static class BalanceDungeonSamplingCli
    {
        public static void Run()
        {
            string outcomePath = null;
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                Dictionary<string, string> args = ParseArgs(Environment.GetCommandLineArgs());
                string output = Resolve(root, Require(args, "-outputDirectory"));
                RequireOutputPath(root, output);
                outcomePath = output + ".outcome.json";
                if (File.Exists(outcomePath))
                    throw new IOException("Sampling outcome already exists: " + outcomePath);
                string recipePath = Resolve(root, Require(args, "-recipeFile"));
                if (!IsWithin(recipePath, root) || !File.Exists(recipePath))
                    throw new IOException("Sampling recipe must be a project file.");
                string recipeJson = File.ReadAllText(recipePath);
                BalanceCheckpointRecipe recipe = BalanceCheckpointRecipe.CompileJson(recipeJson, root);
                int seedStart = ReadInt(args, "-seedStart", recipe.Seed);
                int count = ReadInt(args, "-count", 100);
                string suiteJson = File.ReadAllText(Resolve(root, recipe.SourceSuiteFile));
                BalanceDungeonSamplingReport report = BalanceDungeonSampler.Run(recipe, seedStart, count);
                // Reuse the production input ledger across every declared floor
                // so future content changes can be distinguished from agent changes.
                var ledger = new List<BalanceLedgerEntry>();
                foreach (BalanceCheckpointFloorPlan floor in recipe.Floors)
                    ledger.AddRange(BalanceContentLedger.Build(recipe.SourceSuite,
                        WanjieScroll.Dungeon.LevelRunProfileCatalog.Get(floor.FloorContentId)));
                report.provenance.contentFingerprint =
                    BalanceContentLedger.BuildFingerprint(recipe.SourceSuite, ledger);
                string manifestSha = WriteAtomic(report, recipeJson, suiteJson, output);
                WriteOutcome(outcomePath, new SamplingOutcome
                {
                    exitCode = report.exitCode,
                    status = report.exitCode == 0 ? "completed" : "simulation-limit",
                    runId = report.runId,
                    manifestSha256 = manifestSha
                });
                Debug.Log("[DungeonSampling] samples=" + report.samples.Count +
                    ", limits=" + report.simulationLimitCount + ", output=" + output);
                if (report.exitCode != 0)
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.Exit(report.exitCode);
#else
                    Application.Quit(report.exitCode);
#endif
                }
            }
            catch (Exception exception)
            {
                if (outcomePath != null && !File.Exists(outcomePath))
                    WriteOutcome(outcomePath, new SamplingOutcome
                    {
                        exitCode = 2, status = "error", error = exception.ToString()
                    });
                throw;
            }
        }

        internal static string WriteAtomic(
            BalanceDungeonSamplingReport report, string recipeJson,
            string sourceSuiteJson, string outputPath)
        {
            outputPath = Path.GetFullPath(outputPath);
            if (Directory.Exists(outputPath) || File.Exists(outputPath))
                throw new IOException("Sampling output already exists: " + outputPath);
            if (Sha256(recipeJson) != report.provenance.recipeSha256 ||
                Sha256(sourceSuiteJson) != report.provenance.sourceSuiteSha256)
                throw new IOException("Sampling source hash changed during execution.");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            string temporary = outputPath + ".tmp." + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(temporary);
            try
            {
                var manifest = new SamplingManifest
                {
                    runId = report.runId,
                    exitCode = report.exitCode
                };
                foreach (BalanceDungeonSeedSample sample in report.samples)
                {
                    foreach (BalanceDungeonEntrySample entry in sample.entries)
                    {
                        if (Sha256(entry.payload) != entry.checkpointSha256)
                            throw new IOException("Entry payload hash mismatch: " + entry.checkpointPath);
                        WriteFile(temporary, entry.checkpointPath, entry.payload, manifest);
                    }
                    sample.terminalPath = "seeds/s" + sample.seed.ToString(
                        CultureInfo.InvariantCulture) + "/terminal.json";
                    sample.terminalSha256 = Sha256(sample.terminalPayload);
                    WriteFile(temporary, sample.terminalPath, sample.terminalPayload, manifest);
                }
                WriteFile(temporary, "recipe.json", recipeJson, manifest);
                WriteFile(temporary, "source-suite.json", sourceSuiteJson, manifest);
                WriteFile(temporary, "report.json", JsonUtility.ToJson(report, true), manifest);
                string manifestJson = JsonUtility.ToJson(manifest, true);
                File.WriteAllText(Path.Combine(temporary, "manifest.json"), manifestJson,
                    new UTF8Encoding(false));
                Directory.Move(temporary, outputPath);
                return Sha256(manifestJson);
            }
            catch
            {
                // Only this invocation's verified sibling staging directory.
                if (Path.GetFullPath(temporary).StartsWith(outputPath + ".tmp.",
                        StringComparison.OrdinalIgnoreCase) && Directory.Exists(temporary))
                    Directory.Delete(temporary, true);
                throw;
            }
        }

        private static void WriteFile(
            string directory, string relativePath, string payload, SamplingManifest manifest)
        {
            string full = Path.GetFullPath(Path.Combine(directory, relativePath));
            if (!IsWithin(full, directory)) throw new IOException("Artifact path escapes output.");
            if (File.Exists(full)) throw new IOException("Duplicate sampling artifact: " + relativePath);
            byte[] bytes = new UTF8Encoding(false).GetBytes(payload ?? string.Empty);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, bytes);
            manifest.files.Add(new SamplingFile
            {
                path = relativePath, sha256 = Sha256(payload), size = bytes.LongLength
            });
        }

        internal static string Sha256(string value)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false)
                .GetBytes(value ?? string.Empty))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void WriteOutcome(string path, SamplingOutcome outcome)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, JsonUtility.ToJson(outcome, true), new UTF8Encoding(false));
            File.Move(temporary, path);
        }

        private static void RequireOutputPath(string root, string output)
        {
            if (!IsWithin(output, root)) throw new IOException("Output must stay inside project.");
            foreach (string name in new[] { "Assets", "Library", "Temp", "Logs", "UserSettings",
                         ".git", "SourceAssets", "ContentSource", "Packages", "ProjectSettings", "tools" })
            {
                string protectedPath = Path.Combine(root, name);
                if (output.Equals(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                    IsWithin(output, protectedPath))
                    throw new IOException("Sampling output is inside a protected project directory.");
            }
            if (Directory.Exists(output) || File.Exists(output))
                throw new IOException("Sampling output already exists.");
        }

        private static bool IsWithin(string path, string parent)
            => Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static string Resolve(string root, string path)
            => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

        private static string Require(Dictionary<string, string> args, string key)
            => args.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Missing " + key + ".");

        private static int ReadInt(Dictionary<string, string> args, string key, int fallback)
            => args.TryGetValue(key, out string value)
                ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].StartsWith("-", StringComparison.Ordinal)) result[args[i]] = args[i + 1];
            return result;
        }

        [Serializable]
        private sealed class SamplingOutcome
        {
            public int schemaVersion = 1;
            public int exitCode;
            public string status, runId, manifestSha256, error;
        }

        [Serializable]
        private sealed class SamplingManifest
        {
            public int schemaVersion = 1;
            public string runId;
            public int exitCode;
            public List<SamplingFile> files = new List<SamplingFile>();
        }

        [Serializable]
        private sealed class SamplingFile
        {
            public string path, sha256;
            public long size;
        }
    }
}
