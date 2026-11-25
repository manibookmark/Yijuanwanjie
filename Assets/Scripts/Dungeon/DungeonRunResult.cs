using System;
using System.Collections.Generic;
using WanjieScroll.Core;
using System.Collections.ObjectModel;

namespace WanjieScroll.Dungeon
{
    /// <summary>一次完整 Dungeon Run 的终局状态。</summary>
    public enum DungeonRunResultStatus
    {
        Victory = 1,
        Defeat = 2
    }

    /// <summary>结果页可以向流程层表达的玩家意图。</summary>
    public enum DungeonRunResultAction
    {
        ReturnToHub = 1,
        RetryDungeon = 2,
        ContinueDungeon = 3
    }

    /// <summary>最终牌组中同一稳定卡牌内容的只读聚合。</summary>
    public readonly struct DungeonRunCardResult
    {
        public string ContentId { get; }
        public int Copies { get; }
        public int HighestLimitBreak { get; }
        public int SealCount { get; }
        public int TimesPlayed { get; }
        public int DamageDealt { get; }
        public float AverageDamage => TimesPlayed > 0
            ? (float)DamageDealt / TimesPlayed
            : 0f;

        public DungeonRunCardResult(
            string contentId,
            int copies,
            int highestLimitBreak,
            int sealCount)
            : this(
                contentId,
                copies,
                highestLimitBreak,
                sealCount,
                timesPlayed: 0,
                damageDealt: 0)
        {
        }

        public DungeonRunCardResult(
            string contentId,
            int copies,
            int highestLimitBreak,
            int sealCount,
            int timesPlayed,
            int damageDealt)
        {
            ContentId = contentId ?? string.Empty;
            Copies = Math.Max(0, copies);
            HighestLimitBreak = Math.Max(0, highestLimitBreak);
            SealCount = Math.Max(0, sealCount);
            TimesPlayed = Math.Max(0, timesPlayed);
            DamageDealt = Math.Max(0, damageDealt);
        }
    }

    /// <summary>结果页解锁条目的产品类别；UI 不从名称或奖励 Kind 猜测。</summary>
    public enum DungeonRunRewardCategory
    {
        Crawler = 0,
        Facility = 1,
        Dungeon = 2,
        Relic = 3,
        Arcana = 4,
        PowerUp = 5
    }

    /// <summary>终局产生的永久奖励或解锁回执。</summary>
    public readonly struct DungeonRunRewardResult
    {
        public string GrantContentId { get; }
        public LevelRewardKind Kind { get; }
        public DungeonRunRewardCategory Category { get; }
        public string TargetContentId { get; }
        public string DisplayName { get; }
        public bool NewlyGranted { get; }

        public DungeonRunRewardResult(
            string grantContentId,
            LevelRewardKind kind,
            string targetContentId,
            string displayName,
            bool newlyGranted)
        {
            GrantContentId = grantContentId ?? string.Empty;
            Kind = kind;
            Category = kind switch
            {
                LevelRewardKind.CrawlerUnlock => DungeonRunRewardCategory.Crawler,
                LevelRewardKind.RelicUnlock => DungeonRunRewardCategory.Relic,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    "Only permanent direct rewards can appear in a Run conclusion.")
            };
            TargetContentId = targetContentId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            NewlyGranted = newlyGranted;
        }

        public DungeonRunRewardResult(
            string grantContentId,
            DungeonRunRewardCategory category,
            string targetContentId,
            string displayName,
            bool newlyGranted)
        {
            GrantContentId = grantContentId ?? string.Empty;
            if (!Enum.IsDefined(typeof(DungeonRunRewardCategory), category))
                throw new ArgumentOutOfRangeException(nameof(category));
            // LevelRewardKind describes direct node rewards. The four derived
            // completion categories have no direct Kind and retain the existing
            // CrawlerUnlock placeholder; Category is authoritative for them.
            Kind = category == DungeonRunRewardCategory.Relic
                ? LevelRewardKind.RelicUnlock
                : LevelRewardKind.CrawlerUnlock;
            Category = category;
            TargetContentId = targetContentId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            NewlyGranted = newlyGranted;
        }
    }

    /// <summary>
    /// Run 结束时与统计页直接相关的最终状态。关内收集灵玉、终层基础灵玉
    /// 与 Dungeon Bonus 分账；出战英灵和最终增益在冻结后不再依赖场景对象。
    /// </summary>
    public sealed class DungeonRunFinalStateSnapshot
    {
        public int ElapsedRunSeconds { get; }
        public int CollectedJade { get; }
        public int DungeonBonusJade { get; }
        public int LevelBonusJade { get; }
        public int BaseManaBonus { get; }
        public int BaseArmorPerTurnBonus { get; }
        public int MightBonus { get; }
        public int HandLimitBonus { get; }
        public int AreaBonus { get; }
        public int RunLuckBonus { get; }
        public int RunGreedBonus { get; }
        public int RunGrowthBonus { get; }
        public RunCardPlayGrowthSnapshot CardPlayGrowth { get; }
        public bool CardPlayGrowthEnabled => CardPlayGrowth.Enabled;
        public long CardPlayGrowthTotalRealPlays => CardPlayGrowth.TotalRealPlays;
        public int CardPlayGrowthBaseManaBonus => CardPlayGrowth.BaseManaBonus;
        public IReadOnlyList<string> CrawlerContentIds { get; }

        public DungeonRunFinalStateSnapshot(
            int elapsedRunSeconds,
            int collectedJade,
            int dungeonBonusJade,
            int baseManaBonus,
            int baseArmorPerTurnBonus,
            int mightBonus,
            int runLuckBonus,
            int runGreedBonus,
            int runGrowthBonus,
            IReadOnlyList<string> crawlerContentIds,
            int handLimitBonus = 0,
            int areaBonus = 0,
            int levelBonusJade = 0,
            RunCardPlayGrowthSnapshot cardPlayGrowth = default)
        {
            ElapsedRunSeconds = Math.Max(0, elapsedRunSeconds);
            CollectedJade = Math.Max(0, collectedJade);
            DungeonBonusJade = Math.Max(0, dungeonBonusJade);
            LevelBonusJade = Math.Max(0, levelBonusJade);
            BaseManaBonus = Math.Max(0, baseManaBonus);
            BaseArmorPerTurnBonus = Math.Max(0, baseArmorPerTurnBonus);
            MightBonus = Math.Max(0, mightBonus);
            HandLimitBonus = Math.Max(0, handLimitBonus);
            AreaBonus = Math.Max(0, areaBonus);
            RunLuckBonus = Math.Max(0, runLuckBonus);
            RunGreedBonus = Math.Max(0, runGreedBonus);
            RunGrowthBonus = Math.Max(0, runGrowthBonus);
            CardPlayGrowth = ValidateCardPlayGrowth(cardPlayGrowth);
            CrawlerContentIds = CopyStableIds(crawlerContentIds);
        }

        private static RunCardPlayGrowthSnapshot ValidateCardPlayGrowth(
            RunCardPlayGrowthSnapshot state)
        {
            // Core validates its own source. A frozen result is not a pending
            // checkpoint: it must preserve the actually paid bonus and cannot
            // repair an unpaid cycle or credit mana from the play count alone.
            var validator = new RunCardPlayGrowth(state.Enabled);
            validator.Restore(state);
            RunCardPlayGrowthSnapshot validated = validator.Capture();
            if (validated.SettledCycles != state.SettledCycles ||
                validated.BaseManaBonus != state.BaseManaBonus)
                throw new InvalidOperationException("Run 结算百术周天快照含未结算的周期。");
            return state;
        }

        private static IReadOnlyList<string> CopyStableIds(
            IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<string>();
            var ids = new List<string>(source.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                string id = source[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                    ids.Add(id);
            }
            if (ids.Count == 0)
                return Array.Empty<string>();
            return new ReadOnlyCollection<string>(ids);
        }
    }

    /// <summary>
    /// 整次 Dungeon Run 的不可变终局快照。过程统计来自 Core 与 Dungeon 的
    /// 类型化账本；用时等尚无权威生命周期口径的字段不会从日志或场景状态伪造。
    /// </summary>
    public sealed class DungeonRunResultSnapshot
    {
        public DungeonRunResultStatus Status { get; }
        public string RunId { get; }
        public string DungeonContentId { get; }
        public string DungeonDisplayName { get; }
        public string FloorContentId { get; }
        public string FloorDisplayName { get; }
        public int FloorReached { get; }
        public int FloorsCleared { get; }
        public int EncounterCount { get; }
        public int VictoryCount { get; }
        public int LevelReached { get; }
        public int CurrentExperience { get; }
        public DungeonRunFinalStateSnapshot FinalState { get; }
        public int ElapsedRunSeconds => FinalState.ElapsedRunSeconds;
        public int CollectedJade => FinalState.CollectedJade;
        public int DungeonBonusJade => FinalState.DungeonBonusJade;
        public int LevelBonusJade => FinalState.LevelBonusJade;
        /// <summary>兼容旧调用；语义已明确为关内收集灵玉。</summary>
        public int RunJadeEarned => FinalState.CollectedJade;
        public int BaseManaBonus => FinalState.BaseManaBonus;
        public int BaseArmorPerTurnBonus => FinalState.BaseArmorPerTurnBonus;
        public int MightBonus => FinalState.MightBonus;
        public int HandLimitBonus => FinalState.HandLimitBonus;
        public int AreaBonus => FinalState.AreaBonus;
        public int RunLuckBonus => FinalState.RunLuckBonus;
        public int RunGreedBonus => FinalState.RunGreedBonus;
        public int RunGrowthBonus => FinalState.RunGrowthBonus;
        public RunCardPlayGrowthSnapshot CardPlayGrowth => FinalState.CardPlayGrowth;
        public bool CardPlayGrowthEnabled => FinalState.CardPlayGrowthEnabled;
        public long CardPlayGrowthTotalRealPlays => FinalState.CardPlayGrowthTotalRealPlays;
        public int CardPlayGrowthBaseManaBonus => FinalState.CardPlayGrowthBaseManaBonus;
        public IReadOnlyList<string> CrawlerContentIds =>
            FinalState.CrawlerContentIds;
        public int CurrentHealth { get; }
        public int MaxHealth { get; }
        public int EnemiesDefeated { get; }
        public int MaxCombo { get; }
        public int TotalHealing { get; }
        public int FreeCardsPlayed { get; }
        public int CrawlerActionsTriggered { get; }
        public int DeckCardCount { get; }
        public IReadOnlyList<DungeonRunCardResult> Cards { get; }
        public IReadOnlyList<DungeonRunRewardResult> Rewards { get; }

        internal DungeonRunResultSnapshot(
            DungeonRunResultStatus status,
            string runId,
            string dungeonContentId,
            string dungeonDisplayName,
            string floorContentId,
            string floorDisplayName,
            int floorReached,
            int floorsCleared,
            int encounterCount,
            int victoryCount,
            int levelReached,
            int currentExperience,
            int runJadeEarned,
            int currentHealth,
            int maxHealth,
            int enemiesDefeated,
            int maxCombo,
            int totalHealing,
            int freeCardsPlayed,
            int crawlerActionsTriggered,
            int deckCardCount,
            IReadOnlyList<DungeonRunCardResult> cards,
            IReadOnlyList<DungeonRunRewardResult> rewards)
            : this(
                status,
                runId,
                dungeonContentId,
                dungeonDisplayName,
                floorContentId,
                floorDisplayName,
                floorReached,
                floorsCleared,
                encounterCount,
                victoryCount,
                levelReached,
                currentExperience,
                new DungeonRunFinalStateSnapshot(
                    0,
                    runJadeEarned,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<string>()),
                currentHealth,
                maxHealth,
                enemiesDefeated,
                maxCombo,
                totalHealing,
                freeCardsPlayed,
                crawlerActionsTriggered,
                deckCardCount,
                cards,
                rewards)
        {
        }

        internal DungeonRunResultSnapshot(
            DungeonRunResultStatus status,
            string runId,
            string dungeonContentId,
            string dungeonDisplayName,
            string floorContentId,
            string floorDisplayName,
            int floorReached,
            int floorsCleared,
            int encounterCount,
            int victoryCount,
            int levelReached,
            int currentExperience,
            DungeonRunFinalStateSnapshot finalState,
            int currentHealth,
            int maxHealth,
            int enemiesDefeated,
            int maxCombo,
            int totalHealing,
            int freeCardsPlayed,
            int crawlerActionsTriggered,
            int deckCardCount,
            IReadOnlyList<DungeonRunCardResult> cards,
            IReadOnlyList<DungeonRunRewardResult> rewards)
        {
            Status = status;
            RunId = runId ?? string.Empty;
            DungeonContentId = dungeonContentId ?? string.Empty;
            DungeonDisplayName = dungeonDisplayName ?? string.Empty;
            FloorContentId = floorContentId ?? string.Empty;
            FloorDisplayName = floorDisplayName ?? string.Empty;
            FloorReached = Math.Max(1, floorReached);
            FloorsCleared = Math.Max(0, floorsCleared);
            EncounterCount = Math.Max(0, encounterCount);
            VictoryCount = Math.Max(0, victoryCount);
            LevelReached = Math.Max(1, levelReached);
            CurrentExperience = Math.Max(0, currentExperience);
            FinalState = finalState ?? new DungeonRunFinalStateSnapshot(
                0, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<string>());
            MaxHealth = Math.Max(1, maxHealth);
            CurrentHealth = Math.Max(0, Math.Min(MaxHealth, currentHealth));
            EnemiesDefeated = Math.Max(0, enemiesDefeated);
            MaxCombo = Math.Max(0, maxCombo);
            TotalHealing = Math.Max(0, totalHealing);
            FreeCardsPlayed = Math.Max(0, freeCardsPlayed);
            CrawlerActionsTriggered = Math.Max(
                0,
                crawlerActionsTriggered);
            DeckCardCount = Math.Max(0, deckCardCount);
            Cards = CopyCards(cards);
            Rewards = CopyRewards(rewards);
        }

        private static IReadOnlyList<DungeonRunCardResult> CopyCards(
            IReadOnlyList<DungeonRunCardResult> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<DungeonRunCardResult>();
            var copy = new DungeonRunCardResult[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return new ReadOnlyCollection<DungeonRunCardResult>(copy);
        }

        private static IReadOnlyList<DungeonRunRewardResult> CopyRewards(
            IReadOnlyList<DungeonRunRewardResult> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<DungeonRunRewardResult>();
            var copy = new DungeonRunRewardResult[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return new ReadOnlyCollection<DungeonRunRewardResult>(copy);
        }
    }

    /// <summary>
    /// Run 终局知识的唯一所有者：它把跨层计数、最终构筑和奖励冻结为一次性快照，
    /// 并保证重复进入结果页时返回同一份结果。UI 与 Bootstrap 不解析 Run 存档。
    /// </summary>
    internal sealed class DungeonRunConclusion
    {
        private DungeonRunResultSnapshot _snapshot;

        internal bool TryGetSnapshot(out DungeonRunResultSnapshot snapshot)
        {
            snapshot = _snapshot;
            return snapshot != null;
        }

        internal bool TryFreeze(
            DungeonRunResultStatus status,
            string runId,
            string dungeonContentId,
            string dungeonDisplayName,
            LevelRunProfile currentProfile,
            LevelRunSession currentFloor,
            IReadOnlyList<LevelRunSaveData> completedFloors,
            IReadOnlyList<DungeonRunCardSaveData> cards,
            int currentHealth,
            int maxHealth,
            int levelReached,
            int currentExperience,
            int runJadeEarned,
            IReadOnlyList<LevelRewardReceipt> rewards,
            out DungeonRunResultSnapshot snapshot,
            out string error)
        {
            return TryFreeze(
                status,
                runId,
                dungeonContentId,
                dungeonDisplayName,
                currentProfile,
                currentFloor,
                completedFloors,
                cards,
                currentHealth,
                maxHealth,
                levelReached,
                currentExperience,
                runJadeEarned,
                new DungeonRunStatistics().Capture(),
                CopyRewards(rewards),
                out snapshot,
                out error);
        }

        internal bool TryFreeze(
            DungeonRunResultStatus status,
            string runId,
            string dungeonContentId,
            string dungeonDisplayName,
            LevelRunProfile currentProfile,
            LevelRunSession currentFloor,
            IReadOnlyList<LevelRunSaveData> completedFloors,
            IReadOnlyList<DungeonRunCardSaveData> cards,
            int currentHealth,
            int maxHealth,
            int levelReached,
            int currentExperience,
            int runJadeEarned,
            DungeonRunStatisticsSnapshot statistics,
            IReadOnlyList<DungeonRunRewardResult> rewards,
            out DungeonRunResultSnapshot snapshot,
            out string error)
        {
            return TryFreeze(
                status,
                runId,
                dungeonContentId,
                dungeonDisplayName,
                currentProfile,
                currentFloor,
                completedFloors,
                cards,
                currentHealth,
                maxHealth,
                levelReached,
                currentExperience,
                new DungeonRunFinalStateSnapshot(
                    0,
                    runJadeEarned,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    Array.Empty<string>()),
                statistics,
                rewards,
                out snapshot,
                out error);
        }

        internal bool TryFreeze(
            DungeonRunResultStatus status,
            string runId,
            string dungeonContentId,
            string dungeonDisplayName,
            LevelRunProfile currentProfile,
            LevelRunSession currentFloor,
            IReadOnlyList<LevelRunSaveData> completedFloors,
            IReadOnlyList<DungeonRunCardSaveData> cards,
            int currentHealth,
            int maxHealth,
            int levelReached,
            int currentExperience,
            DungeonRunFinalStateSnapshot finalState,
            DungeonRunStatisticsSnapshot statistics,
            IReadOnlyList<DungeonRunRewardResult> rewards,
            out DungeonRunResultSnapshot snapshot,
            out string error)
        {
            if (_snapshot != null)
            {
                snapshot = _snapshot;
                if (_snapshot.Status == status)
                {
                    error = null;
                    return true;
                }
                error = $"Run 已按 {_snapshot.Status} 冻结，不能改为 {status}。";
                return false;
            }
            if (currentProfile == null || currentFloor == null)
            {
                snapshot = null;
                error = "缺少当前楼层，无法冻结 Run 结果。";
                return false;
            }
            if (!Enum.IsDefined(typeof(DungeonRunResultStatus), status))
            {
                snapshot = null;
                error = $"不支持的 Run 结果：{status}。";
                return false;
            }

            int encounterCount = currentFloor.EncounterCount;
            int victoryCount = currentFloor.VictoryCount;
            int completedFloorCount = completedFloors?.Count ?? 0;
            if (completedFloors != null)
            {
                for (int i = 0; i < completedFloors.Count; i++)
                {
                    LevelRunSaveData floor = completedFloors[i];
                    if (floor == null) continue;
                    encounterCount += Math.Max(0, floor.encounterCount);
                    victoryCount += Math.Max(0, floor.victoryCount);
                }
            }

            statistics ??= new DungeonRunStatistics().Capture();
            List<DungeonRunCardResult> cardResults = AggregateCards(
                cards,
                statistics.Cards);
            List<DungeonRunRewardResult> rewardResults =
                CopyRewardResults(rewards);
            int floorsCleared = completedFloorCount +
                (status == DungeonRunResultStatus.Victory ? 1 : 0);
            int floorReached = Math.Max(1, currentProfile.floorIndex);
            if (floorsCleared > floorReached)
            {
                snapshot = null;
                error = $"完成层数 {floorsCleared} 超过抵达层数 {floorReached}。";
                return false;
            }
            _snapshot = new DungeonRunResultSnapshot(
                status,
                runId,
                dungeonContentId,
                dungeonDisplayName,
                currentProfile.contentId,
                currentProfile.displayName,
                floorReached,
                floorsCleared,
                encounterCount,
                victoryCount,
                levelReached,
                currentExperience,
                finalState,
                currentHealth,
                maxHealth,
                statistics.EnemiesDefeated,
                statistics.MaxCombo,
                statistics.TotalHealing,
                statistics.FreeCardsPlayed,
                statistics.CrawlerActionsTriggered,
                cards?.Count ?? 0,
                cardResults,
                rewardResults);
            snapshot = _snapshot;
            error = null;
            return true;
        }

        internal DungeonRunConclusionSaveData CaptureSaveData()
        {
            if (_snapshot == null)
                return null;
            var data = new DungeonRunConclusionSaveData
            {
                hasResult = true,
                status = _snapshot.Status,
                runId = _snapshot.RunId,
                dungeonContentId = _snapshot.DungeonContentId,
                dungeonDisplayName = _snapshot.DungeonDisplayName,
                floorContentId = _snapshot.FloorContentId,
                floorDisplayName = _snapshot.FloorDisplayName,
                floorReached = _snapshot.FloorReached,
                floorsCleared = _snapshot.FloorsCleared,
                encounterCount = _snapshot.EncounterCount,
                victoryCount = _snapshot.VictoryCount,
                levelReached = _snapshot.LevelReached,
                currentExperience = _snapshot.CurrentExperience,
                runJadeEarned = _snapshot.RunJadeEarned,
                elapsedRunSeconds = _snapshot.ElapsedRunSeconds,
                dungeonBonusJade = _snapshot.DungeonBonusJade,
                levelBonusJade = _snapshot.LevelBonusJade,
                hasCardPlayGrowthState = true,
                cardPlayGrowth = DungeonRunCardPlayGrowthSaveData.Capture(_snapshot.CardPlayGrowth),
                baseManaBonus = _snapshot.BaseManaBonus,
                baseArmorPerTurnBonus = _snapshot.BaseArmorPerTurnBonus,
                mightBonus = _snapshot.MightBonus,
                handLimitBonus = _snapshot.HandLimitBonus,
                areaBonus = _snapshot.AreaBonus,
                runLuckBonus = _snapshot.RunLuckBonus,
                runGreedBonus = _snapshot.RunGreedBonus,
                runGrowthBonus = _snapshot.RunGrowthBonus,
                currentHealth = _snapshot.CurrentHealth,
                maxHealth = _snapshot.MaxHealth,
                deckCardCount = _snapshot.DeckCardCount,
                enemiesDefeated = _snapshot.EnemiesDefeated,
                maxCombo = _snapshot.MaxCombo,
                totalHealing = _snapshot.TotalHealing,
                freeCardsPlayed = _snapshot.FreeCardsPlayed,
                crawlerActionsTriggered =
                    _snapshot.CrawlerActionsTriggered
            };
            for (int i = 0; i < _snapshot.CrawlerContentIds.Count; i++)
                data.crawlerContentIds.Add(_snapshot.CrawlerContentIds[i]);
            for (int i = 0; i < _snapshot.Cards.Count; i++)
            {
                DungeonRunCardResult card = _snapshot.Cards[i];
                data.cards.Add(new DungeonRunResultCardSaveData
                {
                    contentId = card.ContentId,
                    copies = card.Copies,
                    highestLimitBreak = card.HighestLimitBreak,
                    sealCount = card.SealCount,
                    timesPlayed = card.TimesPlayed,
                    damageDealt = card.DamageDealt
                });
            }
            for (int i = 0; i < _snapshot.Rewards.Count; i++)
            {
                DungeonRunRewardResult reward = _snapshot.Rewards[i];
                data.rewards.Add(new DungeonRunResultRewardSaveData
                {
                    grantContentId = reward.GrantContentId,
                    kind = reward.Kind,
                    category = reward.Category,
                    targetContentId = reward.TargetContentId,
                    displayName = reward.DisplayName,
                    newlyGranted = reward.NewlyGranted
                });
            }
            return data;
        }

        internal static DungeonRunConclusion Restore(
            DungeonRunConclusionSaveData data)
        {
            var conclusion = new DungeonRunConclusion();
            if (data == null || !data.hasResult)
                return conclusion;
            if (!Enum.IsDefined(typeof(DungeonRunResultStatus), data.status))
                throw new InvalidOperationException(
                    $"Run 结算包含无效状态：{data.status}。");
            if (string.IsNullOrWhiteSpace(data.runId) ||
                string.IsNullOrWhiteSpace(data.dungeonContentId) ||
                string.IsNullOrWhiteSpace(data.floorContentId))
            {
                throw new InvalidOperationException(
                    "Run 结算缺少稳定 Run、Dungeon 或楼层 ID。");
            }
            if (data.floorReached < 1 || data.floorsCleared < 0 ||
                data.floorsCleared > data.floorReached ||
                data.encounterCount < 0 || data.victoryCount < 0 ||
                data.victoryCount > data.encounterCount ||
                data.levelReached < 1 || data.currentExperience < 0 ||
                data.runJadeEarned < 0 || data.elapsedRunSeconds < 0 ||
                data.dungeonBonusJade < 0 || data.levelBonusJade < 0 ||
                data.baseManaBonus < 0 ||
                data.baseArmorPerTurnBonus < 0 || data.mightBonus < 0 ||
                data.handLimitBonus < 0 || data.areaBonus < 0 ||
                data.runLuckBonus < 0 || data.runGreedBonus < 0 ||
                data.runGrowthBonus < 0 || data.maxHealth < 1 ||
                data.currentHealth < 0 ||
                data.currentHealth > data.maxHealth ||
                data.deckCardCount < 0 || data.enemiesDefeated < 0 ||
                data.maxCombo < 0 || data.totalHealing < 0 ||
                data.freeCardsPlayed < 0 ||
                data.crawlerActionsTriggered < 0)
            {
                throw new InvalidOperationException(
                    "Run 结算包含越界的统计字段。");
            }

            var cards = new List<DungeonRunCardResult>();
            var cardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int countedCards = 0;
            if (data.cards != null)
            {
                for (int i = 0; i < data.cards.Count; i++)
                {
                    DungeonRunResultCardSaveData card = data.cards[i] ??
                        throw new InvalidOperationException(
                            $"Run 结算第 {i} 个卡牌摘要为空。");
                    if (string.IsNullOrWhiteSpace(card.contentId) ||
                        !cardIds.Add(card.contentId) ||
                        card.copies <= 0 ||
                        card.highestLimitBreak < 0 ||
                        card.sealCount < 0 || card.timesPlayed < 0 ||
                        card.damageDealt < 0)
                    {
                        throw new InvalidOperationException(
                            $"Run 结算第 {i} 个卡牌摘要无效。");
                    }
                    cards.Add(new DungeonRunCardResult(
                        card.contentId,
                        card.copies,
                        card.highestLimitBreak,
                        card.sealCount,
                        card.timesPlayed,
                        card.damageDealt));
                    countedCards += card.copies;
                }
            }
            if (countedCards != data.deckCardCount)
                throw new InvalidOperationException(
                    "Run 结算的牌组总数与聚合条目不一致。");

            var rewards = new List<DungeonRunRewardResult>();
            var rewardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (data.rewards != null)
            {
                for (int i = 0; i < data.rewards.Count; i++)
                {
                    DungeonRunResultRewardSaveData reward = data.rewards[i] ??
                        throw new InvalidOperationException(
                            $"Run 结算第 {i} 个奖励摘要为空。");
                    if (string.IsNullOrWhiteSpace(reward.grantContentId) ||
                        !rewardIds.Add(reward.grantContentId) ||
                        string.IsNullOrWhiteSpace(reward.targetContentId) ||
                        !Enum.IsDefined(
                            typeof(DungeonRunRewardCategory),
                            reward.category))
                    {
                        throw new InvalidOperationException(
                            $"Run 结算第 {i} 个奖励摘要无效。");
                    }
                    var restored = new DungeonRunRewardResult(
                        reward.grantContentId,
                        reward.category,
                        reward.targetContentId,
                        reward.displayName,
                        reward.newlyGranted);
                    if (restored.Kind != reward.kind)
                        throw new InvalidOperationException(
                            $"Run 结算第 {i} 个奖励的类别与类型不一致。");
                    rewards.Add(restored);
                }
            }

            IReadOnlyList<string> crawlerContentIds =
                ValidateStableIds(
                    data.crawlerContentIds,
                    "Run 结算出战英灵");
            RunCardPlayGrowthSnapshot cardPlayGrowth = default;
            if (data.hasCardPlayGrowthState)
            {
                DungeonRunCardPlayGrowthSaveData saved = data.cardPlayGrowth;
                if (saved == null || saved.version != 1)
                    throw new InvalidOperationException("Run 结算缺少受支持的百术周天来源快照。");
                cardPlayGrowth = new RunCardPlayGrowthSnapshot(saved.enabled,
                    saved.totalRealPlays, saved.settledCycles, saved.baseManaBonus);
            }

            conclusion._snapshot = new DungeonRunResultSnapshot(
                data.status,
                data.runId,
                data.dungeonContentId,
                data.dungeonDisplayName,
                data.floorContentId,
                data.floorDisplayName,
                data.floorReached,
                data.floorsCleared,
                data.encounterCount,
                data.victoryCount,
                data.levelReached,
                data.currentExperience,
                new DungeonRunFinalStateSnapshot(
                    data.elapsedRunSeconds,
                    data.runJadeEarned,
                    data.dungeonBonusJade,
                    data.baseManaBonus,
                    data.baseArmorPerTurnBonus,
                    data.mightBonus,
                    data.runLuckBonus,
                    data.runGreedBonus,
                    data.runGrowthBonus,
                    crawlerContentIds,
                    data.handLimitBonus,
                    data.areaBonus,
                    data.levelBonusJade,
                    cardPlayGrowth),
                data.currentHealth,
                data.maxHealth,
                data.enemiesDefeated,
                data.maxCombo,
                data.totalHealing,
                data.freeCardsPlayed,
                data.crawlerActionsTriggered,
                data.deckCardCount,
                cards,
                rewards);
            return conclusion;
        }

        private static IReadOnlyList<string> ValidateStableIds(
            IReadOnlyList<string> source,
            string label)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<string>();
            var ids = new List<string>(source.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                string id = source[i]?.Trim();
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    throw new InvalidOperationException(
                        $"{label}第 {i} 个稳定 ID 为空或重复。");
                }
                ids.Add(id);
            }
            return ids;
        }

        private static List<DungeonRunCardResult> AggregateCards(
            IReadOnlyList<DungeonRunCardSaveData> cards,
            IReadOnlyList<DungeonRunCardStatistics> cardStatistics)
        {
            var aggregates = new Dictionary<string, MutableCardAggregate>(
                StringComparer.OrdinalIgnoreCase);
            if (cards != null)
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    DungeonRunCardSaveData card = cards[i];
                    if (card == null || string.IsNullOrWhiteSpace(card.contentId))
                        continue;
                    if (!aggregates.TryGetValue(
                            card.contentId, out MutableCardAggregate aggregate))
                    {
                        aggregate = new MutableCardAggregate(card.contentId);
                        aggregates.Add(card.contentId, aggregate);
                    }
                    aggregate.Copies++;
                    aggregate.HighestLimitBreak = Math.Max(
                        aggregate.HighestLimitBreak,
                        card.limitBreakCount);
                    aggregate.SealCount += card.seals?.Count ?? 0;
                }
            }

            if (cardStatistics != null)
            {
                for (int i = 0; i < cardStatistics.Count; i++)
                {
                    DungeonRunCardStatistics statistics = cardStatistics[i];
                    if (aggregates.TryGetValue(
                            statistics.ContentId,
                            out MutableCardAggregate aggregate))
                    {
                        aggregate.TimesPlayed = statistics.TimesPlayed;
                        aggregate.DamageDealt = statistics.DamageDealt;
                    }
                }
            }

            var ids = new List<string>(aggregates.Keys);
            ids.Sort(StringComparer.Ordinal);
            var results = new List<DungeonRunCardResult>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                MutableCardAggregate aggregate = aggregates[ids[i]];
                results.Add(new DungeonRunCardResult(
                    aggregate.ContentId,
                    aggregate.Copies,
                    aggregate.HighestLimitBreak,
                    aggregate.SealCount,
                    aggregate.TimesPlayed,
                    aggregate.DamageDealt));
            }
            return results;
        }

        private static List<DungeonRunRewardResult> CopyRewards(
            IReadOnlyList<LevelRewardReceipt> rewards)
        {
            var results = new List<DungeonRunRewardResult>(
                rewards?.Count ?? 0);
            if (rewards == null) return results;
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

        private static List<DungeonRunRewardResult> CopyRewardResults(
            IReadOnlyList<DungeonRunRewardResult> rewards)
        {
            var results = new List<DungeonRunRewardResult>(
                rewards?.Count ?? 0);
            if (rewards == null) return results;
            for (int i = 0; i < rewards.Count; i++)
                results.Add(rewards[i]);
            return results;
        }

        private sealed class MutableCardAggregate
        {
            internal MutableCardAggregate(string contentId)
            {
                ContentId = contentId;
            }

            internal string ContentId { get; }
            internal int Copies { get; set; }
            internal int HighestLimitBreak { get; set; }
            internal int SealCount { get; set; }
            internal int TimesPlayed { get; set; }
            internal int DamageDealt { get; set; }
        }
    }
}
