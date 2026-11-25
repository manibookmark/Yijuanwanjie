using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Simulation.Agents;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Deep balance-analysis facade. One call validates coverage, captures a
    /// production-content ledger, runs isolated encounters and full combat
    /// routes with identical seed sets, evaluates suite-owned targets, and
    /// returns a normalized report. This orchestration file exceeds the size
    /// review threshold but owns no combat state or statistical formulas:
    /// Core resolves combat, BalanceStatistics owns populations/uncertainty,
    /// and the export module owns serialization. Revisit before adding a new
    /// execution mode here; cross-floor sampling has its own facade.
    /// </summary>
    public static class BalanceSimulationEngine
    {
        private sealed class EncounterSample
        {
            public int Seed;
            public EncounterMetrics Metrics;
            public int StartingLevel;
            public int EndingLevel;
            public int StartingExperience;
            public int EndingExperience;
        }

        private sealed class CardAccumulator
        {
            public int Plays;
            public int Damage;
            public int Armor;
            public int Healing;
            public int ManaSpent;
            public int ManaGained;
            public int DirectKills;
        }

        private sealed class EnemyAccumulator
        {
            public int Actions;
            public int Attacks;
            public int Defends;
            public int PlayerHealthDamage;
            public int PlayerArmorDamage;
            public int ArmorGained;
        }

        private sealed class RewardAccumulator
        {
            public string CardContentId;
            public string RewardType;
            public int Offered;
            public int Selected;
        }

        public static BalanceSimulationReport Run(
            BalanceSimulationSuite suite,
            BalanceRunOptions options)
        {
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            if (options == null) throw new ArgumentNullException(nameof(options));

            LevelRunProfile floor = Resources.Load<LevelRunProfile>(
                suite.FloorResourcePath);
            if (floor == null)
            {
                throw new BalanceSuiteException(
                    $"Cannot load floor Resources/{suite.FloorResourcePath}.");
            }

            var report = CreateReport(suite, options);
            BalanceSuitePreflight.Validate(suite, floor, report.coverageGaps);
            report.configLedger.AddRange(BalanceContentLedger.Build(suite, floor));
            report.contentFingerprint = BalanceContentLedger.BuildFingerprint(
                suite,
                report.configLedger);

            if (options.Preset == BalanceRunPreset.Calibration &&
                BalanceSuitePreflight.HasCalibrationBlock(report.coverageGaps))
            {
                report.calibrationBlockedByCoverage = true;
                report.checks.Add(new BalanceCheckResult
                {
                    checkId = "COVERAGE_FULL_ROUTE",
                    status = "Fail",
                    severity = BalanceCheckSeverity.Blocking.ToString(),
                    description =
                        "Calibration cannot omit interactions that change later combat.",
                    details = BalanceSuitePreflight.DescribeCalibrationBlockers(
                        report.coverageGaps)
                });
                BalanceThresholdEvaluator.FinalizeStatus(report, options);
                return report;
            }

            IReadOnlyList<BalanceEncounterDefinition> encounters =
                SelectEncounters(suite, options.Preset);
            IReadOnlyList<BalanceRouteDefinition> routes =
                SelectRoutes(suite, options.Preset);
            if (options.Preset == BalanceRunPreset.Quick &&
                BalanceSuitePreflight.HasCalibrationBlock(report.coverageGaps))
            {
                routes = Array.Empty<BalanceRouteDefinition>();
                report.checks.Add(new BalanceCheckResult
                {
                    checkId = "COVERAGE_QUICK_ISOLATED_ONLY",
                    status = "Fail",
                    severity = BalanceCheckSeverity.Warning.ToString(),
                    description =
                        "Quick is restricted to isolated combat baselines.",
                    details =
                        "Combat-affecting interactions are not represented; " +
                        "route results were not executed."
                });
            }
            if (encounters.Count == 0 && routes.Count == 0)
            {
                throw new BalanceSuiteException(
                    $"Preset {options.Preset} selects no simulation targets.");
            }
            if (options.Preset == BalanceRunPreset.Calibration &&
                !ContainsFullRoute(routes))
            {
                throw new BalanceSuiteException(
                    "Calibration requires at least one purpose=Full route. " +
                    "Branch/Probe routes cannot establish a whole-floor result.");
            }

            long combatRunsPerSeed = encounters.Count;
            for (int i = 0; i < routes.Count; i++)
                combatRunsPerSeed += routes[i].NodeContentIds.Count;
            options.EnsureWithinWorkBudget(combatRunsPerSeed);

            for (int i = 0; i < encounters.Count; i++)
            {
                RunIsolatedEncounter(
                    suite,
                    floor,
                    encounters[i],
                    options,
                    report);
            }
            for (int i = 0; i < routes.Count; i++)
            {
                RunRoute(
                    suite,
                    floor,
                    routes[i],
                    options,
                    report);
            }

            report.targetsExecuted = encounters.Count + routes.Count;
            BalanceThresholdEvaluator.Evaluate(suite, report, options);
            SortReport(report);
            return report;
        }

        private static bool ContainsFullRoute(
            IReadOnlyList<BalanceRouteDefinition> routes)
        {
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i].Purpose == BalanceRoutePurpose.Full)
                    return true;
            }
            return false;
        }

        private static BalanceSimulationReport CreateReport(
            BalanceSimulationSuite suite,
            BalanceRunOptions options)
        {
            return new BalanceSimulationReport
            {
                status = "running",
                exitCode = 0,
                suiteId = suite.SuiteId,
                suiteRevision = suite.Revision,
                floorContentId = suite.FloorContentId,
                floorResourcePath = suite.FloorResourcePath,
                snapshotId = suite.SnapshotId,
                coverageMode = suite.CoverageMode.ToString(),
                routeSequenceSemantics =
                    suite.UsesDesignSequence
                        ? BalanceSimulationReport.RouteSequenceSemanticsId
                        : BalanceSimulationReport.ProductionRouteSemanticsId,
                preset = options.Preset.ToString(),
                seedStart = options.SeedStart,
                countPerTarget = options.Count,
                maxTurnsPerEncounter = options.MaxTurnsPerEncounter,
                strictBalanceChecks = options.StrictBalanceChecks,
                suiteSourceSha256 = suite.SourceSha256,
                entryStateKind = suite.EntryState.Kind.ToString(),
                entryStateRevision = suite.EntryState.Revision,
                entryCheckpointFile = suite.EntryState.CheckpointFile,
                entryCheckpointSha256 = suite.EntryState.CheckpointSha256,
                agentId = suite.Agent.Id,
                agentRevision = suite.Agent.Revision,
                rewardPolicyId = suite.RewardPolicy.Id,
                rewardPolicyRevision = suite.RewardPolicy.Revision,
                entryRuleId = suite.EntryRule.Id,
                entryRuleRevision = suite.EntryRule.Revision,
                selectedHeroContentId = suite.EntryRule.SelectedHeroContentId,
                entryBuildScope = BalanceSimulationReport.EntryBuildScopeId,
                hasBalanceTargets =
                    BalanceThresholdEvaluator.HasDesignTargets(suite.Checks)
            };
        }

        private static IReadOnlyList<BalanceEncounterDefinition> SelectEncounters(
            BalanceSimulationSuite suite,
            BalanceRunPreset preset)
        {
            var result = new List<BalanceEncounterDefinition>();
            for (int i = 0; i < suite.IsolatedEncounters.Count; i++)
            {
                BalanceEncounterDefinition item = suite.IsolatedEncounters[i];
                if (preset == BalanceRunPreset.Calibration || item.Quick)
                    result.Add(item);
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<BalanceRouteDefinition> SelectRoutes(
            BalanceSimulationSuite suite,
            BalanceRunPreset preset)
        {
            var result = new List<BalanceRouteDefinition>();
            for (int i = 0; i < suite.Routes.Count; i++)
            {
                BalanceRouteDefinition item = suite.Routes[i];
                if (preset == BalanceRunPreset.Calibration || item.Quick)
                    result.Add(item);
            }
            return result.AsReadOnly();
        }

        private static void RunIsolatedEncounter(
            BalanceSimulationSuite suite,
            LevelRunProfile floor,
            BalanceEncounterDefinition definition,
            BalanceRunOptions options,
            BalanceSimulationReport report)
        {
            string scenarioId = string.Empty;
            var samples = new List<EncounterSample>(options.Count);
            var cardUsage = new Dictionary<string, CardAccumulator>(
                StringComparer.Ordinal);
            var enemyUsage = new Dictionary<string, EnemyAccumulator>(
                StringComparer.Ordinal);
            var rewardUsage = new Dictionary<string, RewardAccumulator>(
                StringComparer.Ordinal);

            for (int index = 0; index < options.Count; index++)
            {
                int seed = checked(options.SeedStart + index);
                var random = new SeededRandomSource(seed);
                var instanceIds = new SeededInstanceIdGenerator(1);
                DungeonRunSession dungeon = BalanceEntrySessionFactory.Create(
                    suite,
                    floor,
                    $"balance.isolated.{definition.Id}.{seed}",
                    instanceIds,
                    runSeed: seed);
                LevelNode node = dungeon.CurrentFloor.FindNode(definition.NodeContentId) ??
                    throw new InvalidOperationException("The Run has no requested encounter node.");
                BattleScenarioProfile scenario = BattleScenarioCatalog.Get(node.scenarioId);
                scenarioId = scenario.scenarioId;
                using StageEncounterAssembly assembly =
                    new StageEncounterAssembler().Create(
                    dungeon,
                    scenario,
                    random,
                    instanceIds,
                    suite.EntryRule.CreateRules(), nodeContentId: definition.NodeContentId);
                CombatController combat = assembly.Combat;
                int startingLevel = combat.CurrentLevel;
                int startingExperience = combat.CurrentXP;
                EncounterMetrics metrics = SimulationRunner.Run(
                    combat,
                    CreateAgent(suite.Agent),
                    options.MaxTurnsPerEncounter,
                    suppressDebugLogs: true,
                    rewardPolicy: CreateRewardPolicy(suite.RewardPolicy),
                    entryExperienceGrant: scenario.entryExperienceGrant);
                var sample = new EncounterSample
                {
                    Seed = seed, Metrics = metrics,
                    StartingLevel = startingLevel, EndingLevel = combat.CurrentLevel,
                    StartingExperience = startingExperience, EndingExperience = combat.CurrentXP
                };
                samples.Add(sample);
                AppendEncounterSample(report, "isolated", definition.Id, definition.NodeContentId, sample);
                report.combatRunsExecuted++;
                AccumulateUsage(
                    metrics,
                    cardUsage,
                    enemyUsage,
                    rewardUsage);
                if (!metrics.Win)
                {
                    report.failures.Add(new BalanceFailureSample
                    {
                        scopeId = "encounter",
                        targetId = definition.Id,
                        seed = seed,
                        nodeContentId = definition.NodeContentId,
                        termination = metrics.Termination.ToString(),
                        endingHealth = metrics.EndingHP,
                        turns = metrics.Turns
                    });
                }
            }

            report.isolatedEncounters.Add(AggregateEncounter(
                "isolated",
                definition.Id,
                definition.NodeContentId,
                scenarioId,
                samples));
            AppendUsage(
                report,
                "encounter",
                definition.Id,
                cardUsage,
                enemyUsage,
                rewardUsage);
        }

        private static void RunRoute(
            BalanceSimulationSuite suite,
            LevelRunProfile floor,
            BalanceRouteDefinition definition,
            BalanceRunOptions options,
            BalanceSimulationReport report)
        {
            StageSimulationBatchReport batch =
                StageSimulationBatchRunner.RunBatch(
                    floor,
                    definition.CreateStageRoute(),
                    () => CreateAgent(suite.Agent),
                    suite.Agent.Revision,
                    () => CreateRewardPolicy(suite.RewardPolicy),
                    options.SeedStart,
                    options.Count,
                    suite.EntryRule.CreateStageInput(),
                    options.MaxTurnsPerEncounter,
                    suppressDebugLogs: true,
                    snapshotId: suite.SnapshotId,
                    entrySessionFactory: (seed, ids) =>
                        BalanceEntrySessionFactory.Create(
                            suite,
                            floor,
                            $"balance.route.{definition.Id}.{seed}",
                            ids,
                            runSeed: seed),
                    treatPrerequisitesAsInformational: suite.UsesDesignSequence);

            var totalTurns = new List<int>(batch.Runs);
            var totalHpLost = new List<int>(batch.Runs);
            var totalCards = new List<int>(batch.Runs);
            var totalDamage = new List<int>(batch.Runs);
            var outcomes = new BalanceStatistics();
            var cardUsage = new Dictionary<string, CardAccumulator>(
                StringComparer.Ordinal);
            var enemyUsage = new Dictionary<string, EnemyAccumulator>(
                StringComparer.Ordinal);
            var rewardUsage = new Dictionary<string, RewardAccumulator>(
                StringComparer.Ordinal);

            for (int i = 0; i < batch.Samples.Count; i++)
            {
                StageSimulationReport sample = batch.Samples[i];
                int turns = 0;
                int cards = 0;
                int damage = 0;
                for (int j = 0; j < sample.Encounters.Count; j++)
                {
                    StageEncounterResult encounter = sample.Encounters[j];
                    turns += encounter.Turns;
                    cards += encounter.CardsPlayed;
                    damage += encounter.TotalDamage;
                    AccumulateUsage(
                        encounter,
                        cardUsage,
                        enemyUsage,
                        rewardUsage);
                }
                totalTurns.Add(turns);
                totalHpLost.Add(sample.StartingHealth - sample.EndingHealth);
                totalCards.Add(cards);
                totalDamage.Add(damage);
                outcomes.Add(sample.RouteCompleted,
                    sample.Termination == StageSimulationTermination.SimulationLimit,
                    sample.EndingHealth);
                report.routeSamples.Add(new BalanceRouteSample
                {
                    routeId = definition.Id,
                    seed = sample.MasterSeed,
                    termination = sample.Termination.ToString(),
                    failedNodeContentId = sample.FailedNodeContentId,
                    encountersReached = sample.Encounters.Count,
                    startingHealth = sample.StartingHealth,
                    endingHealth = sample.EndingHealth,
                    endingLevel = sample.EndingLevel,
                    endingExperience = sample.EndingExperience,
                    deckSize = sample.FinalDeck.Count,
                    turns = turns
                });
                report.combatRunsExecuted += sample.Encounters.Count;
                if (!sample.RouteCompleted)
                {
                    report.failures.Add(new BalanceFailureSample
                    {
                        scopeId = "route",
                        targetId = definition.Id,
                        seed = sample.MasterSeed,
                        nodeContentId = sample.FailedNodeContentId,
                        termination = sample.Termination.ToString(),
                        endingHealth = sample.EndingHealth,
                        turns = turns
                    });
                }
            }

            report.routes.Add(new BalanceRouteAggregate
            {
                routeId = definition.Id,
                routeRevision = definition.Revision,
                runs = batch.Runs,
                completions = batch.RouteCompletions,
                defeats = batch.CombatDefeats,
                simulationLimits = batch.SimulationLimits,
                completionRate = batch.RouteCompletionRate,
                simulationLimitRate = Ratio(batch.SimulationLimits, batch.Runs),
                medianEndingHealth = batch.MedianEndingHealth,
                p10EndingHealth = batch.P10EndingHealth,
                medianEncounterCount = batch.MedianEncounterCount,
                medianTurns = Percentile(totalTurns, 0.50f),
                medianHpLost = Percentile(totalHpLost, 0.50f),
                medianCardsPlayed = Percentile(totalCards, 0.50f),
                medianDamage = Percentile(totalDamage, 0.50f),
                outcomes = outcomes.Summarize()
            });

            AppendRouteEncounterAggregates(floor, definition, batch, report);
            AppendUsage(
                report,
                "route",
                definition.Id,
                cardUsage,
                enemyUsage,
                rewardUsage);
        }

        private static void AppendRouteEncounterAggregates(
            LevelRunProfile floor,
            BalanceRouteDefinition route,
            StageSimulationBatchReport batch,
            BalanceSimulationReport report)
        {
            for (int nodeIndex = 0;
                 nodeIndex < route.NodeContentIds.Count;
                 nodeIndex++)
            {
                string nodeId = route.NodeContentIds[nodeIndex];
                var samples = new List<EncounterSample>();
                var cardUsage = new Dictionary<string, CardAccumulator>(StringComparer.Ordinal);
                var enemyUsage = new Dictionary<string, EnemyAccumulator>(StringComparer.Ordinal);
                var rewardUsage = new Dictionary<string, RewardAccumulator>(StringComparer.Ordinal);
                string scenarioId = floor.FindNode(nodeId)?.scenarioId ??
                    string.Empty;
                for (int sampleIndex = 0;
                     sampleIndex < batch.Samples.Count;
                     sampleIndex++)
                {
                    StageSimulationReport sample = batch.Samples[sampleIndex];
                    for (int encounterIndex = 0;
                         encounterIndex < sample.Encounters.Count;
                         encounterIndex++)
                    {
                        StageEncounterResult encounter =
                            sample.Encounters[encounterIndex];
                        if (!string.Equals(
                                encounter.NodeContentId,
                                nodeId,
                                StringComparison.Ordinal))
                        {
                            continue;
                        }
                        scenarioId = encounter.ScenarioId;
                        AccumulateUsage(encounter, cardUsage, enemyUsage, rewardUsage);
                        var encounterSample = new EncounterSample
                        {
                            Seed = sample.MasterSeed,
                            Metrics = CopyMetrics(encounter),
                            StartingLevel = encounter.EnteringLevel,
                            EndingLevel = encounter.EndingLevel,
                            StartingExperience = encounter.EnteringExperience,
                            EndingExperience = encounter.EndingExperience
                        };
                        samples.Add(encounterSample);
                        AppendEncounterSample(report, route.Id, nodeId, nodeId, encounterSample);
                        break;
                    }
                }
                BalanceEncounterAggregate aggregate = AggregateEncounter(
                    route.Id,
                    nodeId,
                    nodeId,
                    scenarioId,
                    samples);
                aggregate.populationRuns = batch.Runs;
                aggregate.reachRate = Ratio(samples.Count, batch.Runs);
                aggregate.populationWinRate = Ratio(
                    aggregate.wins,
                    batch.Runs);
                aggregate.reachRate95 = BalanceStatistics.Wilson95(samples.Count, batch.Runs);
                aggregate.populationWinRate95 = BalanceStatistics.Wilson95(aggregate.wins, batch.Runs);
                report.routeEncounters.Add(aggregate);
                AppendUsage(report, route.Id, nodeId, cardUsage, enemyUsage, rewardUsage);
            }
        }

        private static EncounterMetrics CopyMetrics(StageEncounterResult source)
        {
            return new EncounterMetrics
            {
                Win = source.Win,
                StartingHP = source.EnteringHealth,
                EndingHP = source.EndingHealth,
                HpLost = source.HpLost,
                Turns = source.Turns,
                CardsPlayed = source.CardsPlayed,
                TotalDamage = source.TotalDamage,
                TelemetryRevision = source.TelemetryRevision,
                AppliedDamage = source.AppliedDamage,
                CritDamage = source.CritDamage,
                DoTDamage = source.DoTDamage,
                OverkillDamage = source.OverkillDamage,
                ComboBreakCount = source.ComboBreakCount,
                KillsByDirect = source.KillsByDirect,
                KillsByArea = source.KillsByArea,
                KillsByDoT = source.KillsByDoT,
                ManaSpent = source.ManaSpent,
                DamageTaken = source.DamageTaken,
                ArmorAbsorbed = source.ArmorAbsorbed,
                HealingDone = source.HealingDone,
                EnemyActions = source.EnemyActions,
                KilledEnemies = source.KilledEnemies,
                EscapedEnemies = source.EscapedEnemies,
                EnemyExperienceEarned = source.EnemyExperienceEarned,
                Termination = source.Termination
            };
        }

        private static void AppendEncounterSample(BalanceSimulationReport report,
            string scopeId, string targetId, string nodeId, EncounterSample sample)
        {
            EncounterMetrics m = sample.Metrics;
            report.encounterSamples.Add(new BalanceEncounterSample
            {
                scopeId = scopeId, targetId = targetId, nodeContentId = nodeId,
                seed = sample.Seed, termination = m.Termination.ToString(), win = m.Win,
                startingHealth = m.StartingHP, endingHealth = m.EndingHP, hpLost = m.HpLost,
                damageTaken = m.DamageTaken, armorAbsorbed = m.ArmorAbsorbed,
                healingDone = m.HealingDone, enemyActions = m.EnemyActions,
                turns = m.Turns, cardsPlayed = m.CardsPlayed,
                startingLevel = sample.StartingLevel, endingLevel = sample.EndingLevel,
                startingExperience = sample.StartingExperience, endingExperience = sample.EndingExperience,
                killedEnemies = m.KilledEnemies, escapedEnemies = m.EscapedEnemies,
                enemyExperienceEarned = m.EnemyExperienceEarned,
                telemetryRevision = m.TelemetryRevision,
                appliedDamage = m.AppliedDamage, criticalDamage = m.CritDamage,
                dotDamage = m.DoTDamage, overkillDamage = m.OverkillDamage,
                comboBreaks = m.ComboBreakCount,
                killsByDirect = m.KillsByDirect, killsBySplash = m.KillsByArea,
                killsByDot = m.KillsByDoT, killsByOther = m.KillsByOther
            });
        }

        private static BalanceEncounterAggregate AggregateEncounter(
            string scopeId,
            string targetId,
            string nodeContentId,
            string scenarioId,
            IReadOnlyList<EncounterSample> samples)
        {
            var turns = new List<int>(samples.Count);
            var hpLost = new List<int>(samples.Count);
            var damage = new List<int>(samples.Count);
            var cards = new List<int>(samples.Count);
            var mana = new List<int>(samples.Count);
            var endingHealth = new List<int>(samples.Count);
            int wins = 0;
            int limits = 0;
            var outcomes = new BalanceStatistics();
            for (int i = 0; i < samples.Count; i++)
            {
                EncounterMetrics metrics = samples[i].Metrics;
                if (metrics.Win) wins++;
                if (IsSimulationLimit(metrics.Termination)) limits++;
                turns.Add(metrics.Turns);
                hpLost.Add(metrics.HpLost);
                damage.Add(metrics.TotalDamage);
                cards.Add(metrics.CardsPlayed);
                mana.Add(metrics.ManaSpent);
                endingHealth.Add(metrics.EndingHP);
                outcomes.Add(metrics.Win, IsSimulationLimit(metrics.Termination), metrics.EndingHP);
            }
            return new BalanceEncounterAggregate
            {
                scopeId = scopeId,
                targetId = targetId,
                nodeContentId = nodeContentId,
                scenarioId = scenarioId,
                runs = samples.Count,
                populationRuns = samples.Count,
                wins = wins,
                simulationLimits = limits,
                winRate = Ratio(wins, samples.Count),
                populationWinRate = Ratio(wins, samples.Count),
                reachRate = samples.Count > 0 ? 1f : 0f,
                simulationLimitRate = Ratio(limits, samples.Count),
                medianTurns = Percentile(turns, 0.50f),
                medianHpLost = Percentile(hpLost, 0.50f),
                p90HpLost = Percentile(hpLost, 0.90f),
                medianDamage = Percentile(damage, 0.50f),
                medianCardsPlayed = Percentile(cards, 0.50f),
                medianManaSpent = Percentile(mana, 0.50f),
                medianEndingHealth = Percentile(endingHealth, 0.50f),
                outcomes = outcomes.Summarize(),
                reachRate95 = BalanceStatistics.Wilson95(samples.Count, samples.Count),
                populationWinRate95 = BalanceStatistics.Wilson95(wins, samples.Count)
            };
        }

        private static void AccumulateUsage(
            EncounterMetrics metrics,
            IDictionary<string, CardAccumulator> cards,
            IDictionary<string, EnemyAccumulator> enemies,
            IDictionary<string, RewardAccumulator> rewards)
        {
            AccumulateCards(metrics.CardUsage, cards);
            AccumulateEnemies(metrics.EnemyUsage, enemies);
            AccumulateRewards(metrics.RewardSelections, rewards);
        }

        private static void AccumulateUsage(
            StageEncounterResult metrics,
            IDictionary<string, CardAccumulator> cards,
            IDictionary<string, EnemyAccumulator> enemies,
            IDictionary<string, RewardAccumulator> rewards)
        {
            AccumulateCards(metrics.CardUsage, cards);
            AccumulateEnemies(metrics.EnemyUsage, enemies);
            AccumulateRewards(metrics.RewardSelections, rewards);
        }

        private static void AccumulateCards(
            IReadOnlyList<CardUsageMetric> source,
            IDictionary<string, CardAccumulator> target)
        {
            for (int i = 0; i < source.Count; i++)
            {
                CardUsageMetric item = source[i];
                if (!target.TryGetValue(
                        item.ContentId,
                        out CardAccumulator aggregate))
                {
                    aggregate = new CardAccumulator();
                    target.Add(item.ContentId, aggregate);
                }
                aggregate.Plays += item.Plays;
                aggregate.Damage += item.Damage;
                aggregate.Armor += item.Armor;
                aggregate.Healing += item.Healing;
                aggregate.ManaSpent += item.ManaSpent;
                aggregate.ManaGained += item.ManaGained;
                aggregate.DirectKills += item.DirectKills;
            }
        }

        private static void AccumulateEnemies(
            IReadOnlyList<EnemyActionUsageMetric> source,
            IDictionary<string, EnemyAccumulator> target)
        {
            for (int i = 0; i < source.Count; i++)
            {
                EnemyActionUsageMetric item = source[i];
                if (!target.TryGetValue(
                        item.ContentId,
                        out EnemyAccumulator aggregate))
                {
                    aggregate = new EnemyAccumulator();
                    target.Add(item.ContentId, aggregate);
                }
                aggregate.Actions += item.Actions;
                aggregate.Attacks += item.Attacks;
                aggregate.Defends += item.Defends;
                aggregate.PlayerHealthDamage += item.PlayerHealthDamage;
                aggregate.PlayerArmorDamage += item.PlayerArmorDamage;
                aggregate.ArmorGained += item.ArmorGained;
            }
        }

        private static void AccumulateRewards(
            IReadOnlyList<RewardSelectionMetric> source,
            IDictionary<string, RewardAccumulator> target)
        {
            for (int i = 0; i < source.Count; i++)
            {
                RewardSelectionMetric selection = source[i];
                for (int j = 0; j < selection.Candidates.Count; j++)
                {
                    RewardCandidateMetric candidate = selection.Candidates[j];
                    string key = BuildRewardKey(candidate);
                    if (!target.TryGetValue(
                            key,
                            out RewardAccumulator aggregate))
                    {
                        aggregate = new RewardAccumulator
                        {
                            CardContentId = candidate.CardContentId,
                            RewardType = candidate.Type.ToString()
                        };
                        target.Add(key, aggregate);
                    }
                    aggregate.Offered++;
                    if (candidate.Index == selection.SelectedIndex)
                        aggregate.Selected++;
                }
            }
        }

        private static string BuildRewardKey(RewardCandidateMetric candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate.CardContentId))
                return candidate.Type + ":card:" + candidate.CardContentId;
            return candidate.Type + ":stat:" + candidate.StatType + ":" +
                candidate.StatAmount.ToString(CultureInfo.InvariantCulture);
        }

        private static void AppendUsage(
            BalanceSimulationReport report,
            string scopeId,
            string targetId,
            IDictionary<string, CardAccumulator> cards,
            IDictionary<string, EnemyAccumulator> enemies,
            IDictionary<string, RewardAccumulator> rewards)
        {
            var cardIds = new List<string>(cards.Keys);
            cardIds.Sort(StringComparer.Ordinal);
            for (int i = 0; i < cardIds.Count; i++)
            {
                string id = cardIds[i];
                CardAccumulator item = cards[id];
                report.cards.Add(new BalanceCardAggregate
                {
                    scopeId = scopeId,
                    targetId = targetId,
                    cardContentId = id,
                    plays = item.Plays,
                    damage = item.Damage,
                    armor = item.Armor,
                    healing = item.Healing,
                    manaSpent = item.ManaSpent,
                    manaGained = item.ManaGained,
                    directKills = item.DirectKills,
                    damagePerPlay = Ratio(item.Damage, item.Plays)
                });
            }

            var enemyIds = new List<string>(enemies.Keys);
            enemyIds.Sort(StringComparer.Ordinal);
            for (int i = 0; i < enemyIds.Count; i++)
            {
                string id = enemyIds[i];
                EnemyAccumulator item = enemies[id];
                report.enemies.Add(new BalanceEnemyAggregate
                {
                    scopeId = scopeId,
                    targetId = targetId,
                    enemyContentId = id,
                    actions = item.Actions,
                    attacks = item.Attacks,
                    defends = item.Defends,
                    playerHealthDamage = item.PlayerHealthDamage,
                    playerArmorDamage = item.PlayerArmorDamage,
                    armorGained = item.ArmorGained
                });
            }

            var rewardIds = new List<string>(rewards.Keys);
            rewardIds.Sort(StringComparer.Ordinal);
            for (int i = 0; i < rewardIds.Count; i++)
            {
                string id = rewardIds[i];
                RewardAccumulator item = rewards[id];
                report.rewards.Add(new BalanceRewardAggregate
                {
                    scopeId = scopeId,
                    targetId = targetId,
                    rewardKey = id,
                    cardContentId = item.CardContentId,
                    rewardType = item.RewardType,
                    offered = item.Offered,
                    selected = item.Selected,
                    selectionRateWhenOffered = Ratio(
                        item.Selected,
                        item.Offered)
                });
            }
        }

        internal static ISimulationAgent CreateAgent(BalanceVersionedInput input)
        {
            if (string.Equals(input.Id, "standard", StringComparison.OrdinalIgnoreCase) &&
                input.Revision == StandardAgent.CurrentRevision)
                return new StandardAgent();
            if (string.Equals(input.Id, "random", StringComparison.OrdinalIgnoreCase) &&
                input.Revision == 1)
                return new LegalRandomAgent();
            throw new BalanceSuiteException($"Unsupported agent: {input.Id}@{input.Revision}.");
        }

        internal static ISimulationRewardPolicy CreateRewardPolicy(
            BalanceVersionedInput input)
        {
            if (input.Revision == 1 && input.Id.StartsWith(
                    FirstCardRewardPolicy.IdPrefix, StringComparison.Ordinal))
                return new FirstCardRewardPolicy(
                    input.Id.Substring(FirstCardRewardPolicy.IdPrefix.Length));
            if (string.Equals(
                    input.Id,
                    "standard",
                    StringComparison.OrdinalIgnoreCase))
            {
                var policy = new StandardRewardPolicy();
                if (policy.Revision == input.Revision) return policy;
            }
            throw new BalanceSuiteException(
                $"Unknown reward policy: {input.Id}@{input.Revision}.");
        }

        private static bool IsSimulationLimit(
            SimulationTerminationReason termination)
        {
            return termination == SimulationTerminationReason.MaxTurns ||
                termination == SimulationTerminationReason.CommandLimit;
        }

        private static float Ratio(int numerator, int denominator)
        {
            return denominator > 0 ? (float)numerator / denominator : 0f;
        }

        private static float Percentile(List<int> values, float percentile)
        {
            if (values == null || values.Count == 0) return 0f;
            values.Sort();
            int index = (int)Math.Round(percentile * (values.Count - 1));
            index = Math.Max(0, Math.Min(index, values.Count - 1));
            return values[index];
        }

        private static void SortReport(BalanceSimulationReport report)
        {
            report.isolatedEncounters.Sort((left, right) =>
                string.CompareOrdinal(left.targetId, right.targetId));
            report.routes.Sort((left, right) =>
                string.CompareOrdinal(left.routeId, right.routeId));
            report.routeEncounters.Sort((left, right) =>
            {
                int value = string.CompareOrdinal(left.scopeId, right.scopeId);
                return value != 0
                    ? value
                    : string.CompareOrdinal(left.targetId, right.targetId);
            });
            report.cards.Sort((left, right) => CompareUsage(
                left.scopeId,
                left.targetId,
                left.cardContentId,
                right.scopeId,
                right.targetId,
                right.cardContentId));
            report.enemies.Sort((left, right) => CompareUsage(
                left.scopeId,
                left.targetId,
                left.enemyContentId,
                right.scopeId,
                right.targetId,
                right.enemyContentId));
            report.rewards.Sort((left, right) => CompareUsage(
                left.scopeId,
                left.targetId,
                left.rewardKey,
                right.scopeId,
                right.targetId,
                right.rewardKey));
            report.failures.Sort((left, right) =>
            {
                int value = string.CompareOrdinal(left.scopeId, right.scopeId);
                if (value != 0) return value;
                value = string.CompareOrdinal(left.targetId, right.targetId);
                return value != 0 ? value : left.seed.CompareTo(right.seed);
            });
        }

        private static int CompareUsage(
            string leftScope,
            string leftTarget,
            string leftContent,
            string rightScope,
            string rightTarget,
            string rightContent)
        {
            int value = string.CompareOrdinal(leftScope, rightScope);
            if (value != 0) return value;
            value = string.CompareOrdinal(leftTarget, rightTarget);
            return value != 0
                ? value
                : string.CompareOrdinal(leftContent, rightContent);
        }
    }
}
