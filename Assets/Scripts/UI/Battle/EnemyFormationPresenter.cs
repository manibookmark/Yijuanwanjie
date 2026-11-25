using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.UI.Battle;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 敌人编队表现的唯一时序所有者。
    /// 负责死亡占位、补位、严格三排可见性与击退/前进；
    /// 不负责状态栏、Tooltip 或素材选择。
    /// 2026-08-24 模块审查：文件虽达到 1200 行预警，但所有可变状态仍只
    /// 服务 FormationRoot 的位置/可见性生命周期；几何依赖图已下沉到
    /// EnemyFormationTransitionScheduler，本次没有引入第二类知识所有权。
    /// 若再增加独立视觉通道或第二套协程策略，必须先重新拆分审查。
    /// 2026-09-29：保留完整阵型状态机，不按协程阶段拆分。已呈现占格由
    /// 本类唯一写入，与 Core 提前结算的位置分离；Actor 统一拥有渲染开关。
    /// 此次只修复该生命周期，不新增独立职责；新增运动通道时重新审查拆分。
    /// 批死亡接入只原子登记既有占位，再推进原依赖图；不增加全局冻结状态，
    /// 腾空仍消费真正命中后的原通知，不让权威死亡预登记直接启动退场。
    /// 击退沿用同一依赖图；只消费 Core 冻结的逐击移动与 Bootstrap 注入的
    /// 本源完成查询，不持有攻击效果、全局暂停或第二套位移生命周期。
    /// </summary>
    internal sealed class EnemyFormationPresenter : MonoBehaviour
    {
        private const float BackfillDuration = 0.22f;
        private const float PaperEntranceDuration = 0.16f;
        private const float PaperEntranceStagger = 0.02f;
        private readonly HashSet<int> _reservedEntranceIndices =
            new HashSet<int>();
        private readonly HashSet<int> _initialEntranceIndices =
            new HashSet<int>();
        private bool _initialEntrancesStarted;
        private readonly Dictionary<int, Coroutine> _activeFormationRoutines =
            new Dictionary<int, Coroutine>();
        private readonly EnemyFormationTransitionScheduler
            _transitionScheduler =
                new EnemyFormationTransitionScheduler();
        private readonly Dictionary<long, Coroutine>
            _transitionCompletionRoutines =
                new Dictionary<long, Coroutine>();
        private readonly Dictionary<int, long> _transitionByFormationIndex =
            new Dictionary<int, long>();
        // Runtime identity -> last reached visual footprint. Core may already
        // be several tickets ahead; a refresh must never grant entry from it.
        private readonly Dictionary<int, FormationFootprint> _presentedFootprints =
            new Dictionary<int, FormationFootprint>();

        private IReadOnlyList<EnemyActorView> _actors;
        private EnemyAreaPresentationProfile _presentation;
        private CombatController _combat;
        private EnemyActionPresenter _actionPresenter;
        public event Action<EnemyActionPresentationSignal> ActionPresentationSignaled;

        private void ForwardActionSignal(EnemyActionPresentationSignal signal)
            => ActionPresentationSignaled?.Invoke(signal);
        private Action _refresh;
        private Action _markTooltipDirty;
        private Func<int, Func<bool>> _attackEffectCompletionResolver;
        private int _presentationDeferralDepth;
        private bool _drainingReadyTransitions;

        public bool IsPresentationDeferred =>
            _presentationDeferralDepth > 0;
        public bool IsEnemyActionPresentationActive =>
            _actionPresenter != null && _actionPresenter.IsActive;
        public bool IsFormationPresentationSettled =>
            _transitionScheduler.IsSettled &&
            _activeFormationRoutines.Count == 0 &&
            _initialEntranceIndices.Count == 0;
        public int EntryFadeCount => 0;
        public int DefeatHoldCount =>
            _transitionScheduler.DeathHoldCount;

        public void Bind(
            CombatController combat,
            EnemyAreaPresentationProfile presentation,
            IReadOnlyList<EnemyActorView> actors,
            Action refresh,
            Action markTooltipDirty)
        {
            Unbind();
            _combat = combat;
            _presentation = presentation;
            _actors = actors;
            _refresh = refresh;
            _markTooltipDirty = markTooltipDirty;
            if (_actionPresenter == null)
            {
                _actionPresenter = GetComponent<EnemyActionPresenter>();
                if (_actionPresenter == null)
                    _actionPresenter = gameObject.AddComponent<EnemyActionPresenter>();
            }
            _actionPresenter.ActionPresentationSignaled -= ForwardActionSignal;
            _actionPresenter.ActionPresentationSignaled += ForwardActionSignal;
            _actionPresenter.Bind(_presentation, AcquireActionRenderer);
            if (_combat == null) return;

            _combat.OnStateChanged += OnCombatStateChanged;
            _combat.OnControlEffectResolved += OnControlEffectResolved;
            _combat.OnEnemyDefeatsPrepared += OnEnemyDefeatsPrepared;
            _combat.OnEnemyDefeated += OnEnemyDefeated;
            _combat.OnFormationAdvanced += OnFormationAdvanced;
        }

        public void Unbind()
        {
            if (_combat != null)
            {
                _combat.OnStateChanged -= OnCombatStateChanged;
                _combat.OnControlEffectResolved -= OnControlEffectResolved;
                _combat.OnEnemyDefeatsPrepared -= OnEnemyDefeatsPrepared;
                _combat.OnEnemyDefeated -= OnEnemyDefeated;
                _combat.OnFormationAdvanced -= OnFormationAdvanced;
            }

            CancelPresentations();
            _attackEffectCompletionResolver = null;
            _actionPresenter?.Unbind();
            if (_actionPresenter != null)
                _actionPresenter.ActionPresentationSignaled -= ForwardActionSignal;
            _presentationDeferralDepth = 0;
            _reservedEntranceIndices.Clear();
            _initialEntranceIndices.Clear();
            _initialEntrancesStarted = false;
            _presentedFootprints.Clear();
            _combat = null;
            _presentation = null;
            _actors = null;
            _refresh = null;
            _markTooltipDirty = null;
        }

        public void InitializeActors()
        {
            CancelFormationRoutines();
            StopAllCoroutines();
            _initialEntranceIndices.Clear();
            _initialEntrancesStarted = false;
            if (_combat == null || _presentation == null || _actors == null)
                return;

            int count = Mathf.Min(_combat.Enemies.Count, _actors.Count);
            for (int i = 0; i < count; i++)
            {
                EnemyStats enemy = _combat.Enemies[i];
                EnemyActorView actor = _actors[i];
                SpriteRenderer renderer = actor?.Renderer;
                if (enemy == null || renderer == null) continue;

                int column = enemy.column >= 0
                    ? enemy.column
                    : i % _presentation.Columns;
                SetEnemyPosition(actor, enemy, column);
                _presentedFootprints[enemy.runtimeId] = FootprintOf(enemy);

                if (!enemy.IsDeployed)
                {
                    actor.HideForState();
                    continue;
                }

                bool fullyEntered =
                    EnemyFormationLayout.IsFullyInsideVisibleRows(enemy);
                bool visible = !enemy.IsDead && fullyEntered;
                actor.SetPresentationVisibility(visible);
            }

            LogFormationSnapshot();
        }

        /// <summary>
        /// 在战斗层已经真正可见后播放普通小怪的纸片入场。预载阶段不启动，
        /// 避免动画在隐藏战斗场景里提前结束，玩家只看到静态结果。
        /// </summary>
        public void PlayInitialEnemyEntrances()
        {
            if (_initialEntrancesStarted || !gameObject.activeInHierarchy ||
                _combat == null || _actors == null)
                return;

            _initialEntrancesStarted = true;
            int entranceOrder = 0;
            int count = Mathf.Min(_combat.Enemies.Count, _actors.Count);
            for (int i = 0; i < count; i++)
            {
                EnemyStats enemy = _combat.Enemies[i];
                EnemyActorView actor = _actors[i];
                if (enemy == null || actor == null || enemy.IsDead ||
                    !enemy.IsDeployed || !IsOrdinaryEnemy(enemy) ||
                    !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy))
                    continue;

                _initialEntranceIndices.Add(i);
                actor.SetPaperEntrancePose(0f, EntranceTwistSign(i));
                StartCoroutine(PlayInitialPaperEntrance(
                    i,
                    enemy.runtimeId,
                    Mathf.Min(0.18f, entranceOrder * PaperEntranceStagger)));
                entranceOrder++;
            }
        }

        private void LogFormationSnapshot()
        {
            if (_combat?.Enemies == null || _actors == null) return;
            int count = Mathf.Min(_combat.Enemies.Count, _actors.Count);
            var snapshot = new StringBuilder();
            snapshot.Append($"[Diag][Formation] count={count} " +
                            $"visibleRows={_presentation.VisibleRows} " +
                            $"thirdRowOccupied={HasEnemyAtRow(_presentation.VisibleRows - 1)}");
            for (int i = 0; i < count; i++)
            {
                EnemyStats enemy = _combat.Enemies[i];
                SpriteRenderer renderer = _actors[i]?.Renderer;
                if (enemy == null || renderer == null) continue;
                snapshot.Append($" [{i}]r{enemy.row}c{enemy.column}" +
                                $"w{enemy.spawnWaveIndex}" +
                                (renderer.gameObject.activeSelf
                                    ? string.Empty
                                    : "(hid)"));
            }
            Debug.Log(snapshot.ToString());
        }

        public void Tick()
        {
            TryStartReadyTransitions();
        }

        public void BindAttackEffectCompletionResolver(Func<int, Func<bool>> resolver)
            => _attackEffectCompletionResolver = resolver;

        public void ReleaseDefeatHold(int enemyRuntimeId)
        {
            if (!_transitionScheduler.ReleaseDeath(enemyRuntimeId))
                return;

            int defeatedIndex = FindEnemyIndexByRuntimeId(enemyRuntimeId);
            if (defeatedIndex >= 0)
                HideEnemyVisualForState(defeatedIndex);

            TryStartReadyTransitions();
            _markTooltipDirty?.Invoke();
            _refresh?.Invoke();
        }

        public bool PlayAttackMotion(
            int enemyIndex,
            Action onImpact,
            Action onComplete)
        {
            return PlayAttackMotion(
                enemyIndex,
                null,
                onImpact,
                onComplete);
        }

        public bool PlayAttackMotion(
            int enemyIndex,
            string actionId,
            Action onImpact,
            Action onComplete,
            Action onInterrupted = null)
        {
            if (_actionPresenter != null)
            {
                return _actionPresenter.PlayAttackMotion(
                    enemyIndex,
                    actionId,
                    onImpact,
                    onComplete,
                    onInterrupted);
            }

            onImpact?.Invoke();
            onComplete?.Invoke();
            return false;
        }

        public bool PlayUtilityMotion(int enemyIndex, Action onImpact, Action onComplete)
            => PlayUtilityMotion(enemyIndex, null, onImpact, onComplete);

        public bool PlayUtilityMotion(int enemyIndex, string actionId,
            Action onImpact, Action onComplete, Action onInterrupted = null)
        {
            if (_actionPresenter != null)
                return _actionPresenter.PlayUtilityMotion(enemyIndex, actionId,
                    onImpact, onComplete, onInterrupted);
            onImpact?.Invoke();
            onComplete?.Invoke();
            return false;
        }

        public void BeginCombatPresentation()
        {
            _presentationDeferralDepth++;
        }

        public void EndCombatPresentation()
        {
            if (_presentationDeferralDepth <= 0) return;
            _presentationDeferralDepth--;
            if (_presentationDeferralDepth > 0) return;

            TryStartReadyTransitions();
            _refresh?.Invoke();
        }

        public void RefreshActors()
        {
            if (_combat == null || _actors == null) return;
            int count = Mathf.Min(_combat.Enemies.Count, _actors.Count);
            for (int i = 0; i < count; i++)
            {
                EnemyStats enemy = _combat.Enemies[i];
                EnemyActorView actor = _actors[i];
                SpriteRenderer renderer = actor?.Renderer;
                if (renderer == null) continue;

                if (_reservedEntranceIndices.Contains(i))
                    continue;
                if (_initialEntranceIndices.Contains(i))
                    continue;

                if (enemy == null || !enemy.IsDeployed)
                {
                    HideEnemyVisualForState(i);
                    continue;
                }
                if (enemy.IsDead)
                {
                    if (_transitionScheduler.IsDeathHeld(enemy.runtimeId))
                        continue;
                    HideEnemyVisualForState(i);
                    continue;
                }

                // A running move owns visibility until arrival. A waiting
                // ticket keeps its last presented footprint, not Core's row.
                if (_activeFormationRoutines.ContainsKey(i))
                {
                    actor.HideDamagePreview();
                    continue;
                }
                bool inVisibleRow =
                    _presentedFootprints.TryGetValue(enemy.runtimeId, out var footprint) &&
                    IsVisibleFootprint(footprint);
                if (!inVisibleRow)
                {
                    actor.HideForState();
                    continue;
                }

                actor.SetPresentationVisibility(true);
                actor.RefreshDamagePreviewPosition();
            }
        }

        /// <summary>
        /// 为一次特殊入场保留角色表现。保留期内阵型刷新不再写该角色的
        /// FormationRoot/alpha/enabled；调用方完成或取消低层特效后必须释放。
        /// 入场运动位于独立子根，因此即使保留期结束也不存在 Transform 抢写。
        /// </summary>
        public bool ReserveEnemyEntrance(int enemyIndex)
        {
            if (_combat == null || _presentation == null || _actors == null ||
                enemyIndex < 0 || enemyIndex >= _combat.Enemies.Count ||
                enemyIndex >= _actors.Count)
            {
                return false;
            }

            EnemyStats enemy = _combat.Enemies[enemyIndex];
            EnemyActorView actor = _actors[enemyIndex];
            SpriteRenderer renderer = actor?.Renderer;
            if (enemy == null || enemy.IsDead || !enemy.IsDeployed ||
                !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy) ||
                renderer == null)
            {
                return false;
            }

            StopFormationRoutine(enemyIndex);
            _reservedEntranceIndices.Add(enemyIndex);
            renderer.GetComponent<BossSequenceAnimator>()?
                .ShowPreparedIdleFrame();
            int column = enemy.column >= 0
                ? enemy.column
                : enemyIndex % _presentation.Columns;
            SetEnemyPosition(actor, enemy, column);
            _presentedFootprints[enemy.runtimeId] = FootprintOf(enemy);
            // Reservation means the entrance animation is now known, not that
            // the actor is allowed to render. Keep the target alive for the
            // prepared animation, but fully transparent until Release... is
            // called by the entrance coordinator's completion callback.
            actor.SetPresentationVisibility(true, 0f);
            actor.RefreshDamagePreviewPosition();
            _markTooltipDirty?.Invoke();
            return true;
        }

        public bool IsEnemyEntranceReserved(int enemyIndex)
            => _reservedEntranceIndices.Contains(enemyIndex);

        public void RefreshEnemyGeometry(int enemyIndex)
        {
            if (_combat == null || _actors == null ||
                enemyIndex < 0 || enemyIndex >= _combat.Enemies.Count ||
                enemyIndex >= _actors.Count)
            {
                return;
            }

            EnemyStats enemy = _combat.Enemies[enemyIndex];
            EnemyActorView actor = _actors[enemyIndex];
            if (enemy == null || actor?.Renderer == null)
                return;

            // Sprite geometry changes must not teleport a queued actor to
            // Core's future position or overwrite a running movement.
            if (_activeFormationRoutines.ContainsKey(enemyIndex)) return;
            if (!_presentedFootprints.TryGetValue(enemy.runtimeId, out var footprint)) return;
            ComputePlacementAtRow(footprint.Row, enemy, footprint.Column,
                actor.Renderer.sprite, actor.Visual, out var position,
                out var scale, out int sortingOrder, footprint);
            actor.FormationTransform.localPosition = position;
            actor.FormationTransform.localScale = scale;
            actor.SetSortingOrder(sortingOrder);
            actor.RefreshDamagePreviewPosition();
        }

        public void ReleaseEnemyEntrance(int enemyIndex)
        {
            if (!_reservedEntranceIndices.Remove(enemyIndex)) return;
            _markTooltipDirty?.Invoke();
            _refresh?.Invoke();

            // 待命期间 actor GameObject 会被禁用，BossSequenceAnimator 因而
            // 停止自身协程与待机音。特殊入场释放后在 UI 模块内部恢复待机，
            // 不把具体动画器泄漏给跨模块协调器。
            EnemyActorView actor = GetActor(enemyIndex);
            BossSequenceAnimator animator = actor?.Renderer != null
                ? actor.Renderer.GetComponent<BossSequenceAnimator>()
                : null;
            animator?.EnterIdle();
        }

        public void CancelPresentations()
        {
            CancelFormationRoutines();
            StopAllCoroutines();
            _transitionCompletionRoutines.Clear();
            _transitionByFormationIndex.Clear();
            _initialEntranceIndices.Clear();
            _initialEntrancesStarted = false;
            _transitionScheduler.Reset();
            _drainingReadyTransitions = false;
            _presentationDeferralDepth = 0;
            _actionPresenter?.CancelPresentations();
            ResetPaperEntrancePoses();
            StabilizeActors();
        }

        private static bool IsOrdinaryEnemy(EnemyStats enemy)
            => enemy != null && !enemy.isBoss &&
               enemy.bossRank == BossRank.None;

        private static float EntranceTwistSign(int enemyIndex)
            => enemyIndex % 2 == 0 ? 1f : -1f;

        private IEnumerator PlayInitialPaperEntrance(
            int enemyIndex,
            int runtimeId,
            float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);

            EnemyActorView actor = GetActor(enemyIndex);
            if (actor == null || !CanPresentEnemy(enemyIndex, runtimeId))
            {
                _initialEntranceIndices.Remove(enemyIndex);
                yield break;
            }

            actor.SetPresentationVisibility(true);
            actor.SetPaperEntrancePose(0f, EntranceTwistSign(enemyIndex));
            actor.PlayEntryParticles();
            float elapsed = 0f;
            while (elapsed < PaperEntranceDuration)
            {
                if (!CanPresentEnemy(enemyIndex, runtimeId))
                {
                    actor.HideForState();
                    actor.ResetPaperEntrancePose();
                    _initialEntranceIndices.Remove(enemyIndex);
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                actor.SetPaperEntrancePose(
                    elapsed / PaperEntranceDuration,
                    EntranceTwistSign(enemyIndex));
                yield return null;
            }

            actor.ResetPaperEntrancePose();
            _initialEntranceIndices.Remove(enemyIndex);
            _markTooltipDirty?.Invoke();
        }

        private void ResetPaperEntrancePoses()
        {
            if (_actors == null) return;
            for (int i = 0; i < _actors.Count; i++)
                _actors[i]?.ResetPaperEntrancePose();
        }

        private void OnCombatStateChanged(CombatStateChangeReason reason)
        {
            _markTooltipDirty?.Invoke();
            if (IsPresentationDeferred) return;
            _refresh?.Invoke();
        }

        private void OnControlEffectResolved(ControlEffectResult control)
        {
            if (control.EffectType != OnHitEffectType.Knockback ||
                control.Moves == null || control.Moves.Count == 0 || _combat == null) return;
            _markTooltipDirty?.Invoke();
            // Capture the exact source now. Looking it up again after a later
            // replay of this card would accidentally wait for another cast.
            Func<bool> attackEffectCompleted =
                _attackEffectCompletionResolver?.Invoke(control.SourceCardInstanceId);
            _transitionScheduler.EnqueueKnockback(control.Moves, attackEffectCompleted);
            TryStartReadyTransitions();
            _refresh?.Invoke();
        }

        private void OnEnemyDefeated(EnemyDefeatResult defeat)
        {
            RegisterDefeatedVisual(defeat);
            TryStartReadyTransitions();
        }

        private void OnEnemyDefeatsPrepared(IReadOnlyList<EnemyDefeatResult> defeats)
        {
            // Core has already written every death. Register visible holds atomically
            // before hidden deaths can release a ready transition and refresh actors.
            for (int i = 0; i < defeats.Count; i++)
                RegisterDefeatedVisual(defeats[i]);
            TryStartReadyTransitions();
        }

        private void RegisterDefeatedVisual(EnemyDefeatResult defeat)
        {
            if (defeat.WasImmune || _combat?.Enemies == null || _actors == null)
                return;

            EnemyStats defeatedEnemy = null;
            int defeatedIndex = defeat.TargetIndex;
            if (defeat.TargetEnemyRuntimeId != 0)
            {
                for (int i = 0; i < _combat.Enemies.Count; i++)
                {
                    if (_combat.Enemies[i] != null &&
                        _combat.Enemies[i].runtimeId == defeat.TargetEnemyRuntimeId)
                    {
                        defeatedEnemy = _combat.Enemies[i];
                        defeatedIndex = i;
                        break;
                    }
                }
            }
            else if (defeatedIndex >= 0 &&
                     defeatedIndex < _combat.Enemies.Count)
            {
                defeatedEnemy = _combat.Enemies[defeatedIndex];
            }

            if (defeatedEnemy == null ||
                !defeatedEnemy.IsDead ||
                defeatedIndex < 0 ||
                defeatedIndex >= _actors.Count ||
                _actors[defeatedIndex]?.Renderer == null)
            {
                return;
            }

            if (defeat.TargetEnemyRuntimeId == 0)
            {
                HideEnemyVisualForState(defeatedIndex);
                return;
            }

            // 已被击退出场的怪物仍可因既有状态伤害死亡；不能为播放死亡
            // 把隐藏主体重新打开。没有在场主体需要等待腾空，直接释放占位。
            if (!_actors[defeatedIndex].IsPresentationVisible ||
                !_presentedFootprints.TryGetValue(defeatedEnemy.runtimeId, out var deathFootprint) ||
                !IsVisibleFootprint(deathFootprint))
            {
                HideEnemyVisualForState(defeatedIndex);
                _transitionScheduler.ReleaseDeath(defeat.TargetEnemyRuntimeId);
                return;
            }

            deathFootprint = _transitionScheduler.GetDeathFootprint(
                defeat.TargetEnemyRuntimeId, deathFootprint);
            if (!_transitionScheduler.RegisterDeath(
                    defeat.TargetEnemyRuntimeId,
                    deathFootprint))
            {
                HideEnemyVisualForState(defeatedIndex);
                return;
            }

            PreserveDefeatedVisualUntilVacated(defeatedIndex);
        }

        private void PreserveDefeatedVisualUntilVacated(int enemyIndex)
        {
            StopFormationRoutine(enemyIndex);

            // Never resurrect an unseen actor just to create a death proxy.
            EnemyActorView actor = GetActor(enemyIndex);
            if (actor == null || !actor.IsPresentationVisible) return;
            actor.SetPresentationVisibility(true);
        }

        private void OnFormationAdvanced(FormationAdvanceEvent advance)
        {
            if (advance == null) return;
            _transitionScheduler.Enqueue(advance);
            _markTooltipDirty?.Invoke();
            TryStartReadyTransitions();
        }

        private void TryStartReadyTransitions()
        {
            if (_drainingReadyTransitions ||
                IsPresentationDeferred)
            {
                return;
            }

            _drainingReadyTransitions = true;
            try
            {
                while (true)
                {
                    IReadOnlyList<EnemyFormationTransition> ready =
                        _transitionScheduler.TakeReady();
                    if (ready.Count == 0)
                        break;

                    for (int i = 0; i < ready.Count; i++)
                        StartFormationTransition(ready[i]);
                }
            }
            finally
            {
                _drainingReadyTransitions = false;
            }
        }

        private void StartFormationTransition(
            EnemyFormationTransition transition)
        {
            for (int i = 0; i < transition.Moves.Count; i++)
            {
                int formationIndex =
                    transition.Moves[i].FormationIndex;
                _transitionByFormationIndex[formationIndex] =
                    transition.TransitionId;
            }

            for (int i = 0; i < transition.Moves.Count; i++)
            {
                FormationAdvanceMove move = transition.Moves[i];
                ApplyFormationMovement(new[] { move.FormationIndex },
                    transition.AdvanceId == 0 ? 0.3f : BackfillDuration,
                    move.ToFootprint, move.RuntimeId);
            }
            _refresh?.Invoke();

            if (!gameObject.activeInHierarchy ||
                !HasActiveRoutineForTransition(transition.TransitionId))
            {
                CompleteFormationTransition(transition.TransitionId);
                return;
            }

            _transitionCompletionRoutines[transition.TransitionId] =
                StartCoroutine(WaitForFormationTransition(
                    transition.TransitionId));
        }

        private IEnumerator WaitForFormationTransition(long transitionId)
        {
            while (HasActiveRoutineForTransition(transitionId))
                yield return null;

            _transitionCompletionRoutines.Remove(transitionId);
            CompleteFormationTransition(transitionId);
        }

        private void CompleteFormationTransition(long transitionId)
        {
            _transitionCompletionRoutines.Remove(transitionId);
            var completedIndices = new List<int>();
            foreach (KeyValuePair<int, long> pair in
                     _transitionByFormationIndex)
            {
                if (pair.Value == transitionId)
                    completedIndices.Add(pair.Key);
            }
            for (int i = 0; i < completedIndices.Count; i++)
                _transitionByFormationIndex.Remove(completedIndices[i]);

            if (!_transitionScheduler.Complete(transitionId))
                return;

            _markTooltipDirty?.Invoke();
            _refresh?.Invoke();
            TryStartReadyTransitions();
        }

        private bool HasActiveRoutineForTransition(long transitionId)
        {
            foreach (KeyValuePair<int, long> pair in
                     _transitionByFormationIndex)
            {
                if (pair.Value == transitionId &&
                    _activeFormationRoutines.ContainsKey(pair.Key))
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyFormationMovement(
            IEnumerable<int> indices,
            float duration,
            FormationFootprint? destination = null,
            int expectedRuntimeId = 0)
        {
            if (_combat == null || _presentation == null || _actors == null)
                return;

            foreach (int i in indices)
            {
                if (i < 0 || i >= _actors.Count ||
                    i >= _combat.Enemies.Count)
                {
                    continue;
                }

                EnemyActorView actor = _actors[i];
                SpriteRenderer renderer = actor?.Renderer;
                if (renderer == null) continue;
                EnemyStats enemy = _combat.Enemies[i];
                if (expectedRuntimeId != 0 && enemy?.runtimeId != expectedRuntimeId) continue;
                if (_reservedEntranceIndices.Contains(i)) continue;
                if (enemy == null || enemy.IsDead || !enemy.IsDeployed)
                {
                    if (enemy != null && _transitionScheduler.IsDeathHeld(enemy.runtimeId)) continue;
                    HideEnemyVisualForState(i);
                    continue;
                }
                bool wasHidden = !actor.IsPresentationVisible;
                FormationFootprint targetFootprint = destination ?? FootprintOf(enemy);
                bool willBeVisible = IsVisibleFootprint(targetFootprint);

                StopFormationRoutine(i);
                if (!willBeVisible)
                {
                    actor.HideForState();
                    _presentedFootprints[enemy.runtimeId] = targetFootprint;
                    continue;
                }

                ComputePlacementAtRow(
                    targetFootprint.Row,
                    enemy,
                    targetFootprint.Column,
                    renderer.sprite,
                    actor.Visual,
                    out Vector3 targetPosition,
                    out Vector3 targetScale,
                    out int sortingOrder,
                    targetFootprint);
                // 隐藏行没有可被看见的过场位置：完整占格抵达目标格之前始终
                // 不显示，到达后才原地渐显。此语义不依赖任何云雾遮罩。
                bool fadeEntry = wasHidden;
                if (fadeEntry)
                {
                    // Hidden actors enter at their fully contained target and
                    // reveal in place.
                    actor.FormationTransform.localPosition = targetPosition;
                    actor.FormationTransform.localScale = targetScale;
                }
                actor.SetPresentationVisibility(!fadeEntry);

                if (gameObject.activeInHierarchy)
                {
                    _activeFormationRoutines[i] = StartCoroutine(
                        MoveToPosition(
                            i,
                            targetPosition,
                            targetScale,
                            sortingOrder,
                            duration,
                            fadeEntry,
                            targetFootprint,
                            enemy.runtimeId));
                }
                else
                {
                    actor.FormationTransform.localPosition = targetPosition;
                    actor.FormationTransform.localScale = targetScale;
                    actor.SetSortingOrder(sortingOrder);
                    _presentedFootprints[enemy.runtimeId] = targetFootprint;
                    actor.SetPresentationVisibility(true);
                }
            }
        }

        private bool HasEnemyAtRow(int row)
        {
            if (_combat?.Enemies == null || row < 0) return false;
            for (int i = 0; i < _combat.Enemies.Count; i++)
            {
                EnemyStats enemy = _combat.Enemies[i];
                if (enemy == null || enemy.IsDead || !enemy.IsDeployed)
                    continue;
                if (enemy.row <= row && row < enemy.row + enemy.GridDepth)
                    return true;
            }
            return false;
        }

        private SpriteRenderer AcquireActionRenderer(int enemyIndex)
        {
            if (_actors == null || enemyIndex < 0 ||
                enemyIndex >= _actors.Count || _combat == null ||
                enemyIndex >= _combat.Enemies.Count)
            {
                return null;
            }

            EnemyStats enemy = _combat.Enemies[enemyIndex];
            if (enemy == null || enemy.IsDead ||
                !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy))
            {
                return null;
            }

            EnemyActorView actor = _actors[enemyIndex];
            // Actions consume visibility; they must never grant entry.
            return actor != null && actor.IsPresentationVisible
                ? actor.Renderer : null;
        }
        private IEnumerator MoveToPosition(
            int enemyIndex,
            Vector3 localPosition,
            Vector3 localScale,
            int sortingOrder,
            float duration,
            bool fadeEntry,
            FormationFootprint destination,
            int runtimeId)
        {
            EnemyActorView actor = GetActor(enemyIndex);
            Transform target = actor?.FormationTransform;
            SpriteRenderer renderer = actor?.Renderer;
            if (target == null) yield break;
            if (!CanPresentEnemy(enemyIndex, runtimeId))
            {
                if (renderer != null)
                {
                    actor.HideForState();
                }
                _activeFormationRoutines.Remove(enemyIndex);
                yield break;
            }

            if (renderer != null)
            {
                // A newly entering actor must not be visible while its
                // formation root is still travelling.  The previous fade-in
                // rendered the sprite at alpha 0 but still exposed its bounds
                // to sorting/hover and made large monsters appear detached
                // above the board. Keep it inactive until the target footprint
                // is fully reached, then reveal it in one frame.
                if (fadeEntry)
                {
                    actor.HideForState();
                }
                else
                {
                    actor.SetPresentationVisibility(true);
                }
            }
            Vector3 startPosition = target.localPosition;
            Vector3 startScale = target.localScale;
            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.deltaTime;
                if (!CanPresentEnemy(enemyIndex, runtimeId))
                {
                    actor.HideForState();
                    _activeFormationRoutines.Remove(enemyIndex);
                    yield break;
                }

                float t = Mathf.Clamp01(timer / duration);
                target.localPosition = Vector3.Lerp(
                    startPosition,
                    localPosition,
                    t);
                target.localScale = Vector3.Lerp(
                    startScale,
                    localScale,
                    t);
                yield return null;
            }

            target.localPosition = localPosition;
            target.localScale = localScale;
            _presentedFootprints[runtimeId] = destination;
            if (renderer != null)
            {
                actor.SetSortingOrder(sortingOrder);
            }
            _activeFormationRoutines.Remove(enemyIndex);
            if (renderer != null)
            {
                if (fadeEntry)
                {
                    yield return PlayPaperEntrance(
                        actor, enemyIndex, runtimeId);
                }
                else
                {
                    actor.SetPresentationVisibility(true);
                    actor.ResetPaperEntrancePose();
                }
            }
        }

        private IEnumerator PlayPaperEntrance(
            EnemyActorView actor,
            int enemyIndex,
            int runtimeId)
        {
            if (actor == null || !CanPresentEnemy(enemyIndex, runtimeId))
                yield break;

            actor.SetPresentationVisibility(true);
            actor.SetPaperEntrancePose(0f, EntranceTwistSign(enemyIndex));
            actor.PlayEntryParticles();
            float elapsed = 0f;
            while (elapsed < PaperEntranceDuration)
            {
                if (!CanPresentEnemy(enemyIndex, runtimeId))
                {
                    actor.HideForState();
                    actor.ResetPaperEntrancePose();
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                actor.SetPaperEntrancePose(
                    elapsed / PaperEntranceDuration,
                    EntranceTwistSign(enemyIndex));
                yield return null;
            }

            actor.ResetPaperEntrancePose();
        }

        private void SetEnemyPosition(
            EnemyActorView actor,
            EnemyStats enemy,
            int column)
        {
            if (actor?.FormationTransform == null) return;
            ComputePlacementAtRow(
                enemy != null ? enemy.row : 0,
                enemy,
                column,
                actor.Renderer != null ? actor.Renderer.sprite : null,
                actor.Visual,
                out Vector3 position,
                out Vector3 scale,
                out int sortingOrder);
            actor.FormationTransform.localPosition = position;
            actor.FormationTransform.localScale = scale;
            actor.SetSortingOrder(sortingOrder);
        }

        private void ComputePlacementAtRow(
            int row,
            EnemyStats enemy,
            int column,
            Sprite sprite,
            EnemySpriteAlignment visual,
            out Vector3 localPosition,
            out Vector3 localScale,
            out int sortingOrder,
            FormationFootprint? footprint = null)
        {
            int gridWidth = footprint?.Width ?? (enemy != null ? enemy.GridWidth : 1);
            int gridDepth = footprint?.Depth ?? (enemy != null ? enemy.GridDepth : 1);
            bool visible = row >= 0 &&
                           row + gridDepth <= _presentation.VisibleRows;
            Vector3 cellAnchor = _presentation.GetFootprintGroundAnchor(
                row,
                column,
                gridWidth);
            int visibleRow = Mathf.Clamp(
                row,
                0,
                _presentation.VisibleRows - 1);
            sortingOrder = visible
                ? _presentation.GetCellSortingOrder(visibleRow, column)
                : 0;

            if (sprite == null)
            {
                localPosition = cellAnchor;
                localScale = Vector3.one;
                return;
            }

            if (visual == null &&
                _presentation.TryGetAlignment(
                    sprite,
                    out EnemySpriteAlignment fallbackVisual))
            {
                visual = fallbackVisual;
            }

            float finalScale = _presentation.GetSpriteScale(
                sprite,
                visual,
                row,
                gridWidth,
                gridDepth,
                enemy != null ? enemy.bossRank : BossRank.None);
            Vector3 placementAnchor =
                _presentation.GetPlacementGroundAnchor(
                    cellAnchor,
                    sprite,
                    visual,
                    finalScale);
            Vector2 groundAnchor = visual != null
                ? visual.groundAnchor
                : new Vector2(0.5f, 0f);
            localScale = new Vector3(finalScale, finalScale, 1f);
            localPosition = _presentation.GetAlignedPivotPosition(
                placementAnchor,
                sprite,
                groundAnchor,
                finalScale);
        }

        private void HideEnemyVisualForState(int enemyIndex)
        {
            StopFormationRoutine(enemyIndex);
            GetActor(enemyIndex)?.HideForState();
        }

        private static FormationFootprint FootprintOf(EnemyStats enemy)
            => new FormationFootprint(enemy.row, enemy.column, enemy.GridWidth, enemy.GridDepth);

        private bool IsVisibleFootprint(FormationFootprint footprint)
            => footprint.Row >= 0 && footprint.EndRowExclusive <= _presentation.VisibleRows;

        private bool CanPresentEnemy(int enemyIndex, int runtimeId)
        {
            return _combat != null && _actors != null &&
                   enemyIndex >= 0 &&
                   enemyIndex < _combat.Enemies.Count &&
                   enemyIndex < _actors.Count &&
                   _combat.Enemies[enemyIndex] != null &&
                   !_combat.Enemies[enemyIndex].IsDead &&
                   _combat.Enemies[enemyIndex].IsDeployed &&
                   _combat.Enemies[enemyIndex].runtimeId == runtimeId;
        }

        private EnemyActorView GetActor(int enemyIndex)
        {
            if (_actors == null || enemyIndex < 0 ||
                enemyIndex >= _actors.Count)
            {
                return null;
            }
            return _actors[enemyIndex];
        }

        private int FindEnemyIndexByRuntimeId(int enemyRuntimeId)
        {
            if (enemyRuntimeId <= 0 || _combat?.Enemies == null)
                return -1;
            for (int i = 0; i < _combat.Enemies.Count; i++)
            {
                EnemyStats enemy = _combat.Enemies[i];
                if (enemy != null && enemy.runtimeId == enemyRuntimeId)
                    return i;
            }
            return -1;
        }

        private void StopFormationRoutine(int enemyIndex)
        {
            if (!_activeFormationRoutines.TryGetValue(
                    enemyIndex,
                    out Coroutine routine))
            {
                return;
            }
            if (routine != null)
                StopCoroutine(routine);
            _activeFormationRoutines.Remove(enemyIndex);
        }

        private void CancelFormationRoutines()
        {
            foreach (Coroutine routine in _activeFormationRoutines.Values)
            {
                if (routine != null)
                    StopCoroutine(routine);
            }
            _activeFormationRoutines.Clear();
        }

        private void StabilizeActors()
        {
            if (_combat == null || _presentation == null || _actors == null)
                return;

            int count = Mathf.Min(_combat.Enemies.Count, _actors.Count);
            for (int i = 0; i < count; i++)
            {
                EnemyStats enemy = _combat.Enemies[i];
                EnemyActorView actor = _actors[i];
                SpriteRenderer renderer = actor?.Renderer;
                if (_reservedEntranceIndices.Contains(i)) continue;
                if (enemy == null || renderer == null || enemy.IsDead ||
                    !enemy.IsDeployed)
                {
                    actor?.HideForState();
                    continue;
                }

                int column = enemy.column >= 0
                    ? enemy.column
                    : i % _presentation.Columns;
                SetEnemyPosition(actor, enemy, column);
                _presentedFootprints[enemy.runtimeId] = FootprintOf(enemy);

                bool visible =
                    EnemyFormationLayout.IsFullyInsideVisibleRows(enemy);
                actor.SetPresentationVisibility(visible);
                if (visible)
                    actor.RefreshDamagePreviewPosition();
                else
                    actor.HideDamagePreview();
            }
        }

        private void OnDisable()
        {
            CancelPresentations();
        }

        private void OnEnable()
        {
            StabilizeActors();
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
