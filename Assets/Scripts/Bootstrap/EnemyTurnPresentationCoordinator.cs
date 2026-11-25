using System;
using System.Collections;
using UnityEngine;
using WanjieScroll.Audio;
using WanjieScroll.Core;
using WanjieScroll.Effects;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Cross-module owner of the presented enemy turn. Core owns action order
    /// and settlement, UI owns character motion, and Bootstrap aligns audio,
    /// hit effects and input with the authoritative impact frame.
    /// One presentation owns the coroutine and input lease through progression
    /// waits; readiness only wakes that presentation, never begins another turn.
    /// Disabling releases active work, but retains binding and resume intent.
    /// Lifecycle review: subscriptions, callback identity, resume intent and input
    /// leases remain one scheduling boundary; no action queue is copied here.
    /// F7 requests the same sequence directly; a scene controller is optional
    /// and supplies an input lease only when present. No second action queue.
    /// Revisit this boundary before adding an independent effect/pause lifecycle.
    /// </summary>
    public sealed class EnemyTurnPresentationCoordinator : MonoBehaviour
    {
        private const float MovementPresentationDuration = 0.24f;
        private const float ActionGap = 0.025f;

        private CombatController _combat;
        private CombatSceneController _sceneController;
        private EnemyAreaView _enemyArea;
        private BattleHudView _battleHud;
        private VisualEffectManager _vfxManager;
        private BossActionEffectPlayer _bossActionEffectPlayer;
        private AudioManager _audioManager;
        private Func<bool> _hasPendingImpacts;
        private Presentation _presentation;
        private IDisposable _stagedActionBinding;
        private bool _subscribed;
        private bool _resumeOnEnable;
        private bool _destroying;

        // Identity also invalidates motion callbacks retained by a previous Bind.
        private sealed class Presentation
        {
            internal readonly CombatController Combat;
            internal readonly IDisposable InputLease;
            internal Coroutine Routine;
            internal bool Ready;

            internal Presentation(CombatController combat, IDisposable inputLease)
            {
                Combat = combat;
                InputLease = inputLease;
            }
        }

        public bool IsPresenting => _presentation != null;
        public int PresentedActionCount { get; private set; }
        public int PresentedAttackCount { get; private set; }
        public int PresentedBossSlashCount { get; private set; }
        public int PresentedBossActionEffectCount { get; private set; }
        public CombatEnemyActionResult LastPresentedAction { get; private set; }

        public event Action<CombatEnemyActionResult> ActionPresented;

        public void Bind(
            CombatController combat,
            CombatSceneController sceneController,
            EnemyAreaView enemyArea,
            BattleHudView battleHud,
            VisualEffectManager vfxManager,
            AudioManager audioManager,
            BossActionEffectPlayer bossActionEffectPlayer = null,
            Func<bool> hasPendingImpacts = null)
        {
            Unbind();
            _combat = combat;
            _sceneController = sceneController;
            _enemyArea = enemyArea;
            _battleHud = battleHud;
            _vfxManager = vfxManager;
            _bossActionEffectPlayer = bossActionEffectPlayer;
            _audioManager = audioManager;
            _hasPendingImpacts = hasPendingImpacts;
            _stagedActionBinding = _combat?.AcquireStagedEnemyActions();
            ActivateBinding();
        }

        public void Unbind()
        {
            RemoveSubscriptions();
            StopPresentation();
            _stagedActionBinding?.Dispose();
            _stagedActionBinding = null;
            _resumeOnEnable = false;
            _combat = null;
            _sceneController = null;
            _enemyArea = null;
            _battleHud = null;
            _vfxManager = null;
            _bossActionEffectPlayer = null;
            _audioManager = null;
            _hasPendingImpacts = null;
        }

        private void ActivateBinding()
        {
            if (_destroying || !isActiveAndEnabled || _combat == null)
                return;
            if (!_subscribed)
            {
                _subscribed = true;
                if (_sceneController != null)
                {
                    _sceneController.EndTurnRequested += OnEndTurnRequested;
                    _sceneController.PendingTurnImpactProbe = _hasPendingImpacts;
                }
                _combat.OnEnemyTurnReady += OnEnemyTurnReady;
            }
            if (_presentation != null)
                return;

            // Ready may have fired while detached. Core owns the remaining queue;
            // the local marker also remembers an interrupted new-turn draw tail.
            bool resume = _resumeOnEnable || _combat.IsEnemyTurnSequenceActive;
            _resumeOnEnable = false;
            if (resume)
                StartPresentation(BeginPresentation(), waitForActivation: true);
        }

        private void RemoveSubscriptions()
        {
            if (!_subscribed) return;
            _subscribed = false;
            if (_sceneController != null)
            {
                _sceneController.EndTurnRequested -= OnEndTurnRequested;
                if (_sceneController.PendingTurnImpactProbe == _hasPendingImpacts)
                    _sceneController.PendingTurnImpactProbe = null;
            }
            if (_combat != null)
                _combat.OnEnemyTurnReady -= OnEnemyTurnReady;
        }

        private void StopPresentation()
        {
            Presentation presentation = _presentation;
            // Explicitly invalidate callbacks and release the lease before stopping.
            // Unity is not required to dispose a stopped iterator or run its finally.
            try { FinishPresentation(presentation); }
            finally
            {
                if (presentation?.Routine != null)
                    StopCoroutine(presentation.Routine);
            }
        }

        private CommandResult OnEndTurnRequested()
            => RequestEndTurn();

        /// <summary>Starts the shared enemy-turn presentation. Core settles
        /// turn-end attacks first; enemy actions wait for their impact receipts.</summary>
        public CommandResult RequestEndTurn()
        {
            if (_destroying || !isActiveAndEnabled || !_subscribed ||
                IsPresenting || _combat == null || _hasPendingImpacts?.Invoke() == true ||
                (_sceneController != null && !_sceneController.IsCombatInputEnabled))
                return CommandResult.Fail(CommandError.InvalidPhase, "当前回合演出尚未完成");

            Presentation presentation = BeginPresentation();
            try
            {
                CommandResult result = presentation.Combat.BeginEndTurnSequence();
                if (!IsCurrentPresentation(presentation))
                    return result;
                if (!result.Success || !presentation.Combat.IsEnemyTurnSequenceActive)
                    FinishPresentation(presentation);
                else
                    StartPresentation(presentation);
                return result;
            }
            catch
            {
                FinishPresentation(presentation);
                throw;
            }
        }

        private void OnEnemyTurnReady()
        {
            if (_destroying || !isActiveAndEnabled || !_subscribed ||
                _combat == null || !_combat.IsEnemyTurnSequenceActive)
                return;

            if (_presentation != null)
            {
                _presentation.Ready = true;
                return;
            }

            StartPresentation(BeginPresentation());
        }

        private Presentation BeginPresentation()
        {
            var presentation = new Presentation(
                _combat, _sceneController?.AcquireCombatInputLock());
            _presentation = presentation;
            _sceneController?.SetCombatInputEnabled(false);
            return presentation;
        }

        private void StartPresentation(Presentation presentation, bool waitForActivation = false)
        {
            if (!IsCurrentPresentation(presentation))
            {
                FinishPresentation(presentation);
                return;
            }
            Coroutine routine = StartCoroutine(PresentEnemyTurn(presentation, waitForActivation));
            // StartCoroutine may finish or be unbound synchronously before returning.
            if (IsCurrentPresentation(presentation))
                presentation.Routine = routine;
            else if (routine != null)
                StopCoroutine(routine);
        }

        private bool IsCurrentPresentation(Presentation presentation)
            => !_destroying && isActiveAndEnabled && _subscribed &&
               ReferenceEquals(_presentation, presentation);

        private void FinishPresentation(Presentation presentation)
        {
            if (presentation == null || !ReferenceEquals(_presentation, presentation))
                return;
            // Also covers engines disposing the iterator before OnDisable reaches us.
            if (!_destroying && !isActiveAndEnabled)
                _resumeOnEnable = true;
            _presentation = null;
            // Reward feedback may have requested input while our lease held it.
            // Reconcile that request before releasing the lease, including terminal paths.
            try
            {
                if (_sceneController != null)
                    _sceneController.SetCombatInputEnabled(false);
                if (!_destroying && isActiveAndEnabled && _subscribed)
                    RestoreInputIfAllowed();
            }
            finally { presentation.InputLease?.Dispose(); }
        }

        private IEnumerator PresentEnemyTurn(Presentation presentation, bool waitForActivation)
        {
            CombatController combat = presentation.Combat;
            try
            {
                // No attack/VFX dispatch in OnEnable's native activation stack.
                if (waitForActivation)
                    yield return null;
                while (IsCurrentPresentation(presentation) &&
                       combat.IsEnemyTurnSequenceActive && !combat.State.IsBattleOver)
                {
                    // Core can resume before reward reveal closes (for example,
                    // cash-out). Arriving player missiles also finish before the
                    // next enemy action; only query the Effects owner's latch.
                    while (IsCurrentPresentation(presentation) &&
                           combat.IsEnemyTurnSequenceActive && !combat.State.IsBattleOver &&
                           (SceneTransitionRuntime.ExternalModalBarrier?.IsClear == false ||
                            _hasPendingImpacts?.Invoke() == true))
                        yield return null;
                    if (!IsCurrentPresentation(presentation) ||
                        !combat.IsEnemyTurnSequenceActive || combat.State.IsBattleOver)
                        break;

                    // Clear before querying Core: a synchronous Ready notification
                    // during preparation/settlement must not be lost.
                    presentation.Ready = false;
                    bool hasAction = combat.TryPeekNextEnemyAction(out EnemyActionCommand command);
                    if (!IsCurrentPresentation(presentation))
                        yield break;
                    if (!hasAction)
                    {
                        if (combat.CompleteEnemyTurnSequence())
                            break;

                        // No action can also mean a pending XP/reward checkpoint, not
                        // an empty turn. Keep the same owner until Core resumes or ends.
                        while (IsCurrentPresentation(presentation) &&
                               combat.IsEnemyTurnSequenceActive &&
                               !combat.State.IsBattleOver && !presentation.Ready)
                            yield return null;
                        continue;
                    }

                    bool impactResolved = false;
                    bool presentationCompleted = false;
                    bool presentationInterrupted = false;

                    Action resolveImpact = () =>
                    {
                        if (impactResolved || !IsCurrentPresentation(presentation))
                            return;
                        impactResolved = true;
                        if (!combat.TryResolveNextEnemyAction(
                                command, out CombatEnemyActionResult result) ||
                            !IsCurrentPresentation(presentation))
                            return;

                        LastPresentedAction = result;
                        PresentedActionCount++;
                        if (result.Intent.ActionType == EnemyActionType.Attack)
                        {
                            PresentedAttackCount++;
                            PresentAttackImpact(result);
                        }
                        else if (result.Intent.Type == EnemyIntentType.Defend ||
                                 (result.Intent.Type == EnemyIntentType.Compound &&
                                  result.Intent.PrimaryType == EnemyIntentType.Defend))
                        {
                            PresentDefendImpact(result);
                        }
                        ActionPresented?.Invoke(result);
                    };
                    Action completePresentation = () =>
                        presentationCompleted = true;
                    Action interruptPresentation = () => presentationInterrupted = true;

                    switch (command.Intent.ActionType)
                    {
                        case EnemyActionType.Attack:
                            if (_enemyArea == null ||
                                !_enemyArea.PlayAttackMotion(
                                    command.EnemyIndex,
                                    command.Intent.ActionId,
                                    resolveImpact,
                                    completePresentation,
                                    interruptPresentation))
                            {
                                // Missing renderers execute both callbacks inside
                                // the view fallback; these calls are idempotent.
                                resolveImpact();
                                completePresentation();
                            }
                            break;

                        case EnemyActionType.Movement:
                            resolveImpact();
                            yield return WaitUnscaled(
                                MovementPresentationDuration);
                            completePresentation();
                            break;

                        default:
                            if (_enemyArea == null ||
                                !_enemyArea.PlayUtilityMotion(
                                    command.EnemyIndex,
                                    command.Intent.ActionId,
                                    resolveImpact,
                                    completePresentation,
                                    interruptPresentation))
                            {
                                resolveImpact();
                                completePresentation();
                            }
                            break;
                    }

                    while (IsCurrentPresentation(presentation) &&
                           !combat.State.IsBattleOver && !presentationCompleted && !presentationInterrupted)
                        yield return null;

                    if (!IsCurrentPresentation(presentation))
                        yield break;
                    if (presentationInterrupted)
                    {
                        // No invented settlement/completion. Core retains an uncommitted
                        // queue head; a committed action has already been consumed.
                        do { yield return null; }
                        while (IsCurrentPresentation(presentation) &&
                               _enemyArea != null && !_enemyArea.isActiveAndEnabled);
                        continue;
                    }
                    if (!impactResolved && !combat.State.IsBattleOver)
                        resolveImpact();
                    if (!IsCurrentPresentation(presentation) || combat.State.IsBattleOver)
                        break;

                    yield return WaitUnscaled(ActionGap);
                }

                // StartTurn publishes draw events synchronously. This gate also
                // survives disable/re-enable after Core already consumed the queue:
                // wait for draw, without Complete/StartTurn or enemy impacts again.
                if (IsCurrentPresentation(presentation) &&
                    !combat.IsEnemyTurnSequenceActive && !combat.IsCombatOver())
                {
                    yield return null;
                    yield return null;
                    while (IsCurrentPresentation(presentation) &&
                           _sceneController != null &&
                           _sceneController.IsCardPresentationActive)
                        yield return null;
                }
            }
            finally
            {
                FinishPresentation(presentation);
            }
        }

        private void PresentAttackImpact(
            CombatEnemyActionResult result)
        {
            if (result.WasDisarmed || !result.DidDamagePlayer)
                return;

            // 序列帧 BOSS 攻击已由 BossAnimationClip 播放同源音频；
            // 再叠加通用 EnemyAttack 会产生双重拳/腿声。无专属音轨的
            // 普通敌人和旧 BOSS 仍沿用通用反馈。
            if (!HasDedicatedBossAttackAudio(result))
            {
                if (_audioManager != null)
                    _audioManager.PlaySfx(SfxType.EnemyAttack);
                else
                    AudioManager.PlaySfxSafe(SfxType.EnemyAttack);
            }

            _battleHud?.PlayPlayerHitFeedback(
                result.TotalPlayerDamage);
            // The active native spear close-up already contains the attack's
            // visual impact. Preserve HUD damage feedback without adding a
            // second, unrelated foreground sword slash.
            if (_enemyArea != null && _enemyArea.IsBossAttackCloseupActive(result.EnemyIndex))
                return;
            bool playedConfiguredEffect = result.EnemyIsBoss &&
                _bossActionEffectPlayer != null &&
                _bossActionEffectPlayer.TryPlay(
                    ResolveEnemyContentId(result),
                    result.Intent,
                    result.EnemyIndex);
            if (playedConfiguredEffect)
            {
                PresentedBossActionEffectCount++;
                return;
            }

            if (result.EnemyIsBoss &&
                BossActionEffectCatalog.SuppressesGenericAttackFallback(
                    ResolveEnemyContentId(result)))
            {
                return;
            }

            if (_vfxManager != null &&
                _vfxManager.playerAttackOrigin != null)
            {
                _vfxManager.PlaySmallHitEffect(
                    _vfxManager.playerAttackOrigin.position,
                    new Color(0.92f, 0.22f, 0.14f, 1f));

                if (result.EnemyIsBoss &&
                    IsLevelBossPresentation(result.EnemyIndex) &&
                    _sceneController != null &&
                    _sceneController.mainCamera != null &&
                    _vfxManager.PlayBossForegroundSlash(
                        _sceneController.mainCamera,
                        _vfxManager.playerAttackOrigin.position) != null)
                {
                    PresentedBossSlashCount++;
                }
            }
        }

        private void PresentDefendImpact(
            CombatEnemyActionResult result)
        {
            if (!result.EnemyIsBoss || _bossActionEffectPlayer == null)
                return;
            if (_bossActionEffectPlayer.TryPlay(
                    ResolveEnemyContentId(result),
                    result.Intent,
                    result.EnemyIndex))
            {
                PresentedBossActionEffectCount++;
            }
        }

        /// <summary>
        /// 全屏斩击是关卡 BOSS 专属演出；无序列帧素材的层 BOSS
        /// （如盘蛇妖）命中只走普通受击反馈。
        /// </summary>
        private bool IsLevelBossPresentation(int enemyIndex)
        {
            if (_combat == null ||
                enemyIndex < 0 ||
                enemyIndex >= _combat.Enemies.Count)
            {
                return false;
            }
            return WanjieScroll.UI.Battle.BossAnimationProfileCatalog
                .HasBossAnimation(_combat.Enemies[enemyIndex]?.contentId);
        }

        private bool HasDedicatedBossAttackAudio(
            CombatEnemyActionResult result)
        {
            if (!result.EnemyIsBoss ||
                result.Intent.ActionType != EnemyActionType.Attack)
                return false;

            if (!WanjieScroll.UI.Battle.BossAnimationProfileCatalog.TryGet(
                    ResolveEnemyContentId(result),
                    out WanjieScroll.UI.Battle.BossAnimationProfile profile))
                return false;

            WanjieScroll.UI.Battle.BossAnimationClip clip =
                profile.ResolveAttackClip(result.Intent.ActionId, out _);
            return clip != null && clip.audioClip != null;
        }

        private string ResolveEnemyContentId(
            CombatEnemyActionResult result)
        {
            if (_combat == null ||
                result.EnemyIndex < 0 ||
                result.EnemyIndex >= _combat.Enemies.Count)
                return null;
            return _combat.Enemies[result.EnemyIndex]?.contentId;
        }

        private static IEnumerator WaitUnscaled(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void RestoreInputIfAllowed()
        {
            if (_sceneController == null || _combat == null ||
                _combat.IsCombatOver() ||
                _combat.IsEnemyTurnSequenceActive ||
                _combat.State.CurrentPhase != BattlePhase.PlayerInput ||
                _sceneController.IsAwaitingRewardSelection ||
                _sceneController.IsCardPresentationActive)
                return;
            _sceneController.SetCombatInputEnabled(true);
        }

        private void OnEnable()
        {
            ActivateBinding();
        }

        private void OnDisable()
        {
            _resumeOnEnable |= _presentation != null;
            RemoveSubscriptions();
            StopPresentation();
        }

        private void OnDestroy()
        {
            _destroying = true;
            Unbind();
        }
    }
}
