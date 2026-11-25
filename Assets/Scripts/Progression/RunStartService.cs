using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using Object = UnityEngine.Object;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WanjieScroll.Tests.EditMode")]

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Owns formal Run admission/loadout and defeat-restart policy. Preparation
    /// produces a detached Run and never mutates the active one; formal restart
    /// adopts that complete result through the existing session host. The Store
    /// remains the sole owner of the active-session pointer.
    /// A declared source reference supplies the fixed leader/deck/baseline; it
    /// never receives a second leader projection or an implicit common prefix.
    /// </summary>
    public sealed class RunStartService
    {
        private readonly RunEntryLoadoutProfile _entry;
        private readonly LevelRunProfile _firstFloor;
        private readonly IReadOnlyList<CardConfig> _cardPool;
        private readonly Action<MetaProgression, PlayerStats> _applyAccountStats;
        private readonly Func<string, bool> _isCardEligible;
        private readonly Func<string, BattleScenarioProfile> _resolveBattle;
        private readonly Func<string, RunReferenceLoadoutProfile> _resolveReference;
        private readonly Func<string, CommonEncounterSourceContract> _commonEncounterSources;
        private readonly Func<RunEntryLoadoutProfile, MetaProgression, DungeonRunSideEventSetup> _sideEvents;

        /// <summary>
        /// Loaded-content form for rule hosts/tests. Inputs remain caller-owned.
        /// applyAccountStats must project the existing account contract onto the
        /// fresh player only; it must not credit currency or mutate an account.
        /// Passing null is an explicit configuration error, not "ignore meta".
        /// </summary>
        public RunStartService(RunEntryLoadoutProfile entry, LevelRunProfile firstFloor,
            IReadOnlyList<CardConfig> cardPool, Action<MetaProgression, PlayerStats> applyAccountStats,
            Func<string, bool> isCardEligible = null,
            Func<string, BattleScenarioProfile> resolveBattle = null,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            Func<RunEntryLoadoutProfile, MetaProgression, DungeonRunSideEventSetup> sideEvents = null,
            Func<string, RunReferenceLoadoutProfile> resolveReference = null)
        {
            _entry = entry;
            _firstFloor = firstFloor;
            _cardPool = cardPool;
            _applyAccountStats = applyAccountStats;
            _isCardEligible = isCardEligible ?? CardTestStatus.CanUseInFormalPlay;
            _resolveBattle = resolveBattle ?? BattleScenarioCatalog.Get;
            _resolveReference = resolveReference ?? RunReferenceLoadoutProfileCatalog.Get;
            _sideEvents = sideEvents ?? DungeonSideEventProductionResolver.ResolveEntry;
            _commonEncounterSources = commonEncounterSources ??
                (id => new CommonEncounterSourceResolver(ContentCatalog.Current).Resolve(id));
        }

        /// <summary>
        /// Production entry used by Hub: loads entry and first-floor dependencies.
        /// An explicitly bound source timeline additionally preflights every future
        /// phase before admission. Only the entry's declared source reference is
        /// accepted; scene/debug overrides cannot change it. The adapter
        /// supplies the existing account projection without a reverse UI dependency.
        /// A rule host may inject its own eligibility policy; production callers
        /// omit it and retain the formal manual-acceptance gate.
        /// </summary>
        public static bool TryPrepare(string entryContentId, MetaProgression account,
            string selectedCrawlerContentId, Action<MetaProgression, PlayerStats> applyAccountStats,
            out DungeonRunSession preparedRun, out string error,
            Func<string, bool> isCardEligible = null)
            => TryPrepare(entryContentId, account, selectedCrawlerContentId, applyAccountStats,
                DemoReleasePolicy.IsDemoBuild, out preparedRun, out error, isCardEligible);

        internal static bool TryPrepare(string entryContentId, MetaProgression account,
            string selectedCrawlerContentId, Action<MetaProgression, PlayerStats> applyAccountStats, bool demo,
            out DungeonRunSession preparedRun, out string error, Func<string, bool> isCardEligible = null)
        {
            preparedRun = null;
            error = null;
            List<CardConfig> pool = null;
            try
            {
                RunEntryLoadoutProfile entry = RunEntryLoadoutProfileCatalog.Get(entryContentId);
                LevelRunProfile floor = LevelRunProfileCatalog.Get(entry.firstFloorContentId);
                pool = ContentRuntimeFactory.CreateCardPool(entry.volumeId);
                return new RunStartService(entry, floor, pool, applyAccountStats, isCardEligible)
                    .TryPrepare(account, selectedCrawlerContentId, demo, out preparedRun, out error);
            }
            catch (Exception exception)
            {
                error = "正式 Run 预检失败：" + exception.Message;
                return false;
            }
            finally { ReleaseCards(pool); }
        }

        /// <summary>
        /// Prepares the next dungeon only after the previous victory is settled.
        /// This is a new formal Run, never a cross-dungeon floor advance or a
        /// diagnostic checkpoint. The caller persists before adopting it.
        /// </summary>
        public static bool TryPrepareVictoryContinuation(DungeonRunSession previous,
            MetaProgression account, string selectedCrawlerContentId,
            Action<MetaProgression, PlayerStats> applyAccountStats,
            out DungeonRunSession prepared, out string error,
            Func<string, bool> isCardEligible = null)
            => TryPrepareVictoryContinuation(previous, account, selectedCrawlerContentId, applyAccountStats,
                DemoReleasePolicy.IsDemoBuild, out prepared, out error, isCardEligible);

        internal static bool TryPrepareVictoryContinuation(DungeonRunSession previous,
            MetaProgression account, string selectedCrawlerContentId,
            Action<MetaProgression, PlayerStats> applyAccountStats, bool demo,
            out DungeonRunSession prepared, out string error, Func<string, bool> isCardEligible = null)
        {
            prepared = null;
            error = null;
            if (previous == null || !previous.TryGetConclusion(out var result) ||
                result.Status != DungeonRunResultStatus.Victory ||
                account == null || !account.IsLevelCleared(previous.FirstFloorContentId))
            {
                error = "必须先完成并保存上一关的通关结算。";
                return false;
            }
            if (DemoReleasePolicy.IsDemoFinale(previous.DungeonContentId, demo))
            {
                error = "前三关试玩已通关，请返回仙域重新挑战。";
                return false;
            }
            string entryId = previous.CurrentFloor?.Profile?.victoryNextRunEntryContentId;
            if (string.IsNullOrWhiteSpace(entryId))
            {
                error = "当前终局未配置下一关正式入口。";
                return false;
            }
            if (!TryPrepare(entryId, account, selectedCrawlerContentId, applyAccountStats, demo,
                    out DungeonRunSession candidate, out error, isCardEligible)) return false;
            if (string.Equals(candidate.DungeonContentId, previous.DungeonContentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "下一关入口不能返回当前关。";
                return false;
            }
            prepared = candidate;
            return true;
        }

        /// <summary>
        /// Explicit defeat-result command. Formal entries repeat the same Hub
        /// preflight and only then replace the hosted Run. Unmigrated entries use
        /// their existing restart contract; broken formal entries never fall back.
        /// </summary>
        public static bool TryRestartActive(MetaProgression account, string selectedCrawlerContentId,
            Action<MetaProgression, PlayerStats> applyAccountStats,
            out DungeonRunSession restarted, out string error,
            Func<string, bool> isCardEligible = null)
        {
            restarted = null;
            error = null;
            DungeonRunSession previous = DungeonRunSessionStore.Current;
            if (previous == null || !previous.TryGetConclusion(out DungeonRunResultSnapshot conclusion) ||
                conclusion.Status != DungeonRunResultStatus.Defeat)
            {
                error = "只有已经冻结的战败 Run 才能重新挑战。";
                return false;
            }
            try
            {
                if (!RunEntryLoadoutProfileCatalog.TryFindForFirstFloor(previous.FirstFloorContentId,
                        out RunEntryLoadoutProfile entry, out error))
                {
                    if (!string.IsNullOrWhiteSpace(error)) return false;
                    return DungeonRunSessionStore.TryRestartAfterDefeat(out restarted, out error);
                }
                if (!TryPrepare(entry.contentId, account, selectedCrawlerContentId, applyAccountStats,
                        out DungeonRunSession prepared, out error, isCardEligible)) return false;
                if (!ReferenceEquals(DungeonRunSessionStore.Current, previous))
                {
                    error = "预检期间活动 Run 已改变，不能覆盖另一个 Run。";
                    return false;
                }
                if (!DungeonRunSessionStore.TryAdoptPrepared(prepared, out error)) return false;
                restarted = prepared;
                return true;
            }
            catch (Exception exception)
            {
                error = "重开 Run 预检失败：" + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// A successful result already owns a fresh deck, full entry health,
        /// captured account-adjusted baseline and new progression state. No
        /// caller should call StartNewRun before this returns successfully.
        /// Explicit diagnostic identities keep a seeded isolated Run reproducible;
        /// ordinary production callers retain the default identity/seed policy.
        /// </summary>
        public bool TryPrepare(MetaProgression account, string selectedCrawlerContentId,
            out DungeonRunSession preparedRun, out string error,
            string runId = null, IInstanceIdGenerator instanceIds = null, int? runSeed = null)
            => TryPrepare(account, selectedCrawlerContentId, DemoReleasePolicy.IsDemoBuild,
                out preparedRun, out error, runId, instanceIds, runSeed);

        internal bool TryPrepare(MetaProgression account, string selectedCrawlerContentId, bool demo,
            out DungeonRunSession preparedRun, out string error,
            string runId = null, IInstanceIdGenerator instanceIds = null, int? runSeed = null)
        {
            preparedRun = null;
            error = null;
            List<CardConfig> preparedCards = null;
            try
            {
                if (_firstFloor != null &&
                    !DemoReleasePolicy.AllowsLevel(_firstFloor.contentId, _firstFloor.dungeonContentId, demo))
                    throw new InvalidOperationException("当前试玩版本不包含此正式入口。");
                ValidateEntry(account);
                RunReferenceLoadoutProfile reference = string.IsNullOrEmpty(_entry.referenceLoadoutContentId)
                    ? null : _resolveReference(_entry.referenceLoadoutContentId);
                if (!string.IsNullOrEmpty(_entry.referenceLoadoutContentId) &&
                    (reference == null || !string.Equals(reference.contentId,
                        _entry.referenceLoadoutContentId, StringComparison.Ordinal)))
                    throw new InvalidOperationException("固定来源装载缺失或稳定 ID 不匹配。");
                if (reference != null)
                {
                    List<string> referenceErrors = reference.ValidateProfile();
                    if (referenceErrors.Count > 0)
                        throw new InvalidOperationException(string.Join(" | ", referenceErrors));
                }
                string leaderId = reference == null ? selectedCrawlerContentId : reference.crawlerContentId;
                bool cardPlayGrowthEnabled = CaptureCardPlayGrowthAdmission(account);
                if (string.IsNullOrWhiteSpace(leaderId) ||
                    !ContentCatalog.Current.TryGetCrawler(leaderId, out CrawlerData crawler))
                    throw new InvalidOperationException("所选英灵不存在：" + leaderId);
                if (!DemoReleasePolicy.AllowsCrawler(crawler.contentId, demo))
                    throw new InvalidOperationException("当前试玩版本不包含所选英灵。");
                if (account.unlockedCrawlers == null || !account.IsCrawlerUnlocked(crawler.contentId))
                    throw new InvalidOperationException("所选英灵尚未解锁：" + crawler.contentId);

                var ids = new List<string>(reference == null
                    ? _entry.commonCardContentIds : reference.deckContentIds);
                if (reference == null) ids.AddRange(CrawlerStartingDeckResolver.BuildPackageContentIds(crawler));
                List<CardConfig> seed = StableCardPoolResolver.ResolveRequired(
                    ids, _cardPool, id => _isCardEligible(id) && DemoReleasePolicy.AllowsCard(id, demo),
                    "正式 Run 起始牌组");
                if (reference == null) ValidateSeed(seed, crawler.contentId);
                var player = new PlayerStats
                {
                    maxHealth = _entry.maxHealth,
                    currentHealth = _entry.maxHealth,
                    faLi = _entry.baseMana,
                    shouPai = _entry.handSize,
                    chouPai = _entry.handSize
                };
                reference?.ApplyTo(player);
                int initialRewardRerolls = 0;
                if (reference?.suppressMetaStatProgression != true)
                {
                    _applyAccountStats(account, player);
                    // Daozang owns the purchased definition; the new Run owns
                    // the consumable balance. Restore and floor changes never
                    // enter this admission path or replenish a spent allowance.
                    DaozangProgressionRules.Definition reroll = DaozangProgressionRules.Get("reroll");
                    int level = DaozangProgressionRules.GetLevel(account, reroll.Id);
                    if (level < 0 || level > reroll.MaxLevel)
                        throw new InvalidOperationException("账号易卷等级超出正式经诀范围。");
                    initialRewardRerolls = checked(level * reroll.AmountPerLevel);
                }
                int initialDurationReserve = reference == null
                    ? crawler.leaderStats?.ProjectNewRunBaseline(player) ?? 0 : 0;
                if (player.maxHealth < 1 || player.faLi < 1 || player.shouPai < 1)
                    throw new InvalidOperationException("账号强化投影产生了无效的入口生命、灵力或手牌数。");
                // Full health is a new-Run rule, not a carry-over from a prior battle.
                player.currentHealth = player.maxHealth;
                DungeonRunSideEventSetup sourceSetup = null;
                if (!string.IsNullOrEmpty(_entry.sideEventTimelineContentId))
                {
                    sourceSetup = _sideEvents(_entry, account);
                    if (sourceSetup == null || sourceSetup.Plan.Phases[0].FloorContentId != _firstFloor.contentId)
                        throw new InvalidOperationException("Configured source admission must return the complete matching setup.");
                }
                DungeonRunSession candidate = DungeonRunSession.Begin(_firstFloor, _entry.entrySnapshotId,
                    runId, new RunProgressionState(cardPlayGrowthEnabled), instanceIds, runSeed: runSeed,
                    leaderCrawlerContentId: crawler.contentId, commonEncounterSources: _commonEncounterSources,
                    sideEventPlan: sourceSetup?.Plan, sideEventContext: sourceSetup?.Context, demo: demo,
                    initialRewardRerolls: initialRewardRerolls);
                candidate.RunProgression.CrawlerDurationReserve.Bank(initialDurationReserve);
                if (!candidate.TryPrepareCombat(player, seed, _cardPool,
                        out preparedCards, out string prepareError))
                    throw new InvalidOperationException(prepareError);
                if (candidate.RunCardCount != ids.Count)
                    throw new InvalidOperationException("正式起始牌组未完整写入 Run。");
                candidate.RegisterPreparedCrawlers(new[] { crawler.contentId });
                preparedRun = candidate;
                return true;
            }
            catch (Exception exception)
            {
                error = "正式 Run 预检失败：" + exception.Message;
                return false;
            }
            finally { ReleaseCards(preparedCards); }
        }

        /// <summary>
        /// Capture once, before constructing the Run. Either stable identity or
        /// the typed effect claiming Baishu requires the entire legal selection;
        /// a forged/mismatched claim fails admission instead of silently gaining
        /// the rule. Other equipped Arcanas retain their existing contracts.
        /// A null account is an explicit account-free diagnostic entry, disabled
        /// by default. Restore/checkpoint callers must use their saved frozen
        /// state, never call this method to re-evaluate an existing Run.
        /// </summary>
        public static bool CaptureCardPlayGrowthAdmission(MetaProgression account)
        {
            if (account == null) return false;
            bool selectedId = string.Equals(account.equippedArcana,
                ArcanaContentIds.BaishuZhoutian, StringComparison.Ordinal);
            bool selectedEffect = account.hasEquippedArcanaEffect &&
                                  account.equippedArcanaEffect == ArcanaEffect.BaishuZhoutian;
            if (!selectedId && !selectedEffect) return false;
            if (!selectedId || !selectedEffect || !account.IsArcanaFacilityUnlocked ||
                !account.IsArcanaUnlocked(ArcanaContentIds.BaishuZhoutian))
                throw new InvalidOperationException("百术周天选择无效：需要已开放天机设施、已解锁稳定ID和一致的类型化效果。");

            ArcanaConfig selected = null;
            foreach (ArcanaConfig config in Resources.LoadAll<ArcanaConfig>("Content/Arcanas"))
            {
                if (config == null || !string.Equals(config.contentId,
                        ArcanaContentIds.BaishuZhoutian, StringComparison.Ordinal)) continue;
                if (selected != null) throw new InvalidOperationException("百术周天天机存在重复稳定ID。");
                selected = config;
            }
            if (selected == null || selected.effect != ArcanaEffect.BaishuZhoutian ||
                selected.effectValue != RunCardPlayGrowth.RealPlaysPerCycle)
                throw new InvalidOperationException("百术周天正式配置缺失或效果/阈值不一致。");
            return true;
        }

        private void ValidateEntry(MetaProgression account)
        {
            if (_entry == null) throw new InvalidOperationException("缺少正式 Run 入口配置。");
            List<string> errors = _entry.ValidateProfile();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
            if (_firstFloor == null || !string.Equals(_entry.firstFloorContentId,
                    _firstFloor.contentId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("正式入口与首层配置不匹配：" + _entry.firstFloorContentId);
            if (_firstFloor.floorIndex > 1)
                throw new InvalidOperationException("正式新 Run 必须从首层开始，不能使用中途检查点。");
            errors = _firstFloor.Validate();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" | ", errors));
            LevelSnapshot snapshot = _firstFloor.FindSnapshot(_entry.entrySnapshotId);
            if (snapshot == null) throw new InvalidOperationException("找不到正式入口快照：" + _entry.entrySnapshotId);
            // This generic rule separates a new Run from a debug checkpoint.
            // Which initial nodes are available/locked remains entirely asset-owned.
            foreach (LevelNodeState state in snapshot.nodeStates)
                if (state.progress == LevelNodeProgress.Cleared &&
                    _firstFloor.FindNode(state.nodeContentId).nodeType != LevelNodeType.Start)
                    throw new InvalidOperationException("正式入口快照不能含已完成的战斗或奖励节点。");
            if (account == null) throw new InvalidOperationException("正式新 Run 缺少账号状态。");
            string prerequisite = _firstFloor.prerequisiteLevelContentId;
            if (!string.IsNullOrWhiteSpace(prerequisite) && !account.IsLevelCleared(prerequisite))
                throw new InvalidOperationException("尚未完成前置整关：" + prerequisite);
            if (_applyAccountStats == null)
                throw new InvalidOperationException("正式入口缺少局外强化投影，不能静默忽略账号属性。");
            if (_cardPool == null) throw new InvalidOperationException("缺少正式入口卡牌定义池。");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardConfig card in _cardPool)
                if (card == null || string.IsNullOrWhiteSpace(card.contentId) || !ids.Add(card.contentId))
                    throw new InvalidOperationException("正式入口卡池存在空定义、缺失或重复的 contentId。");
            ValidateConfiguredBattles();
        }

        private static void ValidateSeed(IReadOnlyList<CardConfig> seed, string crawlerContentId)
        {
            for (int i = 0; i < seed.Count; i++)
            {
                CardConfig card = seed[i];
                if (card.IsEncounterTemporary || card.color == CardColor.Yin)
                    throw new InvalidOperationException("临时或敌方杂牌不能进入正式起手包：" + card.contentId);
                bool isHeroPosition = i == CrawlerStartingDeckResolver.RequiredCommonCardCount;
                int summons = 0;
                foreach (CardEffectSpec effect in card.effects ?? new List<CardEffectSpec>())
                {
                    if (effect == null || effect.effectType != EffectType.SummonCrawler) continue;
                    summons++;
                    if (!isHeroPosition || !string.Equals(effect.crawlerContentId,
                            crawlerContentId, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("起手包包含非所选英灵的召唤卡：" + card.contentId);
                }
                if (isHeroPosition && (card.color != CardColor.Jin || summons != 1))
                    throw new InvalidOperationException("所选英灵金卡缺少唯一匹配的召唤效果：" + card.contentId);
                if (!isHeroPosition && card.color == CardColor.Jin)
                    throw new InvalidOperationException("通用牌或支援牌不能另放英灵金卡：" + card.contentId);
            }
        }

        private void ValidateConfiguredBattles()
        {
            foreach (LevelNode node in _firstFloor.nodes)
            {
                if (node.nodeType != LevelNodeType.Encounter && node.nodeType != LevelNodeType.Finale) continue;
                BattleScenarioProfile scenario = _resolveBattle(node.scenarioId);
                if (scenario == null) throw new InvalidOperationException("首层缺少战斗资源：" + node.scenarioId);
                if (!string.Equals(scenario.scenarioId, node.scenarioId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("首层战斗资源 ID 不匹配：" + node.scenarioId);
                if (scenario.testOnlyUnconfirmed)
                    throw new InvalidOperationException("正式入口不能使用调试战斗：" + node.scenarioId);
                // Combat still resolves its scene seed before restoring the Run.
                // A scene cannot introduce a reference into an ordinary entry,
                // or replace the fixed source explicitly declared by the entry.
                if (string.IsNullOrEmpty(_entry.referenceLoadoutContentId))
                {
                    if (scenario.HasReferenceLoadout)
                        throw new InvalidOperationException("正式入口不能使用实测参考装载：" + node.scenarioId);
                }
                else if (!string.Equals(scenario.referenceLoadoutContentId,
                    _entry.referenceLoadoutContentId, StringComparison.Ordinal))
                    throw new InvalidOperationException("正式入口与战斗的固定来源装载不一致：" + node.scenarioId);
                List<string> errors = scenario.ValidateProfile();
                if (errors.Count > 0)
                    throw new InvalidOperationException(node.scenarioId + "：" + string.Join(" | ", errors));
            }
        }

        private static void ReleaseCards(IReadOnlyList<CardConfig> cards)
        {
            if (cards == null) return;
            foreach (CardConfig card in cards)
            {
                if (card == null) continue;
                if (Application.isPlaying) Object.Destroy(card);
                else Object.DestroyImmediate(card);
            }
        }
    }
}
