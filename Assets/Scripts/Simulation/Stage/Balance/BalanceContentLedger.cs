using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Simulation.Stage;

namespace WanjieScroll.Simulation.Balance
{
    /// <summary>
    /// Captures the production inputs that actually feed combat assembly. The
    /// suite hash covers experiment intent; this ledger covers resolved floor,
    /// scenario, card, enemy, progression, formation, and reward semantics.
    /// </summary>
    internal static class BalanceContentLedger
    {
        [Serializable]
        private sealed class CheckpointLedgerDocument
        {
            public string currentFloorContentId;
            public int currentLevel;
            public int currentExperience;
            public int runLuckBonus;
            public int runGreedBonus;
            public int runJadeEarned;
            public int baseManaBonus;
            public int maxHealth;
            public int currentHealth;
            public List<CheckpointCardDocument> cards;
            public CheckpointFloorDocument currentFloor;
        }

        [Serializable]
        private sealed class CheckpointCardDocument
        {
            public string contentId;
            public int runtimeInstanceId;
            public List<CardSealSpec> seals;
        }

        [Serializable]
        private sealed class CheckpointFloorDocument
        {
            public string snapshotId;
        }

        public static List<BalanceLedgerEntry> Build(
            BalanceSimulationSuite suite,
            LevelRunProfile floor)
        {
            var result = new List<BalanceLedgerEntry>();
            var crawlerContentIds = new HashSet<string>(StringComparer.Ordinal);
            AddCrawlerId(crawlerContentIds, suite.EntryRule.SelectedHeroContentId);
            AddSuiteEntries(result, suite);
            AddFloorEntries(result, suite, floor);

            for (int i = 0; i < floor.nodes.Count; i++)
            {
                LevelNode node = floor.nodes[i];
                if (node == null) continue;
                AddNodeEntry(result, node, i);
                if (node.nodeType != LevelNodeType.Encounter &&
                    node.nodeType != LevelNodeType.Finale)
                {
                    continue;
                }

                BattleScenarioProfile scenario =
                    BattleScenarioCatalog.Get(node.scenarioId);
                if (scenario == null) continue;
                bool generated = node.HasCommonEncounter;
                if (generated) AddCommonEncounterSource(result, node);
                EncounterScenarioSpec spec = generated
                    ? ScenarioFactory.FromProfileForSourceInspection(scenario, suite.EntryRule.CreateRules())
                    : ScenarioFactory.FromProfile(scenario, suite.EntryRule.CreateRules());
                using (var owned = new StageEncounterAssembly(spec))
                {
                    AddScenarioEntries(
                        result,
                        floor,
                        node,
                        scenario,
                        spec,
                        crawlerContentIds);
                }
            }
            AddCrawlerEntries(result, crawlerContentIds);
            return result;
        }

        public static string BuildFingerprint(
            BalanceSimulationSuite suite,
            IReadOnlyList<BalanceLedgerEntry> ledger)
        {
            var lines = new List<string>
            {
                "pipeline=balance-v2",
                "suiteSha256=" + suite.SourceSha256,
                "checkpointSha256=" + suite.EntryState.CheckpointSha256
            };
            for (int i = 0; i < ledger.Count; i++)
            {
                BalanceLedgerEntry item = ledger[i];
                lines.Add(string.Join("|", new[]
                {
                    item.nodeContentId ?? string.Empty,
                    item.scenarioId ?? string.Empty,
                    item.itemKind ?? string.Empty,
                    item.contentId ?? string.Empty,
                    Number(item.ordinal),
                    Number(item.count),
                    Number(item.health),
                    Number(item.attack),
                    Number(item.experience),
                    Number(item.armor),
                    Number(item.manaCost),
                    Number(item.damage),
                    Number(item.healing),
                    Number(item.manaGain),
                    item.behavior ?? string.Empty,
                    item.notes ?? string.Empty
                }));
            }
            lines.Sort(StringComparer.Ordinal);
            return Sha256(string.Join("\n", lines));
        }

        private static void AddSuiteEntries(
            ICollection<BalanceLedgerEntry> result,
            BalanceSimulationSuite suite)
        {
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "suite",
                contentId = suite.SuiteId,
                count = suite.Revision,
                notes = Join(
                    "sourceSha256=" + suite.SourceSha256,
                    "floor=" + suite.FloorContentId,
                    "resource=" + suite.FloorResourcePath,
                    "snapshot=" + suite.SnapshotId,
                    "coverage=" + suite.CoverageMode)
            });
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "entry_state",
                contentId = suite.EntryState.Kind.ToString(),
                count = suite.EntryState.Revision,
                notes = Join(
                    "checkpointFile=" + suite.EntryState.CheckpointFile,
                    "checkpointSha256=" +
                    suite.EntryState.CheckpointSha256)
            });
            AddCheckpointEntries(result, suite);
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "entry_build_scope",
                contentId = BalanceSimulationReport.EntryBuildScopeId,
                notes =
                    "Production Scenario, Checkpoint and Suite rules are " +
                    "applied. Meta progression and Daozang projection are " +
                    "not sampled by this pipeline."
            });
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "entry_rule",
                contentId = suite.EntryRule.Id,
                count = suite.EntryRule.Revision,
                notes = Join(
                    "hero=" + suite.EntryRule.SelectedHeroContentId,
                    "rules=" + JsonUtility.ToJson(
                        suite.EntryRule.CreateRules()))
            });
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "agent",
                contentId = suite.Agent.Id,
                count = suite.Agent.Revision
            });
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "reward_policy",
                contentId = suite.RewardPolicy.Id,
                count = suite.RewardPolicy.Revision
            });

            for (int i = 0; i < suite.IsolatedEncounters.Count; i++)
            {
                BalanceEncounterDefinition item =
                    suite.IsolatedEncounters[i];
                result.Add(new BalanceLedgerEntry
                {
                    nodeContentId = item.NodeContentId,
                    itemKind = "suite_isolated_target",
                    contentId = item.Id,
                    ordinal = i,
                    notes = "quick=" + item.Quick
                });
            }
            for (int i = 0; i < suite.Routes.Count; i++)
            {
                BalanceRouteDefinition route = suite.Routes[i];
                result.Add(new BalanceLedgerEntry
                {
                    itemKind = "suite_route",
                    contentId = route.Id,
                    ordinal = i,
                    count = route.Revision,
                    notes = Join(
                        "purpose=" + route.Purpose,
                        "quick=" + route.Quick,
                        "nodes=" + string.Join(",", route.NodeContentIds))
                });
                if (suite.CoverageMode !=
                    BalanceCoverageMode.ProductionRoute)
                {
                    continue;
                }
                for (int stepIndex = 0;
                     stepIndex < route.Steps.Count;
                     stepIndex++)
                {
                    StageSimulationStep step = route.Steps[stepIndex];
                    result.Add(new BalanceLedgerEntry
                    {
                        nodeContentId = step.NodeContentId,
                        itemKind = "suite_route_step",
                        contentId = route.Id,
                        ordinal = stepIndex,
                        notes = Join(
                            "kind=" + step.Kind,
                            "targetCard=" + step.TargetCardContentId,
                            "occurrence=" + step.TargetCardOccurrence,
                            "victorySeal=" +
                            step.VictorySealDisposition,
                            "victorySealContentId=" + step.VictorySealContentId,
                            "cardConversion=" +
                            step.CardConversionDisposition)
                    });
                }
            }
            for (int i = 0; i < suite.CoverageExclusions.Count; i++)
            {
                BalanceCoverageExclusion exclusion =
                    suite.CoverageExclusions[i];
                result.Add(new BalanceLedgerEntry
                {
                    nodeContentId = exclusion.NodeContentId,
                    itemKind = "coverage_exclusion",
                    contentId = exclusion.NodeContentId,
                    ordinal = i,
                    notes = exclusion.Reason
                });
            }
            for (int i = 0; i < suite.Checks.Count; i++)
            {
                BalanceCheckGroup group = suite.Checks[i];
                for (int j = 0; j < group.Conditions.Count; j++)
                {
                    BalanceCheckCondition condition = group.Conditions[j];
                    result.Add(new BalanceLedgerEntry
                    {
                        itemKind = "balance_check",
                        contentId = group.Id,
                        ordinal = j,
                        notes = Join(
                            "mode=" + group.Mode,
                            "severity=" + group.Severity,
                            "description=" + group.Description,
                            "kind=" + condition.Kind,
                            "scope=" + condition.Scope,
                            "metric=" + condition.Metric,
                            "comparison=" + condition.Comparison,
                            "value=" + Float(condition.Value),
                            "upper=" + Float(condition.UpperValue),
                            "targets=" +
                            string.Join(",", condition.TargetIds),
                            "baseline=" + condition.BaselineId,
                            "candidate=" + condition.CandidateId)
                    });
                }
            }
        }

        private static void AddCheckpointEntries(
            ICollection<BalanceLedgerEntry> result,
            BalanceSimulationSuite suite)
        {
            if (suite.EntryState.Kind != BalanceEntryStateKind.Checkpoint ||
                string.IsNullOrWhiteSpace(
                    suite.EntryState.CheckpointPayload))
                return;

            CheckpointLedgerDocument checkpoint =
                JsonUtility.FromJson<CheckpointLedgerDocument>(
                    suite.EntryState.CheckpointPayload);
            if (checkpoint == null) return;
            int cardCount = checkpoint.cards?.Count ?? 0;
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "entry_checkpoint_build",
                contentId = checkpoint.currentFloorContentId,
                count = cardCount,
                health = checkpoint.currentHealth,
                experience = checkpoint.currentExperience,
                notes = Join(
                    "maxHealth=" + checkpoint.maxHealth,
                    "level=" + checkpoint.currentLevel,
                    "baseManaBonus=" + checkpoint.baseManaBonus,
                    "runLuckBonus=" + checkpoint.runLuckBonus,
                    "runGreedBonus=" + checkpoint.runGreedBonus,
                    "runJadeEarned=" + checkpoint.runJadeEarned,
                    "snapshot=" + checkpoint.currentFloor?.snapshotId)
            });
            if (checkpoint.cards == null) return;
            for (int i = 0; i < checkpoint.cards.Count; i++)
            {
                CheckpointCardDocument card = checkpoint.cards[i];
                if (card == null) continue;
                result.Add(new BalanceLedgerEntry
                {
                    itemKind = "entry_checkpoint_card",
                    contentId = card.contentId,
                    ordinal = i,
                    count = 1,
                    notes = Join(
                        "runtimeInstanceId=" + card.runtimeInstanceId,
                        "sealCount=" + (card.seals?.Count ?? 0))
                });
            }
        }

        private static void AddFloorEntries(
            ICollection<BalanceLedgerEntry> result,
            BalanceSimulationSuite suite,
            LevelRunProfile floor)
        {
            LevelExperienceRules xp = floor.levelExperienceRules ??
                new LevelExperienceRules();
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "floor",
                contentId = floor.contentId,
                count = floor.floorIndex,
                notes = Join(
                    "dungeon=" + floor.dungeonContentId,
                    "nextFloor=" + floor.nextFloorContentId,
                    "prerequisite=" + floor.prerequisiteLevelContentId,
                    "defaultSnapshot=" + floor.defaultSnapshotId,
                    "rewardRule=" + GlobalLevelUpRewardRuleProfileCatalog.ContentId)
            });
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "experience_rules",
                contentId = floor.contentId,
                count = xp.baseRequirement,
                notes = Join(
                    "curveExponent=" + Float(xp.curveExponent),
                    "continuationMode=" + xp.continuationMode,
                    "continuationIncrement=" +
                        xp.continuationIncrement,
                    "explicit=" + string.Join(
                        ",",
                    xp.explicitRequirements ??
                        new List<int>()))
            });

            if (suite.EntryState.Kind != BalanceEntryStateKind.FreshRun)
                return;

            LevelSnapshot snapshot = floor.FindSnapshot(suite.SnapshotId);
            if (snapshot == null) return;
            result.Add(new BalanceLedgerEntry
            {
                itemKind = "entry_snapshot",
                contentId = snapshot.snapshotId,
                count = snapshot.nodeStates?.Count ?? 0,
                notes = "fresh-run authoritative node-state source"
            });
            if (snapshot.nodeStates == null) return;
            for (int i = 0; i < snapshot.nodeStates.Count; i++)
            {
                LevelNodeState state = snapshot.nodeStates[i];
                if (state == null) continue;
                result.Add(new BalanceLedgerEntry
                {
                    nodeContentId = state.nodeContentId,
                    itemKind = "entry_snapshot_node",
                    contentId = state.nodeContentId,
                    ordinal = i,
                    notes = "progress=" + state.progress
                });
            }
        }

        private static void AddNodeEntry(
            ICollection<BalanceLedgerEntry> result,
            LevelNode node,
            int ordinal)
        {
            result.Add(new BalanceLedgerEntry
            {
                nodeContentId = node.contentId,
                scenarioId = node.scenarioId,
                itemKind = "floor_node",
                contentId = node.contentId,
                ordinal = ordinal,
                notes = Join(
                    "type=" + node.nodeType,
                    "prerequisites=" + string.Join(
                        ",",
                        node.prerequisiteNodeIds ??
                        new List<string>()))
            });
            if (node.completionEffects != null)
            {
                for (int i = 0; i < node.completionEffects.Count; i++)
                {
                    LevelInteractionEffect effect = node.completionEffects[i];
                    if (effect == null) continue;
                    result.Add(new BalanceLedgerEntry
                    {
                        nodeContentId = node.contentId,
                        itemKind = "interaction_effect",
                        contentId = effect.effectId,
                        ordinal = i,
                        count = effect.amount,
                        notes = Join(
                            "kind=" + effect.kind,
                            "amountMax=" + effect.amountMax,
                            "target=" + effect.targetContentId,
                            "sealPool=" + effect.sealRewardPoolId,
                            "cashOutJade=" + effect.cashOutJade,
                            "rarity=" + effect.rarity)
                    });
                }
            }
            if (node.victoryRewards != null)
            {
                for (int i = 0; i < node.victoryRewards.Count; i++)
                {
                    LevelRewardGrant reward = node.victoryRewards[i];
                    if (reward == null) continue;
                    result.Add(new BalanceLedgerEntry
                    {
                        nodeContentId = node.contentId,
                        itemKind = "victory_reward",
                        contentId = reward.contentId,
                        ordinal = i,
                        notes = Join(
                            "kind=" + reward.kind,
                            "target=" + reward.targetContentId,
                            "presentation=" + reward.presentationId)
                    });
                }
            }
        }

        private static void AddCommonEncounterSource(
            ICollection<BalanceLedgerEntry> result, LevelNode node)
        {
            LevelCommonEncounterBinding binding = node.commonEncounter;
            CommonEncounterSourceContract source = new CommonEncounterSourceResolver(ContentCatalog.Current)
                .Resolve(binding.datasetContentId);
            if (!source.Source.HasDifficulty(binding.difficultyContentId))
                throw new InvalidOperationException("Unknown common encounter difficulty: " + binding.difficultyContentId);
            result.Add(new BalanceLedgerEntry
            {
                nodeContentId = node.contentId,
                scenarioId = node.scenarioId,
                itemKind = "common_encounter_source",
                contentId = source.ContentId,
                // No Run is generated by content inspection. Count is not roster
                // capacity or a zero-enemy result; actual members are not sampled.
                notes = Join("roster=not-frozen; candidate-contract-only",
                    "generationFingerprint=" + source.GenerationFingerprint,
                    "runtimeContentFingerprint=" + source.RuntimeContentFingerprint,
                    "difficulty=" + binding.difficultyContentId,
                    "hasTileDifficulty=" + binding.hasTileDifficulty,
                    "tileDifficultyBits=" + BitConverter.ToInt32(BitConverter.GetBytes(binding.tileDifficulty), 0)
                        .ToString(CultureInfo.InvariantCulture))
            });
        }

        private static void AddScenarioEntries(
            ICollection<BalanceLedgerEntry> result,
            LevelRunProfile floor,
            LevelNode node,
            BattleScenarioProfile scenario,
            EncounterScenarioSpec spec,
            ISet<string> crawlerContentIds)
        {
            AddCrawlerId(crawlerContentIds, scenario.crawlerContentId);
            result.Add(new BalanceLedgerEntry
            {
                nodeContentId = node.contentId,
                scenarioId = scenario.scenarioId,
                itemKind = node.HasCommonEncounter ? "scenario_source_inspection" : "scenario",
                contentId = scenario.scenarioId,
                count = spec.Enemies.Count,
                health = spec.Player.maxHealth,
                armor = spec.Player.armor,
                notes = Join(
                    "roster=" + (node.HasCommonEncounter ? "not-frozen; count-not-sampled" : "authored"),
                    "schema=" + scenario.schemaVersion,
                    "volume=" + scenario.volumeId,
                    "baseMana=" + spec.Player.faLi,
                    "handLimit=" + spec.Player.shouPai,
                    "turnDraw=" + spec.Player.chouPai,
                    "configuredMight=" + scenario.might,
                    "configuredLuck=" + scenario.luck,
                    "configuredNirvana=" + scenario.nirvana,
                    "entryXP=" + spec.EntryExperienceGrant,
                    "crawler=" + scenario.crawlerContentId,
                    "bossTimer=" + scenario.bossTimerEnabled + ":" +
                    scenario.bossTimerEyes + ":" + scenario.bossTimerDamage,
                    "healthOverride=" + scenario.enemyHealthOverride,
                    "xpOverride=" + scenario.enemyExperienceOverride,
                    "levelBoss=" + OptionalMember(
                        scenario,
                        "levelBossContentId") + ":" + OptionalMember(
                        scenario,
                        "levelBossRank"),
                    "waves=" + JoinInts(spec.EnemyWaveSizes),
                    "formationRows=" + JoinInts(spec.EnemyFormationRows),
                    "formationPattern=" + spec.EnemyFormationPattern,
                    "testOnly=" + scenario.testOnlyUnconfirmed,
                    "rewardUtility=" + scenario.rewardUtilityPolicy,
                    "rules=" + JsonUtility.ToJson(spec.Rules))
            });

            for (int i = 0; i < spec.Enemies.Count; i++)
            {
                EnemyStats enemy = spec.Enemies[i];
                result.Add(new BalanceLedgerEntry
                {
                    nodeContentId = node.contentId,
                    scenarioId = scenario.scenarioId,
                    itemKind = "enemy",
                    contentId = enemy.contentId,
                    ordinal = i,
                    count = 1,
                    health = enemy.baseHealth,
                    attack = enemy.maxHit,
                    experience = enemy.experienceReward,
                    armor = enemy.armor,
                    behavior = DescribeBehavior(enemy.behaviorDefinition),
                    notes = Join(
                        "maxArmor=" + enemy.maxArmor,
                        "isBoss=" + enemy.isBoss,
                        "bossRank=" + OptionalMember(enemy, "bossRank"),
                        "bodySize=" + enemy.bodySize,
                        "knockbackMultiplier=" +
                        Float(enemy.knockbackMultiplier),
                        "displace=" + enemy.canBeDisplaced,
                        "disarmMultiplier=" +
                        Float(enemy.disarmMultiplier),
                        "grid=" + enemy.gridWidth + "x" + enemy.gridDepth,
                        "wave=" + enemy.spawnWaveIndex,
                        "row=" + enemy.row,
                        "column=" + enemy.column)
                });
            }

            for (int i = 0; i < spec.Deck.Count; i++)
                AddCard(
                    result,
                    node,
                    scenario,
                    "starting_deck",
                    spec.Deck[i],
                    i,
                    crawlerContentIds);

            for (int i = 0; i < spec.OwnedCardPool.Count; i++)
                AddCard(
                    result,
                    node,
                    scenario,
                    "owned_card_pool",
                    spec.OwnedCardPool[i],
                    i,
                    crawlerContentIds);

            LevelUpRewardRuleResolution rewardRule =
                StageEncounterAssembler.ResolveRewardRule(
                    floor,
                    scenario,
                    spec.OwnedCardPool);
            result.Add(new BalanceLedgerEntry
            {
                nodeContentId = node.contentId,
                scenarioId = scenario.scenarioId,
                itemKind = "reward_rule",
                contentId = GlobalLevelUpRewardRuleProfileCatalog.ContentId,
                count = rewardRule.Cards.Count,
                notes = "directory=author-and-frozen-legacy; qualification=unfiltered; " +
                    "version=" + rewardRule.NaturalPool.LevelCostPolicy.Version +
                    "; " + JsonUtility.ToJson(rewardRule.Config)
            });
            for (int i = 0; i < rewardRule.Cards.Count; i++)
            {
                AddCard(
                    result,
                    node,
                    scenario,
                    rewardRule.Cards[i].ordinaryRewardEnabled
                        ? "reward_definition_directory" : "frozen_compatible_directory",
                    rewardRule.Cards[i],
                    i,
                    crawlerContentIds);
            }

            AddFixedOfferScheduleEntries(result, floor, node, scenario);

            AddConfiguredCardReferences(
                result,
                node,
                scenario,
                spec.OwnedCardPool,
                crawlerContentIds,
                "fixed_first_reward",
                scenario.fixedFirstLevelUpRewardCardContentIds);
            AddConfiguredCardReferences(
                result,
                node,
                scenario,
                spec.OwnedCardPool,
                crawlerContentIds,
                "forced_opening_hand",
                scenario.forcedOpeningHandContentIds);
        }

        private static void AddFixedOfferScheduleEntries(
            ICollection<BalanceLedgerEntry> result,
            LevelRunProfile floor,
            LevelNode node,
            BattleScenarioProfile scenario)
        {
            string id = floor.levelUpOfferScheduleProfileId;
            if (string.IsNullOrWhiteSpace(id)) return;
            LevelUpOfferScheduleProfile schedule = LevelUpOfferScheduleProfileCatalog.Get(id);
            if (schedule == null)
                throw new InvalidOperationException("Missing fixed level-up offer schedule: " + id);
            AddFixedOfferScheduleEntries(result, schedule, node.contentId, scenario.scenarioId);
        }

        internal static void AddFixedOfferScheduleEntries(
            ICollection<BalanceLedgerEntry> result,
            LevelUpOfferScheduleProfile schedule,
            string nodeContentId,
            string scenarioId)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            result.Add(new BalanceLedgerEntry
            {
                nodeContentId = nodeContentId,
                scenarioId = scenarioId,
                itemKind = "fixed_level_up_schedule",
                contentId = schedule.contentId,
                count = schedule.offers.Count,
                notes = JsonUtility.ToJson(schedule)
            });
            foreach (LevelUpOfferScheduleEntry offer in schedule.offers)
            {
                for (int i = 0; i < offer.candidates.Count; i++)
                {
                    LevelUpOfferScheduleCandidate candidate = offer.candidates[i];
                    string sealState = string.Empty;
                    if (candidate.kind == LevelUpOfferScheduleCandidateKind.Seal)
                    {
                        if (!LingyinCatalog.TryGet(candidate.contentId, out LingyinConfig seal) || seal == null)
                            throw new InvalidOperationException("Missing scheduled seal: " + candidate.contentId);
                        sealState = JsonUtility.ToJson(seal.ToCardSealSpec());
                    }
                    result.Add(new BalanceLedgerEntry
                    {
                        nodeContentId = nodeContentId,
                        scenarioId = scenarioId,
                        itemKind = "fixed_level_up_candidate",
                        contentId = candidate.contentId,
                        ordinal = i,
                        count = offer.reachedLevel,
                        notes = "offer=" + offer.offerContentId +
                            ";kind=" + candidate.kind +
                            ";allowCashOut=" + offer.allowCashOut +
                            ";seal=" + sealState
                    });
                }
            }
        }

        private static void AddConfiguredCardReferences(
            ICollection<BalanceLedgerEntry> result,
            LevelNode node,
            BattleScenarioProfile scenario,
            IReadOnlyList<CardConfig> pool,
            ISet<string> crawlerContentIds,
            string kind,
            IReadOnlyList<string> contentIds)
        {
            if (contentIds == null) return;
            var byId = new Dictionary<string, CardConfig>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pool.Count; i++)
            {
                CardConfig card = pool[i];
                if (card != null && !string.IsNullOrWhiteSpace(card.contentId))
                    byId[card.contentId] = card;
            }
            for (int i = 0; i < contentIds.Count; i++)
            {
                string contentId = contentIds[i];
                byId.TryGetValue(contentId, out CardConfig card);
                AddCard(
                    result,
                    node,
                    scenario,
                    kind,
                    card,
                    i,
                    crawlerContentIds,
                    contentId);
            }
        }

        private static void AddCard(
            ICollection<BalanceLedgerEntry> result,
            LevelNode node,
            BattleScenarioProfile scenario,
            string kind,
            CardConfig card,
            int ordinal,
            ISet<string> crawlerContentIds,
            string fallbackContentId = null)
        {
            CollectCrawlerIds(card?.effects, crawlerContentIds);
            result.Add(new BalanceLedgerEntry
            {
                nodeContentId = node.contentId,
                scenarioId = scenario.scenarioId,
                itemKind = kind,
                contentId = card?.contentId ?? fallbackContentId,
                ordinal = ordinal,
                count = 1,
                manaCost = card?.manaCost ?? 0,
                damage = card?.baseDamage ?? 0,
                armor = card?.armor ?? 0,
                healing = card?.heal ?? 0,
                manaGain = card?.manaGain ?? 0,
                notes = card == null ? "unresolved" : DescribeCard(card)
            });
        }

        private static void AddCrawlerEntries(
            ICollection<BalanceLedgerEntry> result,
            ISet<string> requestedContentIds)
        {
            var pending = new Queue<string>(requestedContentIds);
            var added = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                string contentId = pending.Dequeue();
                if (string.IsNullOrWhiteSpace(contentId) ||
                    !added.Add(contentId))
                    continue;

                CrawlerConfig crawler =
                    ContentRuntimeFactory.CreateCrawlerByContentId(contentId);
                if (crawler == null)
                {
                    result.Add(new BalanceLedgerEntry
                    {
                        itemKind = "crawler_config",
                        contentId = contentId,
                        notes = "unresolved"
                    });
                    continue;
                }

                try
                {
                    var effects = new List<string>();
                    if (crawler.triggerEffects != null)
                    {
                        for (int i = 0;
                             i < crawler.triggerEffects.Count;
                             i++)
                        {
                            CardEffectSpec effect = crawler.triggerEffects[i];
                            effects.Add(effect == null
                                ? "null"
                                : JsonUtility.ToJson(effect));
                        }
                        var nested = new HashSet<string>(StringComparer.Ordinal);
                        CollectCrawlerIds(crawler.triggerEffects, nested);
                        foreach (string nestedId in nested)
                        {
                            if (!added.Contains(nestedId))
                                pending.Enqueue(nestedId);
                        }
                    }
                    result.Add(new BalanceLedgerEntry
                    {
                        itemKind = "crawler_config",
                        contentId = contentId,
                        count = crawler.duration,
                        manaCost = crawler.cardMana,
                        notes = Join(
                            "volume=" + crawler.volumeId,
                            "triggerColor=" + crawler.triggerColor,
                            "enterEffect=" + crawler.enterEffect,
                            "triggerEffect=" + crawler.triggerEffect,
                            "triggerEffects=" + string.Join(",", effects))
                    });
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(crawler);
                }
            }
        }

        private static void CollectCrawlerIds(
            IReadOnlyList<CardEffectSpec> effects,
            ISet<string> contentIds)
        {
            if (effects == null || contentIds == null) return;
            for (int i = 0; i < effects.Count; i++)
                AddCrawlerId(contentIds, effects[i]?.crawlerContentId);
        }

        private static void AddCrawlerId(
            ISet<string> contentIds,
            string contentId)
        {
            if (!string.IsNullOrWhiteSpace(contentId))
                contentIds.Add(contentId);
        }

        private static string DescribeCard(CardConfig card)
        {
            var effects = new List<string>();
            if (card.effects != null)
            {
                for (int i = 0; i < card.effects.Count; i++)
                {
                    CardEffectSpec effect = card.effects[i];
                    effects.Add(effect == null
                        ? "null"
                        : JsonUtility.ToJson(effect));
                }
            }
            var seals = new List<string>();
            if (card.seals != null)
            {
                for (int i = 0; i < card.seals.Count; i++)
                {
                    CardSealSpec seal = card.seals[i];
                    seals.Add(seal == null ? "null" : JsonUtility.ToJson(seal));
                }
            }
            return Join(
                "volume=" + card.volumeId,
                "color=" + card.color,
                "costType=" + card.costType,
                "free=" + card.isFreeToPlay,
                "reverse=" + card.hasReverse,
                "attackPattern=" + card.attackPattern,
                "projectiles=" + card.projectileCount,
                "targetPolicy=" + card.targetPolicy,
                "directTargets=" + card.directTargetsPerHit,
                "crit=" + card.canCrit + ":" + Float(card.critChance) +
                ":" + Float(card.critMultiplier),
                "area=" + card.areaTriggerMode + ":" +
                Float(card.areaCoefficient),
                "status=" + card.statusEffect + ":" +
                Float(card.statusChance),
                "destroy=" + card.destroyOnUse,
                "gemSlots=" + card.gemSlots,
                "evolution=" + card.evolutionTarget?.contentId,
                "effects=" + string.Join(",", effects),
                "seals=" + string.Join(",", seals));
        }

        private static string OptionalMember(object source, string name)
        {
            if (source == null || string.IsNullOrWhiteSpace(name))
                return string.Empty;
            var field = source.GetType().GetField(name);
            object value = field?.GetValue(source);
            return value?.ToString() ?? string.Empty;
        }

        private static string DescribeBehavior(
            EnemyBehaviorDefinition behavior)
        {
            if (behavior == null) return "legacy-random";
            if (behavior.HealthGroups != null)
            {
                var groups = new List<string>();
                var identities = new Dictionary<EnemyHealthBehaviorChoice, int>();
                foreach (EnemyHealthBehaviorGroup group in behavior.HealthGroups.Groups)
                {
                    var choices = new List<string>();
                    foreach (EnemyHealthBehaviorChoice choice in group.Choices)
                    {
                        if (!identities.TryGetValue(choice, out int identity))
                        {
                            identity = identities.Count;
                            identities.Add(choice, identity);
                        }
                        choices.Add(identity + "@" + Float(choice.Weight) + ":" +
                            choice.CanBeConsecutive + ":" + choice.PrioritiseForStartPhase + ":" +
                            DescribeHealthAction(choice.Action));
                    }
                    groups.Add(Float(group.RequiredHealthPercent) + "@" + Number(group.TurnCountdown) +
                        "[" + string.Join(",", choices) + "]");
                }
                return behavior.ContentId + ":health-groups[" + string.Join(";", groups) + "]";
            }
            var actions = new List<string>(behavior.ActionCount);
            for (int i = 0; i < behavior.ActionCount; i++)
            {
                EnemyBehaviorAction action = behavior.GetAction(i);
                actions.Add(
                    action.IntentType + ":" + action.Amount + "@" +
                    behavior.GetWeight(i));
            }
            return behavior.ContentId + ":" + behavior.SelectionMode +
                "[" + string.Join(",", actions) + "]";
        }

        private static string DescribeHealthAction(EnemyBehaviorAction action)
            => string.Join(":", action.IntentType, action.Amount, action.ActionId,
                DescribeTemporaryPayload(action.TemporaryCard),
                action.Compound.PrimaryIntentType, action.Compound.SecondaryIntentType,
                action.Compound.SecondaryAmount, action.Compound.SecondaryActionId,
                DescribeTemporaryPayload(action.Compound.SecondaryTemporaryCard));

        private static string DescribeTemporaryPayload(EnemyTemporaryCardRequest request)
            => string.Join(":", request.ContentId, request.Count, request.Placement,
                request.MaxRemaining, request.SourceBehaviorContentId);

        private static string JoinInts(IReadOnlyList<int> values)
        {
            if (values == null || values.Count == 0) return string.Empty;
            var items = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
                items[i] = Number(values[i]);
            return string.Join(",", items);
        }

        private static string Join(params string[] values)
        {
            return string.Join(";", values);
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Float(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Sha256(string value)
        {
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(
                Encoding.UTF8.GetBytes(value ?? string.Empty));
            var text = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
                text.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }


    }
}
