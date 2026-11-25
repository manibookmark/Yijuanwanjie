using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Audio;
using WanjieScroll.Effects;
using WanjieScroll.Bootstrap;
using WanjieScroll.Meta;
using WanjieScroll.UI;
using WanjieScroll.UI.Cards;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 战斗场景控制器：统一初始化所有 ViewModel 和 View，绑定战斗事件。
    /// 自动全出的选择与有限预算归Core，表现等待与请求取消归CombatAutoPlaySequence。
    /// 本类只接线按钮、手牌就绪查询和与手动共用的出牌入口，不持有第二份全出状态。
    /// 出牌原点只在既有 TryPlay 边界发布屏幕位置事实；不持有飞行或特效状态。
    /// 自动使用接线只消费Core的类型化来源，跳过不属于玩家出牌的连击反馈；
    /// 连击数归ComboSystem、动画归既有连击View，不新增状态或表现owner。
    /// 2026-10-11限时例外：只修已有自动结束意图的屏障、真实回执与取消边界；
    /// 意图和协程仍由此适配器唯一拥有，Core保留支付/回合规则，协调器保留命中屏障。
    /// 回合结束仍沿用既有输入与命中屏障，不由全出模块另建一套结算。
    /// 2026-10-22规模复核：本批只补既有全出按钮在Core最终就绪与重新启用时的
    /// 状态投影及订阅收尾，不新增策略、业务状态或独立生命周期。
    /// </summary>
    public class CombatSceneController : MonoBehaviour
    {
        [SerializeField] private float autoEndTurnCooldownSeconds = 0.25f;

        [Header("核心系统")]
        public CombatController combat;
        private CombatRuleSet _rewardTooltipRules;

        public void ConfigureRewardTooltipRules(CombatRuleSet rules)
        {
            _rewardTooltipRules = rules?.Clone();
        }
        public ArtConfig artConfig;
        public AudioConfig audioConfig;

        [Header("Views")]
        public HandView handView;
        public ComboChainView comboChainView;
        public HUDView hudView;
        public LevelUpPanel levelUpPanel;
        public DeckPileView deckPileView;
        public BattleHudView battleHudView;
        public BattleBoardHudLayout boardHudLayout;
        public EnemyAreaView enemyAreaView;
        public CrawlerResidentFeedbackCoordinator crawlerResidentFeedback;

        private List<LevelUpSystem.Reward> _pendingRewards;
        private List<LevelUpSystem.Reward> _queuedRewards;
        private RewardChoiceOverlay _rewardChoiceOverlay;
        private RewardFeedbackPresenter _rewardRevealOverlay;
        private SealAssignmentCoordinator _levelUpSealAssignment;
        private RewardUtilityAvailability _rewardUtilityAvailability;
        private bool _processingRewardSelection;
        private bool _combatPresentationActive;
        private bool _combatPresentationLocksInput;
        private int _immediateExperiencePresentations;
        private bool _combatInputRequested;
        private readonly HashSet<int> _combatInputLocks =
            new HashSet<int>();
        private int _nextCombatInputLockId;
        private System.IDisposable _combatPresentationInputLease;
        private bool _pendingProgressRefresh;
        private List<LevelUpSystem.Reward> _deferredLevelUpRewards;
        private System.IDisposable _levelUpFlowLease;
        private System.Action _levelUpCheckpointCompletion;
        private bool _autoEndTurnPending;
        private Coroutine _autoEndTurnRoutine;
        private CombatAutoPlaySequence _playAllSequence;

        [Header("效果")]
        public Transform effectLayer;
        public Camera mainCamera;
        public InkSpreadEffect inkSpreadEffect;

        [Header("连击数显示")]
        [Tooltip("连击数字在1920×1080棋盘参考舞台的位置，不随屏幕宽度漂移")]
        [SerializeField] private Vector2 comboAnchor = new Vector2(0.9f, 0.81f);
        [Tooltip("相对 Anchor 的像素偏移，Play 模式可直接调整")]
        [SerializeField] private Vector2 comboOffset = Vector2.zero;
        [Tooltip("连击粒子数字的视口尺寸；墨点按高度自适应")]
        [SerializeField] private Vector2 comboSize = new Vector2(400f, 240f);

        private HandViewModel _handVM;
        private ComboViewModel _comboVM;
        private HUDViewModel _hudVM;
        private LevelUpViewModel _levelUpVM;
        private CardTooltipView _cardTooltip;

        public bool IsInitialized { get; private set; }
        public bool IsBound { get; private set; }
        public bool IsCombatInputEnabled { get; private set; }
        public bool IsCardPresentationActive =>
            handView != null && handView.IsCardPresentationActive;
        public bool IsAwaitingRewardSelection =>
            combat != null && combat.State.AwaitingRewardSelection;
        public event System.Func<CommandResult> EndTurnRequested;
        // The bound enemy-turn coordinator supplies the same impact barrier
        // used for final admission. No presentation state is copied here.
        internal System.Func<bool> PendingTurnImpactProbe { get; set; }
        public event System.Action<int, Vector2> CardPlayScreenOriginPrepared;
        public event System.Action<int> CardPlayOriginRejected;
        private bool _firstTurnStarted;
        private bool _shutdown;
        private bool _ownsBrushComboView;
        // Per-encounter overlays are destroyed together, while shared HUD stays.
        public RectTransform encounterCanvasRoot;
        private System.Action _initialDrawCallback;

        private void Start()
        {
            BindNow();
        }

        /// <summary>绑定所有 ViewModel 和 View，不调用 StartTurn。</summary>
        public void BindNow()
        {
            if (IsBound || _shutdown) return;
            Initialize();
            IsBound = true;
            RefreshAutoPlayButton();
        }

        /// <summary>设置战斗输入是否可用。</summary>
        public void SetCombatInputEnabled(bool enabled)
        {
            _combatInputRequested = enabled;
            ApplyCombatInputState();
        }

        /// <summary>
        /// 取得可嵌套的战斗输入锁。普通流程仍可更新“希望启用”的状态；只有
        /// 最后一份租约释放后，实际输入才按该状态恢复。
        /// </summary>
        public System.IDisposable AcquireCombatInputLock()
        {
            int lockId = ++_nextCombatInputLockId;
            _combatInputLocks.Add(lockId);
            ApplyCombatInputState();
            return new CombatInputLockLease(this, lockId);
        }

        private void ReleaseCombatInputLock(int lockId)
        {
            if (!_combatInputLocks.Remove(lockId)) return;
            ApplyCombatInputState();
        }

        private void ApplyCombatInputState()
        {
            bool enabled = _combatInputRequested &&
                _combatInputLocks.Count == 0;
            bool previous = IsCombatInputEnabled;
            IsCombatInputEnabled = enabled;
            handView?.SetInteractionEnabled(enabled);
            if (battleHudView != null)
            {
                if (battleHudView.drawButton != null)
                    battleHudView.drawButton.interactable = enabled;
                RefreshAutoPlayButton();
                if (battleHudView.endTurnButton != null)
                    battleHudView.endTurnButton.interactable = enabled;
                if (battleHudView.discardButton != null)
                    battleHudView.discardButton.interactable = enabled;
            }

            if (enabled)
                UpdateCardPlayability();

            if (previous != enabled)
            {
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[CombatHoverDiag] Combat input {previous}->{enabled}; " +
                        $"turn={combat?.State?.CurrentTurn ?? 0}, " +
                        $"handInteraction={handView?.IsInteractionEnabled}, " +
                        $"awaitingReward={IsAwaitingRewardSelection}, " +
                        $"presentationActive={_combatPresentationActive}, " +
                        $"presentationLocksInput={_combatPresentationLocksInput}");
            }
        }

        private sealed class CombatInputLockLease : System.IDisposable
        {
            private CombatSceneController _owner;
            private readonly int _lockId;

            public CombatInputLockLease(
                CombatSceneController owner,
                int lockId)
            {
                _owner = owner;
                _lockId = lockId;
            }

            public void Dispose()
            {
                CombatSceneController owner = _owner;
                _owner = null;
                owner?.ReleaseCombatInputLock(_lockId);
            }
        }

        /// <summary>
        /// 注入本场战斗允许显示的奖励操作。默认值全部锁定。
        /// </summary>
        public void ConfigureRewardUtilities(
            RewardUtilityAvailability availability)
        {
            _rewardUtilityAvailability = availability;
        }

        /// <summary>
        /// 在提交一段经验前暂存同步进度事件，避免经验条抢先跳值。
        /// 普通经验段不锁输入；只有 Core 确认跨级后才升级为全局战斗锁。
        /// </summary>
        public void BeginCombatPresentation(bool lockCombatInput = true)
        {
            _combatPresentationActive = true;
            if (!lockCombatInput) return;
            _combatPresentationLocksInput = true;
            _combatPresentationInputLease ??= AcquireCombatInputLock();
        }

        /// <summary>
        /// 结束当前经验提交边界。经验条自行播放权威段动画时，可跳过暂存进度刷新。
        /// </summary>
        public void EndCombatPresentation(
            bool refreshDeferredProgress = true)
        {
            bool restoreInput = _combatPresentationLocksInput;
            System.IDisposable inputLease =
                _combatPresentationInputLease;
            _combatPresentationInputLease = null;
            _combatPresentationActive = false;
            _combatPresentationLocksInput = false;
            if (_pendingProgressRefresh)
            {
                _pendingProgressRefresh = false;
                if (refreshDeferredProgress)
                    RefreshBattleHud();
            }

            if (_deferredLevelUpRewards == null ||
                _deferredLevelUpRewards.Count == 0)
            {
                if (restoreInput &&
                    !IsAwaitingRewardSelection && combat != null &&
                    !combat.IsCombatOver())
                    SetCombatInputEnabled(true);
                else if (restoreInput)
                    SetCombatInputEnabled(false);
                inputLease?.Dispose();
                return;
            }

            List<LevelUpSystem.Reward> rewards = _deferredLevelUpRewards;
            _deferredLevelUpRewards = null;
            if (restoreInput)
                SetCombatInputEnabled(false);
            OnLevelUpRewards(rewards);
            inputLease?.Dispose();
        }

        /// <summary>
        /// Opens the same ordinary level-up panel used everywhere else, but
        /// exposes its fully-closed boundary to the battle presentation
        /// coordinator. No later death or experience action may start until
        /// the supplied callback runs.
        /// </summary>
        public void PresentDeferredLevelUpCheckpoint(
            System.Action onPanelClosed)
        {
            if (_levelUpCheckpointCompletion != null)
            {
                Debug.LogError(
                    "[CombatPresentation] A level-up checkpoint is already active.");
                onPanelClosed?.Invoke();
                return;
            }

            if (_deferredLevelUpRewards == null ||
                _deferredLevelUpRewards.Count == 0)
            {
                onPanelClosed?.Invoke();
                return;
            }

            _levelUpCheckpointCompletion = onPanelClosed;
            List<LevelUpSystem.Reward> rewards =
                _deferredLevelUpRewards;
            _deferredLevelUpRewards = null;
            _pendingRewards = rewards;
            ShowLevelUpOverlay(rewards);
        }

        /// <summary>开始第一回合。只允许成功一次。onInitialDrawPresented 在初始抽牌完成后回调。</summary>
        public void BeginFirstTurn(System.Action onInitialDrawPresented)
        {
            if (_firstTurnStarted) return;
            _firstTurnStarted = true;
            _initialDrawCallback = onInitialDrawPresented;
            combat?.StartTurn();
        }

        /// <summary>初始抽牌完成后应用场景配置的一次性入场经验。</summary>
        public void GrantEntryExperience(int amount)
        {
            if (amount > 0)
                combat?.GainXP(amount);
        }

        private void Initialize()
        {
            if (IsInitialized || combat == null) return;

            if (artConfig == null)
                artConfig = ScriptableObject.CreateInstance<ArtConfig>();
            UIController.ArtConfig = artConfig;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.config = audioConfig;
                AudioManager.Instance.SetCombatController(combat);
                AudioManager.Instance.BindCombatEvents(combat);
            }

            _handVM = new HandViewModel();
            _handVM.Bind(combat);

            _comboVM = new ComboViewModel();
            _comboVM.Bind(combat.Combo);

            _hudVM = new HUDViewModel();
            _hudVM.Bind(combat);

            _levelUpVM = new LevelUpViewModel();

            if (handView != null)
            {
                handView.Bind(_handVM);
                handView.SetCardClickCallback(OnCardClicked);
                handView.SetHoverCallback(OnCardHoverEnter, OnCardHoverExit);
            }
            if (comboChainView != null)
            {
                comboChainView.Bind(_comboVM);
                comboChainView.InkSpreadRequested += OnInkSpreadRequested;
            }
            if (hudView != null) hudView.Bind(_hudVM);
            if (deckPileView != null)
            {
                deckPileView.Bind(_handVM);
            }
            BindBattleHud();

            _handVM.RefreshAll();
            _comboVM.RefreshAll();
            _hudVM.RefreshAll();

            if (mainCamera != null && ScreenShake.Instance == null)
                mainCamera.gameObject.AddComponent<ScreenShake>();

            // StartTurn is now called by BeginFirstTurn via CombatEntryCoordinator
            IsInitialized = true;
            RefreshBattleHud();
            // 连击视图需要在入场时就创建；0 连击由纯粒子视图自行保持隐藏，
            // 首次出牌后只更新数字，避免动态视图直到状态变化才出现。
            UpdateComboDisplay();
            UpdateCardPlayability();

            combat.OnLevelUpRewardsGenerated += OnLevelUpRewards;
        }

        // ── Hover 费用预览 ───────────────────────────────────

        private void OnCardHoverEnter(int sortedIndex)
        {
            if (combat == null || battleHudView == null) return;
            if (!IsCombatInputEnabled || (handView != null &&
                !handView.IsCardReadyForInteraction(sortedIndex))) return;

            // PointerEnter is not guaranteed to be paired with the previous
            // card's pointer-exit when the hover layer reparents pooled cards.
            // Every hover surface is a projection of the same combat snapshot,
            // so clear the whole snapshot before calculating the next card.
            ClearTransientCardHoverPresentation();

            var card = _handVM?.GetCardAt(sortedIndex);
            CardImmediateDamageDisplaySummary immediateDamage = null;
            if (card != null)
            {
                int paymentCost = card.ResolveCost().PaymentCost;
                combat.Mana.PreviewPayment(paymentCost, out ManaPaymentReceipt payment);
                battleHudView.ShowCostPreview(paymentCost, payment.CreditedAmount);
            }

            // AudioManager 统一裁决 Hover 与出牌、命中、升级音效的抢占关系。
            AudioManager.PlaySfxSafe(SfxType.CardHover);

            // 怪物身上伤害预览只消费 Core 聚合结果，UI 不重复计算规则。
            if (card != null)
            {
                immediateDamage = combat.GetCardImmediateDamageDisplay(card);
                TraceCardHoverDamage(card, immediateDamage);
                enemyAreaView?.ShowDamagePreview(immediateDamage.DamagePreviews);
            }

            // 显示卡牌提示面板
            EnsureTooltip();
            if (_cardTooltip != null && card != null)
            {
                _cardTooltip.ShowText(
                    CardTextRenderer.GetTooltipText(card, combat, immediateDamage));
                _cardTooltip.transform.SetAsLastSibling();
            }

            // HandViewModel 已投影此刻出牌数值，悬停不再次应用叠势倍率。

            // hover 时预览增益图标
            if (card != null)
            {
                var buffPreviews = combat.GetBuffPreview(card);
                if (buffPreviews != null && buffPreviews.Count > 0)
                {
                    battleHudView.PreviewBuffs(buffPreviews);
                }
                crawlerResidentFeedback?.ShowCardPreview(card);
            }
        }

        // Diagnostic projection only. Formal hover consumes the one complete
        // immediate-damage summary above and never recomputes these trace plans.
        private void TraceCardHoverDamage(CardConfig card, CardImmediateDamageDisplaySummary immediateDamage)
        {
            if (!CombatLogger.EnableVerboseDiagnostics) return;
            CardAttackDisplaySummary display = combat.GetCardAttackDisplay(card);
            AttackPlan previewPlan = display.Plans.Count > 0
                ? display.Plans[0]
                : null;
            AttackSubHit firstSubHit = previewPlan != null &&
                previewPlan.SubHits.Count > 0
                    ? previewPlan.SubHits[0]
                    : null;
            ComboPreview comboPreview = combat.GetComboPreview(card);
            Debug.Log(
                $"[DamageTrace][Plan:Preview] cardId={card.contentId} " +
                $"cardInstance={card.runtimeInstanceId} " +
                $"might={combat.Player.fengRui} " +
                $"amount={combat.Player.amount} " +
                $"currentCombo={combat.Combo.Combo} " +
                $"plannedCombo={comboPreview.ResultingCombo} " +
                $"subHits={previewPlan?.SubHits.Count ?? 0} " +
                $"displayedCardDamage={immediateDamage.TotalDamage} " +
                $"nominalHostDamage={display.MainDamage} " +
                $"mainDamageIsEstimate={immediateDamage.IsEstimate} " +
                $"maxDirectEvents={previewPlan?.MaximumDirectEventCount ?? 0} " +
                $"maxBounceEvents={previewPlan?.MaximumBounceEventCount ?? 0} " +
                $"firstHitDamage={firstSubHit?.NormalHitDamage ?? 0} " +
                $"firstDirectTargets={firstSubHit?.DirectTargetCount ?? 0} " +
                $"firstBounceHits={firstSubHit?.Bounce?.hitsPerDirectHit ?? 0} " +
                $"firstBounceMode={firstSubHit?.Bounce?.sequenceMode.ToString() ?? "None"}");
            IReadOnlyList<EnemyDamagePreview> previews = immediateDamage.DamagePreviews;
            Debug.Log(
                $"[DamageTrace][Preview] cardId={card.contentId} " +
                $"cardInstance={card.runtimeInstanceId} " +
                $"combo={combat.Combo.Combo} " +
                $"previewTargets={previews.Count}");
            for (int i = 0; i < previews.Count; i++)
            {
                EnemyDamagePreview preview = previews[i];
                EnemyStats enemy = preview.TargetIndex >= 0 &&
                    preview.TargetIndex < combat.Enemies.Count
                        ? combat.Enemies[preview.TargetIndex]
                        : null;
                Debug.Log(
                    $"[DamageTrace][PreviewTarget] " +
                    $"runtime={preview.TargetEnemyRuntimeId} " +
                    $"index={preview.TargetIndex} " +
                    $"damage={preview.Damage} " +
                    $"applied={preview.AppliedDamage} " +
                    $"requested={preview.RequestedDamage} " +
                    $"overkill={preview.OverkillDamage} " +
                    $"isEstimate={preview.IsEstimate} " +
                    $"isErased={preview.IsErased} " +
                    $"willKill={preview.WillKill} " +
                    $"hp={enemy?.currentHealth ?? -1} " +
                    $"armor={enemy?.armor ?? -1} " +
                    $"row={enemy?.row ?? -1} " +
                    $"column={enemy?.column ?? -1} " +
                    $"visible={EnemyFormationLayout.IsFullyInsideVisibleRows(enemy)}");
            }
        }

        private void OnCardHoverExit()
        {
            ClearTransientCardHoverPresentation();
        }

        private void ClearTransientCardHoverPresentation()
        {
            battleHudView?.HideCostPreview();
            _cardTooltip?.Hide();
            enemyAreaView?.ClearDamagePreview();
            handView?.ResetAllComboPreviews();
            battleHudView?.ClearBuffPreview();
            crawlerResidentFeedback?.ClearCardPreview();
        }

        private void EnsureTooltip()
        {
            if (_cardTooltip != null) return;

            var canvas = battleHudView != null
                ? battleHudView.GetComponentInParent<Canvas>()
                : null;
            if (canvas == null)
                canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;

            var go = new GameObject("CardTooltip", typeof(RectTransform));
            go.transform.SetParent(encounterCanvasRoot != null ? encounterCanvasRoot : canvas.transform, false);
            go.transform.SetAsLastSibling();
            _cardTooltip = go.AddComponent<CardTooltipView>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, 120);
        }

        // ── 出牌流程 ──────────────────────────────────────────

        private void OnCardClicked(int sortedIndex)
        {
            if (!IsCombatInputEnabled) return;
            CancelPlayAll();
            PlayCardBySortedIndex(sortedIndex);
        }

        /// <summary>
        /// 执行一次出牌：Detach → TryPlayCard → 动画 → 刷新。
        /// </summary>
        private bool PlayCardBySortedIndex(int sortedIndex)
        {
            if (_handVM == null || combat == null) return false;
            if (!IsCombatInputEnabled || (handView != null &&
                !handView.IsCardReadyForInteraction(sortedIndex)))
            {
                return false;
            }

            // A click can arrive while the card is being reparented by the
            // hover layer. Clear the complete transient snapshot before the
            // hand is refreshed and pooled views are reused.
            ClearTransientCardHoverPresentation();

            var card = _handVM.GetCardAt(sortedIndex);
            if (card == null) return false;

            var cardView = handView?.GetCardView(sortedIndex);

            // Character cards can grant experience synchronously inside Core.
            // Capture those progression events before resolving the card so a
            // level-up overlay cannot cover the card's own play animation.
            bool ownsExperiencePresentation = HasImmediateExperienceEffect(card);
            if (ownsExperiencePresentation)
            {
                _immediateExperiencePresentations++;
                BeginCombatPresentation(lockCombatInput: false);
            }

            // 先脱离手牌，TryPlayCard 触发的 RefreshHand 不会影响此卡牌
            if (TryGetCardScreenOrigin(cardView, out Vector2 cardScreenOrigin))
                CardPlayScreenOriginPrepared?.Invoke(card.runtimeInstanceId, cardScreenOrigin);
            if (cardView != null && handView != null)
                handView.DetachCardView(cardView);

            bool success = combat.TryPlayCard(card);
            if (success && ownsExperiencePresentation &&
                _deferredLevelUpRewards != null &&
                _deferredLevelUpRewards.Count > 0)
            {
                // Ordinary experience does not pause combat. Escalate to a
                // hard input lock only after Core confirms a level boundary.
                BeginCombatPresentation(lockCombatInput: true);
            }

            if (success)
            {
                _comboVM.OnCardPlayed();

                // 普通断链、温和牌、断连牌都会结束旧链；后两者在核心结算中
                // 已经 ClearCombo，不能再只依赖 WasChainBroken 判断。
                ResolvedCardCost playedCost = card.ResolveCost();
                if (combat.Combo.WasChainBroken ||
                    playedCost.IsMild ||
                    playedCost.IsComboBreaker)
                    handView?.playPileView?.ClearPile();

                if (cardView != null && handView != null)
                {
                    handView.PlayCardAnimation(
                        cardView,
                        card.destroyOnUse,
                        () =>
                    {
                        if (this == null || !IsBound) return;
                        UpdateCardPlayability();
                        if (ownsExperiencePresentation)
                            FinishImmediateExperiencePresentation();
                    });
                }
                else
                {
                    UpdateCardPlayability();
                    if (ownsExperiencePresentation)
                        FinishImmediateExperiencePresentation();
                }
            }
            else
            {
                CardPlayOriginRejected?.Invoke(card.runtimeInstanceId);
                if (cardView != null && handView != null)
                    handView.ReattachCardView(cardView, sortedIndex);
                if (ownsExperiencePresentation)
                    FinishImmediateExperiencePresentation();
            }
            // Core has resolved and removed this exact instance synchronously.
            // Its visual completion only owns presentation/progression cleanup.
            return success;
        }

        private void FinishImmediateExperiencePresentation()
        {
            if (_immediateExperiencePresentations <= 0) return;
            // Concurrent experience cards share the progression capture;
            // a later level-up belongs behind every outstanding card flight.
            if (--_immediateExperiencePresentations == 0)
                EndCombatPresentation();
        }

        /// <summary>Capture the rendered card center before hand detachment/reparenting.</summary>
        public static bool TryGetCardScreenOrigin(CardView cardView, out Vector2 screenOrigin)
        {
            screenOrigin = default;
            if (cardView == null) return false;
            RectTransform cardRect = cardView.transform as RectTransform;
            Canvas canvas = cardView.GetComponentInParent<Canvas>();
            if (cardRect == null || canvas == null) return false;
            Canvas rootCanvas = canvas.rootCanvas;
            Camera canvasCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : rootCanvas.worldCamera;
            screenOrigin = RectTransformUtility.WorldToScreenPoint(
                canvasCamera, cardRect.TransformPoint(cardRect.rect.center));
            return true;
        }

        private static bool HasImmediateExperienceEffect(CardConfig card)
        {
            if (card?.effects == null) return false;
            for (int i = 0; i < card.effects.Count; i++)
            {
                CardEffectSpec effect = card.effects[i];
                if (effect != null &&
                    effect.effectType == EffectType.GainExperience)
                    return true;
            }
            return false;
        }

        // ── 全出：Core 选牌，会话模块控制执行边界 ──────────────

        public void OnPlayAllButton()
        {
            if (_playAllSequence?.IsActive == true)
            {
                CancelPlayAll();
                return;
            }
            if (!CanAcceptAutoPlay || !isActiveAndEnabled || !IsBound ||
                combat.IsCombatOver() || combat.HandCount == 0) return;
            _playAllSequence ??= new CombatAutoPlaySequence(this);
            StartCoroutine(_playAllSequence.Begin());
            RefreshAutoPlayButton();
        }

        private void CancelPlayAll()
        {
            _playAllSequence?.Cancel();
            RefreshAutoPlayButton();
        }

        internal bool CanAcceptAutoPlay => IsCombatInputEnabled &&
            combat != null && combat.State.CanPlayCards &&
            !IsAwaitingRewardSelection && !_processingRewardSelection;

        internal bool IsCardReadyForAutoPlay(CardConfig card)
        {
            int index = FindSortedIndexOf(card);
            return index >= 0 &&
                (handView == null || handView.IsCardReadyForInteraction(index));
        }

        internal bool TryAutoPlayCard(CardConfig card)
        {
            int index = FindSortedIndexOf(card);
            return CanAcceptAutoPlay && index >= 0 && PlayCardBySortedIndex(index);
        }

        internal void RefreshAutoPlayButton()
        {
            if (battleHudView == null || battleHudView.playAllButton == null) return;
            bool running = _playAllSequence?.IsActive == true;
            battleHudView.playAllButton.interactable = isActiveAndEnabled && IsBound &&
                (running || CanAcceptAutoPlay && combat.HandCount > 0 && !combat.IsCombatOver());
        }

        private int FindSortedIndexOf(CardConfig target)
        {
            if (_handVM == null) return -1;
            var sortedHand = _handVM.SortedHand;
            for (int i = 0; i < sortedHand.Count; i++)
                if (ReferenceEquals(_handVM.GetCardAt(i), target)) return i;
            return -1;
        }

        // ── 其他按钮 ──────────────────────────────────────────

        private void Update()
        {
            if (!IsCombatInputEnabled) return;
            if (ShortcutBindingRuntime.WasPressed(ShortcutAction.EndTurn))
                OnEndTurnButton();
        }

        private void UpdateCardPlayability()
        {
            if (handView == null || combat == null) return;
            handView.UpdateCardPlayability((idx) =>
            {
                var card = _handVM.GetCardAt(idx);
                if (card == null) return false;
                return combat.Mana.PreviewPayment(
                    card.ResolveCost().PaymentCost, out _);
            });

            TryAutoEndTurnWhenHandExhausted();
        }

        private void TryAutoEndTurnWhenHandExhausted()
        {
            if (_autoEndTurnPending || !isActiveAndEnabled ||
                combat == null || combat.IsCombatOver())
            {
                return;
            }

            if (combat.HandCount > 0 && HasPlayableCard())
                return;

            _autoEndTurnPending = true;
            _autoEndTurnRoutine = StartCoroutine(
                AutoEndTurnWhenHandExhaustedRoutine(combat, combat.State.CurrentTurn));
        }

        private bool HasPlayableCard()
        {
            if (combat?.Hand == null) return false;
            // Core owns the current instances and payment contract. A sorted
            // display snapshot cannot authorize or suppress ending a turn.
            foreach (CardConfig card in combat.Hand)
            {
                if (card != null && combat.Mana.PreviewPayment(
                        card.ResolveCost().PaymentCost, out _))
                    return true;
            }
            return false;
        }

        private void CancelAutoEndTurn()
        {
            // Deactivation can stop Unity coroutines without disposing their
            // iterators. Clear ownership explicitly before stopping the routine.
            _autoEndTurnPending = false;
            Coroutine routine = _autoEndTurnRoutine;
            _autoEndTurnRoutine = null;
            if (routine != null) StopCoroutine(routine);
        }

        private IEnumerator AutoEndTurnWhenHandExhaustedRoutine(
            CombatController source, int turn)
        {
            float settleDelay = 0f;
            try
            {
                while (_autoEndTurnPending)
                {
                    yield return null;

                    if (!isActiveAndEnabled || !IsBound || source == null ||
                        combat != source || source.IsCombatOver() ||
                        source.State.CurrentTurn != turn)
                        yield break;

                    if (source.HandCount > 0 && HasPlayableCard())
                        yield break;

                    if (!IsCombatInputEnabled
                        || !source.State.CanPlayCards
                        || _processingRewardSelection
                        || IsAwaitingRewardSelection
                        || source.HasPendingExperienceAwards
                        || _combatPresentationActive
                        || ShouldWaitForTurnSettlement())
                    {
                        // Rewards/refunds can make a new card payable. Retain
                        // this turn's intent and reevaluate after settlement.
                        settleDelay = 0f;
                        continue;
                    }

                    settleDelay += Time.unscaledDeltaTime;
                    if (settleDelay < autoEndTurnCooldownSeconds)
                        continue;

                    CommandResult result = TryRequestEndTurn();
                    if (result.Success) yield break;

                    // Final admission may change after the readiness probe.
                    // A rejected request does not consume the existing intent.
                    settleDelay = 0f;
                }
            }
            finally
            {
                _autoEndTurnPending = false;
                _autoEndTurnRoutine = null;
            }
        }

        private bool ShouldWaitForTurnSettlement()
        {
            if (combat == null || PendingTurnImpactProbe?.Invoke() == true)
                return true;

            if (combat.State.CurrentPhase == BattlePhase.EnemyActing ||
                enemyAreaView != null &&
                enemyAreaView.IsEnemyActionPresentationActive)
            {
                return true;
            }

            if (enemyAreaView != null &&
                !enemyAreaView.IsFormationPresentationSettled)
            {
                return true;
            }

            return false;
        }

        public void OnEndTurnButton()
            => TryRequestEndTurn();

        private CommandResult TryRequestEndTurn()
        {
            if (!IsCombatInputEnabled || combat == null || combat.IsCombatOver())
                return CommandResult.Fail(CommandError.InvalidPhase,
                    "当前战斗尚不能结束回合");
            CancelPlayAll();
            System.Func<CommandResult> handler = EndTurnRequested;
            return handler != null
                ? handler.Invoke()
                : combat.ExecuteCommand(new EndTurnCommand());
        }

        public void OnDrawButton()
        {
            if (!IsCombatInputEnabled) return;
            if (combat == null || combat.IsCombatOver()) return;
            CancelPlayAll();
            deckPileView?.ShowDrawPile();
        }

        // ── 升级三选一 ──────────────────────────────────────────

        private void OnLevelUpRewards(List<LevelUpSystem.Reward> rewards)
        {
            if (_combatPresentationActive)
            {
                _deferredLevelUpRewards = rewards == null
                    ? null
                    : new List<LevelUpSystem.Reward>(rewards);
                return;
            }
            if (_processingRewardSelection ||
                (_rewardRevealOverlay != null &&
                 _rewardRevealOverlay.IsShown))
            {
                _queuedRewards = rewards;
                return;
            }
            _pendingRewards = rewards;
            ShowLevelUpOverlay(rewards);
        }

        private void ShowLevelUpOverlay(List<LevelUpSystem.Reward> rewards)
        {
            if (rewards == null || rewards.Count == 0) return;

            long offerRevision = combat?.PendingLevelUpRewardRevision ?? 0L;

            var barrier = SceneTransitionRuntime.ExternalModalBarrier;
            if (_levelUpFlowLease == null && barrier != null)
                _levelUpFlowLease = barrier.Hold("modal.combat.level-up");

            Canvas canvas = battleHudView != null
                ? battleHudView.GetComponentInParent<Canvas>()
                : null;
            if (canvas == null)
            {
                if (rewards[0].type == LevelUpSystem.RewardType.Gem)
                {
                    Debug.LogError(
                        "[CombatPresentation] 缺少升级奖励 Canvas，" +
                        "不能为灵印自动选择宿主。升级保持待选状态。");
                    return;
                }
                Debug.LogError(
                    "[CombatPresentation] Level-up Canvas is missing; " +
                    "applying the first generated reward as a safe fallback.");
                _pendingRewards = null;
                _processingRewardSelection = true;
                string fallbackError = null;
                bool applied = combat != null &&
                    combat.TrySelectLevelUpReward(
                        rewards[0], offerRevision, out fallbackError);
                if (!applied)
                {
                    Debug.LogWarning(
                        "[CombatPresentation] 升级奖励回退选择失败：" +
                        (fallbackError ?? "战斗已不可用。"));
                    _processingRewardSelection = false;
                    RestorePendingLevelUpOverlay();
                    return;
                }
                _processingRewardSelection = false;
                FinishRewardFeedback();
                return;
            }

            if (_rewardChoiceOverlay == null)
                _rewardChoiceOverlay =
                    gameObject.AddComponent<RewardChoiceOverlay>();
            var items = new List<RewardFeedbackItem>(rewards.Count);
            for (int i = 0; i < rewards.Count; i++)
                items.Add(ToFeedbackItem(rewards[i], i));
            var actions = new List<RewardUtilityAction>();
            if (combat?.CanRerollPendingRewards == true)
            {
                actions.Add(new RewardUtilityAction(
                    RewardUtilityActionKind.Refresh,
                    $"重选 · {combat.RewardRerollsRemaining}",
                    OnRerollSelected));
            }
            if (combat != null && combat.CanCashOutPendingRewards)
            {
                actions.AddRange(RewardUtilityActionFactory.CreateCashOut(
                    _rewardUtilityAvailability,
                    combat.PreviewCashOut(),
                    OnCashOutSelected));
            }
            _rewardChoiceOverlay.Show(
                canvas,
                "境界突破 · 选择所得",
                items,
                actions,
                index => OnRewardSelected(index, offerRevision),
                RewardCardTooltipContext.FromCombat(combat?.State, _rewardTooltipRules));
        }

        private void OnRewardSelected(int index, long expectedOfferRevision)
        {
            if (_pendingRewards == null || index < 0 ||
                index >= _pendingRewards.Count) return;

            if (combat == null ||
                expectedOfferRevision <= 0L ||
                combat.PendingLevelUpRewardRevision != expectedOfferRevision)
            {
                Debug.LogWarning(
                    "[CombatPresentation] 忽略已过期的升级奖励请求。");
                _pendingRewards = null;
                RestorePendingLevelUpOverlay();
                return;
            }

            LevelUpSystem.Reward selected = _pendingRewards[index];
            _pendingRewards = null;
            _processingRewardSelection = true;
            System.Action applyAndContinue = () =>
            {
                string error = null;
                if (combat == null || !combat.TrySelectLevelUpReward(
                        selected, expectedOfferRevision, out error))
                {
                    Debug.LogWarning(
                        "[CombatPresentation] 升级奖励选择失败：" +
                        (error ?? "战斗已不可用。"));
                    _processingRewardSelection = false;
                    RestorePendingLevelUpOverlay();
                    return;
                }
                if (combat.HasPendingLevelUpSealAssignment)
                {
                    BeginPendingLevelUpSealAssignment();
                    return;
                }
                _processingRewardSelection = false;
                FinishRewardFeedback();
            };
            if (_rewardChoiceOverlay == null ||
                !_rewardChoiceOverlay.PlaySelectionAndClose(
                    index,
                    applyAndContinue))
            {
                _rewardChoiceOverlay?.Close();
                applyAndContinue();
            }
        }

        private void OnCashOutSelected()
        {
            _pendingRewards = null;
            _processingRewardSelection = true;
            System.Action cashOutAndContinue = () =>
            {
                JadeGainResult cashOut = default;
                combat?.TryCashOutPendingRewards(out cashOut);
                _processingRewardSelection = false;
                // 兑换结果直接汇入常驻灵玉 HUD，避免再弹出一张独立回执面板。
                hudView?.PlayJadeGain(cashOut.FinalJade);
                FinishRewardFeedback();
            };
            if (_rewardChoiceOverlay == null ||
                !_rewardChoiceOverlay.PlayClose(cashOutAndContinue))
            {
                _rewardChoiceOverlay?.Close();
                cashOutAndContinue();
            }
        }

        private void OnRerollSelected()
        {
            _pendingRewards = null;
            _processingRewardSelection = true;
            System.Action rerollAndContinue = () =>
            {
                string error = null;
                if (combat == null ||
                    !combat.TryRerollPendingRewards(out error))
                {
                    Debug.LogWarning(
                        "[CombatPresentation] 奖励重选失败：" +
                        (error ?? "战斗已不可用。"));
                    _processingRewardSelection = false;
                    RestorePendingLevelUpOverlay();
                    return;
                }
                _processingRewardSelection = false;
                FinishRewardFeedback();
            };
            if (_rewardChoiceOverlay == null ||
                !_rewardChoiceOverlay.PlayClose(rerollAndContinue))
            {
                _rewardChoiceOverlay?.Close();
                rerollAndContinue();
            }
        }

        private void ShowRewardReveal(RewardFeedbackItem item)
        {
            Canvas canvas = battleHudView != null
                ? battleHudView.GetComponentInParent<Canvas>()
                : null;
            if (canvas == null)
            {
                FinishRewardFeedback();
                return;
            }
            if (_rewardRevealOverlay == null)
                _rewardRevealOverlay =
                    RewardFeedbackPresenter.Create(this);
            _rewardRevealOverlay.Show(
                canvas,
                item,
                FinishRewardFeedback);
        }

        /// <summary>
        /// Reuses the common seal-target presentation while keeping the actual
        /// assignment in CombatController.  The coordinator owns only the
        /// visual selection; Core rechecks the chosen runtime card before it
        /// mutates a seal list or continues queued level-ups.
        /// </summary>
        private void BeginPendingLevelUpSealAssignment()
        {
            if (combat == null || !combat.HasPendingLevelUpSealAssignment)
            {
                _processingRewardSelection = false;
                RestorePendingLevelUpOverlay();
                return;
            }

            CardSealSpec seal = combat.PendingLevelUpSeal;
            IReadOnlyList<CardConfig> candidates =
                combat.GetPendingLevelUpSealCandidates();
            if (seal == null || candidates.Count == 0)
            {
                combat.CancelPendingLevelUpSealSelection();
                _processingRewardSelection = false;
                RestorePendingLevelUpOverlay();
                return;
            }
            if (!LingyinCatalog.TryGet(seal.contentId,
                    out LingyinConfig presentationSeal) ||
                presentationSeal == null ||
                presentationSeal.effectType != seal.effect ||
                presentationSeal.effectValue != seal.value)
            {
                Debug.LogWarning(
                    "[CombatPresentation] 升级灵印缺少与 Core 合同一致的" +
                    "作者资源：" + seal.contentId);
                combat.CancelPendingLevelUpSealSelection();
                _processingRewardSelection = false;
                RestorePendingLevelUpOverlay();
                return;
            }

            if (_levelUpSealAssignment == null)
            {
                _levelUpSealAssignment =
                    gameObject.AddComponent<SealAssignmentCoordinator>();
                _levelUpSealAssignment.BindPresentationAudio(
                    () => AudioManager.PlaySfxSafe(SfxType.SealStamp));
            }
            if (!_levelUpSealAssignment.TryBeginAssignment(
                    presentationSeal,
                    candidates,
                    cardRuntimeInstanceId =>
                    {
                        string error = null;
                        if (combat == null ||
                            !combat.TryAssignPendingLevelUpSeal(
                                cardRuntimeInstanceId,
                                out error))
                        {
                            Debug.LogWarning(
                                "[CombatPresentation] 灵印钤入失败：" +
                                (error ?? "战斗已不可用。"));
                            _levelUpSealAssignment.CloseSilently();
                            combat?.CancelPendingLevelUpSealSelection();
                            _processingRewardSelection = false;
                            RestorePendingLevelUpOverlay();
                            return;
                        }
                        // 提交后的真实牌区决定演出终点；战斗自动归库，不使用地图的
                        // 化粒和点击确认。Core 仍唯一拥有实例与牌区，UI 只投影屏幕终点。
                        SealCardReturnTarget returnTarget = default;
                        if (deckPileView == null ||
                            !deckPileView.TryGetCardReturnTarget(cardRuntimeInstanceId, out returnTarget))
                            Debug.LogWarning("[CombatPresentation] 钤印成果卡缺少可见归库目标，自动关闭成果展示。");
                        _levelUpSealAssignment.PlayCommitted(() =>
                        {
                            if (this == null || !IsBound || !isActiveAndEnabled) return;
                            _processingRewardSelection = false;
                            FinishRewardFeedback();
                        }, returnTarget);
                    },
                    () =>
                    {
                        combat?.CancelPendingLevelUpSealSelection();
                        _processingRewardSelection = false;
                        RestorePendingLevelUpOverlay();
                    },
                    showRewardReveal: false,
                    out string beginError))
            {
                Debug.LogWarning(
                    "[CombatPresentation] 无法打开灵印宿主选择：" +
                    beginError);
                combat.CancelPendingLevelUpSealSelection();
                _processingRewardSelection = false;
                RestorePendingLevelUpOverlay();
            }
        }

        private void RestorePendingLevelUpOverlay()
        {
            if (combat == null || !combat.AwaitingRewardSelection)
            {
                FinishRewardFeedback();
                return;
            }

            var restored = new List<LevelUpSystem.Reward>(
                combat.PendingLevelUpRewards.Count);
            for (int i = 0; i < combat.PendingLevelUpRewards.Count; i++)
                restored.Add(combat.PendingLevelUpRewards[i]);
            _pendingRewards = restored;
            ShowLevelUpOverlay(restored);
        }

        private void FinishRewardFeedback()
        {
            if (_queuedRewards != null && _queuedRewards.Count > 0)
            {
                List<LevelUpSystem.Reward> next = _queuedRewards;
                _queuedRewards = null;
                _pendingRewards = next;
                ShowLevelUpOverlay(next);
                return;
            }

            _levelUpFlowLease?.Dispose();
            _levelUpFlowLease = null;
            if (_levelUpCheckpointCompletion != null)
            {
                System.Action checkpoint =
                    _levelUpCheckpointCompletion;
                _levelUpCheckpointCompletion = null;
                checkpoint.Invoke();
                return;
            }
            if (!IsAwaitingRewardSelection && combat != null &&
                !combat.IsCombatOver())
                SetCombatInputEnabled(true);
        }

        private static RewardFeedbackItem ToFeedbackItem(
            LevelUpSystem.Reward reward,
            int index)
        {
            if (reward.type == LevelUpSystem.RewardType.NewCard &&
                reward.card != null)
            {
                return RewardFeedbackItem.FromCard(
                    $"reward.level-up.card.{reward.card.contentId}.{index}",
                    reward.card);
            }

            if (reward.type == LevelUpSystem.RewardType.Gem &&
                reward.seal != null &&
                LingyinCatalog.TryGet(reward.seal.contentId,
                    out LingyinConfig presentationSeal) &&
                presentationSeal != null &&
                presentationSeal.effectType == reward.seal.effect &&
                presentationSeal.effectValue == reward.seal.value)
            {
                RewardFeedbackItem item = RewardFeedbackItem.FromSeal(presentationSeal);
                item.ReceiptId = $"reward.level-up.{reward.type}.{index}";
                item.Quantity = reward.seal.value;
                return item;
            }

            return new RewardFeedbackItem
            {
                ReceiptId = $"reward.level-up.{reward.type}.{index}",
                Kind = reward.type == LevelUpSystem.RewardType.Gem
                    ? RewardFeedbackKind.Seal
                    : RewardFeedbackKind.Stat,
                DisplayName = reward.name,
                Description = reward.description,
                Quantity = reward.type == LevelUpSystem.RewardType.Gem
                    ? reward.seal?.value ?? 0
                    : reward.statAmount,
                Accent = new Color(0.74f, 0.57f, 0.30f, 1f)
            };
        }

        private void CloseLevelUpOverlay()
        {
            _rewardChoiceOverlay?.Close();
            _rewardRevealOverlay?.CloseSilently();
            _pendingRewards = null;
            _queuedRewards = null;
            _deferredLevelUpRewards = null;
            _pendingProgressRefresh = false;
            _levelUpCheckpointCompletion = null;
            _levelUpFlowLease?.Dispose();
            _levelUpFlowLease = null;
            _processingRewardSelection = false;
            _levelUpSealAssignment?.CloseSilently();
        }

        public void OnDiscardButton()
        {
            if (!IsCombatInputEnabled) return;
            if (combat == null || combat.IsCombatOver()) return;
            CancelPlayAll();
            deckPileView?.ShowDiscardPile();
        }

        // ── 生命周期 ──────────────────────────────────────────

        private void OnEnable()
        {
            if (!IsBound) return;
            UpdateCardPlayability();
            RefreshAutoPlayButton();
        }

        private void OnDisable()
        {
            CancelAutoEndTurn();
            CancelPlayAll();
        }

        public void ShutdownEncounter()
        {
            if (_shutdown) return;
            _shutdown = true;
            _combatInputRequested = false;
            enabled = false;
            IsBound = false;
            if (handView != null)
            {
                handView.SetInteractionEnabled(false);
                handView.Unbind();
                handView.SetCardClickCallback(null);
                handView.SetHoverCallback(null, null);
            }
            if (hudView != null) hudView.Unbind();
            if (deckPileView != null) deckPileView.Unbind();
            if (_cardTooltip != null) _cardTooltip.Hide();
            if (_ownsBrushComboView && _brushComboView != null)
                Destroy(_brushComboView.gameObject);
            IsCombatInputEnabled = false;
            CancelAutoEndTurn();
            CancelPlayAll();
            _combatPresentationInputLease?.Dispose();
            _combatPresentationInputLease = null;
            _combatInputLocks.Clear();
            if (comboChainView != null)
                comboChainView.InkSpreadRequested -= OnInkSpreadRequested;
            if (AudioManager.Instance != null && combat != null)
                AudioManager.Instance.UnbindCombatEvents(combat);
            if (combat != null)
            {
                combat.OnStateChanged -= OnCombatStateChanged;
                combat.OnDamagePreviewsReady -= RefreshAutoPlayButton;
                combat.OnCardPlayResolved -= OnCardPlayResolved;
                combat.OnCardsDrawn -= OnCardsDrawn;
                combat.OnTurnEnd -= OnTurnEnd;
                combat.OnLevelUpRewardsGenerated -= OnLevelUpRewards;
            }
            UnbindBattleHud();
            CloseLevelUpOverlay();

            _handVM?.Dispose();
            _comboVM?.Dispose();
            _hudVM?.Dispose();
            _levelUpVM?.Dispose();
            if (object.ReferenceEquals(UIController.ArtConfig, artConfig))
                UIController.ArtConfig = null;
            IsInitialized = false;
        }

        private void OnDestroy() => ShutdownEncounter();

        private void OnInkSpreadRequested(float intensity, bool climax)
        {
            inkSpreadEffect?.Play(intensity, climax);
        }

        private void BindBattleHud()
        {
            if (battleHudView == null || combat == null) return;
            battleHudView.drawButton?.onClick.AddListener(OnDrawButton);
            battleHudView.playAllButton?.onClick.AddListener(OnPlayAllButton);
            battleHudView.endTurnButton?.onClick.AddListener(OnEndTurnButton);
            battleHudView.discardButton?.onClick.AddListener(OnDiscardButton);
            combat.OnStateChanged += OnCombatStateChanged;
            // CardPlayed can still be CardResolving. Project the button again
            // only after Core restores the next decision state and its previews.
            combat.OnDamagePreviewsReady += RefreshAutoPlayButton;
            combat.OnCardPlayResolved += OnCardPlayResolved;
            combat.OnCardsDrawn += OnCardsDrawn;
            combat.OnTurnEnd += OnTurnEnd;
        }

        private void UnbindBattleHud()
        {
            if (battleHudView == null) return;
            battleHudView.drawButton?.onClick.RemoveListener(OnDrawButton);
            battleHudView.playAllButton?.onClick.RemoveListener(OnPlayAllButton);
            battleHudView.endTurnButton?.onClick.RemoveListener(OnEndTurnButton);
            battleHudView.discardButton?.onClick.RemoveListener(OnDiscardButton);
            if (combat != null)
            {
                combat.OnStateChanged -= OnCombatStateChanged;
                combat.OnDamagePreviewsReady -= RefreshAutoPlayButton;
                combat.OnCardPlayResolved -= OnCardPlayResolved;
                combat.OnCardsDrawn -= OnCardsDrawn;
                combat.OnTurnEnd -= OnTurnEnd;
            }
        }

        private void OnTurnEnd()
        {
            CancelPlayAll();
            handView?.playPileView?.ClearPile();
        }

        private BrushComboView _brushComboView;

        private void OnCombatStateChanged(CombatStateChangeReason reason)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Diagnostic only: retain this controller's existing event ownership.
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log($"[ComboTrace][State] frame={Time.frameCount} t={Time.unscaledTime:F3} " +
                    $"controller={GetInstanceID()} reason={reason} phase={combat?.State.CurrentPhase} " +
                    $"core={combat?.Combo?.ComboLength} last={combat?.Combo?.LastComboValue} " +
                    $"broken={combat?.Combo?.WasChainBroken} bonus={combat?.Combo?.OutstandingWuxingBonus}", this);
#endif
            bool progressionChanged =
                reason == CombatStateChangeReason.XPGained ||
                reason == CombatStateChangeReason.LevelChanged;
            if (_combatPresentationActive && progressionChanged)
                _pendingProgressRefresh = true;
            if (!_combatPresentationActive || !_pendingProgressRefresh)
                RefreshBattleHud(reason);
            // CardPlayed 由 OnCardPlayResolved 携带“是否断链”的精确信息驱动。
            if (reason != CombatStateChangeReason.CardPlayed &&
                combat.State.CurrentPhase != BattlePhase.CardResolving)
                UpdateComboDisplay();
            // HandViewModel owns this event's hand snapshot/notification.
            UpdateCardPlayability();
        }

        private void OnCardPlayResolved(CombatCardResolvedEvent result)
        {
            if (combat == null || result == null) return;

            // Start the transfer before CardPlayed refreshes the HUD. The buff
            // bar then holds its previous value until the glyph arrives.
            battleHudView?.PlayStatGains(SupportArtifactFeedbackCoordinator.GetStandardHudGains(result));
            battleHudView?.PlayHealthGain(result.HealAmount, result.MaxHealthGained);
            battleHudView?.PlayManaGain(SupportArtifactFeedbackCoordinator.GetStandardManaGain(result));

            // Effect-triggered cards settle their own resources but never
            // advance/reset the player's chain. Comboed=false is not a break.
            if (result.PlayOrigin == CardPlayOrigin.CardEffect) return;

            ResolvedCardCost cost = result.Card != null
                ? result.Card.ResolveCost()
                : default;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log($"[ComboTrace][Resolved] frame={Time.frameCount} t={Time.unscaledTime:F3} " +
                    $"controller={GetInstanceID()} card={result.Card?.contentId} wild={cost.IsWild} " +
                    $"mild={cost.IsMild} breaker={cost.IsComboBreaker} comboValue={cost.ComboValue} " +
                    $"comboed={result.Comboed} core={combat.Combo?.ComboLength} " +
                    $"broken={combat.Combo?.WasChainBroken} bonus={combat.Combo?.OutstandingWuxingBonus}", this);
#endif
            if (cost.IsMild || cost.IsComboBreaker)
            {
                // 这两类卡结算后真正清空连击，不重开 ×1。
                UpdateComboDisplay(0, false);
                return;
            }

            // 显示口径 = ComboLength（含未吸收的相生加成）：
            // 相生+1 后连击数字立刻多 +1，下一张牌吸收后继续累加。
            int comboLength = combat.Combo?.ComboLength ?? 0;
            UpdateComboDisplay(
                comboLength,
                restartChain: !result.Comboed && comboLength > 0);
        }

        private void OnCardsDrawn(CombatCardsDrawnEvent result)
        {
            RectTransform source = battleHudView != null &&
                battleHudView.deckCountText != null
                ? battleHudView.deckCountText.rectTransform
                : null;

            // For the first turn, pass the initial draw callback through
            System.Action onComplete = null;
            if (_firstTurnStarted && _initialDrawCallback != null &&
                combat?.State?.CurrentTurn == 1)
            {
                onComplete = _initialDrawCallback;
                _initialDrawCallback = null;
            }

            handView?.QueueDrawPresentation(
                result.CardInstanceIds,
                source,
                onComplete);
        }

        private void RefreshBattleHud(CombatStateChangeReason? reason = null)
        {
            if (battleHudView == null || combat == null) return;

            battleHudView.SetResources(
                combat.Player.currentHealth,
                combat.Player.maxHealth,
                combat.Mana.CurrentMana + combat.Mana.TemporaryMana,
                combat.Mana.BaseMana,
                deferResourceDisplay:
                    combat.State.CurrentPhase == BattlePhase.CardResolving &&
                    reason != CombatStateChangeReason.CardPlayed &&
                    reason != CombatStateChangeReason.CombatEnded);

            battleHudView.SetArmor(combat.Player.armor);

            // comboText 由 UpdateComboDisplay 统一处理，这里不再设置

            bool battleActive = !combat.IsCombatOver();
            // When input is gated (e.g. during initial draw), keep all buttons disabled
            bool inputAllowed = battleActive && IsCombatInputEnabled;
            if (battleHudView.drawButton != null)
            {
                battleHudView.drawButton.interactable = inputAllowed;
            }
            RefreshAutoPlayButton();
            if (battleHudView.discardButton != null)
            {
                battleHudView.discardButton.interactable = inputAllowed;
            }
            if (battleHudView.endTurnButton != null)
                battleHudView.endTurnButton.interactable = inputAllowed;

            // 进度条/经验条/波次
            // 如果 waveText 未绑定，尝试从 TopProgress/Level 查找
            if (battleHudView.waveText == null)
            {
                var levelTr = battleHudView.transform.Find("TopProgress/Level");
                if (levelTr != null)
                    battleHudView.waveText = levelTr.GetComponent<UnityEngine.UI.Text>();
            }
            if (battleHudView.progressFullImage != null)
            {
                battleHudView.progressFullImage.fillAmount =
                    (float)combat.CurrentXP /
                    Mathf.Max(1, combat.RequiredXP);
            }
            if (battleHudView.waveText != null)
            {
                battleHudView.waveText.text = $"Lv.{combat.CurrentLevel}";
            }

            if (reason.HasValue)
                battleHudView.RefreshBuffsForStateChange(combat.Player,
                    combat.GetDisplayStatValue, reason.Value, combat.State.CurrentPhase);
            else
                battleHudView.RefreshBuffs(combat.Player, combat.GetDisplayStatValue);
        }

        /// <summary>
        /// 更新连击长度显示——出牌堆上方的程序化墨点数字。
        /// 位置/大小参数在 Inspector「连击倍率显示」Header 下设置。
        /// </summary>
        private void UpdateComboDisplay()
        {
            int comboLength = combat?.Combo?.ComboLength ?? 0;
            UpdateComboDisplay(comboLength, false);
        }

        private void UpdateComboDisplay(
            int comboLength,
            bool restartChain)
        {
            if (combat == null || battleHudView == null) return;

            // 禁用旧文字连击显示
            if (battleHudView.comboMultiplierText != null && battleHudView.comboMultiplierText.gameObject.activeSelf)
                battleHudView.comboMultiplierText.gameObject.SetActive(false);
            if (battleHudView.comboText != null && battleHudView.comboText.gameObject.activeSelf)
                battleHudView.comboText.gameObject.SetActive(false);

            if (_brushComboView == null)
            {
                _brushComboView = battleHudView.brushComboView;
                if (_brushComboView == null)
                    _brushComboView = CreateBrushComboView();
            }

            if (_brushComboView == null) return;

            ApplyComboLayout();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log($"[ComboTrace][Dispatch] frame={Time.frameCount} t={Time.unscaledTime:F3} " +
                    $"controller={GetInstanceID()} view={_brushComboView.GetInstanceID()} " +
                    $"requested={comboLength} restart={restartChain} core={combat.Combo?.ComboLength}", this);
#endif
            _brushComboView.SetComboLength(comboLength, restartChain);
        }

        private void ApplyComboLayout()
        {
            if (_brushComboView == null) return;
            var rect = _brushComboView.GetComponent<RectTransform>();
            if (rect == null) return;

            boardHudLayout?.AttachCombo(_brushComboView, comboAnchor, comboOffset);
            rect.anchorMin = comboAnchor;
            rect.anchorMax = comboAnchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = comboOffset;
            rect.sizeDelta = comboSize;
            _brushComboView.SetViewportHeight(comboSize.y);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
                ApplyComboLayout();
        }

        private BrushComboView CreateBrushComboView()
        {
            Canvas canvas = battleHudView.GetComponentInParent<Canvas>();
            if (canvas == null) return null;

            var go = new GameObject("BrushComboView",
                typeof(RectTransform), typeof(BrushComboView));
            go.transform.SetParent(encounterCanvasRoot != null ? encounterCanvasRoot : canvas.transform, false);
            _ownsBrushComboView = true;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = comboAnchor;
            rt.anchorMax = comboAnchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = comboSize;
            rt.anchoredPosition = comboOffset;

            BrushComboView view = go.GetComponent<BrushComboView>();
            view.SetViewportHeight(comboSize.y);
            return view;
        }
    }
}
