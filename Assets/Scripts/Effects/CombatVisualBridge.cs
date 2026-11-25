using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Effects.Attacks;
using WanjieScroll.Effects.Projectiles;
using WanjieScroll.Effects.Terrain;
using CardImpactContext = WanjieScroll.Effects.CombatAttackPresentationQueue.Context;
using PendingHit = WanjieScroll.Effects.CombatAttackPresentationQueue.PendingHit;
using PendingTerrainPlacement = WanjieScroll.Effects.CombatAttackPresentationQueue.PendingTerrainPlacement;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 战斗事件 → 视觉特效 桥接器
    /// 监听 CombatController 事件，驱动 VisualEffectManager 播放特效
    /// 通过 VfxProfileRegistry 按 contentId 查找卡牌专属特效配置
    /// 2026-09-30 模块审查：本类仍是现有卡牌/地形表现的事件适配器；死亡
    /// 状态、vacancy、尾效、watchdog 与 XP 门控全部由深模块
    /// CombatDefeatPresentationQueue 唯一拥有。攻击来源、抵达去重、浮字序号、
    /// 命中快照、地形附件和完成/取消状态由 CombatAttackPresentationQueue
    /// 唯一拥有；本适配器只捕获 Core 事实并调度现有视觉算法。
    /// 命中快照仅捕获实际可见主体；延迟演出使用捕获时的合法锚点，不回读候补位置。
    /// 2026-10-06 审查：砂浪几何与节奏由 AuthoritativeSandWavePlayer 独占；
    /// 本类只转发来源完成/取消事实，不持有生成牌或HUD动画状态。
    /// 2026-10-07 审查：中央火符的飞行、蓄势、点燃与清理由
    /// AuthoritativeFireTalismanPlayer 独占；此处只分派原有Arc权威组，
    /// 不新增动画状态、选敌或伤害知识，后续拆分仍按攻击表现队列边界进行。
    /// 2026-10-09 审查：折返短剑的主体、路径、逐发时间和取消由
    /// AuthoritativeReturningBladePlayer 唯一拥有；本类只记录完整Core计划
    /// 并分派权威回执，不以动画选敌或重复推导最终攻击物数。
    /// 2026-10-10 审查：回旋剑阵的列阵、逐剑穿杀、暴击剑芒和收束
    /// 由 AuthoritativeSwordArrayPlayer 独占；本类仅转发同一攻击队列的
    /// 完整计划与已捕获回执。
    /// 2026-10-11 审查：游龙连续巡游对象、逐接触节拍与取消清理由
    /// AuthoritativeWaterDragonSweepPlayer 独占；此处仅增加既有权威攻击
    /// 路由，不新增选敌、伤害、龙数或动画可变状态。
    /// 2026-10-22：每源短效尾部与延迟连击由 AttackEffectCompletion 独占；
    /// 此适配器仅传递票据，死亡闸门仍由 CombatDefeatPresentationQueue 拥有。
    /// 2026-10-23：击退只捕获同一来源的完成票据，位移顺序仍归阵型队列；
    /// 关Boss局部亮层由StageBossHitResponse持有，不在桥内增加动画状态。
    /// </summary>
    public class CombatVisualBridge : MonoBehaviour
    {
        [Header("引用")]
        public CombatController combat;
        public VisualEffectManager vfxManager;

        [Header("特效开关")]
        public bool enableHitStop = true;
        public bool enablePunchScale = true;

        private static readonly Color DefaultHitColor = new Color(1f, 0.85f, 0.3f);

        private readonly CombatAttackPresentationQueue _attackPresentations =
            new CombatAttackPresentationQueue();
        private bool _presentationActive;
        private CombatDefeatPresentationQueue _defeatPresentations;
        private PeriodicAttackPresenter _periodicAttackPresenter;
        private System.Func<int, Vector3?> _cardCastOrigin;
        private System.Func<int, Vector3?> _enemyGroundAnchor;
        private System.Func<BoardVisualFrame?> _boardFrame;
        private const float DamageNumberStagger = 0.075f;

        /// <summary>
        /// Broad lifecycle signals for observers that need to know whether any
        /// combat visual is still alive (for example, the victory return flow).
        /// They are deliberately not an input or formation lock: only an
        /// authoritative level-up checkpoint may pause active combat.
        /// </summary>
        public event System.Action PresentationStarted;
        public event System.Action PresentationCompleted;
        /// <summary>The source's real attack objects finished their contacts.
        /// Used only to join generated-card presentation, never gate Core.</summary>
        public event System.Action<int> CardAttackPresentationCompleted;
        public event System.Action<int> CardAttackPresentationCancelled;
        /// <summary>
        /// Raised after a death subject stops reading as a living monster but
        /// before fragments and ink residue finish. Bootstrap uses this as the
        /// visual hand-off for already-authoritative formation backfill.
        /// </summary>
        public event System.Action<int> DeathSubjectVacated;
        /// <summary>
        /// Requests presentation and settlement of one deferred experience
        /// award after its source enemy's death has fully completed. Multiple
        /// requests may be alive together; Bootstrap keeps their visuals
        /// concurrent while Core settlement remains authoritative FIFO.
        /// </summary>
        public event System.Action<CombatExperienceAward, Vector3, System.Action>
            ExperienceAwardPresentationRequested;
        public bool IsPresentationActive => _presentationActive;
        public bool HasPendingImpacts => _attackPresentations.ActiveCount > 0;

        public void BindPeriodicAttackPresenter(PeriodicAttackPresenter presenter)
        {
            _periodicAttackPresenter = presenter;
        }

        public void BindCardCastOriginResolver(System.Func<int, Vector3?> resolver)
            => _cardCastOrigin = resolver;
        public void BindEnemyGroundAnchorResolver(System.Func<int, Vector3?> resolver)
            => _enemyGroundAnchor = resolver;
        public bool TryGetCardCastOrigin(int cardInstanceId, out Vector3 position)
        {
            Vector3? captured = FindImpactContext(cardInstanceId)?.CastOrigin;
            position = captured ?? default;
            return captured.HasValue;
        }

        /// <summary>Captures this exact cast's terminal short-effect boundary.
        /// Later casts of the same physical card cannot replace the captured
        /// ticket. Cancellation releases already-committed formation movement
        /// without reporting another hit or waiting for unrelated sources.</summary>
        public System.Func<bool> CaptureAttackEffectCompletion(int sourceCardInstanceId)
        {
            AttackEffectCompletion ticket =
                FindImpactContext(sourceCardInstanceId)?.EffectsCompletion;
            return ticket != null ? () => ticket.IsComplete : (System.Func<bool>)null;
        }
        public void BindBoardFrameResolver(System.Func<BoardVisualFrame?> resolver)
            => _boardFrame = resolver;
        public bool IsLevelUpCheckpointPaused =>
            _defeatPresentations != null &&
            _defeatPresentations.IsLevelUpCheckpointPaused;

        /// <summary>
        /// Stops this bridge from starting later deaths or experience flights.
        /// Already-running scaled world effects are paused by the coordinator's
        /// level-up time freeze; this gate covers the bridge's unscaled stagger.
        /// </summary>
        public void SetLevelUpCheckpointPaused(bool paused)
        {
            EnsureDefeatPresentations()
                .SetLevelUpCheckpointPaused(paused);
        }

        /// <summary>
        /// Cancels visuals while the combat remains alive. Runtime-owned death
        /// records release their vacancies before late callbacks are invalidated.
        /// Scene destruction uses Dispose instead and publishes nothing.
        /// </summary>
        public void CancelPresentationsInField()
        {
            GetComponent<AuthoritativeFireRingPlayer>()?.CancelAll();
            GetComponent<AuthoritativeFireTalismanPlayer>()?.CancelAll();
            GetComponent<AuthoritativeReturningBladePlayer>()?.CancelAll();
            GetComponent<AuthoritativeSwordArrayPlayer>()?.CancelAll();
            GetComponent<AuthoritativeWaterDragonSweepPlayer>()?.CancelAll();
            _periodicAttackPresenter?.CancelFlights();
            _defeatPresentations?.CancelInField();
            var impacts = new List<CardImpactContext>(_attackPresentations.Active);
            for (int i = 0; i < impacts.Count; i++)
                ReleaseImpact(impacts[i]);
            TryCompletePresentation();
        }

        private void Awake()
        {
            EnsureDefeatPresentations();
        }

        private CombatDefeatPresentationQueue EnsureDefeatPresentations()
        {
            if (_defeatPresentations != null)
                return _defeatPresentations;

            _defeatPresentations = new CombatDefeatPresentationQueue(
                this,
                () => vfxManager,
                enemyRuntimeId =>
                    DeathSubjectVacated?.Invoke(enemyRuntimeId),
                DispatchExperiencePresentation,
                TryCompletePresentation);
            return _defeatPresentations;
        }

        private void Start()
        {
            VfxProfileRegistry.EnsureInitialized();
            BindEvents();
        }

        private void Update()
        {
            // A pooled flight can be externally removed before contact. Its
            // ticket reports cancellation, not a fake arrival. Retire only that
            // source; sibling casts and committed Core results are untouched.
            List<CardImpactContext> cancelled = null;
            for (int i = 0; i < _attackPresentations.ActiveCount; i++)
            {
                CardImpactContext context = _attackPresentations.Active[i];
                if (!context.EffectsCompletion.IsCancelled) continue;
                if (cancelled == null) cancelled = new List<CardImpactContext>();
                cancelled.Add(context);
            }
            if (cancelled != null)
                for (int i = 0; i < cancelled.Count; i++) ReleaseImpact(cancelled[i]);
            if (vfxManager == null)
            {
                GetComponent<AuthoritativeFireRingPlayer>()?.CancelAll();
                GetComponent<AuthoritativeFireTalismanPlayer>()?.CancelAll();
                GetComponent<AuthoritativeReturningBladePlayer>()?.CancelAll();
                GetComponent<AuthoritativeSwordArrayPlayer>()?.CancelAll();
                GetComponent<AuthoritativeWaterDragonSweepPlayer>()?.CancelAll();
            }
            if (vfxManager != null || _attackPresentations.ActiveCount == 0) return;

            // A manager can be removed while projectiles are in flight. Close
            // every source explicitly so its runtime-owned death record takes
            // the no-visible-hit fallback instead of waiting forever.
            var abandoned = new List<CardImpactContext>(_attackPresentations.Active);
            for (int i = 0; i < abandoned.Count; i++)
            {
                abandoned[i].EffectsCompletion.Cancel();
                _attackPresentations.MarkFlightFinished(abandoned[i]);
                ReleaseImpact(abandoned[i]);
            }
        }

        public void BindEvents()
        {
            // 防止重复绑定
            UnbindEvents();
            GetComponent<AuthoritativeFireRingPlayer>()?.CancelAll();
            GetComponent<AuthoritativeFireTalismanPlayer>()?.CancelAll();
            GetComponent<AuthoritativeReturningBladePlayer>()?.CancelAll();
            GetComponent<AuthoritativeSwordArrayPlayer>()?.CancelAll();
            GetComponent<AuthoritativeWaterDragonSweepPlayer>()?.CancelAll();
            if (combat == null) return;
            combat.OnCardPlayedVFX += OnCardPlayed;
            combat.OnCardPlayResolved += OnCardPlayResolved;
            combat.OnDamageEvent += OnDamageEvent;
            combat.OnEnemyKilled += OnEnemyKilled;
            combat.OnExperienceAwarded += OnExperienceAwarded;
            combat.OnTerrainEffectChanged += OnTerrainEffectChanged;
            combat.OnPeriodicAttackChanged += OnPeriodicAttackChanged;
            combat.OnColumnReplayAttackResolved += OnColumnReplayAttackResolved;
            combat.OnTurnEnd += ClearRetainedFireRings;
            combat.OnCombatEnded += OnFireRingCombatEnded;
            combat.OnProjectileTracesResolved += OnProjectileTracesResolved;
            combat.OnAttackPlanResolved += OnAttackPlanResolved;
        }

        private void UnbindEvents()
        {
            if (combat == null) return;
            combat.OnCardPlayedVFX -= OnCardPlayed;
            combat.OnCardPlayResolved -= OnCardPlayResolved;
            combat.OnDamageEvent -= OnDamageEvent;
            combat.OnEnemyKilled -= OnEnemyKilled;
            combat.OnExperienceAwarded -= OnExperienceAwarded;
            combat.OnTerrainEffectChanged -= OnTerrainEffectChanged;
            combat.OnPeriodicAttackChanged -= OnPeriodicAttackChanged;
            combat.OnColumnReplayAttackResolved -= OnColumnReplayAttackResolved;
            combat.OnTurnEnd -= ClearRetainedFireRings;
            combat.OnCombatEnded -= OnFireRingCombatEnded;
            combat.OnProjectileTracesResolved -= OnProjectileTracesResolved;
            combat.OnAttackPlanResolved -= OnAttackPlanResolved;
        }

        private void OnDisable()
        {
            GetComponent<AuthoritativeFireRingPlayer>()?.CancelAll();
            GetComponent<AuthoritativeFireTalismanPlayer>()?.CancelAll();
            GetComponent<AuthoritativeReturningBladePlayer>()?.CancelAll();
            GetComponent<AuthoritativeSwordArrayPlayer>()?.CancelAll();
            GetComponent<AuthoritativeWaterDragonSweepPlayer>()?.CancelAll();
        }

        private void OnDestroy()
        {
            _presentationActive = false;
            GetComponent<AuthoritativeFireRingPlayer>()?.CancelAll();
            GetComponent<AuthoritativeFireTalismanPlayer>()?.CancelAll();
            GetComponent<AuthoritativeReturningBladePlayer>()?.CancelAll();
            GetComponent<AuthoritativeSwordArrayPlayer>()?.CancelAll();
            GetComponent<AuthoritativeWaterDragonSweepPlayer>()?.CancelAll();
            _periodicAttackPresenter?.CancelFlights();
            _defeatPresentations?.Dispose();
            while (_attackPresentations.ActiveCount > 0)
                ReleaseImpact(_attackPresentations.Active[0]);
            _attackPresentations.Clear();
            // Scene disposal invalidates the presentation generation. Do not
            // publish completion into observers that may already belong to the
            // next scene.
            _presentationActive = false;
            UnbindEvents();
        }

        // ==================== 出牌：发射投射物 / 增益粒子 ====================

        private void OnCardPlayed(CardConfig card, int targetIndex)
        {
            if (vfxManager == null) return;

            // per-card 出牌音效（叠加在全局翻纸声之上）
            VfxProfile profile = VfxProfileRegistry.Get(card);
            if (profile == null || !profile.periodicFireCrow)
                _periodicAttackPresenter?.ForgetCardOrigin(card.runtimeInstanceId);
            else
                _periodicAttackPresenter?.PrepareInitialAttack(card.runtimeInstanceId, profile);
            vfxManager.PlayCardSfx(profile);

            // Artifact geometry starts with the cast, before the next-frame
            // authority-recorded lightning queue. It never adds hits or delays
            // that queue; the resolved presenter deliberately does not replay it.
            if (!card.ResolveCost().IsWild &&
                vfxManager.playerAttackOrigin != null && profile != null &&
                profile.visualType != AuthoritativeFireRingPlayer.VisualTypeName &&
                (profile.selfEffectStyle == CardSelfEffectStyle.LightningRing ||
                 profile.selfEffectStyle == CardSelfEffectStyle.ThunderWoodSword))
                vfxManager.PlayCardSelfEffect(
                    _cardCastOrigin?.Invoke(card.runtimeInstanceId) ??
                        (vfxManager.playerAttackOrigin.position + Vector3.up * 0.52f),
                    profile, null);

            // 读 effects 判断效果类型，不依赖 baseDamage 字段
            bool hasDamage = AttackPlanBuilder.TryFromCard(
                card,
                null,
                out DamageSpec _);
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log($"[VFX] OnCardPlayed card={card.cardName} hasSpecEffects={card.HasSpecEffects} effectsCount={card.effects?.Count ?? 0} hasDamage={hasDamage}");

            bool hasErase = card.effects != null && card.effects.Exists(effect =>
                effect != null && effect.effectType == EffectType.Erase);
            if (hasDamage || hasErase)
            {
                BeginPresentationIfNeeded();
                CardImpactContext context = _attackPresentations.Begin(
                    card.runtimeInstanceId, profile);
                _attackPresentations.RecordCastOrigin(context, _cardCastOrigin?.Invoke(card.runtimeInstanceId));
                CaptureImpactTargets(context);
                if (hasDamage)
                    FireProjectilePath(card, targetIndex, context);
                else
                    // Preserve the profile for authority-registered erase
                    // deaths without inventing a projectile or DamageEvent.
                    _attackPresentations.MarkFlightFinished(context);
            }
            // Authoritative gains already fly to HUD/resident targets. A named
            // artifact may have its own cast, but no generic centre burst is added.
        }

        private void OnCardPlayResolved(CombatCardResolvedEvent result)
        {
            CardImpactContext context = FindImpactContext(
                result?.Card != null ? result.Card.runtimeInstanceId : 0);
            if (context == null) return;

            _attackPresentations.MarkCoreResolved(context);
            if (!context.AwaitingProjectile)
            {
                FinishProjectileImpact(context);
            }
        }

        private void OnProjectileTracesResolved(ProjectileTraceBatch batch)
        {
            if (batch == null) return;
            _attackPresentations.RecordProjectileTraces(FindImpactContext(batch.SourceCardInstanceId), batch.Traces);
        }

        private void OnAttackPlanResolved(AttackPlan plan)
        {
            if (plan == null || plan.SubHits.Count == 0 ||
                plan.SubHits[0].TargetPolicy != TargetPolicy.RandomColumnBagMultiHit) return;
            CardImpactContext context = FindImpactContext(plan.SourceCardInstanceId);
            if (context?.Profile?.visualType == AuthoritativeReturningBladePlayer.VisualTypeName ||
                context?.Profile?.visualType == AuthoritativeSwordArrayPlayer.VisualTypeName)
                _attackPresentations.RecordAttackPlan(context, plan);
        }

        private void FireProjectilePath(
            CardConfig card,
            int targetIndex,
            CardImpactContext context)
        {
            VfxProfile profile = context.Profile;

            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log($"[VFX] OnCardPlayed card={card.cardName} contentId={card.contentId} pattern={card.attackPattern} profile={profile?.name}");

            switch (card.attackPattern)
            {
                case AttackPattern.Arc:
                    StartCoroutine(FireArcAfterDamageEvents(
                        card, targetIndex, profile, context));
                    break;
                case AttackPattern.WhipSlash:
                    if (profile != null &&
                        profile.useAuthoritativeHitSequence &&
                        (profile.visualType == ProceduralWhipSweepPlayer.VisualTypeName ||
                         profile.visualType == ProceduralRakeSweep.VisualTypeName))
                    {
                        StartCoroutine(
                            FireAuthoritativeWhipSweepAfterDamageEvents(
                                context));
                        break;
                    }
                    goto case AttackPattern.MultiTarget;
                case AttackPattern.MultiShot:
                case AttackPattern.MultiTarget:
                    {
                        if (AuthoritativeProjectileSequencePolicy.ShouldPlay(
                                profile))
                        {
                            StartCoroutine(
                                FireAuthoritativeMultiShotAfterDamageEvents(
                                    context));
                            break;
                        }

                        var targetable = combat != null
                            ? combat.GetTargetableEnemyIndices()
                            : new List<int>();
                        targetable.RemoveAll(index =>
                            !TryResolveImpactPosition(context, index, out _));
                        if (targetable.Count == 0)
                        {
                            OnAllProjectilesArrived(context);
                            break;
                        }

                        int count = Mathf.Max(1, card.projectileCount);
                        int[] targets = new int[count];
                        for (int i = 0; i < count; i++)
                            targets[i] = targetable[i % targetable.Count];

                        var landingPositions = new Vector3[targets.Length];
                        for (int i = 0; i < targets.Length; i++)
                            landingPositions[i] = ResolveImpactPosition(
                                context,
                                targets[i]);
                        vfxManager.FireMultiShotAtPositions(
                            targets,
                            landingPositions,
                            profile,
                            arrived => OnEachProjectileArrive(context, arrived),
                            () => OnAllProjectilesArrived(context), context.EffectsCompletion);
                        break;
                    }
                default:
                    if (AuthoritativeProjectileSequencePolicy.ShouldPlay(
                            profile))
                    {
                        StartCoroutine(
                            FireAuthoritativeMultiShotAfterDamageEvents(
                                context));
                        break;
                    }
                    if (!TryResolveImpactPosition(context, targetIndex, out Vector3 landing))
                    {
                        OnAllProjectilesArrived(context);
                        break;
                    }
                    vfxManager.FireProjectileAtPosition(
                        targetIndex,
                        landing,
                        () =>
                        {
                            OnSingleProjectileArrive(context, targetIndex);
                            OnAllProjectilesArrived(context);
                        },
                        profile, context.EffectsCompletion);
                    break;
            }

        }

        private IEnumerator FireAuthoritativeWhipSweepAfterDamageEvents(
            CardImpactContext context)
        {
            // Core resolves the whole card synchronously. Waiting one frame
            // lets the sweep consume the exact target snapshot recorded for
            // every attack object without querying the now-changed formation.
            yield return null;

            if (context == null || !context.AwaitingProjectile ||
                vfxManager == null)
            {
                ReleaseImpact(context);
                yield break;
            }

            var recordedHits = new List<AuthoritativeWhipHit>();
            for (int i = 0; i < context.Hits.Count; i++)
            {
                PendingHit pending = context.Hits[i];
                DamageEvent damageEvent = pending.damageEvent;
                if (!pending.hasWorldAnchor || damageEvent.Kind != DamageKind.Direct)
                    continue;
                if (context.CardInstanceId != 0 &&
                    damageEvent.SourceCardInstanceId != 0 &&
                    damageEvent.SourceCardInstanceId !=
                        context.CardInstanceId)
                {
                    continue;
                }

                Vector3 position = pending.worldAnchor;
                recordedHits.Add(new AuthoritativeWhipHit(
                    damageEvent,
                    position,
                    i));
            }

            List<AuthoritativeWhipSweep> sweeps =
                AuthoritativeWhipSweepBuilder.Build(recordedHits);
            if (sweeps.Count == 0)
            {
                OnAllProjectilesArrived(context);
                yield break;
            }

            Vector3 origin = vfxManager.playerAttackOrigin != null
                    ? vfxManager.playerAttackOrigin.position
                    : new Vector3(0f, -3.5f, 0f);
            if (context.Profile.visualType == ProceduralRakeSweep.VisualTypeName)
            {
                // One contact sound per actual attack object, regardless of
                // how many enemies share the authority-recorded row.
                var soundedGroups = new HashSet<int>();
                yield return ProceduralRakeSweep.Play(
                    vfxManager.transform, origin, sweeps, context.Profile,
                    hit => OnAuthoritativeWhipHit(context, hit,
                        soundedGroups.Add(hit.DamageEvent.AttackInstanceIndex)),
                    () => this == null || !context.AwaitingProjectile || !isActiveAndEnabled);
            }
            else
            {
                yield return ProceduralWhipSweepPlayer.Play(
                    vfxManager.transform, origin, sweeps, context.Profile,
                    hit => OnAuthoritativeWhipHit(context, hit));
            }

            if (context.AwaitingProjectile)
                OnAllProjectilesArrived(context);
        }

        private List<AuthoritativeProjectileSequence> BuildRecordedProjectileSequences(CardImpactContext context)
        {
            var recordedHits = new List<AuthoritativeProjectileHit>();
            bool nativeBounce = UsesNativeBounceSequence(context.AttackPlan);
            for (int i = 0; i < context.Hits.Count; i++)
            {
                PendingHit pending = context.Hits[i];
                DamageKind kind = pending.damageEvent.Kind;
                if (!pending.hasWorldAnchor ||
                    (kind != DamageKind.Direct && kind != DamageKind.Bounce &&
                     !(nativeBounce && kind == DamageKind.Splash))) continue;
                if (context.CardInstanceId != 0 && pending.damageEvent.SourceCardInstanceId != 0 &&
                    pending.damageEvent.SourceCardInstanceId != context.CardInstanceId) continue;
                recordedHits.Add(new AuthoritativeProjectileHit(pending.damageEvent, pending.worldAnchor, i));
            }
            return AuthoritativeProjectileSequenceBuilder.Build(recordedHits);
        }

        private static bool UsesNativeBounceSequence(AttackPlan plan)
            => plan != null && plan.SubHits.Count > 0 &&
               plan.SubHits[0].Bounce?.enabled == true &&
               plan.SubHits[0].Bounce.sequenceMode == BounceSequenceMode.NativePerAttackInstance;

        // Another authoritative source uses the same queue and lightning player;
        // this adapter adds no target, hit, death or renderer ownership.
        private void OnColumnReplayAttackResolved(ColumnReplayAttackEvent replay)
        {
            if (!isActiveAndEnabled || replay == null || vfxManager == null) return;
            VfxProfile profile = VfxProfileRegistry.Get(replay.SourceCardContentId);
            if (profile == null || !AuthoritativeProjectileSequencePolicy.ShouldPlay(profile)) return;
            BeginPresentationIfNeeded();
            CardImpactContext context = _attackPresentations.Begin(
                replay.SourceCardInstanceId, profile, coreResolved: true);
            CaptureImpactTargets(context);
            AuthoritativeFireRingPlayer.ReplayClaim ringReplay = profile.visualType == AuthoritativeFireRingPlayer.VisualTypeName
                ? AuthoritativeFireRingPlayer.ClaimReplay(this, replay.SourceCardInstanceId) : null;
            StartCoroutine(FireAuthoritativeMultiShotAfterDamageEvents(context, ringReplay, true));
        }

        // A source can resolve victory/turn-end before its first frame is drawn.
        // Its deferred attack may finish, but must not retain a ring for that retired turn.
        private int _fireRingLifetimeGeneration;
        private void ClearRetainedFireRings()
        {
            _fireRingLifetimeGeneration++;
            GetComponent<AuthoritativeFireRingPlayer>()?.ClearRetainedRings();
        }

        private void OnFireRingCombatEnded(CombatEndedEvent ended) => ClearRetainedFireRings();

        private void OnPeriodicAttackChanged(PeriodicAttackEvent change)
        {
            if (!isActiveAndEnabled || change?.Source == null || change.Kind != PeriodicAttackChangeKind.Triggered ||
                vfxManager == null || _periodicAttackPresenter == null) return;
            VfxProfile profile = VfxProfileRegistry.Get(change.Source.SourceCardContentId);
            if (profile == null || !profile.periodicFireCrow) return;

            // Triggered precedes Damage/death publication, but its Core plan is
            // already resolved. Do not leave CoreResolving open into unrelated
            // enemy-turn terrain damage later in the same synchronous stack.
            BeginPresentationIfNeeded();
            CardImpactContext context = _attackPresentations.Begin(
                change.Source.SourceCardInstanceId, profile, coreResolved: true);
            CaptureImpactTargets(context);
            _periodicAttackPresenter.QueuePeriodicAttack(change.Source);
            StartCoroutine(FirePeriodicAfterDamageEvents(change.Source, context));
        }

        private IEnumerator FirePeriodicAfterDamageEvents(PeriodicAttackSource source, CardImpactContext context)
        {
            yield return null;
            if (!context.AwaitingProjectile || vfxManager == null || _periodicAttackPresenter == null)
            {
                _periodicAttackPresenter?.DiscardPeriodicAttack(source);
                ReleaseImpact(context);
                yield break;
            }
            _attackPresentations.MarkCoreResolved(context);
            yield return _periodicAttackPresenter.PlayPeriodicAttack(source,
                BuildRecordedProjectileSequences(context), context.Profile,
                hit => OnPeriodicProjectileArrive(context, hit),
                isLastSource => OnAllProjectilesArrived(context,
                    playHitStop: isLastSource, synchronousReceipts: true), context.EffectsCompletion);
            if (context.AwaitingProjectile)
                OnAllProjectilesArrived(context, playHitStop: false, synchronousReceipts: true);
        }

        private IEnumerator FireAuthoritativeMultiShotAfterDamageEvents(CardImpactContext context,
            AuthoritativeFireRingPlayer.ReplayClaim ringReplay = null, bool isColumnReplay = false)
        {
            // Collect the exact synchronous receipts; the crow owner separately
            // consumes the full resolved plan, including hits truncated by victory.
            int fireRingGeneration = _fireRingLifetimeGeneration;
            yield return null;
            if (context == null || !context.AwaitingProjectile || vfxManager == null)
            {
                AuthoritativeFireRingPlayer.DiscardReplay(this, ringReplay);
                ReleaseImpact(context);
                yield break;
            }
            List<AuthoritativeProjectileSequence> sequences = BuildRecordedProjectileSequences(context);
            if (isColumnReplay)
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log($"[VFX][ColumnReplay] source={context.CardInstanceId} " +
                        $"presentation={context.SourcePresentationId} sequences={sequences.Count} receipts={context.Hits.Count}");
            if (context.Profile != null && context.Profile.visualType ==
                ProceduralWaterDragonVisual.VisualTypeName)
            {
                yield return AuthoritativeWaterDragonSweepPlayer.Play(this, vfxManager,
                    context, _boardFrame?.Invoke(),
                    () => context.AwaitingProjectile && isActiveAndEnabled,
                    hit => OnAuthoritativeProjectileArrive(context, hit));
                if (!isActiveAndEnabled)
                {
                    ReleaseImpact(context);
                    yield break;
                }
            }
            else if (context.ProjectileTraces.Count > 0)
            {
                yield return AuthoritativeRicochetPlayer.Play(vfxManager, context.ProjectileTraces,
                    context.Hits, context.Profile, _boardFrame?.Invoke(),
                    () => context.AwaitingProjectile && isActiveAndEnabled,
                    hit => OnAuthoritativeProjectileArrive(context, hit), context.EffectsCompletion);
                if (!isActiveAndEnabled)
                {
                    ReleaseImpact(context);
                    yield break;
                }
            }
            else if (context.Profile != null && context.Profile.visualType ==
                AuthoritativeSwordArrayPlayer.VisualTypeName)
            {
                yield return AuthoritativeSwordArrayPlayer.Play(this, vfxManager,
                    context.Hits, context.Profile, _boardFrame?.Invoke(), context.CastOrigin,
                    context.AttackPlan,
                    () => context.AwaitingProjectile && isActiveAndEnabled,
                    hit => OnAuthoritativeProjectileArrive(context, hit), context.EffectsCompletion);
                if (!isActiveAndEnabled)
                {
                    ReleaseImpact(context);
                    yield break;
                }
            }
            else if (context.Profile != null && context.Profile.visualType ==
                AuthoritativeReturningBladePlayer.VisualTypeName)
            {
                yield return AuthoritativeReturningBladePlayer.Play(this, vfxManager,
                    context.Hits, context.Profile, _boardFrame?.Invoke(), context.CastOrigin,
                    context.AttackPlan,
                    () => context.AwaitingProjectile && isActiveAndEnabled,
                    hit => OnAuthoritativeProjectileArrive(context, hit), context.EffectsCompletion);
                if (!isActiveAndEnabled)
                {
                    ReleaseImpact(context);
                    yield break;
                }
            }
            else if (context.Profile != null && context.Profile.periodicFireCrow &&
                _periodicAttackPresenter != null)
            {
                yield return _periodicAttackPresenter.PlayInitialAttack(
                    context.CardInstanceId, sequences, context.Profile,
                    hit => OnAuthoritativeProjectileArrive(context, hit), context.EffectsCompletion);
            }
            else if (context.Profile != null && context.Profile.visualType ==
                AuthoritativeSandWavePlayer.VisualTypeName)
            {
                yield return AuthoritativeSandWavePlayer.Play(this, vfxManager,
                    context.Hits, context.Profile, _boardFrame?.Invoke(), context.CastOrigin,
                    () => context.AwaitingProjectile && isActiveAndEnabled,
                    hit => OnAuthoritativeProjectileArrive(context, hit));
            }
            else if (context.Profile != null && context.Profile.visualType ==
                AuthoritativeFireRingPlayer.VisualTypeName)
            {
                yield return AuthoritativeFireRingPlayer.Play(this, vfxManager,
                    sequences, context.Profile, context.CastOrigin,
                    () => context.AwaitingProjectile && isActiveAndEnabled,
                    hit => OnAuthoritativeProjectileArrive(context, hit),
                    context.CardInstanceId, ringReplay, isColumnReplay,
                    () => fireRingGeneration == _fireRingLifetimeGeneration);
            }
            else if (context.Profile != null && context.Profile.visualType ==
                AuthoritativeLightningStrikePlayer.VisualTypeName)
            {
                yield return AuthoritativeLightningStrikePlayer.Play(this, vfxManager,
                    sequences, context.Profile, hit => OnAuthoritativeProjectileArrive(context, hit), context.EffectsCompletion);
            }
            else
            {
                yield return AuthoritativeProjectileSequencePlayer.Play(this, vfxManager,
                    sequences, context.Profile, hit => OnAuthoritativeProjectileArrive(context, hit), context.CastOrigin,
                    completeEachSequenceBeforeNext: UsesNativeBounceSequence(context.AttackPlan),
                    completion: context.EffectsCompletion);
            }
            if (context.AwaitingProjectile) OnAllProjectilesArrived(context);
        }
        private IEnumerator FireArcAfterDamageEvents(
            CardConfig card,
            int fallbackTargetIndex,
            VfxProfile profile,
            CardImpactContext context)
        {
            // OnCardPlayedVFX 先于同步伤害结算触发。等待一帧后读取已经入队的
            // DamageEvent，才能让击退、死亡刷新和数量产生的真实目标进入演出。
            yield return null;

            if (context == null || !context.AwaitingProjectile || card == null)
            {
                ReleaseImpact(context);
                yield break;
            }

            List<int> attackInstanceIndices;
            List<int[]> groups = BuildArcTargetGroups(
                card, context.Hits, out attackInstanceIndices);
            if (groups.Count == 0)
            {
                groups = BuildFallbackArcTargetGroups(card, fallbackTargetIndex, context);
                attackInstanceIndices.Clear();
            }
            if (groups.Count == 0)
            {
                OnAllProjectilesArrived(context);
                yield break;
            }

            List<Vector3> landingPositions = BuildArcLandingPositions(
                card, groups, attackInstanceIndices, context.Hits);

            if (profile != null && profile.visualType == AuthoritativeFireTalismanPlayer.VisualTypeName)
            {
                // Fire uses the ground snapshot captured with its placement,
                // while all other attacks retain their body-centre anchors.
                var firePositions = new List<Vector3>(landingPositions);
                for (int i = 0; i < groups.Count; i++)
                {
                    int[] group = groups[i];
                    if (group == null || group.Length == 0) continue;
                    int attackIndex = i < attackInstanceIndices.Count ? attackInstanceIndices[i] : -1;
                    if (_attackPresentations.TryGetTerrainAnchor(context, attackIndex, group[0], out Vector3 ground))
                        firePositions[i] = ground;
                    else
                        firePositions[i] = ResolveTerrainAnchor(group[0]);
                }
                yield return AuthoritativeFireTalismanPlayer.Play(this, vfxManager,
                    groups, firePositions, profile, context.CastOrigin,
                    () => context.AwaitingProjectile && isActiveAndEnabled,
                    arrived => OnEachProjectileArrive(context, arrived));
                if (context.AwaitingProjectile) OnAllProjectilesArrived(context);
                yield break;
            }

            vfxManager.FireArcVolley(groups, landingPositions, profile,
                arrived => OnEachProjectileArrive(context, arrived),
                () => OnAllProjectilesArrived(context), context.EffectsCompletion);
        }

        /// <summary>
        /// Uses the first authoritative Direct target of each resolved attack
        /// as its landing point. PendingHit stores the target's world position
        /// when the DamageEvent is raised, before death/backfill presentation.
        /// </summary>
        private List<Vector3> BuildArcLandingPositions(
            CardConfig card,
            IReadOnlyList<int[]> groups,
            IReadOnlyList<int> attackInstanceIndices,
            IReadOnlyList<PendingHit> pendingHits)
        {
            var positions = new List<Vector3>();
            if (card == null || groups == null || vfxManager == null)
                return positions;

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                int[] group = groups[groupIndex];
                int primaryTargetIndex = group != null && group.Length > 0
                    ? group[0]
                    : -1;
                int attackInstanceIndex =
                    attackInstanceIndices != null &&
                    groupIndex < attackInstanceIndices.Count
                        ? attackInstanceIndices[groupIndex]
                        : -1;

                bool foundAnchor = false;
                if (attackInstanceIndex >= 0)
                {
                    for (int i = 0; i < pendingHits.Count; i++)
                    {
                        PendingHit pending = pendingHits[i];
                        DamageEvent damageEvent = pending.damageEvent;
                        if (damageEvent.Kind != DamageKind.Direct ||
                            damageEvent.AttackInstanceIndex != attackInstanceIndex ||
                            damageEvent.TargetIndex != primaryTargetIndex ||
                            (card.runtimeInstanceId != 0 &&
                             damageEvent.SourceCardInstanceId != 0 &&
                             damageEvent.SourceCardInstanceId != card.runtimeInstanceId))
                            continue;

                        if (pending.hasWorldAnchor)
                        {
                            positions.Add(pending.worldAnchor);
                            foundAnchor = true;
                        }
                        break;
                    }
                }
                if (foundAnchor) continue;

                CardImpactContext context = FindImpactContext(
                    card.runtimeInstanceId);
                if (group != null)
                {
                    for (int i = 0; i < group.Length; i++)
                    {
                        if (TryResolveImpactPosition(
                                context, group[i], out Vector3 captured))
                        {
                            positions.Add(captured);
                            foundAnchor = true;
                            break;
                        }
                    }
                }
                if (!foundAnchor)
                {
                    positions.Add(Vector3.zero);
                }
            }
            return positions;
        }

        private void CaptureImpactTargets(CardImpactContext context)
        {
            if (context == null || vfxManager == null) return;
            int count = combat != null
                ? combat.Enemies.Count
                : vfxManager.enemyTransforms.Count;
            for (int i = 0; i < count; i++)
            {
                if (!TryGetDamageAnchor(i, out Vector3 anchor)) continue;
                _attackPresentations.RecordTarget(context, i, anchor);
            }
        }

        private Vector3 ResolveImpactPosition(
            CardImpactContext context,
            int targetIndex)
            => TryResolveImpactPosition(context, targetIndex, out Vector3 position)
                ? position : Vector3.zero;

        private bool TryResolveImpactPosition(
            CardImpactContext context,
            int targetIndex,
            out Vector3 position)
        {
            return _attackPresentations.TryGetTarget(context, targetIndex, out position);
        }

        private List<int[]> BuildArcTargetGroups(
            CardConfig card,
            IReadOnlyList<PendingHit> pendingHits,
            out List<int> attackInstanceIndices)
        {
            var orderedGroups = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < pendingHits.Count; i++)
            {
                DamageEvent damageEvent = pendingHits[i].damageEvent;
                if (!pendingHits[i].hasWorldAnchor ||
                    damageEvent.Kind != DamageKind.Direct) continue;
                if (card.runtimeInstanceId != 0 &&
                    damageEvent.SourceCardInstanceId != 0 &&
                    damageEvent.SourceCardInstanceId != card.runtimeInstanceId)
                    continue;

                if (!orderedGroups.TryGetValue(
                    damageEvent.AttackInstanceIndex, out List<int> targets))
                {
                    targets = new List<int>();
                    orderedGroups.Add(damageEvent.AttackInstanceIndex, targets);
                }
                if (!targets.Contains(damageEvent.TargetIndex))
                    targets.Add(damageEvent.TargetIndex);
            }

            var result = new List<int[]>(orderedGroups.Count);
            attackInstanceIndices = new List<int>(orderedGroups.Count);
            foreach (KeyValuePair<int, List<int>> pair in orderedGroups)
            {
                if (pair.Value.Count == 0) continue;
                result.Add(pair.Value.ToArray());
                attackInstanceIndices.Add(pair.Key);
            }
            return result;
        }

        private List<int[]> BuildFallbackArcTargetGroups(
            CardConfig card, int fallbackTargetIndex, CardImpactContext context)
        {
            List<int> targetable = combat != null
                ? combat.GetTargetableEnemyIndices()
                : new List<int>();
            if (targetable.Count == 0 && fallbackTargetIndex >= 0)
                targetable.Add(fallbackTargetIndex);
            targetable.RemoveAll(index =>
                !TryResolveImpactPosition(context, index, out _));
            if (targetable.Count == 0) return new List<int[]>();

            bool reverse = card.targetPolicy == TargetPolicy.SequentialRightToLeft ||
                card.targetPolicy == TargetPolicy.PlayedTargetThenSequential;
            if (reverse) targetable.Reverse();

            int attackInstanceCount = Mathf.Max(1, card.projectileCount);
            int targetsPerAttack = card.directTargetsPerHit <= 0
                ? targetable.Count
                : Mathf.Min(card.directTargetsPerHit, targetable.Count);
            var result = new List<int[]>(attackInstanceCount);
            for (int attackIndex = 0;
                 attackIndex < attackInstanceCount;
                 attackIndex++)
            {
                var group = new int[targetsPerAttack];
                int start = attackIndex * targetsPerAttack;
                for (int i = 0; i < targetsPerAttack; i++)
                    group[i] = targetable[(start + i) % targetable.Count];
                result.Add(group);
            }
            return result;
        }

        // ==================== 权威 DamageEvent：等待投射物抵达 ====================

        private void OnDamageEvent(DamageEvent damageEvent)
        {
            if (damageEvent.AppliedDamage <= 0)
                return;

            bool hasWorldAnchor = TryGetDamageAnchor(
                damageEvent.TargetIndex,
                out Vector3 worldAnchor);

            CardImpactContext context = FindImpactContext(
                damageEvent.SourceCardInstanceId);
            if (context != null && context.CoreResolved &&
                damageEvent.AttackInstanceIndex < 0)
                context = null;
            if (context == null && _attackPresentations.CoreResolving != null &&
                _attackPresentations.CoreResolving.AwaitingProjectile)
                context = _attackPresentations.CoreResolving;
            // A hit may itself hide or move the subject before its receipt is
            // published. A visible pre-cast snapshot is still a legal anchor;
            // a waiting reserve has no such snapshot and remains suppressed.
            if (!hasWorldAnchor && context != null)
                hasWorldAnchor = TryResolveImpactPosition(
                    context, damageEvent.TargetIndex, out worldAnchor);
            if (context != null && context.AwaitingProjectile)
            {
                int pendingHitIndex = _attackPresentations.EnqueueHit(
                    context, damageEvent, worldAnchor, hasWorldAnchor);
                if (damageEvent.IsFatal &&
                    damageEvent.TargetEnemyRuntimeId > 0)
                {
                    EnsureDefeatPresentations().ObserveFatalHit(
                        damageEvent.TargetEnemyRuntimeId,
                        new CombatDefeatPresentationQueue.FatalHitToken(
                            context.SourcePresentationId,
                            pendingHitIndex,
                            damageEvent.Kind,
                            context.EffectsCompletion));
                }
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[DamageTrace][VFX:Queued] " +
                        $"runtime={damageEvent.TargetEnemyRuntimeId} " +
                        $"index={damageEvent.TargetIndex} " +
                        $"attack={damageEvent.AttackInstanceIndex} " +
                        $"subHit={damageEvent.SubHitIndex} " +
                        $"kind={damageEvent.Kind} " +
                        $"applied={damageEvent.AppliedDamage} " +
                        $"hasAnchor={hasWorldAnchor} " +
                        $"anchor=({worldAnchor.x:F2},{worldAnchor.y:F2},{worldAnchor.z:F2}) " +
                        $"pending={context.Hits.Count}");
                return;
            }

            // Burn/DoT 等不经过投射物的伤害应在事件发生时立即浮字。
            ShowDamageEvent(damageEvent, worldAnchor, hasWorldAnchor);
            if (damageEvent.IsFatal &&
                damageEvent.TargetEnemyRuntimeId > 0)
            {
                EnsureDefeatPresentations().ObserveFatalHit(
                    damageEvent.TargetEnemyRuntimeId,
                    new CombatDefeatPresentationQueue.FatalHitToken(
                        0,
                        -1,
                        damageEvent.Kind));
            }
        }

        // ==================== 敌人死亡：暂存，等投射物抵达后再播放 ====================

        private void OnEnemyKilled(EnemyStats enemy)
        {
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log($"[VFX] OnEnemyKilled enemy={enemy?.runtimeId ?? -1}");
            if (enemy == null || enemy.runtimeId <= 0) return;

            int index = -1;
            if (combat != null)
            {
                for (int i = 0; i < combat.Enemies.Count; i++)
                {
                    if (combat.Enemies[i] == enemy) { index = i; break; }
                }
            }

            CardImpactContext context = _attackPresentations.FindFatalSource(enemy.runtimeId) ??
                _attackPresentations.CoreResolving;
            if (index < 0)
            {
                Debug.LogWarning(
                    $"[VFX] OnEnemyKilled no matching enemy index runtime={enemy?.runtimeId ?? -1}");
            }
            BeginPresentationIfNeeded();
            EnsureDefeatPresentations().RegisterDeath(
                // 隐藏区的持续伤害死亡仍正常结算经验/腾空，但不复制可见死亡主体。
                EnemyFormationLayout.IsFullyInsideVisibleRows(enemy) ? index : -1,
                enemy.runtimeId,
                context?.Profile,
                enemy.isBoss);
        }

        private void OnExperienceAwarded(CombatExperienceAward award)
        {
            if (combat == null ||
                combat.ExperienceAwardCommitMode !=
                    ExperienceAwardCommitMode.DeferredSegments)
                return;

            BeginPresentationIfNeeded();
            EnsureDefeatPresentations().RecordExperienceAward(award);
        }

        // ==================== 每发投射物到达：对该目标播放命中效果 ====================

        private void OnSingleProjectileArrive(
            CardImpactContext context,
            int targetIndex)
        {
            if (context == null || !context.AwaitingProjectile) return;
            bool foundHit = TryFindNextDirectHit(
                context,
                targetIndex,
                out int pendingHitIndex,
                out DamageEvent presentedHit) &&
                PresentPendingHit(context, pendingHitIndex);
            if (foundHit)
                MarkFatalHitPresentation(context, pendingHitIndex);
            bool isCritical = foundHit && presentedHit.IsCritical;
            if (foundHit && IsStageBossTarget(targetIndex))
            {
                Transform stageBoss = vfxManager?.GetEnemyTransform(targetIndex);
                if (stageBoss != null && TryGetDamageAnchor(targetIndex, out _))
                {
                    StageBossHitResponse response =
                        stageBoss.GetComponent<StageBossHitResponse>() ??
                        stageBoss.gameObject.AddComponent<StageBossHitResponse>();
                    response.Play(ResolveImpactPosition(context, targetIndex), isCritical);
                }
                return;
            }
            if (!isCritical || !enablePunchScale || vfxManager == null)
                return;

            Transform target = vfxManager.GetEnemyTransform(targetIndex);
            if (target == null || !TryGetDamageAnchor(targetIndex, out _))
                return;

            VfxProfile profile = context?.Profile;
            float punch = profile != null
                ? profile.critPunchScaleIntensity
                : 0.25f;
            vfxManager.PunchScale(target, punch);
        }

        private void OnEachProjectileArrive(
            CardImpactContext context,
            int targetIndex)
        {
            if (vfxManager == null || context == null || !context.AwaitingProjectile) return;

            PresentNextTerrainPlacement(context, targetIndex);

            Vector3 hitPos = ResolveImpactPosition(context, targetIndex);
            bool foundHit = TryFindNextDirectHit(
                context,
                targetIndex,
                out int pendingHitIndex,
                out DamageEvent presentedHit) &&
                PresentPendingHit(context, pendingHitIndex);
            bool isCritical = foundHit && presentedHit.IsCritical;

            PresentAttackImpact(
                context,
                targetIndex,
                hitPos,
                isCritical,
                DamageKind.Direct);
            if (foundHit)
                MarkFatalHitPresentation(context, pendingHitIndex);

            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log($"[VFX] OnEachArrive target={targetIndex}");
        }

        private void OnPeriodicProjectileArrive(
            CardImpactContext context,
            AuthoritativeProjectileHit hit)
        {
            if (vfxManager == null || context == null || !context.AwaitingProjectile) return;

            // The periodic owner delivers flights in receipt order. Consume
            // skipped, non-visible receipts before this flight, then its splash
            // up to the next flight. All deduplication stays with the queue.
            PresentPeriodicReceipts(context, 0, hit.PendingHitIndex);
            OnAuthoritativeProjectileArrive(context, hit);
            IReadOnlyList<PendingHit> hits = context.Hits;
            int endIndex = hit.PendingHitIndex + 1;
            while (endIndex < hits.Count)
            {
                DamageKind kind = hits[endIndex].damageEvent.Kind;
                if (kind == DamageKind.Direct || kind == DamageKind.Bounce) break;
                endIndex++;
            }
            PresentPeriodicReceipts(context, hit.PendingHitIndex + 1, endIndex);
        }

        private void PresentPeriodicReceipts(CardImpactContext context, int startIndex, int endIndex)
        {
            for (int i = startIndex; i < endIndex && vfxManager != null &&
                context.AwaitingProjectile; i++)
            {
                if (PresentPendingHit(context, i))
                    MarkFatalHitPresentation(context, i);
            }
        }

        private void OnAuthoritativeProjectileArrive(
            CardImpactContext context,
            AuthoritativeProjectileHit hit)
        {
            if (vfxManager == null || context == null || !context.AwaitingProjectile) return;

            DamageEvent damageEvent = hit.DamageEvent;
            if (!PresentPendingHit(context, hit.PendingHitIndex)) return;
            if (damageEvent.Kind == DamageKind.Direct)
            {
                PresentNextTerrainPlacement(
                    context,
                    damageEvent.TargetIndex);
            }

            // PresentPendingHit already plays the Splash response. Hidden
            // receipts still consume their number/death latch without particles.
            if (damageEvent.Kind != DamageKind.Splash &&
                context.Hits[hit.PendingHitIndex].hasWorldAnchor)
            {
                PresentAttackImpact(
                    context,
                    damageEvent.TargetIndex,
                    hit.Position,
                    damageEvent.IsCritical,
                    damageEvent.Kind);
            }
            MarkFatalHitPresentation(context, hit.PendingHitIndex);

            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[VFX][AuthoritativeFlight] " +
                    $"source={context.CardInstanceId} presentation={context.SourcePresentationId} " +
                    $"attack={damageEvent.AttackInstanceIndex} " +
                    $"subHit={damageEvent.SubHitIndex} " +
                    $"kind={damageEvent.Kind} " +
                    $"target={damageEvent.TargetIndex}");
        }

        private void OnAuthoritativeWhipHit(
            CardImpactContext context,
            AuthoritativeWhipHit hit,
            bool playHitSound = true)
        {
            if (vfxManager == null || context == null || !context.AwaitingProjectile) return;

            DamageEvent damageEvent = hit.DamageEvent;
            if (!PresentPendingHit(context, hit.PendingHitIndex)) return;
            PresentNextTerrainPlacement(
                context,
                damageEvent.TargetIndex);
            PresentAttackImpact(
                context,
                damageEvent.TargetIndex,
                hit.Position,
                damageEvent.IsCritical,
                damageEvent.Kind);
            if (context.Profile != null &&
                context.Profile.visualType == ProceduralRakeSweep.VisualTypeName)
                vfxManager.PlaySmallHitEffect(
                    hit.Position,
                    context.Profile,
                    playHitSound,
                    emitParticles: false);
            MarkFatalHitPresentation(context, hit.PendingHitIndex);
            Transform target = vfxManager.GetEnemyTransform(
                damageEvent.TargetIndex);
            if (target != null && !IsStageBossTarget(damageEvent.TargetIndex) &&
                TryGetDamageAnchor(damageEvent.TargetIndex, out _))
            {
                StartCoroutine(
                    ProceduralWhipTargetResponse.NudgeRight(target));
            }

            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[VFX][AuthoritativeWhipSweep] " +
                    $"attack={damageEvent.AttackInstanceIndex} " +
                    $"target={damageEvent.TargetIndex}");
        }

        private void PresentAttackImpact(
            CardImpactContext context,
            int targetIndex,
            Vector3 hitPos,
            bool isCritical,
            DamageKind kind)
        {
            if (vfxManager == null) return;

            Transform target = vfxManager.GetEnemyTransform(targetIndex);
            VfxProfile profile = context?.Profile;

            // 每个权威命中：粒子 + 闪白 + 缩放。对应的
            // DamageEvent 已由攻击表现回调立即显示；末段只补齐
            // 未能与可见攻击表现一一对应的事件。
            // 九齿钉耙由整排罡风统一表现；保留目标闪白与受击反馈，
            // 不在每个怪物位置重复发射命中粒子。
            if (profile == null ||
                profile.visualType != ProceduralRakeSweep.VisualTypeName)
                vfxManager.PlayHitEffect(hitPos, profile, isCritical, context?.EffectsCompletion);
            bool isStageBoss = IsStageBossTarget(targetIndex);
            bool isBoss = isStageBoss || combat != null && targetIndex >= 0 &&
                targetIndex < combat.Enemies.Count &&
                combat.Enemies[targetIndex] != null &&
                combat.Enemies[targetIndex].isBoss;
            bool subjectVisible = TryGetDamageAnchor(targetIndex, out _);
            if (target != null && subjectVisible)
            {
                if (isStageBoss)
                {
                    StageBossHitResponse response =
                        target.GetComponent<StageBossHitResponse>() ??
                        target.gameObject.AddComponent<StageBossHitResponse>();
                    response.Play(hitPos, isCritical);
                }
                else if (isBoss)
                {
                    BossHitResponse response =
                        target.GetComponent<BossHitResponse>() ??
                        target.gameObject.AddComponent<BossHitResponse>();
                    response.Play(isCritical);
                }
                else
                {
                    HitFlash.FlashSafe(target.gameObject);
                }
            }

            if (!isBoss && enablePunchScale && target != null && subjectVisible)
            {
                float punch = profile != null
                    ? (isCritical
                        ? profile.critPunchScaleIntensity
                        : profile.punchScaleIntensity)
                    : (isCritical ? 0.25f : 0.15f);
                if (kind == DamageKind.Bounce)
                    punch *= 0.72f;
                vfxManager.PunchScale(target, punch);
            }
        }

        private bool IsStageBossTarget(int targetIndex)
            => combat != null && targetIndex >= 0 &&
               targetIndex < combat.Enemies.Count &&
               combat.Enemies[targetIndex] != null &&
               combat.Enemies[targetIndex].bossRank == BossRank.Stage;

        // ==================== 全部投射物到达：顿帧 + 死亡检查 ====================

        private void OnAllProjectilesArrived(CardImpactContext context, bool playHitStop = true,
            bool synchronousReceipts = false)
        {
            if (context == null || !context.AwaitingProjectile) return;
            if (context.EffectsCompletion.IsCancelled)
            {
                ReleaseImpact(context);
                return;
            }
            VfxProfile profile = context?.Profile;

            // 只有超出可见攻击物数的连击/数量 SubHit 才在末发后补齐；
            // 正常投射物都已在各自抵达帧显示伤害数字。
            FlushPendingTerrainPlacements(context);
            if (synchronousReceipts)
                PresentPeriodicReceipts(context, 0, context.Hits.Count);
            else
                FlushPendingHits(context);
            if (context != null)
                _attackPresentations.MarkFlightFinished(context);
            CardAttackPresentationCompleted?.Invoke(context.CardInstanceId);

            // 最终顿帧
            if (playHitStop && enableHitStop && vfxManager != null)
            {
                bool hasCriticalHit = context != null &&
                    context.HasCriticalDirectHit;
                float stopDuration = profile != null
                    ? (hasCriticalHit
                        ? profile.critHitStopDuration
                        : profile.hitStopDuration)
                    : (hasCriticalHit ? 0.04f : 0.02f);
                vfxManager.HitStop(stopDuration);
            }

            if (context != null && context.CoreResolved)
            {
                FinishProjectileImpact(context);
            }
        }

        private bool TryFindNextDirectHit(
            CardImpactContext context,
            int targetIndex,
            out int pendingHitIndex,
            out DamageEvent damageEvent)
        {
            return _attackPresentations.TryFindNextDirectHit(
                context, targetIndex, out pendingHitIndex, out damageEvent);
        }

        /// <summary>
        /// 在对应投射物的抵达帧消费一个权威伤害事件。索引集保证
        /// 同一事件不会在末发补齐时再显示一次。
        /// </summary>
        private bool PresentPendingHit(
            CardImpactContext context,
            int pendingHitIndex)
        {
            if (!_attackPresentations.TryPresentHit(
                    context, pendingHitIndex, out PendingHit pending,
                    out int sequenceOrdinal))
                return false;
            ShowDamageEvent(
                pending.damageEvent,
                pending.worldAnchor,
                pending.hasWorldAnchor,
                sequenceOrdinal);
            PresentSplashImpact(context, pending);
            return true;
        }

        private void PresentSplashImpact(
            CardImpactContext context,
            PendingHit pending)
        {
            DamageEvent damageEvent = pending.damageEvent;
            if (damageEvent.Kind != DamageKind.Splash ||
                !pending.hasWorldAnchor)
            {
                return;
            }

            PresentAttackImpact(
                context,
                damageEvent.TargetIndex,
                pending.worldAnchor,
                damageEvent.IsCritical,
                damageEvent.Kind);
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[VFX][SplashImpact] target={damageEvent.TargetIndex} " +
                    $"anchor=({pending.worldAnchor.x:F2},{pending.worldAnchor.y:F2},{pending.worldAnchor.z:F2}) " +
                    $"applied={damageEvent.AppliedDamage}");
        }

        private void MarkFatalHitPresentation(
            CardImpactContext context,
            int pendingHitIndex)
        {
            if (context == null) return;
            EnsureDefeatPresentations().MarkHitPresented(
                context.SourcePresentationId,
                pendingHitIndex);
        }

        private void CloseDeathSource(CardImpactContext context)
        {
            if (context == null) return;
            EnsureDefeatPresentations().CloseSource(
                context.SourcePresentationId);
        }

        private void OnTerrainEffectChanged(TerrainEffectChange change)
        {
            if (vfxManager == null ||
                vfxManager.terrainEffectPresenter == null)
            {
                return;
            }

            CardImpactContext context = FindImpactContext(
                change.SourceCardInstanceId);
            if (context == null && _attackPresentations.CoreResolving != null &&
                _attackPresentations.CoreResolving.AwaitingProjectile)
                context = _attackPresentations.CoreResolving;
            bool belongsToActiveCard = context != null &&
                context.AwaitingProjectile;
            if (change.ChangeKind == TerrainEffectChangeKind.Placed &&
                belongsToActiveCard)
            {
                _attackPresentations.AttachTerrain(
                    context, change, ResolveTerrainAnchor(change.TargetIndex));
                return;
            }

            vfxManager.terrainEffectPresenter.Present(
                change,
                ResolveTerrainAnchor(change.TargetIndex));
        }

        private void PresentNextTerrainPlacement(
            CardImpactContext context,
            int targetIndex)
        {
            if (vfxManager == null ||
                vfxManager.terrainEffectPresenter == null)
            {
                return;
            }

            if (_attackPresentations.TryTakeTerrain(
                    context, targetIndex, out PendingTerrainPlacement pending))
                vfxManager.terrainEffectPresenter.Present(
                    pending.change, pending.worldAnchor);
        }

        private void FlushPendingTerrainPlacements(CardImpactContext context)
        {
            IReadOnlyList<PendingTerrainPlacement> placements =
                _attackPresentations.DrainTerrain(context);
            if (vfxManager == null ||
                vfxManager.terrainEffectPresenter == null)
            {
                return;
            }

            for (int i = 0; i < placements.Count; i++)
            {
                PendingTerrainPlacement pending = placements[i];
                vfxManager.terrainEffectPresenter.Present(
                    pending.change,
                    pending.worldAnchor);
            }
        }

        private Vector3 ResolveTerrainAnchor(int targetIndex)
        {
            Vector3? ground = _enemyGroundAnchor?.Invoke(targetIndex);
            if (ground.HasValue) return ground.Value;
            Transform target = vfxManager != null
                ? vfxManager.GetEnemyTransform(targetIndex)
                : null;
            if (target == null) return Vector3.zero;

            SpriteRenderer renderer = target.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                Bounds bounds = renderer.bounds;
                return new Vector3(
                    bounds.center.x,
                    bounds.min.y + 0.04f,
                    target.position.z);
            }
            return target.position + Vector3.down * 0.48f;
        }

        private void BeginPresentationIfNeeded()
        {
            if (_presentationActive) return;
            _presentationActive = true;
            PresentationStarted?.Invoke();
        }

        private void DispatchExperiencePresentation(
            CombatExperienceAward award,
            Vector3 worldOrigin,
            System.Action onComplete)
        {
            var handler = ExperienceAwardPresentationRequested;
            if (handler != null)
            {
                handler.Invoke(award, worldOrigin, onComplete);
                return;
            }

            Debug.LogError(
                $"[VFX] Deferred experience award {award.AwardId} " +
                "has no Bootstrap presentation coordinator.");
            onComplete?.Invoke();
        }

        private void CompletePresentation()
        {
            if (!_presentationActive) return;
            _presentationActive = false;
            PresentationCompleted?.Invoke();
        }

        private CardImpactContext FindImpactContext(int cardInstanceId)
        {
            return _attackPresentations.Find(cardInstanceId);
        }

        private void FinishProjectileImpact(CardImpactContext context)
        {
            if (context == null) return;
            ReleaseImpact(context);
        }

        private void ReleaseImpact(CardImpactContext context)
        {
            bool cancelled = context != null && context.AwaitingProjectile;
            if (!_attackPresentations.Release(context)) return;
            if (cancelled) CardAttackPresentationCancelled?.Invoke(context.CardInstanceId);
            CloseDeathSource(context);
            TryCompletePresentation();
        }

        private void TryCompletePresentation()
        {
            if ((_defeatPresentations != null &&
                 _defeatPresentations.IsBusy) ||
                _attackPresentations.ActiveCount > 0)
            {
                return;
            }

            CompletePresentation();
        }

        private void FlushPendingHits(CardImpactContext context)
        {
            IReadOnlyList<CombatAttackPresentationQueue.HitSequence> sequences =
                _attackPresentations.DrainRemainingHits(context);
            for (int i = 0; i < sequences.Count; i++)
            {
                // Acquire before StartCoroutine: even a synchronous first
                // receipt or an observer re-entry must remain inside this source.
                System.IDisposable work = context.EffectsCompletion.HoldWork();
                StartCoroutine(ShowPendingHitSequence(
                    context, sequences[i].Hits, sequences[i].StartingOrdinal, work));
            }
        }

        private System.Collections.IEnumerator ShowPendingHitSequence(
            CardImpactContext context,
            IReadOnlyList<PendingHit> sequence,
            int startingOrdinal,
            System.IDisposable work)
        {
            try
            {
                if (sequence.Count == 0) yield break;

                // 一个权威 DamageEvent 对应一个浮字。短间隔只负责错开同一目标上的
                // 多个数字，不得改变事件数量、同一目标内的事件顺序或单次伤害值。
                for (int i = 0; i < sequence.Count; i++)
                {
                    if (context.EffectsCompletion.IsCancelled) yield break;
                    PendingHit pending = sequence[i];
                    ShowDamageEvent(
                        pending.damageEvent,
                        pending.worldAnchor,
                        pending.hasWorldAnchor,
                        startingOrdinal + i);
                    PresentSplashImpact(context, pending);

                    if (i >= sequence.Count - 1) continue;

                    float elapsed = 0f;
                    while (elapsed < DamageNumberStagger)
                    {
                        if (context.EffectsCompletion.IsCancelled) yield break;
                        elapsed += Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
            }
            finally { work?.Dispose(); }
        }

        private void ShowDamageEvent(
            DamageEvent damageEvent,
            Vector3 recordedAnchor,
            bool hasRecordedAnchor,
            int sequenceOrdinal = 0)
        {
            if (damageEvent.TargetIndex < 0 || vfxManager == null)
                return;
            // A delayed number may outlive its subject. Keep its captured
            // visible anchor, but never fall back to a hidden staging transform.
            if (!hasRecordedAnchor)
            {
                if (CombatLogger.EnableVerboseDiagnostics)
                    Debug.Log(
                        $"[DamageTrace][VFX:SkippedHidden] " +
                        $"runtime={damageEvent.TargetEnemyRuntimeId} " +
                        $"index={damageEvent.TargetIndex} " +
                        $"applied={damageEvent.AppliedDamage}");
                return;
            }
            Vector3 anchor = recordedAnchor;
            if (CombatLogger.EnableVerboseDiagnostics)
                Debug.Log(
                    $"[DamageTrace][VFX:Display] " +
                    $"runtime={damageEvent.TargetEnemyRuntimeId} " +
                    $"index={damageEvent.TargetIndex} " +
                    $"applied={damageEvent.AppliedDamage} " +
                    $"anchor=({anchor.x:F2},{anchor.y:F2},{anchor.z:F2})");
            vfxManager.ShowDamageNumber(
                anchor,
                damageEvent,
                sequenceOrdinal);
        }

        private bool TryGetDamageAnchor(
            int targetIndex,
            out Vector3 worldAnchor)
        {
            worldAnchor = default;
            if (vfxManager == null || targetIndex < 0)
                return false;
            Transform target = vfxManager.GetEnemyTransform(targetIndex);
            if (target == null) return false;
            SpriteRenderer renderer = target.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null ||
                !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer.color.a <= 0f)
                return false;
            worldAnchor = renderer.bounds.center;
            return true;
        }
    }
}
