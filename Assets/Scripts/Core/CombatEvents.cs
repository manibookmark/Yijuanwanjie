using System.Collections.Generic;

namespace WanjieScroll.Core
{
    public enum CombatStateChangeReason
    {
        TurnStarted,
        CardsDrawn,
        CardPlayed,
        CardShattered,
        HandDiscarded,
        TurnEnded,
        EnemyKilled,
        XPGained,
        LevelChanged,
        RewardOptionsChanged,
        CrawlerChanged,
        PlayerDamaged,
        EnemyActed,
        LevelBossArrived,
        RewardSelected,
        CombatEnded,
        EnemyWithdrawn,
        PlayerRevived
    }

    /// <summary>Actual Run-charge consumption after a lethal hit, before terminal publication.</summary>
    public readonly struct PlayerRevivedEvent
    {
        public int RestoredHealth { get; }
        public int RemainingPercent { get; }
        public int SpentLives { get; }
        public PlayerRevivedEvent(int restoredHealth, int remainingPercent, int spentLives)
        { RestoredHealth = restoredHealth; RemainingPercent = remainingPercent; SpentLives = spentLives; }
    }

    /// <summary>
    /// 战斗终局结果。
    /// </summary>
    public enum CombatOutcome
    {
        Victory,
        Defeat
    }

    /// <summary>
    /// 战斗终局事件。每场战斗只发布一次，由 <see cref="CombatController.OnCombatEnded"/> 通知。
    /// </summary>
    public readonly struct CombatEndedEvent
    {
        public CombatOutcome Outcome { get; }

        public CombatEndedEvent(CombatOutcome outcome)
        {
            Outcome = outcome;
        }
    }

    /// <summary>
    /// 一名敌人主动撤离战斗的权威结果。RuntimeId 是稳定身份，列表索引仅供
    /// 同一场战斗定位现有角色；原占格用于撤离表现，不得从后续阵型反推。
    /// </summary>
    public readonly struct EnemyWithdrawnEvent
    {
        public int EnemyRuntimeId { get; }
        public int EnemyIndex { get; }
        public FormationFootprint PreviousFootprint { get; }

        public EnemyWithdrawnEvent(
            int enemyRuntimeId,
            int enemyIndex,
            FormationFootprint previousFootprint)
        {
            EnemyRuntimeId = enemyRuntimeId;
            EnemyIndex = enemyIndex;
            PreviousFootprint = previousFootprint;
        }
    }

    /// <summary>
    /// Controls when enemy experience enters the authoritative progression
    /// state. Immediate preserves headless/simulation behaviour. Deferred
    /// segments are committed explicitly by a presentation coordinator and
    /// stop at every level boundary.
    /// </summary>
    public enum ExperienceAwardCommitMode
    {
        Immediate,
        DeferredSegments
    }

    /// <summary>
    /// An immutable enemy experience credit. The runtime enemy id is the
    /// stable encounter identity; presentation must never pair rewards by an
    /// enemy display name.
    /// </summary>
    public readonly struct CombatExperienceAward
    {
        public long AwardId { get; }
        public int SourceEnemyRuntimeId { get; }
        public int BaseExperience { get; }
        public int ActualExperience { get; }

        public CombatExperienceAward(
            long awardId,
            int sourceEnemyRuntimeId,
            int baseExperience,
            int actualExperience)
        {
            AwardId = awardId;
            SourceEnemyRuntimeId = sourceEnemyRuntimeId;
            BaseExperience = baseExperience;
            ActualExperience = actualExperience;
        }
    }

    /// <summary>
    /// Authoritative result of committing one portion of an experience
    /// award. A deferred award never crosses more than one level boundary in
    /// a segment, allowing presentation to stop at the normal reward panel.
    /// </summary>
    public readonly struct CombatExperienceSegmentResult
    {
        public long AwardId { get; }
        public int SourceEnemyRuntimeId { get; }
        public int AppliedExperience { get; }
        public int RemainingExperience { get; }
        public int LevelBefore { get; }
        public int ExperienceBefore { get; }
        public int RequiredExperienceBefore { get; }
        public int LevelAfter { get; }
        public int ExperienceAfter { get; }
        public int RequiredExperienceAfter { get; }
        public bool TriggeredLevelUp { get; }
        public bool AwaitingRewardSelection { get; }
        public bool AwardFullyCommitted { get; }

        public CombatExperienceSegmentResult(
            long awardId,
            int sourceEnemyRuntimeId,
            int appliedExperience,
            int remainingExperience,
            int levelBefore,
            int experienceBefore,
            int requiredExperienceBefore,
            int levelAfter,
            int experienceAfter,
            int requiredExperienceAfter,
            bool triggeredLevelUp,
            bool awaitingRewardSelection,
            bool awardFullyCommitted)
        {
            AwardId = awardId;
            SourceEnemyRuntimeId = sourceEnemyRuntimeId;
            AppliedExperience = appliedExperience;
            RemainingExperience = remainingExperience;
            LevelBefore = levelBefore;
            ExperienceBefore = experienceBefore;
            RequiredExperienceBefore = requiredExperienceBefore;
            LevelAfter = levelAfter;
            ExperienceAfter = experienceAfter;
            RequiredExperienceAfter = requiredExperienceAfter;
            TriggeredLevelUp = triggeredLevelUp;
            AwaitingRewardSelection = awaitingRewardSelection;
            AwardFullyCommitted = awardFullyCommitted;
        }
    }

    /// <summary>
    /// A stat delta that was actually committed while resolving a card.
    /// Presentation code consumes this result instead of recalculating card
    /// rules, so combo scaling and capped stats stay authoritative.
    /// </summary>
    public readonly struct CombatStatChange
    {
        public StatType StatType { get; }
        public int Amount { get; }
        public int ResultingValue { get; }

        public CombatStatChange(
            StatType statType,
            int amount,
            int resultingValue)
        {
            StatType = statType;
            Amount = amount;
            ResultingValue = resultingValue;
        }
    }

    /// <summary>
    /// 一名在场英灵对某张牌的非破坏性触发预览。
    /// StatDeltas 只描述本次增量；展示层不得据此修改战斗状态。
    /// </summary>
    public sealed class CrawlerTriggerPreview
    {
        public int CrawlerRuntimeId { get; }
        public string CrawlerContentId { get; }
        public string CrawlerName { get; }
        public IReadOnlyList<KeyValuePair<StatType, int>> StatDeltas { get; }

        public CrawlerTriggerPreview(
            int crawlerRuntimeId,
            string crawlerContentId,
            string crawlerName,
            IReadOnlyList<KeyValuePair<StatType, int>> statDeltas)
        {
            CrawlerRuntimeId = crawlerRuntimeId;
            CrawlerContentId = crawlerContentId;
            CrawlerName = crawlerName;
            StatDeltas = statDeltas ??
                System.Array.Empty<KeyValuePair<StatType, int>>();
        }
    }

    /// <summary>
    /// 一名在场英灵已完成触发后的权威结果。属性变化均为实际写入值，
    /// 展示层用稳定 contentId 定位驻场形象，不使用显示名称关联。
    /// </summary>
    public sealed class CombatCrawlerTriggeredEvent
    {
        public int CrawlerRuntimeId { get; }
        public string CrawlerContentId { get; }
        public string CrawlerName { get; }
        public IReadOnlyList<CombatStatChange> StatChanges { get; }
        public IReadOnlyList<CrawlerDurationChange> CrawlerDurationChanges { get; }
        public int DurationReserveGained { get; }
        public int ResultingDurationReserve { get; }

        public CombatCrawlerTriggeredEvent(
            int crawlerRuntimeId,
            string crawlerContentId,
            string crawlerName,
            IReadOnlyList<CombatStatChange> statChanges,
            IReadOnlyList<CrawlerDurationChange> crawlerDurationChanges = null,
            int durationReserveGained = 0,
            int resultingDurationReserve = 0)
        {
            CrawlerRuntimeId = crawlerRuntimeId;
            CrawlerContentId = crawlerContentId;
            CrawlerName = crawlerName;
            StatChanges = statChanges ??
                System.Array.Empty<CombatStatChange>();
            CrawlerDurationChanges = crawlerDurationChanges ?? System.Array.Empty<CrawlerDurationChange>();
            DurationReserveGained = durationReserveGained;
            ResultingDurationReserve = resultingDurationReserve;
        }
    }

    /// <summary>
    /// Authoritative identities of cards that actually entered the hand.
    /// Published before CardsDrawn state refresh so UI can prepare their
    /// arrival without inferring from hand counts or content names.
    /// </summary>
    public readonly struct CombatCardsDrawnEvent
    {
        public IReadOnlyList<int> CardInstanceIds { get; }
        public int Count => CardInstanceIds?.Count ?? 0;

        public CombatCardsDrawnEvent(
            IReadOnlyList<int> cardInstanceIds)
        {
            CardInstanceIds = cardInstanceIds;
        }
    }

    /// <summary>
    /// One authoritative enemy action result. Presentation reads this
    /// snapshot at the impact frame instead of predicting damage or querying
    /// mutable state after a later enemy has already acted.
    /// </summary>
    public readonly struct CombatEnemyActionResult
    {
        public int EnemyRuntimeId { get; }
        public int EnemyIndex { get; }
        public bool EnemyIsBoss { get; }
        public EnemyIntent Intent { get; }
        public bool WasDisarmed { get; }
        public int PlayerHealthBefore { get; }
        public int PlayerHealthAfter { get; }
        public int PlayerArmorBefore { get; }
        public int PlayerArmorAfter { get; }
        public int EnemyRowBefore { get; }
        public int EnemyRowAfter { get; }
        public int EnemyArmorBefore { get; }
        public int EnemyArmorAfter { get; }
        /// <summary>Null when no injection resolved; a capped injection has an actual count of zero.</summary>
        public EnemyTemporaryCardGrantResult? TemporaryCardGrant { get; }

        // Final health includes revival. New combat receipts carry actual hit
        // amounts; optional defaults preserve older in-memory constructors.
        public int PlayerHealthDamage { get; }
        public int PlayerArmorDamage { get; }
        public int TotalPlayerDamage =>
            (int)System.Math.Min(int.MaxValue, (long)PlayerHealthDamage + PlayerArmorDamage);
        public bool DidDamagePlayer => TotalPlayerDamage > 0;
        public bool IsFatal =>
            PlayerHealthBefore > 0 && PlayerHealthAfter <= 0;

        public CombatEnemyActionResult(
            int enemyRuntimeId,
            int enemyIndex,
            bool enemyIsBoss,
            EnemyIntent intent,
            bool wasDisarmed,
            int playerHealthBefore,
            int playerHealthAfter,
            int playerArmorBefore,
            int playerArmorAfter,
            int enemyRowBefore,
            int enemyRowAfter,
            int enemyArmorBefore,
            int enemyArmorAfter,
            EnemyTemporaryCardGrantResult? temporaryCardGrant = null,
            long? playerHealthDamage = null,
            long? playerArmorDamage = null)
        {
            EnemyRuntimeId = enemyRuntimeId;
            EnemyIndex = enemyIndex;
            EnemyIsBoss = enemyIsBoss;
            Intent = intent;
            WasDisarmed = wasDisarmed;
            PlayerHealthBefore = playerHealthBefore;
            PlayerHealthAfter = playerHealthAfter;
            PlayerArmorBefore = playerArmorBefore;
            PlayerArmorAfter = playerArmorAfter;
            EnemyRowBefore = enemyRowBefore;
            EnemyRowAfter = enemyRowAfter;
            EnemyArmorBefore = enemyArmorBefore;
            EnemyArmorAfter = enemyArmorAfter;
            TemporaryCardGrant = temporaryCardGrant;
            PlayerHealthDamage = (int)System.Math.Max(0L, System.Math.Min(int.MaxValue, playerHealthDamage ?? ((long)playerHealthBefore - playerHealthAfter)));
            PlayerArmorDamage = (int)System.Math.Max(0L, System.Math.Min(int.MaxValue, playerArmorDamage ?? ((long)playerArmorBefore - playerArmorAfter)));
        }
    }

    /// <summary>Actual encounter-temporary instance creation; never a draw or a presentation request.</summary>
    public readonly struct CombatCreatedCardReceipt
    {
        public int SourceCardInstanceId { get; }
        public string SourceCardContentId { get; }
        public int CreatedCardInstanceId { get; }
        public string DefinitionContentId { get; }
        public EncounterTemporaryCardPlacement Placement { get; }

        public CombatCreatedCardReceipt(CardConfig source, CardConfig created,
            EncounterTemporaryCardPlacement placement)
        {
            SourceCardInstanceId = source.runtimeInstanceId;
            SourceCardContentId = source.contentId;
            CreatedCardInstanceId = created.runtimeInstanceId;
            DefinitionContentId = created.contentId;
            Placement = placement;
        }
    }

    /// <summary>Actual transaction source; card identity and cost do not determine this.</summary>
    public enum CardPlayOrigin
    {
        Player = 0,
        CardEffect = 1
    }

    public sealed class CombatCardResolvedEvent
    {
        public CardConfig Card { get; set; }
        /// <summary>Defaults to Player for existing manually supplied receipts.</summary>
        public CardPlayOrigin PlayOrigin { get; internal set; }
        public int TargetIndex { get; set; }
        public int ManaCost { get; set; }
        public int DamageDealt { get; set; }
        public int ArmorGained { get; set; }
        public int HealAmount { get; set; }
        public int ManaGained { get; set; }
        public int MaxHealthGained { get; set; }
        public int JadeGained { get; set; }
        public IReadOnlyList<CombatStatChange> StatChanges { get; set; }
        /// <summary>True only when this play committed a Muddle effect, including an empty roster.</summary>
        public bool RandomizedCrawlerTriggerColors { get; internal set; }
        /// <summary>Detached, ordered results of this play only; presentation never samples again.</summary>
        public IReadOnlyList<CrawlerTriggerColorChange> CrawlerTriggerColorChanges { get; internal set; }
            = System.Array.Empty<CrawlerTriggerColorChange>();
        /// <summary>Ordered actual hand-cost writes from this play only;
        /// zero changes are omitted and presentation must never resample them.</summary>
        public IReadOnlyList<HandCardCostChange> HandCostChanges { get; internal set; }
            = System.Array.Empty<HandCardCostChange>();
        public IReadOnlyList<CombatCreatedCardReceipt> CreatedCards { get; set; }
            = System.Array.Empty<CombatCreatedCardReceipt>();
        /// <summary>Only residents that actually gained Duration, captured at the gain boundary.</summary>
        public IReadOnlyList<CrawlerDurationChange> CrawlerDurationChanges { get; set; }
            = System.Array.Empty<CrawlerDurationChange>();
        /// <summary>Actual one-use reserve gained with no resident recipient; never a player baseline stat.</summary>
        public int DurationReserveGained { get; set; }
        public int ResultingDurationReserve { get; set; }
        public bool Comboed { get; set; }
        /// <summary>
        /// Actual cost-sequence mismatch, captured before post-play seals clear
        /// the chain. Seal clearing and turn resets are not sequence breaks.
        /// </summary>
        public bool ChainWasBroken { get; internal set; }
        public int ComboCount { get; set; }
        public bool IsCritical { get; set; }
        public bool EnemyKilled { get; set; }
        public bool DestroyedOnUse { get; set; }
        /// <summary>Actual completed return, not a fresh draw or an inferred seal tooltip.</summary>
        public bool ReturnedToHand { get; internal set; }
    }
}
