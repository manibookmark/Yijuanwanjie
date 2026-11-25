using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// 一次完整地牢 Run 的唯一状态所有者。楼层节点状态仍由
    /// <see cref="LevelRunSession"/> 管理；生命、牌组实例、灵印、等级经验和
    /// Run 内增量只在这里保存，跨战斗与跨楼层复用同一份状态。
    /// 本文件虽超过规模审查阈值，仍保留为单一 Run 聚合根：跨层统计由
    /// DungeonRunStatistics 唯一持有，幂等冻结和结果格式由 DungeonRunConclusion
    /// 隐藏；本类只在战斗与地图提交边界转交不可变数据。
    /// 地图选项、配方和跨级检查点由 DungeonRunInteractionTransaction 拥有；
    /// 本聚合根仅提供内部深拷贝与一次性状态替换，新增交互不得继续在这里解释规则。
    /// 钤印（含消耗式进化）的结果规则属于 Core，存档投影与临时对象生命周期由
    /// DungeonRunSealAssignmentRules 隐藏；后续新增奖励职责应再次审查。
    /// 入口属性、生命与旧档基线迁移由 DungeonRunPlayerState 独占；本聚合根只在
    /// 战斗准备/提交时协调牌组与玩家状态，不解释属性持久化字段或复制临时增益。
    /// G3探索接入：楼层引用/历史提交归DungeonRunFloorProgression，种子/冻结守卫
    /// 归DungeonRunExplorationState，Exit支付回滚归FloorExits。本类仅装配和转交。
    /// 常见阵型的冻结模板归DungeonRunFormationState，几何归Core；本类不选择格位。
    /// 双钥资格/支付账本归DoubleKeyPayments；本类仅装配并负责牌组与保存回滚。
    /// 百术账本由Core RunCardPlayGrowth独占，本类只装配冻结配置并映射存档，
    /// 不解释阈值、不从统计反推、不叠加入场基线。新增独立规则须继续下放owner。
    /// 终局层奖与整关奖沿既有胜利事务分别保存，失败时一起回滚；不在Bootstrap发放。
    /// 已撤销的重复卡ID只在存档恢复时归一到现有定义；牌组状态仍由本类唯一持有。
    /// 行为事实由Core RunCardUnlockFacts独占；本类仅映射已验证快照与独立准备副本，
    /// 不记录事件、不解释条件、不从统计推断旧事实，仍保持同一Run聚合边界。
    /// 单次凝神储备由Core CrawlerDurationReserve独占；这里只映射与回滚快照，
    /// 不解释续时或召唤消费，仍只装配既有Run聚合。
    /// 额外属性和还魂分别归Core AdditionalStats/RevivalReserve唯一持有；
    /// 这里仅保存、复制、恢复其不可变快照，closed wire与旧档准入归独立codec。
    /// CardStat、Duplicate与Hire的独立journal及规则分别归各自owner；此聚合只装配、保存和门禁。
    /// 领卷英灵只在新Run构造时冻结；这里保存入口身份而不解释解锁阈值。
    /// 中途召唤名单、Hub选择和旧档均不得用来反推入口身份，保持Run聚合边界。
    /// 胜利灵印归VictorySealRewards，归元及钱包恢复归CardConversions；此聚合只映射和恢复已有状态。
    /// 局内天机的active/候选/回执由Arcanas唯一持有；本类只装配、序列化和跨owner门禁。
    /// 旧入口账号装备与百术开关不在此迁移；新增奖励不得继续在聚合根解释选择规则。
    /// 单张供奉freeze/销毁/取消/恢复proof归同一CardOfferings（历史ChickenOfferings别名）；本类只协调既有同owner快照和投影。
    /// 完整冻结目录与字节格式属于LevelRunNodeDirectoryCheckpoint/LevelRunSaveCodec；
    /// 灵印宝箱完整候选/选择/回执唯一归GemChests；这里只装配、序列化和协调门禁。
    /// 物理牌的格式、完整作者文档与旧档谱系迁移归DungeonRunCardCodec；本类只持有唯一牌组并提交。
    /// 此聚合只组装已恢复的当前/历史LevelRunSession并转交给类型化owner，
    /// 不反射节点、不重新选择来源、不把完整目录当作来源资格或放置许可。
    /// 试玩准入仅在创建/恢复边界组合Core发布范围，不新增可变状态或复制内容白名单。
    /// 献牌属性的有序预检/应用回执归StatApplications，既有属性owner仍唯一持有值；
    /// 本类仅装配。支付、节点完成与持久化journal不得在聚合根继续增加独立规则。
    /// 免费请灵的候选、授权和起手保证归Invitations；这里只装配journal并汇总既有起手投影。
    /// 新Run易卷额度仅作为构造输入交给既有SharedState；此聚合不解释账号购买，
    /// 不新增计数owner或可重复补发命令，恢复与跨层仍复用同一已消费余额。
    /// </summary>
    public sealed class DungeonRunSession : IRewardRerollBank
    {
        private const int PayloadSchemaVersion = 41;

        private readonly DungeonRunSharedState _shared;
        private readonly LevelRunProfile _firstFloorProfile;
        private readonly DungeonRunFloorProgression _floors;
        private List<DungeonRunCardSaveData> _cards;
        // Borrowed combat author bindings; physical persistence belongs only to CardCodec.
        private ICardAuthorReferenceResolver _combatAuthorReferences;
        private List<DungeonRunCrawlerResidenceSaveData> _crawlerResidents;
        private readonly DungeonRunPlayerState _playerState;
        private int _pendingForcedOpeningHandCardInstanceId;
        private readonly IInstanceIdGenerator _instanceIds;
        private readonly Dictionary<string, DungeonRunSealAssignmentSaveData>
            _sealAssignments;
        private DungeonRunConclusion _conclusion;
        private readonly DungeonRunStatistics _statistics;
        public DungeonRunExperienceCollections ExperienceCollections { get; }
        private readonly List<string> _usedCrawlerContentIds;
        private readonly int _elapsedRunSecondsBeforeResume;
        private readonly long _activeTimerStartedTimestamp;
        private bool _dungeonBonusGranted;
        private int _dungeonBonusJade;
        private bool _levelBonusGranted;
        private int _levelBonusJade;
        // Content/Bootstrap owns account gating and supplies immutable pool
        // snapshots.  The Run only uses this list to freeze a Finale offer;
        // it never queries Meta or mutates the authoring pool.
        private IReadOnlyList<DungeonRunSealRewardPool>
            _victorySealRewardPools = Array.Empty<DungeonRunSealRewardPool>();
        // Account authority is projected live by the composition root; never saved or inferred from seal IDs.
        private Func<bool> _canReceiveSealRewards;
        private DungeonRunSealRewardPolicy _sealRewardPolicy;
        public bool CanReceiveSealRewards => _canReceiveSealRewards?.Invoke() == true;
        public void ConfigureSealRewardEligibility(Func<bool> canReceive)
            => _canReceiveSealRewards = canReceive ?? throw new ArgumentNullException(nameof(canReceive));
        public bool TryCashOutSealReward(string nodeId, LevelInteractionEffect effect, int baseAmount,
            IDungeonRunInteractionWallet wallet, out LevelInteractionReceipt receipt, out string error, Action persist = null)
            => (_sealRewardPolicy ??= new DungeonRunSealRewardPolicy(this)).TryCashOut(
                nodeId, effect, baseAmount, wallet, out receipt, out error, persist);
        internal bool TryRecoverSealRewardWallet(out string error)
        { error = null; return _sealRewardPolicy == null || _sealRewardPolicy.TryRecoverWallet(out error); }

        private DungeonRunSession(
            LevelRunProfile firstFloorProfile,
            LevelRunSession currentFloor,
            DungeonRunSharedState shared,
            List<LevelRunSaveData> completedFloorStates,
            List<DungeonRunCardSaveData> cards,
            List<DungeonRunCrawlerResidenceSaveData> crawlerResidents,
            DungeonRunPlayerState playerState,
            int pendingForcedOpeningHandCardInstanceId = 0,
            IReadOnlyList<DungeonRunSealAssignmentSaveData>
                sealAssignments = null,
            IInstanceIdGenerator instanceIds = null,
            DungeonRunConclusion conclusion = null,
            DungeonRunStatistics statistics = null,
            IReadOnlyList<string> usedCrawlerContentIds = null,
            int elapsedRunSeconds = 0,
            bool dungeonBonusGranted = false,
            int dungeonBonusJade = 0,
            DungeonRunInteractionSaveData interaction = null,
            DungeonRunExplorationState exploration = null,
            DungeonRunFormationState formations = null,
            DungeonRunDoubleKeyPaymentSaveData doubleKeyPayments = null,
            bool levelBonusGranted = false, int levelBonusJade = 0,
            string leaderCrawlerContentId = null, DungeonRunArcanaSaveData arcana = null,
            DungeonRunChickenOfferingSaveData chickenOffering = null,
            DungeonRunManaStatueSaveData manaStatues = null,
            DungeonRunCartSaveData abandonedCarts = null, DungeonRunSideEventState sideEvents = null,
            IDungeonRunSideEventContext sideEventContext = null, DungeonRunCrystalSaveData crystalEvents = null,
            DungeonRunExperienceCollections experienceCollections = null, string gemChestStateJson = null,
            int gemChestJournalVersion = 3, string cardStatOfferingStateJson = null,
            string duplicateOfferingStateJson = null, string gunForHireStateJson = null, string invitationStateJson = null,
            RunManaRepairPickupSaveData manaRepairPickups = null)
        {
            _firstFloorProfile = firstFloorProfile ??
                throw new ArgumentNullException(nameof(firstFloorProfile));
            LeaderCrawlerContentId = ValidateLeaderCrawlerContentId(leaderCrawlerContentId);
            _shared = shared ?? throw new ArgumentNullException(nameof(shared));
            _floors = new DungeonRunFloorProgression(currentFloor, shared,
                completedFloorStates, exploration, formations);
            _cards = cards ?? new List<DungeonRunCardSaveData>();
            _crawlerResidents = crawlerResidents ??
                new List<DungeonRunCrawlerResidenceSaveData>();
            _playerState = playerState ?? throw new ArgumentNullException(nameof(playerState));
            StatApplications = new DungeonRunStatApplications(_shared, _playerState);
            if (pendingForcedOpeningHandCardInstanceId < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(pendingForcedOpeningHandCardInstanceId),
                    "强制起手牌实例 ID 不能为负数。");
            _pendingForcedOpeningHandCardInstanceId =
                pendingForcedOpeningHandCardInstanceId;
            _sealAssignments = BuildSealAssignments(sealAssignments);
            _instanceIds = instanceIds ?? DefaultInstanceIdGenerator.Instance;
            _conclusion = conclusion ?? new DungeonRunConclusion();
            _statistics = statistics ?? new DungeonRunStatistics();
            ExperienceCollections = experienceCollections ?? new DungeonRunExperienceCollections();
            _usedCrawlerContentIds = CopyStableIds(usedCrawlerContentIds);
            _elapsedRunSecondsBeforeResume = Math.Max(0, elapsedRunSeconds);
            _activeTimerStartedTimestamp = Stopwatch.GetTimestamp();
            if (dungeonBonusJade < 0 ||
                (!dungeonBonusGranted && dungeonBonusJade != 0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dungeonBonusJade),
                    "未发放的 Dungeon Bonus 不能携带灵玉金额。");
            }
            _dungeonBonusGranted = dungeonBonusGranted;
            _dungeonBonusJade = dungeonBonusJade;
            if (levelBonusJade < 0 || (!levelBonusGranted && levelBonusJade != 0))
                throw new ArgumentOutOfRangeException(nameof(levelBonusJade), "未发放的终局层奖不能携带金额。");
            _levelBonusGranted = levelBonusGranted;
            _levelBonusJade = levelBonusJade;
            Crystals = new DungeonRunCrystalEvents(this, crystalEvents);
            DoubleKeyPayments = new DungeonRunDoubleKeyPayment(this, doubleKeyPayments);
            Interactions = new DungeonRunInteractionTransaction(this, interaction);
            GemChests = new DungeonRunGemChests(this, gemChestStateJson, gemChestJournalVersion);
            FloorExits = new DungeonRunFloorExitTransaction(this, _floors);
            VictorySealRewards = new DungeonRunVictorySealRewardTransaction(this);
            CardConversions = new RunCardConversionTransaction(this, manaRepairPickups);
            Arcanas = new DungeonRunArcanas(this, arcana);
            ChickenOfferings = new DungeonRunChickenOffering(this, chickenOffering);
            ManaStatues = new DungeonRunManaStatues(this, manaStatues);
            AbandonedCarts = new DungeonRunAbandonedCarts(this, abandonedCarts);
            SideEvents = sideEvents?.Rebind(this) ?? new DungeonRunSideEventState(this);
            CardStatOfferings = new DungeonRunCardStatOfferings(this, cardStatOfferingStateJson);
            DuplicateOfferings = new DungeonRunDuplicateOfferings(this, duplicateOfferingStateJson);
            GunForHire = new DungeonRunGunForHire(this, gunForHireStateJson);
            Invitations = new DungeonRunInvitations(this, invitationStateJson);
            _floors.BindSources(SideEvents, sideEventContext, () => Arcanas.Active);
            Crystals.RequireRewardCheckpoint();
        }

        public string RunId => CurrentFloor.RunId;
        internal DungeonRunStatApplications StatApplications { get; }
        internal DungeonRunPlayerBaselineSaveData CaptureCardStatEntryBaseline() => _playerState.CaptureBaseline();
        internal DungeonRunStatApplicationState CaptureCardStatApplicationState() => DungeonRunStatApplicationState.Capture(_shared);
        public string LeaderCrawlerContentId { get; }
        public string DungeonContentId =>
            string.IsNullOrWhiteSpace(_firstFloorProfile.dungeonContentId)
                ? _firstFloorProfile.contentId
                : _firstFloorProfile.dungeonContentId;
        public string FirstFloorContentId => _firstFloorProfile.contentId;
        public int DungeonBonusBaseJade =>
            Math.Max(0, _firstFloorProfile.dungeonBonusJade);
        public bool IsDungeonBonusGranted => _dungeonBonusGranted;
        public int DungeonBonusJade => _dungeonBonusJade;
        public int LevelBonusBaseJade => Math.Max(0, CurrentFloor.Profile.levelBonusBaseJade);
        public bool IsLevelBonusGranted => _levelBonusGranted;
        public int LevelBonusJade => _levelBonusJade;
        public int ElapsedRunSeconds
        {
            get
            {
                double activeSeconds =
                    (Stopwatch.GetTimestamp() - _activeTimerStartedTimestamp) /
                    (double)Stopwatch.Frequency;
                double total = _elapsedRunSecondsBeforeResume +
                               Math.Max(0d, activeSeconds);
                return (int)Math.Min(int.MaxValue, Math.Floor(total));
            }
        }
        public string CurrentFloorContentId => CurrentFloor.Profile.contentId;
        public LevelRunSession CurrentFloor => _floors.Current;
        public int RunSeed => _floors.Seed;
        /// <summary>Reads this Run's frozen template; Core owns its geometry. Null preserves authored layouts.</summary>
        public string FrozenEncounterFingerprint => _floors.FrozenEncounterFingerprint;
        public DungeonRunSideEventState SideEvents { get; }
        internal IDungeonRunSideEventContext SideEventContext => _floors.SideEventContext;
        /// <summary>Rebinds only the live qualification dependency after loading the account;
        /// captured source proof is never recaptured or rewritten.</summary>
        public void BindSideEventContext(IDungeonRunSideEventContext context)
        {
            if (!SideEvents.HasSourcePlan || context == null || IsRewardCommitInProgress)
                throw new InvalidOperationException("An idle source Run requires its actual admitted context.");
            _floors.BindSources(SideEvents, context, () => Arcanas.Active);
        }
        internal void CommitSideEventPhase(LevelRunSession prepared, DungeonRunSideEventState sources, Action persist)
            => _floors.CommitSourcePhase(prepared, sources, persist);
        public IReadOnlyList<string> CaptureCompletedFloorContentIds()
            => _floors.CaptureHistory().ConvertAll(item => item.profileContentId).AsReadOnly();
        // Read-only projection for the conversion owner's legacy reward recovery.
        // History remains owned by FloorProgression; no new mutable state lives here.
        internal bool TryReadCompletedInteractionReceipt(string floorId, string nodeId,
            string effectId, out LevelInteractionReceipt receipt)
            => _floors.TryReadCompletedInteractionReceipt(floorId, nodeId, effectId, out receipt);
        internal Func<string, CommonEncounterSourceContract> CommonEncounterSources => _floors.CommonEncounterSources;
        public DungeonRunEncounterSnapshot ResolveEnemyRoster(string scenarioId, string nodeId = null)
            => _floors.ResolveRoster(scenarioId, nodeId);
        public EnemyFormationPlan ResolveEnemyFormation(string scenarioId, IReadOnlyList<EnemyStats> enemies, string nodeId = null)
            => _floors.ResolveFormation(scenarioId, enemies, nodeId);
        /// <summary>Creates actors and their frozen formation as one synchronous,
        /// fully validated read. The callback must not change the floor or launch;
        /// content validation is repeated on the next independent call.</summary>
        public EnemyFormationPlan MaterializeEnemyEncounter(string scenarioId, string nodeId,
            Func<DungeonRunEncounterSnapshot, List<EnemyStats>> materializeEnemies, out List<EnemyStats> enemies)
            => _floors.MaterializeEnemyEncounter(scenarioId, nodeId, materializeEnemies, out enemies);
        /// <summary>Shared map/headless Exit settlement; no consumer may award and advance separately.</summary>
        public DungeonRunFloorExitTransaction FloorExits { get; }
        public CombatProgressionState CombatProgression =>
            _shared.CombatProgression;
        public RunProgressionState RunProgression => _shared.RunProgression;
        public bool HasPlayerState => _playerState.HasPlayerState;
        public DungeonRunManaStatues ManaStatues { get; }
        public DungeonRunAbandonedCarts AbandonedCarts { get; }
        public bool HasManaState => _playerState.HasManaState;
        public int MapCurrentMana => _playerState.MapCurrentMana;
        public DungeonRunManaStateOrigin ManaStateOrigin => _playerState.ManaOrigin;
        public int CurrentHealth => _playerState.CurrentHealth;
        public int MaxHealth => _playerState.MaxHealth;
        public DungeonRunPlayerBaselineOrigin PlayerBaselineOrigin => _playerState.Origin;
        public int RunCardCount => _cards.Count;
        public int BaseManaBonus => _shared.BaseManaBonus;
        public int BaseArmorPerTurnBonus => _shared.BaseArmorPerTurnBonus;
        public int MightBonus => _shared.MightBonus;
        public int HandLimitBonus => _shared.HandLimitBonus;
        public int AreaBonus => _shared.AreaBonus;
        public int RewardRerollsRemaining =>
            _shared.RewardRerollsRemaining;

        /// <summary>
        /// Installs the account-resolved seal pools used by random Finale
        /// rewards.  Pool candidates are deep-copied so a catalog refresh or
        /// editor asset reload cannot change an already running Run.
        /// </summary>
        public void ConfigureVictorySealRewardPools(
            IEnumerable<DungeonRunSealRewardPool> pools)
        {
            var copied = new List<DungeonRunSealRewardPool>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DungeonRunSealRewardPool pool in
                     pools ?? Array.Empty<DungeonRunSealRewardPool>())
            {
                if (pool == null || string.IsNullOrWhiteSpace(pool.ContentId))
                    continue;
                if (!ids.Add(pool.ContentId))
                    throw new InvalidOperationException(
                        "终局灵印池 ID 重复：" + pool.ContentId);
                var candidates = new List<CardSealSpec>();
                foreach (CardSealSpec candidate in
                         pool.Candidates ?? Array.Empty<CardSealSpec>())
                    if (candidate != null)
                        candidates.Add(candidate.Clone());
                copied.Add(new DungeonRunSealRewardPool
                {
                    ContentId = pool.ContentId,
                    Candidates = candidates.AsReadOnly(),
                    PreferredCandidateCount = pool.PreferredCandidateCount,
                    PreferredCandidateContentIds = new List<string>(
                        pool.PreferredCandidateContentIds ??
                        Array.Empty<string>()).AsReadOnly(),
                    IsEvolutionResultEligible = pool.IsEvolutionResultEligible
                });
            }
            _victorySealRewardPools = copied.AsReadOnly();
        }

        public bool TryConsumeRewardReroll() =>
            !IsRewardCommitInProgress && !IsSpecializedInteractionBusy && _shared.TryConsumeRewardReroll();
        /// <summary>地图提交与可恢复选择进度的唯一入口；不向调用方开放 Run 集合。</summary>
        public DungeonRunCrystalEvents Crystals { get; }
        public DungeonRunInteractionTransaction Interactions { get; }
        /// <summary>Only owner of frozen Gem offer, selection and durable completion.</summary>
        public DungeonRunGemChests GemChests { get; }
        /// <summary>Two-card payment boundary; paid rewards continue through Interactions.</summary>
        public DungeonRunDoubleKeyPayment DoubleKeyPayments { get; }
        /// <summary>Victory reward checkpoints and durable assignment/cash-out commit owner.</summary>
        public DungeonRunVictorySealRewardTransaction VictorySealRewards { get; }
        /// <summary>Exact-card conversion, durable cash-out and wallet recovery owner.</summary>
        public RunCardConversionTransaction CardConversions { get; }

        /// <summary>Owns only in-run Arcana acquisition, frozen offers and durable receipts.</summary>
        public DungeonRunArcanas Arcanas { get; }

        /// <summary>Owns frozen exact-card Offering choices and their durable completion.</summary>
        public DungeonRunChickenOffering ChickenOfferings { get; }
        /// <summary>Sole owner of CardStat selection, exact payment and durable completion.</summary>
        public DungeonRunCardStatOfferings CardStatOfferings { get; }
        public DungeonRunDuplicateOfferings DuplicateOfferings { get; }
        public DungeonRunGunForHire GunForHire { get; }
        public DungeonRunInvitations Invitations { get; }
        internal bool IsSpecializedInteractionBusy =>
            (_sealRewardPolicy?.IsCommitting ?? false) ||
            Invitations != null && (Invitations.HasPendingChoice || Invitations.IsCommitting) ||
            CardStatOfferings != null && (CardStatOfferings.HasPendingChoice || CardStatOfferings.IsCommitting) ||
            DuplicateOfferings != null && (DuplicateOfferings.HasPendingChoice || DuplicateOfferings.IsCommitting) ||
            GunForHire != null && (GunForHire.HasPendingChoice || GunForHire.IsCommitting);
        /// <summary>The historical ChickenOfferings name is an alias of this same typed one-card owner.</summary>
        public DungeonRunChickenOffering CardOfferings => ChickenOfferings;

        /// <summary>Ordinary checkpoints must not publish staged or unrecovered reward state.</summary>
        public bool IsRewardCommitInProgress => (_sealRewardPolicy?.IsCommitting ?? false) || (Invitations?.IsCommitting ?? false) || (DuplicateOfferings?.IsCommitting ?? false) ||
            (GunForHire?.IsCommitting ?? false) || (CardStatOfferings?.IsCommitting ?? false) || Interactions.IsCommitting || GemChests.IsCommitting ||
            DoubleKeyPayments.IsCommitting || VictorySealRewards.IsCommitting ||
            FloorExits.IsCommitting || CardConversions.IsCommitting || Arcanas.IsCommitting || ChickenOfferings.IsCommitting || ManaStatues.IsCommitting || AbandonedCarts.IsCommitting || SideEvents.IsCommitting || Crystals.IsCommitting;

        /// <summary>只读 Run 准入；调用方必须在预载、转场或楼层变更之前检查。</summary>
        public DungeonRunNodeAdmission GetNodeAdmission(string nodeContentId)
        {
            if (CurrentFloor.FindNode(nodeContentId) == null)
                return DungeonRunNodeAdmission.UnknownNode;
            if (IsRewardCommitInProgress)
                return DungeonRunNodeAdmission.PendingChoiceOutstanding;
            if (Crystals.HasPendingEvent)
                return string.Equals(Crystals.PendingNodeContentId, nodeContentId, StringComparison.Ordinal)
                    ? DungeonRunNodeAdmission.ResumePendingChoice : DungeonRunNodeAdmission.PendingChoiceOutstanding;
            if (Invitations.HasPendingChoice)
                return string.Equals(Invitations.PendingNodeContentId, nodeContentId, StringComparison.Ordinal)
                    ? DungeonRunNodeAdmission.ResumePendingChoice : DungeonRunNodeAdmission.PendingChoiceOutstanding;
            if (DuplicateOfferings.HasPendingChoice)
                return string.Equals(DuplicateOfferings.PendingNodeContentId, nodeContentId, StringComparison.Ordinal)
                    ? DungeonRunNodeAdmission.ResumePendingChoice : DungeonRunNodeAdmission.PendingChoiceOutstanding;
            if (GunForHire.HasPendingChoice)
                return string.Equals(GunForHire.PendingNodeContentId, nodeContentId, StringComparison.Ordinal)
                    ? DungeonRunNodeAdmission.ResumePendingChoice : DungeonRunNodeAdmission.PendingChoiceOutstanding;
            if (CardStatOfferings.HasPendingChoice)
                return string.Equals(CardStatOfferings.PendingNodeContentId, nodeContentId, StringComparison.Ordinal)
                    ? DungeonRunNodeAdmission.ResumePendingChoice : DungeonRunNodeAdmission.PendingChoiceOutstanding;
            if (ChickenOfferings.HasPendingChoice)
                return string.Equals(ChickenOfferings.PendingNodeContentId, nodeContentId, StringComparison.Ordinal)
                    ? DungeonRunNodeAdmission.ResumePendingChoice
                    : DungeonRunNodeAdmission.PendingChoiceOutstanding;
            if (Arcanas.HasPendingChoice)
                return string.Equals(Arcanas.PendingNodeContentId, nodeContentId, StringComparison.Ordinal)
                    ? DungeonRunNodeAdmission.ResumePendingChoice
                    : DungeonRunNodeAdmission.PendingChoiceOutstanding;
            if (CardConversions.HasPendingManaPickup(CurrentFloorContentId, nodeContentId) &&
                CurrentFloor.GetNodeState(nodeContentId) == LevelNodeViewState.Completing)
                return DungeonRunNodeAdmission.ResumePendingChoice;
            if (!Interactions.HasPendingChoice)
                return SideEvents.TryGetDuePhase(out _) && !TryGetConclusion(out _)
                    ? DungeonRunNodeAdmission.SourcePhaseOutstanding : DungeonRunNodeAdmission.Allowed;
            return string.Equals(Interactions.PendingNodeContentId, nodeContentId, StringComparison.Ordinal)
                ? DungeonRunNodeAdmission.ResumePendingChoice
                : DungeonRunNodeAdmission.PendingChoiceOutstanding;
        }

        /// <summary>
        /// 将已建立的入口基线与当前生命投影给地图等消费者；未播种/旧档缺失时
        /// 返回 false 且不改动参数。额外属性与还魂由各自 Run owner 投影一次；
        /// 其余既有楼层 modifier、天运/敛玉/明悟仍由原消费者叠加。
        /// </summary>
        public bool TryProjectPlayerBaseline(PlayerStats player) => _playerState.TryProject(player, RunProgression);
        /// <summary>Base is a read-only projection of the immutable entry and existing Run bonuses, never a combat snapshot.</summary>
        public bool TryGetMapStartingMana(out int value)
        {
            var projection = new PlayerStats(); value = 0;
            if (!_playerState.TryProject(projection)) return false;
            int baselineAndBonus = (int)Math.Max(1L,Math.Min(int.MaxValue,(long)projection.faLi + _shared.BaseManaBonus));
            value = RunProgression.CardPlayGrowth.ProjectBaseMana(baselineAndBonus); return true;
        }
        public IReadOnlyList<CrawlerResidenceSnapshot> CrawlerResidents =>
            CreateCrawlerResidenceSnapshots(_crawlerResidents);
        public IReadOnlyList<string> UsedCrawlerContentIds =>
            _usedCrawlerContentIds.AsReadOnly();
        public int PendingForcedOpeningHandCardInstanceId =>
            _pendingForcedOpeningHandCardInstanceId;
        /// <summary>All explicit guarantees, preserving the existing refinement slot and each newly invited summon.</summary>
        public IReadOnlyList<int> PendingForcedOpeningHandCardInstanceIds
        {
            get
            {
                var ids = new List<int>();
                if (_pendingForcedOpeningHandCardInstanceId > 0) ids.Add(_pendingForcedOpeningHandCardInstanceId);
                foreach (int id in Invitations.PendingOpeningHandCardInstanceIds) if (!ids.Contains(id)) ids.Add(id);
                return ids.AsReadOnly();
            }
        }

        /// <summary>读取已经冻结的整局结果；进行中的 Run 返回 false。</summary>
        public bool TryGetConclusion(out DungeonRunResultSnapshot snapshot)
            => _conclusion.TryGetSnapshot(out snapshot);

        /// <summary>
        /// 冻结终层胜利结果。重复调用幂等返回同一快照；普通遭遇胜利不能终结 Run。
        /// </summary>
        public bool TryConcludeVictory(
            IReadOnlyList<LevelRewardReceipt> rewards,
            out DungeonRunResultSnapshot snapshot,
            out string error)
            => TryConcludeVictoryResults(
                ConvertRewardReceipts(rewards),
                out snapshot,
                out error);

        /// <summary>
        /// 使用已经按总体解锁规则解析的类型化条目冻结终层胜利结果。
        /// </summary>
        public bool TryConcludeVictoryResults(
            IReadOnlyList<DungeonRunRewardResult> rewards,
            out DungeonRunResultSnapshot snapshot,
            out string error)
        {
            if (GemChests.HasPendingChoice || VictorySealRewards.IsCommitting || FloorExits.IsCommitting ||
                Interactions.IsCommitting || GemChests.IsCommitting || DoubleKeyPayments.IsCommitting || CardConversions.IsCommitting || Arcanas.IsCommitting || ChickenOfferings.IsCommitting || ManaStatues.IsCommitting || AbandonedCarts.IsCommitting || SideEvents.IsCommitting || Crystals.IsCommitting || (ChickenOfferings.HasPendingChoice || IsSpecializedInteractionBusy) || Crystals.HasPendingEvent)
            { snapshot = null; error = "Run奖励事务正在提交，不能同时冻结终局。"; return false; }
            if (!SideEvents.TryValidateVictoryReadiness(out error))
            { snapshot = null; return false; }
            if (_conclusion.TryGetSnapshot(out snapshot))
            {
                error = snapshot.Status == DungeonRunResultStatus.Victory ? null : "Run 已经以战败结束。";
                return error == null;
            }
            if (!HasCompletedFinale())
            {
                snapshot = null;
                error = "当前 Run 尚未完成终局节点。";
                return false;
            }
            if (DungeonBonusBaseJade > 0 && !_dungeonBonusGranted)
            {
                snapshot = null;
                error = "Dungeon 通关奖励尚未完成独立结算。";
                return false;
            }
            if (LevelBonusBaseJade > 0 && !_levelBonusGranted)
            {
                snapshot = null;
                error = "终局楼层奖励尚未完成独立结算。";
                return false;
            }
            return TryFreezeConclusion(
                DungeonRunResultStatus.Victory,
                rewards,
                out snapshot,
                out error);
        }

        /// <summary>
        /// Commits the existing Bonus and conclusion invariants together. Scale
        /// review: this adds no policy or independent state; this owner must undo
        /// its Bonus flags, conclusion and return acknowledgement when freezing
        /// or persistence fails. The caller owns its external account rollback.
        /// </summary>
        public bool TryConcludeVictoryWithBonus(
            Func<int, JadeGainResult> grant,
            IReadOnlyList<DungeonRunRewardResult> rewards,
            out DungeonRunResultSnapshot snapshot,
            out string error,
            Action persist = null)
        {
            snapshot = null;
            error = null;
            if (GemChests.HasPendingChoice || VictorySealRewards.IsCommitting || FloorExits.IsCommitting ||
                Interactions.IsCommitting || GemChests.IsCommitting || DoubleKeyPayments.IsCommitting || CardConversions.IsCommitting || Arcanas.IsCommitting || ChickenOfferings.IsCommitting || ManaStatues.IsCommitting || AbandonedCarts.IsCommitting || SideEvents.IsCommitting || Crystals.IsCommitting || (ChickenOfferings.HasPendingChoice || IsSpecializedInteractionBusy) || Crystals.HasPendingEvent)
            { error = "Run奖励事务正在提交，不能同时冻结终局。"; return false; }
            if (!SideEvents.TryValidateVictoryReadiness(out error)) return false;
            bool wasGranted = _dungeonBonusGranted;
            int previousJade = _dungeonBonusJade;
            bool wasLevelGranted = _levelBonusGranted;
            int previousLevelJade = _levelBonusJade;
            DungeonRunConclusion previousConclusion = _conclusion;
            bool committed = false;
            snapshot = null;
            error = null;
            try
            {
                bool hasReturn = CurrentFloor.TryGetPendingReturn(out LevelEncounterReturn pending);
                if (hasReturn && (pending.Outcome != CombatOutcome.Victory ||
                    CurrentFloor.FindNode(pending.NodeContentId)?.nodeType != LevelNodeType.Finale))
                { error = "Only the completed Finale return can commit a victory conclusion."; return false; }
                if (!_conclusion.TryGetSnapshot(out DungeonRunResultSnapshot prior))
                {
                    _conclusion = new DungeonRunConclusion();
                    if (!TryGrantLevelBonus(grant, out _, out _, out error)) return false;
                    if (!TryGrantDungeonBonus(grant, out _, out _, out error)) return false;
                }
                else if (prior.Status != DungeonRunResultStatus.Victory)
                { error = "Run 已经以战败结束。"; return false; }
                if (!TryConcludeVictoryResults(rewards, out snapshot, out error)) return false;
                if (hasReturn)
                {
                    if (!CurrentFloor.TryCommitReturnAcknowledgement(pending.NodeContentId, persist))
                    { error = "The completed Finale return could not be acknowledged."; return false; }
                }
                else persist?.Invoke();
                committed = true;
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally
            {
                if (!committed)
                {
                    _dungeonBonusGranted = wasGranted;
                    _dungeonBonusJade = previousJade;
                    _levelBonusGranted = wasLevelGranted;
                    _levelBonusJade = previousLevelJade;
                    _conclusion = previousConclusion;
                    snapshot = null;
                }
            }
        }

        /// <summary>
        /// 记录本 Run 实际准备过的英灵。结果页冻结该列表，不从结束时仍在场的
        /// 英灵或全局当前选择反推队伍。
        /// </summary>
        public void RegisterPreparedCrawlers(IReadOnlyList<string> contentIds)
        {
            if (IsRewardCommitInProgress)
                throw new InvalidOperationException("Run reward commit cannot register prepared crawlers.");
            if (contentIds == null)
                return;
            for (int i = 0; i < contentIds.Count; i++)
            {
                string id = contentIds[i]?.Trim();
                if (string.IsNullOrWhiteSpace(id) ||
                    ContainsStableId(_usedCrawlerContentIds, id))
                {
                    continue;
                }
                _usedCrawlerContentIds.Add(id);
            }
        }

        /// <summary>
        /// 按首层 Dungeon 根配置幂等发放通关 Bonus。回执必须来自
        /// StageClear、固定金额且不受敛玉影响；失败 Run 不允许调用。
        /// </summary>
        public bool TryGrantDungeonBonus(
            Func<int, JadeGainResult> grant,
            out JadeGainResult receipt,
            out bool newlyGranted,
            out string error)
        {
            if (GemChests.HasPendingChoice || VictorySealRewards.IsCommitting || FloorExits.IsCommitting ||
                Interactions.IsCommitting || GemChests.IsCommitting || DoubleKeyPayments.IsCommitting || CardConversions.IsCommitting || Arcanas.IsCommitting || ChickenOfferings.IsCommitting || ManaStatues.IsCommitting || AbandonedCarts.IsCommitting || SideEvents.IsCommitting || Crystals.IsCommitting || (ChickenOfferings.HasPendingChoice || IsSpecializedInteractionBusy) || Crystals.HasPendingEvent)
            { receipt = default; newlyGranted = false; error = "Run奖励事务正在提交，不能同时发放终局收益。"; return false; }
            if (!SideEvents.TryValidateVictoryReadiness(out error))
            { receipt = default; newlyGranted = false; return false; }
            int configured = DungeonBonusBaseJade;
            if (_dungeonBonusGranted)
            {
                receipt = new JadeGainResult(
                    JadeSource.StageClear,
                    _dungeonBonusJade,
                    _dungeonBonusJade,
                    affectedByGreed: false);
                newlyGranted = false;
                error = null;
                return true;
            }
            if (!HasCompletedFinale())
            {
                receipt = default;
                newlyGranted = false;
                error = "当前 Run 尚未完成终局节点，不能发放 Dungeon Bonus。";
                return false;
            }
            if (grant == null)
            {
                receipt = default;
                newlyGranted = false;
                error = "缺少 Dungeon Bonus 灵玉账本。";
                return false;
            }

            try
            {
                receipt = grant(configured);
            }
            catch (Exception exception)
            {
                receipt = default;
                newlyGranted = false;
                error = "Dungeon Bonus 入账失败：" + exception.Message;
                return false;
            }
            if (receipt.Source != JadeSource.StageClear ||
                receipt.BaseJade != configured ||
                receipt.FinalJade != configured ||
                receipt.AffectedByGreed)
            {
                newlyGranted = false;
                error = "Dungeon Bonus 回执不符合固定、独立结算合同。";
                return false;
            }

            _dungeonBonusGranted = true;
            _dungeonBonusJade = receipt.FinalJade;
            newlyGranted = true;
            error = null;
            return true;
        }

        /// <summary>终局楼层的独立固定收入，沿同一胜利事务记录一次。</summary>
        public bool TryGrantLevelBonus(Func<int, JadeGainResult> grant,
            out JadeGainResult receipt, out bool newlyGranted, out string error)
        {
            if (GemChests.HasPendingChoice || VictorySealRewards.IsCommitting || FloorExits.IsCommitting ||
                Interactions.IsCommitting || GemChests.IsCommitting || DoubleKeyPayments.IsCommitting || CardConversions.IsCommitting || Arcanas.IsCommitting || ChickenOfferings.IsCommitting || ManaStatues.IsCommitting || AbandonedCarts.IsCommitting || SideEvents.IsCommitting || Crystals.IsCommitting || (ChickenOfferings.HasPendingChoice || IsSpecializedInteractionBusy) || Crystals.HasPendingEvent)
            { receipt = default; newlyGranted = false; error = "Run奖励事务正在提交，不能同时发放终局收益。"; return false; }
            if (!SideEvents.TryValidateVictoryReadiness(out error))
            { receipt = default; newlyGranted = false; return false; }
            int configured = LevelBonusBaseJade;
            newlyGranted = false;
            error = null;
            receipt = new JadeGainResult(JadeSource.StageClear, configured, _levelBonusJade, false);
            if (_levelBonusGranted)
            {
                receipt = new JadeGainResult(JadeSource.StageClear, _levelBonusJade, _levelBonusJade, false);
                return true;
            }
            if (_conclusion.TryGetSnapshot(out _))
            { error = "已冻结的结果不能补发终局层奖。"; return false; }
            if (!HasCompletedFinale())
            { error = "当前 Run 尚未完成终局节点，不能发放终局层奖。"; return false; }
            if (grant == null)
            { error = "缺少终局层奖灵玉账本。"; return false; }
            try
            {
                receipt = configured == 0 ? new JadeGainResult(JadeSource.StageClear, 0, 0, false) : grant(configured);
            }
            catch (Exception exception)
            { error = "终局层奖入账失败：" + exception.Message; return false; }
            if (receipt.Source != JadeSource.StageClear || receipt.BaseJade != configured ||
                receipt.FinalJade != configured || receipt.AffectedByGreed)
            { error = "终局层奖回执不符合固定、独立结算合同。"; return false; }
            _levelBonusGranted = true;
            _levelBonusJade = receipt.FinalJade;
            newlyGranted = true;
            return true;
        }

        /// <summary>
        /// 冻结战败结果。调用方必须先提交当前战斗的 Defeat，保证最终生命、牌组与
        /// 等级已回写；重开只能发生在结果页明确选择之后。
        /// </summary>
        public bool TryConcludeDefeat(
            out DungeonRunResultSnapshot snapshot,
            out string error)
        {
            if (!CurrentFloor.TryGetPendingReturn(
                    out LevelEncounterReturn pending) ||
                pending.Outcome != CombatOutcome.Defeat)
            {
                snapshot = null;
                error = "当前 Run 没有待结算的战败结果。";
                return false;
            }
            return TryFreezeConclusion(
                DungeonRunResultStatus.Defeat,
                Array.Empty<DungeonRunRewardResult>(),
                out snapshot,
                out error);
        }

        public static DungeonRunSession Begin(
            LevelRunProfile firstFloorProfile,
            string snapshotId,
            string runId = null,
            int? runSeed = null,
            bool cardPlayGrowthEnabled = false,
            string leaderCrawlerContentId = null,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            DungeonRunSideEventPlan sideEventPlan = null, IDungeonRunSideEventContext sideEventContext = null)
        {
            return Begin(
                firstFloorProfile,
                snapshotId,
                runId,
                new RunProgressionState(cardPlayGrowthEnabled), runSeed: runSeed,
                leaderCrawlerContentId: leaderCrawlerContentId, commonEncounterSources: commonEncounterSources,
                sideEventPlan: sideEventPlan, sideEventContext: sideEventContext);
        }

        /// <summary>
        /// Starts a Run with a caller-owned card instance ID source. Headless
        /// simulations use this to keep action identities independent of other
        /// Runs in the same editor process; production callers use the overload
        /// backed by the process-wide generator.
        /// </summary>
        public static DungeonRunSession Begin(
            LevelRunProfile firstFloorProfile,
            string snapshotId,
            string runId,
            IInstanceIdGenerator instanceIds,
            int? runSeed = null,
            bool cardPlayGrowthEnabled = false,
            string leaderCrawlerContentId = null,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            DungeonRunSideEventPlan sideEventPlan = null, IDungeonRunSideEventContext sideEventContext = null)
        {
            return Begin(
                firstFloorProfile,
                snapshotId,
                runId,
                new RunProgressionState(cardPlayGrowthEnabled),
                instanceIds, runSeed, leaderCrawlerContentId, commonEncounterSources,
                sideEventPlan: sideEventPlan, sideEventContext: sideEventContext);
        }

        internal static DungeonRunSession Begin(
            LevelRunProfile firstFloorProfile,
            string snapshotId,
            string runId,
            RunProgressionState runProgression,
            IInstanceIdGenerator instanceIds = null,
            int? runSeed = null,
            string leaderCrawlerContentId = null,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            LevelRunNodeDirectory nodeDirectory = null, DungeonRunSideEventPlan sideEventPlan = null,
            IDungeonRunSideEventContext sideEventContext = null, bool? demo = null,
            int initialRewardRerolls = 0)
        {
            if (firstFloorProfile == null)
                throw new ArgumentNullException(nameof(firstFloorProfile));
            if (initialRewardRerolls < 0)
                throw new ArgumentOutOfRangeException(nameof(initialRewardRerolls));
            bool demoScope = demo ?? DemoReleasePolicy.IsDemoBuild;
            RequireReleaseScope(firstFloorProfile, demoScope);
            if (!string.IsNullOrEmpty(leaderCrawlerContentId) &&
                !DemoReleasePolicy.AllowsCrawler(leaderCrawlerContentId, demoScope))
                throw new InvalidOperationException("当前试玩版本不包含所选领卷英灵。");
            runId ??= Guid.NewGuid().ToString("N");
            DungeonRunExplorationState exploration = DungeonRunExplorationState.Create(runId, runSeed);
            LevelExplorationGuardView guards = exploration.PrepareFloor(firstFloorProfile);
            var formations = new DungeonRunFormationState(exploration.Seed, commonEncounterSources);
            formations.PrepareFloor(firstFloorProfile);
            var shared = new DungeonRunSharedState(
                firstFloorProfile.levelExperienceRules,
                runProgression ?? new RunProgressionState(),
                rewardRerollsRemaining: initialRewardRerolls);
            if (sideEventPlan != null && nodeDirectory != null)
                throw new InvalidOperationException("A source plan cannot adopt an unrelated manually frozen directory.");
            var sources = new DungeonRunSideEventState(null, sideEventPlan, string.IsNullOrEmpty(leaderCrawlerContentId) ? Array.Empty<string>() : new[] { leaderCrawlerContentId });
            nodeDirectory = sources.PrepareFloor(firstFloorProfile, exploration.Seed, sideEventContext, runId,
                Array.Empty<DungeonRunArcanaDefinition>()) ?? nodeDirectory;
            var floor = new LevelRunSession(
                firstFloorProfile,
                snapshotId,
                runId,
                shared,
                explorationGuards: guards, nodeDirectory: nodeDirectory);
            return new DungeonRunSession(
                firstFloorProfile,
                floor,
                shared,
                new List<LevelRunSaveData>(),
                new List<DungeonRunCardSaveData>(),
                new List<DungeonRunCrawlerResidenceSaveData>(),
                new DungeonRunPlayerState(),
                instanceIds: instanceIds,
                exploration: exploration, formations: formations,
                leaderCrawlerContentId: leaderCrawlerContentId, sideEvents: sources, sideEventContext: sideEventContext);
        }

        /// <summary>
        /// 为战斗生成本 Run 的牌组、入口属性基线与生命状态。首次准备的 player
        /// 必须已应用入口/meta 选择但尚未叠加 Run modifier；成功后再叠加 modifier。
        /// 后续忽略新楼层场景的默认属性/牌组，只恢复本 Run 的持久状态。
        /// </summary>
        public bool TryPrepareCombat(
            PlayerStats player,
            IReadOnlyList<CardConfig> seedDeck,
            IReadOnlyList<CardConfig> cardPool,
            out List<CardConfig> preparedDeck,
            out string error)
        {
            preparedDeck = null;
            error = null;
            if (IsRewardCommitInProgress)
            {
                error = "Run 奖励事务提交期间不能准备战斗。";
                return false;
            }
            if (Interactions.HasPendingChoice || Arcanas.HasPendingChoice || (ChickenOfferings.HasPendingChoice || IsSpecializedInteractionBusy) || Crystals.HasPendingEvent)
            {
                error = "请先完成待处理的地图奖励选择。";
                return false;
            }
            if (player == null)
            {
                error = "玩家状态不能为空。";
                return false;
            }

            // Empty after a paid removal is an established deck, not an entry
            // request. Player state is captured with the initial seed and is
            // already persisted, including older save versions.
            ICardAuthorReferenceResolver references;
            try
            {
                var authors = new List<CardConfig>(cardPool ?? Array.Empty<CardConfig>());
                if (_cards.Count == 0 && !HasPlayerState)
                    foreach (var author in seedDeck ?? Array.Empty<CardConfig>())
                        if (!authors.Contains(author)) authors.Add(author);
                references = DungeonRunCardCodec.CreateResolver(authors);
            }
            catch (Exception exception) { error = exception.Message; return false; }
            if (_cards.Count == 0 && !HasPlayerState)
            {
                if (!DungeonRunCardCodec.TrySeedCards(seedDeck, _instanceIds.NextId, references,
                        out preparedDeck, out var captured, out error)) return false;
                _cards = captured;
            }
            else
            {
                if (!DungeonRunCardCodec.TryRestoreCards(_cards, cardPool, out preparedDeck, out error)) return false;
                // Teaching encounters introduce authored cards gradually. Reconcile
                // only missing multiplicities; retain rewards and every existing
                // physical instance, including its breakthrough/seals. Re-entry
                // and restored encounters therefore cannot grant the same seed twice.
                if (string.Equals(CurrentFloorContentId, DemoReleasePolicy.TutorialLevelContentId,
                        StringComparison.Ordinal))
                {
                    var additions = new List<CardConfig>();
                    try
                    {
                        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (var saved in _cards)
                            remaining[saved.contentId] = remaining.TryGetValue(saved.contentId, out int existingCount) ? existingCount + 1 : 1;
                        var missing = new List<CardConfig>();
                        foreach (var author in seedDeck ?? Array.Empty<CardConfig>())
                        {
                            if (author == null || string.IsNullOrWhiteSpace(author.contentId))
                                throw new InvalidOperationException("教程作者牌组包含空卡牌或缺失的稳定ID。");
                            if (remaining.TryGetValue(author.contentId, out int remainingCount) && remainingCount > 0)
                                remaining[author.contentId] = remainingCount - 1;
                            else missing.Add(author);
                        }
                        if (missing.Count > 0)
                        {
                            // Codec freezes and validates all missing authors before
                            // allocating IDs. No roster changes precede success.
                            if (!DungeonRunCardCodec.TrySeedCards(missing, AllocateInteractionCardId, references,
                                    out additions, out var captured, out error))
                                throw new InvalidOperationException(error);
                            var draft = new List<DungeonRunCardSaveData>(_cards);
                            draft.AddRange(captured);
                            preparedDeck.AddRange(additions);
                            _cards = draft;
                        }
                    }
                    catch (Exception exception)
                    {
                        foreach (var card in preparedDeck) DungeonRunCardCodec.Release(card);
                        foreach (var card in additions ?? new List<CardConfig>())
                            if (!preparedDeck.Contains(card)) DungeonRunCardCodec.Release(card);
                        preparedDeck = null;
                        error = exception.Message;
                        return false;
                    }
                }
            }
            _combatAuthorReferences = references;

            _playerState.Prepare(player, RunProgression);
            if (!TryGetMapStartingMana(out int startingMana)) throw new InvalidOperationException("Run entry Mana baseline is missing.");
            _playerState.PrepareMapMana(startingMana);
            return true;
        }

        /// <summary>Retains only the already borrowed author bindings when the
        /// same live combat retries a failed result save. A normal load must
        /// prepare combat afresh; neither bindings nor resolver enter the save.</summary>
        internal bool TryRetainCombatAuthorBindingsFrom(DungeonRunSession previous, out string error)
        {
            error = null;
            if (previous == null || previous._combatAuthorReferences == null ||
                !string.Equals(RunId, previous.RunId, StringComparison.Ordinal) ||
                !string.Equals(DungeonContentId, previous.DungeonContentId, StringComparison.Ordinal) ||
                !string.Equals(CurrentFloorContentId, previous.CurrentFloorContentId, StringComparison.Ordinal) ||
                !CurrentFloor.ActiveEncounter.HasValue)
            {
                error = "Combat result recovery requires the same prepared Run and floor.";
                return false;
            }
            LevelEncounterLaunch target = CurrentFloor.ActiveEncounter.Value;
            string previousNodeId, previousScenarioId;
            if (previous.CurrentFloor.ActiveEncounter.HasValue)
            {
                LevelEncounterLaunch active = previous.CurrentFloor.ActiveEncounter.Value;
                previousNodeId = active.NodeContentId;
                previousScenarioId = active.ScenarioId;
            }
            else if (previous.CurrentFloor.TryGetPendingReturn(out LevelEncounterReturn accepted))
            {
                previousNodeId = accepted.NodeContentId;
                previousScenarioId = previous.CurrentFloor.FindNode(previousNodeId)?.scenarioId;
            }
            else
            {
                error = "Combat result recovery has no matching active or accepted encounter.";
                return false;
            }
            if (!string.Equals(target.RunId, previous.CurrentFloor.RunId, StringComparison.Ordinal) ||
                !string.Equals(target.NodeContentId, previousNodeId, StringComparison.Ordinal) ||
                !string.Equals(target.ScenarioId, previousScenarioId, StringComparison.Ordinal) ||
                _combatAuthorReferences != null &&
                !ReferenceEquals(_combatAuthorReferences, previous._combatAuthorReferences))
            {
                error = "Combat result recovery cannot borrow bindings from a different encounter configuration.";
                return false;
            }
            _combatAuthorReferences = previous._combatAuthorReferences;
            return true;
        }

        /// <summary>
        /// 取得地图阶段可展示的当前牌组实例副本。返回对象不拥有 Run 状态，只有
        /// TryAssignSeal 能提交修改。
        /// </summary>
        public bool TryGetCurrentCards(
            IReadOnlyList<CardConfig> cardPool,
            out List<CardConfig> cards,
            out string error)
        {
            if (_cards.Count == 0 && !HasPlayerState)
            {
                cards = null;
                error = "Run 尚未建立可钤印的牌组实例。";
                return false;
            }
            return DungeonRunCardCodec.TryRestoreCards(_cards, cardPool, out cards, out error);
        }

        private static DungeonRunExperienceCollections ReadExperienceCollections(DungeonRunSaveData data, string payload)
        {
            bool hasFlag = JsonObjectShape.TryGetField(payload, "hasExperienceCollectionsState", out string flag);
            bool hasBody = JsonObjectShape.TryGetField(payload, "experienceCollections", out string body);
            if (data.schemaVersion < 32)
            {
                if (hasFlag || hasBody || data.hasExperienceCollectionsState)
                    throw new InvalidOperationException("Legacy Run cannot carry future experience collections.");
                return DungeonRunExperienceCollections.EmptyLegacy();
            }
            if (!hasFlag || flag != "true" || !data.hasExperienceCollectionsState || !hasBody || body == "null")
                throw new InvalidOperationException("Run requires explicit experience collections.");
            return DungeonRunExperienceCollections.Restore(DungeonRunExperienceCollections.ReadSaveData(body));
        }

        private static DungeonRunCrystalSaveData ReadCrystalState(DungeonRunSaveData data, string payload)
        {
            DungeonRunCrystalEvents.RequireRunEnvelope(payload, data.schemaVersion >= 31, data.schemaVersion >= 32, data.schemaVersion >= 33, data.schemaVersion >= 35);
            bool hasFlag = JsonObjectShape.TryGetField(payload, "hasCrystalState", out string rawFlag);
            bool hasBody = JsonObjectShape.TryGetField(payload, "crystalEvents", out string rawBody);
            if (data.schemaVersion < 31)
            {
                if (hasFlag || hasBody || data.hasCrystalState)
                    throw new InvalidOperationException("Legacy Run cannot carry future Crystal state.");
                return null;
            }
            if (!hasFlag || rawFlag != "true" || !data.hasCrystalState || !hasBody || rawBody == "null")
                throw new InvalidOperationException("Run requires its explicit Crystal state.");
            return DungeonRunCrystalEvents.ReadSaveData(rawBody);
        }

        private static string ReadGemChestState(DungeonRunSaveData data, string payload)
        {
            if (data.schemaVersion < 33) return null;
            if (!data.hasGemChestState || string.IsNullOrEmpty(data.gemChestStateJson) ||
                !JsonObjectShape.TryGetField(payload, "hasGemChestState", out string flag) || flag != "true" ||
                !JsonObjectShape.TryGetField(payload, "gemChestStateJson", out string body) || body == "null" || !body.StartsWith("\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Run requires explicit Gem Chest state.");
            return data.gemChestStateJson;
        }

        internal DungeonRunInteractionDraft CaptureInteractionDraft()
            => new DungeonRunInteractionDraft
            {
                Cards = DungeonRunCardCodec.CloneCards(_cards),
                Progression = new CombatProgressionState(
                    CombatProgression.Rules, CombatProgression.CurrentLevel,
                    CombatProgression.CurrentExperience, CombatProgression.XpFractionCarryUnits),
                ExperienceCollections = ExperienceCollections.Clone(),
                ForcedOpeningCardId = _pendingForcedOpeningHandCardInstanceId
            };

        // 事务已经完成全部校验和外部参与者提交；此处无回调，替换不会暴露中间状态。
        internal void CommitInteractionDraft(DungeonRunInteractionDraft draft)
        {
            if (draft?.ExperienceCollections == null) throw new InvalidOperationException("Missing draft XP collections.");
            ExperienceCollections.Adopt(draft.ExperienceCollections);
            _cards = draft.Cards;
            CombatProgression.Restore(draft.Progression.CurrentLevel,
                draft.Progression.CurrentExperience, draft.Progression.XpFractionCarryUnits);
            _pendingForcedOpeningHandCardInstanceId = draft.ForcedOpeningCardId;
            var removedAssignments = new List<string>();
            foreach (var pair in _sealAssignments)
                if (!_cards.Exists(card => card.runtimeInstanceId == pair.Value.cardRuntimeInstanceId))
                    removedAssignments.Add(pair.Key);
            foreach (string id in removedAssignments)
                _sealAssignments.Remove(id);
        }

        internal int AllocateInteractionCardId()
        {
            int id;
            do { id = _instanceIds.NextId(); }
            while (_cards.Exists(card => card.runtimeInstanceId == id) || DoubleKeyPayments.WasConsumed(id) || ChickenOfferings.WasConsumed(id) || GemChests.HasConsumed(id) || CardStatOfferings.HasConsumed(id) ||
                DuplicateOfferings.ReservesInstanceId(id) || GunForHire.ReservesInstanceId(id) || Invitations.ReservesInstanceId(id));
            return id;
        }

        /// <summary>
        /// Publish a payment draft only for the duration of its atomic save.
        /// Restores deck order, forced opening and seal references on failure;
        /// the payment/choice owners restore their own checkpoints separately.
        /// </summary>
        internal void CommitInteractionDraft(DungeonRunInteractionDraft draft, Action persist)
        {
            var previous = CaptureInteractionDraft();
            var assignments = new Dictionary<string, DungeonRunSealAssignmentSaveData>(_sealAssignments);
            try
            {
                CommitInteractionDraft(draft);
                persist?.Invoke();
            }
            catch
            {
                CommitInteractionDraft(previous);
                _sealAssignments.Clear();
                foreach (var pair in assignments) _sealAssignments.Add(pair.Key, pair.Value);
                throw;
            }
        }

        /// <summary>
        /// 原子地把一枚灵印钤入指定卡牌实例并登记地图效果回执。失败时牌组、
        /// 回执和下一战首手保证均不改变；同一 effectId 重试只返回旧回执。
        /// </summary>
        public bool TryAssignSeal(
            string nodeContentId,
            LevelInteractionEffect effect,
            int cardInstanceId,
            CardSealSpec seal,
            IReadOnlyList<CardConfig> cardPool,
            out LevelInteractionReceipt receipt,
            out string error,
            Func<string, bool> isEvolutionResultEligible = null)
        {
            receipt = default;
            error = null;
            if (Interactions.HasPendingChoice || Arcanas.HasPendingChoice || (ChickenOfferings.HasPendingChoice || IsSpecializedInteractionBusy) || Interactions.IsCommitting || GemChests.IsCommitting || DoubleKeyPayments.IsCommitting ||
                VictorySealRewards.IsCommitting || FloorExits.IsCommitting || CardConversions.IsCommitting || Arcanas.IsCommitting || ChickenOfferings.IsCommitting || ManaStatues.IsCommitting || AbandonedCarts.IsCommitting || SideEvents.IsCommitting || Crystals.IsCommitting || (ChickenOfferings.HasPendingChoice || IsSpecializedInteractionBusy) || Crystals.HasPendingEvent)
            { error = "请先完成当前地图奖励事务。"; return false; }
            if (effect == null ||
                effect.kind != LevelInteractionEffectKind.GrantSeal)
            {
                error = "钤印事务只接受 GrantSeal 效果。";
                return false;
            }
            if (effect.amount != 1)
            {
                error = $"即时钤印效果 {effect.effectId} 的 amount 必须为 1。";
                return false;
            }
            if (seal == null || string.IsNullOrWhiteSpace(seal.contentId) ||
                !string.Equals(
                    seal.contentId,
                    effect.targetContentId,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = $"交互效果 {effect.effectId} 的灵印内容不匹配。";
                return false;
            }
            if (CurrentFloor.TryGetInteractionReceipt(
                    effect.effectId, out receipt))
            {
                if (!_sealAssignments.TryGetValue(
                        effect.effectId, out var previous))
                {
                    error = $"交互效果 {effect.effectId} 已按其他去向结算，" +
                            "不能再钤入卡牌。";
                    return false;
                }
                if (previous.cardRuntimeInstanceId != cardInstanceId)
                {
                    error = $"交互效果 {effect.effectId} 已钤入卡牌实例 " +
                            $"{previous.cardRuntimeInstanceId}，不能改绑为 " +
                            $"{cardInstanceId}。";
                    return false;
                }
                return true;
            }
            if (cardInstanceId <= 0)
            {
                error = "钤印目标必须是有效的卡牌实例 ID。";
                return false;
            }
            if (!CanReceiveSealRewards)
            { error = "取得玉印后才能钤印。"; return false; }
            int savedIndex = _cards.FindIndex(card =>
                card != null && card.runtimeInstanceId == cardInstanceId);
            if (savedIndex < 0)
            {
                error = $"Run 牌组中找不到卡牌实例：{cardInstanceId}。";
                return false;
            }
            DungeonRunCardSaveData saved = _cards[savedIndex];
            if (!DungeonRunSealAssignmentRules.TryPrepare(saved,
                    DungeonRunSealAssignmentRules.FindSource(cardPool, saved.contentId),
                    seal, id => DungeonRunSealAssignmentRules.FindSource(cardPool, id) != null &&
                        (isEvolutionResultEligible == null || isEvolutionResultEligible(id)), out var replacement,
                    out int slotIndex, out error,
                    references: DungeonRunCardCodec.CreateResolver(cardPool)))
                return false;
            _cards[savedIndex] = replacement;
            if (!CurrentFloor.TryRecordExternalEffect(
                    nodeContentId,
                    effect,
                    slotIndex,
                    slotIndex + 1,
                    effect.amount,
                    out receipt,
                    out error,
                    targetRuntimeInstanceId: cardInstanceId))
            {
                _cards[savedIndex] = saved;
                return false;
            }

            _pendingForcedOpeningHandCardInstanceId = cardInstanceId;
            _sealAssignments[effect.effectId] =
                new DungeonRunSealAssignmentSaveData
                {
                    effectId = effect.effectId,
                    sealContentId = seal.contentId,
                    cardRuntimeInstanceId = cardInstanceId,
                    slotIndex = slotIndex,
                    disposition = seal.effect == CardSealEffect.Evolution
                        ? DungeonRunSealAssignmentDisposition.EvolutionConsumed
                        : DungeonRunSealAssignmentDisposition.Installed
                };
            return true;
        }

        /// <summary>
        /// 把待处理的胜利灵印奖励钤入精确卡牌实例。卡牌、奖励终态、下一战
        /// 首手标记和存档用稳定回执 ID 关联；重复提交不会再次落印。
        /// </summary>
        public bool TryAssignVictorySealReward(
            string rewardContentId,
            int cardInstanceId,
            CardSealSpec seal,
            IReadOnlyList<CardConfig> cardPool,
            out LevelVictorySealRewardReceipt receipt,
            out string error,
            Func<string, bool> isEvolutionResultEligible = null, Action persist = null)
            => VictorySealRewards.TryAssign(rewardContentId, cardInstanceId, seal,
                cardPool, out receipt, out error, isEvolutionResultEligible, persist);

        internal bool ApplyVictorySealAssignment(
            string rewardContentId, int cardInstanceId, CardSealSpec seal,
            IReadOnlyList<CardConfig> cardPool, out LevelVictorySealRewardReceipt receipt,
            out string error, Func<string, bool> isEvolutionResultEligible)
        {
            if (!CanReceiveSealRewards && !CurrentFloor.TryGetVictorySealRewardReceipt(rewardContentId, out _))
            { receipt = default; error = "取得玉印后才能钤印。"; return false; }
            bool committed =
                DungeonRunVictorySealRewardTransaction.TryAssign(
                    _cards,
                    CurrentFloor,
                    rewardContentId,
                    cardInstanceId,
                    seal,
                    cardPool,
                    out receipt,
                    out error,
                    isEvolutionResultEligible);
            if (!committed || !receipt.NewlyResolved)
                return committed;

            _pendingForcedOpeningHandCardInstanceId = cardInstanceId;
            _sealAssignments[rewardContentId] =
                new DungeonRunSealAssignmentSaveData
                {
                    effectId = rewardContentId,
                    sealContentId = seal.contentId,
                    cardRuntimeInstanceId = cardInstanceId,
                    slotIndex = receipt.SlotIndex,
                    disposition = seal.effect == CardSealEffect.Evolution
                        ? DungeonRunSealAssignmentDisposition.EvolutionConsumed
                        : DungeonRunSealAssignmentDisposition.Installed
                };
            return true;
        }

        /// <summary>把待处理胜利灵印交给统一经济入口化玉，重复提交不重复入账。</summary>
        public bool TryCashOutVictorySealReward(
            string rewardContentId,
            Func<JadeGainResult> grantCashOut,
            out LevelVictorySealRewardReceipt receipt,
            out string error)
        {
            return VictorySealRewards.TryCashOut(rewardContentId, grantCashOut, out receipt, out error);
        }

        /// <summary>Durable victory cash-out; the wallet restores external ledgers and this Run restores its income.</summary>
        public bool TryCashOutVictorySealReward(string rewardContentId, int baseAmount,
            IDungeonRunInteractionWallet wallet, out LevelVictorySealRewardReceipt receipt,
            out string error, Action persist = null)
            => VictorySealRewards.TryCashOut(rewardContentId, baseAmount, wallet, out receipt, out error, persist);

        // Scale review: this only publishes/restores existing Run-owned projections.
        // Reward rules and reentrancy belong to their transaction owners;
        // this aggregate restores its existing projections and the same floor identity.
        // Scale review: this aggregate remains a snapshot/projection boundary.
        // Healing rules and receipt/persistence intent stay in Interactions; no new owner is added.
        internal void CommitRewardState(Action commit, Action persist)
        {
            LevelRunSession floor = CurrentFloor;
            LevelRunSession previousFloor = floor.CaptureTransactionState();
            DungeonRunSharedState previousModifiers = _shared.CloneForFloorPreparation();
            var previous = CaptureInteractionDraft();
            DungeonRunPlayerState previousPlayer = _playerState.CaptureTransactionState();
            DungeonRunStatisticsSnapshot previousStatistics = _statistics.Capture();
            CardUnlockFactsSnapshot previousFacts = RunProgression.CardUnlockFacts.Capture();
            var assignments = new Dictionary<string, DungeonRunSealAssignmentSaveData>(_sealAssignments);
            RunProgressionState run = RunProgression;
            RunProgressionSnapshot counters = run.Capture();
            try
            {
                commit();
                persist?.Invoke();
            }
            catch
            {
                floor.AdoptPreparedState(previousFloor);
                _shared.CopyFloorModifiersFrom(previousModifiers);
                CommitInteractionDraft(previous);
                _sealAssignments.Clear();
                foreach (var pair in assignments) _sealAssignments.Add(pair.Key, pair.Value);
                run.Restore(counters);
                run.CardPlayGrowth.Restore(previousModifiers.RunProgression.CardPlayGrowth.Capture());
                run.CrawlerDurationReserve.Restore(previousModifiers.RunProgression.CrawlerDurationReserve.PendingBonus);
                _playerState.AdoptPreparedState(previousPlayer);
                _statistics.RestoreTransactionState(previousStatistics);
                run.CardUnlockFacts.Restore(previousFacts);
                throw;
            }
        }

        // Independent world-event owners request only their shared projection inside this save boundary.
        internal void ApplyCartDrawPickup() => _shared.AddHandLimit(DungeonRunAbandonedCarts.DrawCountBonus);
        // The conversion owner controls pickup timing; this aggregate only projects its Run modifier.
        internal int ApplyRepairedManaPickup(int amount) => _shared.AddBaseMana(amount);

        // Only the statue / one-card Offering owner requests this Mana projection.
        // Source current Mana is clamped to 1m even if the existing project combat
        // returned a larger credit; the Starting/base bonus retains project saturation.
        internal void ApplyPersistentManaPickup()
        {
            if (!_playerState.HasManaState || !TryGetMapStartingMana(out _)) throw new InvalidOperationException("Known Run Mana baseline/balance is required.");
            _shared.AddBaseMana(DungeonRunManaStatues.ManaAmount);
            _playerState.SetMapManaAfterPickup((int)Math.Min(DungeonRunManaStatues.SourceMaximumMana,
                (long)_playerState.MapCurrentMana + DungeonRunManaStatues.ManaAmount));
            RunProgression.CardUnlockFacts.RecordWorldFact(CardUnlockMetric.ManaCollected, DungeonRunManaStatues.ManaAmount);
        }

        /// <summary>
        /// 原子地移除一个当前 Run 卡牌实例并应用规则表定义的永久修正。
        /// 规则解析、回执幂等与失败回滚由独立事务模块负责。
        /// </summary>
        public bool TryConvertCard(
            string nodeContentId,
            LevelInteractionEffect effect,
            int cardInstanceId,
            RunCardConversionRuleProfile profile,
            IReadOnlyList<CardConfig> cardPool,
            out LevelInteractionReceipt receipt,
            out string error,
            Action persist = null)
            => CardConversions.TryConvert(nodeContentId, effect, cardInstanceId,
                profile, cardPool, out receipt, out error, persist);

        // Only the conversion owner invokes this projection mutation inside its snapshot.
        internal bool ApplyCardConversion(
            string nodeContentId, LevelInteractionEffect effect, int cardInstanceId,
            RunCardConversionRuleProfile profile, IReadOnlyList<CardConfig> cardPool,
            out LevelInteractionReceipt receipt, out string error)
        {
            bool committed = RunCardConversionTransaction.TryCommit(
                _cards,
                _shared,
                CurrentFloor,
                nodeContentId,
                effect,
                cardInstanceId,
                profile,
                cardPool,
                out receipt,
                out error);
            if (committed && receipt.NewlyApplied)
            {
                // The conversion owns the complete physical card, including
                // its seals. Remove only references to the consumed instance;
                // keeping them would make this otherwise valid save unrestorable.
                var consumedAssignments = new List<string>();
                foreach (var pair in _sealAssignments)
                    if (pair.Value.cardRuntimeInstanceId == cardInstanceId)
                        consumedAssignments.Add(pair.Key);
                foreach (string id in consumedAssignments)
                    _sealAssignments.Remove(id);
                if (_pendingForcedOpeningHandCardInstanceId == cardInstanceId)
                    _pendingForcedOpeningHandCardInstanceId = 0;
            }
            return committed;
        }

        /// <summary>Canonical map healing and its receipt are committed by the existing interaction owner.</summary>
        public bool TryHealCurrentHealth(string nodeContentId, string effectId,
            out LevelInteractionReceipt receipt, out string error, Action persist = null)
            => Interactions.TryHeal(nodeContentId, effectId, out receipt, out error, persist);

        /// <summary>
        /// Memory-only compatibility API. It has no node receipt or durable commit;
        /// production map callers use the node-bound overload above.
        /// </summary>
        public bool TryHealCurrentHealth(
            int amount,
            out int valueBefore,
            out int valueAfter,
            out string error)
        {
            valueBefore = valueAfter = CurrentHealth;
            error = null;
            if (IsRewardCommitInProgress)
            { error = "Run 奖励事务提交期间不能执行未绑定节点的回血。"; return false; }
            return ApplyHealing(amount, out valueBefore, out valueAfter, out error);
        }

        // Only the Offering owner invokes this fixed source projection within CommitRewardState.
        // Existing player/statistic/fact owners remain the same objects on success and rollback.
        internal List<int> ApplyChickenOfferingHealing(bool experimentalMedicineEnabled)
        {
            if (!HasPlayerState) throw new InvalidOperationException("Offering requires an established player health state.");
            RunProgression.CardUnlockFacts.RecordWorldFact(CardUnlockMetric.FoodCollected, ChickenOfferingContracts.FoodCount);
            var actual = new List<int>();
            for (int i = 0; i < ChickenOfferingContracts.FoodCount; i++)
            {
                int amount = ChickenOfferingContracts.HealingPerFood(experimentalMedicineEnabled);
                if (!_playerState.TryHeal(amount, out int before, out int after, out string error))
                    throw new InvalidOperationException(error);
                int delta = after - before;
                _statistics.RecordExternalHealing(delta);
                actual.Add(delta);
            }
            return actual;
        }

        // Only the interaction owner invokes this projection mutation under its guard/snapshot.
        internal bool ApplyHealing(int amount, out int valueBefore, out int valueAfter, out string error)
        {
            if (!_playerState.TryHeal(amount, out valueBefore, out valueAfter, out error)) return false;
            _statistics.RecordExternalHealing(valueAfter - valueBefore);
            // A completed map recovery node is the authoritative "picked up
            // Floor Chicken" boundary used by the Garlic unlock contract.
            // Count the pickup even when the player was already at full health;
            // the node was consumed successfully, while the editor-only
            // transient minimum-health helper never enters this path.
            RunProgression.CardUnlockFacts.RecordWorldFact(
                CardUnlockMetric.FoodCollected);
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 仅供编辑器瞬态入口把旧检查点抬到人工验收所需的最低生命。
        /// 这不是 Run 内治疗，因此不会写入治疗统计或交互回执。
        /// </summary>
        public bool TryEnsureTransientEntryMinimumHealth(
            int minimumHealth,
            out string error)
            => _playerState.TryEnsureTransientMinimumHealth(minimumHealth, out error);
#endif

        /// <summary>提交胜利并保存下一场战斗要继承的生命、牌组和等级经验。</summary>
        public bool TryCompleteVictory(
            CombatController combat,
            out LevelEncounterReturn result,
            out string error)
            => TryCompleteCombat(
                combat,
                CombatOutcome.Victory,
                out result,
                out error);

        /// <summary>
        /// 提交战败并保留终局快照所需的生命、牌组和等级经验。此方法不重建 Run。
        /// </summary>
        public bool TryCompleteDefeat(
            CombatController combat,
            out LevelEncounterReturn result,
            out string error)
            => TryCompleteCombat(
                combat,
                CombatOutcome.Defeat,
                out result,
                out error);

        private bool TryCompleteCombat(
            CombatController combat,
            CombatOutcome outcome,
            out LevelEncounterReturn result,
            out string error)
        {
            result = default;
            error = null;
            if (combat == null)
            {
                error = "战斗控制器不能为空。";
                return false;
            }
            if (!CurrentFloor.ActiveEncounter.HasValue)
            {
                error = "当前 Run 没有活动遭遇。";
                return false;
            }
            if (outcome != CombatOutcome.Victory &&
                outcome != CombatOutcome.Defeat)
            {
                error = $"不支持的战斗结果：{outcome}。";
                return false;
            }

            var survivingCards = new List<CardConfig>(
                combat.Deck.Count + combat.Hand.Count + combat.Discard.Count);
            AddCards(survivingCards, combat.Deck);
            AddCards(survivingCards, combat.Hand);
            AddCards(survivingCards, combat.Discard);
            List<DungeonRunCardSaveData> capturedCards =
                DungeonRunCardCodec.CaptureCards(survivingCards, _combatAuthorReferences, out error);
            if (capturedCards == null)
                return false;
            List<DungeonRunCrawlerResidenceSaveData> capturedCrawlers =
                CaptureCrawlerResidents(
                    combat.ColorTrigger.ActiveCrawlers,
                    out error);
            if (capturedCrawlers == null)
                return false;

            LevelEncounterLaunch encounter =
                CurrentFloor.ActiveEncounter.Value;
            IReadOnlyList<string> resolvedVictorySealIds = null;
            if (outcome == CombatOutcome.Victory)
            {
                LevelNode node = CurrentFloor.FindNode(
                    encounter.NodeContentId);
                LevelRewardGrant randomGrant = null;
                foreach (LevelRewardGrant grant in
                         node?.victoryRewards ?? new List<LevelRewardGrant>())
                {
                    if (grant != null &&
                        !string.IsNullOrWhiteSpace(grant.sealRewardPoolId))
                    {
                        randomGrant = grant;
                        break;
                    }
                }
                if (CanReceiveSealRewards && randomGrant != null &&
                    !DungeonRunVictorySealRewardResolver.TryResolve(
                        RunId,
                        RunSeed,
                        combat.CurrentLuck,
                        randomGrant,
                        _victorySealRewardPools,
                        out resolvedVictorySealIds,
                        out error,
                        eligibleHosts: survivingCards))
                {
                    return false;
                }
            }
            DungeonRunExperienceCollections nextExperienceCollections;
            try
            {
                nextExperienceCollections = ExperienceCollections.Clone();
                nextExperienceCollections.Record(CurrentFloorContentId,
                    new ExperienceCollectionTotals(combat.Statistics.PlayerXPGathered, 0));
            }
            catch (Exception exception) { error = exception.Message; return false; }
            if (!CurrentFloor.TryCompleteEncounter(
                    encounter.RunId,
                    encounter.NodeContentId,
                    outcome,
                    resolvedVictorySealIds,
                    out result,
                    out error, allowSealRewards: CanReceiveSealRewards))
            {
                return false;
            }

            _playerState.CaptureCombatHealth(combat.Player);
            _playerState.CaptureCombatMana(combat.Mana);
            _cards = capturedCards;
            _crawlerResidents = capturedCrawlers;
            _pendingForcedOpeningHandCardInstanceId = 0;
            Invitations.ConsumeOpeningGuarantees();
            CombatProgression.Store(combat.CurrentLevel, combat.CurrentXP);
            _statistics.AddCombat(combat.Statistics);
            ExperienceCollections.Adopt(nextExperienceCollections);
            return true;
        }

        public string Serialize() => Serialize(captureElapsedTime: true);

        /// <summary>Detached comparison projection for deterministic replay.
        /// Wall-clock report metadata is omitted; the live timer and frozen result
        /// remain unchanged. This projection is not a replacement player save.</summary>
        public string SerializeForDeterministicComparison() => Serialize(captureElapsedTime: false);

        private string Serialize(bool captureElapsedTime)
        {
            var data = new DungeonRunSaveData
            {
                schemaVersion = PayloadSchemaVersion,
                hasManaRepairPickupState = true,
                manaRepairPickups = CardConversions.CaptureManaPickupSaveData(),
                hasDuplicateOfferingState = true, duplicateOfferingStateJson = DuplicateOfferings.CaptureCanonicalJson(),
                hasGunForHireState = true, gunForHireStateJson = GunForHire.CaptureCanonicalJson(),
                hasInvitationState = true, invitationStateJson = Invitations.CaptureCanonicalJson(),
                hasCardStatOfferingState = true, cardStatOfferingStateJson = CardStatOfferings.CaptureCanonicalJson(),
                hasAdditionalStatState = true,
                additionalStats = DungeonRunAdditionalStatsSaveData.Capture(RunProgression.AdditionalStats),
                hasRevivalReserve = true,
                revivalReserve = DungeonRunRevivalReserveSaveData.Capture(RunProgression.RevivalReserve),
                hasExperienceCollectionsState = true, experienceCollections = ExperienceCollections.CaptureSaveData(),
                hasCrystalState = true, crystalEvents = Crystals.CaptureSaveData(),
                hasGemChestState = true, gemChestStateJson = GemChests.CaptureCanonicalJson(),
                hasSideEventSourceState = true,
                sideEventSourceStateJson = SideEvents.CaptureCanonicalJson(),
                runId = RunId,
                dungeonContentId = DungeonContentId,
                hasLeaderCrawlerState = true,
                leaderCrawlerContentId = LeaderCrawlerContentId,
                firstFloorContentId = FirstFloorContentId,
                currentFloorContentId = CurrentFloorContentId,
                exploration = _floors.CaptureExploration(),
                formations = _floors.CaptureFormations(),
                currentLevel = CombatProgression.CurrentLevel,
                currentExperience = CombatProgression.CurrentExperience,
                xpFractionCarryUnits =
                    CombatProgression.XpFractionCarryUnits,
                runLuckBonus = RunProgression.RunLuckBonus,
                runGreedBonus = RunProgression.RunGreedBonus,
                runGrowthBonus = RunProgression.RunGrowthBonus,
                runJadeEarned = RunProgression.RunJadeEarned,
                hasCardPlayGrowthState = true,
                cardPlayGrowth = DungeonRunCardPlayGrowthSaveData.Capture(RunProgression.CardPlayGrowth),
                hasCardUnlockFactsState = true,
                cardUnlockFacts = DungeonRunCardUnlockFactsSaveData.Capture(RunProgression.CardUnlockFacts),
                hasCrawlerDurationReserveState = true,
                pendingCrawlerDurationBonus = RunProgression.CrawlerDurationReserve.PendingBonus,
                elapsedRunSeconds = captureElapsedTime ? ElapsedRunSeconds : 0,
                dungeonBonusGranted = _dungeonBonusGranted,
                dungeonBonusJade = _dungeonBonusJade,
                hasLevelBonusState = true,
                levelBonusGranted = _levelBonusGranted,
                levelBonusJade = _levelBonusJade,
                baseManaBonus = _shared.BaseManaBonus,
                baseArmorPerTurnBonus = _shared.BaseArmorPerTurnBonus,
                mightBonus = _shared.MightBonus,
                handLimitBonus = _shared.HandLimitBonus,
                areaBonus = _shared.AreaBonus,
                rewardRerollsRemaining =
                    _shared.RewardRerollsRemaining,
                hasPlayerState = HasPlayerState,
                maxHealth = MaxHealth,
                currentHealth = CurrentHealth,
                hasPlayerBaseline = _playerState.HasBaseline,
                playerBaselineOrigin = PlayerBaselineOrigin,
                playerBaseline = _playerState.CaptureBaseline(),
                pendingForcedOpeningHandCardInstanceId =
                    _pendingForcedOpeningHandCardInstanceId,
                cards = DungeonRunCardCodec.CloneCards(_cards),
                crawlerResidents = CloneCrawlerResidents(_crawlerResidents),
                usedCrawlerContentIds =
                    new List<string>(_usedCrawlerContentIds),
                sealAssignments = CloneSealAssignments(_sealAssignments),
                completedFloorStates = _floors.CaptureHistory(),
                currentFloor = CurrentFloor.CaptureSaveData(),
                statistics = _statistics.CaptureSaveData(),
                conclusion = _conclusion.CaptureSaveData(),
                interaction = Interactions.CaptureSaveData(),
                doubleKeyPayments = DoubleKeyPayments.CaptureSaveData(),
                hasArcanaState = true,
                arcana = Arcanas.CaptureSaveData(),
                hasChickenOfferingState = true,
                chickenOffering = ChickenOfferings.CaptureSaveData(),
                hasManaState = HasManaState,
                mapCurrentMana = MapCurrentMana,
                manaStateOrigin = ManaStateOrigin,
                hasManaStatueState = true,
                manaStatues = ManaStatues.CaptureSaveData(),
                hasAbandonedCartState = true,
                abandonedCarts = AbandonedCarts.CaptureSaveData()
            };
            if (!captureElapsedTime && data.conclusion != null)
                data.conclusion.elapsedRunSeconds = 0;
            return JsonUtility.ToJson(data);
        }

        public static bool TryRestore(
            string payload,
            Func<string, LevelRunProfile> profileResolver,
            out DungeonRunSession session,
            out string error,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            IDungeonRunSideEventContext sideEventContext = null)
        {
            return TryRestore(
                payload,
                profileResolver,
                runProgression: null,
                instanceIds: null,
                runIdOverride: null,
                out session,
                out error, commonEncounterSources, sideEventContext);
        }

        /// <summary>
        /// Restores a checkpoint for a deterministic headless run. Runtime IDs
        /// created after restore use the supplied generator, and the saved run
        /// identity can be replaced without changing the checkpoint payload.
        /// </summary>
        public static bool TryRestore(
            string payload,
            Func<string, LevelRunProfile> profileResolver,
            IInstanceIdGenerator instanceIds,
            string runIdOverride,
            out DungeonRunSession session,
            out string error,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            IDungeonRunSideEventContext sideEventContext = null)
        {
            if (instanceIds == null)
                throw new ArgumentNullException(nameof(instanceIds));
            return TryRestore(
                payload,
                profileResolver,
                null,
                instanceIds,
                runIdOverride,
                out session,
                out error, commonEncounterSources, sideEventContext);
        }

        internal static bool TryRestore(
            string payload,
            Func<string, LevelRunProfile> profileResolver,
            RunProgressionState runProgression,
            out DungeonRunSession session,
            out string error,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            IDungeonRunSideEventContext sideEventContext = null)
        {
            return TryRestore(
                payload,
                profileResolver,
                runProgression,
                instanceIds: null,
                runIdOverride: null,
                out session,
                out error, commonEncounterSources, sideEventContext);
        }

        internal static bool TryRestore(string payload, Func<string, LevelRunProfile> profileResolver,
            bool demo, out DungeonRunSession session, out string error)
            => TryRestore(payload, profileResolver, null, null, null, out session, out error, demo: demo);

        private static bool TryRestore(
            string payload,
            Func<string, LevelRunProfile> profileResolver,
            RunProgressionState runProgression,
            IInstanceIdGenerator instanceIds,
            string runIdOverride,
            out DungeonRunSession session,
            out string error,
            Func<string, CommonEncounterSourceContract> commonEncounterSources = null,
            IDungeonRunSideEventContext sideEventContext = null, bool? demo = null)
        {
            session = null;
            error = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                error = "Run 存档载荷为空。";
                return false;
            }
            if (profileResolver == null)
            {
                error = "缺少楼层配置解析器。";
                return false;
            }

            // A caller-owned progression object must not retain half of a rejected
            // restore (bad cards, floor state or interaction data can fail later).
            RunProgressionSnapshot priorCounters = runProgression?.Capture() ?? default;
            RunCardPlayGrowthSnapshot? priorCardPlayGrowth = runProgression?.CardPlayGrowth.Capture();
            CardUnlockFactsSnapshot priorCardUnlockFacts = runProgression?.CardUnlockFacts.Capture();
            int priorDurationReserve = runProgression?.CrawlerDurationReserve.PendingBonus ?? 0;
            RunAdditionalStatSnapshot priorAdditionalStats = runProgression?.AdditionalStats.Capture() ?? default;
            RunRevivalSnapshot priorRevivalReserve = runProgression?.RevivalReserve.Capture() ?? default;
            try
            {
                DungeonRunSaveData data =
                    JsonUtility.FromJson<DungeonRunSaveData>(payload);
                if (data == null)
                    throw new InvalidOperationException("Run 存档无法解析。");
                if (data.schemaVersion < 1 ||
                    data.schemaVersion > PayloadSchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"Run 存档版本 {data.schemaVersion} 不受支持。" +
                        $"当前版本为 {PayloadSchemaVersion}。");
                }
                if (string.IsNullOrWhiteSpace(data.runId))
                    throw new InvalidOperationException("Run 存档缺少 runId。");
                // New payloads must explicitly state even an unknown diagnostic
                // leader. Legacy payloads remain unknown; used/summoned IDs and
                // mutable Hub selection are not proof of the original leader.
                if (data.schemaVersion >= 22 && (!data.hasLeaderCrawlerState ||
                    !JsonObjectShape.TryGetField(payload, "leaderCrawlerContentId", out string rawLeader) ||
                    string.IsNullOrWhiteSpace(rawLeader) || rawLeader[0] != '"'))
                    throw new InvalidOperationException("Run 存档缺少冻结的领卷英灵身份。");
                string leaderCrawlerContentId = data.schemaVersion >= 22
                    ? ValidateLeaderCrawlerContentId(data.leaderCrawlerContentId) : string.Empty;

                LevelRunProfile first =
                    profileResolver(data.firstFloorContentId);
                LevelRunProfile current =
                    profileResolver(data.currentFloorContentId);
                if (first == null)
                {
                    throw new InvalidOperationException(
                        $"首层配置不存在：{data.firstFloorContentId}。");
                }
                if (current == null)
                {
                    throw new InvalidOperationException(
                        $"当前层配置不存在：{data.currentFloorContentId}。");
                }
                if (!string.Equals(first.contentId, data.firstFloorContentId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(current.contentId, data.currentFloorContentId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("楼层解析器返回了不匹配的首层或当前层身份。");

                bool demoScope = demo ?? DemoReleasePolicy.IsDemoBuild;
                RequireReleaseScope(first, demoScope);
                RequireReleaseScope(current, demoScope);
                if (demoScope)
                {
                    string dungeonIdentity = string.IsNullOrWhiteSpace(first.dungeonContentId)
                        ? first.contentId : first.dungeonContentId;
                    if (!string.IsNullOrEmpty(data.dungeonContentId) &&
                        !string.Equals(data.dungeonContentId, dungeonIdentity, StringComparison.Ordinal))
                        throw new InvalidOperationException("试玩存档的关区身份与真实首层配置不一致。");
                    // Legacy unknown leaders remain unknown; no account/UI inference.
                    if (!string.IsNullOrEmpty(leaderCrawlerContentId) &&
                        !DemoReleasePolicy.AllowsCrawler(leaderCrawlerContentId, true))
                        throw new InvalidOperationException("当前试玩版本不包含存档领卷英灵。");
                    foreach (var card in data.cards ?? new List<DungeonRunCardSaveData>())
                        if (card != null && !DemoReleasePolicy.AllowsCard(card.contentId, true))
                            throw new InvalidOperationException("当前试玩版本不包含存档卡牌。");
                    foreach (var crawler in data.crawlerResidents ?? new List<DungeonRunCrawlerResidenceSaveData>())
                        if (crawler != null && !DemoReleasePolicy.AllowsCrawler(crawler.contentId, true))
                            throw new InvalidOperationException("当前试玩版本不包含存档驻场英灵。");
                    foreach (string crawlerId in data.usedCrawlerContentIds ?? new List<string>())
                        if (!DemoReleasePolicy.AllowsCrawler(crawlerId, true))
                            throw new InvalidOperationException("当前试玩版本不包含存档出战英灵。");
                }

                var visited = new List<LevelRunProfile>();
                var visitedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (LevelRunSaveData historical in data.completedFloorStates ?? new List<LevelRunSaveData>())
                {
                    if (historical == null || string.IsNullOrWhiteSpace(historical.profileContentId) ||
                        !visitedIds.Add(historical.profileContentId))
                        throw new InvalidOperationException("Completed-floor history contains a missing or duplicate identity.");
                    LevelRunProfile profile = profileResolver(historical.profileContentId);
                    if (profile == null || !string.Equals(profile.contentId, historical.profileContentId,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Completed floor cannot be resolved: " + historical.profileContentId);
                    RequireReleaseScope(profile, demoScope);
                    visited.Add(profile);
                }
                if (!visitedIds.Add(current.contentId))
                    throw new InvalidOperationException("Current floor is already in completed-floor history.");
                visited.Add(current);
                DungeonRunExplorationState exploration = DungeonRunExplorationState.Restore(
                    data.exploration, data.runId, visited, requireState: data.schemaVersion >= 15);
                if (data.schemaVersion >= 24) DungeonRunFormationState.RequireWireShape(payload);
                DungeonRunFormationState formations = DungeonRunFormationState.Restore(
                    data.formations, exploration.Seed, visited, requireState: data.schemaVersion >= 16,
                    commonSources: commonEncounterSources, requireVersionTwo: data.schemaVersion >= 24);

                LevelRunSaveCodec.RequireRunDirectoryWire(data, payload);
                var duplicateState = DungeonRunDuplicateOfferingSaveCodec.ReadRun(data, payload);
                var hireState = DungeonRunGunForHireSaveCodec.ReadRun(data, payload);
                var invitationState = DungeonRunInvitationSaveCodec.ReadRun(data, payload);
                LevelRunSaveCodec.RequireLegacyInvitationAdmission(data, visited);
                LevelRunSaveCodec.RequireLegacyAdditionalEventAdmission(data, visited);
                var cardStatState = DungeonRunCardStatOfferingSaveCodec.ReadRun(data, payload);
                LevelRunSaveCodec.RequireLegacyCardStatAdmission(data, visited);

                RunCardPlayGrowthSnapshot savedGrowth = DungeonRunCardPlayGrowthSaveData.Read(data);
                CardUnlockFactsSnapshot savedUnlockFacts = DungeonRunCardUnlockFactsSaveData.Read(data, payload);
                int savedDurationReserve = DungeonRunCrawlerDurationReserveSaveData.Read(data, payload);
                DungeonRunPlayerState savedPlayerState = DungeonRunPlayerState.Restore(data, payload);
                var additional = DungeonRunAdditionalStatsSaveCodec.Read(data, payload, savedPlayerState.CaptureBaseline());
                RunProgressionSnapshot savedCounters = DungeonRunProgressionSaveCodec.Read(data, payload);
                RunProgressionState restoredRunProgression =
                    runProgression ?? new RunProgressionState(savedGrowth.Enabled);
                restoredRunProgression.CardPlayGrowth.Restore(savedGrowth);
                restoredRunProgression.CardUnlockFacts.Restore(savedUnlockFacts);
                restoredRunProgression.CrawlerDurationReserve.Restore(savedDurationReserve);
                restoredRunProgression.AdditionalStats.Restore(additional.Additional);
                restoredRunProgression.RevivalReserve.Restore(additional.Revival);
                restoredRunProgression.Restore(savedCounters);
                var shared = new DungeonRunSharedState(
                    first.levelExperienceRules,
                    restoredRunProgression,
                    data.currentLevel,
                    data.currentExperience,
                    data.baseManaBonus,
                    data.baseArmorPerTurnBonus,
                    data.mightBonus,
                    data.xpFractionCarryUnits,
                    data.handLimitBonus,
                    data.areaBonus,
                    data.rewardRerollsRemaining);
                string restoredRunId = string.IsNullOrWhiteSpace(runIdOverride)
                    ? data.runId
                    : runIdOverride;
                // The explicit headless restore API may rebind the aggregate ID
                // after ReadRun has validated the original persisted identity.
                cardStatState.runId = restoredRunId;
                duplicateState.runId = restoredRunId;
                hireState.runId = restoredRunId;
                if (!string.Equals(invitationState.runId, restoredRunId, StringComparison.Ordinal))
                {
                    if (invitationState.entries.Count != 0)
                        throw new InvalidOperationException("Invitation decisions cannot be rebound to another Run.");
                    invitationState.runId = restoredRunId;
                }
                LevelRunSession floor = LevelRunSession.Restore(
                    current,
                    data.currentFloor,
                    restoredRunId,
                    shared,
                    exploration.ReadFloor(current));
                // Every typed owner reads the restored effective contracts, including
                // historical floors. Author payloads are not frozen-save evidence.
                var restoredFloors = new List<LevelRunSession>();
                for (int i = 0; i < (data.completedFloorStates?.Count ?? 0); i++)
                    restoredFloors.Add(LevelRunSession.Restore(visited[i], data.completedFloorStates[i], restoredRunId,
                        shared.CloneForFloorPreparation(), exploration.ReadFloor(visited[i])));
                restoredFloors.Add(floor);
                DungeonRunSideEventState sideEvents = DungeonRunSideEventState.Restore(null, data, payload, restoredFloors);
                // 外部 DTO 必须在任何正规化/深拷贝之前校验。损坏条目不能因
                // Clone 跳过 null 或 Dictionary 覆盖重复键而被静默“修复”。
                List<DungeonRunCardSaveData> restoredCardRoster = DungeonRunCardCodec.ReadRun(payload, data.schemaVersion);
                DungeonRunDoubleKeyPaymentSaveData doubleKeyPayments =
                    DungeonRunDoubleKeyPayment.ValidateRestored(data, restoredFloors);
                DungeonRunArcanaSaveData arcana = DungeonRunArcanas.ValidateRestored(data, restoredFloors, payload);
                DungeonRunChickenOfferingSaveData chickenOffering = DungeonRunChickenOffering.ValidateRestored(data, restoredFloors, payload);
                DungeonRunManaStatueSaveData manaStatues = DungeonRunManaStatues.ValidateRestored(data, restoredFloors, payload);
                DungeonRunCartSaveData abandonedCarts = DungeonRunAbandonedCarts.ValidateRestored(data, restoredFloors, payload);
                RunManaRepairPickupSaveData manaRepairPickups = RunManaRepairPickupJournal.ValidateRestored(data, restoredFloors, payload);
                DungeonRunChickenOffering.RequireCombinedManaFacts(data, chickenOffering, manaStatues, payload);
                ValidateCrawlerResidents(data.crawlerResidents);
                ValidateStableIds(
                    data.usedCrawlerContentIds,
                    "Run 存档出战英灵");
                if (data.elapsedRunSeconds < 0 || data.dungeonBonusJade < 0 ||
                    (!data.dungeonBonusGranted && data.dungeonBonusJade != 0))
                {
                    throw new InvalidOperationException(
                        "Run 存档包含无效的用时或 Dungeon Bonus。" );
                }
                if ((data.schemaVersion >= 19 && !data.hasLevelBonusState) ||
                    data.levelBonusJade < 0 || (!data.levelBonusGranted && data.levelBonusJade != 0) ||
                    (!data.hasLevelBonusState && (data.levelBonusGranted || data.levelBonusJade != 0)))
                    throw new InvalidOperationException("Run 存档包含无效或缺失的终局层奖回执。");
                DungeonRunConclusion conclusion = DungeonRunConclusion.Restore(data.conclusion);
                bool hasConclusion = conclusion.TryGetSnapshot(out DungeonRunResultSnapshot concluded);
                if (data.dungeonBonusGranted || data.levelBonusGranted ||
                    (hasConclusion && concluded.Status == DungeonRunResultStatus.Victory))
                    sideEvents.RequireVictoryReady(floor);
                if (hasConclusion)
                {
                    if (concluded.LevelBonusJade != data.levelBonusJade ||
                        (concluded.Status == DungeonRunResultStatus.Defeat && data.levelBonusGranted))
                        throw new InvalidOperationException("终局层奖与冻结结果不一致。");
                    if (data.schemaVersion >= 19 && !data.conclusion.hasCardPlayGrowthState)
                        throw new InvalidOperationException("冻结结果缺少百术周天来源快照。");
                }
                ValidateSavedCardReferences(
                    data.cards,
                    data.pendingForcedOpeningHandCardInstanceId,
                    data.sealAssignments);
                List<DungeonRunCardSaveData> cards =
                    DungeonRunCardCodec.CloneCards(restoredCardRoster);
                // Migrate the owned memento before any projection or seal transaction
                // reads it; a resolver-only fallback would leave _cards inconsistent.
                for (int i = 0; i < cards.Count; i++)
                    if (!DungeonRunCardCodec.IsKnown(cards[i]))
                        cards[i].contentId = NormalizeRetiredCardContentId(cards[i].contentId);
                List<DungeonRunCrawlerResidenceSaveData> crawlerResidents =
                    CloneCrawlerResidents(data.crawlerResidents);

                session = new DungeonRunSession(
                    first,
                    floor,
                    shared,
                    restoredFloors.GetRange(0, restoredFloors.Count - 1)
                        .ConvertAll(item => item.CaptureSaveData()),
                    cards,
                    crawlerResidents,
                    savedPlayerState,
                    data.pendingForcedOpeningHandCardInstanceId,
                    data.sealAssignments,
                    instanceIds: instanceIds,
                    conclusion: conclusion,
                    statistics: DungeonRunStatistics.Restore(data.statistics,
                        NormalizeRetiredCardContentId),
                    usedCrawlerContentIds: data.usedCrawlerContentIds,
                    elapsedRunSeconds: data.elapsedRunSeconds,
                    dungeonBonusGranted: data.dungeonBonusGranted,
                    dungeonBonusJade: data.dungeonBonusJade,
                    interaction: data.interaction,
                    exploration: exploration, formations: formations,
                    doubleKeyPayments: doubleKeyPayments,
                    levelBonusGranted: data.levelBonusGranted, levelBonusJade: data.levelBonusJade,
                    leaderCrawlerContentId: leaderCrawlerContentId, arcana: arcana, chickenOffering: chickenOffering, manaStatues: manaStatues,
                    abandonedCarts: abandonedCarts, sideEvents: sideEvents, sideEventContext: sideEventContext,
                    crystalEvents: ReadCrystalState(data, payload),
                    experienceCollections: ReadExperienceCollections(data, payload),
                    gemChestStateJson: ReadGemChestState(data, payload),
                    gemChestJournalVersion: data.schemaVersion >= 38 ? 3 : data.schemaVersion == 33 ? 1 : 2,
                    cardStatOfferingStateJson: DungeonRunCardStatOfferingSaveCodec.Write(cardStatState),
                    duplicateOfferingStateJson: DungeonRunDuplicateOfferingSaveCodec.Write(duplicateState),
                    gunForHireStateJson: DungeonRunGunForHireSaveCodec.Write(hireState),
                    invitationStateJson: DungeonRunInvitationSaveCodec.Write(invitationState),
                    manaRepairPickups: manaRepairPickups);
                session.Crystals.RequireVisitedFloors(restoredFloors);
                session.GemChests.RequireVisitedFloors(restoredFloors);
                session.CardStatOfferings.RequireVisitedFloors(restoredFloors);
                session.DuplicateOfferings.RequireVisitedFloors(restoredFloors);
                session.GunForHire.RequireVisitedFloors(restoredFloors);
                session.Invitations.RequireVisitedFloors(restoredFloors);
                long provenJade = session.CardStatOfferings.ProvenJadeIncome + session.GemChests.ProvenJadeIncome + session.GunForHire.ProvenJadeIncome;
                if (session.RunProgression.RunJadeEarned < provenJade)
                    throw new InvalidOperationException("Combined interaction grants exceed actual Run income.");
                session.ExperienceCollections.RequireVisitedFloors(restoredFloors.ConvertAll(f => f.Profile.contentId));
                foreach (var restoredFloor in restoredFloors)
                {
                    long collected = session.ExperienceCollections.GetFloorTotals(restoredFloor.Profile.contentId).PickupCrystal;
                    long proven = session.Crystals.GetFloorExperienceCollected(restoredFloor.Profile.contentId);
                    if (session.ExperienceCollections.HistoryComplete ? collected != proven : collected > proven)
                        throw new InvalidOperationException("Crystal XP collections contradict the committed floor hit journal.");
                }
                return true;
            }
            catch (Exception exception)
            {
                session = null;
                runProgression?.Restore(priorCounters);
                if (priorCardPlayGrowth.HasValue)
                    runProgression.CardPlayGrowth.Restore(priorCardPlayGrowth.Value);
                if (priorCardUnlockFacts != null)
                    runProgression.CardUnlockFacts.Restore(priorCardUnlockFacts);
                runProgression?.CrawlerDurationReserve.Restore(priorDurationReserve);
                if (runProgression != null)
                {
                    runProgression.AdditionalStats.Restore(priorAdditionalStats);
                    runProgression.RevivalReserve.Restore(priorRevivalReserve);
                }
                error = $"恢复 Run 存档失败：{exception.Message}";
                return false;
            }
        }

        internal DungeonRunSession CreateRestarted(
            RunProgressionState runProgression)
        {
            string snapshotId = _firstFloorProfile.ResolveDefaultSnapshotId();
            return Begin(
                _firstFloorProfile,
                snapshotId,
                Guid.NewGuid().ToString("N"),
                runProgression,
                _instanceIds, leaderCrawlerContentId: LeaderCrawlerContentId,
                commonEncounterSources: _floors.CommonEncounterSources,
                sideEventPlan: SideEvents.Plan, sideEventContext: _floors.SideEventContext);
        }

        private bool TryFreezeConclusion(
            DungeonRunResultStatus status,
            IReadOnlyList<DungeonRunRewardResult> rewards,
            out DungeonRunResultSnapshot snapshot,
            out string error)
        {
            if (IsSpecializedInteractionBusy)
            { snapshot = null; error = "Resolve the active specialized interaction before concluding the Run."; return false; }
            if (GemChests.IsCommitting || GemChests.HasPendingChoice)
            { snapshot = null; error = "Resolve the active Gem chest before concluding the Run."; return false; }
            if (status == DungeonRunResultStatus.Victory &&
                !SideEvents.TryValidateVictoryReadiness(out error))
            { snapshot = null; return false; }
            return _conclusion.TryFreeze(
                status,
                RunId,
                DungeonContentId,
                _firstFloorProfile.displayName,
                CurrentFloor.Profile,
                CurrentFloor,
                _floors.CaptureHistory(),
                _cards,
                CurrentHealth,
                MaxHealth,
                CombatProgression.CurrentLevel,
                CombatProgression.CurrentExperience,
                new DungeonRunFinalStateSnapshot(
                    ElapsedRunSeconds,
                    RunProgression.RunJadeEarned,
                    _dungeonBonusGranted ? _dungeonBonusJade : 0,
                    _shared.BaseManaBonus,
                    _shared.BaseArmorPerTurnBonus,
                    _shared.MightBonus,
                    RunProgression.RunLuckBonus,
                    RunProgression.RunGreedBonus,
                    RunProgression.RunGrowthBonus,
                    _usedCrawlerContentIds,
                    _shared.HandLimitBonus,
                    _shared.AreaBonus,
                    levelBonusJade: _levelBonusGranted ? _levelBonusJade : 0,
                    cardPlayGrowth: RunProgression.CardPlayGrowth.Capture()),
                _statistics.Capture(),
                rewards,
                out snapshot,
                out error);
        }

        private static IReadOnlyList<DungeonRunRewardResult>
            ConvertRewardReceipts(IReadOnlyList<LevelRewardReceipt> rewards)
        {
            if (rewards == null || rewards.Count == 0)
                return Array.Empty<DungeonRunRewardResult>();
            var results = new List<DungeonRunRewardResult>(rewards.Count);
            for (int i = 0; i < rewards.Count; i++)
            {
                LevelRewardReceipt reward = rewards[i];
                results.Add(new DungeonRunRewardResult(
                    reward.GrantContentId,
                    reward.Kind,
                    reward.TargetContentId,
                    reward.DisplayName,
                    reward.NewlyGranted));
            }
            return results;
        }

        private bool HasCompletedFinale()
        {
            IReadOnlyList<LevelNode> nodes = CurrentFloor.Nodes;
            if (nodes == null) return false;
            for (int i = 0; i < nodes.Count; i++)
            {
                LevelNode node = nodes[i];
                if (node != null && node.nodeType == LevelNodeType.Finale &&
                    CurrentFloor.GetNodeState(node.contentId) ==
                    LevelNodeViewState.Completed)
                {
                    return true;
                }
            }
            return false;
        }

        private static void RequireReleaseScope(LevelRunProfile profile, bool demo)
        {
            if (!DemoReleasePolicy.AllowsLevel(profile.contentId, profile.dungeonContentId, demo))
                throw new InvalidOperationException("当前试玩版本不包含此楼层：" + profile.contentId);
        }

        internal bool CanAdvanceCurrentFloor(
            Func<string, LevelRunProfile> profileResolver,
            out LevelRunProfile nextFloor,
            out string error, bool? demo = null)
        {
            nextFloor = null;
            error = null;
            if (IsRewardCommitInProgress || TryGetConclusion(out _) ||
                Interactions.HasPendingChoice || Arcanas.HasPendingChoice || (ChickenOfferings.HasPendingChoice || IsSpecializedInteractionBusy) || Crystals.HasPendingEvent)
            {
                error = "请先完成当前楼层的地图奖励选择及提交。";
                return false;
            }
            try
            {
                string active = CurrentFloor.ActiveInteractionNodeId;
                CurrentFloor.RequireFloorExitReady(
                    !string.IsNullOrWhiteSpace(active) && CurrentFloor.FindNode(active)?.nodeType == LevelNodeType.Exit
                        ? active : null);
            }
            catch (Exception exception) { error = exception.Message; return false; }
            return _floors.TryValidateNext(profileResolver, out nextFloor, out error, demo);
        }

        /// <summary>
        /// Advances this explicit Run through its configured exit, using the
        /// supplied content resolver without reading or replacing a global Run.
        /// </summary>
        public bool TryAdvanceCurrentFloor(
            Func<string, LevelRunProfile> profileResolver,
            out string error)
        {
            error = null;
            string exitNodeId = null;
            IReadOnlyList<LevelNode> effectiveNodes = CurrentFloor.Nodes;
            for (int i = 0; i < effectiveNodes.Count; i++)
            {
                LevelNode node = effectiveNodes[i];
                if (node != null && node.nodeType == LevelNodeType.Exit &&
                    CurrentFloor.GetNodeState(node.contentId) ==
                    LevelNodeViewState.Completed)
                {
                    exitNodeId = node.contentId;
                    break;
                }
            }
            if (exitNodeId == null)
            {
                error = "离层节点尚未完成。";
                return false;
            }
            // Compatibility for already-settled legacy consumers: validate every
            // receipt and use the same commit gate; never bypass pending returns.
            return FloorExits.TryCommit(CurrentFloorContentId, exitNodeId,
                profileResolver, null, out _, out error);
        }

        private static void ValidateSavedCardReferences(
            IReadOnlyList<DungeonRunCardSaveData> cards,
            int pendingForcedOpeningHandCardInstanceId,
            IReadOnlyList<DungeonRunSealAssignmentSaveData> assignments)
        {
            if (pendingForcedOpeningHandCardInstanceId < 0)
                throw new InvalidOperationException(
                    "Run 存档的强制起手牌实例 ID 不能为负数。");

            var cardsById = new Dictionary<int, DungeonRunCardSaveData>();
            if (cards != null)
            {
                for (int i = 0; i < cards.Count; i++)
                    cardsById.Add(cards[i].runtimeInstanceId, cards[i]);
            }
            if (pendingForcedOpeningHandCardInstanceId > 0 &&
                !cardsById.ContainsKey(
                    pendingForcedOpeningHandCardInstanceId))
            {
                throw new InvalidOperationException(
                    "Run 存档的强制起手牌引用不存在的卡牌实例：" +
                    $"{pendingForcedOpeningHandCardInstanceId}。");
            }

            if (assignments == null) return;
            var effectIds = new HashSet<string>(StringComparer.Ordinal);
            var occupiedSlots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < assignments.Count; i++)
            {
                DungeonRunSealAssignmentSaveData assignment = assignments[i];
                if (assignment == null)
                    throw new InvalidOperationException(
                        $"Run 存档第 {i} 个灵印分配为空。");
                if (string.IsNullOrWhiteSpace(assignment.effectId) ||
                    !effectIds.Add(assignment.effectId))
                {
                    throw new InvalidOperationException(
                        "Run 存档包含空白或重复的灵印效果 ID：" +
                        $"{assignment.effectId ?? "<null>"}。");
                }
                if (string.IsNullOrWhiteSpace(assignment.sealContentId))
                    throw new InvalidOperationException(
                        $"灵印效果 {assignment.effectId} 缺少 sealContentId。");
                if (!cardsById.TryGetValue(
                        assignment.cardRuntimeInstanceId,
                        out DungeonRunCardSaveData card))
                {
                    throw new InvalidOperationException(
                        $"灵印效果 {assignment.effectId} 引用不存在的卡牌实例：" +
                        $"{assignment.cardRuntimeInstanceId}。");
                }
                if (!Enum.IsDefined(typeof(DungeonRunSealAssignmentDisposition), assignment.disposition))
                    throw new InvalidOperationException($"灵印效果 {assignment.effectId} 的分配去向无效。");
                if (assignment.disposition == DungeonRunSealAssignmentDisposition.EvolutionConsumed)
                {
                    // 消耗记录描述历史操作，不指向现存印位；后续进化与普通钤印可复用该索引。
                    if (assignment.slotIndex < 0 || assignment.slotIndex >= CardSealRules.MaximumSealSlots ||
                        !card.hasGemSlotCapacity)
                        throw new InvalidOperationException($"灵印效果 {assignment.effectId} 的进化消耗记录无效。");
                    continue;
                }
                if (assignment.slotIndex < 0 || card.seals == null ||
                    assignment.slotIndex >= card.seals.Count)
                {
                    throw new InvalidOperationException(
                        $"灵印效果 {assignment.effectId} 引用无效槽位：" +
                        $"{assignment.slotIndex}。");
                }
                CardSealSpec savedSeal = card.seals[assignment.slotIndex];
                if (!string.Equals(
                        savedSeal.contentId,
                        assignment.sealContentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"灵印效果 {assignment.effectId} 的 sealContentId " +
                        "与卡牌槽位内容不一致。");
                }
                string slotKey = assignment.cardRuntimeInstanceId + ":" +
                    assignment.slotIndex;
                if (!occupiedSlots.Add(slotKey))
                    throw new InvalidOperationException(
                        $"Run 存档中的卡牌槽位 {slotKey} 被重复分配。");
            }
        }

        private static List<DungeonRunCrawlerResidenceSaveData>
            CaptureCrawlerResidents(
                IReadOnlyList<ColorTriggerSystem.ActiveCrawler> crawlers,
                out string error)
        {
            error = null;
            var result = new List<DungeonRunCrawlerResidenceSaveData>();
            if (crawlers == null)
                return result;

            for (int i = 0; i < crawlers.Count; i++)
            {
                ColorTriggerSystem.ActiveCrawler crawler = crawlers[i];
                string contentId = crawler?.config?.contentId;
                if (string.IsNullOrWhiteSpace(contentId))
                {
                    error = $"Run 驻留英灵第 {i} 个实例缺少稳定 " +
                            "contentId。";
                    return null;
                }
                if (crawler.remainingTriggers <= 0 ||
                    crawler.remainingTriggers >
                    ColorTriggerSystem.MaxRemainingTriggers)
                {
                    error = $"Run 驻留英灵 {contentId} 的剩余凝神无效：" +
                            $"{crawler.remainingTriggers}。";
                    return null;
                }
                result.Add(new DungeonRunCrawlerResidenceSaveData
                {
                    contentId = contentId,
                    remainingTriggers = crawler.remainingTriggers
                });
            }
            return result;
        }

        private static void ValidateCrawlerResidents(
            IReadOnlyList<DungeonRunCrawlerResidenceSaveData> crawlers)
        {
            if (crawlers == null)
                return;
            for (int i = 0; i < crawlers.Count; i++)
            {
                DungeonRunCrawlerResidenceSaveData crawler = crawlers[i];
                if (crawler == null ||
                    string.IsNullOrWhiteSpace(crawler.contentId))
                {
                    throw new InvalidOperationException(
                        "Run 存档包含无 contentId 的驻留英灵。");
                }
                if (crawler.remainingTriggers <= 0 ||
                    crawler.remainingTriggers >
                    ColorTriggerSystem.MaxRemainingTriggers)
                {
                    throw new InvalidOperationException(
                        $"Run 存档中英灵 {crawler.contentId} 的" +
                        $"剩余凝神无效：{crawler.remainingTriggers}。");
                }
            }
        }

        private static void AddCards(
            ICollection<CardConfig> target,
            IReadOnlyList<CardConfig> source)
        {
            for (int i = 0; i < source.Count; i++)
                target.Add(source[i]);
        }

        private static string ValidateLeaderCrawlerContentId(string contentId)
        {
            if (string.IsNullOrEmpty(contentId)) return string.Empty;
            foreach (char value in contentId)
                if (char.IsWhiteSpace(value) || char.IsControl(value))
                    throw new ArgumentException("领卷英灵必须使用不含空白或控制字符的稳定ID。", nameof(contentId));
            return contentId;
        }

        private static List<CardSealSpec> CloneSeals(
            IReadOnlyList<CardSealSpec> seals)
        {
            var result = new List<CardSealSpec>();
            if (seals == null)
                return result;
            for (int i = 0; i < seals.Count; i++)
            {
                if (seals[i] != null)
                    result.Add(seals[i].Clone());
            }
            return result;
        }

        // Duplicate definitions were withdrawn after their starter package
        // reused the established cards. Other IDs retain the ordinary missing-source error.
        private static string NormalizeRetiredCardContentId(string contentId)
        {
            if (string.Equals(contentId, "card.xiyou.jiuchidingpa", StringComparison.OrdinalIgnoreCase))
                return "legacy.card.common.4c1505d8";
            if (string.Equals(contentId, "card.xiyou.yunzhanjiashi", StringComparison.OrdinalIgnoreCase))
                return "legacy.card.common.2927e58a";
            if (string.Equals(contentId, "card.xiyou.gengtiankailong", StringComparison.OrdinalIgnoreCase))
                return "legacy.card.common.fe5cd821";
            return contentId;
        }

        private static List<DungeonRunCrawlerResidenceSaveData>
            CloneCrawlerResidents(
                IReadOnlyList<DungeonRunCrawlerResidenceSaveData> crawlers)
        {
            var result = new List<DungeonRunCrawlerResidenceSaveData>();
            if (crawlers == null)
                return result;
            for (int i = 0; i < crawlers.Count; i++)
            {
                DungeonRunCrawlerResidenceSaveData crawler = crawlers[i];
                if (crawler == null)
                    throw new InvalidOperationException(
                        $"Run 存档第 {i} 个驻留英灵为空。");
                result.Add(new DungeonRunCrawlerResidenceSaveData
                {
                    contentId = crawler.contentId,
                    remainingTriggers = crawler.remainingTriggers
                });
            }
            return result;
        }

        private static IReadOnlyList<CrawlerResidenceSnapshot>
            CreateCrawlerResidenceSnapshots(
                IReadOnlyList<DungeonRunCrawlerResidenceSaveData> crawlers)
        {
            if (crawlers == null || crawlers.Count == 0)
                return Array.Empty<CrawlerResidenceSnapshot>();

            var result = new CrawlerResidenceSnapshot[crawlers.Count];
            for (int i = 0; i < crawlers.Count; i++)
            {
                DungeonRunCrawlerResidenceSaveData crawler = crawlers[i];
                result[i] = new CrawlerResidenceSnapshot(
                    crawler.contentId,
                    crawler.remainingTriggers);
            }
            return result;
        }

        private static Dictionary<string, DungeonRunSealAssignmentSaveData>
            BuildSealAssignments(
                IReadOnlyList<DungeonRunSealAssignmentSaveData> assignments)
        {
            var result = new Dictionary<string,
                DungeonRunSealAssignmentSaveData>(
                StringComparer.Ordinal);
            if (assignments == null)
                return result;
            for (int i = 0; i < assignments.Count; i++)
            {
                DungeonRunSealAssignmentSaveData source = assignments[i];
                if (source == null || string.IsNullOrWhiteSpace(source.effectId))
                    throw new InvalidOperationException(
                        $"Run 存档第 {i} 个灵印分配无效。");
                result.Add(source.effectId, new DungeonRunSealAssignmentSaveData
                {
                    effectId = source.effectId,
                    sealContentId = source.sealContentId,
                    cardRuntimeInstanceId = source.cardRuntimeInstanceId,
                    slotIndex = source.slotIndex,
                    disposition = source.disposition
                });
            }
            return result;
        }

        private static List<DungeonRunSealAssignmentSaveData>
            CloneSealAssignments(
                IReadOnlyDictionary<string,
                    DungeonRunSealAssignmentSaveData> assignments)
        {
            var result = new List<DungeonRunSealAssignmentSaveData>();
            if (assignments == null)
                return result;
            foreach (DungeonRunSealAssignmentSaveData source in
                     assignments.Values)
            {
                if (source == null)
                    continue;
                result.Add(new DungeonRunSealAssignmentSaveData
                {
                    effectId = source.effectId,
                    sealContentId = source.sealContentId,
                    cardRuntimeInstanceId = source.cardRuntimeInstanceId,
                    slotIndex = source.slotIndex,
                    disposition = source.disposition
                });
            }
            result.Sort((left, right) => string.CompareOrdinal(
                left.effectId, right.effectId));
            return result;
        }

        private static List<string> CopyStableIds(
            IReadOnlyList<string> source)
        {
            var result = new List<string>();
            if (source == null)
                return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                string contentId = source[i]?.Trim();
                if (string.IsNullOrWhiteSpace(contentId) ||
                    !seen.Add(contentId))
                {
                    continue;
                }
                result.Add(contentId);
            }
            return result;
        }

        private static bool ContainsStableId(
            IReadOnlyList<string> source,
            string contentId)
        {
            if (source == null || string.IsNullOrWhiteSpace(contentId))
                return false;
            for (int i = 0; i < source.Count; i++)
            {
                if (string.Equals(
                        source[i],
                        contentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void ValidateStableIds(
            IReadOnlyList<string> source,
            string label)
        {
            if (source == null)
                return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                string contentId = source[i]?.Trim();
                if (string.IsNullOrWhiteSpace(contentId) ||
                    !seen.Add(contentId))
                {
                    throw new InvalidOperationException(
                        $"{label}第 {i} 个稳定 ID 为空或重复。");
                }
            }
        }
    }

}
