using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Thin batchmode adapter. Machine-readable outcome.json is the process
    /// protocol even when validation or execution fails before a report exists.
    /// Unity logs remain diagnostics only.
    /// </summary>
    public static class BalanceSimulationCli
    {
        public static void Run()
        {
            string projectRoot = Path.GetFullPath(
                Path.Combine(Application.dataPath, ".."));
            Dictionary<string, string> args = ParseArgs(
                Environment.GetCommandLineArgs());
            string outcomePath = null;
            try
            {
                outcomePath = ResolvePath(
                    projectRoot,
                    Require(args, "-outcomeFile"));
                ExecutionResult result = Execute(projectRoot, args);
                WriteOutcome(
                    outcomePath,
                    result.Report.exitCode == 0 ? "completed" : "gate",
                    result.Report.exitCode,
                    result.Report.status,
                    result.OutputDirectory,
                    result.Report.suiteId,
                    string.Empty);
                if (result.Report.exitCode != 0)
                {
                    throw new BalanceSimulationGateException(
                        result.Report.exitCode,
                        "Balance simulation completed with status " +
                        result.Report.status + ". See " +
                        result.SummaryPath);
                }
            }
            catch (BalanceSimulationGateException)
            {
                throw;
            }
            catch (BalanceSimulationInputException exception)
            {
                WriteOutcome(
                    outcomePath,
                    "error",
                    2,
                    "input_error",
                    string.Empty,
                    string.Empty,
                    exception.Message);
                throw;
            }
            catch (BalanceSimulationExecutionException exception)
            {
                WriteOutcome(
                    outcomePath,
                    "error",
                    3,
                    "simulation_error",
                    string.Empty,
                    string.Empty,
                    exception.Message);
                throw;
            }
            catch (BalanceSimulationExportException exception)
            {
                WriteOutcome(
                    outcomePath,
                    "error",
                    5,
                    "export_error",
                    string.Empty,
                    string.Empty,
                    exception.Message);
                throw;
            }
            catch (Exception exception)
            {
                WriteOutcome(
                    outcomePath,
                    "error",
                    3,
                    "unexpected_error",
                    string.Empty,
                    string.Empty,
                    exception.Message);
                throw;
            }
        }

        private static ExecutionResult Execute(
            string projectRoot,
            IReadOnlyDictionary<string, string> args)
        {
            BalanceSimulationSuite suite;
            BalanceRunOptions options;
            string outputPath;
            try
            {
                string suiteArgument = Require(args, "-suiteFile");
                string suitePath = ResolvePath(projectRoot, suiteArgument);
                if (!File.Exists(suitePath))
                {
                    throw new FileNotFoundException(
                        "Balance suite not found.",
                        suitePath);
                }
                suite = BalanceSimulationSuite.CompileJson(
                    File.ReadAllText(suitePath),
                    projectRoot);

                string presetText = Get(
                    args,
                    "-preset",
                    BalanceRunPreset.Quick.ToString());
                if (!Enum.TryParse(
                        presetText,
                        true,
                        out BalanceRunPreset preset) ||
                    !Enum.IsDefined(typeof(BalanceRunPreset), preset))
                {
                    throw new ArgumentException(
                        "Unknown balance preset: " + presetText + ".");
                }

                options = new BalanceRunOptions(
                    preset,
                    GetInt(args, "-seedStart", 1),
                    GetInt(args, "-count", 0),
                    GetInt(
                        args,
                        "-maxTurnsPerEncounter",
                        BalanceRunOptions.DefaultMaxTurnsPerEncounter),
                    GetBool(args, "-strict", false));

                string outputArgument = Get(
                    args,
                    "-outputDirectory",
                    null);
                outputPath = string.IsNullOrWhiteSpace(outputArgument)
                    ? BuildDefaultOutputPath(projectRoot, suite, preset)
                    : ResolvePath(projectRoot, outputArgument);
                ValidateOutputPath(projectRoot, outputPath);
                if (Directory.Exists(outputPath) || File.Exists(outputPath))
                {
                    throw new IOException(
                        "Output path already exists: " + outputPath);
                }
            }
            catch (Exception exception)
            {
                throw new BalanceSimulationInputException(
                    exception.Message,
                    exception);
            }

            Debug.Log(
                "[BalanceSimulation] suite=" + suite.SuiteId + "@" +
                suite.Revision + ", preset=" + options.Preset + ", seeds=" +
                options.SeedStart + "-" +
                (options.SeedStart + options.Count - 1) + ", output=" +
                outputPath);

            BalanceSimulationReport report;
            try
            {
                report = BalanceSimulationEngine.Run(suite, options);
            }
            catch (BalanceSuiteException exception)
            {
                throw new BalanceSimulationInputException(
                    exception.Message,
                    exception);
            }
            catch (Exception exception)
            {
                throw new BalanceSimulationExecutionException(
                    exception.Message,
                    exception);
            }

            report.gitCommit = Get(args, "-gitCommit", string.Empty);
            report.gitDirtyFingerprint = Get(
                args,
                "-gitDirtyFingerprint",
                string.Empty);

            BalanceReportExportResult exported;
            try
            {
                exported = BalanceSimulationExport.WriteAtomic(
                    report,
                    outputPath,
                    projectRoot);
            }
            catch (Exception exception)
            {
                throw new BalanceSimulationExportException(
                    exception.Message,
                    exception);
            }

            Debug.Log(
                "[BalanceSimulation] status=" + report.status +
                ", summary=" + exported.SummaryPath);
            return new ExecutionResult(
                report,
                exported.OutputDirectory,
                exported.SummaryPath);
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (!key.StartsWith("-", StringComparison.Ordinal)) continue;
                if (i + 1 < args.Length &&
                    !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    result[key] = args[++i];
                }
                else
                {
                    result[key] = "true";
                }
            }
            return result;
        }

        private static string Require(
            IReadOnlyDictionary<string, string> args,
            string key)
        {
            string value = Get(args, key, null);
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Missing required argument " + key + ".");
            return value;
        }

        private static string Get(
            IReadOnlyDictionary<string, string> args,
            string key,
            string fallback)
        {
            return args.TryGetValue(key, out string value) ? value : fallback;
        }

        private static int GetInt(
            IReadOnlyDictionary<string, string> args,
            string key,
            int fallback)
        {
            if (!args.TryGetValue(key, out string text)) return fallback;
            if (int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                return value;
            }
            throw new ArgumentException(
                key + " must be an integer, got " + text + ".");
        }

        private static bool GetBool(
            IReadOnlyDictionary<string, string> args,
            string key,
            bool fallback)
        {
            if (!args.TryGetValue(key, out string text)) return fallback;
            if (bool.TryParse(text, out bool value)) return value;
            throw new ArgumentException(
                key + " must be true or false, got " + text + ".");
        }

        private static string ResolvePath(string projectRoot, string path)
        {
            return TrimTrailingSeparators(Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectRoot, path)));
        }

        private static string TrimTrailingSeparators(string path)
        {
            string root = Path.GetPathRoot(path);
            string result = path;
            while (result.Length > root.Length &&
                   (result.EndsWith(
                        Path.DirectorySeparatorChar.ToString(),
                        StringComparison.Ordinal) ||
                    result.EndsWith(
                        Path.AltDirectorySeparatorChar.ToString(),
                        StringComparison.Ordinal)))
            {
                result = result.Substring(0, result.Length - 1);
            }
            return result;
        }

        private static void ValidateOutputPath(
            string projectRoot,
            string outputPath)
        {
            string root = TrimTrailingSeparators(
                Path.GetFullPath(projectRoot));
            string candidate = TrimTrailingSeparators(
                Path.GetFullPath(outputPath));
            if (string.Equals(
                    root,
                    candidate,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Output directory cannot be the project root.");
            }
            string[] protectedDirectories =
            {
                "Assets", "Library", "Temp", "Logs", "UserSettings"
            };
            for (int i = 0; i < protectedDirectories.Length; i++)
            {
                string protectedPath = Path.Combine(
                    root,
                    protectedDirectories[i]);
                if (IsWithin(candidate, protectedPath))
                {
                    throw new ArgumentException(
                        "Output directory cannot be inside project " +
                        protectedDirectories[i] + ".");
                }
            }
        }

        private static bool IsWithin(string path, string directory)
        {
            string parent = TrimTrailingSeparators(
                Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(path);
            return candidate.StartsWith(
                parent,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildDefaultOutputPath(
            string projectRoot,
            BalanceSimulationSuite suite,
            BalanceRunPreset preset)
        {
            string runId = DateTime.UtcNow.ToString(
                "yyyyMMdd_HHmmss_fff",
                CultureInfo.InvariantCulture);
            return Path.Combine(
                projectRoot,
                "SimulationResults",
                SafePathSegment(suite.FloorContentId),
                SafePathSegment(suite.SuiteId),
                preset.ToString().ToLowerInvariant(),
                runId);
        }

        private static string SafePathSegment(string value)
        {
            var result = new char[value.Length];
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                result[i] = Array.IndexOf(invalid, current) >= 0
                    ? '_'
                    : current;
            }
            string segment = new string(result);
            if (segment == "." || segment == ".." ||
                segment.EndsWith(".", StringComparison.Ordinal) ||
                segment.EndsWith(" ", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Unsafe output path segment: " + value + ".");
            }
            return segment;
        }

        private static void WriteOutcome(
            string path,
            string state,
            int exitCode,
            string status,
            string outputDirectory,
            string suiteId,
            string message)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Debug.LogError(
                    "[BalanceSimulation] Cannot write outcome: " + message);
                return;
            }
            try
            {
                string parent = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(parent))
                    throw new IOException("Outcome path has no parent.");
                Directory.CreateDirectory(parent);
                var outcome = new BalanceProcessOutcome
                {
                    schemaVersion = 1,
                    state = state,
                    exitCode = exitCode,
                    status = status,
                    outputDirectory = outputDirectory,
                    suiteId = suiteId,
                    message = message
                };
                string staging = path + ".tmp-" +
                    Guid.NewGuid().ToString("N");
                File.WriteAllText(
                    staging,
                    JsonUtility.ToJson(outcome, true) + Environment.NewLine,
                    new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(staging, path);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[BalanceSimulation] Cannot write outcome " + path +
                    ": " + exception.Message);
            }
        }

        private sealed class ExecutionResult
        {
            public ExecutionResult(
                BalanceSimulationReport report,
                string outputDirectory,
                string summaryPath)
            {
                Report = report;
                OutputDirectory = outputDirectory;
                SummaryPath = summaryPath;
            }

            public BalanceSimulationReport Report { get; }
            public string OutputDirectory { get; }
            public string SummaryPath { get; }
        }

        [Serializable]
        private sealed class BalanceProcessOutcome
        {
            public int schemaVersion;
            public string state;
            public int exitCode;
            public string status;
            public string outputDirectory;
            public string suiteId;
            public string message;
        }

        private sealed class BalanceSimulationInputException : Exception
        {
            public BalanceSimulationInputException(
                string message,
                Exception inner)
                : base(message, inner) { }
        }

        private sealed class BalanceSimulationExecutionException : Exception
        {
            public BalanceSimulationExecutionException(
                string message,
                Exception inner)
                : base(message, inner) { }
        }

        private sealed class BalanceSimulationExportException : Exception
        {
            public BalanceSimulationExportException(
                string message,
                Exception inner)
                : base(message, inner) { }
        }
    }

    public sealed class BalanceSimulationGateException : Exception
    {
        public BalanceSimulationGateException(int exitCode, string message)
            : base(message)
        {
            ExitCode = exitCode;
        }

        public int ExitCode { get; }
    }
}
