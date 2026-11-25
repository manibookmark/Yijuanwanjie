using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Simulation.Balance
{
    public sealed class BalanceReportExportResult
    {
        internal BalanceReportExportResult(string outputDirectory)
        {
            OutputDirectory = outputDirectory;
            ManifestPath = Path.Combine(outputDirectory, "manifest.json");
            ReportPath = Path.Combine(outputDirectory, "report.json");
            SummaryPath = Path.Combine(outputDirectory, "summary.md");
        }

        public string OutputDirectory { get; }
        public string ManifestPath { get; }
        public string ReportPath { get; }
        public string SummaryPath { get; }
    }

    /// <summary>
    /// Deterministic report exporter. The final directory appears atomically and
    /// manifest.json is written last, making it the authority for completion.
    /// Logs remain diagnostics and are never parsed as a module protocol.
    /// </summary>
    public static class BalanceSimulationExport
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static BalanceReportExportResult WriteAtomic(
            BalanceSimulationReport report,
            string outputDirectory,
            string projectRoot)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is required.");
            if (string.IsNullOrWhiteSpace(report.status) ||
                string.Equals(report.status, "running", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cannot export a report that has not reached a terminal status.");
            }

            string finalPath = Path.GetFullPath(outputDirectory);
            if (Directory.Exists(finalPath) || File.Exists(finalPath))
                throw new IOException($"Output path already exists: {finalPath}");
            string parent = Path.GetDirectoryName(finalPath);
            if (string.IsNullOrWhiteSpace(parent))
                throw new IOException($"Cannot resolve output parent: {finalPath}");
            Directory.CreateDirectory(parent);

            string staging = Path.Combine(
                parent,
                "." + Path.GetFileName(finalPath) + ".tmp-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);

            try
            {
                Write(Path.Combine(staging, "report.json"),
                    JsonUtility.ToJson(report, true) + Environment.NewLine);
                Write(Path.Combine(staging, "config-ledger.csv"),
                    BuildLedgerCsv(report.configLedger));
                Write(Path.Combine(staging, "routes.csv"),
                    BuildRoutesCsv(report.routes));
                Write(Path.Combine(staging, "encounters.csv"),
                    BuildEncountersCsv(
                        report.isolatedEncounters,
                        report.routeEncounters));
                Write(Path.Combine(staging, "cards.csv"),
                    BuildCardsCsv(report.cards));
                Write(Path.Combine(staging, "enemies.csv"),
                    BuildEnemiesCsv(report.enemies));
                Write(Path.Combine(staging, "rewards.csv"),
                    BuildRewardsCsv(report.rewards));
                Write(Path.Combine(staging, "failures.csv"),
                    BuildFailuresCsv(report.failures));
                Write(Path.Combine(staging, "summary.md"), BuildSummary(report));

                string[] artifactNames =
                {
                    "report.json",
                    "config-ledger.csv",
                    "routes.csv",
                    "encounters.csv",
                    "cards.csv",
                    "enemies.csv",
                    "rewards.csv",
                    "failures.csv",
                    "summary.md"
                };
                var manifest = new BalanceManifest
                {
                    schemaVersion = 2,
                    reportSchemaVersion = report.schemaVersion,
                    status = report.status,
                    exitCode = report.exitCode,
                    generatedAtUtc = DateTime.UtcNow.ToString(
                        "o",
                        CultureInfo.InvariantCulture),
                    unityVersion = Application.unityVersion,
                    gitCommit = report.gitCommit,
                    gitDirtyFingerprint = report.gitDirtyFingerprint,
                    suiteId = report.suiteId,
                    suiteRevision = report.suiteRevision,
                    suiteSourceSha256 = report.suiteSourceSha256,
                    floorContentId = report.floorContentId,
                    routeSequenceSemantics = report.routeSequenceSemantics,
                    preset = report.preset,
                    seedStart = report.seedStart,
                    countPerTarget = report.countPerTarget,
                    maxTurnsPerEncounter = report.maxTurnsPerEncounter,
                    strictBalanceChecks = report.strictBalanceChecks,
                    agentId = report.agentId,
                    agentRevision = report.agentRevision,
                    rewardPolicyId = report.rewardPolicyId,
                    rewardPolicyRevision = report.rewardPolicyRevision,
                    entryRuleId = report.entryRuleId,
                    entryRuleRevision = report.entryRuleRevision,
                    entryBuildScope = report.entryBuildScope,
                    entryStateKind = report.entryStateKind,
                    entryStateRevision = report.entryStateRevision,
                    entryCheckpointSha256 = report.entryCheckpointSha256,
                    contentFingerprint = report.contentFingerprint,
                    artifacts = BuildArtifactManifest(staging, artifactNames)
                };
                Write(Path.Combine(staging, "manifest.json"),
                    JsonUtility.ToJson(manifest, true) + Environment.NewLine);

                Directory.Move(staging, finalPath);
                return new BalanceReportExportResult(finalPath);
            }
            catch (Exception exception)
            {
                throw new IOException(
                    $"Balance report export failed. Incomplete evidence remains " +
                    $"at {staging}: {exception.Message}",
                    exception);
            }
        }

        private static string BuildLedgerCsv(
            IReadOnlyList<BalanceLedgerEntry> items)
        {
            var rows = Header(
                "NodeContentId", "ScenarioId", "ItemKind", "ContentId",
                "Ordinal", "Count", "Health", "Attack", "Experience",
                "Armor", "ManaCost", "Damage", "Healing", "ManaGain",
                "Behavior", "Notes");
            for (int i = 0; i < items.Count; i++)
            {
                BalanceLedgerEntry item = items[i];
                AddRow(rows,
                    item.nodeContentId, item.scenarioId, item.itemKind,
                    item.contentId, item.ordinal, item.count, item.health,
                    item.attack, item.experience, item.armor, item.manaCost,
                    item.damage, item.healing, item.manaGain, item.behavior,
                    item.notes);
            }
            return Finish(rows);
        }

        private static string BuildRoutesCsv(
            IReadOnlyList<BalanceRouteAggregate> items)
        {
            var rows = Header(
                "RouteId", "Revision", "Runs", "Completions", "Defeats",
                "SimulationLimits", "CompletionRate", "SimulationLimitRate",
                "MedianEndingHealth", "P10EndingHealth", "MedianEncounterCount",
                "MedianTurns", "MedianHpLost", "MedianCardsPlayed",
                "MedianImmediateResolvedDamage", "Success95Lower", "Success95Upper",
                "VictorySamples", "VictoryMedianEndingHealth",
                "DefeatSamples", "DefeatMedianEndingHealth", "LimitSamples");
            for (int i = 0; i < items.Count; i++)
            {
                BalanceRouteAggregate item = items[i];
                AddRow(rows,
                    item.routeId, item.routeRevision, item.runs,
                    item.completions, item.defeats, item.simulationLimits,
                    item.completionRate, item.simulationLimitRate,
                    item.medianEndingHealth, item.p10EndingHealth,
                    item.medianEncounterCount, item.medianTurns,
                    item.medianHpLost, item.medianCardsPlayed,
                    item.medianDamage,
                    item.outcomes.successRate95.lower, item.outcomes.successRate95.upper,
                    item.outcomes.victories.samples, OptionalMedian(item.outcomes.victories),
                    item.outcomes.defeats.samples, OptionalMedian(item.outcomes.defeats),
                    item.outcomes.limits.samples);
            }
            return Finish(rows);
        }

        private static string BuildEncountersCsv(
            IReadOnlyList<BalanceEncounterAggregate> isolated,
            IReadOnlyList<BalanceEncounterAggregate> routed)
        {
            var rows = Header(
                "ScopeId", "TargetId", "NodeContentId", "ScenarioId", "Runs",
                "PopulationRuns", "ReachRate", "Wins", "SimulationLimits",
                "WinRate", "PopulationWinRate", "SimulationLimitRate",
                "MedianTurns", "MedianHpLost", "P90HpLost",
                "MedianImmediateResolvedDamage",
                "MedianCardsPlayed", "MedianManaSpent", "MedianEndingHealth",
                "Win95Lower", "Win95Upper", "PopulationWin95Lower", "PopulationWin95Upper",
                "Reach95Lower", "Reach95Upper", "VictorySamples", "VictoryMedianEndingHealth",
                "DefeatSamples", "DefeatMedianEndingHealth", "LimitSamples");
            AppendEncounterRows(rows, isolated);
            AppendEncounterRows(rows, routed);
            return Finish(rows);
        }

        private static void AppendEncounterRows(
            List<string> rows,
            IReadOnlyList<BalanceEncounterAggregate> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                BalanceEncounterAggregate item = items[i];
                AddRow(rows,
                    item.scopeId, item.targetId, item.nodeContentId,
                    item.scenarioId, item.runs, item.populationRuns,
                    item.reachRate, item.wins,
                    item.simulationLimits, item.winRate,
                    item.populationWinRate, item.simulationLimitRate,
                    item.medianTurns,
                    item.medianHpLost, item.p90HpLost, item.medianDamage,
                    item.medianCardsPlayed, item.medianManaSpent,
                    item.medianEndingHealth,
                    item.outcomes.successRate95.samples == 0 ? (object)null : item.outcomes.successRate95.lower,
                    item.outcomes.successRate95.samples == 0 ? (object)null : item.outcomes.successRate95.upper,
                    item.populationWinRate95.lower, item.populationWinRate95.upper,
                    item.reachRate95.lower, item.reachRate95.upper,
                    item.outcomes.victories.samples, OptionalMedian(item.outcomes.victories),
                    item.outcomes.defeats.samples, OptionalMedian(item.outcomes.defeats),
                    item.outcomes.limits.samples);
            }
        }

        private static string BuildCardsCsv(
            IReadOnlyList<BalanceCardAggregate> items)
        {
            var rows = Header(
                "ScopeId", "TargetId", "CardContentId", "Plays",
                "ImmediateResolvedDamage", "Armor", "Healing", "ManaSpent",
                "ManaGained", "PlaysWithDirectKill",
                "ImmediateDamagePerPlay");
            for (int i = 0; i < items.Count; i++)
            {
                BalanceCardAggregate item = items[i];
                AddRow(rows,
                    item.scopeId, item.targetId, item.cardContentId, item.plays,
                    item.damage, item.armor, item.healing, item.manaSpent,
                    item.manaGained, item.directKills, item.damagePerPlay);
            }
            return Finish(rows);
        }

        private static string BuildEnemiesCsv(
            IReadOnlyList<BalanceEnemyAggregate> items)
        {
            var rows = Header(
                "ScopeId", "TargetId", "EnemyContentId", "Actions", "Attacks",
                "Defends", "PlayerHealthDamage", "PlayerArmorDamage",
                "ArmorGained");
            for (int i = 0; i < items.Count; i++)
            {
                BalanceEnemyAggregate item = items[i];
                AddRow(rows,
                    item.scopeId, item.targetId, item.enemyContentId,
                    item.actions, item.attacks, item.defends,
                    item.playerHealthDamage, item.playerArmorDamage,
                    item.armorGained);
            }
            return Finish(rows);
        }

        private static string BuildRewardsCsv(
            IReadOnlyList<BalanceRewardAggregate> items)
        {
            var rows = Header(
                "ScopeId", "TargetId", "RewardKey", "CardContentId",
                "RewardType", "Offered", "Selected", "SelectionRateWhenOffered");
            for (int i = 0; i < items.Count; i++)
            {
                BalanceRewardAggregate item = items[i];
                AddRow(rows,
                    item.scopeId, item.targetId, item.rewardKey,
                    item.cardContentId, item.rewardType, item.offered,
                    item.selected, item.selectionRateWhenOffered);
            }
            return Finish(rows);
        }

        private static string BuildFailuresCsv(
            IReadOnlyList<BalanceFailureSample> items)
        {
            var rows = Header(
                "ScopeId", "TargetId", "Seed", "NodeContentId", "Termination",
                "EndingHealth", "Turns");
            for (int i = 0; i < items.Count; i++)
            {
                BalanceFailureSample item = items[i];
                AddRow(rows,
                    item.scopeId, item.targetId, item.seed,
                    item.nodeContentId, item.termination,
                    item.endingHealth, item.turns);
            }
            return Finish(rows);
        }

        private static string BuildSummary(BalanceSimulationReport report)
        {
            var text = new StringBuilder();
            text.AppendLine("# 数值模拟摘要");
            text.AppendLine();
            text.Append("- 状态：`").Append(report.status).AppendLine("`");
            text.Append("- Suite：`").Append(report.suiteId).Append('@')
                .Append(report.suiteRevision).AppendLine("`");
            text.Append("- 楼层：`").Append(report.floorContentId).AppendLine("`");
            text.Append("- 预设：`").Append(report.preset).Append("`，每项目 ")
                .Append(report.countPerTarget).Append(" seeds（")
                .Append(report.seedStart).Append('–')
                .Append(report.seedStart + Math.Max(0, report.countPerTarget - 1))
                .AppendLine("）");
            text.Append("- 实际执行：").Append(report.targetsExecuted)
                .Append(" 个目标，").Append(report.combatRunsExecuted)
                .AppendLine(" 场战斗");
            text.Append("- 内容指纹：`").Append(report.contentFingerprint)
                .AppendLine("`");
            text.Append("- 路线执行语义：`").Append(report.routeSequenceSemantics)
                .AppendLine("`");
            text.Append("- 入口构筑范围：`").Append(report.entryBuildScope)
                .AppendLine("`");
            text.AppendLine();
            text.AppendLine(
                "> `passed` 只表示 Suite 中明确声明的门禁全部通过；" +
                "未声明目标的卡牌、敌人和奖励数据仍是观测项，不能据此自动称为“已合理”。");
            text.AppendLine(
                "> 每卡伤害归因当前只统计 CombatCardResolvedEvent 的即时结算；" +
                "不把后续 DoT 或英灵触发回填给来源卡。PlaysWithDirectKill " +
                "是兼容旧列名，表示结算中有即时击杀的出牌次数，不限定 Direct 伤害类型，也不是击杀人数。");
            text.AppendLine(
                "> 逐场 JSON 样本的 telemetryRevision=1 表示已按实际事件采集断势、伤害与击杀分类；" +
                "缺失或为 0 时为未采集，不可按零值比较。comboBreaks 仅指费用序列断势，" +
                "不含印清势或回合重置；appliedDamage 包含延迟伤害但不含余威，" +
                "killsBySplash 仅指溅射，killsByOther 包含其他伤害类型及非伤害击败。");
            text.AppendLine(
                "> 当前入口构筑只包含正式 Scenario、Checkpoint 与 Suite " +
                "规则；没有对玩家元进度或道藏分布做投影。结论只适用于该固定入口构筑。");
            text.AppendLine();

            text.AppendLine("## 自动判断");
            text.AppendLine();
            if (report.checks.Count == 0)
            {
                text.AppendLine("没有可执行的判断项。");
            }
            else
            {
                text.AppendLine("| 结果 | 检查 | 严重度 | 说明 |");
                text.AppendLine("|---|---|---|---|");
                for (int i = 0; i < report.checks.Count; i++)
                {
                    BalanceCheckResult item = report.checks[i];
                    text.Append("| ").Append(item.status).Append(" | `")
                        .Append(EscapeMarkdown(item.checkId)).Append("` | ")
                        .Append(EscapeMarkdown(item.severity)).Append(" | ")
                        .Append(EscapeMarkdown(item.description)).Append(" — ")
                        .Append(EscapeMarkdown(item.details)).AppendLine(" |");
                }
            }
            text.AppendLine();

            text.AppendLine("## 覆盖边界");
            text.AppendLine();
            if (report.coverageGaps.Count == 0)
            {
                text.AppendLine("当前 Suite 未发现被排除的地图节点。");
            }
            else
            {
                text.AppendLine("| 节点 | 类型 | 影响后续战斗 | 配置效果 | 排除理由 |");
                text.AppendLine("|---|---|---:|---|---|");
                for (int i = 0; i < report.coverageGaps.Count; i++)
                {
                    BalanceCoverageGap item = report.coverageGaps[i];
                    text.Append("| `").Append(EscapeMarkdown(item.nodeContentId))
                        .Append("` | ").Append(item.nodeType).Append(" | ")
                        .Append(item.affectsCombat ? "是" : "否").Append(" | ")
                        .Append(EscapeMarkdown(item.configuredEffects)).Append(" | ")
                        .Append(EscapeMarkdown(item.declaredReason)).AppendLine(" |");
                }
            }
            text.AppendLine();

            text.AppendLine("## 路线概览");
            text.AppendLine();
            text.AppendLine("| 路线 | 通关率（95% 区间） | 全体 P10/中位生命 | 胜者中位生命（样本数） | 败者中位生命（样本数） | 中位回合 | 模拟上限 |");
            text.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < report.routes.Count; i++)
            {
                BalanceRouteAggregate item = report.routes[i];
                text.Append("| `").Append(EscapeMarkdown(item.routeId))
                    .Append("` | ").Append(Percent(item.completionRate))
                    .Append("（").Append(Percent(item.outcomes.successRate95.lower))
                    .Append("–").Append(Percent(item.outcomes.successRate95.upper)).Append('）')
                    .Append(" | ").Append(Number(item.p10EndingHealth))
                    .Append('/').Append(Number(item.medianEndingHealth))
                    .Append(" | ").Append(DescribeHealth(item.outcomes.victories))
                    .Append(" | ").Append(DescribeHealth(item.outcomes.defeats))
                    .Append(" | ").Append(Number(item.medianTurns))
                    .Append(" | ").Append(item.simulationLimits).AppendLine(" |");
            }
            text.AppendLine();
            text.AppendLine(
                "> 全体生命包含败局和模拟上限；胜者、败者分别统计，空分组标为无样本。" +
                "95% 区间使用 Wilson 方法，只描述本策略与入口的种子波动。" +
                "report.json 的 routeSamples 保留全部路线种子、胜败、等级、经验和牌数，支持配对比较。");
            text.AppendLine(
                "> encounterSamples 保留逐场逐种子的实际承伤、护甲吸收、回复、净生命变化、击杀、逃跑和敌人经验；" +
                "hpLost 是进入生命减结束生命，不等于累计伤害。贡献表中 scopeId=route 是整条路线，" +
                "scopeId=具体路线 ID 则按 targetId 节点分组，两种口径不能重复累加。");
            text.AppendLine(
                "> `cards.csv` 只统计实际打出后的权威贡献；当前事件合同无法可靠按 contentId 统计每卡抽取次数，因此没有伪造“抽取率”。");
            return text.ToString();
        }

        private static object OptionalMedian(BalanceHealthDistribution distribution)
            => distribution.samples == 0 ? null : (object)distribution.median;

        private static string DescribeHealth(BalanceHealthDistribution distribution)
            => distribution.samples == 0 ? "无样本（0）" :
                Number(distribution.median) + "（" + distribution.samples + "）";

        private static List<string> Header(params object[] values)
        {
            var result = new List<string> { Csv(values) };
            return result;
        }

        private static void AddRow(List<string> rows, params object[] values)
        {
            rows.Add(Csv(values));
        }

        private static string Csv(IReadOnlyList<object> values)
        {
            var cells = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
                cells[i] = EscapeCsv(Format(values[i]));
            return string.Join(",", cells);
        }

        private static string Format(object value)
        {
            if (value == null) return string.Empty;
            if (value is float single)
                return single.ToString("0.######", CultureInfo.InvariantCulture);
            if (value is double real)
                return real.ToString("0.######", CultureInfo.InvariantCulture);
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }

        private static string EscapeCsv(string value)
        {
            value ??= string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Finish(IReadOnlyList<string> rows)
        {
            return string.Join(Environment.NewLine, rows) + Environment.NewLine;
        }

        private static string EscapeMarkdown(string value)
        {
            return (value ?? string.Empty)
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static string Number(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string Percent(float value)
        {
            return value.ToString("P1", CultureInfo.InvariantCulture);
        }

        private static void Write(string path, string content)
        {
            File.WriteAllText(path, content, Utf8);
        }

        private static List<BalanceArtifactManifestEntry> BuildArtifactManifest(
            string directory,
            IReadOnlyList<string> names)
        {
            var result = new List<BalanceArtifactManifestEntry>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                string path = Path.Combine(directory, name);
                var info = new FileInfo(path);
                result.Add(new BalanceArtifactManifestEntry
                {
                    path = name,
                    size = info.Length,
                    sha256 = FileSha256(path)
                });
            }
            return result;
        }

        private static string FileSha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            byte[] digest = sha.ComputeHash(stream);
            var value = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
                value.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            return value.ToString();
        }

        [Serializable]
        private sealed class BalanceManifest
        {
            public int schemaVersion;
            public int reportSchemaVersion;
            public string status;
            public int exitCode;
            public string generatedAtUtc;
            public string unityVersion;
            public string gitCommit;
            public string gitDirtyFingerprint;
            public string suiteId;
            public int suiteRevision;
            public string suiteSourceSha256;
            public string floorContentId;
            public string routeSequenceSemantics;
            public string preset;
            public int seedStart;
            public int countPerTarget;
            public int maxTurnsPerEncounter;
            public bool strictBalanceChecks;
            public string agentId;
            public int agentRevision;
            public string rewardPolicyId;
            public int rewardPolicyRevision;
            public string entryRuleId;
            public int entryRuleRevision;
            public string entryBuildScope;
            public string entryStateKind;
            public int entryStateRevision;
            public string entryCheckpointSha256;
            public string contentFingerprint;
            public List<BalanceArtifactManifestEntry> artifacts;
        }

        [Serializable]
        private sealed class BalanceArtifactManifestEntry
        {
            public string path;
            public long size;
            public string sha256;
        }
    }
}
