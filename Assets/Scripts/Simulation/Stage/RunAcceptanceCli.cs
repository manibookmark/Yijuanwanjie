using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using WanjieScroll.Dungeon;
using WanjieScroll.Simulation.Balance;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns the main-acceptance process protocol and atomic evidence bundle.
    /// Normal defeats remain complete samples; limits and structural errors
    /// remain explicit nonzero outcomes with their available evidence retained.
    /// </summary>
    public static class RunAcceptanceCli
    {
        public static void Run()
        {
            string outcomePath = null;
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                Dictionary<string, string> args = Parse(Environment.GetCommandLineArgs());
                string output = Path.GetFullPath(Path.Combine(root, Required(args, "-outputDirectory")));
                RequireOutput(root, output);
                outcomePath = output + ".outcome.json";
                if (File.Exists(outcomePath)) throw new IOException("Outcome already exists.");
                string suiteFile = Path.GetFullPath(Path.Combine(root, Required(args, "-suiteFile")));
                if (!Inside(suiteFile, root)) throw new IOException("Suite is outside the project.");
                string suiteJson = File.ReadAllText(suiteFile);
                BalanceSimulationSuite suite = BalanceSimulationSuite.CompileJson(suiteJson, root);
                var settings = new RunAcceptanceSettings
                {
                    sliceId = args.TryGetValue("-sliceId", out string slice) ? slice : "g1.post-tutorial.no-upgrades",
                    objective = args.TryGetValue("-objective", out string objective)
                        ? (RunAcceptanceObjective)Enum.Parse(typeof(RunAcceptanceObjective), objective, true)
                        : RunAcceptanceObjective.Survival,
                    maxTurnsPerEncounter = Integer(args, "-maxTurns", 100),
                    maxNodeDecisions = Integer(args, "-maxNodeDecisions", 256),
                    searchDepth = Integer(args, "-searchDepth", 4), beamWidth = Integer(args, "-beamWidth", 6),
                    futureSamples = Integer(args, "-futureSamples", 3), maxExpansions = Integer(args, "-maxExpansions", 96),
                    validateReplay = !args.TryGetValue("-validateReplay", out string replay) || bool.Parse(replay)
                };
                int seedStart = Integer(args, "-seedStart", 2026090601), count = Integer(args, "-count", 1);
                var ledger = new List<BalanceLedgerEntry>();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                string floorId = suite.FloorContentId;
                while (!string.IsNullOrEmpty(floorId))
                {
                    if (!visited.Add(floorId)) throw new InvalidOperationException("Production floor chain contains a cycle.");
                    LevelRunProfile floor = LevelRunProfileCatalog.Get(floorId);
                    ledger.AddRange(BalanceContentLedger.Build(suite, floor));
                    floorId = floor.nextFloorContentId;
                }
                RunAcceptanceReport report = RunAcceptanceSession.Run(suite, seedStart, count, settings);
                report.contentFingerprint = BalanceContentLedger.BuildFingerprint(suite, ledger);
                string manifestHash = WriteAtomic(report, suiteJson, output);
                WriteOutcome(outcomePath, new Outcome { exitCode = report.exitCode, runId = report.runId,
                    status = report.exitCode == 0 ? "completed" : report.exitCode == 1 ? "simulation-limit" : "error",
                    manifestSha256 = manifestHash, error = report.errorCount > 0 ? "One or more seeds failed; see report." : null });
                Debug.Log("[MainAcceptance] samples=" + report.samples.Count + ", output=" + output);
                Exit(report.exitCode);
            }
            catch (Exception exception)
            {
                if (outcomePath != null && !File.Exists(outcomePath))
                    WriteOutcome(outcomePath, new Outcome { exitCode = 2, status = "error", error = exception.ToString() });
                Debug.LogException(exception);
                Exit(2);
            }
        }

        public static string WriteAtomic(RunAcceptanceReport report, string suiteJson, string output)
        {
            output = Path.GetFullPath(output);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Acceptance output already exists.");
            if (Hash(suiteJson) != report.sourceSuiteSha256 || report.samples.Count != report.requestedCount)
                throw new IOException("Acceptance source hash or sample count is inconsistent.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string temporary = output + ".tmp." + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(temporary);
            try
            {
                var manifest = new Manifest { runId = report.runId, exitCode = report.exitCode };
                for (int i = 0; i < report.samples.Count; i++)
                {
                    RunAcceptanceSample sample = report.samples[i];
                    sample.terminalPayload ??= "{}";
                    sample.initialAccountPayload ??= "{}";
                    sample.finalAccountPayload ??= "{}";
                    if (sample.seed != report.seedStart + i || sample.gameRunId != report.settings.GameRunId(sample.seed))
                        throw new IOException("Acceptance sample ordering or game identity is inconsistent.");
                    string prefix = "seeds/s" + sample.seed.ToString(CultureInfo.InvariantCulture) + "/";
                    for (int entryIndex = 0; entryIndex < sample.entries.Count; entryIndex++)
                    {
                        RunAcceptanceEntry entry = sample.entries[entryIndex];
                        if (string.IsNullOrEmpty(entry.payload)) throw new IOException("Reached entry has no Run payload.");
                        entry.checkpointPath = prefix + "entry-" + entryIndex.ToString("D2") + ".json";
                        entry.checkpointSha256 = Hash(entry.payload);
                        WriteFile(temporary, entry.checkpointPath, entry.payload, manifest);
                    }
                    for (int combatIndex = 0; combatIndex < sample.combats.Count; combatIndex++)
                    {
                        RunAcceptanceCombat combat = sample.combats[combatIndex];
                        if (string.IsNullOrEmpty(combat.tracePayload)) throw new IOException("Combat has no replay entry evidence.");
                        combat.tracePath = prefix + "combat-" + combatIndex.ToString("D3") + ".json";
                        combat.traceSha256 = Hash(combat.tracePayload);
                        WriteFile(temporary, combat.tracePath, combat.tracePayload, manifest);
                    }
                    sample.terminalPath = prefix + "terminal.json";
                    sample.terminalSha256 = Hash(sample.terminalPayload);
                    WriteFile(temporary, sample.terminalPath, sample.terminalPayload ?? "{}", manifest);
                    sample.initialAccountPath = prefix + "initial-account.json";
                    sample.initialAccountSha256 = Hash(sample.initialAccountPayload);
                    WriteFile(temporary, sample.initialAccountPath, sample.initialAccountPayload ?? "{}", manifest);
                    sample.finalAccountPath = prefix + "final-account.json";
                    sample.finalAccountSha256 = Hash(sample.finalAccountPayload);
                    WriteFile(temporary, sample.finalAccountPath, sample.finalAccountPayload ?? "{}", manifest);
                }
                WriteFile(temporary, "source-suite.json", suiteJson, manifest);
                WriteFile(temporary, "report.json", JsonUtility.ToJson(report, true), manifest);
                string json = JsonUtility.ToJson(manifest, true);
                File.WriteAllText(Path.Combine(temporary, "manifest.json"), json, new UTF8Encoding(false));
                Directory.Move(temporary, output);
                return Hash(json);
            }
            catch
            {
                if (Path.GetFullPath(temporary).StartsWith(output + ".tmp.", StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(temporary)) Directory.Delete(temporary, true);
                throw;
            }
        }

        private static void WriteFile(string root, string path, string payload, Manifest manifest)
        {
            string full = Path.GetFullPath(Path.Combine(root, path));
            if (!Inside(full, root) || File.Exists(full)) throw new IOException("Duplicate or escaping artifact path.");
            byte[] bytes = new UTF8Encoding(false).GetBytes(payload ?? string.Empty);
            Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllBytes(full, bytes);
            manifest.files.Add(new Artifact { path = path, size = bytes.LongLength, sha256 = Hash(payload) });
        }
        public static string Hash(string value) => BalanceDungeonSamplingCli.Sha256(value);

        public static string BuildContentFingerprint(BalanceSimulationSuite suite)
        {
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            var ledger = new List<BalanceLedgerEntry>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string floorId = suite.FloorContentId;
            while (!string.IsNullOrEmpty(floorId))
            {
                if (!visited.Add(floorId))
                    throw new InvalidOperationException("Production floor chain contains a cycle.");
                LevelRunProfile floor = LevelRunProfileCatalog.Get(floorId);
                ledger.AddRange(BalanceContentLedger.Build(suite, floor));
                floorId = floor.nextFloorContentId;
            }
            return BalanceContentLedger.BuildFingerprint(suite, ledger);
        }
        private static void WriteOutcome(string path, Outcome outcome)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, JsonUtility.ToJson(outcome, true), new UTF8Encoding(false));
            File.Move(temporary, path);
        }
        private static void RequireOutput(string root, string output)
        {
            if (!Inside(output, root)) throw new IOException("Output must be inside the project.");
            foreach (string name in new[] { "Assets", "Library", "Temp", "Logs", "UserSettings", ".git",
                         "SourceAssets", "ContentSource", "Packages", "ProjectSettings", "tools" })
            {
                string path = Path.Combine(root, name);
                if (output.Equals(path, StringComparison.OrdinalIgnoreCase) || Inside(output, path))
                    throw new IOException("Output is inside a protected project directory.");
            }
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists.");
        }
        private static bool Inside(string path, string parent) => Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static Dictionary<string, string> Parse(string[] values)
        {
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < values.Length - 1; i++) if (values[i].StartsWith("-")) args[values[i]] = values[i + 1];
            return args;
        }
        private static string Required(Dictionary<string, string> args, string key)
            => args.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value)
                ? value : throw new ArgumentException("Missing " + key);
        private static int Integer(Dictionary<string, string> args, string key, int fallback)
            => args.TryGetValue(key, out string value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
        private static void Exit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
        [Serializable] private sealed class Outcome
        { public int schemaVersion = 1, exitCode; public string status, runId, manifestSha256, error; }
        [Serializable] private sealed class Manifest
        { public int schemaVersion = 1, exitCode; public string runId; public List<Artifact> files = new List<Artifact>(); }
        [Serializable] private sealed class Artifact
        { public string path, sha256; public long size; }
    }
}
