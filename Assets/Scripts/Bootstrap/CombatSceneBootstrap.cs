using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.Profiling;
using WanjieScroll.Audio;
using WanjieScroll.Bootstrap;
using WanjieScroll.Content;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects;
using WanjieScroll.Effects.Attacks;
using WanjieScroll.Effects.Map;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.Progression;
using WanjieScroll.UI.Daozang;
using WanjieScroll.UI.Battle;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 战斗场景唯一装配入口。
    /// Combat 与 BattleLayoutTest 只提供不同的 BattleScenarioProfile，
    /// 世界层、卡牌、HUD、交互、音频和特效始终走同一条生产路径。
    /// 场景解析优先级：活动遭遇 scenario → Inspector 独立测试配置 → 原默认战斗。
    /// 2026-10-06规模审查：生成牌仅增加独立协调器的装配与解绑；待到达
    /// 实例、动画、显隐分别由协调器及HandView所属深模块拥有，不扩展本类状态机。
    /// 2026-10-13乱魂蛊接线只创建、注入、解绑纸纹协调器；回执由Core拥有，
    /// UI纸纹与驻场标记分别由各自展示owner管理，不在本Bootstrap保存动画状态。
        /// 2026-10-17入场优化只接线Dungeon的一次性遭遇物化与装配计时；
        /// 冻结源校验仍归Dungeon，计时为本次调用局部值，不扩展生命周期状态。
        /// 2026-10-17驻留试点审查：静态视图与复位归CombatPresentationShell，
        /// 楼层/加载/入场代次归FloorCombatSceneOwner；此类只组装和释放本场对象。
        /// 所有场景沿同一工厂，跨场不保留Core/Controller/特效实例；新增独立流程前再审查。
        /// 2026-10-18分帧审查：这里只拆分既有装配的执行预算，BuildNow与异步入口
        /// 消费同一序列；唯一BuildOperation持有构建租约，Run准备/登记/保存不跨帧。
        /// 不增加视觉状态或业务规则；若出现第二套构建顺序或跨场数据缓存须重新审查。
        /// 2026-11-01镜头接入复核：本文件仅装配独立camera owner及HUD投影依赖；
        /// 镜头几何、lease代次和恢复策略由BattleCameraViewOwner拥有，不增加第二套场景流程。
    /// </summary>
    public sealed class CombatSceneBootstrap : MonoBehaviour
    {
        private static readonly ProfilerMarker BuildNowMarker =
            new ProfilerMarker("CombatSceneBootstrap.BuildNow");

        [Header("战斗数据")]
        [Tooltip("为空时使用 GameState 当前卷的默认战斗；测试场景绑定固定验证数据。")]
        public BattleScenarioProfile scenario;
        [Tooltip("独立手工验收场景的地图表现依赖；活动Run始终使用其真实楼层地图。")]
        public MapPreviewProfile standaloneMapPresentation;

        [Header("共享表现组件")]
        public BattleHudPresentationProfile battleHudPresentation;
        public CardView cardPrefab;
        public CardPresentationProfile cardPresentation;
        public CrawlerResidentLayoutProfile crawlerResidentLayout;

        [Header("场景静态引用（Inspector 赋值，为空时 fallback 创建）")]
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private Canvas _canvas;
        [SerializeField] private EventSystem _eventSystem;
        [SerializeField] private AudioManager _audioManager;
        [SerializeField] private Transform _worldSpaceRoot;

        private CombatController _combat;
        private CombatSceneController _sceneController;
        private CombatEntryCoordinator _entryCoordinator;
        private CombatScenePreloader _preloadHost;
        private CombatScenePreloadPlan? _preloadPlan;
        private FloorCombatSceneOwner _residentHost;
        private CombatPresentationShell _presentationShell;
        private CombatEncounterResourceLease _encounterResources;
        private GameObject _encounterRoot, _encounterWorldRoot, _encounterCanvasRoot;
        private Transform EncounterServices => _encounterRoot.transform;
        private Transform EncounterWorld => _encounterWorldRoot.transform;
        private Transform EncounterCanvas => _encounterCanvasRoot.transform;
        private VisualEffectManager _vfxManager;
        private BossActionEffectPlayer _bossActionEffectPlayer;
        private BossCloseupImpactFeedback _bossCloseupImpactFeedback;
        private LevelBossEntranceCoordinator _levelBossEntranceCoordinator;
        private CombatVisualBridge _visualBridge;
        private PeriodicAttackPresenter _periodicAttackPresenter;
        private CombatExperiencePresentationCoordinator
            _experiencePresentationCoordinator;
        private EnemyTurnPresentationCoordinator
            _enemyTurnPresentationCoordinator;
        private ExperienceTransferEffect _experienceTransferEffect;
        private CombatReturnCoordinator _returnCoordinator;
        private bool _levelUpPresentationPaused;
        private BattlefieldView _battlefield;
        private BattleHudView _battleHud;
        private CrawlerResidentPresenter _crawlerResidentPresenter;
        private CrawlerResidentFeedbackCoordinator
            _crawlerResidentFeedbackCoordinator;
        private SupportArtifactFeedbackCoordinator _supportArtifactFeedback;
        private CreatedCardPresentationCoordinator _createdCardPresentation;
        private EnemyHandDisruptionFeedbackCoordinator _handDisruptionFeedback;
        private BossMotionAudioCoordinator _bossMotionAudio;
        private HandView _handView;
        private PlayPileView _playPileView;
        private BattleBoardHudLayout _boardHudLayout;
        private MetaProgression _activeMeta;
        private CombatPauseMenu _pauseMenu;
        private EncounterBuildOperation _buildOperation;

        public CombatController Combat => _combat;
        public CombatSceneController SceneController => _sceneController;
        public BattlefieldView Battlefield => _battlefield;
        public BattleHudView BattleHud => _battleHud;
        public HandView HandView => _handView;
        public PlayPileView PlayPileView => _playPileView;
        public VisualEffectManager VisualEffects => _vfxManager;
        public BossActionEffectPlayer BossActionEffects =>
            _bossActionEffectPlayer;
        public BossCloseupImpactFeedback CloseupImpactFeedback => _bossCloseupImpactFeedback;
        public BossEntranceEffect BossEntranceEffect =>
            _levelBossEntranceCoordinator?.EntranceEffect;
        public int PresentedBossEntranceCount =>
            _levelBossEntranceCoordinator?.PresentedCount ?? 0;
        public int CompletedBossEntranceCount =>
            _levelBossEntranceCoordinator?.CompletedCount ?? 0;
        public CombatExperiencePresentationCoordinator
            ExperiencePresentationCoordinator =>
                _experiencePresentationCoordinator;
        public EnemyTurnPresentationCoordinator
            EnemyTurnPresentationCoordinator =>
                _enemyTurnPresentationCoordinator;
        public bool IsBuilt { get; private set; }
        public bool IsPresentationVisible { get; private set; } = true;

        private void Awake()
        {
            if (gameObject.scene.name != SceneNames.Combat)
                return;

            FloorCombatSceneOwner resident = FloorCombatSceneOwner.Current;
            if (resident != null && resident.TryClaim(this))
            {
                _residentHost = resident;
                SetPresentationVisible(false);
                return;
            }

            CombatScenePreloader preloader =
                FindObjectOfType<CombatScenePreloader>();
            if (preloader == null ||
                !preloader.TryClaim(this, out CombatScenePreloadPlan preloadPlan))
            {
                return;
            }

            _preloadHost = preloader;
            _preloadPlan = preloadPlan;
            SetPresentationVisible(false);

            try
            {
                scenario = BattleScenarioCatalog.Get(preloadPlan.ScenarioId);
                if (scenario == null)
                {
                    throw new InvalidOperationException(
                        $"找不到后台战斗配置：{preloadPlan.ScenarioId}");
                }
            }
            catch (Exception exception)
            {
                _preloadHost.MarkFailed(this, exception.Message);
            }
        }

        private IEnumerator Start()
        {
            if (_residentHost != null)
            {
                if (!_residentHost.IsShellBuildRequested(this)) yield break;
                try
                {
                    PrepareResidentShell(_residentHost.Floor);
                    _residentHost.MarkShellReady(this);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    _residentHost.MarkFailed(this, exception.Message);
                }
                yield break;
            }
            if (_preloadHost != null &&
                _preloadHost.State != CombatScenePreloadState.Loading)
            {
                yield break;
            }

            string buildRequestId = SceneContext.Get<string>(
                SceneContextKeys.TransitionRequestId, null);
            LevelEncounterLaunch? buildLaunch =
                LevelRunSessionStore.Current?.ActiveEncounter;
            bool IsStartBuildCurrent() =>
                IsSceneBuildCurrent(buildRequestId, buildLaunch);
            IEnumerator build = null;
            Exception buildFailure = null;
            try { build = BuildStaged(IsStartBuildCurrent); }
            catch (Exception exception) { buildFailure = exception; }
            if (buildFailure != null)
            {
                if (_preloadHost == null) throw buildFailure;
                Debug.LogException(buildFailure);
                _preloadHost.MarkFailed(this, buildFailure.Message);
                yield break;
            }
            try
            {
                while (true)
                {
                    bool advanced = false;
                    try { advanced = build.MoveNext(); }
                    catch (Exception exception) { buildFailure = exception; }
                    if (buildFailure != null)
                    {
                        if (_preloadHost == null) throw buildFailure;
                        Debug.LogException(buildFailure);
                        _preloadHost.MarkFailed(this, buildFailure.Message);
                        yield break;
                    }
                    if (!advanced) break;
                    yield return build.Current;
                }
                if (IsBuilt && IsStartBuildCurrent())
                    _preloadHost?.MarkReady(this);
                else if (_preloadHost != null &&
                    _preloadHost.State == CombatScenePreloadState.Loading)
                    _preloadHost.MarkCancelled(this);
            }
            finally { (build as IDisposable)?.Dispose(); }
        }

        private bool IsSceneBuildCurrent(string requestId,
            LevelEncounterLaunch? expectedLaunch)
        {
            if (_preloadHost != null)
                return _preloadHost.State == CombatScenePreloadState.Loading &&
                    _preloadHost.PreparedBootstrap == this && _preloadPlan.HasValue &&
                    _preloadHost.IsPreparingFor(_preloadPlan.Value.NodeContentId,
                        _preloadPlan.Value.ScenarioId);
            // Independently opened component/test scenes have no transition.
            if (string.IsNullOrEmpty(requestId)) return true;
            LevelEncounterLaunch? currentLaunch =
                LevelRunSessionStore.Current?.ActiveEncounter;
            if (expectedLaunch.HasValue != currentLaunch.HasValue) return false;
            if (expectedLaunch.HasValue &&
                (!string.Equals(expectedLaunch.Value.RunId,
                    currentLaunch.Value.RunId, StringComparison.Ordinal) ||
                 !string.Equals(expectedLaunch.Value.NodeContentId,
                    currentLaunch.Value.NodeContentId, StringComparison.Ordinal) ||
                 !string.Equals(expectedLaunch.Value.ScenarioId,
                    currentLaunch.Value.ScenarioId, StringComparison.Ordinal))) return false;
            return SceneTransitionRuntime.IsDestinationBuildCurrent(
                requestId, SceneNames.Combat);
        }

        /// <summary>
        /// 同步兼容入口。与场景生命周期共用同一装配序列，不维护第二份构建顺序。
        /// </summary>
        public void BuildNow()
        {
            if (IsBuilt) return;
            IEnumerator build = BuildStaged();
            try { while (build.MoveNext()) { } }
            finally { (build as IDisposable)?.Dispose(); }
        }

        /// <summary>
        /// Reserves the sole encounter build immediately. Every yielded boundary
        /// leaves the scene unready; disposing or revoking the caller's scope
        /// releases the incomplete encounter instead of activating it later.
        /// </summary>
        public IEnumerator BuildStaged(Func<bool> isCurrent = null)
        {
            if (IsBuilt) return Array.Empty<object>().GetEnumerator();
            if (_buildOperation != null)
                throw new InvalidOperationException("An encounter build is already in progress.");
            _buildOperation = new EncounterBuildOperation(this, isCurrent);
            return _buildOperation;
        }

        private IEnumerator BuildStages()
        {
            string transitionRequestId = _buildOperation.TransitionRequestId;
            SetPresentationVisible(false);
            var buildClock = System.Diagnostics.Stopwatch.StartNew();
            var wallClock = System.Diagnostics.Stopwatch.StartNew();
            var timings = new List<string>();
            double lastStageMs = 0;
            void MarkStage(string name)
            {
                double now = buildClock.Elapsed.TotalMilliseconds;
                timings.Add(name + "=" + (now - lastStageMs).ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) + "ms");
                lastStageMs = now;
            }

            EnsureEventSystem(transform);
            Camera camera = ResolveCamera(transform);
            if (_encounterResources != null)
                throw new InvalidOperationException("The previous encounter has not been released.");
            _encounterResources = new CombatEncounterResourceLease();
            // Sprite definitions are read-only assets. Preserve the loader cache
            // across encounters; F7 retains its explicit refresh for art review.

            // 场景解析优先级：活动遭遇 → Inspector → 默认
            BattleScenarioProfile resolvedScenario = ResolveScenario();
            if (resolvedScenario != null && resolvedScenario != scenario)
                scenario = resolvedScenario;

            string volumeId = scenario != null
                ? scenario.volumeId
                : GameSession.Current.GetVolumeId();
            if (scenario != null)
            {
                List<string> errors = scenario.ValidateProfile();
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException(
                        "战斗场景数据不合法：" + Environment.NewLine +
                        string.Join(Environment.NewLine, errors));
                }
            }

            MarkStage("scenario");
            IReadOnlyList<CardConfig> cardPool =
                ContentRuntimeFactory.CreateCardPool(volumeId);
            _encounterResources.OwnCards(cardPool);
            Func<string, CardConfig> cardResolver = contentId =>
            {
                if (string.IsNullOrWhiteSpace(contentId))
                    return null;
                for (int i = 0; i < cardPool.Count; i++)
                {
                    CardConfig candidate = cardPool[i];
                    if (candidate != null && string.Equals(
                            candidate.contentId,
                            contentId,
                            System.StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
                return null;
            };
            DungeonRunSession activeDungeonRun =
                DungeonRunSessionStore.Current;
            LevelRunSession activeRun = LevelRunSessionStore.Current;
            List<CardConfig> deck = CreateDeckFromContent(
                cardPool,
                activeDungeonRun);
            _encounterResources.OwnCards(deck);
            LevelUpOfferScheduleProfile levelUpOfferSchedule = null;
            ILevelUpRewardOfferResolver levelUpOfferResolver = null;
            string levelUpOfferScheduleId =
                activeRun?.Profile?.levelUpOfferScheduleProfileId;
            if (!string.IsNullOrWhiteSpace(levelUpOfferScheduleId))
            {
                levelUpOfferSchedule =
                    LevelUpOfferScheduleProfileCatalog.Get(
                        levelUpOfferScheduleId);
                if (levelUpOfferSchedule == null)
                {
                    throw new InvalidOperationException(
                        $"关卡缺少固定升级 Offer 排程：" +
                        levelUpOfferScheduleId + "。");
                }
                levelUpOfferResolver = LevelUpOfferScheduleAdapter.Resolve(
                    levelUpOfferSchedule,
                    cardPool,
                    contentId => scenario != null &&
                        scenario.testOnlyUnconfirmed
                            ? WanjieScroll.Core.CardTestStatus
                                .CanUseInTestBattle(contentId)
                            : WanjieScroll.Core.CardTestStatus
                                .IsReady(contentId),
                    canReceiveSeals: () => SealRewardEligibility.CanReceiveSeals(_activeMeta));
            }
            List<CardConfig> fixedFirstLevelUpRewards =
                CreateFixedFirstLevelUpRewards(cardPool);
            if ((scenario == null || !scenario.testOnlyUnconfirmed) &&
                deck.Count == 0)
            {
                throw new InvalidOperationException(
                    "正式关卡牌组中没有已完成验收的卡牌。" +
                    "请先在 F7 卡牌特效测试场景中标记完成。");
            }
            string encounterNodeId = _preloadPlan?.NodeContentId;
            MarkStage("loadout");
            if (_preloadPlan.HasValue &&
                (activeDungeonRun == null ||
                 !string.Equals(_preloadPlan.Value.RunId, activeDungeonRun.RunId, StringComparison.Ordinal) ||
                 !string.Equals(_preloadPlan.Value.FloorContentId, activeDungeonRun.CurrentFloorContentId, StringComparison.Ordinal)))
                throw new InvalidOperationException("Prepared combat belongs to a different Run or floor.");
            // Inspector-only legacy battles have no exact launch node. Retain the
            // old unique authored/preset lookup; its owner rejects generated mode
            // without a node rather than inventing a new roster for diagnostics.
            bool explicitLaunch = encounterNodeId != null || activeRun?.ActiveEncounter.HasValue == true;
            List<EnemyStats> enemies;
            EnemyFormationPlan formationPlan;
            if (activeDungeonRun != null && scenario != null && explicitLaunch)
            {
                // One synchronous source validation owns roster and geometry.
                // Do not retain this source snapshot across calls or encounters.
                formationPlan = activeDungeonRun.MaterializeEnemyEncounter(
                    scenario.scenarioId, encounterNodeId,
                    encounter => CreateEnemiesFromContent(volumeId, encounter?.GeneratedRoster),
                    out enemies);
            }
            else
            {
                enemies = CreateEnemiesFromContent(volumeId, null);
                formationPlan = activeDungeonRun != null && scenario != null
                    ? activeDungeonRun.ResolveEnemyFormation(scenario.scenarioId, enemies, encounterNodeId)
                    : null;
            }
            MarkStage("encounter");
            buildClock.Stop();
            yield return null;
            buildClock.Start();
            PlayerStats player = scenario != null
                ? scenario.CreatePlayer()
                : CreateDefaultPlayer();
            bool applyMetaStats = scenario?.ResolveReferenceLoadout()
                ?.suppressMetaStatProgression != true;
            MetaProgression meta = ApplyDaozangProgression(
                player, applyMetaStats);
            _activeMeta = meta;
            activeDungeonRun?.ConfigureSealRewardEligibility(() =>
                SealRewardEligibility.CanReceiveSeals(meta));
            MarkStage("meta");
            buildClock.Stop();
            yield return null;
            buildClock.Start();
            // Progression owns account/release eligibility. Bootstrap only
            // supplies the already loaded account and the scene's release gate.
            LevelUpRewardRuleResolution levelUpRewardRule =
                NaturalLevelUpRewardCatalog.ResolveGlobal(
                        cardPool, volumeId, meta,
                        contentId => scenario != null && scenario.testOnlyUnconfirmed
                            ? CardTestStatus.CanUseInTestBattle(contentId)
                            : CardTestStatus.IsReady(contentId),
                        runContext: activeDungeonRun == null ? null : new CardUnlockRunContext(
                            activeDungeonRun.RunId, () => activeDungeonRun.RunProgression.CardUnlockFacts.Capture(),
                            activeDungeonRun.LeaderCrawlerContentId));
            List<CardConfig> upgradePool = levelUpRewardRule.Cards;
            MarkStage("reward-rules");
            buildClock.Stop();
            yield return null;
            buildClock.Start();
            if (activeDungeonRun != null)
            {
                if (!activeDungeonRun.TryPrepareCombat(
                        player,
                        deck,
                        cardPool,
                        out List<CardConfig> runDeck,
                        out string runPrepareError))
                {
                    throw new InvalidOperationException(
                        "恢复 Run 战斗状态失败：" + runPrepareError);
                }
                deck = runDeck;
                _encounterResources.OwnCards(deck);
                activeDungeonRun.RegisterPreparedCrawlers(
                    CollectPreparedCrawlerIds(deck));
                TrySaveActiveRun(meta);
            }
            // Run 先恢复唯一入口基线；地图永久增量只对这份战斗副本叠加一次。
            if (activeRun != null)
                LevelRunCombatModifier.ApplyToPlayer(player, activeRun.Modifiers);
            // Preparing instances, registering crawlers and saving remain one
            // synchronous transaction. Yield only after all three have finished.
            MarkStage("run-prepare");
            buildClock.Stop();
            yield return null;
            buildClock.Start();
            int timerEyes = scenario != null
                ? scenario.bossTimerEyes
                : 5;
            int timerDamage = scenario != null
                ? scenario.bossTimerDamage
                : 25;

            CombatRuleSet rules = CreateCombatRules(meta, activeRun, activeDungeonRun);
            RunProgressionState runState = activeDungeonRun?.RunProgression ??
                GameSession.Current.RunProgression;
            GameSaveService economySave = scenario == null &&
                EditorLevelMapQuickPlayRuntime.CanPersist
                ? GameSaveService.CreateDefault()
                : null;
            var economy = new EconomySystem(new EconomyConfig(), runState,
                amount =>
                {
                    if (meta == null)
                    {
                        GameSession.Current.PlayerLingyu += amount;
                        return;
                    }
                    meta.CreditLingyu(amount);
                    GameSession.Current.PlayerLingyu = meta.lingyu;
                    economySave?.Save(meta);
                });

            bool timerEnabled = scenario == null || scenario.bossTimerEnabled;
            BossTimer bossTimer = timerEnabled
                ? new BossTimer(timerEyes, timerDamage)
                : null;

            IReadOnlyList<int> waveSizes = scenario != null
                && scenario.enemyWaveSizes != null
                && scenario.enemyWaveSizes.Count > 0
                ? scenario.enemyWaveSizes
                : null;
            string formationPattern = scenario != null &&
                !string.IsNullOrWhiteSpace(scenario.enemyFormationPattern)
                ? scenario.enemyFormationPattern
                : null;
            IReadOnlyList<CardConfig> forcedOpeningHand =
                CreateForcedOpeningHand(
                    cardPool,
                    deck,
                    activeDungeonRun?.PendingForcedOpeningHandCardInstanceIds);
            IReadOnlyList<int> formationRows = scenario != null
                && scenario.enemyFormationRows != null
                && scenario.enemyFormationRows.Count > 0
                && formationPattern == null
                ? scenario.enemyFormationRows
                : null;
            if (formationPlan != null)
            {
                waveSizes = null;
                formationRows = null;
                formationPattern = null;
            }

            MarkStage("run-and-rules");
            _combat = new CombatController(
                player,
                deck,
                enemies,
                bossTimer,
                upgradePool,
                rules,
                runState,
                economy,
                waveSizes,
                fixedFirstLevelUpRewards,
                activeRun?.CombatProgression,
                formationRows,
                formationPattern,
                forcedOpeningHand.Count > 0 ? forcedOpeningHand : null,
                ResolveCrawlerByContentId,
                levelUpRewardRule.Config,
                residentCrawlers: activeDungeonRun?.CrawlerResidents,
                deckInitialization: activeDungeonRun != null
                    ? CardDeckInitialization.RestoreInstances
                    : CardDeckInitialization.CreateNewInstances,
                levelUpOfferResolver: levelUpOfferResolver,
                isEvolutionResultEligible: contentId => scenario != null && scenario.testOnlyUnconfirmed
                    ? CardTestStatus.CanUseInTestBattle(contentId)
                    : CardTestStatus.CanUseEvolutionResult(contentId),
                rewardRerollBank: activeDungeonRun,
                naturalRewardPool: levelUpRewardRule.NaturalPool,
                enemyFormationPlan: formationPlan,
                cardResolver: cardResolver,
                canReceiveSeals: () => SealRewardEligibility.CanReceiveSeals(meta));
            _encounterResources.AttachCombat(_combat);
            MarkStage("core");
            buildClock.Stop();
            yield return null;
            buildClock.Start();
            BuildWorldSpaceLayer(camera);
            MarkStage("world");
            buildClock.Stop();
            yield return null;
            buildClock.Start();
            BuildCanvasLayer(deck, camera);
            TianjiStatusView.Create(EncounterCanvas, rules, _combat);
            _battlefield?.EnemyArea?.ConfigureTooltipCanvas(_canvas);
            MarkStage("canvas");
            AudioConfig audioConfig = SetupAudio();
            SetupVisualEffectManager(camera);
            SetupCombatVisualBridge();
            SetupCombatReturnCoordinator(meta,
                NaturalLevelUpRewardCatalog.ResolveUnlockRequirements(cardPool));
            SetupSceneController(audioConfig, camera, scenario, meta);
            SetupPauseMenu();
            _sceneController.ConfigureRewardTooltipRules(rules);
            SetupCrawlerResidentFeedback();
            var bossAudioHost = new GameObject("BossMotionAudio");
            bossAudioHost.transform.SetParent(EncounterServices, false);
            var bossAudioPlayer = bossAudioHost.AddComponent<BossMotionAudioPlayer>();
            bossAudioPlayer.Bind(_audioManager);
            _bossMotionAudio = bossAudioHost.AddComponent<BossMotionAudioCoordinator>();
            _bossMotionAudio.Bind(_battlefield.EnemyArea, bossAudioPlayer);
            SetupEnemyTurnPresentationCoordinator();
            SetupHandDisruptionFeedback();
            SetupExperiencePresentationCoordinator(camera);
            _returnCoordinator?.SetSceneController(_sceneController);
            SetupLevelBossEntranceCoordinator(camera);

            // Setup entry coordinator for ordered combat startup
            SetupEntryCoordinator();

            MarkStage("bindings");
            buildClock.Stop();
            yield return null;
            buildClock.Start();
            // 所有 ViewModel 和 View 在预载阶段也必须完成绑定，但不能开始回合。
            _sceneController.BindNow();
            MarkStage("bind-now");

            // 预载/普通路径此前不预热手牌槽：首抽的 5 张会在揭幕后才新建
            // CardView，首次排版与字形图集都落在玩家已可见的窗口里。这里与
            // 驻留壳对齐，在装配期（转场遮幕内）先建好可复用槽位；只建槽，
            // 不绑定牌、不显示。
            _handView?.PreparePool(12);
            MarkStage("hand-pool");

            // Bind callbacks can revoke the launch synchronously. Completion
            // must validate again before ready, warmup or first-turn admission.
            if (_buildOperation == null || !_buildOperation.IsCurrent)
                yield break;
            if (_preloadHost == null && _residentHost == null)
            {
                SetPresentationVisible(true);
                // Activating the hierarchy may invoke user lifecycle callbacks.
                // A navigation from one of them must not enter this old combat.
                if (_buildOperation == null || !_buildOperation.IsCurrent)
                {
                    SetPresentationVisible(false);
                    yield break;
                }
                _battlefield?.EnemyArea?.PlayInitialEnemyEntrances();
            }
            IsBuilt = true;

            if (_preloadHost == null && _residentHost == null)
            {
                BeginDeferredBossPresentationWarmup();
                _entryCoordinator.BeginEntry(transitionRequestId);
            }
            Debug.Log(
                $"[CombatBootstrap] 战斗场景已装配：{volumeId}，" +
                $"卡牌 {deck.Count}，敌人 {enemies.Count}。");
            Debug.Log("[CombatBuildTiming] scenario=" + (scenario?.scenarioId ?? "default") +
                " resident=" + (_residentHost != null) + " shell=" + _canvas.GetInstanceID() +
                " encounter=" + _sceneController.GetInstanceID() +
                " total=" + buildClock.Elapsed.TotalMilliseconds.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) + "ms wall=" +
                wallClock.Elapsed.TotalMilliseconds.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) + "ms " + string.Join(" ", timings));
        }

        /// <summary>
        /// 解析战斗场景配置。优先级：
        /// 1. 活动遭遇的 scenario（从 LevelRunSessionStore）
        /// 2. Inspector 序列化字段
        /// 3. null（使用默认）
        /// 有活动遭遇但配置缺失时，中止遭遇并导航回 LevelMap，禁止进入默认战斗。
        /// </summary>
        private BattleScenarioProfile ResolveScenario()
        {
            LevelRunSession session = LevelRunSessionStore.Current;
            if (session != null && session.ActiveEncounter.HasValue)
            {
                string scenarioId = session.ActiveEncounter.Value.ScenarioId;
                BattleScenarioProfile catalogProfile =
                    BattleScenarioCatalog.Get(scenarioId);
                if (catalogProfile == null)
                {
                    Debug.LogError(
                        $"[CombatBootstrap] 活动 scenario 缺失：" +
                        $"{scenarioId}，中止遭遇并返回 LevelMap。");
                    // This is an entry failure, not a completed combat.  If
                    // the map opened a checkpoint before the hidden scene was
                    // built, restore it before returning; otherwise a stale
                    // checkpoint would block the next map entry.  The map is
                    // reloaded below, so replacing the hosted aggregate is
                    // safe here (unlike the in-place transition-rejection
                    // recovery owned by LevelMapEntryCoordinator).
                    if (DungeonRunSessionStore.HasCombatRollbackCheckpoint)
                    {
                        if (!DungeonRunSessionStore.TryRollbackCombat(
                                out _, out string rollbackError))
                        {
                            Debug.LogError(
                                "[CombatBootstrap] 活动 scenario 缺失且战斗检查点无法恢复：" +
                                rollbackError);
                            session.AbortActiveEncounter("scenario missing");
                            DungeonRunSessionStore.DiscardCombatRollbackCheckpoint();
                        }
                    }
                    else
                    {
                        session.AbortActiveEncounter("scenario missing");
                    }
                    SceneLoader.GoToLevelMap();
                    throw new InvalidOperationException(
                        $"活动 scenario 缺失：{scenarioId}，已中止遭遇。");
                }
                return catalogProfile;
            }
            return scenario;
        }

        private void EnsurePresentationShell(Camera camera, LevelRunSession floor = null)
        {
            if (_presentationShell != null)
            {
                BattleCameraViewOwner.EnsureOn(camera).BindHudLayout(_presentationShell.BoardHudLayout);
                return;
            }
            MapPreviewProfile map = standaloneMapPresentation;
            LevelRunSession session = floor ?? LevelRunSessionStore.Current;
            if (!string.IsNullOrWhiteSpace(session?.Profile?.mapPresentationContentId))
                map = MapPresentationCatalog.Get(session.Profile.mapPresentationContentId);
            _presentationShell = new CombatPresentationShell(transform, camera,
                _canvas, _worldSpaceRoot, battleHudPresentation, cardPrefab,
                cardPresentation, crawlerResidentLayout);
            _presentationShell.Prepare(map);
            _canvas = _presentationShell.Canvas;
            _worldSpaceRoot = _presentationShell.WorldRoot;
            _battlefield = _presentationShell.Battlefield;
            _battleHud = _presentationShell.Hud;
            _handView = _presentationShell.Hand;
            _playPileView = _presentationShell.PlayPile;
            _boardHudLayout = _presentationShell.BoardHudLayout;
            BattleCameraViewOwner.EnsureOn(camera).BindHudLayout(_boardHudLayout);
            _crawlerResidentPresenter = _presentationShell.Crawlers;
            // Newly created roots must inherit the reserved build's hidden
            // state before the next frame can render or receive input.
            SetPresentationVisible(IsPresentationVisible);
        }

        /// <summary>Builds only fixed presentation; no Run preparation or turn.</summary>
        public void PrepareResidentShell(LevelRunSession floor)
        {
            if (_residentHost == null || floor == null)
                throw new InvalidOperationException("Resident shell has no floor owner.");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            SetPresentationVisible(false);
            EnsureEventSystem(transform);
            Camera camera = ResolveCamera(transform);
            EnsurePresentationShell(camera, floor);
            _handView.PreparePool(12);
            SetPresentationVisible(false);
            Debug.Log("[CombatShellTiming] floor=" + floor.Profile.contentId +
                " shell=" + _canvas.GetInstanceID() +
                " prepared=" + clock.Elapsed.TotalMilliseconds.ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) + "ms");
        }

        public void BuildResidentEncounter(CombatScenePreloadPlan plan)
        {
            IEnumerator build = BuildResidentEncounterStaged(plan);
            try { while (build.MoveNext()) { } }
            finally { (build as IDisposable)?.Dispose(); }
        }

        /// <summary>Builds a fresh encounter on the retained shell without
        /// entering combat. The floor owner revokes its launch between stages.</summary>
        public IEnumerator BuildResidentEncounterStaged(CombatScenePreloadPlan plan,
            Func<bool> isCurrent = null)
        {
            if (_residentHost == null || _presentationShell == null || IsBuilt)
                throw new InvalidOperationException("Resident shell is not available for a new encounter.");
            if (_buildOperation != null)
                throw new InvalidOperationException("An encounter build is already in progress.");
            _preloadPlan = plan;
            scenario = BattleScenarioCatalog.Get(plan.ScenarioId);
            if (scenario == null) throw new InvalidOperationException("Missing encounter scenario: " + plan.ScenarioId);
            SetPresentationVisible(false);
            return BuildStaged(isCurrent);
        }

        public void SetResidentPresentationVisible(bool visible)
        {
            SetPresentationVisible(visible);
            if (visible)
                _battlefield?.EnemyArea?.PlayInitialEnemyEntrances();
        }

        private void BuildWorldSpaceLayer(Camera camera)
        {
            EnsurePresentationShell(camera);
            _presentationShell.BindEncounter(_combat,
                CollectPreparedCrawlerIds(_combat.Deck));
            _encounterRoot = new GameObject("EncounterServices");
            _encounterRoot.transform.SetParent(transform, false);
            _encounterWorldRoot = new GameObject("EncounterWorld");
            _encounterWorldRoot.transform.SetParent(_worldSpaceRoot, false);
            _encounterCanvasRoot = new GameObject("EncounterCanvas", typeof(RectTransform));
            _encounterCanvasRoot.transform.SetParent(_canvas.transform, false);
            var rect = (RectTransform)_encounterCanvasRoot.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            ConfigureActorEffects();
        }

        private void ConfigureActorEffects()
        {
            _battlefield.EnemyArea.ConfigureEffectTargets(
                (_, target, __) =>
                {
                    if (target == null ||
                        target.GetComponent<HitFlash>() != null)
                    {
                        return;
                    }

                    target.gameObject.AddComponent<HitFlash>();
                });
        }

        private void BuildCanvasLayer(IReadOnlyList<CardConfig> deck, Camera battleCamera)
        {
            _handView.CardDrawFeedbackRequested -= OnCardDrawFeedbackRequested;
            _handView.CardDrawFeedbackRequested += OnCardDrawFeedbackRequested;
            if (scenario != null && !string.IsNullOrWhiteSpace(scenario.introHintText))
                BattleIntroHintView.Create(EncounterCanvas).Bind(_combat, scenario.introHintText);
        }

        private AudioConfig SetupAudio()
        {
            // 返回地图后 AudioManager 常驻；再次进入战斗时场景里的序列化副本
            // 会在 Awake 自毁，因此必须优先复用有效单例。
            AudioManager audioManager = AudioManager.Instance != null
                ? AudioManager.Instance
                : _audioManager;
            if (audioManager == null)
            {
                var audioObject = new GameObject("AudioManager");
                audioManager = audioObject.AddComponent<AudioManager>();
            }
            _audioManager = audioManager;
            if (audioManager.config == null)
            {
                audioManager.config = Resources.Load<AudioConfig>(
                    AudioConfig.DefaultResourcePath);
                if (audioManager.config == null)
                    audioManager.config = ScriptableObject.CreateInstance<AudioConfig>();
            }
            return audioManager.config;
        }

        private void SetupVisualEffectManager(Camera camera)
        {
            var vfxObject = new GameObject("VisualEffectManager");
            vfxObject.transform.SetParent(EncounterServices, false);
            _vfxManager =
                vfxObject.AddComponent<VisualEffectManager>();
            if (_audioManager != null)
            {
                _vfxManager.SetOneShotAudio(
                    new PersistentEffectOneShotAudio(_audioManager));
            }

            // 玩家攻击起点：屏幕底部中央（卡牌区域），无需英雄角色
            var playerOrigin = new GameObject("PlayerOrigin");
            playerOrigin.transform.SetParent(EncounterWorld, false);
            playerOrigin.transform.localPosition = new Vector3(0f, -3.5f, 0f);
            _vfxManager.playerAttackOrigin = playerOrigin.transform;

            _bossActionEffectPlayer =
                vfxObject.AddComponent<BossActionEffectPlayer>();
            _bossActionEffectPlayer.Configure(
                _vfxManager.effectContainer,
                camera,
                playerOrigin.transform);
            _bossCloseupImpactFeedback = vfxObject.AddComponent<BossCloseupImpactFeedback>();
            _bossCloseupImpactFeedback.Bind(_battlefield.EnemyArea, _combat,
                _bossActionEffectPlayer, Resources.Load<BossAttackCloseupProfile>(
                    BossAttackCloseupProfile.DefaultResourcePath));
            // Contact SFX has a separate voice owner from the native video track.
            var contactAudio = vfxObject.AddComponent<BossMotionAudioPlayer>();
            contactAudio.Bind(_audioManager);
            _bossCloseupImpactFeedback.ConfigureContactAudio(contactAudio,
                Resources.Load<AudioClip>(BossCloseupImpactFeedback.ContactAudioResourcePath));

            _battlefield.EnemyArea.ConfigureEffectTargets(
                (index, target, entranceMotionRoot) =>
                {
                    _vfxManager.RegisterEnemy(
                        index,
                        target,
                        entranceMotionRoot);
                    _bossActionEffectPlayer.RegisterEnemy(
                        index,
                        target,
                        entranceMotionRoot);
                });

            _vfxManager.defaultProjectileSprite =
                RuntimeArtLoader.GetKnifeProjectile();
            if (_vfxManager.defaultProjectileSprite == null)
            {
                _vfxManager.defaultProjectileSprite =
                    CreateDefaultProjectileSprite();
            }
            _vfxManager.particleTexture =
                RuntimeArtLoader.GetBloodParticle();
            _vfxManager.sparkParticleTexture =
                RuntimeArtLoader.GetSparkParticle();
        }

        private void OnCardDrawFeedbackRequested()
        {
            _audioManager?.PlaySfx(SfxType.CardDraw);
        }

        private void SetupCombatVisualBridge()
        {
            var bridgeObject =
                new GameObject("CombatVisualBridge");
            bridgeObject.transform.SetParent(EncounterServices, false);
            _visualBridge =
                bridgeObject.AddComponent<CombatVisualBridge>();
            _visualBridge.combat = _combat;
            _visualBridge.vfxManager = _vfxManager;
            _battlefield?.EnemyArea?.BindAttackEffectCompletionResolver(
                _visualBridge.CaptureAttackEffectCompletion);
            _visualBridge.DeathSubjectVacated += OnDeathSubjectVacated;
            _visualBridge.BindEnemyGroundAnchorResolver(index =>
            {
                if (_battlefield?.EnemyArea == null ||
                    !_battlefield.EnemyArea.TryGetEnemyGroundAnchor(index, out Vector3 anchor))
                    return (Vector3?)null;
                return anchor;
            });

            var periodicHost = new GameObject("PeriodicAttackPresentation");
            periodicHost.transform.SetParent(_vfxManager.transform, false);
            _periodicAttackPresenter = periodicHost.AddComponent<PeriodicAttackPresenter>();
            _periodicAttackPresenter.Bind(
                _combat, _vfxManager, _mainCamera != null ? _mainCamera : Camera.main,
                () =>
                {
                    if (_battlefield?.EnemyArea == null ||
                        !_battlefield.EnemyArea.TryGetBoardWorldFrame(
                            out Vector3 frontLeft, out Vector3 frontRight,
                            out Vector3 backLeft, out Vector3 backRight)) return (Bounds?)null;
                    var bounds = new Bounds(frontLeft, Vector3.zero);
                    bounds.Encapsulate(frontRight);
                    bounds.Encapsulate(backLeft);
                    bounds.Encapsulate(backRight);
                    return bounds;
                });
            _visualBridge.BindPeriodicAttackPresenter(_periodicAttackPresenter);
            _visualBridge.BindBoardFrameResolver(() =>
            {
                if (_battlefield?.EnemyArea == null || !_battlefield.EnemyArea.TryGetBoardWorldFrame(
                    out Vector3 frontLeft, out Vector3 frontRight, out Vector3 backLeft, out Vector3 backRight))
                    return (WanjieScroll.Effects.Projectiles.BoardVisualFrame?)null;
                return new WanjieScroll.Effects.Projectiles.BoardVisualFrame(frontLeft, frontRight, backLeft, backRight,
                    _battlefield.EnemyArea.GetBoardProjectileSortingOrder);
            });
        }

        private void OnCardPlayScreenOriginPrepared(int cardRuntimeId, Vector2 screenOrigin)
        {
            Camera camera = _sceneController != null && _sceneController.mainCamera != null
                ? _sceneController.mainCamera : (_mainCamera != null ? _mainCamera : Camera.main);
            if (camera == null) return;
            float worldZ = _vfxManager?.playerAttackOrigin != null
                ? _vfxManager.playerAttackOrigin.position.z : 0f;
            Ray ray = camera.ScreenPointToRay(screenOrigin);
            var battlefieldPlane = new Plane(Vector3.forward, new Vector3(0f, 0f, worldZ));
            if (battlefieldPlane.Raycast(ray, out float distance))
            {
                Vector3 origin = ray.GetPoint(distance);
                _periodicAttackPresenter?.PrepareCardOrigin(cardRuntimeId, origin);
                _supportArtifactFeedback?.PrepareCardOrigin(cardRuntimeId, origin);
            }
        }

        private void OnCardPlayOriginRejected(int cardRuntimeId)
        {
            _periodicAttackPresenter?.ForgetCardOrigin(cardRuntimeId);
            _supportArtifactFeedback?.ForgetCardOrigin(cardRuntimeId);
        }

        private void OnDeathSubjectVacated(int enemyRuntimeId)
        {
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[VFX] Bootstrap OnDeathSubjectVacated runtime={enemyRuntimeId}");
            if (_battlefield == null || _battlefield.EnemyArea == null)
                return;

            _battlefield.EnemyArea.ReleaseDefeatHold(
                enemyRuntimeId);
        }

        private void SetupCombatReturnCoordinator(MetaProgression meta,
            CardUnlockRequirements unlockRequirements)
        {
            var unlockObject = new GameObject("RunScopedCardUnlockCoordinator");
            unlockObject.transform.SetParent(EncounterServices, false);
            unlockObject.AddComponent<RunScopedCardUnlockCoordinator>()
                .Bind(_combat, meta, unlockRequirements);
            var coordinatorObject =
                new GameObject("CombatReturnCoordinator");
            coordinatorObject.transform.SetParent(EncounterServices, false);
            _returnCoordinator =
                coordinatorObject.AddComponent<CombatReturnCoordinator>();
            _returnCoordinator.Bind(
                _combat,
                _visualBridge,
                SceneTransitionRuntime.ExternalModalBarrier,
                runStateChanged: () =>
                {
                    if (!EditorLevelMapQuickPlayRuntime.CanPersist) return;
                    DungeonRunSession run = DungeonRunSessionStore.Current;
                    if (run == null) throw new InvalidOperationException("战斗提交缺少活动Run。");
                    if (!CardUnlockLevelTransaction.TryCommit(meta,
                        CardUnlockAcceptance.FromRun(run), unlockRequirements,
                        () => GameSaveService.CreateDefault().SaveTransactional(meta, run.Serialize()),
                        () => { }, out _, out string error))
                        throw new InvalidOperationException(error);
                });
        }

        private void SetupPauseMenu()
        {
            if (_canvas == null || _pauseMenu != null)
                return;
            _pauseMenu = CombatPauseMenu.Create(EncounterServices);
            _pauseMenu.ReturnToHubRequested += ReturnToHubFromPause;
            _pauseMenu.QuitRequested += QuitFromPause;
        }

        private void ReturnToHubFromPause()
        {
            // F5/QuickPlay remains a transient test boundary: it must roll
            // back and return to the map without touching the production slot.
            // A formal run has a different contract.  Keep its active
            // encounter in the save, so the Hub portal can route straight back
            // to Combat instead of silently creating a fresh map entry.
            if (ShouldResumeFormalCombat())
            {
                if (!TrySaveActiveRun(_activeMeta, preserveCombatResume: true))
                {
                    Debug.LogError(
                        "[CombatBootstrap] 无法保存正式战斗恢复点，取消返回仙域。");
                    _pauseMenu?.ShowError("战斗进度保存失败，请重试。");
                    return;
                }
                DungeonRunSessionStore.DiscardCombatRollbackCheckpoint();
            }
            else
            {
                if (!RollbackAndAbortCombat("返回仙域", out string rollbackError))
                {
                    Debug.LogError(
                        "[CombatBootstrap] 返回仙域前无法恢复战斗检查点：" +
                        rollbackError);
                    _pauseMenu?.ShowError("战斗检查点恢复失败，请重试。");
                    return;
                }
            }

            _pauseMenu?.Close();
            Time.timeScale = 1f;
            SceneContext.Clear();
            SceneLoader.GoToHub();
        }

        private void QuitFromPause()
        {
            if (ShouldResumeFormalCombat())
            {
                if (!TrySaveActiveRun(_activeMeta, preserveCombatResume: true))
                {
                    Debug.LogError(
                        "[CombatBootstrap] 无法保存正式战斗恢复点，取消退出游戏。");
                    _pauseMenu?.ShowError("战斗进度保存失败，请重试。");
                    return;
                }
                DungeonRunSessionStore.DiscardCombatRollbackCheckpoint();
            }
            else
            {
                if (!RollbackAndAbortCombat("退出游戏", out string rollbackError))
                {
                    Debug.LogError(
                        "[CombatBootstrap] 退出游戏前无法恢复战斗检查点：" +
                        rollbackError);
                    _pauseMenu?.ShowError("战斗检查点恢复失败，请重试。");
                    return;
                }
            }

            _pauseMenu?.Close();
            Time.timeScale = 1f;
            CombatPauseMenu.RequestApplicationQuit();
        }

        private bool ShouldResumeFormalCombat()
        {
            DungeonRunSession run = DungeonRunSessionStore.Current;
            return EditorLevelMapQuickPlayRuntime.CanPersist &&
                run != null && run.CurrentFloor != null &&
                run.CurrentFloor.ActiveEncounter.HasValue;
        }

        private static bool RollbackAndAbortCombat(
            string reason,
            out string error)
        {
            error = null;
            if (DungeonRunSessionStore.HasCombatRollbackCheckpoint &&
                !DungeonRunSessionStore.TryRollbackCombat(
                    out _, out error))
                return false;

            LevelRunSession current = LevelRunSessionStore.Current;
            current?.AbortActiveEncounter("player left combat: " + reason);
            return true;
        }

        private static bool TrySaveActiveRun(
            MetaProgression meta,
            bool preserveCombatResume = false)
        {
            if (!EditorLevelMapQuickPlayRuntime.CanPersist)
                return true;
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run == null)
                return true;
            try
            {
                string resumePayload;
                bool hasPayload;
                if (preserveCombatResume)
                {
                    hasPayload = DungeonRunSessionStore
                        .TryGetCombatResumePayload(out resumePayload);
                }
                else
                {
                    hasPayload = DungeonRunSessionStore
                        .TryGetCombatPersistencePayload(out resumePayload);
                }
                if (!hasPayload)
                {
                    Debug.LogWarning(
                        "[CombatBootstrap] 活动 Run 没有可保存的载荷。");
                    return false;
                }
                GameSaveService.CreateDefault().Save(
                    meta ?? new MetaProgression(),
                    resumePayload);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[CombatBootstrap] 保存活动 Run 失败：{exception.Message}");
                return false;
            }
        }

        private void SetupExperiencePresentationCoordinator(Camera camera)
        {
            if (_combat == null || _visualBridge == null ||
                _sceneController == null)
                return;

            var coordinatorObject =
                new GameObject("CombatExperiencePresentationCoordinator");
            coordinatorObject.transform.SetParent(EncounterServices, false);
            _experiencePresentationCoordinator =
                coordinatorObject.AddComponent<
                    CombatExperiencePresentationCoordinator>();
            _experienceTransferEffect =
                coordinatorObject.AddComponent<ExperienceTransferEffect>();
            if (_audioManager != null)
            {
                _experienceTransferEffect.SetOneShotAudio(
                    new PersistentEffectOneShotAudio(_audioManager));
            }
            _experiencePresentationCoordinator.Bind(
                _combat,
                _visualBridge,
                _sceneController,
                _battleHud,
                _canvas,
                camera,
                _experienceTransferEffect);
            _experiencePresentationCoordinator.LevelUpCheckpointStarted +=
                PausePresentationForLevelUp;
            _experiencePresentationCoordinator.LevelUpCheckpointCompleted +=
                ResumePresentationAfterLevelUp;
        }

        private void SetupEnemyTurnPresentationCoordinator()
        {
            if (_combat == null || _sceneController == null)
                return;

            var coordinatorObject =
                new GameObject("EnemyTurnPresentationCoordinator");
            coordinatorObject.transform.SetParent(EncounterServices, false);
            _enemyTurnPresentationCoordinator =
                coordinatorObject.AddComponent<
                    EnemyTurnPresentationCoordinator>();
            _enemyTurnPresentationCoordinator.Bind(
                _combat,
                _sceneController,
                _battlefield != null ? _battlefield.EnemyArea : null,
                _battleHud,
                _vfxManager,
                _audioManager,
                _bossActionEffectPlayer,
                () => (_visualBridge != null && _visualBridge.HasPendingImpacts) ||
                    (_handDisruptionFeedback != null && _handDisruptionFeedback.IsPresenting));
        }

        private void SetupHandDisruptionFeedback()
        {
            var host = new GameObject("EnemyHandDisruptionFeedback");
            host.transform.SetParent(EncounterServices, false);
            var injection = host.AddComponent<EnemyCardInjectionEffectPlayer>();
            _handDisruptionFeedback = host.AddComponent<EnemyHandDisruptionFeedbackCoordinator>();
            _handDisruptionFeedback.Bind(_combat, _handView, _sceneController.deckPileView,
                _battlefield.EnemyArea, _mainCamera != null ? _mainCamera : Camera.main, injection);
        }

        private void SetupCrawlerResidentFeedback()
        {
            if (_combat == null || _crawlerResidentPresenter == null ||
                _battleHud == null || _sceneController == null)
            {
                return;
            }

            var coordinatorObject =
                new GameObject("CrawlerResidentFeedbackCoordinator");
            coordinatorObject.transform.SetParent(EncounterServices, false);
            _crawlerResidentFeedbackCoordinator =
                coordinatorObject.AddComponent<
                    CrawlerResidentFeedbackCoordinator>();
            _crawlerResidentFeedbackCoordinator.Bind(
                _combat,
                _crawlerResidentPresenter,
                _battleHud);
            _sceneController.crawlerResidentFeedback =
                _crawlerResidentFeedbackCoordinator;
            var supportObject = new GameObject("SupportArtifactFeedbackCoordinator");
            supportObject.transform.SetParent(EncounterServices, false);
            _supportArtifactFeedback = supportObject.AddComponent<SupportArtifactFeedbackCoordinator>();
            _supportArtifactFeedback.Bind(_combat, _vfxManager, _battleHud,
                _mainCamera != null ? _mainCamera : Camera.main, _crawlerResidentPresenter);
            _visualBridge?.BindCardCastOriginResolver(_supportArtifactFeedback.FindCardOrigin);
            _createdCardPresentation = supportObject.AddComponent<CreatedCardPresentationCoordinator>();
            _createdCardPresentation.Bind(_combat, _visualBridge, _handView,
                _mainCamera != null ? _mainCamera : Camera.main, _supportArtifactFeedback);
        }

        private void PausePresentationForLevelUp()
        {
            if (_levelUpPresentationPaused) return;
            _levelUpPresentationPaused = true;
            _visualBridge?.SetLevelUpCheckpointPaused(true);
            _experienceTransferEffect?.SetPaused(true);
            _handView?.SetPresentationPaused(true);
            _vfxManager?.SetLevelUpPaused(true);
        }

        private void ResumePresentationAfterLevelUp()
        {
            if (!_levelUpPresentationPaused) return;
            _vfxManager?.SetLevelUpPaused(false);
            _handView?.SetPresentationPaused(false);
            _experienceTransferEffect?.SetPaused(false);
            _visualBridge?.SetLevelUpCheckpointPaused(false);
            _levelUpPresentationPaused = false;
        }

        private void SetupSceneController(
            AudioConfig audioConfig,
            Camera camera,
            BattleScenarioProfile scenario,
            MetaProgression meta)
        {
            var controllerObject =
                new GameObject("CombatSceneController");
            controllerObject.transform.SetParent(EncounterServices, false);
            _sceneController =
                controllerObject.AddComponent<CombatSceneController>();
            _sceneController.combat = _combat;
            _sceneController.artConfig =
                ScriptableObject.CreateInstance<ArtConfig>();
            _encounterResources.Own(_sceneController.artConfig);
            _sceneController.encounterCanvasRoot = (RectTransform)EncounterCanvas;
            _sceneController.audioConfig = audioConfig;
            _sceneController.handView = _handView;
            _sceneController.battleHudView = _battleHud;
            _sceneController.boardHudLayout = _boardHudLayout;
            _sceneController.enemyAreaView = _battlefield.EnemyArea;
            _sceneController.ConfigureRewardUtilities(
                ResolveRewardUtilityAvailability(scenario, meta));
            Transform parent = EncounterCanvas;
            Debug.Log($"[Bootstrap] Creating views under parent: {parent.name}");

            var hudGo = new GameObject("HUDView", typeof(WanjieScroll.UI.Views.HUDView));
            hudGo.transform.SetParent(parent, false);
            _sceneController.hudView = hudGo.GetComponent<WanjieScroll.UI.Views.HUDView>();

            var lupGo = new GameObject("LevelUpPanel", typeof(WanjieScroll.UI.Views.LevelUpPanel));
            lupGo.transform.SetParent(parent, false);
            _sceneController.levelUpPanel = lupGo.GetComponent<WanjieScroll.UI.Views.LevelUpPanel>();

            var dpvGo = new GameObject("DeckPileView", typeof(RectTransform), typeof(WanjieScroll.UI.Views.DeckPileView));
            dpvGo.transform.SetParent(parent, false);
            var pileRect = dpvGo.GetComponent<RectTransform>();
            pileRect.anchorMin = Vector2.zero;
            pileRect.anchorMax = Vector2.one;
            pileRect.offsetMin = pileRect.offsetMax = Vector2.zero;
            _sceneController.deckPileView = dpvGo.GetComponent<WanjieScroll.UI.Views.DeckPileView>();
            _sceneController.deckPileView.Configure(_combat,
                _battleHud != null ? _battleHud.deckCountText : null,
                _battleHud != null ? _battleHud.discardCountText : null,
                _playPileView, _sceneController.AcquireCombatInputLock,
                () => _sceneController.IsCombatInputEnabled,
                _sceneController.handView);

            Debug.Log($"[Bootstrap] Views created: pureParticleCombo=true hudView={_sceneController.hudView != null} levelUpPanel={_sceneController.levelUpPanel != null} deckPileView={_sceneController.deckPileView != null}");

            _sceneController.effectLayer = EncounterWorld;
            _sceneController.mainCamera = camera;
            _sceneController.CardPlayScreenOriginPrepared += OnCardPlayScreenOriginPrepared;
            _sceneController.CardPlayOriginRejected += OnCardPlayOriginRejected;
        }

        private static RewardUtilityAvailability
            ResolveRewardUtilityAvailability(
                BattleScenarioProfile scenario,
                MetaProgression meta)
        {
            if (scenario != null &&
                scenario.rewardUtilityPolicy ==
                RewardUtilityPolicy.Disabled)
            {
                return RewardUtilityAvailability.Locked;
            }

            return meta == null
                ? RewardUtilityAvailability.Locked
                : new RewardUtilityAvailability(
                    meta.rewardRefreshUnlocked,
                    meta.rewardAbandonUnlocked,
                    meta.rewardExcludeUnlocked);
        }

        private void SetupEntryCoordinator()
        {
            // Ensure Canvas has a CanvasGroup for HUD fade
            if (_canvas != null)
            {
                var group = _canvas.GetComponent<CanvasGroup>();
                if (group == null)
                    group = _canvas.gameObject.AddComponent<CanvasGroup>();
            }

            var coordinatorObject = new GameObject("CombatEntryCoordinator");
            coordinatorObject.transform.SetParent(EncounterServices, false);
            _entryCoordinator =
                coordinatorObject.AddComponent<CombatEntryCoordinator>();

            var canvasGroup = _canvas != null
                ? _canvas.GetComponent<CanvasGroup>()
                : null;
            _entryCoordinator.Bind(
                _sceneController,
                canvasGroup,
                0f,
                scenario != null ? scenario.entryExperienceGrant : 0,
                _levelBossEntranceCoordinator != null
                    ? _levelBossEntranceCoordinator.ContinueOpeningEntry
                    : (Action<Action>)null);
        }

        private void SetupLevelBossEntranceCoordinator(Camera camera)
        {
            if (_combat == null || _battlefield == null ||
                _battlefield.EnemyArea == null || _vfxManager == null)
            {
                return;
            }

            var coordinatorObject =
                new GameObject("LevelBossEntranceCoordinator");
            coordinatorObject.transform.SetParent(EncounterServices, false);
            _levelBossEntranceCoordinator =
                coordinatorObject.AddComponent<LevelBossEntranceCoordinator>();
            _levelBossEntranceCoordinator.Bind(
                _combat,
                _battlefield.EnemyArea,
                _sceneController,
                _vfxManager,
                _visualBridge,
                _audioManager,
                camera != null ? camera : _mainCamera);
        }

        /// <summary>
        /// Starts presentation-only warmup after the Combat hierarchy becomes
        /// visible. UI owns atlas/audio/Sprite preparation; Effects owns its
        /// active Infinity host. Bootstrap only coordinates their lifetime.
        /// </summary>
        private void BeginDeferredBossPresentationWarmup()
        {
            _battlefield?.EnemyArea?.BeginDeferredBossPresentationLoad();
            _levelBossEntranceCoordinator?.BeginPresentationWarmup();
        }

        /// <summary>显示已经构建好的战斗，并开始遭遇入场与首轮抽牌表现。</summary>
        public bool ActivatePrepared(
            string transitionRequestId,
            out string error)
        {
            error = null;
            if ((_preloadHost == null && _residentHost == null) || !IsBuilt || _entryCoordinator == null)
            {
                error = "后台战斗尚未完成装配。";
                return false;
            }

            SetPresentationVisible(true);
            _battlefield?.EnemyArea?.PlayInitialEnemyEntrances();
            BeginDeferredBossPresentationWarmup();
            _entryCoordinator.BeginEntry(transitionRequestId);
            return true;
        }

        private void SetPresentationVisible(bool visible)
        {
            bool previous = IsPresentationVisible;
            IsPresentationVisible = visible;

            if (_worldSpaceRoot != null)
                _worldSpaceRoot.gameObject.SetActive(visible);

            if (_canvas != null)
            {
                _canvas.enabled = visible;
                GraphicRaycaster raycaster =
                    _canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                    raycaster.enabled = visible;
            }

            if (_eventSystem != null)
                _eventSystem.gameObject.SetActive(visible);

            if (_mainCamera != null)
            {
                _mainCamera.enabled = visible;
                AudioListener listener =
                    _mainCamera.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = visible;
            }
            SceneAudioListenerContinuity.Ensure().Reconcile();

            if (CombatLogger.EnableVerboseDiagnostics && previous != visible)
            {
                GraphicRaycaster raycaster = _canvas != null
                    ? _canvas.GetComponent<GraphicRaycaster>()
                    : null;
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[CombatHoverDiag] Combat presentation " +
                        $"{previous}->{visible}; canvas={_canvas?.enabled}, " +
                        $"raycaster={raycaster?.enabled}, ownEventSystem=" +
                        $"{_eventSystem?.name ?? "null"}, ownEventActive=" +
                        $"{_eventSystem?.gameObject.activeInHierarchy}, current=" +
                        $"{EventSystem.current?.name ?? "null"}");
            }
        }

        private List<CardConfig> CreateDeckFromContent(
            IReadOnlyList<CardConfig> cardPool,
            DungeonRunSession activeDungeonRun)
        {
            if (scenario != null)
            {
                return RunCombatDeckResolver.Resolve(scenario, activeDungeonRun,
                    GameSession.Current.SelectedCrawlerId, cardPool, IsCardEligibleForCurrentScene);
            }

            var deck = new List<CardConfig>();
            for (int i = 0;
                 i < cardPool.Count && deck.Count < 12;
                 i++)
            {
                if (WanjieScroll.Core.CardTestStatus.CanUseInFormalPlay(
                        cardPool[i].contentId))
                    deck.Add(cardPool[i]);
            }
            return deck;
        }

        private bool IsCardEligibleForCurrentScene(string contentId)
        {
            return scenario != null && scenario.testOnlyUnconfirmed
                ? WanjieScroll.Core.CardTestStatus.CanUseInTestBattle(contentId)
                : WanjieScroll.Core.CardTestStatus.CanUseInFormalPlay(contentId);
        }

        private List<CardConfig> CreateFixedFirstLevelUpRewards(
            IReadOnlyList<CardConfig> cardPool)
        {
            return scenario == null ||
                scenario.fixedFirstLevelUpRewardCardContentIds == null ||
                scenario.fixedFirstLevelUpRewardCardContentIds.Count == 0
                ? new List<CardConfig>()
                : ResolveCardsFromPool(
                    scenario.fixedFirstLevelUpRewardCardContentIds,
                    cardPool,
                    "首次升级奖励");
        }

        private List<CardConfig> CreateForcedOpeningHand(
            IReadOnlyList<CardConfig> cardPool,
            IReadOnlyList<CardConfig> runDeck,
            IReadOnlyList<int> forcedCardInstanceIds)
        {
            List<CardConfig> result = scenario == null ||
                scenario.forcedOpeningHandContentIds == null ||
                scenario.forcedOpeningHandContentIds.Count == 0
                ? new List<CardConfig>()
                : ResolveCardsFromPool(
                    scenario.forcedOpeningHandContentIds,
                    cardPool,
                    "强制起手");

            if (forcedCardInstanceIds == null || runDeck == null) return result;
            var guarantees = new List<CardConfig>();
            foreach (int id in forcedCardInstanceIds)
                for (int i = 0; i < runDeck.Count; i++)
                {
                    CardConfig card = runDeck[i];
                    if (card == null || card.runtimeInstanceId != id || guarantees.Contains(card)) continue;
                    guarantees.Add(card); break;
                }
            result.InsertRange(0, guarantees);
            return result;
        }

        private List<CardConfig> ResolveCardsFromPool(
            IReadOnlyList<string> contentIds,
            IReadOnlyList<CardConfig> cardPool,
            string errorLabel)
        {
            return StableCardPoolResolver.ResolveEligible(
                contentIds,
                cardPool,
                IsCardEligibleForCurrentScene,
                errorLabel);
        }

        private List<EnemyStats> CreateEnemiesFromContent(
            string volumeId, CommonEncounterFrozenRoster frozenRoster = null)
        {
            List<EnemyStats> enemies = frozenRoster != null
                ? ContentRuntimeFactory.CreateRequiredEnemyLineup(volumeId,
                    frozenRoster.EnemyContentIds, scenario.enemyHealthOverride,
                    scenario.enemyExperienceOverride, scenario.levelBossContentId, scenario.levelBossRank)
                : ContentRuntimeFactory.CreateEnemyLineup(volumeId,
                    scenario != null ? scenario.enemyContentIds : null,
                    scenario != null ? scenario.enemyHealthOverride : 0,
                    scenario != null ? scenario.enemyExperienceOverride : 0,
                    scenario != null ? scenario.levelBossContentId : null,
                    scenario != null ? scenario.levelBossRank : BossRank.Floor);
            _encounterResources?.OwnEnemyDefinitions(enemies);
            return enemies;
        }

        private List<string> CollectPreparedCrawlerIds(
            IReadOnlyList<CardConfig> deck)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (scenario != null &&
                !string.IsNullOrWhiteSpace(scenario.crawlerContentId) &&
                seen.Add(scenario.crawlerContentId))
            {
                result.Add(scenario.crawlerContentId);
            }

            if (deck == null)
                return result;

            for (int cardIndex = 0; cardIndex < deck.Count; cardIndex++)
            {
                CardConfig card = deck[cardIndex];
                if (card?.effects == null)
                    continue;

                for (int effectIndex = 0;
                     effectIndex < card.effects.Count;
                     effectIndex++)
                {
                    CardEffectSpec effect = card.effects[effectIndex];
                    if (effect == null ||
                        (effect.effectType != EffectType.SummonCrawler &&
                         effect.effectType !=
                            EffectType.IncreaseCrawlerDuration) ||
                        string.IsNullOrWhiteSpace(effect.crawlerContentId) ||
                        !seen.Add(effect.crawlerContentId))
                    {
                        continue;
                    }

                    result.Add(effect.crawlerContentId);
                }
            }

            return result;
        }

        /// <summary>
        /// 英灵卡（EffectType.SummonCrawler）按稳定 contentId 解析英灵配置，
        /// 统一走 ContentRuntimeFactory.CreateCrawlerByContentId。
        /// 解析失败返回 null，由战斗控制器记录警告而非中断出牌。
        /// </summary>
        private static CrawlerConfig ResolveCrawlerByContentId(string contentId)
        {
            return ContentRuntimeFactory.CreateCrawlerByContentId(contentId);
        }

        private static PlayerStats CreateDefaultPlayer()
        {
            return new PlayerStats
            {
                maxHealth = 120,
                currentHealth = 120,
                faLi = 12,
                shouPai = 20,
                chouPai = 20,
                fengRui = 10,
                tianYun = 15,
                hunHun = 10
            };
        }

        /// <summary>
        /// 从存档加载道藏经诀等级。实测参考装载可以只禁用局外属性叠加，
        /// 但仍返回 MetaProgression 供遗珍与 Arcana 战斗规则读取。
        /// </summary>
        private MetaProgression ApplyDaozangProgression(
            PlayerStats player,
            bool applyStatProgression = true)
        {
            if (player == null) return null;
            MetaProgression meta = null;
            try
            {
                var save = GameSaveService.CreateDefault();
                DungeonRunSession accountRun = DungeonRunSessionStore.Current;
                LevelRunSession accountFloor = LevelRunSessionStore.Current;
                bool hasRunEncounter = accountFloor?.ActiveEncounter.HasValue == true ||
                    (_preloadPlan.HasValue && accountRun != null &&
                     string.Equals(_preloadPlan.Value.RunId, accountRun.RunId, StringComparison.Ordinal) &&
                     string.Equals(_preloadPlan.Value.FloorContentId, accountRun.CurrentFloorContentId,
                         StringComparison.Ordinal));
                if (accountRun == null || !ReferenceEquals(
                        accountRun.CurrentFloor, accountFloor) || !hasRunEncounter)
                    accountRun = null;
                // The retained map, its reward ledgers and this encounter must
                // keep one account object. Reloading a second copy would reset
                // transient earnings and later let the old map overwrite them.
                meta = RunAccountScope.Resolve(accountRun, save.SavePath, () =>
                {
                    var loaded = new MetaProgression();
                    if (!save.TryLoad(loaded, out _))
                        loaded.lingyu = GameSession.Current.PlayerLingyu;
                    return loaded;
                });
                if (applyStatProgression)
                    DaozangConfig.ApplyToPlayerStats(meta, player);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[CombatBootstrap] 道藏存档加载失败，跳过经诀属性叠加：{e.Message}");
            }
            return meta ?? new MetaProgression();
        }

        /// <summary>
        /// 教程关 contentId：其叠势/相生激活节奏由本局拾取决定，
        /// 重玩时不继承存档中的遗珍持久解锁。
        /// </summary>
        public const string TutorialLevelContentId =
            WanjieScroll.Progression.RunCombatRules.TutorialLevelContentId;

        public static CombatRuleSet CreateCombatRules(
            MetaProgression meta,
            LevelRunSession activeRun, DungeonRunSession dungeonRun = null)
            => WanjieScroll.Progression.RunCombatRules.Create(meta, activeRun, dungeonRun);

        private static Camera EnsureCamera(Transform owner)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject(
                    "Main Camera",
                    typeof(Camera),
                    typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(owner, false);
                camera = cameraObject.GetComponent<Camera>();
            }
            ConfigureCamera(camera);
            return camera;
        }

        private Camera ResolveCamera(Transform owner)
        {
            Camera camera = _mainCamera;
            // Additive preparation must never take or reconfigure the map camera.
            if (camera == null && Camera.main != null && Camera.main.gameObject.scene == gameObject.scene)
                camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject(
                    "Main Camera",
                    typeof(Camera),
                    typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(owner, false);
                camera = cameraObject.GetComponent<Camera>();
            }
            _mainCamera = camera;
            ConfigureCamera(camera);
            SetPresentationVisible(IsPresentationVisible);
            return camera;
        }

        private static void ConfigureCamera(Camera camera)
        {
            BattleCameraViewOwner existingView = camera.GetComponent<BattleCameraViewOwner>();
            if (existingView != null) existingView.CancelView();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor =
                new Color(0.08f, 0.06f, 0.05f);
            SpriteCameraRenderingPolicy.Apply(camera);
            if (camera.GetComponent<ScreenShake>() == null)
                camera.gameObject.AddComponent<ScreenShake>();
            BattleCameraViewOwner.EnsureOn(camera).ConfigureBaseOrthographicSize(5.4f);
        }

        private void EnsureEventSystem(Transform owner)
        {
            if (_eventSystem != null)
            {
                // Combat is loaded additively. The serialized EventSystem is
                // intentionally inactive in Combat.unity so LevelMap remains
                // the sole active input owner during preloading. Directly
                // opening Combat has no preloader, so restore the requested
                // presentation state here before BuildNow returns.
                _eventSystem.gameObject.SetActive(IsPresentationVisible);
                return;
            }
            var eventObject = new GameObject(
                "CombatEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventObject.transform.SetParent(owner, false);
            _eventSystem = eventObject.GetComponent<EventSystem>();
            eventObject.SetActive(IsPresentationVisible);
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    "[CombatHoverDiag] Created Combat-owned EventSystem; " +
                    $"scene={gameObject.scene.name}, owner={owner.name}, " +
                    $"active={eventObject.activeInHierarchy}.");
        }

        private static Sprite CreateDefaultProjectileSprite()
        {
            const int size = 32;
            var texture =
                new Texture2D(
                    size,
                    size,
                    TextureFormat.RGBA32,
                    false);
            texture.filterMode = FilterMode.Bilinear;
            float center = size / 2f;
            float radius = size / 2f - 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(
                        new Vector2(x, y),
                        new Vector2(center, center));
                    float alpha = distance <= radius - 2f
                        ? 1f
                        : distance <= radius
                            ? 1f - (distance - radius + 2f) / 2f
                            : 0f;
                    Color color = Color.Lerp(
                        Color.white,
                        new Color(1f, 0.9f, 0.5f),
                        distance / radius);
                    color.a = alpha;
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        private void UnbindEncounterSubscriptions()
        {
            if (_bossMotionAudio != null) _bossMotionAudio.Unbind();
            if (_handDisruptionFeedback != null) _handDisruptionFeedback.Unbind();
            if (_createdCardPresentation != null) _createdCardPresentation.Unbind();
            if (_supportArtifactFeedback != null) _supportArtifactFeedback.Unbind();
            if (_sceneController != null)
            {
                _sceneController.CardPlayScreenOriginPrepared -= OnCardPlayScreenOriginPrepared;
                _sceneController.CardPlayOriginRejected -= OnCardPlayOriginRejected;
            }
            if (_handView != null)
                _handView.CardDrawFeedbackRequested -=
                    OnCardDrawFeedbackRequested;
            if (_visualBridge != null)
                _visualBridge.DeathSubjectVacated -= OnDeathSubjectVacated;
            if (_experiencePresentationCoordinator != null)
            {
                _experiencePresentationCoordinator.LevelUpCheckpointStarted -=
                    PausePresentationForLevelUp;
                _experiencePresentationCoordinator.LevelUpCheckpointCompleted -=
                    ResumePresentationAfterLevelUp;
            }
            _levelUpPresentationPaused = false;
        }

        /// <summary>Releases only per-encounter objects; the owner waits one frame
        /// before another binding so deferred destruction cannot reach a new fight.</summary>
        public void ReleaseResidentEncounter()
        {
            // Revocation precedes cleanup. A caller retaining an old iterator
            // cannot resume it after this shell has admitted another encounter.
            EncounterBuildOperation abandonedBuild = _buildOperation;
            _buildOperation = null;
            abandonedBuild?.Dispose();
            _bossCloseupImpactFeedback?.Unbind();
            UnbindEncounterSubscriptions();
            if (_sceneController != null) _sceneController.ShutdownEncounter();
            _presentationShell?.ReleaseEncounter();
            if (_encounterRoot != null) Destroy(_encounterRoot);
            if (_encounterWorldRoot != null) Destroy(_encounterWorldRoot);
            if (_encounterCanvasRoot != null) Destroy(_encounterCanvasRoot);
            _encounterResources?.Dispose();
            _encounterResources = null;
            _encounterRoot = _encounterWorldRoot = _encounterCanvasRoot = null;
            _combat = null;
            _sceneController = null;
            _entryCoordinator = null;
            _vfxManager = null;
            _bossActionEffectPlayer = null;
            _bossCloseupImpactFeedback = null;
            _levelBossEntranceCoordinator = null;
            _visualBridge = null;
            _periodicAttackPresenter = null;
            _experiencePresentationCoordinator = null;
            _enemyTurnPresentationCoordinator = null;
            _experienceTransferEffect = null;
            _returnCoordinator = null;
            _crawlerResidentFeedbackCoordinator = null;
            _supportArtifactFeedback = null;
            _createdCardPresentation = null;
            _handDisruptionFeedback = null;
            _bossMotionAudio = null;
            _pauseMenu = null;
            _activeMeta = null;
            _preloadPlan = null;
            IsBuilt = false;
        }

        private void OnDestroy()
        {
            // Native scene teardown can destroy views before this component.
            // Their own OnDestroy cancels presentation; only detach this owner.
            EncounterBuildOperation abandonedBuild = _buildOperation;
            _buildOperation = null;
            abandonedBuild?.Dispose();
            _bossCloseupImpactFeedback?.Unbind();
            UnbindEncounterSubscriptions();
            _encounterResources?.Dispose();
            _encounterResources = null;
        }

        /// <summary>
        /// The bootstrap's one reserved assembly lifetime. It owns only iterator
        /// scheduling/disposal, keeping profiler scopes within one frame and
        /// rejecting a replaced Run/Floor before any further constructor runs.
        /// </summary>
        private sealed class EncounterBuildOperation : IEnumerator, IDisposable
        {
            private readonly CombatSceneBootstrap _owner;
            private readonly DungeonRunSession _run;
            private readonly LevelRunSession _floor;
            private readonly Func<bool> _isCurrent;
            private readonly IEnumerator _steps;
            private bool _disposed;
            public string TransitionRequestId { get; }

            public EncounterBuildOperation(CombatSceneBootstrap owner,
                Func<bool> isCurrent)
            {
                _owner = owner;
                _run = DungeonRunSessionStore.Current;
                _floor = LevelRunSessionStore.Current;
                _isCurrent = isCurrent;
                TransitionRequestId = SceneContext.Get<string>(
                    SceneContextKeys.TransitionRequestId, null);
                _steps = owner.BuildStages();
            }

            public object Current => _steps.Current;
            public bool IsCurrent => !_disposed && _owner != null &&
                ReferenceEquals(_owner._buildOperation, this) &&
                ReferenceEquals(_run, DungeonRunSessionStore.Current) &&
                ReferenceEquals(_floor, LevelRunSessionStore.Current) &&
                (_isCurrent == null || _isCurrent());

            public bool MoveNext()
            {
                if (_disposed) return false;
                try
                {
                    if (!IsCurrent)
                    {
                        Dispose();
                        return false;
                    }
                    // Auto scopes cannot remain open over a yield/frame.
                    using (BuildNowMarker.Auto())
                    {
                        bool advanced = _steps.MoveNext();
                        if (!advanced) Dispose();
                        return advanced;
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                (_steps as IDisposable)?.Dispose();
                if (_owner == null ||
                    !ReferenceEquals(_owner._buildOperation, this)) return;
                _owner._buildOperation = null;
                if (!_owner.IsBuilt) _owner.ReleaseResidentEncounter();
            }

            public void Reset() => throw new NotSupportedException();
        }
    }
}
