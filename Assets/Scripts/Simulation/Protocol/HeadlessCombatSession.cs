using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Protocol
{
    /// <summary>
    /// Deep adapter around one authoritative CombatController. It owns the
    /// agent-facing observe/act/replay contract while Core remains the sole
    /// owner of battle rules and mutable state. Use from one thread only.
    /// Size review: copying observations and dispatching typed requests remain
    /// one protocol boundary; seal selection adds no pending-state owner here.
    /// </summary>
    public sealed class HeadlessCombatSession : IDisposable
    {
        private readonly CombatController _combat;
        private readonly Action<List<LevelUpSystem.Reward>> _rewardHandler;
        private readonly List<ReplayTrace.Step> _replaySteps =
            new List<ReplayTrace.Step>();
        private int _nextSequence = 1;
        private bool _rewardOptionsKnown;
        private IReadOnlyList<RewardOptionView> _rewardOptions =
            Array.Empty<RewardOptionView>();
        private bool _disposed;

        public HeadlessCombatSession(CombatController combat)
        {
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _rewardHandler = rewards =>
            {
                _rewardOptions = CopyRewardOptions(rewards);
                _rewardOptionsKnown = true;
            };
            _combat.OnLevelUpRewardsGenerated += _rewardHandler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _combat.OnLevelUpRewardsGenerated -= _rewardHandler;
            _disposed = true;
        }

        /// <summary>
        /// Copies the current player-visible state. Hidden wave members,
        /// random-source state, future actions, and mutable Core objects are
        /// deliberately omitted.
        /// </summary>
        public BattleObservation Observe()
        {
            ThrowIfDisposed();
            CombatState state = _combat.State;
            CombatOutcome? outcome = state.CurrentPhase switch
            {
                BattlePhase.BattleVictory => CombatOutcome.Victory,
                BattlePhase.BattleDefeat => CombatOutcome.Defeat,
                _ => null
            };

            IReadOnlyList<EnemyIntent> intents = GetVisibleIntentSource(state);
            var visibleEnemies = new List<BattleObservation.EnemyView>();
            var visibleEnemyIds = new HashSet<int>();
            for (int i = 0; i < state.Enemies.Count; i++)
            {
                EnemyStats enemy = state.Enemies[i];
                if (!IsPlayerVisible(enemy))
                    continue;

                EnemyIntent intent = i < intents.Count
                    ? intents[i]
                    : EnemyIntent.None;
                EnemyInterruptSnapshot interrupt = default;
                bool hasInterrupt = state.EnemyInterrupts != null &&
                    state.EnemyInterrupts.TryGetSnapshot(enemy.runtimeId, out interrupt);
                visibleEnemies.Add(new BattleObservation.EnemyView(
                    enemy.runtimeId,
                    enemy.contentId,
                    enemy.name,
                    enemy.shortDescription,
                    enemy.currentHealth,
                    enemy.baseHealth,
                    enemy.armor,
                    enemy.isBoss,
                    enemy.isDisarmed,
                    enemy.isOverkillTarget,
                    enemy.row,
                    enemy.column,
                    enemy.GridWidth,
                    enemy.GridDepth,
                    new BattleObservation.IntentView(
                        intent.Type,
                        intent.Amount,
                        intent.TargetCount,
                        intent.Description),
                    CopyEnemyStatuses(state, enemy.runtimeId),
                    hasInterrupt ? interrupt.MaximumCountdown : (int?)null,
                    hasInterrupt ? interrupt.CurrentCountdown : (int?)null));
                visibleEnemyIds.Add(enemy.runtimeId);
            }

            var hand = new List<BattleObservation.CardView>(state.Hand.Count);
            for (int i = 0; i < state.Hand.Count; i++)
            {
                CardConfig card = state.Hand[i];
                if (card != null)
                    hand.Add(BuildCardView(card, visibleEnemyIds));
            }

            var discardPile = new List<BattleObservation.CardIdentityView>(
                state.DiscardCount);
            for (int i = 0; i < state.DiscardPile.Count; i++)
            {
                CardConfig card = state.DiscardPile[i];
                if (card == null) continue;
                discardPile.Add(new BattleObservation.CardIdentityView(
                    card.runtimeInstanceId,
                    card.contentId,
                    card.cardName));
            }

            IReadOnlyList<BattleObservation.CrawlerView> crawlers =
                CopyCrawlers(state);
            IReadOnlyList<BattleObservation.StatusView> playerStatuses =
                CopyStatuses(state.Statuses?.PlayerStatuses);
            IReadOnlyList<BattleObservation.TerrainEffectView> terrain =
                CopyTerrain(state);
            BattleObservation.BossTimerView bossTimer =
                _combat.IsBossTimerActive && state.BossTimer != null
                ? new BattleObservation.BossTimerView(state.BossTimer)
                : null;

            PlayerStats player = state.Player;
            var playerView = new BattleObservation.PlayerView(
                player.currentHealth,
                player.maxHealth,
                player.armor,
                state.Mana.CurrentMana,
                state.Mana.TemporaryMana,
                state.Mana.BaseMana,
                state.ComboCount,
                state.Combo.ComboLength,
                state.CurrentLevel,
                state.CurrentXP,
                _combat.RequiredXP,
                player.fengRui,
                player.amount,
                player.bengLie,
                _combat.GetCurrentStatValue(StatType.NingShen),
                player.shouPai,
                player.yinJuan,
                _combat.CurrentLuck,
                _combat.CurrentGreed,
                _combat.CurrentGrowth,
                player.hunHun,
                player.zhaoSha,
                _combat.RunProgression.CardPlayGrowth.Capture());

            bool awaitingReward = state.AwaitingRewardSelection;
            return new BattleObservation(
                state.CurrentTurn,
                state.CurrentPhase,
                outcome,
                playerView,
                state.DeckCount,
                state.DiscardCount,
                hand,
                discardPile,
                visibleEnemies,
                crawlers,
                playerStatuses,
                terrain,
                bossTimer,
                awaitingReward,
                awaitingReward && _rewardOptionsKnown,
                awaitingReward && _rewardOptionsKnown
                    ? _rewardOptions
                    : Array.Empty<RewardOptionView>(),
                _combat.HasPendingLevelUpSealAssignment);
        }

        /// <summary>
        /// Returns exactly the actions accepted by Step at this instant. Reward
        /// actions are withheld when their legal index range is not known.
        /// </summary>
        public IReadOnlyList<LegalAction> GetLegalActions()
        {
            ThrowIfDisposed();
            CombatState state = _combat.State;
            if (state.IsBattleOver)
                return Array.Empty<LegalAction>();

            var actions = new List<LegalAction>();
            if (state.AwaitingRewardSelection)
            {
                if (_combat.HasPendingLevelUpSealAssignment)
                {
                    foreach (CardConfig host in _combat.GetPendingLevelUpSealCandidates())
                        actions.Add(LegalAction.AssignRewardSeal(host.runtimeInstanceId));
                    actions.Sort((a, b) => a.CardRuntimeInstanceId.CompareTo(b.CardRuntimeInstanceId));
                    actions.Add(LegalAction.CancelRewardSeal());
                    return actions.AsReadOnly();
                }
                if (_rewardOptionsKnown)
                {
                    for (int i = 0; i < _rewardOptions.Count; i++)
                    {
                        actions.Add(LegalAction.SelectReward(
                            _rewardOptions[i].RewardIndex));
                    }
                }
                return actions.AsReadOnly();
            }

            if (state.CanPlayCards)
            {
                int availableMana =
                    state.Mana.CurrentMana + state.Mana.TemporaryMana;
                for (int i = 0; i < state.Hand.Count; i++)
                {
                    CardConfig card = state.Hand[i];
                    if (card == null || card.runtimeInstanceId <= 0)
                        continue;
                    if (card.ResolveCost().PaymentCost <= availableMana)
                        actions.Add(LegalAction.PlayCard(
                            card.runtimeInstanceId));
                }
            }

            if (state.CanEndTurn)
                actions.Add(LegalAction.EndTurn());
            if (actions.Count == 0)
                return Array.Empty<LegalAction>();
            return actions.AsReadOnly();
        }

        /// <summary>
        /// Validates and executes one structured action through Core. The
        /// optional decision summary is appended to the replay only; its text
        /// is never parsed and cannot alter command selection or execution.
        /// </summary>
        public StepResult Step(
            LegalAction action,
            string decisionSummary = null)
        {
            ThrowIfDisposed();
            int sequence = _nextSequence++;
            BattleObservation before = Observe();

            if (action == null)
            {
                return RecordResult(
                    sequence,
                    null,
                    decisionSummary,
                    before,
                    false,
                    StepFailureCode.InvalidAction,
                    string.Empty,
                    "Action is required.");
            }

            if (!ContainsAction(GetLegalActions(), action))
            {
                StepFailureCode failure =
                    action.Type == LegalActionType.SelectReward &&
                    _combat.State.AwaitingRewardSelection &&
                    !_rewardOptionsKnown
                        ? StepFailureCode.RewardOptionsUnavailable
                        : StepFailureCode.InvalidAction;
                return RecordResult(
                    sequence,
                    action,
                    decisionSummary,
                    before,
                    false,
                    failure,
                    string.Empty,
                    "Action is not legal in the current observation.");
            }

            CommandResult coreResult = ExecuteAction(action);
            if (coreResult.Success && !_combat.State.AwaitingRewardSelection)
            {
                _rewardOptionsKnown = false;
                _rewardOptions = Array.Empty<RewardOptionView>();
            }

            return RecordResult(
                sequence,
                action,
                decisionSummary,
                before,
                coreResult.Success,
                coreResult.Success
                    ? StepFailureCode.None
                    : StepFailureCode.CoreRejected,
                coreResult.Success ? string.Empty : coreResult.Error.ToString(),
                coreResult.Message);
        }

        /// <summary>Exports a detached immutable trace at the current point.</summary>
        public ReplayTrace ExportTrace()
        {
            ThrowIfDisposed();
            if (_replaySteps.Count == 0)
            {
                BattleObservation current = Observe();
                return new ReplayTrace(
                    current,
                    current,
                    Array.Empty<ReplayTrace.Step>());
            }

            return new ReplayTrace(
                _replaySteps[0].Before,
                Observe(),
                _replaySteps);
        }

        private static bool IsPlayerVisible(EnemyStats enemy)
        {
            if (enemy == null || !enemy.IsDeployed || enemy.row < 0 ||
                enemy.row >= EnemyFormationLayout.VisibleRows)
                return false;
            return !enemy.IsDead || enemy.isOverkillTarget;
        }

        private static IReadOnlyList<BattleObservation.CrawlerView> CopyCrawlers(
            CombatState state)
        {
            IReadOnlyList<ColorTriggerSystem.ActiveCrawler> source =
                state.ColorTrigger?.ActiveCrawlers;
            if (source == null || source.Count == 0)
                return Array.Empty<BattleObservation.CrawlerView>();
            var result = new List<BattleObservation.CrawlerView>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                ColorTriggerSystem.ActiveCrawler crawler = source[i];
                CrawlerConfig config = crawler?.config;
                if (crawler == null || config == null || crawler.IsExpired)
                    continue;
                result.Add(new BattleObservation.CrawlerView(
                    crawler.RuntimeId,
                    config.contentId,
                    config.crawlerName,
                    crawler.EffectiveTriggerColor,
                    config.triggerEffect,
                    crawler.remainingTriggers));
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<BattleObservation.StatusView>
            CopyEnemyStatuses(CombatState state, int enemyRuntimeId)
        {
            if (state.Statuses == null || enemyRuntimeId <= 0 ||
                !state.Statuses.EnemyStatuses.TryGetValue(
                    enemyRuntimeId,
                    out List<StatusInstance> statuses))
            {
                return Array.Empty<BattleObservation.StatusView>();
            }
            return CopyStatuses(statuses);
        }

        private static IReadOnlyList<BattleObservation.StatusView> CopyStatuses(
            IReadOnlyList<StatusInstance> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<BattleObservation.StatusView>();
            var result = new List<BattleObservation.StatusView>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                StatusInstance status = source[i];
                if (status == null) continue;
                result.Add(new BattleObservation.StatusView(
                    status.effectType,
                    status.stacks,
                    status.remainingTurns,
                    status.sourceId));
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<BattleObservation.TerrainEffectView>
            CopyTerrain(CombatState state)
        {
            IReadOnlyList<TerrainEffectInstance> source =
                state.TerrainEffects?.Active;
            if (source == null || source.Count == 0)
                return Array.Empty<BattleObservation.TerrainEffectView>();
            var result =
                new List<BattleObservation.TerrainEffectView>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                {
                    result.Add(new BattleObservation.TerrainEffectView(
                        source[i]));
                }
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<EnemyIntent> GetVisibleIntentSource(
            CombatState state)
        {
            // StartTurn freezes intents before exposing PlayerInput. Avoid
            // asking EnemyAi to generate them during BattleInitializing,
            // because observation must not consume random numbers.
            if (state.CurrentTurn <= 0 || state.EnemyAi == null)
                return Array.Empty<EnemyIntent>();
            return state.EnemyAi.GetPlannedIntents();
        }

        private BattleObservation.CardView BuildCardView(
            CardConfig card,
            HashSet<int> visibleEnemyIds)
        {
            ResolvedCardCost cost = card.ResolveCost();
            AttackPlan plan = _combat.GetAttackPlanPreview(card);
            CardDamagePreviewCoverage coverage = _combat.GetDamagePreviewCoverage(card);
            var targetPreviews =
                new List<BattleObservation.TargetDamageView>();
            if (_combat.State.CurrentTurn > 0)
            {
                IReadOnlyList<EnemyDamagePreview> previews =
                    _combat.GetDeterministicDamagePreview(card);
                for (int i = 0; i < previews.Count; i++)
                {
                    EnemyDamagePreview preview = previews[i];
                    if (!visibleEnemyIds.Contains(
                            preview.TargetEnemyRuntimeId))
                        continue;
                    targetPreviews.Add(
                        new BattleObservation.TargetDamageView(
                            preview.TargetEnemyRuntimeId,
                            preview.Damage,
                            preview.WillKill,
                            preview.IsEstimate || coverage.IsEstimate));
                }
            }

            CardSupportEffectPreview support = CardSupportEffectProjection.Project(
                card, _combat.GetComboPreview(card).ResultingMultiplier, _combat.State.Mana);
            // Observe never prepares random choices. Core supplies an existing
            // frozen candidate or an explicitly estimated, RNG-free fallback.
            var immediate = new BattleObservation.ImmediateDamageView(
                _combat.State.CurrentTurn > 0
                    ? _combat.GetPreparedCardImmediateDamageDisplay(card) : null,
                visibleEnemyIds);
            return new BattleObservation.CardView(
                card.runtimeInstanceId,
                card.contentId,
                card.cardName,
                card.description,
                card.detailDescription,
                card.color,
                card.costType,
                cost.BaseManaValue,
                cost.ComboValue,
                cost.PaymentCost,
                cost.IsFreeToPlay,
                card.baseDamage,
                plan?.DisplayedCardDamage ?? 0,
                card.projectileCount,
                card.directTargetsPerHit,
                support.BaseArmor,
                support.BaseHeal,
                support.BaseManaGain,
                card.destroyOnUse,
                targetPreviews,
                support,
                coverage,
                immediate);
        }

        private CommandResult ExecuteAction(LegalAction action)
        {
            // Core owns both stages, including revalidation. No direct card or
            // pending-reward mutation and no inferred effect execution here.
            if (action.Type == LegalActionType.AssignRewardSeal)
                return _combat.TryAssignPendingLevelUpSeal(action.CardRuntimeInstanceId, out string error)
                    ? CommandResult.Ok : CommandResult.Fail(CommandError.InvalidTarget, error);
            if (action.Type == LegalActionType.CancelRewardSeal)
                return _combat.CancelPendingLevelUpSealSelection()
                    ? CommandResult.Ok : CommandResult.Fail(CommandError.NoRewardPending,
                        "No pending seal host selection to cancel.");
            return _combat.ExecuteCommand(BuildCommand(action));
        }

        private ICombatCommand BuildCommand(LegalAction action)
        {
            switch (action.Type)
            {
                case LegalActionType.PlayCard:
                    for (int i = 0; i < _combat.Hand.Count; i++)
                    {
                        CardConfig card = _combat.Hand[i];
                        if (card != null &&
                            card.runtimeInstanceId ==
                            action.CardRuntimeInstanceId)
                            return new PlayCardCommand(card, -1);
                    }
                    throw new InvalidOperationException(
                        "A legal card action lost its runtime instance.");
                case LegalActionType.EndTurn:
                    return new EndTurnCommand();
                case LegalActionType.SelectReward:
                    return new SelectRewardCommand(action.RewardIndex);
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        private StepResult RecordResult(
            int sequence,
            LegalAction action,
            string decisionSummary,
            BattleObservation before,
            bool success,
            StepFailureCode failureCode,
            string coreErrorCode,
            string message)
        {
            BattleObservation after = Observe();
            var result = new StepResult(
                sequence,
                success,
                failureCode,
                coreErrorCode,
                message,
                after);
            _replaySteps.Add(new ReplayTrace.Step(
                sequence,
                action,
                decisionSummary,
                before,
                result));
            return result;
        }

        private static bool ContainsAction(
            IReadOnlyList<LegalAction> legalActions,
            LegalAction requested)
        {
            for (int i = 0; i < legalActions.Count; i++)
            {
                if (legalActions[i].Equals(requested))
                    return true;
            }
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HeadlessCombatSession));
        }

        private static IReadOnlyList<RewardOptionView> CopyRewardOptions(
            IReadOnlyList<LevelUpSystem.Reward> rewards)
        {
            if (rewards == null || rewards.Count == 0)
                return Array.Empty<RewardOptionView>();

            var result = new List<RewardOptionView>(rewards.Count);
            for (int i = 0; i < rewards.Count; i++)
            {
                if (rewards[i] != null)
                    result.Add(new RewardOptionView(i, rewards[i]));
            }
            return result.AsReadOnly();
        }
    }
}
