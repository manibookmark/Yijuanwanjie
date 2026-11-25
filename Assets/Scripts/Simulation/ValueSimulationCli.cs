using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Simulation.Agents;

namespace WanjieScroll.Simulation
{
    /// <summary>
    /// Headless value simulation entry.
    /// Launch from editor CLI:
    ///   -executeMethod WanjieScroll.Simulation.ValueSimulationCli.Run
    ///   -scenarioRange 1-7 -count 300 -agent standard -resultFile "SimulationResults/value-sim.csv"
    /// </summary>
    public static class ValueSimulationCli
    {
        private const string DefaultScenarioFormat = "Content/Battle/TutorialHuaguoshanW{0}";
        private const int MaxTurnLoopFallback = 1000;

        public static void Run()
        {
            var args = ParseArgs(Environment.GetCommandLineArgs());

            var scenarios = ParseScenarioIds(args).ToArray();
            var scenarioFormat = GetArg(args, "-scenarioFormat", DefaultScenarioFormat);
            var seedStart = GetInt(args, "-seedStart", 1);
            var countPerScenario = GetInt(args, "-count", 200);
            var outputPath = ResolveOutputPath(GetArg(args, "-resultFile", null));
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
                outputDirectory = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(outputDirectory);

            var exportSeedLevel = GetBool(args, "-exportRuns", false);
            var strict = GetBool(args, "-strict", false);
            var minWinRate = GetFloat(args, "-minWinRate", 0f);
            var maxLoopTolerance = GetInt(args, "-maxLoopTolerance", 0);
            var agent = CreateAgent(GetArg(args, "-agent", "standard"));
            if (agent == null)
                throw new Exception($"Unknown agent: {GetArg(args, "-agent", "standard")}");

            Debug.Log($"[ValueSim] agent={agent.Name} | seeds={seedStart}~{seedStart + countPerScenario - 1} | scenarios={string.Join(",", scenarios)}");
            Debug.Log($"[ValueSim] resultFile={outputPath}");

            var lines = new List<string>
            {
                "ScenarioId,ScenarioPath,Agent,Wins,Losses,Runs,WinRate,MedianTurns,P25Turns,P75Turns,MedianHpLost,P90HpLost,MedianCards,MedianCombo,MedianMana,MaxTurnsHits,LoopDetected,FailureReason"
            };

            var failed = false;

            foreach (var scenarioRaw in scenarios)
            {
                if (!int.TryParse(scenarioRaw, out var scenarioIndex))
                {
                    var errRow = $"{scenarioRaw},invalid_id,{agent.Name},0,0,0,0,0,0,0,0,0,0,0,0,0,InvalidScenarioId";
                    lines.Add(errRow);
                    failed = true;
                    continue;
                }

                var scenarioPath = BuildScenarioPath(scenarioFormat, scenarioIndex);
                var profile = Resources.Load<BattleScenarioProfile>(scenarioPath);
                if (profile == null)
                {
                    var errRow = $"{scenarioIndex},{scenarioPath},{agent.Name},0,0,0,0,0,0,0,0,0,0,0,0,0,ProfileNotFound";
                    lines.Add(errRow);
                    Debug.LogError($"[ValueSim] cannot load scenario profile: {scenarioPath}");
                    failed = true;
                    continue;
                }

                var scenarioRuns = RunScenario(profile, scenarioPath, scenarioIndex, agent, seedStart, countPerScenario, outputDirectory, exportSeedLevel);
                var failure = string.Empty;

                if (scenarioRuns.WinRate < minWinRate)
                {
                    failure = AppendFailure(failure, $"WinRate<{minWinRate:0.###}");
                    failed = true;
                }

                if (scenarioRuns.LoopDetected > maxLoopTolerance)
                {
                    failure = AppendFailure(failure, $"LoopDetected>{maxLoopTolerance}");
                    failed = true;
                }

                var row = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6:0.###},{7:0.0},{8:0.0},{9:0.0},{10:0.0},{11:0.0},{12:0},{13:0},{14:0},{15},{16},{17}",
                    profile.scenarioId,
                    scenarioPath,
                    agent.Name,
                    scenarioRuns.Wins,
                    scenarioRuns.Losses,
                    scenarioRuns.Runs,
                    scenarioRuns.WinRate,
                    scenarioRuns.MedianTurns,
                    scenarioRuns.P25Turns,
                    scenarioRuns.P75Turns,
                    scenarioRuns.MedianHpLost,
                    scenarioRuns.P90HpLost,
                    scenarioRuns.MedianCardsPlayed,
                    scenarioRuns.MedianMaxCombo,
                    scenarioRuns.MedianManaSpent,
                    scenarioRuns.MaxTurnsHits,
                    scenarioRuns.LoopDetected,
                    failure);

                lines.Add(row);

                Debug.Log(
                    $"[ValueSim] {scenarioPath} | runs={scenarioRuns.Runs} wins={scenarioRuns.Wins} " +
                    $"winRate={scenarioRuns.WinRate:0.000} medTurns={scenarioRuns.MedianTurns:0.0} " +
                    $"medHpLost={scenarioRuns.MedianHpLost:0.0} p90HpLost={scenarioRuns.P90HpLost:0.0} " +
                    $"loops={scenarioRuns.LoopDetected}");
            }

            File.WriteAllText(outputPath, string.Join(Environment.NewLine, lines), System.Text.Encoding.UTF8);
            Debug.Log($"[ValueSim] done, summary saved to: {outputPath}");

            if (strict && failed)
                throw new Exception("[ValueSim] simulation failed threshold checks, stop.");
        }

        private static RunSummary RunScenario(
            BattleScenarioProfile profile,
            string scenarioPath,
            int scenarioIndex,
            ISimulationAgent agent,
            int seedStart,
            int countPerScenario,
            string outputDirectory,
            bool exportSeedLevel)
        {
            var wins = 0;
            var losses = 0;
            var maxTurnsHits = 0;
            var loopDetected = 0;

            var turns = new List<int>(countPerScenario);
            var hpLost = new List<int>(countPerScenario);
            var cardsPlayed = new List<int>(countPerScenario);
            var maxCombo = new List<int>(countPerScenario);
            var manaSpent = new List<int>(countPerScenario);

            var rows = new List<string>();
            if (exportSeedLevel)
                rows.Add("Scenario,Seed,Win,Turns,HpLost,CardsPlayed,MaxCombo,ManaSpent,Termination");

            for (int i = 0; i < countPerScenario; i++)
            {
                // PlayerStats/EnemyStats/CardConfig 都是运行期可变对象，
                // 每个 seed 必须从 profile 重建，禁止跨场共享已结算状态。
                var spec = ScenarioFactory.FromProfile(profile);
                var random = new SeededRandomSource(seedStart + i);
                var metrics = SimulationRunner.Run(spec, agent, random, true);

                var win = metrics.Win;
                if (win) wins++; else losses++;

                turns.Add(metrics.Turns);
                hpLost.Add(metrics.HpLost);
                cardsPlayed.Add(metrics.CardsPlayed);
                maxCombo.Add(metrics.MaxCombo);
                manaSpent.Add(metrics.ManaSpent);

                if (metrics.Termination == SimulationTerminationReason.MaxTurns)
                    maxTurnsHits++;

                if (metrics.Termination == SimulationTerminationReason.CommandLimit ||
                    metrics.Turns >= MaxTurnLoopFallback)
                {
                    loopDetected++;
                }

                if (exportSeedLevel)
                {
                    var term = metrics.Termination.ToString();
                    rows.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                        profile.scenarioId,
                        seedStart + i,
                        win ? "win" : "lose",
                        metrics.Turns,
                        metrics.HpLost,
                        metrics.CardsPlayed,
                        metrics.MaxCombo,
                        metrics.ManaSpent,
                        term));
                }
            }

            if (exportSeedLevel)
            {
                var runPath = Path.Combine(
                    outputDirectory,
                    $"{scenarioPath.Replace('/', '_').Replace("\\", "_")}-runs.csv");
                File.WriteAllText(runPath, string.Join(Environment.NewLine, rows), System.Text.Encoding.UTF8);
            }

            var sortedTurns = turns.OrderBy(v => v).ToList();
            var sortedHp = hpLost.OrderBy(v => v).ToList();
            var sortedCards = cardsPlayed.OrderBy(v => v).ToList();
            var sortedCombo = maxCombo.OrderBy(v => v).ToList();
            var sortedMana = manaSpent.OrderBy(v => v).ToList();

            return new RunSummary
            {
                Runs = countPerScenario,
                Wins = wins,
                Losses = losses,
                WinRate = countPerScenario > 0 ? wins / (float)countPerScenario : 0f,
                MedianTurns = Percentile(sortedTurns, 0.50f),
                P25Turns = Percentile(sortedTurns, 0.25f),
                P75Turns = Percentile(sortedTurns, 0.75f),
                MedianHpLost = Percentile(sortedHp, 0.50f),
                P90HpLost = Percentile(sortedHp, 0.90f),
                MedianCardsPlayed = Percentile(sortedCards, 0.50f),
                MedianMaxCombo = Percentile(sortedCombo, 0.50f),
                MedianManaSpent = Percentile(sortedMana, 0.50f),
                MaxTurnsHits = maxTurnsHits,
                LoopDetected = loopDetected
            };
        }

        private static ISimulationAgent CreateAgent(string agentName)
        {
            return agentName.ToLowerInvariant() switch
            {
                "standard" => new StandardAgent(),
                "random" => new LegalRandomAgent(),
                _ => null
            };
        }

        private static string ResolveOutputPath(string explicitPath)
        {
            string path;
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                path = explicitPath;
            }
            else
            {
                var now = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var cwd = Directory.GetCurrentDirectory();
                path = Path.Combine(cwd, "SimulationResults", $"value-sim-{now}.csv");
            }

            return path;
        }

        private static string BuildScenarioPath(string format, int scenarioIndex)
        {
            var padded = scenarioIndex.ToString("D2");
            if (format.Contains("{0"))
                return string.Format(CultureInfo.InvariantCulture, format, padded, scenarioIndex);

            return $"{format}{padded}";
        }

        private static IEnumerable<string> ParseScenarioIds(Dictionary<string, string> args)
        {
            if (args.TryGetValue("-scenarios", out var explicitScenarios))
            {
                foreach (var scenario in explicitScenarios
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    yield return scenario;
                }

                yield break;
            }

            if (args.TryGetValue("-scenarioRange", out var rangeSpec))
            {
                var parts = rangeSpec.Split('-');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var start) &&
                    int.TryParse(parts[1], out var end))
                {
                    var asc = start <= end;
                    for (var i = start; asc ? i <= end : i >= end; i += asc ? 1 : -1)
                        yield return i.ToString();
                    yield break;
                }
            }

            if (args.TryGetValue("-startScenario", out var startStr) &&
                args.TryGetValue("-endScenario", out var endStr) &&
                int.TryParse(startStr, out var start2) &&
                int.TryParse(endStr, out var end2))
            {
                var asc2 = start2 <= end2;
                for (var i = start2; asc2 ? i <= end2 : i >= end2; i += asc2 ? 1 : -1)
                    yield return i.ToString();
                yield break;
            }

            yield return "1";
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i];
                if (!key.StartsWith("-", StringComparison.Ordinal))
                    continue;

                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    map[key] = args[i + 1];
                    i++;
                }
                else
                {
                    map[key] = "true";
                }
            }
            return map;
        }

        private static string GetArg(Dictionary<string, string> args, string key, string fallback)
            => args.TryGetValue(key, out var value) ? value : fallback;

        private static int GetInt(Dictionary<string, string> args, string key, int fallback)
            => int.TryParse(GetArg(args, key, fallback.ToString(CultureInfo.InvariantCulture)), out var value) ? value : fallback;

        private static float GetFloat(Dictionary<string, string> args, string key, float fallback)
            => float.TryParse(GetArg(args, key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        private static bool GetBool(Dictionary<string, string> args, string key, bool fallback)
            => args.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

        private static float Percentile(IReadOnlyList<int> sorted, float p)
        {
            if (sorted == null || sorted.Count == 0)
                return 0f;
            var idx = (int)Math.Round(p * (sorted.Count - 1));
            if (idx < 0) idx = 0;
            if (idx >= sorted.Count) idx = sorted.Count - 1;
            return sorted[idx];
        }

        private static string AppendFailure(string oldValue, string next)
            => string.IsNullOrEmpty(oldValue) ? next : $"{oldValue};{next}";

        private sealed class RunSummary
        {
            public int Runs;
            public int Wins;
            public int Losses;
            public float WinRate;
            public float MedianTurns;
            public float P25Turns;
            public float P75Turns;
            public float MedianHpLost;
            public float P90HpLost;
            public float MedianCardsPlayed;
            public float MedianMaxCombo;
            public float MedianManaSpent;
            public int MaxTurnsHits;
            public int LoopDetected;
        }

    }
}
