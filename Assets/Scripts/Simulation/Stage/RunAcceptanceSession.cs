using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.Progression;
using WanjieScroll.Simulation.Agents;
using WanjieScroll.Simulation.Balance;
using WanjieScroll.Simulation.Protocol;

namespace WanjieScroll.Simulation.Stage
{
    /// <summary>
    /// Owns one isolated acceptance lifecycle. Dungeon owns the Run, Meta owns
    /// the private account, Core owns combat. This driver never installs either
    /// owner in a global store and never substitutes health, deck or route data.
    /// </summary>
    public static class RunAcceptanceSession
    {
        public sealed class EntryProjection
        {
            public string Id;
            public MetaProgression Account;
            public int SourceAccountSeed;
            public bool ForceMetaStatProgression;
            public Action<MetaProgression, PlayerStats> ApplyMetaStats;
            /// <summary>Optional, explicit diagnostic baseline. Ordinary entries
            /// never infer a VC reference from a production battle scenario.</summary>
            public string ReferenceLoadoutContentId;

            internal RunReferenceLoadoutProfile ResolveReferenceLoadout()
            {
                if (string.IsNullOrEmpty(ReferenceLoadoutContentId)) return null;
                RunReferenceLoadoutProfile reference =
                    RunReferenceLoadoutProfileCatalog.Get(ReferenceLoadoutContentId);
                if (reference == null || reference.ValidateProfile().Count != 0)
                    throw new InvalidOperationException("Explicit diagnostic reference loadout is missing or invalid: " + ReferenceLoadoutContentId);
                return reference;
            }

            internal bool SuppressesMetaStats(BattleScenarioProfile scenario)
                => !ForceMetaStatProgression &&
                    (ResolveReferenceLoadout() ?? scenario?.ResolveReferenceLoadout())
                    ?.suppressMetaStatProgression == true;

            internal void Validate()
            {
                if (string.IsNullOrWhiteSpace(Id) || Account == null)
                    throw new ArgumentException("Projected acceptance entry requires an id and account.");
                if (ApplyMetaStats == null)
                    throw new ArgumentException("Projected acceptance entry requires the production stat projector.");
                ResolveReferenceLoadout();
            }
        }

        public static RunAcceptanceReport Run(BalanceSimulationSuite suite, int seedStart,
            int count, RunAcceptanceSettings settings = null)
            => RunProjected(suite, seedStart, count, settings, null);

        public static RunAcceptanceReport RunProjected(BalanceSimulationSuite suite, int seedStart,
            int count, RunAcceptanceSettings settings,
            Func<int, EntryProjection> entryProjection)
        {
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            if (suite.EntryState.Kind != BalanceEntryStateKind.FreshRun)
                throw new ArgumentException("Main acceptance requires a fresh formal Run, not a checkpoint.");
            settings = JsonUtility.FromJson<RunAcceptanceSettings>(
                JsonUtility.ToJson(settings ?? new RunAcceptanceSettings()));
            settings.Validate();
            RunAcceptanceReport report = Collect(seedStart, count, settings,
                seed => new SeedRun(suite, seed, settings,
                    entryProjection?.Invoke(seed)).Execute());
            report.sourceSuiteId = suite.SuiteId;
            report.sourceSuiteSha256 = suite.SourceSha256;
            return report;
        }

        /// <summary>The batch contract retains every requested seed, including structural errors.</summary>
        internal static RunAcceptanceReport Collect(int seedStart, int count,
            RunAcceptanceSettings settings, Func<int, RunAcceptanceSample> execute)
        {
            settings.Validate();
            if (seedStart <= 0 || count <= 0 || count > 100000 ||
                (long)seedStart + count - 1 > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(count));
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            var report = new RunAcceptanceReport { runId = "main-acceptance." + Guid.NewGuid().ToString("N"),
                settings = settings, seedStart = seedStart, seedEnd = seedStart + count - 1, requestedCount = count };
            for (int i = 0; i < count; i++)
            {
                int seed = seedStart + i;
                RunAcceptanceSample sample;
                try
                {
                    sample = execute(seed) ?? throw new InvalidOperationException("Run returned no sample.");
                    if (sample.seed != seed || sample.gameRunId != settings.GameRunId(seed))
                        throw new InvalidOperationException("Sample identity differs from the requested game Run.");
                }
                catch (Exception exception)
                {
                    sample = new RunAcceptanceSample { seed = seed, gameRunId = settings.GameRunId(seed),
                        outcome = RunAcceptanceOutcome.Error, failureReason = exception.ToString(), terminalPayload = "{}" };
                }
                report.samples.Add(sample);
            }
            report.Summarize();
            return report;
        }

        /// <summary>Replace an isolated simulation account from a declared projection.
        /// JsonUtility covers the public account fields; dictionary and permanent
        /// facts are restored explicitly through their existing owners.</summary>
        internal static void RestoreProjectedAccount(MetaProgression source, MetaProgression target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (ReferenceEquals(source, target))
                throw new ArgumentException("Projected simulation account must have a separate owner.");
            if (source.EventUnlocks.IsCommitting || source.ExperienceCollections.IsCommitting ||
                target.EventUnlocks.IsCommitting || target.ExperienceCollections.IsCommitting)
                throw new InvalidOperationException("A projected account cannot replace a committing owner.");
            // Validate and capture every private owner before replacing public fields.
            CardUnlockProgressSnapshot facts = source.CardUnlocks.Capture();
            EventUnlockProgressSnapshot events = source.EventUnlocks.Capture();
            ExperienceCollectionProgressSnapshot experience = source.ExperienceCollections.Capture();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), target);
            target.powerUpLevels = new Dictionary<string, int>(
                source.powerUpLevels ?? new Dictionary<string, int>());
            target.CardUnlocks.Restore(facts);
            target.EventUnlocks.Restore(events);
            target.ExperienceCollections.Restore(experience);
        }

        /// <summary>Commit only after Dungeon accepts a real simulation result.
        /// This account belongs to the isolated session: its persistence boundary
        /// is memory, and replay/planning factories never invoke this method.</summary>
        internal static void CommitAcceptedUnlocks(MetaProgression account,
            DungeonRunSession acceptedRun, CardUnlockRequirements requirements, Action persistMemory = null)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (acceptedRun == null) throw new ArgumentNullException(nameof(acceptedRun));
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (account.EventUnlocks.IsCommitting || account.ExperienceCollections.IsCommitting)
                throw new InvalidOperationException("Simulation facts are already committing.");
            var accepted = CardUnlockAcceptance.FromRun(acceptedRun);
            persistMemory ??= () => { }; // Isolated memory publication; never a player disk save.
            Action commitUnlocksInMemory = () =>
            {
                // This isolated account has an explicit memory persistence boundary.
                // The card/event transaction retains its own rollback; a rejection
                // must throw so the enclosing XP watermark rolls back as well.
                if (!CardUnlockLevelTransaction.TryCommit(account, accepted, requirements,
                    persistMemory, () => { }, out _, out string innerError))
                    throw new InvalidOperationException(innerError);
            };
            bool committed = accepted.Conclusion == EventUnlockRunConclusion.None
                ? account.ExperienceCollections.TryCommitRunFacts(accepted.RunId,
                    acceptedRun.ExperienceCollections.RunTotals, commitUnlocksInMemory, out string error)
                : account.ExperienceCollections.TryCommitRunConclusion(accepted.RunId,
                    acceptedRun.ExperienceCollections.RunTotals, accepted.Conclusion, commitUnlocksInMemory, out error);
            if (!committed) throw new InvalidOperationException(error);
        }

        // The seed lifecycle owns its collaborators; combat calls do not
        // shuttle a growing parameter bag between shallow static helpers.
        private sealed class SeedRun
        {
            private readonly BalanceSimulationSuite suite;
            private readonly RunAcceptanceSettings settings;
            private readonly RunAcceptanceSample sample;
            private readonly MetaProgression account = new MetaProgression();
            private readonly SeededRandomSource random;
            private readonly RunIdentities identities = new RunIdentities();
            private readonly List<CrawlerConfig> heroes = new List<CrawlerConfig>();
            private readonly StageEncounterAssembler assembler = new StageEncounterAssembler();
            private readonly EntryProjection entryProjection;
            private DungeonRunSession run;
            private RunAcceptancePolicy policy;
            private RunAcceptanceEconomy economy;

            internal SeedRun(BalanceSimulationSuite suite, int seed, RunAcceptanceSettings settings,
                EntryProjection entryProjection)
            {
                this.suite = suite; this.settings = settings;
                this.entryProjection = entryProjection;
                random = new SeededRandomSource(seed);
                sample = new RunAcceptanceSample { seed = seed, gameRunId = settings.GameRunId(seed),
                    outcome = RunAcceptanceOutcome.Error, managedBytesBefore = GC.GetTotalMemory(false) };
            }

            internal RunAcceptanceSample Execute()
            {
                var clock = Stopwatch.StartNew();
            try
            {
                PrepareAccount();
                sample.initialPowerUps = CapturePowerUps(account);
                sample.initialAccountPayload = JsonUtility.ToJson(account, true);
                sample.initialHighestPlayerLevel = account.CardUnlocks.HighestPlayerLevel;
                sample.initialUnlockProgress = CardUnlockProgressSaveData.Capture(account.CardUnlocks.Capture());
                sample.initialEventUnlockProgress = EventUnlockProgressSaveData.Capture(account.EventUnlocks.Capture());
                sample.initialExperienceCollectionProgress = account.ExperienceCollections.CaptureSaveData();
                LevelRunProfile firstFloor = LevelRunProfileCatalog.Get(suite.FloorContentId);
                if (firstFloor.floorIndex > 1) throw new InvalidOperationException("Fresh entry is not a first floor.");
                // PrepareAccount has finished cloning/replaying this seed's
                // private account. Freeze Run-only admission once, not per battle.
                run = DungeonRunSession.Begin(firstFloor, suite.SnapshotId, sample.gameRunId, identities,
                    runSeed: sample.seed,
                    cardPlayGrowthEnabled: RunStartService.CaptureCardPlayGrowthAdmission(account),
                    leaderCrawlerContentId: entryProjection?.ResolveReferenceLoadout()?.crawlerContentId ??
                        suite.EntryRule.SelectedHeroContentId,
                    commonEncounterSources: id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id));
                economy = new RunAcceptanceEconomy(run.RunProgression, account);
                Initialize(run, suite, heroes, CombatPlayerProjectionFor(
                    RunAcceptanceDecisions.ResolveScenario(run.CurrentFloor), false),
                    entryProjection?.ResolveReferenceLoadout());
                sample.entries.Add(Capture(run));
                for (int decisionIndex = 0; decisionIndex < settings.maxNodeDecisions; decisionIndex++)
                {
                    policy = new RunAcceptancePolicy(settings.objective, heroes,
                        RunCombatRules.Create(account, run.CurrentFloor));
                    using RunAcceptanceDecisions decisions = RunAcceptanceDecisions.Open(run, policy,
                        economy, random, sample, MapPlayerProjection());
                    RunAcceptanceDecision selected = decisions.ChooseNode();
                    LevelNode node = run.CurrentFloor.FindNode(selected.selectedId);
                    sample.failedFloorContentId = run.CurrentFloorContentId;
                    sample.failedNodeContentId = node.contentId;
                    if (node.nodeType == LevelNodeType.Encounter || node.nodeType == LevelNodeType.Finale)
                    {
                        bool terminal = ExecuteCombat(node, decisions);
                        selected.healthAfter = run.CurrentHealth;
                        if (terminal) break;
                    }
                    else
                    {
                        DungeonRunFloorExitResult exit = decisions.ExecuteInteraction(node);
                        economy.Synchronize();
                        selected.healthAfter = run.CurrentHealth;
                        if (exit?.NewlyCommitted == true)
                            sample.entries.Add(Capture(run));
                    }
                    if (decisionIndex == settings.maxNodeDecisions - 1)
                    {
                        sample.outcome = RunAcceptanceOutcome.SimulationLimit;
                        sample.failureReason = "MaximumNodeDecisions";
                    }
                }
                if (sample.outcome == RunAcceptanceOutcome.Victory)
                { sample.failedFloorContentId = null; sample.failedNodeContentId = null; }
            }
            catch (Exception exception)
            {
                sample.outcome = RunAcceptanceOutcome.Error;
                sample.failureReason = exception.ToString();
            }
            finally
            {
                if (run != null)
                {
                    try
                    {
                        economy.Synchronize();
                        sample.terminal ??= Capture(run);
                        sample.terminalPayload = run.Serialize();
                        sample.collectedJade = run.RunProgression.RunJadeEarned;
                        sample.dungeonBonusJade = run.DungeonBonusJade;
                        sample.finaleCompleted = HasCompletedFinale(run);
                        sample.conclusionFrozen = run.TryGetConclusion(out _);
                        sample.pendingTransaction = HasPendingTransaction(run);
                    }
                    catch (Exception exception)
                    { sample.outcome = RunAcceptanceOutcome.Error; sample.failureReason += "\nCapture: " + exception; }
                }
                sample.terminalPayload ??= "{}";
                sample.accountJade = account.lingyu;
                sample.finalAccountPayload = JsonUtility.ToJson(account, true);
                sample.finalHighestPlayerLevel = account.CardUnlocks.HighestPlayerLevel;
                sample.finalUnlockProgress = CardUnlockProgressSaveData.Capture(account.CardUnlocks.Capture());
                sample.finalEventUnlockProgress = EventUnlockProgressSaveData.Capture(account.EventUnlocks.Capture());
                // A failed account preparation still exports an explicit empty
                // starting ledger, never an inferred historical collection.
                sample.initialExperienceCollectionProgress ??= new ExperienceCollectionProgress().CaptureSaveData();
                sample.finalExperienceCollectionProgress = account.ExperienceCollections.CaptureSaveData();
                sample.experienceCollectionProgressSchemaVersion = ExperienceCollectionProgress.CurrentVersion;
                sample.eventUnlockProgressSchemaVersion = EventUnlockProgressSaveData.CurrentSchemaVersion;
                sample.accountProgressSchemaVersion = CardUnlockProgressSaveData.CurrentSchemaVersion;
                sample.accountTotalJadeEarned = account.totalLingyuEarned;
                sample.finalPowerUps = CapturePowerUps(account);
                sample.unlockedContentIds = account.unlockedRelics.Concat(account.unlockedCrawlers)
                    .Concat(account.unlockedCards).Concat(account.unlockedArcanas).OrderBy(id => id).ToList();
                foreach (CrawlerConfig hero in heroes) if (hero != null) UnityEngine.Object.DestroyImmediate(hero);
                clock.Stop(); sample.elapsedMilliseconds = clock.ElapsedMilliseconds;
                sample.managedBytesAfter = GC.GetTotalMemory(false);
            }
            return sample;
        }

        private void PrepareAccount()
        {
            if (entryProjection == null)
            {
                PrepareStartingAccount(account, suite.EntryRule.SelectedHeroContentId);
                sample.entryProjectionId = "post-tutorial-no-upgrades";
                return;
            }
            entryProjection.Validate();
            RestoreProjectedAccount(entryProjection.Account, account);
            sample.entryProjectionId = entryProjection.Id;
            sample.sourceAccountSeed = entryProjection.SourceAccountSeed;
            sample.forceMetaStatProgression = entryProjection.ForceMetaStatProgression;
        }

        private Action<PlayerStats> CombatPlayerProjectionFor(
            BattleScenarioProfile scenario, bool recordCombat)
        {
            if (entryProjection == null) return null;
            if (entryProjection.SuppressesMetaStats(scenario))
            {
                if (recordCombat) sample.metaSuppressedCombatCount++;
                return null;
            }
            if (recordCombat) sample.metaProjectedCombatCount++;
            return stats => entryProjection.ApplyMetaStats(account, stats);
        }

        // Production map economy reads Daozang greed independently from a
        // battle reference loadout's combat-only suppression flag.
        private Action<PlayerStats> MapPlayerProjection()
        {
            if (entryProjection == null)
                return null;
            return stats => entryProjection.ApplyMetaStats(account, stats);
        }

        private static List<RunAcceptancePowerUpLevel> CapturePowerUps(MetaProgression source)
            => (source?.powerUpLevels ?? new Dictionary<string, int>())
                .Where(pair => pair.Value > 0)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RunAcceptancePowerUpLevel
                    { contentId = pair.Key, level = pair.Value }).ToList();

        private bool ExecuteCombat(LevelNode node, RunAcceptanceDecisions decisions)
        {
            RunAcceptanceDecisions.Require(run.CurrentFloor.TryBeginEncounter(node.contentId,
                out LevelEncounterLaunch launch, out string error), error);
            BattleScenarioProfile scenario = BattleScenarioCatalog.Get(launch.ScenarioId);
            RequireSelection(suite, scenario, entryProjection?.ResolveReferenceLoadout());
            CombatRuleSet rules = RunCombatRules.Create(account, run.CurrentFloor);
            int plannerSeed = unchecked(sample.seed * 397 ^ (sample.combats.Count + 1) * 7919 ^ 0x37A219);
            var trace = new RunAcceptanceTrace { initialRunPayload = run.Serialize(),
                scenarioId = scenario.scenarioId, nextInstanceId = identities.Next,
                plannerSeed = plannerSeed, entryExperienceGrant = scenario.entryExperienceGrant,
                budget = settings, rules = rules.Clone() };
            trace.CaptureRewardAccount(account);
            Action<PlayerStats> playerProjection = CombatPlayerProjectionFor(scenario, true);
            var planning = new CombatPlanningOptions(assembler.CaptureBranchFactory(
                trace.initialRunPayload, scenario, identities.Next, rules, playerProjection, account), plannerSeed, settings.searchDepth, settings.beamWidth,
                settings.futureSamples, settings.maxExpansions)
            { EntryIdentity = sample.gameRunId + "/" + run.CurrentFloorContentId + "/" + node.contentId };
            var record = new RunAcceptanceCombat { floorContentId = run.CurrentFloorContentId,
                nodeContentId = node.contentId, scenarioId = scenario.scenarioId, startingHealth = run.CurrentHealth };
            record.tracePayload = JsonUtility.ToJson(trace, true);
            sample.combats.Add(record);
            using StageEncounterAssembly assembly = assembler.Create(run, scenario, random, identities,
                rules, playerProjection, account);
            CombatController combat = assembly.Combat;
            EncounterMetrics metrics = null;
            bool committed = false;
            try
            {
                metrics = SimulationRunner.Run(combat, new StandardAgent(), settings.maxTurnsPerEncounter,
                    true, policy, scenario.entryExperienceGrant, planning);
                if (metrics.Termination == SimulationTerminationReason.MaxTurns ||
                    metrics.Termination == SimulationTerminationReason.CommandLimit)
                {
                    sample.outcome = RunAcceptanceOutcome.SimulationLimit;
                    sample.failureReason = metrics.Termination.ToString();
                    return true;
                }
                if (metrics.Termination == SimulationTerminationReason.Defeat)
                {
                    if (combat.Player.currentHealth > 0 || combat.IsVictory)
                        throw new InvalidOperationException("Defeat metrics do not describe a real gameplay defeat.");
                    RunAcceptanceDecisions.Require(run.TryCompleteDefeat(combat, out _, out error), error);
                    economy.Synchronize();
                    RunAcceptanceDecisions.Require(DungeonRunSettlement.TrySettleDefeat(run, out _, out error), error);
                    CommitAcceptedUnlocks(account, run, assembly.UnlockRequirements);
                    committed = true;
                    sample.outcome = RunAcceptanceOutcome.Defeat;
                    sample.failureReason = "GameplayDefeat";
                    return true;
                }
                if (metrics.Termination != SimulationTerminationReason.Victory ||
                    !metrics.Win ||
                    combat.State.CurrentPhase != BattlePhase.BattleVictory ||
                    combat.AwaitingRewardSelection ||
                    combat.HasPendingExperienceAwards)
                    throw new InvalidOperationException("Combat did not return a supported authoritative terminal outcome.");
                RunAcceptanceDecisions.Require(run.TryCompleteVictory(combat, out _, out error), error);
                CommitAcceptedUnlocks(account, run, assembly.UnlockRequirements);
                committed = true;
                decisions.ResolveVictorySeal(node.contentId);
                economy.Synchronize();
                if (node.nodeType == LevelNodeType.Finale)
                {
                    RunAcceptanceDecisions.Require(DungeonRunSettlement.TrySettleVictory(run, account,
                        out DungeonRunResultSnapshot result, out error, persist: () => { }), error); // Isolated account memory boundary.
                    if (result.Status != DungeonRunResultStatus.Victory || HasPendingTransaction(run))
                        throw new InvalidOperationException("Victory did not finish all formal settlement transactions.");
                    sample.outcome = RunAcceptanceOutcome.Victory;
                    return true;
                }
                RunAcceptanceDecisions.Require(run.CurrentFloor.TryAcknowledgeReturn(node.contentId),
                    "Cannot acknowledge completed encounter return.");
                return false;
            }
            finally
            {
                record.endingHealth = combat.Player.currentHealth;
                record.termination = metrics?.Termination.ToString() ?? "Error";
                record.turns = metrics?.Turns ?? combat.State.CurrentTurn;
                record.cardsPlayed = metrics?.CardsPlayed ?? 0;
                record.maxCombo = metrics?.MaxCombo ?? 0;
                record.armorGenerated = metrics?.ArmorGenerated ?? 0;
                record.healing = metrics?.HealingDone ?? 0;
                record.cardsDrawn = metrics?.CardsDrawn ?? 0;
                record.damage = metrics?.TotalDamage ?? 0;
                record.comboBreaks = metrics?.ComboBreakCount ?? 0;
                record.telemetryRevision = metrics?.TelemetryRevision ?? 0;
                record.appliedDamage = metrics?.AppliedDamage ?? 0;
                record.criticalDamage = metrics?.CritDamage ?? 0;
                record.dotDamage = metrics?.DoTDamage ?? 0;
                record.overkillDamage = metrics?.OverkillDamage ?? 0;
                record.killedEnemies = metrics?.KilledEnemies ?? 0;
                record.killsByDirect = metrics?.KillsByDirect ?? 0;
                record.killsBySplash = metrics?.KillsByArea ?? 0;
                record.killsByDot = metrics?.KillsByDoT ?? 0;
                record.killsByOther = metrics?.KillsByOther ?? 0;
                record.decisions = planning.Statistics.Decisions;
                record.expandedNodes = planning.Statistics.ExpandedNodes;
                record.replayedCommands = planning.Statistics.ReplayedCommands;
                record.budgetExhaustions = planning.Statistics.BudgetExhaustions;
                record.hiddenBoundaryStops = planning.Statistics.HiddenBoundaryStops;
                record.maximumSearchDepth = planning.Statistics.MaximumDepth;
                record.crawlerTriggers = planning.Statistics.CrawlerTriggers;
                record.searchMilliseconds = planning.Statistics.ElapsedMilliseconds;
                sample.totalTurns += record.turns; sample.totalCardsPlayed += record.cardsPlayed;
                trace.Finish(planning, metrics);
                record.tracePayload = JsonUtility.ToJson(trace, true);
                if (settings.validateReplay && metrics != null)
                {
                    if (metrics.Termination == SimulationTerminationReason.Victory)
                    {
                        RunAcceptanceObservation terminal = trace.final;
                        if (terminal == null ||
                            terminal.outcome != CombatOutcome.Victory.ToString() ||
                            terminal.phase != BattlePhase.BattleVictory ||
                            terminal.awaitingReward)
                            throw new InvalidOperationException(
                                "Victory trace did not reach formal BattleVictory after all rewards.");
                    }
                    var replayClock = Stopwatch.StartNew();
                    // Deserialize first: this validates the exported fields,
                    // not a still-live protocol object or planner closure.
                    RunAcceptanceTrace.Deserialize(record.tracePayload).ValidateReplay();
                    record.replayValidationMilliseconds = replayClock.ElapsedMilliseconds;
                    record.replayValidated = true;
                }
                if (!committed)
                {
                    // An infrastructure limit is not a formal defeat. Preserve
                    // the active Run plus actual combat trace without faking a
                    // terminal transaction merely to obtain a save payload.
                    sample.terminal = CaptureCards(run, combat.Deck.Concat(combat.Hand).Concat(combat.Discard));
                    sample.terminal.health = combat.Player.currentHealth;
                    sample.terminal.maxHealth = combat.Player.maxHealth;
                }
            }
        }

        }

        internal static void Initialize(DungeonRunSession run, BalanceSimulationSuite suite,
            List<CrawlerConfig> heroes, Action<PlayerStats> playerProjection,
            RunReferenceLoadoutProfile diagnosticReference)
        {
            BattleScenarioProfile scenario = RunAcceptanceDecisions.ResolveScenario(run.CurrentFloor);
            RequireSelection(suite, scenario, diagnosticReference);
            List<CardConfig> pool = ContentRuntimeFactory.CreateCardPool(scenario.volumeId);
            try
            {
                RunReferenceLoadoutProfile reference =
                    diagnosticReference ?? scenario.ResolveReferenceLoadout();
                List<CardConfig> starting = reference != null
                    ? reference.ResolveDeck(
                        pool, CardTestStatus.CanUseInFormalPlay)
                    : CrawlerStartingDeckResolver.ComposeRunEntryDeck(
                        scenario.deckContentIds,
                        scenario.crawlerContentId,
                        suite.EntryRule.SelectedHeroContentId,
                        pool,
                        CardTestStatus.CanUseInFormalPlay);
                PlayerStats player = scenario.CreatePlayer();
                diagnosticReference?.ApplyTo(player);
                playerProjection?.Invoke(player);
                RunAcceptanceDecisions.Require(run.TryPrepareCombat(player, starting, pool,
                    out List<CardConfig> instances, out string error), error);
                RunAcceptanceDecisions.DestroyCards(instances);
                foreach (string id in pool.Where(c => c.effects != null).SelectMany(c => c.effects)
                             .Where(e => e != null && e.effectType == EffectType.SummonCrawler)
                             .Select(e => e.crawlerContentId).Where(id => !string.IsNullOrEmpty(id)).Distinct())
                    heroes.Add(ContentRuntimeFactory.CreateCrawlerByContentId(id));
            }
            finally { RunAcceptanceDecisions.DestroyCards(pool); }
        }

        private static void RequireSelection(BalanceSimulationSuite suite, BattleScenarioProfile scenario,
            RunReferenceLoadoutProfile diagnosticReference)
        {
            if (scenario == null || scenario.testOnlyUnconfirmed)
                throw new InvalidOperationException("Scenario is not a formal entry for the selected hero slice.");
            if (diagnosticReference != null)
            {
                if (!string.Equals(diagnosticReference.crawlerContentId,
                    suite.EntryRule.SelectedHeroContentId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Diagnostic reference leader differs from the declared suite hero.");
                return;
            }
            // VC 实测参考装载是固定起手，不会因验收套件的项目英灵选择而
            // 换成另一套牌组；实际牌组由该装载在 Initialize 中唯一解析。
            if (scenario.HasReferenceLoadout)
                return;
            if (!suite.EntryRule.CreateStageInput().MatchesScenarioSelection(
                    scenario.crawlerContentId))
            {
                throw new InvalidOperationException(
                    "Scenario is not a formal entry for the selected hero slice.");
            }
        }

        internal static void PrepareStartingAccount(MetaProgression account, string selectedHero)
        {
            // 账号起点语义的唯一所有者是 ProgressAccountBlueprints；
            // 验收管线与编辑器测试存档菜单共享同一套回放，不得各自复制。
            ProgressAccountBlueprints.ReplayTutorial(account, selectedHero);
        }

        internal static RunAcceptanceEntry Capture(DungeonRunSession run)
        {
            List<CardConfig> pool = ContentRuntimeFactory.CreateCardPool(
                RunAcceptanceDecisions.ResolveScenario(run.CurrentFloor).volumeId);
            List<CardConfig> cards = null;
            try
            {
                RunAcceptanceDecisions.Require(run.TryGetCurrentCards(pool, out cards, out string error), error);
                RunAcceptanceEntry entry = CaptureCards(run, cards);
                entry.payload = run.Serialize();
                return entry;
            }
            finally { RunAcceptanceDecisions.DestroyCards(cards); RunAcceptanceDecisions.DestroyCards(pool); }
        }

        private static RunAcceptanceEntry CaptureCards(DungeonRunSession run, IEnumerable<CardConfig> cards)
            => new RunAcceptanceEntry { floorContentId = run.CurrentFloorContentId,
                snapshotId = run.CurrentFloor.SnapshotId, health = run.CurrentHealth, maxHealth = run.MaxHealth,
                level = run.CombatProgression.CurrentLevel, experience = run.CombatProgression.CurrentExperience,
                xpFractionCarryUnits = run.CombatProgression.XpFractionCarryUnits,
                runJadeEarned = run.RunProgression.RunJadeEarned,
                baseManaBonus = run.BaseManaBonus,
                baseArmorPerTurnBonus = run.BaseArmorPerTurnBonus,
                mightBonus = run.MightBonus,
                handLimitBonus = run.HandLimitBonus,
                areaBonus = run.AreaBonus,
                runLuckBonus = run.RunProgression.RunLuckBonus,
                runGreedBonus = run.RunProgression.RunGreedBonus,
                runGrowthBonus = run.RunProgression.RunGrowthBonus,
                cards = cards.Where(c => c != null).Select(c => new RunAcceptanceCard { contentId = c.contentId,
                    instanceId = c.runtimeInstanceId, limitBreak = c.LimitBreakCount,
                    seals = c.seals?.Select(s => s.contentId).ToList() ?? new List<string>() }).ToList() };

        internal static bool HasPendingTransaction(DungeonRunSession run)
            => run.CurrentFloor.ActiveEncounter.HasValue ||
               !string.IsNullOrEmpty(run.CurrentFloor.ActiveInteractionNodeId) ||
               run.CurrentFloor.TryGetPendingReturn(out _) || run.CurrentFloor.TryGetPendingVictorySealReward(out _) ||
               run.Interactions.HasPendingChoice;

        internal static bool HasCompletedFinale(DungeonRunSession run)
            => run.CurrentFloor.Nodes.Any(node => node != null && node.nodeType == LevelNodeType.Finale &&
                run.CurrentFloor.GetNodeState(node.contentId) == LevelNodeViewState.Completed);

        private sealed class RunIdentities : IInstanceIdGenerator
        {
            internal int Next { get; private set; } = 1;
            public int NextId()
            { int id = Next; Next = checked(Next + 1); return id; }
        }
    }
}
