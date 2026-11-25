using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 增益预览结果（不实际执行，只计算假设值）
    /// </summary>
    public struct BuffPreview
    {
        public StatType statType;
        public int currentValue;
        public int projectedValue;
        public int delta;
    }

    /// <summary>
    /// 测试驱动对死亡后 roster 的显式处置。仅对 Core 友元测试程序集
    /// 可见，正式游戏必须通过 TryPlayCard。
    /// </summary>
    internal enum CombatHarnessDefeatMode
    {
        ResetForReplay,
        KeepDefeated
    }

    /// <summary>
    /// 战斗控制器
    /// 职责：回合管理、子系统协调、事件分发
    /// 效果编排 → CombatController，牌区管理 → DeckManager，攻击计划与伤害
    /// 写入 → AttackPlanBuilder / AttackResolver / DamageSystem。
    ///
    /// 规模审查：本存量协调器已超过 1200 行。本次没有把 BOSS 资格或部署规则
    /// 继续写入此处；这些状态由 LevelBossEncounter 唯一拥有，本类只在既有完整
    /// 结算边界请求到达并发布类型化事件。后续 BOSS 规则不得回流到本类。
    /// 撤离是既有敌人行动结算的一种结果；持久状态归 EnemyStats，补位归
    /// EnemyBackfillCoordinator，本类不持有倒计时或新的独立生命周期。
    /// 生产统计由 CombatStatisticsTracker 唯一持有，本类只在既有结算事件边界
    /// 提交卡牌、伤害、击杀、治疗与英灵触发事实。
    /// 2026-09-05 限定接入：灵印种类、顺序与倍率由 CardSealRules 拥有；
    /// 本类仅在既有整卡结束边界执行后置效果并合并回执，不新增长期可变状态。
    /// 本次保留现有执行边界；下次扩充卡牌结算阶段前须先评估整段效果执行的独立封装。
    /// G3F1复核：回手次数归CardInstance、结算期牌区归DeckManager、叠势保留归
    /// ComboSystem、印效果归CardSealRules、冻结目标集归AttackTargetSelector。
    /// 此处只提交已有阶段边界及执行标准Damage，不新增印计数、选敌或独立状态。
    /// G3F2复核：在手光环由CardSealRules只读投影，动态后置攻击由
    /// CardSealPostPlayAction冻结规则与成本；本类仅在已有牌离手/攻击边界
    /// 向经济owner提交收益、提交当前护甲成本。不另存光环或灵印监听生命周期。
    /// 化形接入复用既有奖励提交边界：局部规则归 CardSealRules，实体替换归
    /// DeckManager；本类只维持既有当回合免耗引用和发布门禁委托，不持有进化路线。
    /// 假设投影的限定接入：仅提供独占副本上排除未知敌人的原子入口，复用既有
    /// 敌人部署与时钟资格所有者，不加入搜索/评分/回放知识。冻结的时钟资格只存在
    /// 于一次性假设副本；后续建设完整分支快照时将此入口一起迁入专用状态投影边界。
    /// Garlic 原合同的意图取消由 EnemyAiSystem 唯一拥有；本类只在既有逐效果边界
    /// 提交取消命令，行动执行入口拒绝已取消的缓存命令，不增加控制状态或选敌规则。
    /// 完整入场接入只查询 EnemyFormationLayout 并在既有边界发布补位；
    /// 空场恢复与击退归位仍由 EnemyBackfillCoordinator 唯一调度，不新增本类状态。
    /// 周期冻结源刷新、鸟源累积/计次/恢复归PeriodicAttackSystem，Erase行快照归EraseResolver；
    /// 本类只提交基础攻击与回合/终局边界并复用既有攻击回执消费器。下次独立规则仍须下沉。
    /// 一次列补打的队列/清空/当前属性计划归ColumnReplayAttackSystem；本类只接既有边界，
    /// 不把其列记录混入长期鸟源，不新增独立规则状态。
    /// 鸟初击数学与固定鸟数归AttackPlanBuilder；本类仍只提交已有Damage结算边界。
    /// Agent观测新增只读覆盖查询：覆盖知识归CardPlayDamagePreview，无新增状态或执行责任；
    /// 支持效果投影归独立CardSupportEffectProjection。下一次新增独立规则仍须下沉。
    /// 基础主伤显示归CardAttackDisplayProjection；即时实际扣除汇总归
    /// CardImmediateDamageDisplaySummary，整手牌随机预告及状态匹配由
    /// CardAttackPreviewSession唯一拥有。本类仅提交既有决策/结算边界，
    /// 不保留另一份预览缓存；后续复合效果投影仍须下沉独立owner。
    /// 周期首击计划通知是既有攻击结算边界的只读输出，不新增规则、状态或表现生命周期；
    /// 即使首击清场未登记持续源，消费者仍能读取已使用的权威计划。
    /// 敌回合队列及经验/奖励暂停由EnemyTurnSequence独占；本类保留既有行动副作用。
    /// Copy接入复核：本类无复制状态；只在真实使用的既有后置印位提交牌区事务、
    /// 发布已提交回执。模板/强制销毁归CardInstanceFactory与实例，ID及牌区归DeckManager。
    /// 英灵抽牌接入复核：颜色资格、次数及倍率归ColorTriggerSystem，本类仅消费
    /// 类型化基础张数；引卷与实际牌区变化归DeckManager，不新增规则或可变状态。
    /// G3三印接入：留手归DeckManager正常弃牌事务；逆行资格归费用解析与ComboSystem；
    /// 掠夺标记/去重/逐来源经济结算归MugMarkSystem。本类仅把基础攻击、死亡、撤离
    /// 与终局事实交给owner，不新增标记集合；后续灵印不得继续向本类加入独立状态。
    /// 通用英灵续时与单次储备归ColorTriggerSystem，真实死亡累计归RunCardUnlockFacts；
    /// 本轮只在既有效果/死亡边界提交命令，不扩张控制器的可变状态所有权。
    /// 扰灵回执复核：费用差值由DeckManager在既有写入处采集，本类只将本次调用
    /// 的局部只读结果并入整卡回执；不新增费用规则、随机或表现状态。
    /// 续时表现回执只在既有逐效果与整卡边界汇聚；封顶、资格、写入仍由
    /// ColorTriggerSystem 独占，本类不重新计算增量或持有表现状态。
    /// 抽到自动使用复核：沿用同一出牌事务，以局部来源决定是否提交支付、
    /// 玩家叠势、英灵及Boss计时；不增加第二个效果执行器或长期状态。
    /// 自动使用标记归内容，费用修正归CardInstance，批次牌区归DeckManager。
    /// 本次仅补已有抽牌边界；后续新增自动使用模式前先提取完整出牌事务。
    /// </summary>
    public class CombatController
    {
        // 相生数值由结算与只读预览共用，不额外持有表现状态。
        private const int WuxingMightGain = 10;
        private const int WuxingDurationGain = 1;
        private enum DamageDefeatPublicationPolicy
        {
            AuthoritativeCombat,
            HarnessResetForReplay,
            HarnessKeepDefeated
        }

        private ManaSystem _mana;
        private ComboSystem _combo;
        private ColorTriggerSystem _colorTrigger;
        private BossTimer _bossTimer;
        private LevelUpSystem _levelUp;
        private PlayerStats _player;
        private List<EnemyStats> _enemies;
        private readonly EnemyBackfillCoordinator _enemyBackfill;
        private readonly LevelBossEncounter _levelBossEncounter;
        private readonly BossOverkillSession _bossOverkill;
        private DeckManager _deckMgr;
        private readonly CombatRuleSet _rules;
        private readonly RunProgressionState _runState;
        private readonly PlayerDamageReceiver _playerDamageReceiver;
        private readonly EnemyDeathActionResolver _enemyDeathActions;
        private readonly CombatProgressionState _progression;
        private readonly EconomySystem _economy;
        private readonly EncounterRecovery _encounterRecovery;
        private readonly Func<string, CrawlerConfig> _crawlerResolver;
        private readonly Func<string, CardConfig> _cardResolver;
        private readonly Func<string, bool> _isEvolutionResultEligible;
        private readonly IRewardRerollBank _rewardRerollBank;
        private readonly Func<bool> _canReceiveSeals;
        private readonly CombatStatisticsTracker _statistics =
            new CombatStatisticsTracker();
        private readonly CardAttackPreviewSession _attackPreviews;
        private AttackProjection _executingAttackProjection;
        private bool _stageClearJadeGranted;
        private bool _hasLastPlayedCardColor;
        private bool? _hypotheticalBossTimerActive;

        /// <summary>上一张打出牌的颜色（用于五行相生判断）</summary>
        private CardColor _lastPlayedCardColor = CardColor.Yang;

        // 终局幂等：保证 OnCombatEnded 每场只发布一次
        private CombatOutcome? _publishedOutcome;

        // 升级（XP/Level/RewardSelection 统一存储在 State 中，UI 通过属性读取）
        private List<LevelUpSystem.Reward> _pendingRewards;
        private LevelUpSystem.Reward _pendingSealReward;
        private bool _pendingRewardsAllowCashOut = true;
        // A reward group is a short-lived offer, not a persistent Run value.
        // Keep a monotonic identity beside the list so delayed presentation
        // callbacks cannot silently target a later reroll that happens to
        // contain an equivalent value object.
        private long _nextRewardOfferRevision;
        private long _pendingRewardOfferRevision;

        private sealed class PendingExperienceAward
        {
            public CombatExperienceAward Award;
            public int RemainingExperience;
        }

        private readonly Queue<PendingExperienceAward> _pendingExperienceAwards =
            new Queue<PendingExperienceAward>();
        private ExperienceAwardCommitMode _experienceAwardCommitMode =
            ExperienceAwardCommitMode.Immediate;
        private long _nextExperienceAwardId;
        private long _nextFormationAdvanceId = 1;
        private int _diagKillSequence;
        private bool _awaitingDeferredExperienceReward;

        // Queue ownership includes suspension across turn-end XP/reward checkpoints.
        private readonly EnemyTurnSequence _enemyTurnSequence;

        // 教学强制起手：首回合开始前按 contentId 依次定向入手。
        private IReadOnlyList<CardConfig> _forcedOpeningHand;
        // 升级奖励"当回合免费"的实例；成功出牌立即消费，未出牌的则在回合末清除。
        private readonly List<CardConfig> _freeRewardCardsThisTurn =
            new List<CardConfig>();

        // ── 统一战斗状态 ──
        public CombatState State { get; }

        // 事件
        public event Action<string> OnLog;
        public event Action<CombatStateChangeReason> OnStateChanged;
        /// <summary>After resolution has returned to a player decision and all
        /// immediate attack choices are frozen. CardPlayed remains before terminal
        /// cleanup for rule observers; display readers use this settled boundary.</summary>
        public event Action OnDamagePreviewsReady;
        public event Action<CombatCardResolvedEvent> OnCardPlayResolved;
        public event Action<CombatRuleTriggeredEvent> OnRuleTriggered;
        /// <summary>Actual complete-card commits only; never harness, preview or automatic triggers.</summary>
        public event Action<RunCardPlayGrowthReceipt> OnCardPlayGrowthCommitted;
        public event Action<CombatCrawlerTriggeredEvent> OnCrawlerTriggered;
        public event Action<CombatCardsDrawnEvent> OnCardsDrawn;
        public event Action<CombatCardsCopiedEvent> OnCardsCopied;
        public event Action OnTurnEnd;
        public event Action<EnemyStats> OnEnemyKilled;
        /// <summary>
        /// 整批权威死亡已写入，尚未发布控制位移或死亡引起的奖励变化。
        /// 消费者可一次登记全部死亡身份；此通知不代表命中已显现或占位已腾空。
        /// </summary>
        public event Action<IReadOnlyList<EnemyDefeatResult>> OnEnemyDefeatsPrepared;
        public event Action<EnemyDefeatResult> OnEnemyDefeated;
        public event Action<EnemyWithdrawnEvent> OnEnemyWithdrawn;
        /// <summary>其余敌人全灭且完整结算结束后，关 BOSS 首次部署。</summary>
        public event Action<LevelBossArrivalEvent> OnLevelBossArrived;
        public event Action<CombatEnemyActionResult> OnEnemyActionResolved;
        /// <summary>
        /// An enemy experience credit was created. In deferred mode the
        /// credit is authoritative but has not entered CurrentXP yet.
        /// </summary>
        public event Action<CombatExperienceAward> OnExperienceAwarded;
        public event Action<int> OnLevelUp;
        /// <summary>战斗终局事件。每场只发布一次，顺序固定：设置最终 BattlePhase → 胜利收益 → OnStateChanged(CombatEnded) → OnCombatEnded。</summary>
        public event Action<CombatEndedEvent> OnCombatEnded;

        // 视觉事件
        /// <summary>卡牌打出（出牌瞬间）</summary>
        public event Action<CardConfig, int> OnCardPlayedVFX;
        /// <summary>卡牌结算完成（卡牌、目标索引、伤害、是否连击、是否暴击）</summary>
        public event Action<CardConfig, int, int, bool, bool> OnCardResolved;
        /// <summary>敌人受击（敌人索引、伤害、是否暴击、是否击杀）</summary>
        public event Action<int, int, bool, bool> OnEnemyDamaged;
        /// <summary>逐 DamageEvent 的权威事件；旧 OnEnemyDamaged 仅作表现兼容。</summary>
        public event Action<DamageEvent> OnDamageEvent;
        /// <summary>
        /// 已选择的只读攻击计划，先于本批实际伤害发布；正式与测试直结共用。
        /// 表现据此读取完整攻击物数，不能由提前清场后的实际命中数反算。
        /// 仅追加既有攻击边界通知，不保存表现状态或新增结算所有者。
        /// </summary>
        public event Action<AttackPlan> OnAttackPlanResolved;
        /// <summary>地形放置、触发与消退的权威快照；表现层不得直接修改地形实例。</summary>
        public event Action<TerrainEffectChange> OnTerrainEffectChanged;
        public event Action<ProjectileTraceBatch> OnProjectileTracesResolved;

        public event Action<PeriodicAttackEvent> OnPeriodicAttackChanged;
        /// <summary>真实周期牌首击已使用的只读计划；在伤害发布后、持续源登记前通知，包括首击清场。</summary>
        public event Action<CardConfig, AttackPlan> OnPeriodicAttackPlanResolved;
        /// <summary>本回合一次列补打回执，先于同批标准DamageEvent发布，供正式/F7表现共用。</summary>
        public event Action<ColumnReplayAttackEvent> OnColumnReplayAttackResolved;
        public event Action<EraseResolutionResult> OnEraseResolved;
        /// <summary>A staged enemy turn resumes after all pending experience/rewards settle.</summary>
        public event Action OnEnemyTurnReady;
        /// <summary>
        /// Immutable authoritative formation movement. The event contains the
        /// pre- and post-mutation footprints captured by Core.
        /// </summary>
        public event Action<FormationAdvanceEvent> OnFormationAdvanced;
        /// <summary>击退：敌人被推后一格（被推的敌人索引列表，按级联顺序）</summary>
        public event Action<List<int>> OnEnemiesKnockedBack;
        /// <summary>Immutable per-hit control facts; consumers never reconstruct moves from the already-final roster.</summary>
        public event Action<ControlEffectResult> OnControlEffectResolved;
        /// <summary>升级三选一奖励生成（等待玩家选择）</summary>
        public event Action<List<LevelUpSystem.Reward>> OnLevelUpRewardsGenerated;

        public ComboSystem Combo => State.Combo;
        public ManaSystem Mana => State.Mana;
        public PlayerStats Player => State.Player;
        public IReadOnlyList<EnemyStats> Enemies => State.Enemies;
        public IReadOnlyList<CardConfig> Hand => State.Hand;
        public IReadOnlyList<CardConfig> Deck => State.DrawPile;
        public IReadOnlyList<CardConfig> Discard => State.DiscardPile;
        public BossTimer BossTimer => State.BossTimer;
        public bool HasWaitingLevelBoss =>
            _levelBossEncounter != null && _levelBossEncounter.IsWaiting;
        public bool IsBossTimerActive =>
            _hypotheticalBossTimerActive ?? (BossTimer != null && !HasWaitingLevelBoss);
        public ColorTriggerSystem ColorTrigger => State.ColorTrigger;
        public EconomySystem Economy => _economy;
        public RunProgressionState RunProgression => _runState;
        public CombatStatisticsSnapshot Statistics => _statistics.Capture();
        public int CurrentLevel { get => State.CurrentLevel; private set => State.CurrentLevel = value; }
        public int CurrentXP { get => State.CurrentXP; private set => State.CurrentXP = value; }
        public int RequiredXP =>
            _progression.GetRequiredExperience(State.CurrentLevel);

        /// <summary>
        /// Restricts an exclusively owned, already reconstructed hypothetical
        /// combat to known actors. Unknown space is modeled as empty. This is
        /// destructive on that copy and must never be applied to the live Run.
        /// Known IDs are validated before mutation; excluding an unseen waiting
        /// boss cannot activate a timer that was not previously visible.
        /// </summary>
        public void ApplyKnownEnemyHypothesis(IReadOnlyCollection<int> knownRuntimeIds)
        {
            if (knownRuntimeIds == null) throw new ArgumentNullException(nameof(knownRuntimeIds));
            if (State.CurrentTurn <= 0 || _hypotheticalBossTimerActive.HasValue)
                throw new InvalidOperationException("A hypothetical roster requires a started, unprojected copy.");
            var known = new HashSet<int>(knownRuntimeIds);
            var unresolved = new HashSet<int>(known);
            foreach (EnemyStats enemy in _enemies) unresolved.Remove(enemy.runtimeId);
            if (unresolved.Count != 0) throw new ArgumentException("A known actor is absent from the copied roster.");
            bool timerWasActive = IsBossTimerActive;
            bool changedRoster = false;
            foreach (EnemyStats enemy in _enemies)
                if (!known.Contains(enemy.runtimeId))
                {
                    // Observation omits ordinary defeated/withdrawn actors. They
                    // already cannot affect a target or spawn, so do not change
                    // their stamps and reroll a fully known live battlefield.
                    if (enemy.IsWithdrawn || (enemy.IsDead && !enemy.isOverkillTarget))
                        continue;
                    changedRoster |= !enemy.IsWithdrawn || enemy.IsDeployed ||
                        enemy.isOverkillTarget || enemy.isDisarmed || enemy.justFilledFrontRow;
                    enemy.ExcludeFromHypothesis();
                }
            _hypotheticalBossTimerActive = timerWasActive;
            if (changedRoster) _attackPreviews.MarkIncompleteBattlefield();
            // Reconstructing an all-known battlefield must preserve the already
            // recorded current choices. Actual exclusions invalidate the owner's
            // enemy stamps naturally; changing a hypothetical future seed alone
            // must not reroll the next card's previously promised attack.
        }
        public int DeckCount => State.DeckCount;
        public int DiscardCount => State.DiscardCount;
        public int HandCount => State.HandCount;
        public bool AwaitingRewardSelection { get => State.AwaitingRewardSelection; private set => State.AwaitingRewardSelection = value; }
        /// <summary>当前升级候选的只读视图；候选对象仅由本次战斗生成。 </summary>
        public IReadOnlyList<LevelUpSystem.Reward> PendingLevelUpRewards =>
            _pendingRewards != null
                ? (IReadOnlyList<LevelUpSystem.Reward>)_pendingRewards
                : Array.Empty<LevelUpSystem.Reward>();
        /// <summary>
        /// Monotonic identity of the currently visible reward group.  It is
        /// zero when no reward choice is pending and is intentionally not
        /// persisted with the Run.
        /// </summary>
        public long PendingLevelUpRewardRevision =>
            State != null && State.AwaitingRewardSelection &&
            _pendingRewards != null
                ? _pendingRewardOfferRevision
                : 0L;
        /// <summary>当前组是否允许走化玉分支，由作者 Offer 明确控制。 </summary>
        public bool CanCashOutPendingRewards =>
            State.AwaitingRewardSelection &&
            _pendingSealReward == null &&
            _pendingRewardsAllowCashOut;
        /// <summary>选中灵印后尚未完成宿主选择的二阶段状态。 </summary>
        public bool HasPendingLevelUpSealAssignment =>
            _pendingSealReward?.seal != null;
        public CardSealSpec PendingLevelUpSeal =>
            _pendingSealReward?.seal?.Clone();
        public ExperienceAwardCommitMode ExperienceAwardCommitMode =>
            _experienceAwardCommitMode;
        public int PendingExperienceAwardCount =>
            _pendingExperienceAwards.Count;
        public bool HasPendingExperienceAwards =>
            _pendingExperienceAwards.Count > 0;
        public bool IsEnemyTurnSequenceActive =>
            _enemyTurnSequence.IsActive;
        public int PendingEnemyActionCount =>
            _enemyTurnSequence.PendingCount;
        public int CurrentLuck => _runState.ProjectLuck(_player.tianYun);
        public int CurrentGreed => _runState.ProjectGreed(_player.lianYu);
        public int CurrentGrowth => _runState.ProjectGrowth(_player.mingWu);

        /// <summary>当前连击计数（0=无连击），用于UI显示</summary>
        public int ComboCount => State.ComboCount;

        /// <summary>连击是否激活（已起链，第一张牌打出后即为true）</summary>
        public bool IsComboActive => State.Combo.IsActive;

        /// <summary>上一张打出牌的颜色</summary>
        public CardColor LastPlayedCardColor => _lastPlayedCardColor;

        /// <summary>
        /// 判断五行相生是否成立：from 生 to
        /// 木生火 → 火生土 → 土生金 → 金生水 → 水生木
        /// </summary>
        public static bool IsWuxingGenerating(CardColor from, CardColor to)
        {
            return (from, to) switch
            {
                (CardColor.Mu, CardColor.Huo) => true,
                (CardColor.Huo, CardColor.Tu) => true,
                (CardColor.Tu, CardColor.Jin) => true,
                (CardColor.Jin, CardColor.Shui) => true,
                (CardColor.Shui, CardColor.Mu) => true,
                _ => false,
            };
        }

        /// <summary>
        /// 获取五行“生”关系的后续颜色（前 → 后）
        /// </summary>
        public static bool TryGetWuxingSuccessor(CardColor from, out CardColor toColor)
        {
            toColor = from;
            return from switch
            {
                CardColor.Mu => Set(ref toColor, CardColor.Huo),
                CardColor.Huo => Set(ref toColor, CardColor.Tu),
                CardColor.Tu => Set(ref toColor, CardColor.Jin),
                CardColor.Jin => Set(ref toColor, CardColor.Shui),
                CardColor.Shui => Set(ref toColor, CardColor.Mu),
                _ => false,
            };
        }

        /// <summary>
        /// 获取五行“生”关系的前置颜色（后 → 前）
        /// </summary>
        public static bool TryGetWuxingPredecessor(CardColor to, out CardColor fromColor)
        {
            fromColor = to;
            return to switch
            {
                CardColor.Huo => Set(ref fromColor, CardColor.Mu),
                CardColor.Tu => Set(ref fromColor, CardColor.Huo),
                CardColor.Jin => Set(ref fromColor, CardColor.Tu),
                CardColor.Shui => Set(ref fromColor, CardColor.Jin),
                CardColor.Mu => Set(ref fromColor, CardColor.Shui),
                _ => false,
            };
        }

        private static bool Set<T>(ref T value, T next)
        {
            value = next;
            return true;
        }

        /// <summary>
        /// 解析当前可触发的五行对（含阳牌替代关系）并返回前置/后置色。
        /// </summary>
        private bool TryResolveWuxingPair(
            CardConfig card, out CardColor fromColor, out CardColor toColor)
        {
            fromColor = CardColor.Yang;
            toColor = CardColor.Yang;

            if (!_rules.wuxingEnabled || card == null || !_hasLastPlayedCardColor)
                return false;

            if (_lastPlayedCardColor == CardColor.Yang && card.color != CardColor.Yang)
            {
                if (!TryGetWuxingPredecessor(card.color, out fromColor))
                    return false;
                toColor = card.color;
                return true;
            }

            if (_lastPlayedCardColor != CardColor.Yang && card.color == CardColor.Yang)
            {
                if (!TryGetWuxingSuccessor(_lastPlayedCardColor, out toColor))
                    return false;
                fromColor = _lastPlayedCardColor;
                return true;
            }

            if (IsWuxingGenerating(_lastPlayedCardColor, card.color))
            {
                fromColor = _lastPlayedCardColor;
                toColor = card.color;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 供 UI hover 使用的有效五行增益解析（不改写状态）。
        /// 返手冻结叠势时，木生火没有实际奖励；其它相生不受影响。
        /// </summary>
        public bool TryResolveWuxingPairForPreview(
            CardConfig card, out CardColor fromColor, out CardColor toColor)
        {
            if (!TryResolveWuxingPair(card, out fromColor, out toColor))
                return false;
            // 复用牌区 owner 的返手资格，与 ApplyWuxingIfTriggered 的冻结边界一致。
            // 这里只收紧既有只读投影，不新增结算状态或修改颜色牌序。
            return fromColor != CardColor.Mu || !_deckMgr.WillReturnOnPlay(card);
        }

        /// <summary>
        /// 判断打出这张牌是否会触发五行相生（上一张牌颜色生这张牌颜色）
        /// </summary>
        public bool WouldTriggerWuxing(CardConfig card)
        {
            return TryResolveWuxingPairForPreview(card, out _, out _);
        }

        /// <summary>
        /// 五行相生效果：在当前卡牌效果全部结算完毕后调用。
        /// 木→火 Combo+1 / 火→土 锋锐+10% / 土→金 驻灵+1 / 金→水 +1灵力 / 水→木 抽1牌
        /// </summary>
        private int ApplyWuxingIfTriggered(bool triggered, CardColor fromColor,
            List<CrawlerDurationChange> resolvedCrawlerDurationChanges,
            bool preserveCombo = false)
        {
            if (!triggered)
                return 0;

            int durationReserveGained = 0;

            switch (fromColor)
            {
                case CardColor.Mu:
                    if (preserveCombo) break;
                    _combo.AddWuxingBonus(1);
                    OnLog?.Invoke($"★ 五行相生：木生火 · 连击+1 → ×{_combo.Combo + 1}");
                    break;
                case CardColor.Huo:
                    _player.fengRui += WuxingMightGain;
                    OnLog?.Invoke($"★ 五行相生：火生土 · 锋锐+{WuxingMightGain}%");
                    break;
                case CardColor.Tu:
                    durationReserveGained = ApplyCrawlerDurationGain(
                        WuxingDurationGain, resolvedCrawlerDurationChanges);
                    OnLog?.Invoke($"★ 五行相生：土生金 · 驻灵+{WuxingDurationGain}");
                    break;
                case CardColor.Jin:
                    _mana.AddTemporaryMana(1);
                    OnLog?.Invoke($"★ 五行相生：金生水 · +1灵力");
                    break;
                case CardColor.Shui:
                    DrawCardsInternal(1);
                    OnLog?.Invoke($"★ 五行相生：水生木 · 抽1牌");
                    break;
            }
            return durationReserveGained;
        }

        /// <summary>
        /// 查询某张手牌的连击预览（不实际出牌）。
        /// UI 可用此方法在 hover 时显示"会连击/断链"。
        /// </summary>
        public ComboPreview GetComboPreview(CardConfig card)
        {
            var resolved = card.ResolveCost();
            return _combo.PreviewPlayCard(resolved, _deckMgr.WillReturnOnPlay(card));
        }

        /// <summary>
        /// 预览卡牌打出的属性变化（不实际执行）。
        /// 供 UI hover 显示增益图标预估值。
        /// </summary>
        public List<BuffPreview> GetBuffPreview(CardConfig card)
        {
            if (card == null) return new List<BuffPreview>();
            // 完整投影拥有独立状态；控制器只提供既有规则快照与相生判断。
            return new CardBuffProjection(State, _runState, _economy.Config.runGreedMax,
                _crawlerResolver).Project(card, GetComboPreview(card).ResultingMultiplier,
                    GetWuxingGainForBuffPreview(card));
        }

        /// <summary>
        /// 预览出牌前既有英灵的个人剩余次数。Key 为实例 RuntimeId，
        /// Value 为投影后次数；本次离场返回 0，新召唤实例不计入。
        /// </summary>
        public IReadOnlyList<KeyValuePair<int, int>> GetCrawlerDurationPreview(CardConfig card)
        {
            if (card == null) return Array.Empty<KeyValuePair<int, int>>();
            return new CardBuffProjection(State, _runState, _economy.Config.runGreedMax,
                _crawlerResolver).ProjectCrawlerDurations(card, GetComboPreview(card).ResultingMultiplier,
                    GetWuxingGainForBuffPreview(card));
        }

        private KeyValuePair<StatType, int>? GetWuxingGainForBuffPreview(CardConfig card)
        {
            KeyValuePair<StatType, int>? wuxingGain = null;
            if (TryResolveWuxingPairForPreview(card, out CardColor from, out _))
            {
                if (from == CardColor.Huo)
                    wuxingGain = new KeyValuePair<StatType, int>(StatType.FengRui, WuxingMightGain);
                else if (from == CardColor.Tu)
                    wuxingGain = new KeyValuePair<StatType, int>(StatType.NingShen, WuxingDurationGain);
            }
            return wuxingGain;
        }

        /// <summary>
        /// 查询某张牌会触发的在场英灵及其增量，不消耗凝神。
        /// Hover 和其他只读表现必须走此入口，不能复制颜色触发规则。
        /// </summary>
        public List<CrawlerTriggerPreview> GetCrawlerTriggerPreviews(
            CardConfig card)
        {
            var previews = new List<CrawlerTriggerPreview>();
            if (card == null)
                return previews;

            int multiplier = Mathf.Max(
                1,
                GetComboPreview(card).ResultingMultiplier);
            List<ColorTriggerSystem.TriggerResult> triggerResults =
                _colorTrigger.PreviewCardPlayed(card, multiplier);
            for (int i = 0; i < triggerResults.Count; i++)
            {
                ColorTriggerSystem.TriggerResult trigger = triggerResults[i];
                CrawlerConfig crawler = trigger.Crawler?.config;
                previews.Add(new CrawlerTriggerPreview(
                    trigger.Crawler?.RuntimeId ?? 0,
                    crawler?.contentId,
                    crawler?.crawlerName,
                    new List<KeyValuePair<StatType, int>>(
                        trigger.StatDeltas)));
            }
            return previews;
        }

        /// <summary>
        /// 单一攻击首次查询在克隆战场上缓存投影，重复查询不重掷，实际出牌
        /// 消费同一份随机计划。含未知抽牌或后置攻击的复合牌则返回显式估计，
        /// 不预先消费随机数或保留可误用于主攻击的随机带。
        /// </summary>
#pragma warning disable 0618 // The parameterless API retains the compatibility implementation.
        public List<EnemyDamagePreview> GetDamagePreview(CardConfig card)
            => GetDamagePreview(card, -1);
#pragma warning restore 0618

        /// <summary>
        /// 使用当前增益、连击与规则构建宿主主攻击计划；不代表后置印等整卡
        /// 多阶段结算。整卡敌人伤害请使用GetDamagePreview系列入口。
        /// </summary>
        public AttackPlan GetAttackPlanPreview(CardConfig card)
        {
            return AttackPlanBuilder.TryBuildFromCard(
                card,
                _rules,
                GetAttackStatsPreview(card),
                out AttackPlan plan)
                    ? plan
                    : null;
        }

        private CombatStatSnapshot GetAttackStatsPreview(CardConfig card)
        {
            int runLuckBonus = _runState.RunLuckBonus;
            // Luck seals settle before the host attack. Only those rare hosts
            // need a detached Run projection; its owner retains all cap rules.
            RunProgressionState projectedRun = null;
            if (card?.seals != null)
                foreach (CardSealSpec seal in card.seals)
                    if (seal?.effect == CardSealEffect.Luck && seal.value > 0)
                    {
                        if (projectedRun == null)
                        {
                            projectedRun = new RunProgressionState();
                            projectedRun.Restore(_runState.Capture());
                        }
                        projectedRun.AddLuck(seal.value);
                    }
            if (projectedRun != null) runLuckBonus = projectedRun.RunLuckBonus;
            return new CombatStatSnapshot(_player, GetComboPreview(card).ResultingCombo,
                CardSealRules.GetHeldAttackMultiplier(_deckMgr.Hand, card),
                (long)_mana.CurrentMana + _mana.TemporaryMana, runLuckBonus);
        }

        /// <summary>只读本次出牌的全部宿主主伤，包含回手叠势、持牌光环与支付前灵力。</summary>
        public CardAttackDisplaySummary GetCardAttackDisplay(CardConfig card)
            => CardAttackDisplayProjection.Project(card,
                card == null ? default : GetAttackStatsPreview(card), _rules);

        /// <summary>
        /// This play's immediate applied armor + health damage, from the same rows
        /// shown on enemies. Overflow, future ticks and non-damage defeats are not damage.
        /// Composite or unresolved effect conditions remain explicitly estimated.
        /// </summary>
        public CardImmediateDamageDisplaySummary GetCardImmediateDamageDisplay(CardConfig card)
            => CreateImmediateDamageDisplay(card, GetDamagePreview(card), forceEstimate: false);

        /// <summary>
        /// Read-only protocol view of decisions already frozen by the lifecycle.
        /// This never prepares candidates or consumes RNG. A cache miss returns
        /// an explicitly estimated ordinary result, including an empty result.
        /// </summary>
        public CardImmediateDamageDisplaySummary GetPreparedCardImmediateDamageDisplay(CardConfig card)
        {
            AttackProjection prepared = _attackPreviews.GetPrepared(card);
            if (prepared != null)
                return CreateImmediateDamageDisplay(card, prepared.DamagePreviews, forceEstimate: false);
            IReadOnlyList<EnemyDamagePreview> estimate = CardPlayDamagePreview.NeedsEstimate(card, _rules)
                ? CardPlayDamagePreview.Estimate(card, State,
                    GetComboPreview(card).ResultingCombo, _rules, includeNonDamageDefeats: true)
                : GetDeterministicDamagePreview(card);
            return CreateImmediateDamageDisplay(card, estimate, forceEstimate: true);
        }

        private CardImmediateDamageDisplaySummary CreateImmediateDamageDisplay(CardConfig card,
            IReadOnlyList<EnemyDamagePreview> previews, bool forceEstimate)
        {
            return new CardImmediateDamageDisplaySummary(previews,
                CardPlayDamagePreview.HasPotentialImmediateDamage(card, _rules),
                forceEstimate || _attackPreviews.HasIncompleteBattlefield ||
                    CardPlayDamagePreview.NeedsEstimate(card, _rules) ||
                    CardPlayDamagePreview.HasUnresolvedConditions(card));
        }

        /// <summary>
        /// Returns the ordinary-hit estimate on cloned enemies without consuming
        /// combat randomness or reserving the next real attack projection. This
        /// lets decision policies compare cards without changing their outcome.
        /// IsEstimate marks unresolved random targets, criticals and on-hit control;
        /// a kill in such an estimate is not a guarantee of the eventual hit.
        /// </summary>
        public List<EnemyDamagePreview> GetDeterministicDamagePreview(CardConfig card)
        {
            if (CardPlayDamagePreview.NeedsEstimate(card, _rules))
                return CardPlayDamagePreview.Estimate(
                    card, State, GetComboPreview(card).ResultingCombo, _rules);
            AttackPlan plan = GetAttackPlanPreview(card);
            return plan == null
                ? new List<EnemyDamagePreview>()
                : AttackResolver.Preview(
                    plan, _enemies, State.EnemyAi?.GetPlannedIntents(), _bossOverkill,
                    activeTerrainEffects: State.TerrainEffects?.Active);
        }

        /// <summary>Classifies preview coverage even when the direct-damage hypothesis has no targets.</summary>
        public CardDamagePreviewCoverage GetDamagePreviewCoverage(CardConfig card)
        {
            CombatStatSnapshot stats = GetAttackStatsPreview(card);
            AttackPlanBuilder.TryBuildFromCard(card, _rules, stats, out AttackPlan plan);
            return CardPlayDamagePreview.DescribeCoverage(card, plan, _rules, stats);
        }

        [Obsolete("首发攻击自动选敌；targetIndex 仅为迁移兼容参数并会被忽略。")]
        public List<EnemyDamagePreview> GetDamagePreview(CardConfig card, int targetIndex)
        {
            // Resolve the whole choice set even when this particular card is support-only.
            // Reading B must not discard A's already promised critical/target decisions.
            AttackProjection projection = _attackPreviews.Get(card);
            if (projection != null)
                return new List<EnemyDamagePreview>(projection.DamagePreviews);
            if (CardPlayDamagePreview.NeedsEstimate(card, _rules))
                return CardPlayDamagePreview.Estimate(card, State,
                    GetComboPreview(card).ResultingCombo, _rules, includeNonDamageDefeats: true);
            return GetDeterministicDamagePreview(card);
        }

        /// <summary>
        /// 返回战斗当前生效值。Run 增量属性必须通过这里合并，调用方不应
        /// 从 PlayerStats 猜测天运、敛玉或明悟的最终值。
        /// </summary>
        public int GetCurrentStatValue(StatType stat)
        {
            return stat switch
            {
                StatType.FengRui => _player.fengRui,
                StatType.Amount => _player.amount,
                StatType.BengLie => _player.bengLie,
                StatType.NingShen => _colorTrigger.PendingDurationBonus,
                StatType.ShouPai => _deckMgr.HandLimit,
                StatType.ChouPai => _deckMgr.ExplicitDrawBonus,
                StatType.TianYun => CurrentLuck,
                StatType.LianYu => CurrentGreed,
                StatType.MingWu => CurrentGrowth,
                StatType.HunHun => _player.hunHun,
                StatType.FaLi => _player.faLi,
                StatType.HuJia => _player.armor,
                StatType.ZhaoSha => _player.zhaoSha,
                StatType.Recovery => _encounterRecovery.CurrentValue,
                _ => 0
            };
        }

        /// <summary>
        /// 图标与规则共用当前属性值。凝神只表示未消费的额外储备，
        /// 英灵自身的基础及剩余次数由各驻场实例单独显示。
        /// </summary>
        public int GetDisplayStatValue(StatType stat)
            => GetCurrentStatValue(stat);

        /// <summary>战斗事件流（统一事件订阅入口）。</summary>
        public CombatEventStream Events => State.Events;

        // ── 事件发布辅助 ──

        private void PublishLog(string msg)
        {
            OnLog?.Invoke(msg);
            State.Events?.PublishLog(msg);
        }

        private void PublishEvent(CombatEventType type, object payload = null)
        {
            State.Events?.Publish(type, payload);
        }

        public CombatController(PlayerStats player, List<CardConfig> deck, List<EnemyStats> enemies,
            BossTimer bossTimer = null, List<CardConfig> upgradePool = null,
            CombatRuleSet rules = null, RunProgressionState runState = null,
            EconomySystem economy = null, IReadOnlyList<int> enemyWaveSizes = null,
            IReadOnlyList<CardConfig> fixedFirstLevelUpRewardCards = null,
            CombatProgressionState progression = null,
            IReadOnlyList<int> enemyFormationRows = null,
            string enemyFormationPattern = null,
            IReadOnlyList<CardConfig> forcedOpeningHand = null,
            Func<string, CrawlerConfig> crawlerResolver = null,
            RewardConfig levelUpRewardConfig = null,
            IInstanceIdGenerator instanceIdGenerator = null,
            IReadOnlyList<CrawlerResidenceSnapshot> residentCrawlers = null,
            CardDeckInitialization deckInitialization =
                CardDeckInitialization.CreateNewInstances,
            ILevelUpRewardOfferResolver levelUpOfferResolver = null,
            Func<string, bool> isEvolutionResultEligible = null,
            IRewardRerollBank rewardRerollBank = null,
            NaturalLevelUpRewardPool naturalRewardPool = null,
            EnemyFormationPlan enemyFormationPlan = null,
            Func<string, CardConfig> cardResolver = null,
            Func<bool> canReceiveSeals = null)
        {
            if (levelUpRewardConfig?.useNaturalCardSealPool == true &&
                (upgradePool == null || naturalRewardPool == null))
                throw new ArgumentException("自然升级需要显式解析的卡牌／灵印奖励池。");
            _rules = rules?.Clone() ?? new CombatRuleSet();
            _runState = runState ?? new RunProgressionState();
            _progression = progression ?? new CombatProgressionState();
            _economy = economy ?? new EconomySystem(runState: _runState);
            _crawlerResolver = crawlerResolver;
            _cardResolver = cardResolver;
            _isEvolutionResultEligible = isEvolutionResultEligible;
            _rewardRerollBank = rewardRerollBank;
            _canReceiveSeals = canReceiveSeals;
            _economy.ResetEncounter();
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _encounterRecovery = new EncounterRecovery(_player);
            _runState.RevivalReserve.InitializeOnce(Math.Max(0, player.hunHun));
            _player.hunHun = _runState.RevivalReserve.RemainingPercent;
            _playerDamageReceiver = new PlayerDamageReceiver(_player, _runState.RevivalReserve);
            _playerDamageReceiver.OnRevived += OnPlayerRevived;
            // Keep the author/baseline PlayerStats free of this Run-owned source.
            // A restored encounter projects the total once; real plays below add only new cycles.
            _mana = new ManaSystem(_runState.CardPlayGrowth.ProjectBaseMana(player.faLi));
            _combo = new ComboSystem(_rules.comboEnabled);
            _colorTrigger = new ColorTriggerSystem(player, _runState.CrawlerDurationReserve);
            _colorTrigger.RestoreActiveCrawlers(
                residentCrawlers,
                crawlerResolver);
            _enemies = enemies;
            if (enemyFormationPlan != null)
            {
                if (enemyWaveSizes != null || enemyFormationRows != null || enemyFormationPattern != null)
                    throw new ArgumentException("Enemy formation plan cannot be combined with waves, rows or a pattern.", nameof(enemyFormationPlan));
                // Placement/identity validation belongs to the plan; boss lifecycle stays below.
                enemyFormationPlan.ApplyTo(_enemies);
            }
            else if (!string.IsNullOrWhiteSpace(enemyFormationPattern))
                EnemyFormationLayout.AssignByPattern(_enemies, enemyFormationPattern);
            else if (enemyWaveSizes != null && enemyWaveSizes.Count > 0)
                EnemyFormationLayout.AssignWaves(_enemies, enemyWaveSizes);
            else if (enemyFormationRows != null && enemyFormationRows.Count > 0)
                EnemyFormationLayout.AssignByRows(_enemies, enemyFormationRows);
            else
                EnemyFormationLayout.Assign(_enemies);
            _forcedOpeningHand = forcedOpeningHand;
            for (int i = 0; i < _enemies.Count; i++)
                _enemies[i].runtimeId = i + 1;
            _levelBossEncounter = new LevelBossEncounter(_enemies);
            _bossOverkill = new BossOverkillSession(
                _enemies,
                _rules.bossOverkillEnabled);
            _enemyBackfill = new EnemyBackfillCoordinator(_enemies);
            _deckMgr = deckInitialization ==
                CardDeckInitialization.RestoreInstances
                    ? DeckManager.Restore(
                        deck,
                        player.shouPai,
                        CanDrawCard,
                        instanceIdGenerator,
                        player.yinJuan)
                    : new DeckManager(
                        deck,
                        player.shouPai,
                        CanDrawCard,
                        instanceIdGenerator,
                        player.yinJuan);
            _bossTimer = bossTimer;

            if (upgradePool != null)
                _levelUp = new LevelUpSystem(player, upgradePool,
                    levelUpRewardConfig ?? new RewardConfig(), () => CurrentLuck,
                    fixedFirstRewardCards: fixedFirstLevelUpRewardCards,
                    applyStatBoost: ModifyPlayerStat,
                    fixedOfferResolver: levelUpOfferResolver,
                    naturalRewardPool: naturalRewardPool,
                    hasLegalSealHost: seal =>
                        _canReceiveSeals?.Invoke() == true &&
                        _deckMgr.HasPersistentSealHost(seal, _rules, _isEvolutionResultEligible));

            if (_bossTimer != null)
                _bossTimer.OnTianjieTriggered += OnTianjieReleased;

            _colorTrigger.OnTrigger += msg => OnLog?.Invoke(msg);

            // 创建统一战斗状态
            State = new CombatState(
                _player, _enemies, _deckMgr, _mana, _combo, _colorTrigger,
                _bossTimer, _bossOverkill);
            _enemyDeathActions = new EnemyDeathActionResolver(State, _playerDamageReceiver);
            _enemyDeathActions.DamageApplied += _ => OnStateChanged?.Invoke(CombatStateChangeReason.PlayerDamaged);
            _attackPreviews = new CardAttackPreviewSession(State, _rules,
                GetAttackStatsPreview);
            State.CurrentLevel = _progression.CurrentLevel;
            State.CurrentXP = _progression.CurrentExperience;
            State.MugMarks = new MugMarkSystem(State, _economy, () => CurrentGreed);
            State.Statuses = new StatusSystem(State);
            State.Statuses.DamageBatchResolved += (damageEvents, defeats) =>
            {
                var preparedDefeats = PrepareResolvedDefeats(defeats);
                for (int i = 0; i < damageEvents.Count; i++)
                {
                    DamageEvent damageEvent = damageEvents[i];
                    TraceDamageEvent(damageEvent, "Status");
                    OnDamageEvent?.Invoke(damageEvent);
                    OnEnemyDamaged?.Invoke(damageEvent.TargetIndex,
                        damageEvent.AppliedDamage, false, damageEvent.IsFatal);
                }
                bool anyKill = false;
                PublishResolvedDamageDefeats(preparedDefeats, ref anyKill,
                    beginBossOverkill: true);
            };
            State.TerrainEffects = new TerrainEffectSystem(State);
            State.TerrainEffects.Changed += change =>
                OnTerrainEffectChanged?.Invoke(change);
            State.TerrainEffects.DamageResolved += damageEvent =>
            {
                TraceDamageEvent(damageEvent, "Terrain");
                OnDamageEvent?.Invoke(damageEvent);
                OnEnemyDamaged?.Invoke(
                    damageEvent.TargetIndex,
                    damageEvent.AppliedDamage,
                    false,
                    damageEvent.IsFatal);
            };
            State.TerrainEffects.EnemyDefeated += defeat =>
            {
                PublishEnemyDefeat(defeat);
                _bossOverkill.TryBeginAfterDefeat(defeat);
                if (defeat.TargetIndex >= 0 && defeat.TargetIndex < _enemies.Count)
                {
                    EnemyStats enemy = _enemies[defeat.TargetIndex];
                    OnEnemyKilled?.Invoke(enemy);
                    State.Statuses.ClearEnemy(defeat.TargetIndex);
                    OnStateChanged?.Invoke(CombatStateChangeReason.EnemyKilled);
                    AwardEnemyExperience(enemy);
                }
            };
            State.Events = new CombatEventStream();
            State.PeriodicAttacks = new PeriodicAttackSystem(State);
            State.PeriodicAttacks.Changed += change => OnPeriodicAttackChanged?.Invoke(change);
            State.PeriodicAttacks.AttackResolved += resolved =>
            {
                int damage = 0, first = -1, target = -1;
                bool crit = false, kill = false;
                PublishAttackResolution(resolved, ref damage, ref crit, ref kill, ref first, ref target);
            };
            State.ColumnReplays = new ColumnReplayAttackSystem(State);
            State.ColumnReplays.Resolved += result => OnColumnReplayAttackResolved?.Invoke(result);
            State.ColumnReplays.AttackResolved += resolved =>
            {
                int damage = 0, first = -1, target = -1;
                bool crit = false, kill = false;
                PublishAttackResolution(resolved, ref damage, ref crit, ref kill, ref first, ref target);
            };
            State.PresentationQueue = new CombatPresentationQueue();
            State.EnemyInterrupts = new EnemyInterruptRuntime(State);
            State.EnemyInterrupts.Synchronize();
            State.EnemyAi = new EnemyAiSystem(State);
            _enemyTurnSequence = new EnemyTurnSequence(State, () => HasPendingExperienceAwards,
                PrepareEnemyTurnSequence,
                (EnemyActionCommand command, out CombatEnemyActionResult result) =>
                    ExecuteEnemyAction(command, out result).Success,
                CompleteEnemyTurn, RestorePhaseAfterProgression);
            _enemyTurnSequence.Ready += () => OnEnemyTurnReady?.Invoke();
            OnDamageEvent += _ => State.EnemyInterrupts.Synchronize();
            OnStateChanged += _ => State.EnemyInterrupts.Synchronize();
            OnDamageEvent += _statistics.RecordDamage;
            OnEnemyKilled += _ =>
            {
                _statistics.RecordEnemyDefeated();
                _runState.CardUnlockFacts.RecordEnemyKilled();
            };
            OnCrawlerTriggered += _ =>
            {
                _statistics.RecordCrawlerTriggered();
                _runState.CardUnlockFacts.RecordCrawlerActionTriggered();
            };

            // 同步随机源到 DeckManager（State.Random 可后续注入）
            _deckMgr.SetRandomSource(State.Random);
        }

        /// <summary>
        /// 统一注入随机源，同时设置 State.Random 与 DeckManager。
        /// 用于战斗模拟器保证确定性：构造后调用此方法可覆盖构造期 null。
        /// </summary>
        public void SetRandomSource(IRandomSource random)
        {
            _attackPreviews.Clear();
            _executingAttackProjection = null;
            State.Random = random;
            _deckMgr.SetRandomSource(random);
            _levelUp?.SetRandomSource(random);
        }

        /// <summary>
        /// 英灵卡只在对应英灵不在场时进入可抽取集合。在场状态仍由
        /// ColorTriggerSystem 唯一拥有，牌区模块只消费这个动态判定。
        /// </summary>
        private bool CanDrawCard(CardConfig card)
        {
            if (card?.effects == null)
                return true;

            for (int i = 0; i < card.effects.Count; i++)
            {
                CardEffectSpec effect = card.effects[i];
                if (effect != null &&
                    effect.effectType == EffectType.SummonCrawler &&
                    _colorTrigger.HasActiveCrawler(effect.crawlerContentId))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>召唤英灵入场</summary>
        public void SummonCrawler(CrawlerConfig crawler)
        {
            _colorTrigger.SummonCrawler(crawler);
            OnLog?.Invoke($"★ 英灵 [{crawler.crawlerName}] 入场！{crawler.enterEffect} | 驻留:{crawler.duration}");
            OnStateChanged?.Invoke(CombatStateChangeReason.CrawlerChanged);
        }

        /// <summary>回合开始</summary>
        public void StartTurn()
        {
            if (State.IsBattleOver) return;

            int drawCount;
            using (_attackPreviews.Suspend())
            {
                _mana.OnTurnStart();
                _combo.Reset();
                _deckMgr.ResetTurnState();
                _hasLastPlayedCardColor = false;
                _lastPlayedCardColor = CardColor.Yang;

                if (_player.baseArmorPerTurn > 0)
                    _player.armor = (int)Math.Max(0L, Math.Min(int.MaxValue,
                        (long)_player.armor + _player.baseArmorPerTurn));

                State.CurrentTurn++;
                IReadOnlyList<CardConfig> forcedOpening = State.CurrentTurn == 1
                    ? _forcedOpeningHand
                    : null;
                IReadOnlyList<CardConfig> drawn =
                    _deckMgr.RefillStandard(forcedOpening);
                if (State.CurrentTurn == 1)
                    _forcedOpeningHand = null;
                drawCount = PublishCardsDrawn(drawn);

                State.CurrentPhase = BattlePhase.PlayerInput;
                if (State.Events != null) State.Events.CurrentTurn = State.CurrentTurn;
                State.Statuses?.OnTurnStarted();
                PublishFormationAdvance(_enemyBackfill.RecoverEmptyField());
                State.EnemyAi?.RefreshIntents();
            }
            _attackPreviews.Prepare();

            OnLog?.Invoke(
                $"=== 回合开始 === {_mana} | 标准补牌 {drawCount} 张" +
                $"（目标 {_deckMgr.HandLimit}）");
            if (_colorTrigger.ActiveCrawlers.Count > 0)
                OnLog?.Invoke($"  {_colorTrigger.GetSummary()}");
            if (_bossTimer != null)
                OnLog?.Invoke($"  {_bossTimer.GetSummary()}");
            OnStateChanged?.Invoke(CombatStateChangeReason.TurnStarted);
        }

        /// <summary>
        /// 主动抽牌。按钮、奖励和后续卡牌效果都通过该入口改变牌区，
        /// 避免表现层直接操作 DeckManager。
        /// </summary>
        public int DrawCards(int count)
        {
            if (!State.CanDrawCards || _enemyTurnSequence.IsActive) return 0;
            _attackPreviews.Prepare();
            return DrawCardsInternal(count);
        }

        /// <summary>
        /// 卡牌效果结算期间的抽牌入口，跳过阶段检查。
        /// </summary>
        internal int DrawCardsInternal(int count)
        {
            int requested = Math.Max(0, count);
            IReadOnlyList<CardConfig> drawn =
                _deckMgr.DrawExplicit(requested);
            // Draw-before-damage cards use the RNG-free whole-card estimate and
            // never reserve an exact tape. External hand changes are checked by
            // CardAttackPreviewSession state stamps, not by silently rerolling a promise.
            return PublishCardsDrawn(drawn);
        }

        private int PublishCardsDrawn(
            IReadOnlyList<CardConfig> drawnCards)
        {
            int drawn = drawnCards?.Count ?? 0;
            if (drawn > 0)
            {
                var instanceIds = new int[drawn];
                for (int i = 0; i < drawn; i++)
                {
                    instanceIds[i] = drawnCards[i].runtimeInstanceId;
                }
                OnLog?.Invoke($"抽取 {drawn} 张牌");
                using (_attackPreviews.Suspend())
                {
                    // Presentation receives identities before the hand refresh,
                    // allowing the new views to start hidden at their destination.
                    OnCardsDrawn?.Invoke(
                        new CombatCardsDrawnEvent(instanceIds));
                    // All cards in this receipt enter the hand before draw effects
                    // run. VC executes ClearDrawEffects after the draw batch; an
                    // effect-triggered play bypasses the normal player-play chain.
                    for (int i = 0; i < drawnCards.Count; i++)
                    {
                        CardConfig card = drawnCards[i];
                        if (card != null && card.autoPlayOnDraw && _deckMgr.HasInHand(card))
                            TryPlayCardTransaction(card, -1, CardPlayOrigin.CardEffect);
                    }
                }
                _attackPreviews.Prepare();
                OnStateChanged?.Invoke(CombatStateChangeReason.CardsDrawn);
            }
            return drawn;
        }

        /// <summary>
        /// 主动弃掉当前手牌。回合是否推进由调用方决定。
        /// </summary>
        public int DiscardHand()
        {
            int discarded = _deckMgr.HandCount;
            if (discarded <= 0) return 0;

            _deckMgr.DiscardHand();
            OnLog?.Invoke($"弃置 {discarded} 张手牌");
            OnStateChanged?.Invoke(CombatStateChangeReason.HandDiscarded);
            return discarded;
        }

        /// <summary>尝试打出一张牌</summary>
        public bool TryPlayCard(CardConfig card)
            => TryPlayCardTransaction(card, -1, CardPlayOrigin.Player);

        [Obsolete("首发攻击自动选敌；targetIndex 仅为迁移兼容参数并会被忽略。")]
        public bool TryPlayCard(CardConfig card, int targetIndex)
            => TryPlayCardTransaction(card, targetIndex, CardPlayOrigin.Player);

        private bool TryPlayCardTransaction(CardConfig card, int targetIndex, CardPlayOrigin origin)
        {
            bool effectTriggered = origin == CardPlayOrigin.CardEffect;
            if (effectTriggered ? State.IsBattleOver : (!State.CanPlayCards || _enemyTurnSequence.IsActive))
            {
                OnLog?.Invoke($"✗ 当前阶段无法出牌（{State.CurrentPhase}）");
                return false;
            }
            if (card == null || !_deckMgr.HasInHand(card))
            {
                OnLog?.Invoke("✗ 该牌不在当前手牌中");
                return false;
            }
            // Copy requires an original basis, not today's definition of a legacy card.
            // This shared player/effect-triggered boundary runs before mana, chain, hand
            // membership, use statistics and ID allocation can change. Generated copies
            // never recurse, and nonpositive Copy entries are not actual copy actions.
            if (card.RuntimeInstance?.IsSealCopy != true)
                foreach (CardSealPostPlayAction action in CardSealRules.GetPostPlayActions(card.seals))
                    if (action.CopyCount > 0 && !card.HasKnownAuthorConfigurations)
                    {
                        OnLog?.Invoke("✗ 旧牌缺少原始作者配置，不能执行基础化影复制");
                        return false;
                    }
            BattlePhase enclosingPhase = State.CurrentPhase;
            AttackProjection enclosingProjection = _executingAttackProjection;
            bool advanceBossTimer = !effectTriggered && IsBossTimerActive;
            var resolvedCost = card.ResolveCost();
            int paymentCost = effectTriggered ? 0 : resolvedCost.PaymentCost;
            int manaBefore = _mana.CurrentMana;
            long manaBeforePayment = (long)_mana.CurrentMana + _mana.TemporaryMana;
            AttackProjection cachedProjection = !effectTriggered
                ? _attackPreviews.Get(card)
                : null;

            if (!_mana.TrySpend(paymentCost, out ManaPaymentReceipt payment))
            {
                Debug.Log(
                    $"[Diag][Play] REJECT id={card.contentId} " +
                    $"pay={resolvedCost.PaymentCost} " +
                    $"free={resolvedCost.IsFreeToPlay} " +
                    $"mana={_mana.CurrentMana} phase={State.CurrentPhase}");
                OnLog?.Invoke($"✗ 灵力不足！需要 {resolvedCost.PaymentCost}，当前 {_mana}");
                return false;
            }
            _attackPreviews.Clear();
            _executingAttackProjection = cachedProjection;

            // 输入和费用校验成功后才进入结算阶段；失败出牌必须保留 PlayerInput/Overkill 原阶段。
            State.CurrentPhase = BattlePhase.CardResolving;
            EnemyBackfillCheckpoint backfillCheckpoint =
                _enemyBackfill.BeginResolution();

            // 首发取消手动选怪；兼容参数不参与规则，只给旧表现事件保留一个自动目标索引。
            targetIndex = ResolveEffectTarget(-1);

            bool returnsAfterResolution = _deckMgr.WillReturnOnPlay(card);
            ComboPreview comboResolution = effectTriggered ? default : _combo.ResolvePlayCard(
                resolvedCost, returnsAfterResolution);
            bool comboed = comboResolution.WouldCombo;
            bool chainWasBroken = comboResolution.WouldBreak;
            CardColor wuxingFromColor = default;
            bool wuxingTriggered = !effectTriggered &&
                TryResolveWuxingPair(card, out wuxingFromColor, out _);
            if (!effectTriggered)
            {
                _lastPlayedCardColor = card.color;
                _hasLastPlayedCardColor = true;
            }
            int combo = comboResolution.ResultingCombo;
            ColorTriggerSystem.TriggerBoundary crawlerTriggerBoundary =
                _colorTrigger.CaptureTriggerBoundary();
            _statistics.RecordCardStarted(
                card,
                combo,
                resolvedCost.IsFreeToPlay);
            Debug.Log(
                $"[Diag][Play] id={card.contentId} " +
                $"origin={origin} pay={paymentCost} free={resolvedCost.IsFreeToPlay} " +
                $"comboVal={resolvedCost.ComboValue} " +
                $"mana {manaBefore}->{_mana.CurrentMana} " +
                $"combo={combo} chained={comboed}");

            OnCardPlayedVFX?.Invoke(card, targetIndex);
            ApplyCardSeals(card);

            // A played card leaves the hand before its effects resolve. This
            // makes DrawCards able to replace the played card even when the
            // hand was at its limit, and gives draw presentation the final
            // authoritative hand composition in one refresh.
            if (!_deckMgr.BeginPlayFromHand(card, card.MustDestroyOnUse,
                    out bool actualReturn) || actualReturn != returnsAfterResolution)
                throw new InvalidOperationException(
                    "出牌校验后牌牌未能离开手牌，牌区不变量已被破坏。");

            // 升级奖励的免耗只可用于这一次成功出牌。必须在牌离手后、效果
            // 结算前清除：抽牌效果可能立刻从弃牌堆抽回这张牌，此时不能再带着
            // 已消费的免耗标记。
            ConsumeFreeRewardCardOnPlay(card);

            // 结算效果
            int totalDamage = 0;
            bool anyCrit = false;
            bool anyKill = false;
            int firstTargetIndex = targetIndex;
            int armorGained = 0, healAmount = 0, manaGained = payment.CreditedAmount;
            int maxHealthGained = 0, jadeGained = 0;
            int durationReserveGained = 0;
            var resolvedCrawlerDurationChanges = new List<CrawlerDurationChange>();
            // Per-call facts keep nested automatic draws isolated from their parent play.
            bool randomizedCrawlerTriggerColors = false;
            var resolvedCrawlerTriggerColors = new List<CrawlerTriggerColorChange>();
            var resolvedHandCostChanges = new List<HandCardCostChange>();
            var createdCards = new List<CombatCreatedCardReceipt>();
            foreach (int baseJade in CardSealRules.GetHeldPlayJadeRewards(_deckMgr.Hand, card))
                jadeGained = (int)Math.Min(int.MaxValue, (long)jadeGained +
                    _economy.Grant(baseJade, CurrentGreed, JadeSource.Seal, true).FinalJade);
            var deferredStats = new List<KeyValuePair<StatType, int>>();

            if (card.HasSpecEffects)
            {
                // ── Spec 驱动路径 ──
                int multiplier = 1 + combo;

                foreach (var spec in card.effects)
                {
                    int scaledAmount = spec.comboScalable ? spec.amount * multiplier : spec.amount;
                    int scaledCount = spec.comboScalable && spec.effectType != EffectType.Damage ? spec.count * multiplier : spec.count;

                    switch (spec.effectType)
                    {
                        case EffectType.Damage:
                            ResolveDamageSpec(spec, card, combo, manaBeforePayment,
                                ref totalDamage, ref anyCrit, ref anyKill,
                                ref firstTargetIndex, ref targetIndex);
                            break;
                        case EffectType.GainArmor:
                            _player.armor += scaledAmount; armorGained += scaledAmount; break;
                        case EffectType.Heal:
                            {
                                int before = _player.currentHealth;
                                _player.currentHealth = Mathf.Min(
                                    _player.maxHealth,
                                    _player.currentHealth +
                                    ApplyHealingRules(scaledAmount));
                                healAmount += _player.currentHealth - before;
                            }
                            break;
                        case EffectType.GainMana:
                            {
                                int before = _mana.TemporaryMana;
                                _mana.AddTemporaryMana(scaledAmount);
                                manaGained = (int)Math.Min(int.MaxValue,
                                    (long)manaGained + _mana.TemporaryMana - before);
                            }
                            break;
                        case EffectType.DrawCards: DrawCardsInternal(scaledCount); break;
                        case EffectType.ApplyStatus:
                            if (spec.target == TargetRule.Self)
                                State.Statuses?.ApplyToPlayer(spec.statusId, spec.amount, spec.duration);
                            else { int st = ResolveEffectTarget(targetIndex); if (st >= 0 && State.RandomValue() < spec.chance) State.Statuses?.ApplyToEnemy(st, spec.statusId, spec.amount, spec.duration); }
                            break;
                        case EffectType.Burn:
                            { int st = ResolveEffectTarget(targetIndex); if (st >= 0 && State.RandomValue() < spec.chance) State.Statuses?.ApplyToEnemy(st, StatusEffect.ZhuoShao, Math.Max(1, spec.amount), spec.duration, card.runtimeInstanceId); }
                            break;
                        case EffectType.Bounce:
                            {
                                int excludedRuntimeId = targetIndex >= 0 && targetIndex < _enemies.Count
                                    ? _enemies[targetIndex].runtimeId : 0;
                                ResolveSpecialDamage(AttackResolver.ResolveBounce(spec.amount, spec.count,
                                    new PrePlayStatsSnapshot(_player, combo), _enemies, targetIndex,
                                    BounceTargetMode.UniqueThenStop, excludedRuntimeId,
                                    _bossOverkill, card.runtimeInstanceId),
                                    ref totalDamage, ref anyCrit, ref anyKill, ref firstTargetIndex, ref targetIndex, card);
                            }
                            break;
                        case EffectType.Erase:
                            ResolveEraseDefeat(spec.target, card.runtimeInstanceId,
                                ResolveEffectTarget(targetIndex), ref anyKill, ref firstTargetIndex, ref targetIndex);
                            break;
                        case EffectType.ModifyStat:
                            if (spec.statType == StatType.NingShen)
                                durationReserveGained += ApplyCrawlerDurationGain(
                                    scaledAmount, resolvedCrawlerDurationChanges);
                            else
                                deferredStats.Add(new KeyValuePair<StatType, int>(spec.statType, scaledAmount));
                            break;
                        case EffectType.GainJade:
                            jadeGained += _economy.Grant(
                                scaledAmount,
                                CurrentGreed,
                                JadeSource.Card,
                                true).FinalJade;
                            break;
                        case EffectType.IncreaseMaxHealth:
                            maxHealthGained +=
                                _player.IncreaseMaxHealth(scaledAmount);
                            break;
                        case EffectType.GainExperience:
                            GainXP(
                                scaledAmount,
                                ExperienceSource.Character);
                            break;
                        case EffectType.SummonCrawler:
                            {
                                var crawler = _crawlerResolver?.Invoke(spec.crawlerContentId);
                                if (crawler != null)
                                    SummonCrawler(crawler);
                                else
                                    OnLog?.Invoke($"  ⚠ 未找到英灵配置: {spec.crawlerContentId}");
                            }
                            break;
                        case EffectType.CreateCard:
                            ResolveCreatedCards(card, spec, createdCards);
                            break;
                        case EffectType.IncreaseCrawlerDuration:
                            ApplyCrawlerDurationIncrease(
                                spec.crawlerContentId,
                                scaledAmount, resolvedCrawlerDurationChanges);
                            break;
                        case EffectType.ExtendCrawlerDuration:
                            durationReserveGained += ApplyCrawlerDurationGain(
                                scaledAmount, resolvedCrawlerDurationChanges);
                            break;
                        case EffectType.RandomizeCrawlerTriggerColors:
                            resolvedCrawlerTriggerColors.AddRange(
                                _colorTrigger.RandomizeActiveTriggerColors(State.Random ?? DefaultRandomSource.Instance));
                            randomizedCrawlerTriggerColors = true;
                            break;
                        case EffectType.RandomizeHandCosts:
                            {
                                IReadOnlyList<HandCardCostChange> handCostChanges;
                                if (spec.randomCostRelative)
                                    _deckMgr.RandomizeNumericHandCostOffsets(
                                        spec.randomCostMin, spec.randomCostMax, out handCostChanges);
                                else
                                    _deckMgr.RandomizeNumericHandCosts(
                                        Math.Max(0, spec.randomCostMin),
                                        Math.Max(Math.Max(0, spec.randomCostMin), spec.randomCostMax),
                                        out handCostChanges);
                                resolvedHandCostChanges.AddRange(handCostChanges);
                            }
                            break;
                        case EffectType.RemoveEnemyIntent:
                            State.EnemyAi?.RemoveRandomPlannedIntents(
                                spec.count,
                                spec.attackIntentOnly,
                                spec.removeAllIntents);
                            break;
                    }
                }
            }
            else
            {
                // ── Legacy 字段路径 ──
                int multiplier = 1 + combo;
                if (card.baseDamage > 0)
                {
                    ResolveAttack(card, card.baseDamage, Mathf.Max(1, card.projectileCount), combo,
                        ref totalDamage, ref anyCrit, ref anyKill, ref firstTargetIndex, ref targetIndex);
                }
                // Legacy 效果也应用 combo 缩放
                if (card.armor > 0) { int v = card.armor * multiplier; _player.armor += v; armorGained = v; }
                if (card.heal > 0)
                {
                    int v = ApplyHealingRules(card.heal * multiplier);
                    int before = _player.currentHealth;
                    _player.currentHealth = Mathf.Min(_player.maxHealth, _player.currentHealth + v);
                    healAmount = _player.currentHealth - before;
                }
                if (card.manaGain > 0)
                {
                    int before = _mana.TemporaryMana;
                    _mana.AddTemporaryMana(card.manaGain * multiplier);
                    manaGained = (int)Math.Min(int.MaxValue,
                        (long)manaGained + _mana.TemporaryMana - before);
                }
                if (card.statusChance > 0f && card.statusEffect != StatusEffect.ZhenTui &&
                    card.statusEffect != StatusEffect.JiaoXie && card.statusEffect != StatusEffect.BaoJi &&
                    State.RandomValue() < card.statusChance)
                {
                    int st = ResolveEffectTarget(targetIndex);
                    if (st >= 0 && card.statusEffect == StatusEffect.ZhuoShao)
                        State.Statuses?.ApplyToEnemy(st, StatusEffect.ZhuoShao, 1, 0, card.runtimeInstanceId);
                    else if (st >= 0 && card.statusEffect == StatusEffect.MoSha)
                        ResolveEraseDefeat(TargetRule.PlayedTarget, card.runtimeInstanceId,
                            st, ref anyKill, ref firstTargetIndex, ref targetIndex);
                    else if (st >= 0 && card.statusEffect != StatusEffect.None &&
                        card.statusEffect != StatusEffect.ZhenTui && card.statusEffect != StatusEffect.JiaoXie &&
                        card.statusEffect != StatusEffect.BaoJi)
                        State.Statuses?.ApplyToEnemy(st, card.statusEffect, 1);
                }
            }

            // Stat Gain 在整卡伤害完成后写入，下一张牌才享受。
            List<CombatStatChange> resolvedStatChanges =
                ApplyDeferredStatChanges(deferredStats);

            // VC 的 Shield Bash 只由本牌自身的护甲结算触发；已有护甲
            // 不能让攻击／治疗等非护甲卡重复追加伤害，后置灵印的护甲
            // 也不倒灌给宿主。本牌主效果结算后的 armorGained 正好是
            // 这个边界唯一的事实来源。
            if (_rules.shieldBashEnabled && armorGained > 0 && _player.armor > 0)
                ResolveShieldBash(card, ref totalDamage, ref anyCrit, ref anyKill,
                    ref firstTargetIndex, ref targetIndex);

            var sealGains = ApplyPostPlayCardSeals(card, resolvedStatChanges,
                sealPlan => ResolveAttackPlan(sealPlan,
                    ref totalDamage, ref anyCrit, ref anyKill,
                    ref firstTargetIndex, ref targetIndex), payment, isRealPlay: true);
            armorGained += sealGains.ArmorGained;
            manaGained = (int)Math.Min(int.MaxValue, (long)manaGained + sealGains.ManaGained);

            // 本牌只有产生新死亡时才立即推进一次；历史尸体与额外出牌
            // 不会重复消费尚未收敛的补位信号。
            PublishFormationAdvances(
                _enemyBackfill.AdvanceAfterResolution(backfillCheckpoint));

            // Mild / ComboBreaker 结算后清空连击
            if (!effectTriggered && !returnsAfterResolution &&
                (resolvedCost.IsMild || resolvedCost.IsComboBreaker))
                _combo.ClearCombo();

            // 五行相生在当前卡牌效果全部结算完毕后生效（后续牌才享受加成）
            durationReserveGained += ApplyWuxingIfTriggered(wuxingTriggered, wuxingFromColor,
                resolvedCrawlerDurationChanges, returnsAfterResolution);
            bool returnedToHand = _deckMgr.CompletePlayFromHand(card);

            _statistics.RecordCardHealing(healAmount);
            if (_rules.experimentalMedicineEnabled && healAmount > 0)
                OnRuleTriggered?.Invoke(new CombatRuleTriggeredEvent(
                    CombatRuleTriggerKind.ExperimentalMedicine, healAmount));
            OnCardResolved?.Invoke(card, firstTargetIndex, totalDamage, comboed, anyCrit);
            OnCardPlayResolved?.Invoke(new CombatCardResolvedEvent
            {
                Card = card,
                PlayOrigin = origin,
                TargetIndex = firstTargetIndex,
                ManaCost = paymentCost,
                DamageDealt = totalDamage,
                ArmorGained = armorGained,
                HealAmount = healAmount,
                ManaGained = manaGained,
                MaxHealthGained = maxHealthGained,
                JadeGained = jadeGained,
                StatChanges = resolvedStatChanges,
                CrawlerDurationChanges = resolvedCrawlerDurationChanges.AsReadOnly(),
                RandomizedCrawlerTriggerColors = randomizedCrawlerTriggerColors,
                CrawlerTriggerColorChanges = resolvedCrawlerTriggerColors.AsReadOnly(),
                HandCostChanges = resolvedHandCostChanges.AsReadOnly(),
                CreatedCards = createdCards.AsReadOnly(),
                DurationReserveGained = durationReserveGained,
                ResultingDurationReserve = _colorTrigger.PendingDurationBonus,
                Comboed = comboed,
                ChainWasBroken = chainWasBroken,
                ComboCount = combo,
                IsCritical = anyCrit,
                EnemyKilled = anyKill,
                DestroyedOnUse = card.MustDestroyOnUse,
                ReturnedToHand = returnedToHand
            });

            // 颜色触发：倍率快照取本牌结算值；触发产生的属性增量由
            // 属性所有者（本控制器）立即写入，下一张牌即可享受。
            if (!effectTriggered)
                ResolveCrawlerTriggers(
                    card,
                    1 + combo,
                    crawlerTriggerBoundary);

            // Boss 计时器
            if (advanceBossTimer)
                _bossTimer?.OnCardPlayed();

            // 关 BOSS 的部署是整张牌的最终边界。必须等伤害、状态、
            // 英灵被动与本牌计时器快照全部结算完，清场牌才绝无机会
            // 在同一次结算中继续命中新出现的 BOSS。
            if (!effectTriggered || enclosingPhase != BattlePhase.CardResolving)
                TryPublishLevelBossArrival();

            if (card.MustDestroyOnUse)
                OnLog?.Invoke($"  {card.cardName} 已销毁");

            if (_combo.IsActive)
                OnLog?.Invoke($"  {_combo.GetSummary()}");

            if (_bossTimer != null && _bossTimer.IsTriggered)
                OnLog?.Invoke($"  {_bossTimer.GetSummary()}");

            // The entire actual play (including its dependent triggers/timer) has settled.
            // This is before the public complete-card/planning and terminal boundaries,
            // and is deliberately absent from ResolveCardForTestHarness.
            // Core also accepts anonymous in-memory/prototype cards. They have
            // no content identity to qualify and cannot fabricate one from a name.
            // Formal authored content is required to have stable IDs by Content.
            if (!string.IsNullOrWhiteSpace(card.contentId) && card.contentId == card.contentId.Trim())
                _runState.CardUnlockFacts.RecordCardPlayed(card.contentId);
            RunCardPlayGrowthReceipt growth = _runState.CardPlayGrowth.CommitRealPlays();
            if (growth.BaseManaDelta > 0) _mana.AddBaseMana(growth.BaseManaDelta);
            if (growth.CommittedRealPlays > 0) OnCardPlayGrowthCommitted?.Invoke(growth);
            // Immediate/headless XP may cross a threshold during this card. Freeze
            // the offer only after the whole real play has become a behavior fact.
            if (!effectTriggered || enclosingPhase != BattlePhase.CardResolving)
                ProcessPendingLevelUps();
            StoreProgression();
            if (!effectTriggered) _enemyTurnSequence.OnCardPlayed();
            OnStateChanged?.Invoke(CombatStateChangeReason.CardPlayed);

            // 所有出牌后阶段都经过同一优先级：玩家失败 > 奖励等待 > 余威 >
            // 胜利 > 玩家输入。天劫可能在本牌击杀 Boss 后才把玩家击败，
            // 因而不能在这里另写一套“余威优先”的分支。
            if (effectTriggered && enclosingPhase == BattlePhase.CardResolving)
                State.CurrentPhase = enclosingPhase;
            else
                RestorePhaseAfterProgression();

            _executingAttackProjection = effectTriggered ? enclosingProjection : null;
            return true;
        }

        // ──────────────────────────────────────────────
        //  Command 模式入口
        // ──────────────────────────────────────────────

        /// <summary>
        /// 执行战斗指令。UI 层创建指令，此处验证并执行。
        /// 返回 <see cref="CommandResult"/> 表示成功或失败原因。
        /// </summary>
        public CommandResult ExecuteCommand(ICombatCommand command)
        {
            if (State.IsBattleOver)
                return CommandResult.Fail(CommandError.CombatOver, "战斗已结束");

            return command switch
            {
                PlayCardCommand pc => ExecutePlayCard(pc),
                EndTurnCommand => ExecuteEndTurn(),
                DrawCardsCommand dc => ExecuteDrawCards(dc),
                SelectRewardCommand sr => ExecuteSelectReward(sr),
                DiscardHandCommand => ExecuteDiscardHand(),
                EnemyActionCommand ea => ExecuteEnemyAction(ea),
                _ => CommandResult.Fail(CommandError.InvalidPhase, $"未知指令类型: {command?.CommandType}")
            };
        }

        private CommandResult ExecutePlayCard(PlayCardCommand cmd)
        {
            if (!State.CanPlayCards)
                return CommandResult.Fail(CommandError.InvalidPhase, $"当前阶段无法出牌（{State.CurrentPhase}）");

            if (cmd.Card == null)
                return CommandResult.Fail(CommandError.CardNotInHand, "卡牌为空");

            if (!_deckMgr.HasInHand(cmd.Card))
                return CommandResult.Fail(CommandError.CardNotInHand, $"卡牌 {cmd.Card.cardName} 不在手牌中");

            var resolved = cmd.Card.ResolveCost();
            if (!_mana.PreviewPayment(resolved.PaymentCost, out _))
                return CommandResult.Fail(CommandError.InsufficientMana, $"需要 {resolved.PaymentCost} 法力，当前 {_mana}");

            bool success = TryPlayCardTransaction(cmd.Card, cmd.TargetIndex, CardPlayOrigin.Player);
            return success
                ? CommandResult.Ok
                : CommandResult.Fail(CommandError.InvalidTarget, "出牌失败");
        }

        private CommandResult ExecuteEndTurn()
        {
            CommandResult result = BeginEndTurnSequence();
            if (!result.Success)
                return result;
            if (_enemyTurnSequence.IsActive)
                ExecuteEnemyTurn();
            return CommandResult.Ok;
        }

        private CommandResult ExecuteDrawCards(DrawCardsCommand cmd)
        {
            if (!State.CanDrawCards)
                return CommandResult.Fail(CommandError.InvalidPhase, $"当前阶段无法抽牌（{State.CurrentPhase}）");

            int drawn = DrawCards(Math.Max(0, cmd.Count));
            return drawn > 0
                ? CommandResult.Ok
                : CommandResult.Fail(CommandError.NoCardsToDraw, "牌库和弃牌堆为空");
        }

        private CommandResult ExecuteSelectReward(SelectRewardCommand cmd)
        {
            if (!State.AwaitingRewardSelection)
                return CommandResult.Fail(CommandError.NoRewardPending, "当前没有待选择的升级奖励");

            LevelUpSystem.Reward reward = _pendingRewards != null &&
                cmd.RewardIndex >= 0 &&
                cmd.RewardIndex < _pendingRewards.Count
                    ? _pendingRewards[cmd.RewardIndex]
                    : null;
            return TrySelectLevelUpReward(
                    reward,
                    PendingLevelUpRewardRevision,
                    out string error)
                ? CommandResult.Ok
                : CommandResult.Fail(
                    CommandError.InvalidTarget,
                    error ?? "升级奖励选择失败。");
        }

        private CommandResult ExecuteDiscardHand()
        {
            if (!State.CanEndTurn)
                return CommandResult.Fail(CommandError.InvalidPhase, $"当前阶段无法弃牌（{State.CurrentPhase}）");

            int discarded = DiscardHand();
            return discarded > 0
                ? CommandResult.Ok
                : CommandResult.Fail(CommandError.InvalidTarget, "手牌为空");
        }

        // ──────────────────────────────────────────────
        //  敌人回合
        // ──────────────────────────────────────────────

        /// <summary>
        /// 执行敌人回合。完整玩家回合流程应通过 EndTurnCommand 进入；
        /// 此方法保留给规则测试与内部阶段化结算使用。
        /// </summary>
        public void ExecuteEnemyTurn()
        {
            if (State.IsBattleOver) return;

            _enemyTurnSequence.Begin();
            _enemyTurnSequence.Drain();
        }

        /// <summary>
        /// Ends the player turn and prepares its authoritative enemy action
        /// queue without resolving it. The scene advances this queue at each
        /// visual impact; EndTurnCommand still consumes it synchronously.
        /// </summary>
        public CommandResult BeginEndTurnSequence()
        {
            if (_enemyTurnSequence.IsActive)
            {
                return CommandResult.Fail(
                    CommandError.InvalidPhase,
                    "敌人回合已经开始");
            }

            if (!State.CanEndTurn)
            {
                return CommandResult.Fail(
                    CommandError.InvalidPhase,
                    $"当前阶段无法结束回合（{State.CurrentPhase}）");
            }

            EndTurn();
            if (State.IsBattleOver || State.IsCombatOver())
                return CommandResult.Ok;

            _enemyTurnSequence.Begin();
            return CommandResult.Ok;
        }

        /// <summary>Returns the next queued action without applying it.</summary>
        public bool TryPeekNextEnemyAction(out EnemyActionCommand command)
            => _enemyTurnSequence.TryPeek(out command);

        /// <summary>
        /// Applies exactly one queued enemy action. Presentation calls this at
        /// contact time, so damage and the visible hit share one frame.
        /// </summary>
        public bool TryResolveNextEnemyAction(
            out CombatEnemyActionResult result)
            => _enemyTurnSequence.TryResolve(out result);

        /// <summary>Settles only the receipt previously presented, never the next actor's action.</summary>
        public bool TryResolveNextEnemyAction(EnemyActionCommand expected, out CombatEnemyActionResult result)
            => _enemyTurnSequence.TryResolve(expected, out result);

        /// <summary>
        /// Finalizes a fully consumed enemy turn and only then starts the next
        /// player turn. Returns false while actions remain unresolved.
        /// </summary>
        public bool CompleteEnemyTurnSequence()
            => _enemyTurnSequence.Complete();

        /// <summary>
        /// Binds a staged consumer to the existing authoritative FIFO. Without a binding,
        /// card interrupts settle synchronously. Disposing never executes detached scene work.
        /// </summary>
        public IDisposable AcquireStagedEnemyActions()
            => _enemyTurnSequence.AcquireStagedExecution();

        private void CompleteEnemyTurn()
        {
            if (!_rules.preservePlayerArmorBetweenTurns)
                _player.armor = 0;

            // 清除死亡补位标记
            for (int i = 0; i < _enemies.Count; i++)
                if (_enemies[i] != null) _enemies[i].justFilledFrontRow = false;

            if (State.IsCombatOver())
            {
                TryFinishCombat();
                return;
            }

            StartTurn();
        }

        private IReadOnlyList<EnemyActionCommand> PrepareEnemyTurnSequence()
        {
            State.CurrentPhase = BattlePhase.EnemyTurnStarting;
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (!_enemies[i].IsDead && _enemies[i].IsDeployed)
                    _enemies[i].armor = 0;
            }

            // 保留既有地形时序：敌方回合开始地形先读取当前阵型。本阶段随后
            // 整体前压一排——所有非前排存活敌人向前收拢（含地形刚造成的
            // 新死亡腾出的空位）。
            State.TerrainEffects?.Trigger(
                TerrainEffectTiming.EnemyTurnStarted);
            // 玩家已经主动结束回合；若回合边界的地形伤害才击败
            // Boss，不再为已结束的回合重开余威窗口。
            if (State.HasOverkillTarget)
            {
                _bossOverkill.End();
                TryFinishCombat();
                return Array.Empty<EnemyActionCommand>();
            }
            PublishFormationAdvance(
                _enemyBackfill.AdvanceAtEnemyActionPreparation(),
                "TurnMarch",
                "回合推进前压");
            TryPublishLevelBossArrival();
            OnStateChanged?.Invoke(CombatStateChangeReason.TurnStarted);

            State.CurrentPhase = BattlePhase.EnemyActing;

            return State.EnemyAi?.GenerateCommands() ?? new List<EnemyActionCommand>();
        }

        private CommandResult ExecuteEnemyAction(EnemyActionCommand cmd)
            => ExecuteEnemyAction(cmd, out CombatEnemyActionResult _);

        private CommandResult ExecuteEnemyAction(
            EnemyActionCommand cmd,
            out CombatEnemyActionResult actionResult)
        {
            actionResult = default;
            if (cmd.EnemyIndex < 0 || cmd.EnemyIndex >= _enemies.Count)
                return CommandResult.Fail(CommandError.InvalidIndex, "敌人索引越界");

            var enemy = _enemies[cmd.EnemyIndex];
            if (enemy == null || enemy.IsDead || !enemy.IsDeployed)
                return CommandResult.Fail(
                    CommandError.InvalidTarget,
                    "敌人尚未部署或已经死亡");

            if (State.EnemyAi?.IsIntentRemoved(cmd.EnemyIndex) == true)
                return CommandResult.Fail(
                    CommandError.InvalidTarget,
                    "敌人本回合的行动意图已取消");

            if (!EnemyFormationLayout.IsFullyInsideVisibleRows(enemy))
                return CommandResult.Fail(
                    CommandError.InvalidTarget,
                    "敌人尚未完整进入战场");

            var intent = cmd.Intent;
            bool isEscape = intent.Type == EnemyIntentType.Escape;
            bool requiresFrontRow =
                intent.ActionType == EnemyActionType.Attack ||
                intent.Type == EnemyIntentType.Attack ||
                isEscape;
            if (requiresFrontRow &&
                !EnemyFormationLayout.CanAttackPlayer(enemy))
            {
                return CommandResult.Fail(
                    CommandError.InvalidTarget,
                    isEscape
                        ? "只有第一排敌人可以撤离战斗"
                        : "只有第一排敌人可以攻击玩家");
            }
            if (isEscape && enemy.justFilledFrontRow)
            {
                return CommandResult.Fail(
                    CommandError.InvalidTarget,
                    "刚补到第一排的敌人必须等待一个完整玩家回合后才能撤离");
            }
            if (State.EnemyAi?.TryBeginAction(cmd) == false)
                return CommandResult.Fail(CommandError.InvalidTarget, "敌人行动回执已失效或已消费");
            PlayerDamageTotals playerDamageBefore = _playerDamageReceiver.Totals;
            int playerHealthBefore = _player.currentHealth;
            int playerArmorBefore = _player.armor;
            int enemyRowBefore = enemy.row;
            int enemyArmorBefore = enemy.armor;
            bool wasDisarmed = false;
            EnemyTemporaryCardGrantResult? temporaryCardGrant = null;

            if (intent.ActionType == EnemyActionType.Attack && intent.CanBeDisarmed && enemy.isDisarmed)
            {
                enemy.isDisarmed = false;
                wasDisarmed = true;
                OnLog?.Invoke($"⛓ {enemy.name} 的攻击被缴械取消！");
            }
            else
            {
                switch (intent.Type)
                {
                    case EnemyIntentType.Attack:
                        var damage = ApplyPlayerDamage(intent.Amount);
                        OnLog?.Invoke(
                            $"⚔ {enemy.name} 攻击玩家 -{intent.Amount}！" +
                            (damage.BecameFatal
                                ? " 玩家陨落！"
                                : $" 剩余 HP: {damage.RemainingHealth}"));
                        OnStateChanged?.Invoke(
                            CombatStateChangeReason.PlayerDamaged);
                        break;

                    case EnemyIntentType.Defend:
                        int requestedArmor = Math.Max(0, intent.Amount);
                        int armorAfter = enemy.maxArmor > 0
                            ? Math.Min(
                                enemy.maxArmor,
                                enemy.armor + requestedArmor)
                            : enemy.armor + requestedArmor;
                        int appliedArmor = armorAfter - enemy.armor;
                        enemy.armor = armorAfter;
                        OnLog?.Invoke(
                            $"🛡 {enemy.name} 防御 +{appliedArmor}");
                        OnStateChanged?.Invoke(
                            CombatStateChangeReason.EnemyActed);
                        break;

                    case EnemyIntentType.Buff:
                    case EnemyIntentType.Debuff:
                    case EnemyIntentType.Summon:
                    case EnemyIntentType.Special:
                        OnLog?.Invoke(
                            $"✦ {enemy.name} 使用 {intent.Description}");
                        OnStateChanged?.Invoke(
                            CombatStateChangeReason.EnemyActed);
                        break;

                    case EnemyIntentType.Wait:
                        OnLog?.Invoke(
                            $"… {enemy.name} {intent.Description}");
                        OnStateChanged?.Invoke(
                            CombatStateChangeReason.EnemyActed);
                        break;

                    case EnemyIntentType.Escape:
                        var previousFootprint = new FormationFootprint(
                            enemy.row,
                            enemy.column,
                            enemy.GridWidth,
                            enemy.GridDepth);
                        if (!enemy.TryWithdraw())
                        {
                            return CommandResult.Fail(
                                CommandError.InvalidTarget,
                                "敌人已经死亡、撤离或尚未部署");
                        }

                        State.Statuses?.ClearEnemy(cmd.EnemyIndex);
                        State.MugMarks.ForgetEnemy(enemy.runtimeId);
                        var withdrawal = new EnemyWithdrawnEvent(
                            enemy.runtimeId,
                            cmd.EnemyIndex,
                            previousFootprint);
                        OnLog?.Invoke(
                            $"↗ {enemy.name} {intent.Description}");
                        OnEnemyWithdrawn?.Invoke(withdrawal);
                        OnStateChanged?.Invoke(
                            CombatStateChangeReason.EnemyWithdrawn);
                        PublishFormationAdvances(
                            _enemyBackfill.AdvanceAfterWithdrawal(
                                cmd.EnemyIndex));
                        TryPublishLevelBossArrival();
                        break;

                    case EnemyIntentType.AddEncounterTemporaryCard:
                        if (!TryGrantEncounterTemporaryCards(
                                intent,
                                out EnemyTemporaryCardGrantResult grant,
                                out string temporaryCardError))
                        {
                            return CommandResult.Fail(
                                CommandError.InvalidTarget,
                                temporaryCardError);
                        }
                        temporaryCardGrant = grant;
                        OnLog?.Invoke(
                            $"✦ {enemy.name} {intent.Description} · " +
                            $"临时卡 {intent.TemporaryCard.ContentId} " +
                            $"×{grant.ActualCount}" +
                            (grant.LimitReached ? "（已达同源残留上限）" : string.Empty));
                        OnStateChanged?.Invoke(
                            CombatStateChangeReason.EnemyActed);
                        break;

                    case EnemyIntentType.Compound:
                        if (!TryExecuteCompoundEnemyIntent(
                                enemy,
                                intent,
                                out EnemyTemporaryCardGrantResult compoundGrant,
                                out string compoundError))
                        {
                            return CommandResult.Fail(
                                CommandError.InvalidTarget,
                                compoundError);
                        }
                        temporaryCardGrant = compoundGrant;
                        OnStateChanged?.Invoke(
                            CombatStateChangeReason.EnemyActed);
                        break;
                }
            }

            if (wasDisarmed)
            {
                OnStateChanged?.Invoke(
                    CombatStateChangeReason.EnemyActed);
            }

            actionResult = new CombatEnemyActionResult(
                enemy.runtimeId,
                cmd.EnemyIndex,
                enemy.isBoss,
                intent,
                wasDisarmed,
                playerHealthBefore,
                _player.currentHealth,
                playerArmorBefore,
                _player.armor,
                enemyRowBefore,
                enemy.row,
                enemyArmorBefore,
                enemy.armor,
                temporaryCardGrant,
                playerHealthDamage: _playerDamageReceiver.Totals.HpDamage - playerDamageBefore.HpDamage,
                playerArmorDamage: _playerDamageReceiver.Totals.ShieldDamage - playerDamageBefore.ShieldDamage);
            State.EnemyAi?.MarkActionResolved(cmd);
            OnEnemyActionResolved?.Invoke(actionResult);

            return CommandResult.Ok;
        }

        private EnemyActionTurnContext ResolveEnemyActionTurnContext()
        {
            // The FIFO owns turn origin: a player interruption uses EnemyActing
            // without becoming an ordinary enemy turn.
            if (_enemyTurnSequence.IsActive)
                return _enemyTurnSequence.IsPlayerInterrupt
                    ? EnemyActionTurnContext.PlayerTurn
                    : EnemyActionTurnContext.OrdinaryEnemyTurn;
            return State.CurrentPhase switch
            {
                BattlePhase.PlayerInput => EnemyActionTurnContext.PlayerTurn,
                BattlePhase.Overkill => EnemyActionTurnContext.PlayerTurn,
                BattlePhase.EnemyTurnStarting => EnemyActionTurnContext.OrdinaryEnemyTurn,
                BattlePhase.EnemyActing => EnemyActionTurnContext.OrdinaryEnemyTurn,
                _ => EnemyActionTurnContext.Unspecified
            };
        }

        private static void ApplyNoCompoundEnemyPrimary() { }

        private bool TryGrantEncounterTemporaryCards(
            EnemyIntent intent,
            out EnemyTemporaryCardGrantResult grant,
            out string error)
        {
            grant = default;
            error = null;
            try
            {
                grant = _deckMgr.GrantEnemyTemporaryCardsAfter(intent.TemporaryCard,
                    ResolveEnemyActionTurnContext(), ApplyNoCompoundEnemyPrimary);
                return true;
            }
            catch (EnemyTemporaryCardPreparationException exception)
            {
                error = exception.InnerException.Message;
                return false;
            }
        }

        /// <summary>Spec 路径：伤害效果结算（含 MultiShot 多目标分配）</summary>
        private void ResolveDamageSpec(CardEffectSpec spec, CardConfig card, int combo, long manaBeforePayment,
            ref int totalDamage, ref bool anyCrit, ref bool anyKill,
            ref int firstTargetIndex, ref int targetIndex)
        {
            if (!AttackPlanBuilder.TryFromDamageEffect(
                    card,
                    spec,
                    _rules,
                    out DamageSpec damage))
                return;
            AttackPlan resolvedPlan = ResolveAttackPlan(AttackPlanBuilder.Build(
                    damage,
                    new CombatStatSnapshot(_player, combo,
                        CardSealRules.GetHeldAttackMultiplier(_deckMgr.Hand), manaBeforePayment,
                        _runState.RunLuckBonus),
                    CardAttackSnapshot.Capture(card)),
                ref totalDamage, ref anyCrit, ref anyKill, ref firstTargetIndex, ref targetIndex, card, spec);
            if (spec.repeatAtPlayerTurnEnd)
                OnPeriodicAttackPlanResolved?.Invoke(card, resolvedPlan);
            State.PeriodicAttacks.RegisterResolvedAttack(card, spec, resolvedPlan);
        }

        private void ResolveAttack(CardConfig card, int baseDamage, int baseAttackInstanceCount, int combo,
            ref int totalDamage, ref bool anyCrit, ref bool anyKill,
            ref int firstTargetIndex, ref int targetIndex)
        {
            DamageSpec damage = AttackPlanBuilder.FromLegacy(card, baseDamage, baseAttackInstanceCount, _rules);
            damage = CardSealRules.ApplyDamageMultiplier(
                damage, card.seals);
            ResolveAttackPlan(AttackPlanBuilder.Build(
                    damage,
                    new CombatStatSnapshot(_player, combo,
                        CardSealRules.GetHeldAttackMultiplier(_deckMgr.Hand),
                        runLuckBonus: _runState.RunLuckBonus),
                    CardAttackSnapshot.Capture(card)),
                ref totalDamage, ref anyCrit, ref anyKill, ref firstTargetIndex, ref targetIndex, card);
        }

        private void ResolveShieldBash(CardConfig sourceCard,
            ref int totalDamage, ref bool anyCrit, ref bool anyKill,
            ref int firstTargetIndex, ref int targetIndex)
        {
            if (AttackResolver.GetAttackableTargets(_enemies).Count == 0) return;
            int damage = Math.Max(0, _player.armor);
            AttackPlan plan = AttackPlanBuilder.BuildFixedDirect(
                CardAttackSnapshot.Capture(sourceCard),
                damage,
                TargetPolicy.SequentialRightToLeft);
            int damageBefore = totalDamage;
            ResolveAttackPlan(plan, ref totalDamage, ref anyCrit, ref anyKill,
                ref firstTargetIndex, ref targetIndex);
            OnRuleTriggered?.Invoke(new CombatRuleTriggeredEvent(
                CombatRuleTriggerKind.ShieldBash, Math.Max(0, totalDamage - damageBefore)));
        }

        private void ApplyCardSeals(CardConfig card)
        {
            if (card?.seals == null) return;
            for (int i = 0; i < card.seals.Count; i++)
            {
                CardSealSpec seal = card.seals[i];
                if (seal == null || seal.value <= 0) continue;
                if (seal.effect == CardSealEffect.Luck)
                {
                    int gained = _runState.AddLuck(seal.value);
                    if (gained > 0) OnLog?.Invoke($"☘ 天运印：本局天运 +{gained}");
                }
                else if (seal.effect == CardSealEffect.Greed)
                {
                    int gained = _runState.AddGreed(seal.value, _economy.Config.runGreedMax);
                    if (gained > 0) OnLog?.Invoke($"玉 敛玉印：本局敛玉 +{gained}");
                }
            }
        }

        // 灵印种类、顺序和固定倍率由 CardSealRules 拥有；此处仅执行后置效果。
        private (int ArmorGained, int ManaGained) ApplyPostPlayCardSeals(
            CardConfig card, List<CombatStatChange> resolvedStatChanges,
            Action<AttackPlan> resolveAttack,
            ManaPaymentReceipt payment = default, bool isRealPlay = false)
        {
            int armorGained = 0;
            int manaGained = 0;
            IReadOnlyList<CardSealPostPlayAction> actions =
                CardSealRules.GetPostPlayActions(card?.seals, payment);
            for (int i = 0; i < actions.Count; i++)
            {
                CardSealPostPlayAction action = actions[i];
                if (action.CopyCount > 0)
                {
                    // The harness has no successful hand/payment transaction.
                    // Periodic attacks and previews never call this real-play boundary.
                    if (isRealPlay)
                    {
                        IReadOnlyList<CardConfig> copied = _deckMgr.GrantBaseCopiesToHand(card, action.CopyCount);
                        if (copied.Count > 0)
                            OnCardsCopied?.Invoke(new CombatCardsCopiedEvent(card, action, copied));
                    }
                    continue;
                }
                if (action.TryPrepareAttack(card, _player, _enemies,
                        out AttackPlan sealPlan, out int armorCost))
                {
                    // Pay the frozen cost once at attack submission, before damage
                    // callbacks can grant new armor. Never alter Run/base armor.
                    _player.armor -= armorCost;
                    resolveAttack(sealPlan);
                    continue;
                }
                CardEffectSpec effect = action.Effect;
                if (effect == null) continue;
                switch (effect.effectType)
                {
                    case EffectType.DrawCards:
                        DrawCardsInternal(effect.count);
                        break;
                    case EffectType.GainArmor:
                        _player.armor += effect.amount;
                        armorGained += effect.amount;
                        break;
                    case EffectType.GainMana:
                        int manaBefore = _mana.TemporaryMana;
                        _mana.AddTemporaryMana(effect.amount);
                        manaGained += _mana.TemporaryMana - manaBefore;
                        break;
                    case EffectType.ModifyStat:
                        int before = GetCurrentStatValue(effect.statType);
                        ModifyPlayerStat(effect.statType, effect.amount);
                        int after = GetCurrentStatValue(effect.statType);
                        int gained = after - before;
                        if (gained == 0) break;
                        int existing = resolvedStatChanges.FindIndex(
                            change => change.StatType == effect.statType);
                        if (existing >= 0)
                        {
                            gained += resolvedStatChanges[existing].Amount;
                            resolvedStatChanges[existing] = new CombatStatChange(
                                effect.statType, gained, after);
                        }
                        else
                            resolvedStatChanges.Add(new CombatStatChange(
                                effect.statType, gained, after));
                        break;
                }
            }
            return (armorGained, manaGained);
        }

        public JadeGainResult GrantStageClearJade()
        {
            if (_stageClearJadeGranted)
                return new JadeGainResult(JadeSource.StageClear, 0, 0, true);
            _stageClearJadeGranted = true;
            return _economy.Grant(_economy.Config.stageClearJade, CurrentGreed,
                JadeSource.StageClear, true);
        }

        /// <summary>
        /// 统一终局入口。收口所有设置 BattleVictory/BattleDefeat 的路径。
        /// 顺序：设置最终 BattlePhase → 胜利收益结算 → OnStateChanged(CombatEnded) → OnCombatEnded。
        /// 用 <see cref="_publishedOutcome"/> 保证每场只发布一次。
        /// </summary>
        private bool TryFinishCombat()
        {
            if (_publishedOutcome.HasValue)
            {
                _bossOverkill.End();
                State.PeriodicAttacks.Clear();
                State.TerrainEffects.ClearAll();
                State.ColumnReplays.Clear();
                State.MugMarks.Clear();
                return true;
            }

            CombatOutcome outcome;
            if (State.IsDefeat)
                outcome = CombatOutcome.Defeat;
            else if (State.IsVictory)
                outcome = CombatOutcome.Victory;
            else
                return false;

            State.PeriodicAttacks.Clear();
            State.TerrainEffects.ClearAll();
            State.ColumnReplays.Clear();
            State.MugMarks.Clear();
            if (outcome == CombatOutcome.Victory)
            {
                // 敌人已全部解决不等于终局已经可发布。升级奖励与延迟
                // 经验仍是战斗内的权威检查点；在它们完成前必须同时给出
                // 不可输入的安全阶段，避免余威会话已关闭却仍留在 Overkill。
                if (State.AwaitingRewardSelection)
                {
                    State.CurrentPhase = BattlePhase.AwaitingReward;
                    return false;
                }

                if (HasPendingExperienceAwards)
                {
                    State.CurrentPhase = BattlePhase.CardResolving;
                    return false;
                }
            }

            if (outcome == CombatOutcome.Victory)
            {
                if (_encounterRecovery.TryConsumeAfterEncounter(
                        out EncounterRecoveryReceipt recoveryReceipt) &&
                    recoveryReceipt.ActualHealthChange > 0)
                {
                    OnLog?.Invoke(
                        $"✦ 战后调息：恢复 {recoveryReceipt.ActualHealthChange} 点气血" +
                        (recoveryReceipt.RequestedRecovery != recoveryReceipt.ActualHealthChange
                            ? $"（请求 {recoveryReceipt.RequestedRecovery}）" : string.Empty));
                }
            }
            else
            {
                _encounterRecovery.Discard();
            }

            _bossOverkill.End();
            _publishedOutcome = outcome;
            // Overrides expire before Run observers capture residents; duration is retained.
            _colorTrigger.ResetActiveTriggerColors();
            _enemyTurnSequence.Clear();
            State.EnemyInterrupts.Clear();
            State.EnemyAi.ClearActionReceipts();

            State.CurrentPhase = outcome == CombatOutcome.Victory
                ? BattlePhase.BattleVictory
                : BattlePhase.BattleDefeat;

            if (outcome == CombatOutcome.Victory)
                GrantStageClearJade();

            // 展卷与引卷的战斗内增量只属于本遭遇；结束事件看到的是入场基线。
            _deckMgr.ResetEncounterModifiers();
            _player.shouPai = _deckMgr.HandLimit;
            _player.yinJuan = _deckMgr.ExplicitDrawBonus;

            // 敌方注入的碎甲等遭遇临时卡绝不跨越本场结束边界。清理发生在
            // OnCombatEnded 前，因而 DungeonRunSession 以及任何存档/回放接收
            // 的都是纯持久牌组。
            int removedTemporaryCards =
                _deckMgr.RemoveEncounterTemporaryCards();
            if (removedTemporaryCards > 0)
            {
                OnLog?.Invoke(
                    $"… 遭遇结束，移除临时卡 ×{removedTemporaryCards}");
            }

            OnStateChanged?.Invoke(CombatStateChangeReason.CombatEnded);
            OnCombatEnded?.Invoke(new CombatEndedEvent(outcome));
            return true;
        }

        public int PreviewCashOut()
            => EconomySystem.CalculateFinalJade(
                _economy.Config.cashOutJade,
                CurrentGreed,
                affectedByGreed: true);

        public JadeGainResult CashOut()
            => _economy.CashOut(CurrentGreed);

        /// <summary>将当前整组升级/灵印奖励化为灵玉；不保留任何候选项。</summary>
        public bool CashOutPendingRewards()
            => TryCashOutPendingRewards(out _);

        public bool TryCashOutPendingRewards(out JadeGainResult result)
        {
            result = default;
            if (!CanCashOutPendingRewards) return false;
            result = CashOut();
            ClearPendingRewardOffer();
            _pendingSealReward = null;
            _pendingRewardsAllowCashOut = true;
            State.AwaitingRewardSelection = false;
            State.CurrentPhase = _enemyTurnSequence.IsActive ? BattlePhase.EnemyActing : BattlePhase.PlayerInput;
            OnLog?.Invoke($"💰 化为灵玉 · +{result.FinalJade}");
            OnStateChanged?.Invoke(CombatStateChangeReason.RewardSelected);
            bool wasDeferredCheckpoint = _awaitingDeferredExperienceReward;
            _awaitingDeferredExperienceReward = false;
            if (!wasDeferredCheckpoint)
                ProcessPendingLevelUps();
            StoreProgression();
            RestorePhaseAfterProgression();
            return true;
        }

        private void GrantOverkillFromReceipt(DamageEvent damageEvent)
        {
            // OverkillDamage 只能由 DamageSystem 对 BossOverkillSession 的活动
            // 尸体产出；控制器只消费数值回执，不再二次拥有资格规则。
            if (damageEvent.OverkillDamage <= 0) return;
            _economy.GrantOverkillDamage(damageEvent.OverkillDamage, CurrentGreed,
                _rules.ultraOverkillEnabled);
        }

        private AttackPlan ResolveAttackPlan(AttackPlan plan,
            ref int totalDamage, ref bool anyCrit, ref bool anyKill,
            ref int firstTargetIndex, ref int targetIndex, CardConfig sealHost = null,
            CardEffectSpec replayEffect = null)
        {
            AttackProjection projection =
                _executingAttackProjection != null &&
                _executingAttackProjection.Plan.SourceCardInstanceId ==
                plan.SourceCardInstanceId
                    ? _executingAttackProjection
                    : null;
            _executingAttackProjection = null;
            if (projection != null)
                plan = projection.Plan;

            OnAttackPlanResolved?.Invoke(plan);

            AttackSubHit firstSubHit = plan.SubHits.Count > 0
                ? plan.SubHits[0]
                : null;
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[DamageTrace][Plan:Actual] sourceCard={plan.SourceCardInstanceId} " +
                    $"might={_player.fengRui} " +
                    $"amount={_player.amount} " +
                    $"combo={_combo.Combo} " +
                    $"subHits={plan.SubHits.Count} " +
                    $"displayedCardDamage={plan.DisplayedCardDamage} " +
                    $"maxDirectEvents={plan.MaximumDirectEventCount} " +
                    $"maxBounceEvents={plan.MaximumBounceEventCount} " +
                    $"areaStat={plan.AreaStat} " +
                    $"firstHitDamage={firstSubHit?.NormalHitDamage ?? 0} " +
                    $"firstTargetPolicy={firstSubHit?.TargetPolicy.ToString() ?? "None"} " +
                    $"firstDirectTargets={firstSubHit?.DirectTargetCount ?? 0} " +
                    $"firstBounceHits={firstSubHit?.Bounce?.hitsPerDirectHit ?? 0} " +
                    $"firstBounceMode={firstSubHit?.Bounce?.sequenceMode.ToString() ?? "None"}");
            IRandomSource resolutionRandom = projection != null
                ? projection.CreateReplayRandomSource()
                : State.Random ?? DefaultRandomSource.Instance;
            IReadOnlyList<EnemyIntent> resolutionIntents = projection != null
                ? projection.UpcomingIntents
                : State.EnemyAi?.GenerateIntents();
            int mugBaseJade = CardSealRules.GetMugBaseReward(sealHost?.seals);
            var resolved = AttackResolver.Execute(plan, _enemies,
                ResolutionMode.Actual, resolutionRandom,
                resolutionIntents, _bossOverkill,
                activeTerrainEffects: State.TerrainEffects?.Active);
            if (resolved.ProjectileTraces.Count > 0)
                OnProjectileTracesResolved?.Invoke(new ProjectileTraceBatch(
                    plan.SourceCardInstanceId, resolved.ProjectileTraces));
            State.ColumnReplays.RegisterResolvedAttack(sealHost, replayEffect, resolved);
            // Only base host calls carry sealHost. Post-play seal attacks, Shield Bash
            // and automatic plans never use the host-Mug registration boundary.
            State.MugMarks.ObserveHostHits(sealHost?.runtimeInstanceId ?? 0, mugBaseJade, resolved.Events);
            PublishAttackResolution(resolved, ref totalDamage, ref anyCrit, ref anyKill,
                ref firstTargetIndex, ref targetIndex);
            return plan;
        }

        /// <summary>
        /// Complete validation and fallible card preparation precede numeric effects.
        /// Primary state/log observers see its real result before secondary enters a
        /// card zone; primary observer exceptions are not preparation failures.
        /// </summary>
        private bool TryExecuteCompoundEnemyIntent(
            EnemyStats enemy,
            EnemyIntent intent,
            out EnemyTemporaryCardGrantResult grant,
            out string error)
        {
            grant = default;
            error = null;
            if (intent.Type != EnemyIntentType.Compound ||
                (intent.PrimaryType != EnemyIntentType.Attack &&
                 intent.PrimaryType != EnemyIntentType.Defend &&
                 intent.PrimaryType != EnemyIntentType.Wait) ||
                (intent.SecondaryType != EnemyIntentType.AddEncounterTemporaryCard &&
                 intent.SecondaryType != EnemyIntentType.Attack &&
                 intent.SecondaryType != EnemyIntentType.Defend &&
                 intent.SecondaryType != EnemyIntentType.Wait))
            {
                error = "复合敌方意图的主/次行动类型不受支持。";
                return false;
            }

            if (intent.SecondaryType == EnemyIntentType.AddEncounterTemporaryCard)
            {
                try
                {
                    grant = _deckMgr.GrantEnemyTemporaryCardsAfter(
                        intent.SecondaryTemporaryCard, ResolveEnemyActionTurnContext(),
                        () => ApplyCompoundEnemyPrimary(enemy, intent));
                }
                catch (EnemyTemporaryCardPreparationException exception)
                {
                    error = exception.InnerException.Message;
                    return false;
                }
                if (!grant.CanceledByEncounterEnd)
                    OnLog?.Invoke(
                        $"✦ {enemy.name} {intent.SecondaryDescription} · " +
                        $"临时卡 {intent.SecondaryTemporaryCard.ContentId} " +
                        $"×{grant.ActualCount}" +
                        (grant.LimitReached ? "（已达同源残留上限）" : string.Empty));
                return true;
            }

            ApplyCompoundEnemyPrimary(enemy, intent);
            switch (intent.SecondaryType)
            {
                case EnemyIntentType.Attack:
                    ApplyPlayerDamage(Math.Max(0, intent.SecondaryAmount));
                    break;
                case EnemyIntentType.Defend:
                    int secondaryArmor = Math.Max(0, intent.SecondaryAmount);
                    enemy.armor = enemy.maxArmor > 0
                        ? Math.Min(enemy.maxArmor, enemy.armor + secondaryArmor)
                        : enemy.armor + secondaryArmor;
                    break;
                case EnemyIntentType.Wait:
                    break;
            }
            return true;
        }

        private void ApplyCompoundEnemyPrimary(EnemyStats enemy, EnemyIntent intent)
        {
            switch (intent.PrimaryType)
            {
                case EnemyIntentType.Attack:
                    var damage = ApplyPlayerDamage(intent.Amount);
                    OnLog?.Invoke(
                        $"⚔ {enemy.name} {intent.Description} -{intent.Amount}！" +
                        (damage.BecameFatal
                            ? " 玩家陨落！"
                            : $" 剩余 HP: {damage.RemainingHealth}"));
                    OnStateChanged?.Invoke(CombatStateChangeReason.PlayerDamaged);
                    break;
                case EnemyIntentType.Defend:
                    int requestedArmor = Math.Max(0, intent.Amount);
                    int armorAfter = enemy.maxArmor > 0
                        ? Math.Min(enemy.maxArmor, enemy.armor + requestedArmor)
                        : enemy.armor + requestedArmor;
                    int appliedArmor = armorAfter - enemy.armor;
                    enemy.armor = armorAfter;
                    OnLog?.Invoke($"🛡 {enemy.name} 防御 +{appliedArmor}");
                    break;
                case EnemyIntentType.Wait:
                    OnLog?.Invoke($"… {enemy.name} {intent.Description}");
                    break;
            }
        }

        // Both played and automatic plans publish through this existing attack consumer;
        // only TryPlayCard performs payment, seals, color triggers and real-play statistics.
        private void PublishAttackResolution(AttackResolutionResult resolved,
            ref int totalDamage, ref bool anyCrit, ref bool anyKill,
            ref int firstTargetIndex, ref int targetIndex)
        {
            var preparedDefeats = PrepareResolvedDefeats(resolved.Defeats);
            State.TerrainEffects?.Schedule(
                resolved.TerrainEffectPlacements);
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[DamageTrace][Resolved] " +
                    $"events={resolved.Events.Count} " +
                    $"totalApplied={resolved.TotalAppliedDamage} " +
                    $"defeats={resolved.Defeats.Count}");

            PublishResolvedControlEffects(resolved.ControlEffects);

            bool first = true;
            for (int i = 0; i < resolved.Events.Count; i++)
            {
                DamageEvent damageEvent = resolved.Events[i];
                if (first && damageEvent.Kind == DamageKind.Direct)
                {
                    firstTargetIndex = damageEvent.TargetIndex;
                    first = false;
                }
                totalDamage += damageEvent.AppliedDamage;
                GrantOverkillFromReceipt(damageEvent);
                anyCrit |= damageEvent.IsCritical;
                TraceDamageEvent(damageEvent, "AttackPlan");
                OnDamageEvent?.Invoke(damageEvent);
                OnEnemyDamaged?.Invoke(damageEvent.TargetIndex, damageEvent.AppliedDamage,
                    damageEvent.IsCritical, damageEvent.IsFatal);
            }

            PublishResolvedDamageDefeats(preparedDefeats, ref anyKill);
            if (resolved.Events.Count > 0) targetIndex = firstTargetIndex;
        }

        // This is the existing publication boundary, shared with F7. It owns no
        // visual state and neither reruns control resolution nor consumes RNG.
        private void PublishResolvedControlEffects(IReadOnlyList<ControlEffectResult> controls)
        {
            for (int i = 0; i < controls.Count; i++)
            {
                ControlEffectResult control = controls[i];
                if (control.EffectType == OnHitEffectType.Knockback)
                {
                    _enemyBackfill.RegisterKnockbackBoundaryCrossings(
                        control.PushedBeyondVisibleIndices);
                    OnControlEffectResolved?.Invoke(control);
                    var affected = new List<int>(control.AffectedIndices);
                    OnEnemiesKnockedBack?.Invoke(affected);
                    OnLog?.Invoke($"💨 {_enemies[control.TargetIndex].name} 触发震退，连锁移动 {affected.Count} 个敌人");
                }
                else
                {
                    OnControlEffectResolved?.Invoke(control);
                    OnLog?.Invoke($"⛓ {_enemies[control.TargetIndex].name} 被缴械（最多 1 层）");
                }
            }
        }

        private void ResolveSpecialDamage(AttackResolutionResult resolved,
            ref int totalDamage, ref bool anyCrit, ref bool anyKill,
            ref int firstTargetIndex, ref int targetIndex, CardConfig sealHost = null)
        {
            State.MugMarks.ObserveHostHits(sealHost?.runtimeInstanceId ?? 0,
                CardSealRules.GetMugBaseReward(sealHost?.seals), resolved.Events);
            var preparedDefeats = PrepareResolvedDefeats(resolved.Defeats);
            for (int i = 0; i < resolved.Events.Count; i++)
            {
                var e = resolved.Events[i];
                if (i == 0) firstTargetIndex = e.TargetIndex;
                totalDamage += e.AppliedDamage;
                GrantOverkillFromReceipt(e);
                anyCrit |= e.IsCritical;
                TraceDamageEvent(e, "Special");
                OnDamageEvent?.Invoke(e);
                OnEnemyDamaged?.Invoke(e.TargetIndex, e.AppliedDamage, e.IsCritical, e.IsFatal);
                targetIndex = e.TargetIndex;
            }
            PublishResolvedDamageDefeats(preparedDefeats, ref anyKill);
        }

        /// <summary>
        /// 校验并冻结整批死亡身份，在任何可能刷新阵型的回执前一次发布。
        /// 回执与敌人引用只属于本次局部结算；死亡保留仍由表现消费者独占。
        /// </summary>
        private List<(EnemyDefeatResult Defeat, EnemyStats Enemy)> PrepareResolvedDefeats(
            IReadOnlyList<EnemyDefeatResult> defeats)
        {
            var validatedDefeats = new List<(EnemyDefeatResult Defeat, EnemyStats Enemy)>();
            if (defeats == null || defeats.Count == 0) return validatedDefeats;

            var publishedRuntimeIds = new HashSet<int>();
            for (int i = 0; i < defeats.Count; i++)
            {
                EnemyDefeatResult defeat = defeats[i];
                int targetIndex = defeat.TargetIndex;
                if (targetIndex < 0 || targetIndex >= _enemies.Count)
                    throw new InvalidOperationException(
                        $"伤害死亡回执指向越界目标：index={targetIndex}。");

                EnemyStats enemy = _enemies[targetIndex];
                if (enemy == null ||
                    enemy.runtimeId != defeat.TargetEnemyRuntimeId)
                {
                    throw new InvalidOperationException(
                        "伤害死亡回执的 runtimeId 与目标索引不一致。" +
                        $" receipt={defeat.TargetEnemyRuntimeId}, " +
                        $"index={targetIndex}, " +
                        $"actual={enemy?.runtimeId ?? 0}。");
                }
                if (!publishedRuntimeIds.Add(defeat.TargetEnemyRuntimeId))
                    continue;
                validatedDefeats.Add((defeat, enemy));
            }

            if (validatedDefeats.Count > 0)
            {
                var snapshot = new EnemyDefeatResult[validatedDefeats.Count];
                for (int i = 0; i < validatedDefeats.Count; i++)
                    snapshot[i] = validatedDefeats[i].Defeat;
                OnEnemyDefeatsPrepared?.Invoke(Array.AsReadOnly(snapshot));
            }
            return validatedDefeats;
        }

        /// <summary>
        /// 消费已预登记的死亡事实，Cause保持原值；先全批死亡通知，再逐敌击杀/经验。
        /// 仅编排既有事件及奖励副作用，不拥有表现保留状态或增加长期状态。
        /// </summary>
        private void PublishResolvedDamageDefeats(
            List<(EnemyDefeatResult Defeat, EnemyStats Enemy)> validatedDefeats,
            ref bool anyKill,
            DamageDefeatPublicationPolicy policy = DamageDefeatPublicationPolicy.AuthoritativeCombat,
            bool beginBossOverkill = false)
        {
            if (validatedDefeats.Count == 0) return;
            anyKill = true;
            bool resetForReplay = policy ==
                DamageDefeatPublicationPolicy.HarnessResetForReplay;
            for (int i = 0; i < validatedDefeats.Count; i++)
            {
                PublishEnemyDefeat(
                    validatedDefeats[i].Defeat,
                    createsFormationVacancy: !resetForReplay,
                    settleMugMarks: policy == DamageDefeatPublicationPolicy.AuthoritativeCombat,
                    defeatBatchPrepared: true,
                    resolveDeathActions: policy == DamageDefeatPublicationPolicy.AuthoritativeCombat);
                if (beginBossOverkill)
                    _bossOverkill.TryBeginAfterDefeat(validatedDefeats[i].Defeat);
            }

            for (int i = 0; i < validatedDefeats.Count; i++)
            {
                EnemyStats enemy = validatedDefeats[i].Enemy;
                int targetIndex = validatedDefeats[i].Defeat.TargetIndex;
                OnLog?.Invoke($"✦ {enemy.name} 被击败！");
                OnEnemyKilled?.Invoke(enemy);
                State.Statuses?.ClearEnemy(targetIndex);
                OnStateChanged?.Invoke(CombatStateChangeReason.EnemyKilled);
                if (policy ==
                    DamageDefeatPublicationPolicy.AuthoritativeCombat)
                {
                    AwardEnemyExperience(enemy);
                }
                else if (resetForReplay)
                {
                    enemy.currentHealth = enemy.baseHealth;
                    enemy.armor = 0;
                }
            }
        }

        private void ResolveEraseDefeat(TargetRule rule, int sourceCardInstanceId,
            int targetIndex, ref bool anyKill,
            ref int firstTargetIndex, ref int currentTargetIndex)
        {
            EraseResolutionResult resolved = EraseResolver.Execute(
                _enemies, rule, targetIndex, sourceCardInstanceId);
            var preparedDefeats = PrepareResolvedDefeats(resolved.Defeats);
            OnEraseResolved?.Invoke(resolved);
            PublishResolvedDamageDefeats(preparedDefeats, ref anyKill);
            if (resolved.Defeats.Count == 0) return;
            firstTargetIndex = currentTargetIndex = resolved.Defeats[0].TargetIndex;
        }

        private void TraceDamageEvent(DamageEvent damageEvent, string origin)
        {
            if (!CombatLogger.EnableVerboseDiagnostics) return;
            EnemyStats enemy = damageEvent.TargetIndex >= 0 &&
                damageEvent.TargetIndex < _enemies.Count
                    ? _enemies[damageEvent.TargetIndex]
                    : null;
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[DamageTrace][Core:{origin}] " +
                    $"sourceCard={damageEvent.SourceCardInstanceId} " +
                    $"runtime={damageEvent.TargetEnemyRuntimeId} " +
                    $"index={damageEvent.TargetIndex} " +
                    $"attack={damageEvent.AttackInstanceIndex} " +
                    $"subHit={damageEvent.SubHitIndex} " +
                    $"kind={damageEvent.Kind} " +
                    $"requested={damageEvent.RequestedDamage} " +
                    $"applied={damageEvent.AppliedDamage} " +
                    $"shield={damageEvent.ShieldDamage} " +
                    $"hp={damageEvent.HpDamage} " +
                    $"overkill={damageEvent.OverkillDamage} " +
                    $"crit={damageEvent.IsCritical} " +
                    $"fatal={damageEvent.IsFatal} " +
                    $"remainingHp={enemy?.currentHealth ?? -1} " +
                    $"remainingArmor={enemy?.armor ?? -1} " +
                    $"row={enemy?.row ?? -1} " +
                    $"column={enemy?.column ?? -1} " +
                    $"visible={EnemyFormationLayout.IsFullyInsideVisibleRows(enemy)}");
        }

        /// <summary>
        /// Ordinary healing requires a living player. Fatal hits first pass through
        /// PlayerDamageReceiver's revival boundary; a later effect in the same card
        /// cannot replace a missing revival. Spec, legacy and replay healing share
        /// this eligibility and multiplier boundary.
        /// </summary>
        private int ApplyHealingRules(int requestedAmount)
        {
            if (_player.currentHealth <= 0) return 0;
            int amount = Math.Max(0, requestedAmount);
            if (amount == 0 || !_rules.experimentalMedicineEnabled)
                return amount;
            return amount > int.MaxValue / 2
                ? int.MaxValue
                : amount * 2;
        }

        /// <summary>
        /// Commits all deferred modifiers and reports their actual deltas.
        /// Multiple effects for one stat are intentionally collapsed into one
        /// result so presentation does not depend on effect-list structure.
        /// </summary>
        private List<CombatStatChange> ApplyDeferredStatChanges(
            List<KeyValuePair<StatType, int>> deferredStats)
        {
            var results = new List<CombatStatChange>();
            if (deferredStats == null || deferredStats.Count == 0)
                return results;

            var startingValues = new Dictionary<StatType, int>();
            var orderedStats = new List<StatType>();
            for (int i = 0; i < deferredStats.Count; i++)
            {
                StatType stat = deferredStats[i].Key;
                if (!startingValues.ContainsKey(stat))
                {
                    startingValues.Add(stat, GetCurrentStatValue(stat));
                    orderedStats.Add(stat);
                }
                // Preserve configured effect order because capped stats can
                // produce a different result when positive and negative
                // modifiers are collapsed before application.
                ModifyPlayerStat(stat, deferredStats[i].Value);
            }

            for (int i = 0; i < orderedStats.Count; i++)
            {
                StatType stat = orderedStats[i];
                int after = GetCurrentStatValue(stat);
                int actualDelta = after - startingValues[stat];
                if (actualDelta != 0)
                {
                    results.Add(new CombatStatChange(
                        stat,
                        actualDelta,
                        after));
                }
            }

            return results;
        }

        /// <summary>修改玩家属性</summary>
        private void ModifyPlayerStat(StatType stat, int amount)
        {
            switch (stat)
            {
                case StatType.FengRui: _player.fengRui += amount; break;
                case StatType.Amount: _player.amount += amount; break;
                case StatType.BengLie: _player.bengLie += amount; break;
                case StatType.NingShen:
                    _colorTrigger.ResolveDurationGain(amount);
                    OnStateChanged?.Invoke(CombatStateChangeReason.CrawlerChanged);
                    break;
                case StatType.ShouPai:
                    _deckMgr.IncreaseHandLimit(amount);
                    _player.shouPai = _deckMgr.HandLimit;
                    break;
                case StatType.ChouPai:
                    _deckMgr.IncreaseExplicitDrawBonus(amount);
                    _player.yinJuan = _deckMgr.ExplicitDrawBonus;
                    break;
                case StatType.TianYun: _runState.AddLuck(amount); break;
                case StatType.LianYu: _runState.AddGreed(amount, _economy.Config.runGreedMax); break;
                case StatType.MingWu:
                    _runState.AddGrowth(
                        amount,
                        Math.Max(
                            0,
                            RunProgressionState.DefaultGrowthCap -
                            Math.Max(0, _player.mingWu)));
                    break;
                case StatType.HunHun:
                    _runState.RevivalReserve.AddCharge(amount);
                    _player.hunHun = _runState.RevivalReserve.RemainingPercent;
                    break;
                case StatType.FaLi: _player.faLi += amount; break;
                case StatType.HuJia: _player.armor += amount; break;
                case StatType.ZhaoSha: _player.zhaoSha += amount; break;
                case StatType.Recovery: _encounterRecovery.GainResolvedAmount(amount); break;
            }
        }

        private int ResolveEffectTarget(int requestedIndex)
        {
            var attackable = AttackResolver.GetAttackableTargets(_enemies);
            if (attackable.Contains(requestedIndex)) return requestedIndex;
            if (attackable.Count == 0) return -1;

            int nearestRow = EnemyFormationLayout.GetPlayerTargetPriorityRow(
                _enemies[attackable[0]]);
            int rightmostNearest = attackable[0];
            for (int i = 1; i < attackable.Count; i++)
            {
                if (EnemyFormationLayout.GetPlayerTargetPriorityRow(
                        _enemies[attackable[i]]) != nearestRow) break;
                rightmostNearest = attackable[i];
            }
            return rightmostNearest;
        }

        /// <summary>获取每列当前最前敌人数量（宽体敌人去重）。</summary>
        public int GetTargetableEnemyCount()
            => AttackResolver.GetAttackableTargets(_enemies).Count;

        /// <summary>获取可被攻击的敌人索引列表</summary>
        public List<int> GetTargetableEnemyIndices()
            => AttackResolver.GetAttackableTargets(_enemies);

        private void PublishEnemyDefeat(
            EnemyDefeatResult defeat,
            bool createsFormationVacancy = true, bool settleMugMarks = true,
            bool defeatBatchPrepared = false, bool resolveDeathActions = true)
        {
            if (!defeatBatchPrepared)
                PrepareResolvedDefeats(new[] { defeat });
            if (settleMugMarks) State.MugMarks.ResolveDefeat(defeat);
            if (createsFormationVacancy)
                _enemyBackfill.RegisterDefeat(defeat.TargetIndex);
            if (resolveDeathActions) _enemyDeathActions.Resolve(defeat);
            OnEnemyDefeated?.Invoke(defeat);
        }

        private void PublishFormationAdvances(
            IReadOnlyList<FormationAdvanceBatch> batches)
        {
            if (batches == null) return;
            for (int i = 0; i < batches.Count; i++)
                PublishFormationAdvance(batches[i]);
        }

        private void PublishFormationAdvance(
            FormationAdvanceBatch batch,
            string diagnosticReason = null,
            string logDescription = null)
        {
            if (batch == null) return;
            IReadOnlyList<FormationAdvanceMove> moves = batch.Moves;
            if (moves == null || moves.Count == 0) return;

            diagnosticReason ??= batch.Cause switch
            {
                FormationAdvanceCause.WaveDeploy => "WaveDeploy",
                FormationAdvanceCause.WithdrawalBackfill =>
                    "WithdrawalBackfill",
                FormationAdvanceCause.EnemyTurnPreparation =>
                    "EnemyTurnPreparation",
                FormationAdvanceCause.KnockbackBoundaryReturn =>
                    "KnockbackBoundaryReturn",
                FormationAdvanceCause.EmptyFieldRecovery => "EmptyFieldRecovery",
                _ => "Backfill"
            };
            logDescription ??= batch.Cause switch
            {
                FormationAdvanceCause.WaveDeploy => "整波部署",
                FormationAdvanceCause.WithdrawalBackfill => "撤离补位",
                FormationAdvanceCause.EnemyTurnPreparation => "回合推进",
                FormationAdvanceCause.KnockbackBoundaryReturn => "震退归位",
                FormationAdvanceCause.EmptyFieldRecovery => "空场补位",
                _ => "死亡补位"
            };

            long advanceId = _nextFormationAdvanceId++;
            var eventSnapshot = new FormationAdvanceEvent(
                advanceId,
                batch.Cause,
                moves);
            var diagnostic = new StringBuilder();
            for (int i = 0; i < eventSnapshot.Moves.Count; i++)
            {
                FormationAdvanceMove move = eventSnapshot.Moves[i];
                diagnostic.Append(
                    $" [{move.FormationIndex}]runtime={move.RuntimeId}" +
                    $" r{move.FromFootprint.Row}c{move.FromFootprint.Column}" +
                    $"->r{move.ToFootprint.Row}c{move.ToFootprint.Column}");
            }
            Debug.Log(
                $"[Diag][{diagnosticReason}] id={advanceId} " +
                $"cause={batch.Cause} " +
                $"moved={eventSnapshot.Moves.Count}:{diagnostic}");
            OnLog?.Invoke(
                $"↥ {logDescription}：{eventSnapshot.Moves.Count} 名敌人前移");
            OnFormationAdvanced?.Invoke(eventSnapshot);
        }

        private void TryPublishLevelBossArrival()
        {
            if (_levelBossEncounter == null ||
                !_levelBossEncounter.TryArrive(
                    out LevelBossArrivalEvent arrival))
            {
                return;
            }

            EnemyStats boss = _enemies[arrival.BossIndex];
            Debug.Log(
                $"[LevelBoss] arrived runtime={arrival.BossRuntimeId} " +
                $"index={arrival.BossIndex} row={boss.row} column={boss.column}");
            OnLog?.Invoke($"⚔ 关 BOSS {boss.name} 降临！");
            // 先刷新权威可见状态，再让表现协调器同步保留并隐藏角色，
            // 避免到达事件捕获到待命期的 disabled Renderer。
            OnStateChanged?.Invoke(CombatStateChangeReason.LevelBossArrived);
            OnLevelBossArrived?.Invoke(arrival);
        }

        /// <summary>结束回合</summary>
        public void EndTurn()
        {
            if (!State.CanEndTurn) return;
            _attackPreviews.Prepare();
            int endingTurn = State.CurrentTurn;
            IDisposable deferredPreviews = _attackPreviews.Suspend();
            try
            {
                bool advanceBossTimer = IsBossTimerActive;
                ClearFreeRewardCards();

                if (State.CurrentPhase == BattlePhase.Overkill)
                {
                    _bossOverkill.End();
                    _deckMgr.DiscardHand();
                    OnStateChanged?.Invoke(CombatStateChangeReason.TurnEnded);
                    OnTurnEnd?.Invoke();
                    TryFinishCombat();
                    return;
                }

                OnLog?.Invoke($"=== 回合结束 === {_combo.GetSummary()}");
                _combo.Reset();

                if (_bossTimer != null && advanceBossTimer)
                {
                    _bossTimer.OnTurnEnd();
                    if (_bossTimer.IsTriggered)
                        OnLog?.Invoke($"  {_bossTimer.GetSummary()}");
                }

                _deckMgr.DiscardHandAtTurnEnd();
                EnemyBackfillCheckpoint statusBackfillCheckpoint =
                    _enemyBackfill.BeginResolution();
                State.Statuses?.OnTurnEnded();
                State.PeriodicAttacks.OnPlayerTurnEnded();
                State.ColumnReplays.OnPlayerTurnEnded();
                PublishFormationAdvances(
                    _enemyBackfill.AdvanceAfterResolution(
                        statusBackfillCheckpoint));
                PublishFormationAdvance(
                    _enemyBackfill.ReturnKnockbackBoundaryCrossersAtTurnEnd(),
                    "KnockbackBoundaryReturn",
                    "回合末震退归位");
                TryPublishLevelBossArrival();
                OnStateChanged?.Invoke(CombatStateChangeReason.TurnEnded);
                OnTurnEnd?.Invoke();

                // 灼烧等回合末伤害可以击败 Boss，但玩家已经提交了
                // EndTurn；此时应直接收束，不让下个回合继承上回合的余威状态。
                if (State.HasOverkillTarget)
                {
                    _bossOverkill.End();
                    TryFinishCombat();
                    return;
                }

                if (State.IsCombatOver())
                    TryFinishCombat();
            }
            finally
            {
                deferredPreviews.Dispose();
                // Some hosts complete enemy actions synchronously from OnTurnEnd.
                // Their nested StartTurn cannot publish exact choices while this
                // transaction is suspended; publish them after it fully unwinds.
                if (State.CurrentTurn != endingTurn && _attackPreviews.Prepare())
                    OnDamagePreviewsReady?.Invoke();
            }
        }

        /// <summary>
        /// 消费一张升级奖励卡的单次免耗标记。只有由本控制器授予并登记的
        /// 当前回合奖励卡会受影响，其他来源的免耗状态不在这里改写。
        /// </summary>
        private void ConsumeFreeRewardCardOnPlay(CardConfig card)
        {
            if (card == null || !_freeRewardCardsThisTurn.Remove(card))
                return;

            card.isFreeToPlay = false;
            Debug.Log($"[Diag][FreeConsume] id={card.contentId} consumed");
        }

        /// <summary>清除未出牌升级奖励卡的当回合免耗标记（回合结束即失效）。</summary>
        private void ClearFreeRewardCards()
        {
            for (int i = 0; i < _freeRewardCardsThisTurn.Count; i++)
            {
                CardConfig card = _freeRewardCardsThisTurn[i];
                if (card == null) continue;
                Debug.Log($"[Diag][FreeClear] id={card.contentId} cleared");
                card.isFreeToPlay = false;
            }
            _freeRewardCardsThisTurn.Clear();
        }

        /// <summary>
        /// Selects an ordinary level-up reward, or begins the second phase for
        /// a typed seal reward.  The caller must submit the current offer
        /// revision together with the exact candidate instance that was read
        /// from that offer.  A seal does not finish the level-up until a legal
        /// exact card instance is chosen and revalidated by Core.
        /// </summary>
        public bool TrySelectLevelUpReward(
            LevelUpSystem.Reward reward,
            long expectedOfferRevision,
            out string error)
        {
            error = null;
            if (!State.AwaitingRewardSelection)
            {
                error = "当前没有待选择的升级奖励。";
                return false;
            }
            if (_pendingSealReward != null)
            {
                error = "当前灵印仍在等待选择宿主。";
                return false;
            }
            if (expectedOfferRevision != _pendingRewardOfferRevision)
            {
                error = "升级奖励请求已过期，请重新读取当前候选。";
                return false;
            }
            LevelUpSystem.Reward selected = FindPendingLevelUpReward(reward);
            if (selected == null)
            {
                error = "升级奖励不是当前 offer 的原始候选。";
                return false;
            }
            if (_levelUp == null)
            {
                error = "升级奖励系统尚未初始化。";
                return false;
            }

            if (selected.type == LevelUpSystem.RewardType.Gem)
            {
                if (_canReceiveSeals?.Invoke() != true)
                {
                    error = "尚未取得玉印，无法选择灵印奖励。";
                    return false;
                }
                if (selected.seal == null ||
                    string.IsNullOrWhiteSpace(selected.seal.contentId))
                {
                    error = "灵印奖励缺少可执行的稳定规格。";
                    return false;
                }
                if (GetPendingLevelUpSealCandidatesInternal(
                        selected.seal).Count == 0)
                {
                    error = "当前没有可钤入此灵印的卡牌。";
                    return false;
                }
                _pendingSealReward = selected;
                OnLog?.Invoke(
                    $"  选择灵印: {selected.name}，请指定宿主卡牌。");
                return true;
            }

            ApplyNonSealLevelUpReward(selected);
            CompleteLevelUpRewardSelection(selected);
            return true;
        }

        public int RewardRerollsRemaining =>
            Math.Max(0, _rewardRerollBank?.RewardRerollsRemaining ?? 0);

        public bool CanRerollPendingRewards =>
            State.AwaitingRewardSelection &&
            _pendingSealReward == null &&
            _pendingRewards != null &&
            _pendingRewards.Count > 0 &&
            RewardRerollsRemaining > 0;

        /// <summary>
        /// Replaces the current choice group and consumes exactly one Run
        /// charge only after a valid replacement has been generated.  Any
        /// handles to the previous group become invalid immediately.
        /// </summary>
        public bool TryRerollPendingRewards(out string error)
        {
            error = null;
            if (!State.AwaitingRewardSelection || _pendingRewards == null ||
                _pendingRewards.Count == 0)
            {
                error = "当前没有可重选的奖励候选。";
                return false;
            }
            if (_pendingSealReward != null)
            {
                error = "灵印宿主选择期间不能重选奖励。";
                return false;
            }
            if (_levelUp == null || _rewardRerollBank == null ||
                RewardRerollsRemaining <= 0)
            {
                error = "本次探索没有可用重选次数。";
                return false;
            }

            List<LevelUpSystem.Reward> replacement;
            try
            {
                replacement =
                    _levelUp.RerollRewards(_pendingRewards.Count);
            }
            catch (InvalidOperationException exception)
            {
                error = "无法重选奖励：" + exception.Message;
                return false;
            }
            if (replacement == null || replacement.Count == 0)
            {
                error = "奖励系统未能生成新的候选。";
                return false;
            }
            if (!_rewardRerollBank.TryConsumeRewardReroll())
            {
                error = "重选次数已被其他结算消耗。";
                return false;
            }

            SetPendingRewardOffer(replacement);
            _pendingRewardsAllowCashOut =
                _levelUp.CurrentOfferAllowsCashOut;
            OnLevelUpRewardsGenerated?.Invoke(_pendingRewards);
            OnStateChanged?.Invoke(
                CombatStateChangeReason.RewardOptionsChanged);
            return true;
        }

        /// <summary>
        /// Resolves only the exact candidate instance from the current offer.
        /// Stable content values alone are not sufficient because a reroll may
        /// legitimately produce an equivalent payload in a new offer.
        /// </summary>
        private LevelUpSystem.Reward FindPendingLevelUpReward(
            LevelUpSystem.Reward requested)
        {
            if (requested == null || _pendingRewards == null)
                return null;
            for (int index = 0; index < _pendingRewards.Count; index++)
            {
                LevelUpSystem.Reward candidate = _pendingRewards[index];
                if (ReferenceEquals(candidate, requested))
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns all unique runtime cards in the combat's zones that remain
        /// legal hosts for the selected level-up seal.  The caller receives a
        /// snapshot only; mutation stays in <see cref="TryAssignPendingLevelUpSeal"/>.
        /// </summary>
        public IReadOnlyList<CardConfig> GetPendingLevelUpSealCandidates()
        {
            if (_pendingSealReward?.seal == null)
                return Array.Empty<CardConfig>();
            return GetPendingLevelUpSealCandidatesInternal(
                _pendingSealReward.seal);
        }

        /// <summary>
        /// Commits a selected level-up seal to one exact runtime card.  The
        /// card can have moved among draw/hand/discard while the UI was open;
        /// it is therefore looked up and validated again at commit time.
        /// </summary>
        public bool TryAssignPendingLevelUpSeal(
            int cardRuntimeInstanceId,
            out string error)
        {
            error = null;
            if (_canReceiveSeals?.Invoke() != true)
            {
                error = "尚未取得玉印，无法钤印。";
                return false;
            }
            if (!State.AwaitingRewardSelection ||
                _pendingSealReward?.seal == null)
            {
                error = "当前没有待钤入的升级灵印。";
                return false;
            }
            if (cardRuntimeInstanceId <= 0)
            {
                error = "灵印宿主必须是有效的运行时卡牌实例。";
                return false;
            }

            CardConfig target = FindCombatCard(cardRuntimeInstanceId);
            CardSealSpec seal = _pendingSealReward.seal;
            if (target == null || target.IsEncounterTemporary || !CardSealRules.CanAssign(
                    target, seal, _rules, _isEvolutionResultEligible))
            {
                error = "目标卡牌不再是该灵印的合法宿主。";
                return false;
            }

            if (!_deckMgr.TryAssignSeal(cardRuntimeInstanceId, seal,
                    out CardConfig assigned, _rules, _isEvolutionResultEligible))
            {
                error = "目标卡牌不再是该灵印的合法宿主。";
                return false;
            }
            for (int i = 0; i < _freeRewardCardsThisTurn.Count; i++)
            {
                if (_freeRewardCardsThisTurn[i] == target)
                    _freeRewardCardsThisTurn[i] = assigned;
            }
            LevelUpSystem.Reward completed = _pendingSealReward;
            _pendingSealReward = null;
            _levelUp.SelectReward(completed);
            OnLog?.Invoke(
                $"  灵印已钤入: {completed.name} → {assigned.cardName}");
            CompleteLevelUpRewardSelection(completed);
            return true;
        }

        /// <summary>
        /// Cancels only the presentation-phase seal target choice.  No card,
        /// progression, or reward candidate has changed, so the caller may
        /// safely return to the original level-up candidates.
        /// </summary>
        public bool CancelPendingLevelUpSealSelection()
        {
            if (_pendingSealReward == null)
                return false;
            _pendingSealReward = null;
            return true;
        }

        private void ApplyNonSealLevelUpReward(LevelUpSystem.Reward reward)
        {
            _levelUp.SelectReward(reward);
            if (reward.type == LevelUpSystem.RewardType.NewCard &&
                reward.card != null)
            {
                // 升级新卡直接入手；展卷只控制回合抽牌数。
                CardGrantResult grant =
                    _deckMgr.GrantToHandOrDrawPile(reward.card);
                CardConfig granted = grant.Card;
                if (grant.AddedToHand)
                {
                    // 升级卡当回合免费：免灵力支付、保留连击值（三值费用模型）。
                    granted.isFreeToPlay = true;
                    _freeRewardCardsThisTurn.Add(granted);
                    Debug.Log(
                        $"[Diag][Reward] NewCard id={reward.card.contentId} " +
                        $"toHand=true free=true");
                }
            }
            else
            {
                Debug.Log($"[Diag][Reward] type={reward.type}");
            }
        }

        private void CompleteLevelUpRewardSelection(
            LevelUpSystem.Reward reward)
        {
            ClearPendingRewardOffer();
            _pendingSealReward = null;
            _pendingRewardsAllowCashOut = true;
            State.AwaitingRewardSelection = false;
            State.CurrentPhase = _enemyTurnSequence.IsActive ? BattlePhase.EnemyActing : BattlePhase.PlayerInput;
            OnLog?.Invoke($"  升级奖励: {reward.GetDisplay()}");
            OnStateChanged?.Invoke(CombatStateChangeReason.RewardSelected);

            bool wasDeferredCheckpoint = _awaitingDeferredExperienceReward;
            _awaitingDeferredExperienceReward = false;
            if (!wasDeferredCheckpoint)
                ProcessPendingLevelUps();
            StoreProgression();
            RestorePhaseAfterProgression();
        }

        private List<CardConfig> GetPendingLevelUpSealCandidatesInternal(
            CardSealSpec seal)
        {
            var result = new List<CardConfig>();
            if (_canReceiveSeals?.Invoke() != true) return result;
            var seen = new HashSet<int>();
            AddPendingSealCandidates(_deckMgr.Deck, seal, seen, result);
            AddPendingSealCandidates(_deckMgr.Hand, seal, seen, result);
            AddPendingSealCandidates(_deckMgr.Discard, seal, seen, result);
            return result;
        }

        private void AddPendingSealCandidates(
            IReadOnlyList<CardConfig> cards,
            CardSealSpec seal,
            ISet<int> seen,
            ICollection<CardConfig> result)
        {
            if (cards == null)
                return;
            for (int i = 0; i < cards.Count; i++)
            {
                CardConfig card = cards[i];
                if (card == null || card.IsEncounterTemporary || card.runtimeInstanceId <= 0 ||
                    !seen.Add(card.runtimeInstanceId) ||
                    !CardSealRules.CanAssign(card, seal, _rules,
                        _isEvolutionResultEligible))
                {
                    continue;
                }
                result.Add(card);
            }
        }

        private CardConfig FindCombatCard(int runtimeInstanceId)
        {
            CardConfig found = FindCombatCard(_deckMgr.Deck,
                runtimeInstanceId);
            return found ?? FindCombatCard(_deckMgr.Hand,
                runtimeInstanceId) ?? FindCombatCard(_deckMgr.Discard,
                runtimeInstanceId);
        }

        private static CardConfig FindCombatCard(
            IReadOnlyList<CardConfig> cards,
            int runtimeInstanceId)
        {
            if (cards == null)
                return null;
            for (int i = 0; i < cards.Count; i++)
            {
                CardConfig card = cards[i];
                if (card != null &&
                    card.runtimeInstanceId == runtimeInstanceId)
                {
                    return card;
                }
            }
            return null;
        }

        // Size review: actual damage, consumed charge and hit totals belong to
        // PlayerDamageReceiver. This coordinator only publishes typed callbacks.
        // A new independent lifecycle requires extraction before further expansion.
        private DamageResolution ApplyPlayerDamage(int amount) => _playerDamageReceiver.Apply(amount);
        private void OnPlayerRevived(PlayerRevivedEvent revived)
        {
            PublishEvent(CombatEventType.PlayerRevived, revived);
            OnStateChanged?.Invoke(CombatStateChangeReason.PlayerRevived);
            OnLog?.Invoke($"还魂：恢复 {revived.RestoredHealth} 点生命，剩余 {revived.RemainingPercent}% 复活进度。");
        }

        /// <summary>天劫释放</summary>
        private void OnTianjieReleased()
        {
            if (!IsBossTimerActive) return;
            int bossIndex = _enemies.FindIndex(enemy =>
                enemy != null && enemy.isBoss && enemy.IsDeployed &&
                !enemy.IsDead);
            if (bossIndex >= 0 && _enemies[bossIndex].isDisarmed)
            {
                _enemies[bossIndex].isDisarmed = false;
                OnLog?.Invoke($"⛓ {_enemies[bossIndex].name} 的天劫被缴械取消！");
                _bossTimer.Reset();
                return;
            }
            var result = ApplyPlayerDamage(_bossTimer.Damage);
            OnLog?.Invoke($"⚡ 天劫降临！玩家受到 {_bossTimer.Damage} 点伤害！" +
                          (result.BecameFatal ? " 玩家陨落！" : $" 剩余 HP: {result.RemainingHealth}"));
            _bossTimer.Reset();
            OnStateChanged?.Invoke(CombatStateChangeReason.PlayerDamaged);
        }

        /// <summary>
        /// Configures enemy experience settlement. This is a construction-time
        /// boundary: changing modes while credits or a deferred reward are
        /// outstanding would make their ownership ambiguous and is rejected.
        /// </summary>
        public void SetExperienceAwardCommitMode(
            ExperienceAwardCommitMode mode)
        {
            if (HasPendingExperienceAwards ||
                State.AwaitingRewardSelection ||
                _awaitingDeferredExperienceReward)
            {
                throw new InvalidOperationException(
                    "Cannot change experience commit mode while deferred progression is pending.");
            }

            _experienceAwardCommitMode = mode;
        }

        /// <summary>
        /// Returns the next enemy experience credit without committing it.
        /// Presentation uses this to validate stable ordering.
        /// </summary>
        public bool TryPeekPendingExperienceAward(
            out CombatExperienceAward award)
        {
            if (_pendingExperienceAwards.Count == 0)
            {
                award = default;
                return false;
            }

            award = _pendingExperienceAwards.Peek().Award;
            return true;
        }

        /// <summary>
        /// Commits the head experience credit up to (but never beyond) the
        /// next level boundary. If a reward is generated, no further segment
        /// can be committed until that ordinary reward has been selected or
        /// cashed out.
        /// </summary>
        public bool TryCommitNextExperienceSegment(
            long awardId,
            out CombatExperienceSegmentResult result)
        {
            result = default;
            if (_experienceAwardCommitMode !=
                    ExperienceAwardCommitMode.DeferredSegments ||
                State.AwaitingRewardSelection ||
                _pendingExperienceAwards.Count == 0)
            {
                return false;
            }

            PendingExperienceAward pending =
                _pendingExperienceAwards.Peek();
            if (pending.Award.AwardId != awardId)
                return false;

            int levelBefore = State.CurrentLevel;
            int experienceBefore = State.CurrentXP;
            int requiredBefore = _progression.GetRequiredExperience(
                levelBefore);
            int roomToBoundary = Math.Max(
                0,
                requiredBefore - experienceBefore);
            int applied = Math.Min(
                pending.RemainingExperience,
                roomToBoundary);

            if (applied > 0)
            {
                int nextExperience = checked(State.CurrentXP + applied);
                _statistics.RecordExperienceGained(applied);
                State.CurrentXP = nextExperience;
                pending.RemainingExperience -= applied;
                OnStateChanged?.Invoke(
                    CombatStateChangeReason.XPGained);
            }

            bool triggeredLevelUp =
                State.CurrentXP >= requiredBefore;
            if (triggeredLevelUp)
                ApplySingleLevelUp(deferredRewardCheckpoint: true);

            bool fullyCommitted = pending.RemainingExperience <= 0;
            if (fullyCommitted)
                _pendingExperienceAwards.Dequeue();

            StoreProgression();
            result = new CombatExperienceSegmentResult(
                pending.Award.AwardId,
                pending.Award.SourceEnemyRuntimeId,
                applied,
                Math.Max(0, pending.RemainingExperience),
                levelBefore,
                experienceBefore,
                requiredBefore,
                State.CurrentLevel,
                State.CurrentXP,
                _progression.GetRequiredExperience(State.CurrentLevel),
                triggeredLevelUp,
                State.AwaitingRewardSelection,
                fullyCommitted);

            if (!State.AwaitingRewardSelection)
                RestorePhaseAfterProgression();
            return true;
        }

        /// <summary>
        /// Direct/system experience is credited synchronously. Within a real card,
        /// reward checkpoints wait for its complete-play boundary. Enemy kills use
        /// AwardEnemyExperience so presentation can opt into deferred segments.
        /// </summary>
        public void GainXP(
            int amount,
            ExperienceSource source = ExperienceSource.DirectCard)
        {
            if (amount <= 0) return;
            int actualXP = _progression.SettleExperience(
                amount,
                source,
                CurrentGrowth,
                _player.zhaoSha);
            CommitImmediateExperience(actualXP);
        }

        private void AwardEnemyExperience(EnemyStats enemy)
        {
            if (enemy == null || enemy.experienceReward <= 0)
                return;

            int actualXP = _progression.SettleExperience(
                enemy.experienceReward,
                ExperienceSource.EnemyKill,
                CurrentGrowth,
                _player.zhaoSha);
            if (actualXP <= 0)
                return;

            _diagKillSequence++;
            Debug.Log(
                $"[Diag][Kill] seq={_diagKillSequence} " +
                $"idx={_enemies.IndexOf(enemy)} runtime={enemy.runtimeId} " +
                $"xpReward={enemy.experienceReward} actualXP={actualXP} " +
                $"mode={_experienceAwardCommitMode} xp={State.CurrentXP} " +
                $"lv={State.CurrentLevel}");

            var award = new CombatExperienceAward(
                ++_nextExperienceAwardId,
                enemy.runtimeId,
                enemy.experienceReward,
                actualXP);

            if (_experienceAwardCommitMode ==
                ExperienceAwardCommitMode.DeferredSegments)
            {
                _pendingExperienceAwards.Enqueue(
                    new PendingExperienceAward
                    {
                        Award = award,
                        RemainingExperience = actualXP
                    });
                OnExperienceAwarded?.Invoke(award);
                return;
            }

            OnExperienceAwarded?.Invoke(award);
            CommitImmediateExperience(actualXP);
        }

        private void CommitImmediateExperience(int actualXP)
        {
            if (actualXP <= 0)
                return;

            int nextExperience = checked(State.CurrentXP + actualXP);
            _statistics.RecordExperienceGained(actualXP);
            State.CurrentXP = nextExperience;
            OnStateChanged?.Invoke(CombatStateChangeReason.XPGained);

            if (State.CurrentPhase != BattlePhase.CardResolving)
                ProcessPendingLevelUps();
            StoreProgression();
        }

        /// <summary>处理待处理的升级（支持连升多级）</summary>
        private void ProcessPendingLevelUps()
        {
            if (State.AwaitingRewardSelection) return;

            int required = _progression.GetRequiredExperience(
                State.CurrentLevel);
            while (State.CurrentXP >= required && !State.AwaitingRewardSelection)
            {
                ApplySingleLevelUp(deferredRewardCheckpoint: false);

                required = _progression.GetRequiredExperience(
                    State.CurrentLevel);
            }
        }

        private void ApplySingleLevelUp(bool deferredRewardCheckpoint)
        {
            int required = _progression.GetRequiredExperience(
                State.CurrentLevel);
            State.CurrentXP = Math.Max(0, State.CurrentXP - required);
            State.CurrentLevel++;
            Debug.Log(
                $"[Diag][LevelUp] seq={_diagKillSequence} " +
                $"lv {State.CurrentLevel - 1}->{State.CurrentLevel} " +
                $"xp={State.CurrentXP}/" +
                $"{_progression.GetRequiredExperience(State.CurrentLevel)} " +
                $"deferred={deferredRewardCheckpoint}");
            OnLog?.Invoke(
                $"★ 升级！当前等级 {State.CurrentLevel} | " +
                $"XP: {State.CurrentXP}/" +
                $"{_progression.GetRequiredExperience(State.CurrentLevel)}");
            OnLevelUp?.Invoke(State.CurrentLevel);
            OnStateChanged?.Invoke(CombatStateChangeReason.LevelChanged);

            if (_levelUp == null)
                return;

            SetPendingRewardOffer(_levelUp.GenerateRewardsForLevel(
                State.CurrentLevel));
            _pendingSealReward = null;
            _pendingRewardsAllowCashOut =
                _levelUp.CurrentOfferAllowsCashOut;
            State.AwaitingRewardSelection = true;
            _awaitingDeferredExperienceReward =
                deferredRewardCheckpoint;
            State.CurrentPhase = BattlePhase.AwaitingReward;
            OnLevelUpRewardsGenerated?.Invoke(_pendingRewards);
        }

        private void SetPendingRewardOffer(
            List<LevelUpSystem.Reward> rewards)
        {
            if (_canReceiveSeals?.Invoke() != true && rewards != null &&
                rewards.Exists(reward => reward.type == LevelUpSystem.RewardType.Gem))
            {
                rewards = rewards.FindAll(reward => reward.type != LevelUpSystem.RewardType.Gem);
                // An external authored resolver may still supply a legacy
                // seal-only group. Use the ordinary stream without charging a reroll.
                if (rewards.Count == 0)
                    rewards = _levelUp.RerollRewards(0);
            }
            _pendingRewards = rewards;
            if (rewards == null || rewards.Count == 0)
            {
                _pendingRewardOfferRevision = 0L;
                return;
            }

            _pendingRewardOfferRevision = ++_nextRewardOfferRevision;
        }

        private void ClearPendingRewardOffer()
        {
            _pendingRewards = null;
            _pendingRewardOfferRevision = 0L;
        }

        private void RestorePhaseAfterProgression()
        {
            if (State.IsDefeat)
            {
                TryFinishCombat();
                return;
            }

            if (State.AwaitingRewardSelection)
            {
                State.CurrentPhase = BattlePhase.AwaitingReward;
                return;
            }

            if (State.HasOverkillTarget)
            {
                State.CurrentPhase = BattlePhase.Overkill;
                if (_attackPreviews.Prepare()) OnDamagePreviewsReady?.Invoke();
                return;
            }

            if (State.IsCombatOver())
            {
                // 最后一只怪已经死亡时仍需等经验按权威顺序落账，避免胜利
                // 转场抢在经验条或升级面板之前。这个终局等待不会影响仍在
                // 进行中的战斗。
                if (HasPendingExperienceAwards)
                {
                    State.CurrentPhase = BattlePhase.CardResolving;
                    return;
                }
                TryFinishCombat();
                return;
            }

            if (_enemyTurnSequence.IsActive)
            {
                _enemyTurnSequence.ResumeAfterProgression();
                return;
            }
            State.CurrentPhase = BattlePhase.PlayerInput;
            if (_attackPreviews.Prepare()) OnDamagePreviewsReady?.Invoke();
        }

        private void StoreProgression()
        {
            _progression.Store(State.CurrentLevel, State.CurrentXP);
        }

        public bool IsCombatOver() => State.IsCombatOver();

        public bool IsVictory => State.IsVictory;

        /// <summary>
        /// F7's explicit preview of an already granted automatic card as a fresh
        /// draw. False rejects before publishing any event; true means the shared
        /// draw pipeline was invoked, including its synchronous automatic play.
        /// Does not grant another instance, set free flags or use player play.
        /// Ordinary cards and unavailable card/turn checkpoints are rejected.
        /// </summary>
        public bool ResolveAutomaticDrawForTestHarness(CardConfig card)
        {
            if (card == null || !card.autoPlayOnDraw || !_deckMgr.HasInHand(card) ||
                !State.CanDrawCards || _enemyTurnSequence.IsActive) return false;
            PublishCardsDrawn(new[] { card });
            return true;
        }

        /// <summary>
        /// 友元测试驱动入口：跳过法力/手牌/阶段合同，但仍复用
        /// 权威 AttackResolver 与死亡回执消费器。不得作为正式游戏入口。
        /// </summary>
        internal void ResolveCardForTestHarness(
            CardConfig card,
            int targetIndex = 0,
            CombatHarnessDefeatMode defeatMode =
                CombatHarnessDefeatMode.ResetForReplay)
        {
            using IDisposable combatFactScope = _runState.CardUnlockFacts.SuppressCombatFactsForHarness();
            bool resetDefeatedEnemies = defeatMode ==
                CombatHarnessDefeatMode.ResetForReplay;
            // External 测试路径不检查 phase（用于 VFX 测试场景）
            var resolvedCost = card.ResolveCost();
            bool comboed = _combo.TryPlayCard(resolvedCost);
            bool chainWasBroken = _combo.WasChainBroken;
            bool wuxingTriggered = TryResolveWuxingPair(card, out var wuxingFromColor, out _);
            _lastPlayedCardColor = card.color;
            _hasLastPlayedCardColor = true;
            int combo = _combo.Combo;
            ColorTriggerSystem.TriggerBoundary crawlerTriggerBoundary =
                _colorTrigger.CaptureTriggerBoundary();
            _statistics.RecordCardStarted(
                card,
                combo,
                resolvedCost.IsFreeToPlay);

            OnCardPlayedVFX?.Invoke(card, targetIndex);

            int firstTarget = targetIndex;
            int totalDamage = 0;
            bool anyCrit = false;
            bool anyKill = false;
            EnemyBackfillCheckpoint backfillCheckpoint =
                _enemyBackfill.BeginResolution();
            if (AttackPlanBuilder.TryBuildFromCard(
                    card,
                    _rules,
                    new CombatStatSnapshot(_player, combo,
                        CardSealRules.GetHeldAttackMultiplier(_deckMgr.Hand, card),
                    (long)_mana.CurrentMana + _mana.TemporaryMana,
                        _runState.RunLuckBonus),
                    out AttackPlan plan))
            {
                OnAttackPlanResolved?.Invoke(plan);
                AttackSubHit firstSubHit = plan.SubHits.Count > 0
                    ? plan.SubHits[0]
                    : null;
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[DamageTrace][Plan:External] " +
                        $"cardId={card.contentId} " +
                        $"sourceCard={plan.SourceCardInstanceId} " +
                        $"might={_player.fengRui} " +
                        $"amount={_player.amount} " +
                        $"combo={combo} " +
                        $"subHits={plan.SubHits.Count} " +
                        $"displayedCardDamage={plan.DisplayedCardDamage} " +
                        $"maxDirectEvents={plan.MaximumDirectEventCount} " +
                        $"maxBounceEvents={plan.MaximumBounceEventCount} " +
                        $"firstHitDamage={firstSubHit?.NormalHitDamage ?? 0} " +
                        $"firstDirectTargets={firstSubHit?.DirectTargetCount ?? 0} " +
                        $"firstBounceHits={firstSubHit?.Bounce?.hitsPerDirectHit ?? 0} " +
                        $"firstBounceMode={firstSubHit?.Bounce?.sequenceMode.ToString() ?? "None"}");
                AttackResolutionResult resolved = AttackResolver.Execute(
                    plan,
                    _enemies,
                    ResolutionMode.Actual,
                    State.Random ?? DefaultRandomSource.Instance,
                    State.EnemyAi?.GenerateIntents(),
                    _bossOverkill,
                    activeTerrainEffects: State.TerrainEffects?.Active);
                var preparedDefeats = PrepareResolvedDefeats(resolved.Defeats);
                if (resolved.ProjectileTraces.Count > 0)
                    OnProjectileTracesResolved?.Invoke(new ProjectileTraceBatch(
                        plan.SourceCardInstanceId, resolved.ProjectileTraces));
                // The harness resolves the first valid Damage plan directly,
                // so it must register that same source at the authority boundary
                // even when the plan contains only empty column projectiles.
                if (card.HasSpecEffects)
                    foreach (CardEffectSpec effect in card.effects)
                        if (AttackPlanBuilder.TryFromDamageEffect(card, effect, _rules, out _))
                        {
                            State.ColumnReplays.RegisterResolvedAttack(card, effect, resolved);
                            break;
                        }
                State.TerrainEffects?.Schedule(
                    resolved.TerrainEffectPlacements);
                PublishResolvedControlEffects(resolved.ControlEffects);
                totalDamage = resolved.TotalAppliedDamage;
                firstTarget = resolved.Events.Count > 0
                    ? resolved.Events[0].TargetIndex
                    : targetIndex;
                for (int i = 0; i < resolved.Events.Count; i++)
                {
                    DamageEvent damageEvent = resolved.Events[i];
                    anyCrit |= damageEvent.IsCritical;
                    TraceDamageEvent(damageEvent, "External");
                    OnDamageEvent?.Invoke(damageEvent);
                    OnEnemyDamaged?.Invoke(
                        damageEvent.TargetIndex,
                        damageEvent.AppliedDamage,
                        damageEvent.IsCritical,
                        damageEvent.IsFatal);
                }
                PublishResolvedDamageDefeats(
                    preparedDefeats,
                    ref anyKill,
                    resetDefeatedEnemies
                        ? DamageDefeatPublicationPolicy
                            .HarnessResetForReplay
                        : DamageDefeatPublicationPolicy
                            .HarnessKeepDefeated);
                if (resetDefeatedEnemies)
                    _bossOverkill.End();
                if (!resetDefeatedEnemies)
                {
                    PublishFormationAdvances(
                        _enemyBackfill.AdvanceAfterResolution(
                            backfillCheckpoint));
                }
            }

            int armorGained = 0;
            int healAmount = 0;
            int manaGained = 0;
            int maxHealthGained = 0;
            int jadeGained = 0;
            int durationReserveGained = 0;
            var resolvedCrawlerDurationChanges = new List<CrawlerDurationChange>();
            // Per-call facts keep nested automatic draws isolated from their parent play.
            bool randomizedCrawlerTriggerColors = false;
            var resolvedCrawlerTriggerColors = new List<CrawlerTriggerColorChange>();
            var resolvedHandCostChanges = new List<HandCardCostChange>();
            var createdCards = new List<CombatCreatedCardReceipt>();
            var deferredStats = new List<KeyValuePair<StatType, int>>();
            int multiplier = 1 + combo;
            if (card.HasSpecEffects)
            {
                for (int i = 0; i < card.effects.Count; i++)
                {
                    CardEffectSpec spec = card.effects[i];
                    if (spec == null ||
                        spec.effectType == EffectType.Damage)
                        continue;

                    int scaledAmount = spec.comboScalable
                        ? spec.amount * multiplier
                        : spec.amount;
                    int scaledCount = spec.comboScalable
                        ? spec.count * multiplier
                        : spec.count;
                    switch (spec.effectType)
                    {
                        case EffectType.GainArmor:
                            _player.armor += scaledAmount;
                            armorGained += scaledAmount;
                            break;
                        case EffectType.Heal:
                            int healthBefore = _player.currentHealth;
                            _player.currentHealth = Mathf.Min(
                                _player.maxHealth,
                                _player.currentHealth +
                                ApplyHealingRules(scaledAmount));
                            healAmount +=
                                _player.currentHealth - healthBefore;
                            break;
                        case EffectType.GainMana:
                            _mana.AddTemporaryMana(scaledAmount);
                            manaGained += scaledAmount;
                            break;
                        case EffectType.DrawCards:
                            DrawCardsInternal(scaledCount);
                            break;
                        case EffectType.ModifyStat:
                            if (spec.statType == StatType.NingShen)
                                durationReserveGained += ApplyCrawlerDurationGain(
                                    scaledAmount, resolvedCrawlerDurationChanges);
                            else
                                deferredStats.Add(
                                    new KeyValuePair<StatType, int>(
                                        spec.statType,
                                        scaledAmount));
                            break;
                        case EffectType.GainJade:
                            jadeGained += _economy.Grant(
                                scaledAmount,
                                CurrentGreed,
                                JadeSource.Card,
                                true).FinalJade;
                            break;
                        case EffectType.IncreaseMaxHealth:
                            maxHealthGained +=
                                _player.IncreaseMaxHealth(scaledAmount);
                            break;
                        case EffectType.GainExperience:
                            GainXP(
                                scaledAmount,
                                ExperienceSource.Character);
                            break;
                        case EffectType.SummonCrawler:
                            {
                                CrawlerConfig crawler =
                                    _crawlerResolver?.Invoke(
                                        spec.crawlerContentId);
                                if (crawler != null)
                                    SummonCrawler(crawler);
                                else
                                    OnLog?.Invoke(
                                        $"  ⚠ 未找到英灵配置: " +
                                        spec.crawlerContentId);
                            }
                            break;
                        case EffectType.IncreaseCrawlerDuration:
                            ApplyCrawlerDurationIncrease(
                                spec.crawlerContentId,
                                scaledAmount, resolvedCrawlerDurationChanges);
                            break;
                        case EffectType.CreateCard:
                            ResolveCreatedCards(card, spec, createdCards);
                            break;
                        case EffectType.ExtendCrawlerDuration:
                            durationReserveGained += ApplyCrawlerDurationGain(
                                scaledAmount, resolvedCrawlerDurationChanges);
                            break;
                        case EffectType.RandomizeCrawlerTriggerColors:
                            resolvedCrawlerTriggerColors.AddRange(
                                _colorTrigger.RandomizeActiveTriggerColors(State.Random ?? DefaultRandomSource.Instance));
                            randomizedCrawlerTriggerColors = true;
                            break;
                        case EffectType.RandomizeHandCosts:
                            {
                                IReadOnlyList<HandCardCostChange> handCostChanges;
                                if (spec.randomCostRelative)
                                    _deckMgr.RandomizeNumericHandCostOffsets(
                                        spec.randomCostMin, spec.randomCostMax, out handCostChanges);
                                else
                                    _deckMgr.RandomizeNumericHandCosts(
                                        Math.Max(0, spec.randomCostMin),
                                        Math.Max(Math.Max(0, spec.randomCostMin), spec.randomCostMax),
                                        out handCostChanges);
                                resolvedHandCostChanges.AddRange(handCostChanges);
                            }
                            break;
                    }
                }
            }
            else
            {
                if (card.armor > 0)
                {
                    armorGained = card.armor * multiplier;
                    _player.armor += armorGained;
                }
                if (card.heal > 0)
                {
                    int healthBefore = _player.currentHealth;
                    _player.currentHealth = Mathf.Min(
                        _player.maxHealth,
                        _player.currentHealth + ApplyHealingRules(
                            card.heal * multiplier));
                    healAmount = _player.currentHealth - healthBefore;
                }
                if (card.manaGain > 0)
                {
                    manaGained = card.manaGain * multiplier;
                    _mana.AddTemporaryMana(manaGained);
                }
            }
            List<CombatStatChange> resolvedStatChanges =
                ApplyDeferredStatChanges(deferredStats);
            // This external resolution path has no payment transaction.
            // Its default receipt must not manufacture refunds or rebates.
            var sealGains = ApplyPostPlayCardSeals(card, resolvedStatChanges,
                sealPlan => ResolveAttackPlan(sealPlan,
                    ref totalDamage, ref anyCrit, ref anyKill,
                    ref firstTarget, ref targetIndex));
            armorGained += sealGains.ArmorGained;
            manaGained = (int)Math.Min(int.MaxValue, (long)manaGained + sealGains.ManaGained);

            // Match the formal complete-host boundary: post-play seals and
            // wuxing settle before the frozen residents consume their charges.
            if (resolvedCost.IsMild || resolvedCost.IsComboBreaker)
                _combo.ClearCombo();
            durationReserveGained += ApplyWuxingIfTriggered(wuxingTriggered, wuxingFromColor,
                resolvedCrawlerDurationChanges);

            _statistics.RecordCardHealing(healAmount);
            if (_rules.experimentalMedicineEnabled && healAmount > 0)
                OnRuleTriggered?.Invoke(new CombatRuleTriggeredEvent(
                    CombatRuleTriggerKind.ExperimentalMedicine, healAmount));
            OnCardResolved?.Invoke(
                card,
                firstTarget,
                totalDamage,
                comboed,
                anyCrit);
            OnCardPlayResolved?.Invoke(new CombatCardResolvedEvent
            {
                Card = card,
                TargetIndex = firstTarget,
                ManaCost = resolvedCost.PaymentCost,
                DamageDealt = totalDamage,
                ArmorGained = armorGained,
                HealAmount = healAmount,
                ManaGained = manaGained,
                MaxHealthGained = maxHealthGained,
                JadeGained = jadeGained,
                StatChanges = resolvedStatChanges,
                CrawlerDurationChanges = resolvedCrawlerDurationChanges.AsReadOnly(),
                RandomizedCrawlerTriggerColors = randomizedCrawlerTriggerColors,
                CrawlerTriggerColorChanges = resolvedCrawlerTriggerColors.AsReadOnly(),
                HandCostChanges = resolvedHandCostChanges.AsReadOnly(),
                CreatedCards = createdCards.AsReadOnly(),
                DurationReserveGained = durationReserveGained,
                ResultingDurationReserve = _colorTrigger.PendingDurationBonus,
                Comboed = comboed,
                ChainWasBroken = chainWasBroken,
                ComboCount = combo,
                IsCritical = anyCrit,
                EnemyKilled = anyKill,
                DestroyedOnUse = card.MustDestroyOnUse
            });

            // Test harness resolution consumes the same frozen resident
            // boundary after the same host/seal/wuxing sequence as real play.
            ResolveCrawlerTriggers(
                card,
                multiplier,
                crawlerTriggerBoundary);

            // 外部结算路径遵守与正式出牌相同的完整卡牌边界。
            if (!resetDefeatedEnemies)
                TryPublishLevelBossArrival();

            // 外部测试路径结束后恢复到 PlayerInput（不影响正常流程）
            if (State.CurrentPhase == BattlePhase.CardResolving)
                State.CurrentPhase = BattlePhase.PlayerInput;
        }

        private void ResolveCrawlerTriggers(
            CardConfig card,
            int comboMultiplier,
            ColorTriggerSystem.TriggerBoundary triggerBoundary)
        {
            List<ColorTriggerSystem.TriggerResult> triggerResults =
                _colorTrigger.OnCardPlayed(
                    card,
                    comboMultiplier,
                    triggerBoundary);
            if (triggerResults.Count == 0)
                return;

            for (int i = 0; i < triggerResults.Count; i++)
            {
                ColorTriggerSystem.TriggerResult trigger = triggerResults[i];
                List<CombatStatChange> committedChanges =
                    ApplyDeferredStatChanges(trigger.StatDeltas);
                for (int d = 0; d < committedChanges.Count; d++)
                {
                    CombatStatChange change = committedChanges[d];
                    OnLog?.Invoke(
                        $"  ↑ {change.StatType} +{change.Amount}（英灵触发）");
                }

                // Consume the typed result through the existing deck boundary;
                // color qualification/scaling and the draw bonus stay with their owners.
                for (int d = 0; d < trigger.DrawRequests.Count; d++)
                    DrawCardsInternal(trigger.DrawRequests[d].BaseCount);

                CrawlerConfig crawler = trigger.Crawler?.config;
                OnCrawlerTriggered?.Invoke(
                    new CombatCrawlerTriggeredEvent(
                        trigger.Crawler?.RuntimeId ?? 0,
                        crawler?.contentId,
                        crawler?.crawlerName,
                        committedChanges,
                        trigger.CrawlerDurationChanges, trigger.DurationReserveGained,
                        trigger.ResultingDurationReserve));
            }

            // 即使某英灵没有属性增量，其凝神也已被消耗。
            // 统一发布一次状态变化，供驻场数字和离场生命周期刷新。
            OnStateChanged?.Invoke(CombatStateChangeReason.CrawlerChanged);
        }

        // DeckManager remains the sole owner of instance identity and placement.
        // This collects creation facts before the card-resolved state refresh.
        private void ResolveCreatedCards(CardConfig source, CardEffectSpec effect,
            List<CombatCreatedCardReceipt> receipts)
        {
            CardConfig definition = _cardResolver?.Invoke(effect.createCardDefinitionId);
            if (definition == null) return;
            for (int i = 0; i < Math.Max(0, effect.count); i++)
            {
                CardConfig created = _deckMgr.GrantEncounterTemporaryToDrawPile(
                    definition, EncounterTemporaryCardPlacement.Hand);
                if (created != null)
                    receipts.Add(new CombatCreatedCardReceipt(source, created,
                        EncounterTemporaryCardPlacement.Hand));
            }
        }

        private int ApplyCrawlerDurationGain(
            int amount, List<CrawlerDurationChange> resolvedChanges)
            => CollectCrawlerDurationGain(_colorTrigger.ResolveDurationGain(amount), resolvedChanges);

        private static int CollectCrawlerDurationGain(
            CrawlerDurationGainResult gain, List<CrawlerDurationChange> resolvedChanges)
        {
            for (int i = 0; i < gain.CrawlerChanges.Count; i++)
            {
                CrawlerDurationChange change = gain.CrawlerChanges[i];
                int existing = resolvedChanges.FindIndex(
                    item => item.CrawlerRuntimeId == change.CrawlerRuntimeId);
                if (existing < 0)
                    resolvedChanges.Add(change);
                else
                    resolvedChanges[existing] = new CrawlerDurationChange(
                        change.CrawlerRuntimeId, change.CrawlerContentId,
                        resolvedChanges[existing].Amount + change.Amount, change.RemainingTriggers);
            }
            // This receipt is published by the enclosing complete-card event
            // before its state refresh; no partial gain refresh is emitted here.
            return gain.ReserveGained;
        }

        private void ApplyCrawlerDurationIncrease(
            string crawlerContentId,
            int requestedAmount, List<CrawlerDurationChange> resolvedChanges)
        {
            CrawlerDurationGainResult gain = _colorTrigger.ResolveDurationIncrease(
                crawlerContentId,
                requestedAmount);
            CollectCrawlerDurationGain(gain, resolvedChanges);
            int applied = gain.TotalApplied;
            if (applied <= 0)
            {
                OnLog?.Invoke(
                    $"  ◇ 凝神未增加（英灵未在场、已离场或已达 " +
                    $"{ColorTriggerSystem.MaxRemainingTriggers}）: " +
                    $"{crawlerContentId}");
                return;
            }

            OnLog?.Invoke(
                $"  ◆ 英灵凝神 +{applied}：{crawlerContentId}");
        }

    }
}


