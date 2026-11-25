using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Simulation.Protocol
{
    /// <summary>
    /// Version marker for the headless agent boundary. Consumers must record
    /// this value with traces instead of assuming that DTO fields never change.
    /// </summary>
    public static class AgentProtocol
    {
        public const int SchemaVersion = 1;
        // Additive actions/observation field only: v1 payload fields and action
        // ordinals retain their meaning. New writers advertise the capability.
        public const string LegacyVersion = "wanjie-scroll-agent.v1";
        public const string SealAssignmentVersion = "wanjie-scroll-agent.v1.1";
        public const string Version = "wanjie-scroll-agent.v1.2";
    }

    /// <summary>The complete action vocabulary exposed to an external agent.</summary>
    public enum LegalActionType
    {
        PlayCard = 0,
        EndTurn = 1,
        SelectReward = 2,
        AssignRewardSeal = 3,
        CancelRewardSeal = 4
    }

    /// <summary>
    /// A value-only request that can cross a process boundary. Card actions use
    /// the encounter-stable runtime instance id; display names and content ids
    /// are deliberately not accepted as identities.
    /// </summary>
    public sealed class LegalAction : IEquatable<LegalAction>
    {
        private LegalAction(
            LegalActionType type,
            int cardRuntimeInstanceId,
            int rewardIndex)
        {
            Type = type;
            CardRuntimeInstanceId = cardRuntimeInstanceId;
            RewardIndex = rewardIndex;
        }

        public LegalActionType Type { get; }
        public int CardRuntimeInstanceId { get; }
        public int RewardIndex { get; }

        public static LegalAction PlayCard(int cardRuntimeInstanceId)
            => new LegalAction(
                LegalActionType.PlayCard,
                cardRuntimeInstanceId,
                -1);

        public static LegalAction EndTurn()
            => new LegalAction(LegalActionType.EndTurn, 0, -1);

        public static LegalAction SelectReward(int rewardIndex)
            => new LegalAction(LegalActionType.SelectReward, 0, rewardIndex);

        public static LegalAction AssignRewardSeal(int cardRuntimeInstanceId)
            => new LegalAction(LegalActionType.AssignRewardSeal, cardRuntimeInstanceId, -1);

        public static LegalAction CancelRewardSeal()
            => new LegalAction(LegalActionType.CancelRewardSeal, 0, -1);

        public bool Equals(LegalAction other)
        {
            return other != null &&
                   Type == other.Type &&
                   CardRuntimeInstanceId == other.CardRuntimeInstanceId &&
                   RewardIndex == other.RewardIndex;
        }

        public override bool Equals(object obj)
            => Equals(obj as LegalAction);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Type;
                hash = (hash * 397) ^ CardRuntimeInstanceId;
                hash = (hash * 397) ^ RewardIndex;
                return hash;
            }
        }

        public override string ToString()
        {
            return Type switch
            {
                LegalActionType.PlayCard =>
                    $"PlayCard({CardRuntimeInstanceId})",
                LegalActionType.SelectReward =>
                    $"SelectReward({RewardIndex})",
                LegalActionType.AssignRewardSeal =>
                    $"AssignRewardSeal({CardRuntimeInstanceId})",
                LegalActionType.CancelRewardSeal => "CancelRewardSeal",
                _ => "EndTurn"
            };
        }
    }

    /// <summary>
    /// Immutable, player-visible battle snapshot. It contains copied values
    /// only: no CombatController, CombatState, ScriptableObject, collection
    /// owned by Core, or other mutable gameplay object crosses this boundary.
    /// </summary>
    public sealed class BattleObservation
    {
        internal BattleObservation(
            int turn,
            BattlePhase phase,
            CombatOutcome? outcome,
            PlayerView player,
            int drawPileCount,
            int discardPileCount,
            IReadOnlyList<CardView> hand,
            IReadOnlyList<CardIdentityView> discardPile,
            IReadOnlyList<EnemyView> visibleEnemies,
            IReadOnlyList<CrawlerView> activeCrawlers,
            IReadOnlyList<StatusView> playerStatuses,
            IReadOnlyList<TerrainEffectView> terrainEffects,
            BossTimerView bossTimer,
            bool awaitingRewardSelection,
            bool rewardOptionsKnown,
            IReadOnlyList<RewardOptionView> rewardOptions,
            bool sealAssignmentPending = false)
        {
            Turn = turn;
            Phase = phase;
            Outcome = outcome;
            Player = player ?? throw new ArgumentNullException(nameof(player));
            DrawPileCount = drawPileCount;
            DiscardPileCount = discardPileCount;
            Hand = Freeze(hand);
            DiscardPile = Freeze(discardPile);
            VisibleEnemies = Freeze(visibleEnemies);
            ActiveCrawlers = Freeze(activeCrawlers);
            PlayerStatuses = Freeze(playerStatuses);
            TerrainEffects = Freeze(terrainEffects);
            BossTimer = bossTimer;
            AwaitingRewardSelection = awaitingRewardSelection;
            RewardOptionsKnown = rewardOptionsKnown;
            RewardOptions = Freeze(rewardOptions);
            SealAssignmentPending = sealAssignmentPending;
        }

        public string ProtocolVersion => AgentProtocol.Version;
        public int Turn { get; }
        public BattlePhase Phase { get; }
        public CombatOutcome? Outcome { get; }
        public bool IsCombatOver => Outcome.HasValue;
        /// <summary>Derived solely from public observation; it reveals no hidden roster information.</summary>
        public bool WaitingForTargets => !IsCombatOver && !AwaitingRewardSelection &&
            Turn > 0 && VisibleEnemies.Count == 0;
        public string TargetAvailabilityMessage => WaitingForTargets
            ? "Battle is still in progress, but no enemy is currently visible. Resolve available support actions or end the turn when legal to advance the battle."
            : string.Empty;
        public PlayerView Player { get; }
        public int DrawPileCount { get; }
        public int DiscardPileCount { get; }
        public IReadOnlyList<CardView> Hand { get; }
        public IReadOnlyList<CardIdentityView> DiscardPile { get; }
        public IReadOnlyList<EnemyView> VisibleEnemies { get; }
        public IReadOnlyList<CrawlerView> ActiveCrawlers { get; }
        public IReadOnlyList<StatusView> PlayerStatuses { get; }
        public IReadOnlyList<TerrainEffectView> TerrainEffects { get; }
        public BossTimerView BossTimer { get; }
        public bool AwaitingRewardSelection { get; }
        /// <summary>Core is awaiting an exact seal host, not another reward index.</summary>
        public bool SealAssignmentPending { get; }

        /// <summary>
        /// False means the public Core event carrying reward candidates was
        /// emitted before this session attached. In that degraded state the
        /// session exposes no guessed SelectReward action.
        /// </summary>
        public bool RewardOptionsKnown { get; }

        /// <summary>
        /// Number of legal reward indices copied from the public reward event.
        /// </summary>
        public int RewardOptionCount => RewardOptions.Count;
        public IReadOnlyList<RewardOptionView> RewardOptions { get; }

        private static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<T>();
            return new List<T>(source).AsReadOnly();
        }

        public sealed class PlayerView
        {
            internal PlayerView(
                int currentHealth,
                int maxHealth,
                int armor,
                int currentMana,
                int temporaryMana,
                int baseMana,
                int comboCount,
                int comboLength,
                int level,
                int experience,
                int requiredExperience,
                int might,
                int amount,
                int area,
                int duration,
                int handLimit,
                int drawPerTurn,
                int luck,
                int greed,
                int growth,
                int revival,
                int curse,
                RunCardPlayGrowthSnapshot cardPlayGrowth = default)
            {
                CurrentHealth = currentHealth;
                MaxHealth = maxHealth;
                Armor = armor;
                CurrentMana = currentMana;
                TemporaryMana = temporaryMana;
                BaseMana = baseMana;
                ComboCount = comboCount;
                ComboLength = comboLength;
                Level = level;
                Experience = experience;
                RequiredExperience = requiredExperience;
                Might = might;
                Amount = amount;
                Area = area;
                Duration = duration;
                HandLimit = handLimit;
                DrawPerTurn = drawPerTurn;
                Luck = luck;
                Greed = greed;
                Growth = growth;
                Revival = revival;
                Curse = curse;
                CardPlayGrowth = cardPlayGrowth;
            }

            public int CurrentHealth { get; }
            public int MaxHealth { get; }
            public int Armor { get; }
            public int CurrentMana { get; }
            public int TemporaryMana { get; }
            public int AvailableMana => CurrentMana + TemporaryMana;
            public int BaseMana { get; }
            public int ComboCount { get; }
            public int ComboLength { get; }
            public int Level { get; }
            public int Experience { get; }
            public int RequiredExperience { get; }
            public int Might { get; }
            public int Amount { get; }
            public int Area { get; }
            public int Duration { get; }
            public int HandLimit { get; }
            public int DrawPerTurn { get; }
            public int Luck { get; }
            public int Greed { get; }
            public int Growth { get; }
            public int Revival { get; }
            public int Curse { get; }
            /// <summary>Frozen public Run rule progress, not a reference to mutable state.</summary>
            public RunCardPlayGrowthSnapshot CardPlayGrowth { get; }
        }

        public sealed class CardView
        {
            internal CardView(
                int runtimeInstanceId,
                string contentId,
                string name,
                string description,
                string detailDescription,
                CardColor color,
                CostType costType,
                int baseManaValue,
                int comboValue,
                int paymentCost,
                bool isFreeToPlay,
                int baseDamage,
                int displayedDamage,
                int projectileCount,
                int directTargetsPerHit,
                int armor,
                int heal,
                int manaGain,
                bool destroyOnUse,
                IReadOnlyList<TargetDamageView> damagePreview,
                CardSupportEffectPreview supportEffects = null,
                CardDamagePreviewCoverage damageCoverage = null,
                ImmediateDamageView immediateDamage = null)
            {
                RuntimeInstanceId = runtimeInstanceId;
                ContentId = contentId ?? string.Empty;
                Name = name ?? string.Empty;
                Description = description ?? string.Empty;
                DetailDescription = detailDescription ?? string.Empty;
                Color = color;
                CostType = costType;
                BaseManaValue = baseManaValue;
                ComboValue = comboValue;
                PaymentCost = paymentCost;
                IsFreeToPlay = isFreeToPlay;
                BaseDamage = baseDamage;
                DisplayedDamage = displayedDamage;
                ProjectileCount = projectileCount;
                DirectTargetsPerHit = directTargetsPerHit;
                Armor = armor;
                Heal = heal;
                ManaGain = manaGain;
                DestroyOnUse = destroyOnUse;
                DamagePreview = Freeze(damagePreview);
                SupportEffects = supportEffects;
                DamageCoverage = damageCoverage;
                ImmediateDamage = immediateDamage;
            }

            public int RuntimeInstanceId { get; }
            public string ContentId { get; }
            public string Name { get; }
            public string Description { get; }
            public string DetailDescription { get; }
            public CardColor Color { get; }
            public CostType CostType { get; }
            public int BaseManaValue { get; }
            public int ComboValue { get; }
            public int PaymentCost { get; }
            public bool IsFreeToPlay { get; }
            public int BaseDamage { get; }
            public int DisplayedDamage { get; }
            public int ProjectileCount { get; }
            public int DirectTargetsPerHit { get; }
            public int Armor { get; }
            public int Heal { get; }
            public int ManaGain { get; }
            public bool DestroyOnUse { get; }
            public IReadOnlyList<TargetDamageView> DamagePreview { get; }
            /// <summary>Typed-first host bases, scaled requests and conditional/seal effects.</summary>
            public CardSupportEffectPreview SupportEffects { get; }
            /// <summary>Coverage and uncertainty remain explicit when DamagePreview is empty.</summary>
            public CardDamagePreviewCoverage DamageCoverage { get; }
            /// <summary>Player-visible immediate applied damage; legacy estimates above keep their meaning.</summary>
            public ImmediateDamageView ImmediateDamage { get; }
        }

        /// <summary>
        /// A detached copy of the player's prepared damage display. Only public
        /// enemy identities and their applied damage enter this observation;
        /// requested damage, random tapes and hidden actors are never exposed.
        /// </summary>
        public sealed class ImmediateDamageView
        {
            internal ImmediateDamageView(CardImmediateDamageDisplaySummary source,
                ISet<int> visibleEnemyIds)
            {
                HasCombatContext = source != null;
                HasDamage = source?.HasDamage == true;
                IsEstimate = source == null || source.IsEstimate;
                var targets = new List<ImmediateTargetDamageView>();
                if (source != null)
                    foreach (EnemyDamagePreview row in source.DamagePreviews)
                    {
                        if (visibleEnemyIds == null || !visibleEnemyIds.Contains(row.TargetEnemyRuntimeId))
                            continue;
                        targets.Add(new ImmediateTargetDamageView(row, IsEstimate));
                        // Sum only the already-resolved public rows. This is a
                        // visibility filter, not a second damage calculation.
                        TotalAppliedDamage = checked(TotalAppliedDamage + row.AppliedDamage);
                    }
                Targets = targets.AsReadOnly();
            }

            public bool HasCombatContext { get; }
            public bool HasDamage { get; }
            public bool IsEstimate { get; }
            public int TotalAppliedDamage { get; }
            public IReadOnlyList<ImmediateTargetDamageView> Targets { get; }
        }

        public sealed class ImmediateTargetDamageView
        {
            internal ImmediateTargetDamageView(EnemyDamagePreview row, bool isEstimate)
            {
                EnemyRuntimeId = row.TargetEnemyRuntimeId;
                AppliedDamage = row.AppliedDamage;
                OverkillDamage = row.OverkillDamage;
                IsErased = row.IsErased;
                IsEstimate = isEstimate || row.IsEstimate;
                WillKill = row.WillKill && !IsEstimate;
            }

            public int EnemyRuntimeId { get; }
            public int AppliedDamage { get; }
            public int OverkillDamage { get; }
            public bool IsErased { get; }
            public bool IsEstimate { get; }
            public bool WillKill { get; }
        }

        public sealed class CardIdentityView
        {
            internal CardIdentityView(
                int runtimeInstanceId,
                string contentId,
                string name)
            {
                RuntimeInstanceId = runtimeInstanceId;
                ContentId = contentId ?? string.Empty;
                Name = name ?? string.Empty;
            }

            public int RuntimeInstanceId { get; }
            public string ContentId { get; }
            public string Name { get; }
        }

        public sealed class StatusView
        {
            internal StatusView(
                StatusEffect effect,
                int stacks,
                int remainingTurns,
                int sourceId)
            {
                Effect = effect;
                Stacks = stacks;
                RemainingTurns = remainingTurns;
                SourceId = sourceId;
            }

            public StatusEffect Effect { get; }
            public int Stacks { get; }
            public int RemainingTurns { get; }
            public bool IsPermanent => RemainingTurns < 0;
            public int SourceId { get; }
        }

        public sealed class CrawlerView
        {
            internal CrawlerView(
                int runtimeId,
                string contentId,
                string name,
                CardColor triggerColor,
                string triggerEffect,
                int remainingTriggers)
            {
                RuntimeId = runtimeId;
                ContentId = contentId ?? string.Empty;
                Name = name ?? string.Empty;
                TriggerColor = triggerColor;
                TriggerEffect = triggerEffect ?? string.Empty;
                RemainingTriggers = remainingTriggers;
            }

            public int RuntimeId { get; }
            public string ContentId { get; }
            public string Name { get; }
            public CardColor TriggerColor { get; }
            public string TriggerEffect { get; }
            public int RemainingTriggers { get; }
        }

        public sealed class TerrainEffectView
        {
            internal TerrainEffectView(TerrainEffectInstance effect)
            {
                RuntimeId = effect.RuntimeId;
                EffectId = effect.EffectId ?? string.Empty;
                EffectKind = effect.EffectKind;
                TriggerTiming = effect.TriggerTiming;
                DamageKind = effect.DamageKind;
                SourceCardInstanceId = effect.SourceCardInstanceId;
                Column = effect.Column;
                Row = effect.Row;
                MagnitudePerStack = effect.MagnitudePerStack;
                Stacks = effect.Stacks;
                RemainingTriggers = effect.RemainingTriggers;
            }

            public int RuntimeId { get; }
            public string EffectId { get; }
            public TerrainEffectKind EffectKind { get; }
            public TerrainEffectTiming TriggerTiming { get; }
            public DamageKind DamageKind { get; }
            public int SourceCardInstanceId { get; }
            public int Column { get; }
            public int Row { get; }
            public int MagnitudePerStack { get; }
            public int Stacks { get; }
            public int TotalMagnitude => MagnitudePerStack * Stacks;
            public int RemainingTriggers { get; }
        }

        public sealed class BossTimerView
        {
            internal BossTimerView(BossTimer timer)
            {
                TotalEyes = timer.TotalEyes;
                OpenEyes = timer.OpenEyes;
                Damage = timer.Damage;
                IsTriggered = timer.IsTriggered;
            }

            public int TotalEyes { get; }
            public int OpenEyes { get; }
            public int Damage { get; }
            public bool IsTriggered { get; }
        }

        public sealed class TargetDamageView
        {
            internal TargetDamageView(
                int enemyRuntimeId,
                int damage,
                bool willKill,
                bool isEstimate = false)
            {
                EnemyRuntimeId = enemyRuntimeId;
                Damage = damage;
                IsEstimate = isEstimate;
                EstimatedWillKill = willKill;
                WillKill = willKill && !isEstimate;
            }

            public int EnemyRuntimeId { get; }
            public int Damage { get; }
            /// <summary>Unresolved random/control decisions make Damage an estimate.</summary>
            public bool IsEstimate { get; }
            public bool EstimatedWillKill { get; }
            /// <summary>Never promises a kill when the preview is only an estimate.</summary>
            public bool WillKill { get; }
        }

        public sealed class EnemyView
        {
            internal EnemyView(
                int runtimeId,
                string contentId,
                string name,
                string description,
                int currentHealth,
                int maxHealth,
                int armor,
                bool isBoss,
                bool isDisarmed,
                bool isOverkillTarget,
                int row,
                int column,
                int gridWidth,
                int gridDepth,
                IntentView intent,
                IReadOnlyList<StatusView> statuses,
                int? interruptMaximumCountdown = null,
                int? interruptCurrentCountdown = null)
            {
                RuntimeId = runtimeId;
                ContentId = contentId ?? string.Empty;
                Name = name ?? string.Empty;
                Description = description ?? string.Empty;
                CurrentHealth = currentHealth;
                MaxHealth = maxHealth;
                Armor = armor;
                IsBoss = isBoss;
                IsDisarmed = isDisarmed;
                IsOverkillTarget = isOverkillTarget;
                Row = row;
                Column = column;
                GridWidth = gridWidth;
                GridDepth = gridDepth;
                Intent = intent ?? IntentView.None;
                Statuses = Freeze(statuses);
                InterruptMaximumCountdown = interruptMaximumCountdown;
                InterruptCurrentCountdown = interruptCurrentCountdown;
            }

            public int RuntimeId { get; }
            public string ContentId { get; }
            public string Name { get; }
            public string Description { get; }
            public int CurrentHealth { get; }
            public int MaxHealth { get; }
            public int Armor { get; }
            public bool IsBoss { get; }
            public bool IsDisarmed { get; }
            public bool IsOverkillTarget { get; }
            public int Row { get; }
            public int Column { get; }
            public int GridWidth { get; }
            public int GridDepth { get; }
            public IntentView Intent { get; }
            public IReadOnlyList<StatusView> Statuses { get; }
            public int? InterruptMaximumCountdown { get; }
            public int? InterruptCurrentCountdown { get; }
        }

        public sealed class IntentView
        {
            internal static readonly IntentView None = new IntentView(
                EnemyIntentType.None,
                0,
                0,
                string.Empty);

            internal IntentView(
                EnemyIntentType type,
                int amount,
                int targetCount,
                string description)
            {
                Type = type;
                Amount = amount;
                TargetCount = targetCount;
                Description = description ?? string.Empty;
            }

            public EnemyIntentType Type { get; }
            public int Amount { get; }
            public int TargetCount { get; }
            public string Description { get; }
        }
    }

    public enum StepFailureCode
    {
        None,
        InvalidAction,
        RewardOptionsUnavailable,
        CoreRejected
    }

    /// <summary>
    /// Result of one attempted agent action. Observation is always the fresh
    /// snapshot after acceptance or rejection, so callers never need Core data.
    /// </summary>
    public sealed class StepResult
    {
        internal StepResult(
            int sequence,
            bool success,
            StepFailureCode failureCode,
            string coreErrorCode,
            string message,
            BattleObservation observation)
        {
            Sequence = sequence;
            Success = success;
            FailureCode = failureCode;
            CoreErrorCode = coreErrorCode ?? string.Empty;
            Message = message ?? string.Empty;
            Observation = observation ??
                throw new ArgumentNullException(nameof(observation));
        }

        public string ProtocolVersion => AgentProtocol.Version;
        public int Sequence { get; }
        public bool Success { get; }
        public StepFailureCode FailureCode { get; }
        public string CoreErrorCode { get; }
        public string Message { get; }
        public BattleObservation Observation { get; }
    }

    /// <summary>
    /// Immutable replay of every attempted action, including rejected ones.
    /// DecisionSummary is telemetry only: HeadlessCombatSession records it
    /// after choosing the command and never parses it to determine execution.
    /// </summary>
    public sealed class ReplayTrace
    {
        internal ReplayTrace(
            BattleObservation initialObservation,
            BattleObservation finalObservation,
            IReadOnlyList<Step> steps)
        {
            InitialObservation = initialObservation ??
                throw new ArgumentNullException(nameof(initialObservation));
            FinalObservation = finalObservation ??
                throw new ArgumentNullException(nameof(finalObservation));
            if (steps == null || steps.Count == 0)
                Steps = Array.Empty<Step>();
            else
                Steps = new List<Step>(steps).AsReadOnly();
        }

        public string ProtocolVersion => AgentProtocol.Version;
        public BattleObservation InitialObservation { get; }
        public BattleObservation FinalObservation { get; }
        public IReadOnlyList<Step> Steps { get; }

        public sealed class Step
        {
            internal Step(
                int sequence,
                LegalAction requestedAction,
                string decisionSummary,
                BattleObservation before,
                StepResult result)
            {
                Sequence = sequence;
                RequestedAction = requestedAction;
                DecisionSummary = decisionSummary ?? string.Empty;
                Before = before ?? throw new ArgumentNullException(nameof(before));
                Result = result ?? throw new ArgumentNullException(nameof(result));
            }

            public int Sequence { get; }
            public LegalAction RequestedAction { get; }
            public string DecisionSummary { get; }
            public BattleObservation Before { get; }
            public StepResult Result { get; }
            public BattleObservation After => Result.Observation;
        }
    }
}
