using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Audio;
using WanjieScroll.Bootstrap;
using WanjieScroll.Core;
using WanjieScroll.Content;
using WanjieScroll.UI.Views;
using WanjieScroll.UI;
using WanjieScroll.UI.Cards;
using WanjieScroll.UI.Components;
using WanjieScroll.UI.Battle;
using WanjieScroll.Effects.Attacks;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// VFX 独立测试脚本（专用场景用）
    /// 从权威内容目录构建已登记卡牌列表；验收状态决定标签和
    /// “标记完成”是否可用，默认选未确认卡，选择器可切换全部与已确认。
    /// 操作：点击卡牌或Space=触发出牌 E=结束回合 A=自动发射 R=重置 S=+2护甲 X=-1护甲。
    /// 规模审查：本测试 Bootstrap 超过 800 行，但只组装 F7 验收场景与转发
    /// 测试意图；卡牌实例、战斗规则、卡面、特效状态机仍分别由现有深模块拥有。
    /// 选中卡入手牌仅编排这些公开入口，不复制配置或持有第二套结算规则。
    /// 2026-10-06生成牌接线同正式场景，仅创建/注入/解绑独立owner，
    /// 不在本文件维护凝符队列或复制手牌显隐、沙灵回灵状态。
    /// 2026-10-08补齐完整定义解析依赖，并区分换选清手与显式重测补牌；
    /// 生成、临时生命周期及手牌仍由Core拥有，展示由既有owner消费回执。
    /// 2026-10-13乱魂蛊与正式场景装配同一纸纹协调器，仅消费Core实际回执；
    /// 此处不重抽触发色、不自行播放低铃或保存第二份展示状态。
    /// </summary>
    public class VFXTestBootstrap : MonoBehaviour
    {
        private const string DefaultScenarioResourcePath =
            "Content/Battle/BattleComponentShowcase";
        private const int MinimumTestEnemyHealth = 500;

        [Header("共享战斗配置")]
        [Tooltip("为空时读取 F6 使用的 BattleComponentShowcase。")]
        public BattleScenarioProfile scenario;

        [Header("测试卡牌选择")]
        [Tooltip("从权威内容目录中已登记验收的卡牌自动构建。")]
        public int selectedCardIndex = 0;
        [Tooltip("可测试卡牌列表（自动生成，勿手动编辑）")]
        [SerializeField] private List<string> availableCardNames = new List<string>();

        [Header("抽牌特效测试")]
        [Tooltip("F7 初始化时重复放入几组共享场景牌组，避免连续测试抽牌时过快耗尽。")]
        [Min(1)] public int testDeckCopies = 6;

        private List<CardConfig> _availableCards = new List<CardConfig>();
        private List<CardConfig> _availableCardPool = new List<CardConfig>();

        // Editor 访问
        public int SelectedCardIndex
        {
            get => selectedCardIndex;
            set => selectedCardIndex = value;
        }
        public List<string> AvailableCardNames => availableCardNames;
        public void SetAvailableCardNames(List<string> names) { availableCardNames = names; }

        private VisualEffectManager _vfx;
        private CombatVisualBridge _bridge;
        private PeriodicAttackPresenter _periodicAttackPresenter;
        private EnemyTurnPresentationCoordinator _enemyTurnPresentationCoordinator;
        private LevelBossEntranceCoordinator _levelBossEntranceCoordinator;
        private CombatController _combat;
        private BattlefieldView _battlefield;
        private Transform _playerAttackOrigin;
        private BattleHudView _battleHud;
        private HandView _testHandView;
        private PlayPileView _testPlayPile;
        private HandViewModel _testHandViewModel;
        private CardTooltipView _testCardTooltip;
        private CrawlerResidentPresenter _crawlerResidentPresenter;
        private CrawlerResidentFeedbackCoordinator
            _crawlerResidentFeedbackCoordinator;
        private SupportArtifactFeedbackCoordinator _supportArtifactFeedback;
        private CreatedCardPresentationCoordinator _createdCardPresentation;
        private EnemyHandDisruptionFeedbackCoordinator _handDisruptionFeedback;
        private BossMotionAudioCoordinator _bossMotionAudio;
        private CrawlerResidentLayoutProfile _crawlerResidentLayout;
        private BattleScenarioProfile _activeScenario;
        private EventSystem _shortcutEventSystem;
        private bool _previousNavigationEvents;
        private Button _completionButton;
        private Text _completionButtonLabel;
        private int _completionLabelIndex = -1;
        private VfxTestTuningPanel _tuningPanel;
        private CrawlerResidentTuningPanel _crawlerResidentTuningPanel;
        private CardConfig _stagedTestCard;
        private int _stagedTestCardIndex = -1;

        // F7 reads authoritative Core receipts for its audit log. This is a
        // compact accumulator only; it does not duplicate damage calculation.
        private sealed class F7CardAudit
        {
            internal string CardName;
            internal string ContentId;
            internal int ExpectedDisplayedDamage;
            internal int ExpectedDirectEvents;
            internal int ExpectedBounceEvents;
            internal int AppliedDamage;
            internal int EventCount;
        }
        private readonly Dictionary<int, F7CardAudit> _f7CardAudits =
            new Dictionary<int, F7CardAudit>();

        private float _autoFireTimer;
        private bool _autoFire = false;
        private bool _initialized;

        private void Start()
        {
            Init();
        }

        private void Init()
        {
            if (_bossMotionAudio != null) _bossMotionAudio.Unbind();
            if (_handDisruptionFeedback != null) _handDisruptionFeedback.Unbind();
            if (_createdCardPresentation != null) _createdCardPresentation.Unbind();
            if (_crawlerResidentFeedbackCoordinator != null) _crawlerResidentFeedbackCoordinator.Unbind();
            if (_supportArtifactFeedback != null) _supportArtifactFeedback.Unbind();
            if (_enemyTurnPresentationCoordinator != null) _enemyTurnPresentationCoordinator.Unbind();
            if (_bridge != null)
                _bridge.DeathSubjectVacated -= OnDeathSubjectVacated;
            if (_combat != null)
            {
                _combat.OnCardPlayResolved -= OnCardPlayResolvedPresentation;
                _combat.OnCardsDrawn -= OnCardsDrawnPresentation;
                _combat.OnStateChanged -= OnCombatStateChanged;
                UnbindF7AuditEvents(_combat);
            }
            _testHandViewModel?.Dispose();
            CleanupOldObjects();
            ReleaseAvailableCardPool();

            _combat = null;
            _vfx = null;
            _bridge = null;
            _periodicAttackPresenter = null;
            _enemyTurnPresentationCoordinator = null;
            _levelBossEntranceCoordinator = null;
            _battlefield = null;
            _testHandView = null;
            _testPlayPile = null;
            _testHandViewModel = null;
            _testCardTooltip = null;
            _crawlerResidentPresenter = null;
            _crawlerResidentFeedbackCoordinator = null;
            _crawlerResidentLayout = null;
            _completionButton = null;
            _completionButtonLabel = null;
            _completionLabelIndex = -1;
            _tuningPanel = null;
            _crawlerResidentTuningPanel = null;
            _stagedTestCard = null;
            _stagedTestCardIndex = -1;
            _f7CardAudits.Clear();
            _initialized = false;

            VfxProfileRegistry.Initialize();
            _activeScenario = ResolveScenario();
            BuildAvailableCards();

            Camera cam = SetupCamera();
            SetupHero();
            SetupBattlefield(cam);
            SetupCombat();
            SetupVFX();
            SetupLevelBossEntrance(cam);
            SetupHud();
            SetupEnemyTurnPresentation();
            // Bind every draw/effect observer before the first automatic card.
            _combat.StartTurn();
            StageSelectedCardInHand(resetHand: true);
            _initialized = true;
            Debug.Log(
                $"[VFXTest] 就绪。{VfxTestTuningPanel.Revision} / " +
                $"{CrawlerResidentTuningPanel.Revision} / " +
                $"{Projectiles.RuyiStaffParticleVisual.Revision} / " +
                $"{Attacks.ProceduralWhipSweepPlayer.Revision}。" +
                "英灵卡选中时显示独立显化面板；" +
                "点击卡牌/Space=出牌 E=结束回合 A=自动 R=重置 " +
                "S=+2护甲 X=-1护甲");
        }

        private void CleanupOldObjects()
        {
            var names = new[] {
                "WorldSpace", "VisualEffectManager", "CombatVisualBridge",
                "LevelBossEntranceCoordinator",
                "EnemyTurnPresentationCoordinator",
                "Main Camera", "BattleBackground", "PlayerOrigin",
                "PaperCutDeath", "Battlefield", "EnemyArea",
                "CrawlerResidentPresenter", "VfxHudCanvas", "SupportArtifactFeedbackCoordinator",
                "VfxTestEventSystem"
            };
            foreach (var name in names)
            {
                var go = GameObject.Find(name);
                if (go != null) DestroyImmediate(go);
            }
        }

        private void Update()
        {
            if (!_initialized || _combat == null || _vfx == null)
            {
                if (Time.frameCount % 60 == 0)
                {
                    Debug.Log("[VFXTest] 自动重建...");
                    Init();
                }
                return;
            }

            // Inspector 与运行时面板都可以切卡；面板只在索引变化时刷新。
            _tuningPanel?.SetSelectedIndex(selectedCardIndex);
            if (_availableCards.Count > 0 && selectedCardIndex >= 0)
            {
                int selected = Mathf.Clamp(
                    selectedCardIndex,
                    0,
                    _availableCards.Count - 1);
                _crawlerResidentTuningPanel?.SetSelectedCard(
                    _availableCards[selected]);
                if (selected != _stagedTestCardIndex &&
                    !IsTurnPresentationActive)
                {
                    StageSelectedCardInHand(resetHand: true);
                }
            }
            else if (selectedCardIndex < 0 && _stagedTestCardIndex >= 0 &&
                !IsTurnPresentationActive)
            {
                ClearStagedCardFromHand();
            }

            if (Input.GetKeyDown(KeyCode.Space))
                PlaySelectedCard();

            RefreshCompletionButton();

            if (Input.GetKeyDown(KeyCode.A))
            {
                _autoFire = !_autoFire;
                Debug.Log($"[VFXTest] 自动发射: {(_autoFire ? "开" : "关")}");
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                Debug.Log("[VFXTest] 手动重置");
                Init();
            }

            if (Input.GetKeyDown(KeyCode.E))
                EndTestTurn();

            if (Input.GetKeyDown(KeyCode.S))
            {
                _combat.Player.armor += 2;
                _battleHud?.SetResources(
                    _combat.Player.currentHealth,
                    _combat.Player.maxHealth,
                    _combat.Mana.CurrentMana + _combat.Mana.TemporaryMana,
                    _combat.Mana.BaseMana);
                _battleHud?.SetArmor(_combat.Player.armor);
                Debug.Log($"[VFXTest] 护甲+2 → 当前护甲: {_combat.Player.armor}");
            }

            if (Input.GetKeyDown(KeyCode.X))
            {
                if (_combat.Player.armor > 0)
                {
                    _combat.Player.armor -= 1;
                    _battleHud?.SetArmor(_combat.Player.armor);
                    Debug.Log($"[VFXTest] 护甲-1 → 当前护甲: {_combat.Player.armor}");
                }
            }

            if (_autoFire)
            {
                _autoFireTimer -= Time.deltaTime;
                if (_autoFireTimer <= 0f)
                {
                    PlaySelectedCard();
                    _autoFireTimer = 2.5f;
                }
            }
        }

        // ==================== 场景搭建（复刻战斗场景） ====================

        private Camera SetupCamera()
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            var cam = go.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.04f);
            SceneAudioListenerContinuity.Ensure().Reconcile();
            return cam;
        }

        private void SetupHero()
        {
            var playerOriginObject = new GameObject("PlayerOrigin");
            _playerAttackOrigin = playerOriginObject.transform;
            _playerAttackOrigin.position = new Vector3(0f, -3.5f, 0f);
        }

        private void SetupBattlefield(Camera cam)
        {
            RuntimeArtLoader.ClearCache();
        }

        private void SetupCombat()
        {
            BattleScenarioProfile active = _activeScenario ?? ResolveScenario();
            PlayerStats player = active.CreatePlayer();
            List<CardConfig> testDeck = CreateScenarioDeck(
                active,
                Mathf.Max(1, testDeckCopies));
            List<EnemyStats> enemies = ContentRuntimeFactory.CreateEnemyLineup(
                active.volumeId,
                active.enemyContentIds,
                Mathf.Max(MinimumTestEnemyHealth, active.enemyHealthOverride),
                active.enemyExperienceOverride,
                active.levelBossContentId,
                active.levelBossRank);
            _combat = new CombatController(
                player,
                testDeck,
                enemies,
                // F7 只验收卡牌表现。Boss 倒计时会按出牌次数自动扣血，
                // 与测试者是否主动结束回合无关，因此专用场景始终禁用。
                bossTimer: null,
                crawlerResolver:
                    ContentRuntimeFactory.CreateCrawlerByContentId,
                // 生成牌可能不在F7筛选结果或普通奖池中；只从完整作者定义池解析。
                cardResolver: contentId => _availableCardPool.FirstOrDefault(
                    definition => definition != null &&
                        string.Equals(definition.contentId, contentId,
                            System.StringComparison.OrdinalIgnoreCase)));
            BindF7AuditEvents(_combat);
            Debug.Log(
                $"[VFXTest] 抽牌测试牌库: {testDeck.Count} 张 " +
                $"({Mathf.Max(1, testDeckCopies)} 组), " +
                $"每回合抽牌数: {player.shouPai}, " +
                $"怪物最低生命: {MinimumTestEnemyHealth}, " +
                "自动 Boss 伤害: 关闭");

            Camera cam = Camera.main;
            var worldRoot = new GameObject("WorldSpace");
            _battlefield = BattlefieldView.Create(worldRoot.transform, cam, _combat);

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

        private void SetupVFX()
        {
            var vfxGo = new GameObject("VisualEffectManager");
            _vfx = vfxGo.AddComponent<VisualEffectManager>();
            _vfx.playerAttackOrigin = _playerAttackOrigin;

            // 确保 AudioManager 单例存在并注入给 VFX，否则命中/出牌音不会播放
            AudioManager audioManager = AudioManager.Instance;
            if (audioManager == null)
            {
                var audioGo = new GameObject("AudioManager");
                audioManager = audioGo.AddComponent<AudioManager>();
            }
            if (audioManager.config == null)
            {
                audioManager.config = Resources.Load<AudioConfig>(
                    AudioConfig.DefaultResourcePath);
            }
            _vfx.SetOneShotAudio(
                new PersistentEffectOneShotAudio(audioManager));

            _battlefield.EnemyArea.ConfigureEffectTargets(
                _vfx.RegisterEnemy);

            _vfx.defaultProjectileSprite = RuntimeArtLoader.GetKnifeProjectile();
            _vfx.particleTexture = RuntimeArtLoader.GetBloodParticle();
            _vfx.sparkParticleTexture = RuntimeArtLoader.GetSparkParticle();

            var bridgeGo = new GameObject("CombatVisualBridge");
            _bridge = bridgeGo.AddComponent<CombatVisualBridge>();
            _bridge.combat = _combat;
            _bridge.vfxManager = _vfx;
            _battlefield?.EnemyArea?.BindAttackEffectCompletionResolver(
                _bridge.CaptureAttackEffectCompletion);
            _bridge.DeathSubjectVacated += OnDeathSubjectVacated;
            _bridge.BindEnemyGroundAnchorResolver(index =>
            {
                if (_battlefield?.EnemyArea == null ||
                    !_battlefield.EnemyArea.TryGetEnemyGroundAnchor(index, out Vector3 anchor))
                    return (Vector3?)null;
                return anchor;
            });
            _bridge.BindEvents();

            var periodicHost = new GameObject("PeriodicAttackPresentation");
            periodicHost.transform.SetParent(_vfx.transform, false);
            _periodicAttackPresenter = periodicHost.AddComponent<PeriodicAttackPresenter>();
            _periodicAttackPresenter.Bind(_combat, _vfx, Camera.main,
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
            _bridge.BindPeriodicAttackPresenter(_periodicAttackPresenter);
            _bridge.BindBoardFrameResolver(() =>
            {
                if (_battlefield?.EnemyArea == null || !_battlefield.EnemyArea.TryGetBoardWorldFrame(
                    out Vector3 frontLeft, out Vector3 frontRight, out Vector3 backLeft, out Vector3 backRight))
                    return (WanjieScroll.Effects.Projectiles.BoardVisualFrame?)null;
                return new WanjieScroll.Effects.Projectiles.BoardVisualFrame(frontLeft, frontRight, backLeft, backRight,
                    _battlefield.EnemyArea.GetBoardProjectileSortingOrder);
            });
        }

        private void SetupLevelBossEntrance(Camera camera)
        {
            if (_combat == null || _battlefield?.EnemyArea == null ||
                _vfx == null)
            {
                return;
            }

            var host = new GameObject("LevelBossEntranceCoordinator");
            host.transform.SetParent(transform, false);
            _levelBossEntranceCoordinator =
                host.AddComponent<LevelBossEntranceCoordinator>();
            _levelBossEntranceCoordinator.Bind(
                _combat,
                _battlefield.EnemyArea,
                null,
                _vfx,
                _bridge,
                AudioManager.Instance,
                camera);
            _levelBossEntranceCoordinator.ContinueOpeningEntry(null);
        }

        private void SetupEnemyTurnPresentation()
        {
            var audioHost = new GameObject("BossMotionAudio");
            audioHost.transform.SetParent(transform, false);
            var audioPlayer = audioHost.AddComponent<BossMotionAudioPlayer>();
            audioPlayer.Bind(AudioManager.Instance);
            _bossMotionAudio = audioHost.AddComponent<BossMotionAudioCoordinator>();
            _bossMotionAudio.Bind(_battlefield.EnemyArea, audioPlayer);
            var host = new GameObject("EnemyTurnPresentationCoordinator");
            host.transform.SetParent(transform, false);
            _enemyTurnPresentationCoordinator = host.AddComponent<EnemyTurnPresentationCoordinator>();
            _enemyTurnPresentationCoordinator.Bind(
                _combat, null, _battlefield.EnemyArea, _battleHud, _vfx,
                AudioManager.Instance, hasPendingImpacts: () => (_bridge != null && _bridge.HasPendingImpacts) ||
                    (_handDisruptionFeedback != null && _handDisruptionFeedback.IsPresenting));
        }

        private bool IsBattlePresentationActive =>
            (_bridge != null && _bridge.IsPresentationActive) ||
            (_testHandView != null && _testHandView.IsCardPresentationActive) ||
            IsTurnPresentationActive;

        // Card visuals are concurrent. Only an actual turn/reward boundary
        // blocks new plays; completion/end-turn still use the full barrier.
        private bool IsTurnPresentationActive =>
            (_enemyTurnPresentationCoordinator != null && _enemyTurnPresentationCoordinator.IsPresenting) ||
            (_combat != null && _combat.IsEnemyTurnSequenceActive);

        private bool CanPlayTestCards => _combat != null &&
            _combat.State.CanPlayCards && !_combat.State.AwaitingRewardSelection &&
            !IsTurnPresentationActive;

        private void OnCombatStateChanged(CombatStateChangeReason reason)
        {
            _battleHud?.SetResources(
                _combat.Player.currentHealth, _combat.Player.maxHealth,
                _combat.Mana.CurrentMana + _combat.Mana.TemporaryMana, _combat.Mana.BaseMana,
                deferResourceDisplay:
                    _combat.State.CurrentPhase == BattlePhase.CardResolving &&
                    reason != CombatStateChangeReason.CardPlayed &&
                    reason != CombatStateChangeReason.CombatEnded);
            _battleHud?.SetArmor(_combat.Player.armor);
            _battleHud?.RefreshBuffsForStateChange(_combat.Player,
                _combat.GetDisplayStatValue, reason, _combat.State.CurrentPhase);
        }

        private void OnDeathSubjectVacated(int enemyRuntimeId)
        {
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[VFX] VFXTestBootstrap OnDeathSubjectVacated runtime={enemyRuntimeId}");
            if (_battlefield == null || _battlefield.EnemyArea == null)
                return;

            _battlefield.EnemyArea.ReleaseDefeatHold(
                enemyRuntimeId);
        }

        private void SetupHud()
        {
            EnsureEventSystem();
            var canvasGo = new GameObject(
                "VfxHudCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            // ScreenSpaceCamera 模式 + FXAA，与战斗场景一致
            Camera cam = Camera.main;
            if (cam != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 100f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            canvas.sortingOrder = 100;

            var raycaster = canvasGo.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.ignoreReversedGraphics = false;
                raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            }

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _crawlerResidentLayout =
                CrawlerResidentLayoutProfile.LoadDefault();
            _crawlerResidentPresenter =
                CrawlerResidentPresenter.Create(
                    canvasGo.transform,
                    _crawlerResidentLayout);
            _crawlerResidentPresenter.Prepare(
                _availableCards
                    .Where(card => card?.effects != null)
                    .SelectMany(card => card.effects)
                    .Where(effect =>
                        effect != null &&
                        (effect.effectType == EffectType.SummonCrawler ||
                         effect.effectType ==
                            EffectType.IncreaseCrawlerDuration))
                    .Select(effect => effect.crawlerContentId));
            _crawlerResidentPresenter.Bind(_combat);

            // 统一走 BattleHudFactory.Create（内部加载 prefab 实例化）
            _battleHud = BattleHudFactory.Create(
                canvasGo.transform,
                BattleHudPresentationProfile.LoadDefault());
            var tooltipObject = new GameObject(
                "CardTooltip",
                typeof(RectTransform));
            tooltipObject.transform.SetParent(canvasGo.transform, false);
            _testCardTooltip =
                tooltipObject.AddComponent<CardTooltipView>();

            _testHandView = BattleHandFactory.Create(
                canvasGo.transform,
                null,
                CardPresentationProfile.LoadDefault());
            _testPlayPile = PlayPileView.Create(canvasGo.transform);
            _testHandView.playPileView = _testPlayPile;
            _testHandView.deckCountText = _battleHud.deckCountText;
            _testHandView.discardCountText = _battleHud.discardCountText;
            _testHandViewModel = new HandViewModel();
            _testHandViewModel.Bind(_combat);
            _testHandView.Bind(_testHandViewModel);
            _testHandView.SetCardClickCallback(OnTestCardClicked);
            _testHandView.SetHoverCallback(
                OnTestCardHoverEnter,
                OnTestCardHoverExit);
            // 保留资源、护甲和属性栏，供伤害/护甲/增益卡共用同一套落点。
            if (_battleHud.progressEmptyImage != null)
                _battleHud.progressEmptyImage.transform.parent.gameObject.SetActive(false);
            if (_battleHud.heroAvatarImage != null)
                _battleHud.heroAvatarImage.transform.parent.gameObject.SetActive(false);
            if (_battleHud.leftBuffBar != null)
                _battleHud.leftBuffBar.gameObject.SetActive(true);
            if (_battleHud.rightBuffBar != null)
                _battleHud.rightBuffBar.gameObject.SetActive(true);
            if (_battleHud.drawButton != null)
                _battleHud.drawButton.gameObject.SetActive(false);
            if (_battleHud.playAllButton != null)
                _battleHud.playAllButton.gameObject.SetActive(false);
            if (_battleHud.endTurnButton != null)
                _battleHud.endTurnButton.gameObject.SetActive(false);
            if (_battleHud.discardButton != null)
                _battleHud.discardButton.gameObject.SetActive(false);

            _battleHud.SetResources(
                _combat.Player.currentHealth,
                _combat.Player.maxHealth,
                _combat.Mana.CurrentMana + _combat.Mana.TemporaryMana,
                _combat.Mana.BaseMana);
            _battleHud.SetArmor(0);
            _battleHud.RefreshBuffs(
                _combat.Player,
                _combat.GetDisplayStatValue);
            var crawlerFeedbackObject = new GameObject(
                "CrawlerResidentFeedbackCoordinator",
                typeof(CrawlerResidentFeedbackCoordinator));
            crawlerFeedbackObject.transform.SetParent(
                canvasGo.transform,
                false);
            _crawlerResidentFeedbackCoordinator =
                crawlerFeedbackObject.GetComponent<
                    CrawlerResidentFeedbackCoordinator>();
            _crawlerResidentFeedbackCoordinator.Bind(
                _combat,
                _crawlerResidentPresenter,
                _battleHud);
            var supportObject = new GameObject("SupportArtifactFeedbackCoordinator");
            supportObject.transform.SetParent(canvasGo.transform, false);
            _supportArtifactFeedback = supportObject.AddComponent<SupportArtifactFeedbackCoordinator>();
            _supportArtifactFeedback.Bind(_combat, _vfx, _battleHud, Camera.main, _crawlerResidentPresenter);
            _bridge?.BindCardCastOriginResolver(_supportArtifactFeedback.FindCardOrigin);
            _createdCardPresentation = supportObject.AddComponent<CreatedCardPresentationCoordinator>();
            _createdCardPresentation.Bind(_combat, _bridge, _testHandView, Camera.main, _supportArtifactFeedback);
            _handDisruptionFeedback = supportObject.AddComponent<EnemyHandDisruptionFeedbackCoordinator>();
            _handDisruptionFeedback.Bind(_combat, _testHandView, null,
                _battlefield.EnemyArea, Camera.main, null);
            _combat.OnCardPlayResolved += OnCardPlayResolvedPresentation;
            _combat.OnCardsDrawn += OnCardsDrawnPresentation;
            _combat.OnStateChanged += OnCombatStateChanged;
            CreateCompletionButton(canvasGo.transform);
            _tuningPanel = VfxTestTuningPanel.Create(
                canvasGo.transform,
                OnTuningPanelSelectionChanged,
                PlaySelectedCard);
            _tuningPanel.BindCards(_availableCards, selectedCardIndex);
            _crawlerResidentTuningPanel =
                CrawlerResidentTuningPanel.Create(
                    canvasGo.transform,
                    _crawlerResidentLayout,
                    ReplaySelectedCrawlerEntry,
                    ReplaySelectedCrawlerExit,
                    RefreshCrawlerResidentLayout,
                    SetCrawlerResidentAuraPreview);
            if (_availableCards.Count > 0 && selectedCardIndex >= 0)
            {
                _crawlerResidentTuningPanel.SetSelectedCard(
                    _availableCards[Mathf.Clamp(
                        selectedCardIndex,
                        0,
                        _availableCards.Count - 1)]);
            }
        }

        private void EnsureEventSystem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject(
                    "VfxTestEventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystemObject.transform.SetParent(transform, false);
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }
            // F7 uses explicit keyboard shortcuts and pointer controls. UGUI's
            // Submit also binds Space and could accept the next card after a click.
            if (_shortcutEventSystem != eventSystem)
            {
                RestoreEventSystemNavigation();
                _shortcutEventSystem = eventSystem;
                _previousNavigationEvents = eventSystem.sendNavigationEvents;
            }
            eventSystem.sendNavigationEvents = false;
            eventSystem.SetSelectedGameObject(null);
        }

        private void RestoreEventSystemNavigation()
        {
            if (_shortcutEventSystem != null)
                _shortcutEventSystem.sendNavigationEvents = _previousNavigationEvents;
            _shortcutEventSystem = null;
        }

        private void CreateCompletionButton(Transform parent)
        {
            _completionButton = UIElementFactory.CreateButton(
                "MarkCardCompletedButton",
                parent);
            RectTransform rect = _completionButton.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(390f, 72f);
            rect.anchoredPosition = new Vector2(-36f, -36f);
            _completionButton.image.color = new Color(0.32f, 0.16f, 0.08f, 0.96f);

            _completionButtonLabel = UIElementFactory.CreateText(
                "Label",
                _completionButton.transform,
                string.Empty,
                26,
                new Color(1f, 0.91f, 0.7f),
                TextAnchor.MiddleCenter);
            UIElementFactory.Stretch(
                _completionButtonLabel.GetComponent<RectTransform>());
            _completionButton.onClick.AddListener(MarkSelectedCardCompleted);
            RefreshCompletionButton(force: true);
        }

        private void MarkSelectedCardCompleted()
        {
            if (_availableCards.Count == 0 || selectedCardIndex < 0)
                return;
            if (IsBattlePresentationActive)
            {
                Debug.Log("[VFXTest] 当前卡牌演出尚未结束，暂不能标记完成。");
                return;
            }

            int index = Mathf.Clamp(
                selectedCardIndex,
                0,
                _availableCards.Count - 1);
            CardConfig completed = _availableCards[index];
            if (CardTestStatus.IsReady(completed.contentId))
            {
                Debug.Log(
                    $"[VFXTest] {completed.cardName} 已经完成验收，" +
                    "无需重复标记。");
                return;
            }

            CardTestStatus.SetCompleted(
                completed.contentId,
                completed.cardName);
            availableCardNames[index] = BuildInspectorCardLabel(completed);
            _tuningPanel?.AdvanceToNextPending(index);
            _completionLabelIndex = -1;
            RefreshCompletionButton(force: true);
            Debug.Log(
                $"[VFXTest] 已确认完成：{completed.cardName} ({completed.contentId})。" +
                "该卡已开放到正式关卡。");
        }

        private void RefreshCompletionButton(bool force = false)
        {
            if (_completionButton == null || _completionButtonLabel == null)
                return;

            if (_availableCards.Count == 0)
            {
                _completionButton.interactable = false;
                _completionButtonLabel.text = "没有已登记卡牌";
                _completionLabelIndex = -1;
                return;
            }
            if (selectedCardIndex < 0)
            {
                _completionButton.interactable = false;
                _completionButtonLabel.text = "没有选中的卡牌";
                _completionLabelIndex = -1;
                return;
            }

            int index = Mathf.Clamp(
                selectedCardIndex,
                0,
                _availableCards.Count - 1);
            if (!force && _completionLabelIndex == index)
                return;

            CardConfig selected = _availableCards[index];
            bool isCompleted = CardTestStatus.IsReady(selected.contentId);
            _completionButton.interactable = !isCompleted;
            _completionButtonLabel.text = isCompleted
                ? $"已完成：{selected.cardName}"
                : $"标记完成：{selected.cardName}";
            _completionLabelIndex = index;
        }

        private void OnTuningPanelSelectionChanged(int index)
        {
            if (_availableCards.Count == 0)
                return;

            if (index < 0)
            {
                selectedCardIndex = -1;
                if (_initialized && !IsTurnPresentationActive)
                    ClearStagedCardFromHand();
                _completionLabelIndex = -1;
                RefreshCompletionButton(force: true);
                return;
            }

            selectedCardIndex = Mathf.Clamp(
                index,
                0,
                _availableCards.Count - 1);
            _completionLabelIndex = -1;
            RefreshCompletionButton(force: true);
            _crawlerResidentTuningPanel?.SetSelectedCard(
                _availableCards[selectedCardIndex]);
            if (_initialized && !IsTurnPresentationActive)
                StageSelectedCardInHand(resetHand: true);
        }

        private void ReplaySelectedCrawlerEntry(string crawlerContentId)
        {
            if (_crawlerResidentPresenter != null &&
                _crawlerResidentPresenter.TryReplayEntry(crawlerContentId))
            {
                Debug.Log(
                    $"[VFXTest][CrawlerTuning] 只重播入场表现：" +
                    crawlerContentId);
                return;
            }

            // 首次预览时权威列表还没有该英灵，正常打出
            // 当前选中的英灵卡；后续重播不会重复召唤。
            PlaySelectedCard();
        }

        private bool ReplaySelectedCrawlerExit(string crawlerContentId)
        {
            bool started = _crawlerResidentPresenter != null &&
                _crawlerResidentPresenter.TryReplayExit(crawlerContentId);
            if (started)
            {
                Debug.Log(
                    $"[VFXTest][CrawlerTuning] 只预览退场表现：" +
                    crawlerContentId);
            }
            else
            {
                Debug.Log(
                    $"[VFXTest][CrawlerTuning] 无法预览退场；" +
                    $"请先让英灵显形：{crawlerContentId}");
            }
            return started;
        }

        private void RefreshCrawlerResidentLayout()
        {
            _crawlerResidentPresenter?.RefreshTunableLayout();
        }

        private bool SetCrawlerResidentAuraPreview(
            string crawlerContentId,
            bool visible)
        {
            return _crawlerResidentPresenter != null &&
                _crawlerResidentPresenter.SetTuningAuraPreview(
                    crawlerContentId,
                    visible);
        }

        private void OnCardPlayResolvedPresentation(
            CombatCardResolvedEvent result)
        {
            if (result?.Card != null && result.PlayOrigin == CardPlayOrigin.CardEffect && result.DestroyedOnUse)
                Destroy(result.Card, 3f);
            if (result?.Card != null &&
                _f7CardAudits.TryGetValue(
                    result.Card.runtimeInstanceId,
                    out F7CardAudit audit))
            {
                Debug.Log(
                    $"[F7][CardAudit] card={audit.CardName} " +
                    $"contentId={audit.ContentId} instance={result.Card.runtimeInstanceId} " +
                    $"expectedDisplayed={audit.ExpectedDisplayedDamage} " +
                    $"actualResultDamage={result.DamageDealt} " +
                    $"damageEvents={audit.EventCount} " +
                    $"eventApplied={audit.AppliedDamage} " +
                    $"directBudget={audit.ExpectedDirectEvents} " +
                    $"bounceBudget={audit.ExpectedBounceEvents} " +
                    $"destroyedOnUse={result.DestroyedOnUse}");
            }
            _battleHud?.PlayStatGains(SupportArtifactFeedbackCoordinator.GetStandardHudGains(result));
            _battleHud?.PlayHealthGain(result?.HealAmount ?? 0, result?.MaxHealthGained ?? 0);
            _battleHud?.PlayManaGain(SupportArtifactFeedbackCoordinator.GetStandardManaGain(result));
        }

        private void BindF7AuditEvents(CombatController combat)
        {
            if (combat == null) return;
            combat.OnCardPlayedVFX += OnF7CardPlayed;
            combat.OnDamageEvent += OnF7DamageEvent;
            combat.OnPeriodicAttackPlanResolved += OnF7PeriodicPlanResolved;
            combat.OnPeriodicAttackChanged += OnF7PeriodicAttackChanged;
            combat.OnColumnReplayAttackResolved += OnF7ColumnReplayResolved;
        }

        private void UnbindF7AuditEvents(CombatController combat)
        {
            if (combat == null) return;
            combat.OnCardPlayedVFX -= OnF7CardPlayed;
            combat.OnDamageEvent -= OnF7DamageEvent;
            combat.OnPeriodicAttackPlanResolved -= OnF7PeriodicPlanResolved;
            combat.OnPeriodicAttackChanged -= OnF7PeriodicAttackChanged;
            combat.OnColumnReplayAttackResolved -= OnF7ColumnReplayResolved;
        }

        private void OnF7CardPlayed(CardConfig card, int targetIndex)
        {
            if (card == null || _combat == null) return;
            AttackPlan plan = _combat.GetAttackPlanPreview(card);
            var audit = new F7CardAudit
            {
                CardName = card.cardName ?? string.Empty,
                ContentId = card.contentId ?? string.Empty,
                ExpectedDisplayedDamage = plan?.DisplayedCardDamage ?? 0,
                ExpectedDirectEvents = plan?.MaximumDirectEventCount ?? 0,
                ExpectedBounceEvents = plan?.MaximumBounceEventCount ?? 0
            };
            _f7CardAudits[card.runtimeInstanceId] = audit;
            Debug.Log(
                $"[F7][CardPlan] card={audit.CardName} " +
                $"contentId={audit.ContentId} instance={card.runtimeInstanceId} " +
                $"target={targetIndex} expectedDisplayed={audit.ExpectedDisplayedDamage} " +
                $"maxDirectEvents={audit.ExpectedDirectEvents} " +
                $"maxBounceEvents={audit.ExpectedBounceEvents}");
        }

        private void OnF7DamageEvent(DamageEvent damageEvent)
        {
            if (damageEvent.SourceCardInstanceId <= 0 ||
                !_f7CardAudits.TryGetValue(
                    damageEvent.SourceCardInstanceId,
                    out F7CardAudit audit))
            {
                return;
            }

            audit.AppliedDamage += damageEvent.AppliedDamage;
            audit.EventCount++;
            EnemyStats enemy = _combat != null &&
                damageEvent.TargetIndex >= 0 &&
                damageEvent.TargetIndex < _combat.Enemies.Count
                ? _combat.Enemies[damageEvent.TargetIndex]
                : null;
            int row = enemy != null ? enemy.row : -1;
            int column = enemy != null ? enemy.column : -1;
            Debug.Log(
                $"[F7][Damage] card={audit.CardName} " +
                $"contentId={audit.ContentId} instance={damageEvent.SourceCardInstanceId} " +
                $"kind={damageEvent.Kind} attack={damageEvent.AttackInstanceIndex} " +
                $"subHit={damageEvent.SubHitIndex} targetIndex={damageEvent.TargetIndex} " +
                $"targetRuntime={damageEvent.TargetEnemyRuntimeId} row={row} col={column} " +
                $"requested={damageEvent.RequestedDamage} applied={damageEvent.AppliedDamage} " +
                $"hp={damageEvent.HpDamage} shield={damageEvent.ShieldDamage}");
        }

        private void OnF7ColumnReplayResolved(ColumnReplayAttackEvent replay)
        {
            if (replay?.Plan == null || replay.Resolution == null) return;
            int direct = 0, splash = 0, directCount = 0;
            foreach (DamageEvent hit in replay.Resolution.Events)
            {
                if (hit.Kind == DamageKind.Direct) { direct += hit.AppliedDamage; directCount++; }
                else if (hit.Kind == DamageKind.Splash) splash += hit.AppliedDamage;
            }
            Debug.Log($"[F7][ColumnReplay] contentId={replay.SourceCardContentId} " +
                $"instance={replay.SourceCardInstanceId} turn={replay.Turn} " +
                $"recordedColumns={replay.Plan.SubHits.Count} " +
                $"nominalDirect={replay.Plan.DisplayedCardDamage} " +
                $"actualDirectEvents={directCount} appliedDirect={direct} appliedSplash={splash}");
        }

        private void OnF7PeriodicPlanResolved(CardConfig card, AttackPlan plan)
        {
            if (card == null || plan == null ||
                (!card.cardName.Contains("飞鸦") && !card.cardName.Contains("蜂")))
            {
                return;
            }
            Debug.Log(
                $"[F7][PeriodicPlan] card={card.cardName} contentId={card.contentId} " +
                $"instance={card.runtimeInstanceId} residentBirds={plan.ResidentBirdCount} " +
                $"initialSubHits={plan.SubHits.Count} " +
                $"initialDisplayed={plan.DisplayedCardDamage}");
        }

        private void OnF7PeriodicAttackChanged(PeriodicAttackEvent change)
        {
            if (change?.Source == null ||
                change.Kind != PeriodicAttackChangeKind.Triggered ||
                change.Resolution == null)
            {
                return;
            }
            PeriodicAttackSource source = change.Source;
            string targetRows = change.Targets == null
                ? string.Empty
                : string.Join(",", change.Targets.Select(target =>
                    $"{target.TargetIndex}:r{target.Row}c{target.Column}"));
            Debug.Log(
                $"[F7][PeriodicAudit] card={source.SourceCardContentId} " +
                $"instance={source.SourceCardInstanceId} kind={source.Kind} " +
                $"birdIndex={source.BirdIndex} birdCount={source.BirdCount} " +
                $"turn={change.Turn} events={change.Resolution.Events.Count} " +
                $"applied={change.Resolution.TotalAppliedDamage} " +
                $"resolved={change.Resolution.TotalResolvedDamage} " +
                $"targets=[{targetRows}]");
        }

        private void OnTestCardHoverEnter(int sortedIndex)
        {
            OnTestCardHoverExit();
            CardConfig card = _testHandViewModel?.GetCardAt(sortedIndex);
            if (card == null || _combat == null)
                return;

            _crawlerResidentFeedbackCoordinator?.ShowCardPreview(card);
            CardImmediateDamageDisplaySummary immediateDamage =
                _combat.GetCardImmediateDamageDisplay(card);
            _battlefield?.EnemyArea?.ShowDamagePreview(immediateDamage.DamagePreviews);
            _testCardTooltip?.ShowText(
                CardTextRenderer.GetTooltipText(card, _combat, immediateDamage));
            _testCardTooltip?.transform.SetAsLastSibling();
            List<BuffPreview> previews = _combat.GetBuffPreview(card);
            if (previews.Count > 0)
                _battleHud?.PreviewBuffs(previews);
        }

        private void OnTestCardHoverExit()
        {
            _crawlerResidentFeedbackCoordinator?.ClearCardPreview();
            _testCardTooltip?.Hide();
            _battlefield?.EnemyArea?.ClearDamagePreview();
            _battleHud?.ClearBuffPreview();
        }

        private void OnCardsDrawnPresentation(
            CombatCardsDrawnEvent result)
        {
            RectTransform source = _battleHud != null &&
                _battleHud.deckCountText != null
                ? _battleHud.deckCountText.rectTransform
                : null;
            _testHandView?.QueueDrawPresentation(
                result.CardInstanceIds,
                source);
        }

        private void OnDestroy()
        {
            RestoreEventSystemNavigation();
            if (_bossMotionAudio != null) _bossMotionAudio.Unbind();
            if (_handDisruptionFeedback != null) _handDisruptionFeedback.Unbind();
            if (_createdCardPresentation != null)
                _createdCardPresentation.Unbind();
            if (_crawlerResidentFeedbackCoordinator != null)
                _crawlerResidentFeedbackCoordinator.Unbind();
            if (_supportArtifactFeedback != null)
                _supportArtifactFeedback.Unbind();
            if (_enemyTurnPresentationCoordinator != null)
                _enemyTurnPresentationCoordinator.Unbind();
            if (_bridge != null)
                _bridge.DeathSubjectVacated -= OnDeathSubjectVacated;
            if (_combat != null)
            {
                _combat.OnCardPlayResolved -= OnCardPlayResolvedPresentation;
                _combat.OnCardsDrawn -= OnCardsDrawnPresentation;
                _combat.OnStateChanged -= OnCombatStateChanged;
                UnbindF7AuditEvents(_combat);
            }
            _testHandViewModel?.Dispose();
            ReleaseAvailableCardPool();
        }

        private static List<CardConfig> CreateScenarioDeck(
            BattleScenarioProfile active,
            int copies)
        {
            List<CardConfig> pool =
                ContentRuntimeFactory.CreateCardPool(active.volumeId);
            var byId = pool.ToDictionary(
                card => card.contentId,
                card => card);
            var deck = new List<CardConfig>();
            int safeCopies = Mathf.Max(1, copies);
            for (int copy = 0; copy < safeCopies; copy++)
            {
                for (int i = 0; i < active.deckContentIds.Count; i++)
                {
                    if (!CardTestStatus.ShouldAppearInTest(
                            active.deckContentIds[i]))
                        continue;
                    if (byId.TryGetValue(
                            active.deckContentIds[i],
                            out CardConfig card))
                        deck.Add(card);
                }
            }
            return deck;
        }

        // ==================== 可测试卡牌自动构建 ====================

        private BattleScenarioProfile ResolveScenario()
        {
            BattleScenarioProfile active = scenario != null
                ? scenario
                : Resources.Load<BattleScenarioProfile>(
                    DefaultScenarioResourcePath);
            if (active == null)
            {
                throw new System.InvalidOperationException(
                    $"F7 缺少共享战斗配置：Resources/{DefaultScenarioResourcePath}.asset。");
            }

            List<string> errors = active.ValidateProfile();
            if (errors.Count > 0)
            {
                throw new System.InvalidOperationException(
                    "F7 战斗场景数据不合法：" +
                    System.Environment.NewLine +
                    string.Join(System.Environment.NewLine, errors));
            }
            return active;
        }

        /// <summary>
        /// 按权威内容目录顺序加载已进入人工验收登记的卡牌。
        /// 未登记的旧卡通常没有完整素材，不进入 F7 可选集；
        /// 已完成卡仍进入完整卡池；启动只交给选择器首个未确认卡的稳定索引。
        /// 可见筛选与行号映射由选择器拥有，不新增 Bootstrap 筛选状态。
        /// </summary>
        private void BuildAvailableCards()
        {
            _availableCards.Clear();
            availableCardNames.Clear();
            _availableCardPool = ContentRuntimeFactory.CreateAllCardPool();
            var runtimeCardsById = _availableCardPool.ToDictionary(
                card => card.contentId,
                card => card,
                System.StringComparer.OrdinalIgnoreCase);

            var seenContentIds = new HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (VolumeContent volume in ContentCatalog.Current.Volumes)
            {
                if (volume?.cards == null)
                    continue;

                foreach (CardData data in volume.cards)
                {
                    if (data == null ||
                        string.IsNullOrWhiteSpace(data.contentId))
                        continue;
                    if (!VfxTestCardSelectionPolicy.ShouldInclude(
                            data.contentId,
                            hideConfirmed: false))
                    {
                        continue;
                    }
                    if (!seenContentIds.Add(data.contentId))
                    {
                        Debug.LogWarning(
                            $"[VFXTest] 卡牌 contentId '{data.contentId}' " +
                            "重复，F7 已登记卡牌列表只保留首个。");
                        continue;
                    }

                    if (!runtimeCardsById.TryGetValue(
                            data.contentId,
                            out CardConfig card))
                    {
                        Debug.LogError(
                            $"[VFXTest] 完整运行时卡池缺少 {data.contentId}。");
                        continue;
                    }
                    _availableCards.Add(card);
                    availableCardNames.Add(
                        BuildInspectorCardLabel(card));
                }
            }

            // A serialized index may point at a card already accepted in an
            // earlier session. Every F7 entry starts at the first pending card
            // in authoritative content order, regardless of that cached index.
            selectedCardIndex = _availableCards.FindIndex(card =>
                VfxTestCardSelectionPolicy.ShouldInclude(card.contentId,
                    VfxTestCardSelectionFilter.Pending));

            _completionLabelIndex = -1;

            Debug.Log(
                $"[VFXTest] 已加载已登记卡牌 {availableCardNames.Count} 张: " +
                string.Join(", ", availableCardNames));
        }

        private void ReleaseAvailableCardPool()
        {
            if (_availableCardPool == null) return;
            for (int i = 0; i < _availableCardPool.Count; i++)
            {
                CardConfig card = _availableCardPool[i];
                if (card == null) continue;
                if (Application.isPlaying)
                    Destroy(card);
                else
                    DestroyImmediate(card);
            }
            _availableCardPool.Clear();
            _availableCards.Clear();
        }

        private static string BuildInspectorCardLabel(CardConfig card)
        {
            string status = CardTestStatus.IsReady(card.contentId)
                ? "已完成"
                : "待验收";
            return $"[{status}] {card.cardName} ({card.contentId})";
        }

        // ==================== 测试动作 ====================

        private void EndTestTurn()
        {
            // 出牌可并发；回合末仍等待初击回执，避免与敌方行动交叉。
            if (IsBattlePresentationActive)
            {
                Debug.Log("[VFXTest] 当前战场演出尚未完成，本次 E 输入已忽略。");
                return;
            }
            if (_enemyTurnPresentationCoordinator == null) return;
            CommandResult result = _enemyTurnPresentationCoordinator.RequestEndTurn();
            if (result.Success)
            {
                Debug.Log("[VFXTest] 已开始回合末结算；飞鸦导弹落地后，怪物依次行动。");
            }
            else
            {
                Debug.LogWarning($"[VFXTest] 当前无法结束回合：{result.Message}");
            }
        }

        private void PlaySelectedCard()
        {
            if (_combat == null || _availableCards.Count == 0 ||
                selectedCardIndex < 0)
            {
                return;
            }
            if (!CanPlayTestCards)
            {
                Debug.Log("[VFXTest] 当前战斗阶段无法出牌，本次 Space 输入已忽略。");
                return;
            }
            int idx = Mathf.Clamp(selectedCardIndex, 0, _availableCards.Count - 1);
            if (!IsStagedCardReady(idx))
                StageSelectedCardInHand();
            CardConfig card = _stagedTestCard;
            if (card == null)
            {
                Debug.LogError("[VFXTest] 选中卡未能显示到手牌，无法测试。");
                return;
            }

            // 生成牌与抽到的牌会留在手中，选中测试牌不一定仍在第0位。
            int sortedIndex = -1;
            for (int i = 0; i < (_testHandViewModel?.SortedHand.Count ?? 0); i++)
            {
                if (ReferenceEquals(_testHandViewModel.GetCardAt(i), card))
                {
                    sortedIndex = i;
                    break;
                }
            }
            if (sortedIndex < 0)
            {
                Debug.LogWarning("[VFXTest] 选中测试牌尚未进入当前手牌展示。");
                return;
            }
            PlayTestCard(card, sortedIndex);
        }

        private void OnTestCardClicked(int sortedIndex)
        {
            if (_testHandView == null ||
                !_testHandView.IsCardReadyForInteraction(sortedIndex)) return;
            CardConfig card = _testHandViewModel?.GetCardAt(sortedIndex);
            if (card != null) PlayTestCard(card, sortedIndex);
        }

        private void PlayTestCard(CardConfig card, int sortedIndex)
        {
            if (card == null || !CanPlayTestCards) return;
            OnTestCardHoverExit();
            CardView cardView = _testHandView?.GetCardView(sortedIndex);
            if (CombatSceneController.TryGetCardScreenOrigin(cardView, out Vector2 screenOrigin))
            {
                Camera camera = Camera.main;
                if (camera != null)
                {
                    float worldZ = _playerAttackOrigin != null ? _playerAttackOrigin.position.z : 0f;
                    Ray ray = camera.ScreenPointToRay(screenOrigin);
                    var battlefieldPlane = new Plane(Vector3.forward, new Vector3(0f, 0f, worldZ));
                    if (battlefieldPlane.Raycast(ray, out float distance))
                    {
                        Vector3 origin = ray.GetPoint(distance);
                        _periodicAttackPresenter?.PrepareCardOrigin(card.runtimeInstanceId, origin);
                        _supportArtifactFeedback?.PrepareCardOrigin(card.runtimeInstanceId, origin);
                    }
                }
            }
            if (card.autoPlayOnDraw)
            {
                // Selection only stages the real instance. The explicit test
                // action now publishes the shared draw receipt and lets Core
                // resolve CardEffect synchronously, with no player payment.
                bool drawn = _combat.ResolveAutomaticDrawForTestHarness(card);
                if (!drawn)
                {
                    _periodicAttackPresenter?.ForgetCardOrigin(card.runtimeInstanceId);
                    _supportArtifactFeedback?.ForgetCardOrigin(card.runtimeInstanceId);
                    Debug.LogWarning("[VFXTest] 当前无法进行自动抽牌测试。");
                }
                else if (ReferenceEquals(_stagedTestCard, card) && !_combat.Hand.Any(held => ReferenceEquals(held, card)))
                    _stagedTestCard = null;
                _testHandViewModel?.RefreshAll();
                // HandDrawPresentation owns the frozen automatic face and its
                // draw/departure. Do not run a second manual play animation.
                return;
            }
            // F7 只校验当前单卡的基础表现。每次测试出牌都从
            // 新链开始，不让上一次测试把抽牌、护甲等效果放大。
            _combat.Combo.ClearCombo();
            if (cardView != null && _testHandView != null)
                _testHandView.DetachCardView(cardView);

            if (!_combat.TryPlayCard(card))
            {
                _periodicAttackPresenter?.ForgetCardOrigin(card.runtimeInstanceId);
                _supportArtifactFeedback?.ForgetCardOrigin(card.runtimeInstanceId);
                Debug.LogWarning("[VFXTest] 当前战斗阶段不允许出牌。");
                if (cardView != null && _testHandView != null)
                    _testHandView.ReattachCardView(cardView, sortedIndex);
                return;
            }
            // F7 isolates each cast; the next hover and click share an empty chain.
            _combat.Combo.ClearCombo();
            _testHandViewModel?.RefreshAll();
            // 点击生成牌时，原选中测试牌可能仍在手中。仅失效实际离手的实例；
            // 保留选择索引，避免Update把出牌补回误判成换选并清空整个手牌。
            if (_stagedTestCard != null && !_combat.Hand.Any(
                    held => ReferenceEquals(held, _stagedTestCard)))
                _stagedTestCard = null;
            if (_battleHud != null)
            {
                _battleHud.SetResources(
                    _combat.Player.currentHealth,
                    _combat.Player.maxHealth,
                    _combat.Mana.CurrentMana + _combat.Mana.TemporaryMana,
                    _combat.Mana.BaseMana);
                _battleHud.SetArmor(_combat.Player.armor);
                _battleHud.RefreshBuffs(
                    _combat.Player,
                    _combat.GetDisplayStatValue);
            }

            // 已打出的牌正常退手；生成牌沿正式回执凝聚入手。不要在出牌
            // 飞行结束时立即复制源牌，重测只由Space或用户换选显式补入。
            System.Action completePlayedCard = () =>
            {
                if (card != null && card.destroyOnUse)
                    Destroy(card, 3f);
            };
            if (cardView != null && _testHandView != null)
            {
                _testHandView.PlayCardAnimation(
                    cardView,
                    card.destroyOnUse,
                    completePlayedCard);
            }
            else
            {
                completePlayedCard();
            }
        }

        private bool IsStagedCardReady(int selectedIndex)
        {
            if (_combat == null || _stagedTestCard == null ||
                _stagedTestCardIndex != selectedIndex)
            {
                return false;
            }
            return _combat.Hand.Any(card =>
                ReferenceEquals(card, _stagedTestCard));
        }

        /// <summary>
        /// F7 选择的是作者定义；手牌展示和正式结算使用 DeckManager 创建的同一
        /// 张运行时实例。这样卡名、描述、费用、插画、灵印与进化字段都经过唯一
        /// 实例工厂复制，不再维护一份容易漏字段的测试代理。
        /// </summary>
        private void StageSelectedCardInHand(bool resetHand = false)
        {
            if (_combat == null || _availableCards.Count == 0 ||
                selectedCardIndex < 0)
            {
                ClearStagedCardFromHand();
                return;
            }

            int index = Mathf.Clamp(
                selectedCardIndex,
                0,
                _availableCards.Count - 1);
            if (IsStagedCardReady(index))
                return;

            // 初入、显式换选或重置才清手；Space显式重测保留真实生成与抽到的牌。
            // 换选可能发生在上一张牌演出中，由旧回调或Space先进入这里；
            // 以已暂存的选择索引判断，确保这些入口也完成一次显式换选清手。
            if (resetHand || index != _stagedTestCardIndex)
                _combat.DiscardHand();
            CardGrantResult grant = _combat.State.Deck
                .GrantToHandOrDrawPile(_availableCards[index]);
            if (!grant.AddedToHand || grant.Card == null)
            {
                _stagedTestCard = null;
                _stagedTestCardIndex = -1;
                _testHandViewModel?.RefreshAll();
                Debug.LogError("[VFXTest] 选中卡未能进入测试手牌。");
                return;
            }

            grant.Card.isFreeToPlay = !grant.Card.autoPlayOnDraw;
            _stagedTestCard = grant.Card;
            _stagedTestCardIndex = index;
            _testHandViewModel?.RefreshAll();
        }

        private void ClearStagedCardFromHand()
        {
            _stagedTestCard = null;
            _stagedTestCardIndex = -1;
            _combat?.DiscardHand();
            _testHandViewModel?.RefreshAll();
        }
    }
}
