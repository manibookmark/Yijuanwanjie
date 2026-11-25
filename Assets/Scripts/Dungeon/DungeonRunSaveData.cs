using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// DungeonRunSession 的序列化边界。这里仅保存稳定 ID 与纯数据，
    /// 不把运行时 Unity 对象泄漏进存档协议。
    /// </summary>
    [Serializable]
    internal sealed class DungeonRunSaveData
    {
        public int schemaVersion;
        // v41 separates repairing a Mana platform from collecting its released Mana.
        public bool hasManaRepairPickupState;
        public RunManaRepairPickupSaveData manaRepairPickups;
        // v40 gives free project invitation its own journal, separate from native Hire.
        public bool hasInvitationState;
        public string invitationStateJson;
        // v39 gives each remaining interaction its own closed durable journal.
        public bool hasDuplicateOfferingState;
        public string duplicateOfferingStateJson;
        public bool hasGunForHireState;
        public string gunForHireStateJson;
        // v37 independently freezes CardStat Offering and its exact-card/durable reward journal.
        public bool hasCardStatOfferingState;
        public string cardStatOfferingStateJson;
        // v35 owns additional durable modifiers and the one-use revival reserve.
        public bool hasAdditionalStatState;
        public DungeonRunAdditionalStatsSaveData additionalStats;
        public bool hasRevivalReserve;
        public DungeonRunRevivalReserveSaveData revivalReserve;
        // v33 freezes Gem offers and their exact-card/durable reward journal independently.
        public bool hasGemChestState;
        public string gemChestStateJson;
        // v32 records actual XP collections independently of remaining XP and card metrics.
        public bool hasExperienceCollectionsState;
        public DungeonRunExperienceCollectionSaveData experienceCollections;
        // v31 owns partial Crystal lifecycle and its exact hit payout proofs.
        public bool hasCrystalState;
        public DungeonRunCrystalSaveData crystalEvents;
        // v30 binds complete source qualification/attempt/placement to frozen directories.
        public bool hasSideEventSourceState;
        public string sideEventSourceStateJson;
        public string runId;
        // v22 freezes entry leader independently of used/summoned crawlers.
        public bool hasLeaderCrawlerState;
        public string leaderCrawlerContentId;
        public string dungeonContentId;
        public string firstFloorContentId;
        public string currentFloorContentId;
        public DungeonRunExplorationSaveData exploration;
        public DungeonRunFormationSaveData formations;
        public int currentLevel;
        public int currentExperience;
        public int xpFractionCarryUnits;
        public int runLuckBonus;
        public int runGreedBonus;
        public int runGrowthBonus;
        public int runJadeEarned;
        // v18 adds an explicit presence bit. Unity inline deserialization may
        // manufacture an empty object for missing data; it is not proof of a
        // frozen entry decision. v1-v17 migrate to disabled without an account.
        public bool hasCardPlayGrowthState;
        public DungeonRunCardPlayGrowthSaveData cardPlayGrowth;
        // v20 saves the real-play/world-fact owner explicitly. Earlier payloads
        // start empty; summary statistics are never a substitute for these facts.
        public bool hasCardUnlockFactsState;
        public DungeonRunCardUnlockFactsSaveData cardUnlockFacts;
        // v21 persists one-use Duration separately from the player's baseline.
        // Presence is explicit so a malformed new checkpoint cannot become zero.
        public bool hasCrawlerDurationReserveState;
        public int pendingCrawlerDurationBonus;
        public int elapsedRunSeconds;
        public bool dungeonBonusGranted;
        public int dungeonBonusJade;
        // v19 distinguishes a missing legacy receipt from a saved zero bonus.
        // Older concluded Runs retain their original frozen result; restoring
        // them must not retroactively grant newly configured floor income.
        public bool hasLevelBonusState;
        public bool levelBonusGranted;
        public int levelBonusJade;
        public int baseManaBonus;
        public int baseArmorPerTurnBonus;
        public int mightBonus;
        public int handLimitBonus;
        public int areaBonus;
        public int rewardRerollsRemaining;
        public bool hasPlayerState;
        public int maxHealth;
        public int currentHealth;
        public bool hasPlayerBaseline;
        public DungeonRunPlayerBaselineOrigin playerBaselineOrigin;
        public DungeonRunPlayerBaselineSaveData playerBaseline;
        public int pendingForcedOpeningHandCardInstanceId;
        public List<DungeonRunCardSaveData> cards =
            new List<DungeonRunCardSaveData>();
        public List<DungeonRunCrawlerResidenceSaveData> crawlerResidents =
            new List<DungeonRunCrawlerResidenceSaveData>();
        public List<string> usedCrawlerContentIds = new List<string>();
        public List<DungeonRunSealAssignmentSaveData> sealAssignments =
            new List<DungeonRunSealAssignmentSaveData>();
        public List<LevelRunSaveData> completedFloorStates =
            new List<LevelRunSaveData>();
        public LevelRunSaveData currentFloor;
        public DungeonRunStatisticsSaveData statistics =
            new DungeonRunStatisticsSaveData();
        public DungeonRunConclusionSaveData conclusion;
        public DungeonRunInteractionSaveData interaction;
        public DungeonRunDoubleKeyPaymentSaveData doubleKeyPayments;
        // v23 explicitly stores in-run Arcana; older Runs migrate to empty, never account-derived.
        public bool hasArcanaState;
        public DungeonRunArcanaSaveData arcana;
        // v25 explicit exact-card Offering state; legacy Runs migrate empty, never inferred from Meta.
        public bool hasChickenOfferingState;
        // Historical wire name retained: one typed Chicken/Mana Offering ledger, envelope v2.
        public DungeonRunChickenOfferingSaveData chickenOffering;
        // v26 explicit map-only Mana balance and collision/pickup journal.
        public bool hasManaState;
        public int mapCurrentMana;
        public DungeonRunManaStateOrigin manaStateOrigin;
        public bool hasManaStatueState;
        public DungeonRunManaStatueSaveData manaStatues;
        public bool hasAbandonedCartState;
        public DungeonRunCartSaveData abandonedCarts;
    }

    internal static class DungeonRunCrawlerDurationReserveSaveData
    {
        internal static int Read(DungeonRunSaveData data, string rawPayload)
        {
            if (data.schemaVersion < 21) return 0;
            if (!data.hasCrawlerDurationReserveState ||
                !JsonObjectShape.TryGetField(rawPayload, "pendingCrawlerDurationBonus", out string rawBonus) ||
                !int.TryParse(rawBonus, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int bonus) ||
                bonus != data.pendingCrawlerDurationBonus)
                throw new InvalidOperationException("Run 存档缺少有效的单次凝神储备状态。");
            CrawlerDurationReserve.RequireValid(bonus);
            return bonus;
        }
    }

    [Serializable]
    internal sealed class DungeonRunCardUnlockFactsSaveData
    {
        public int version;
        // The raw payload is also checked: Unity may manufacture [] for a missing
        // collection even when this field has no initializer.
        public List<DungeonRunCardUnlockFactSaveData> counts;

        internal static DungeonRunCardUnlockFactsSaveData Capture(RunCardUnlockFacts owner)
        {
            var saved = new DungeonRunCardUnlockFactsSaveData
            {
                version = 1,
                counts = new List<DungeonRunCardUnlockFactSaveData>()
            };
            foreach (CardUnlockCount fact in owner.Capture().Counts)
                saved.counts.Add(new DungeonRunCardUnlockFactSaveData
                {
                    metric = fact.Metric, sourceContentId = fact.SourceContentId, count = fact.Count
                });
            return saved;
        }

        internal static CardUnlockFactsSnapshot Read(DungeonRunSaveData data, string rawPayload = null)
        {
            if (data.schemaVersion < 20) return CardUnlockFactsSnapshot.Empty;
            DungeonRunCardUnlockFactsSaveData saved = data.cardUnlockFacts;
            if (!data.hasCardUnlockFactsState || saved == null || saved.version != 1 || saved.counts == null)
                throw new InvalidOperationException("Run 存档缺少受支持的行为解锁事实状态。");
            if (rawPayload != null &&
                (!JsonObjectShape.TryGetField(rawPayload, "cardUnlockFacts", out string rawFacts) ||
                 !JsonObjectShape.TryGetField(rawFacts, "counts", out string rawCounts) ||
                 !JsonObjectShape.TryGetArrayItems(rawCounts, out _)))
                throw new InvalidOperationException("Run 行为解锁事实必须显式保存 counts 数组。");
            var counts = new List<CardUnlockCount>();
            foreach (DungeonRunCardUnlockFactSaveData fact in saved.counts)
            {
                if (fact == null) throw new InvalidOperationException("Run 行为解锁事实不能包含空条目。");
                counts.Add(new CardUnlockCount(fact.metric, fact.sourceContentId, fact.count));
            }
            // The Core owner validates metric/source identity, nonnegative counts,
            // and duplicate keys before any live Run owner is replaced.
            return new CardUnlockFactsSnapshot(counts);
        }
    }

    [Serializable]
    internal sealed class DungeonRunCardUnlockFactSaveData
    {
        public CardUnlockMetric metric;
        public string sourceContentId;
        public int count;
    }

    [Serializable]
    internal sealed class DungeonRunCardPlayGrowthSaveData
    {
        public int version;
        public bool enabled;
        public long totalRealPlays;
        public int settledCycles;
        public int baseManaBonus;

        internal static DungeonRunCardPlayGrowthSaveData Capture(RunCardPlayGrowth owner)
            => Capture(owner.Capture());

        internal static DungeonRunCardPlayGrowthSaveData Capture(RunCardPlayGrowthSnapshot state)
            => new DungeonRunCardPlayGrowthSaveData
            {
                version = 1, enabled = state.Enabled, totalRealPlays = state.TotalRealPlays,
                settledCycles = state.SettledCycles, baseManaBonus = state.BaseManaBonus
            };

        internal static RunCardPlayGrowthSnapshot Read(DungeonRunSaveData data)
        {
            if (data.schemaVersion < 18) return default;
            DungeonRunCardPlayGrowthSaveData saved = data.cardPlayGrowth;
            if (!data.hasCardPlayGrowthState || saved == null || saved.version != 1)
                throw new InvalidOperationException("Run 存档缺少受支持的百术周天冻结状态。");
            return new RunCardPlayGrowthSnapshot(saved.enabled, saved.totalRealPlays,
                saved.settledCycles, saved.baseManaBonus);
        }
    }

    /// <summary>
    /// 仅记录最初入口的静态属性。armor 不属于 Run；明悟等 Run 增量另存，
    /// yinJuan 等字段也只取入口值，绝不从战斗结束的临时 PlayerStats 回写。
    /// </summary>
    [Serializable]
    internal sealed class DungeonRunPlayerBaselineSaveData
    {
        public int baseArmorPerTurn;
        public int fengRui;
        public int amount;
        public int bengLie;
        public int ningShen;
        public int shouPai;
        public int yinJuan;
        public int chouPai;
        public int tianYun;
        public int lianYu;
        public int mingWu;
        public int hunHun;
        public int faLi;
        public int zhaoSha;
        public int recovery;

        internal DungeonRunPlayerBaselineSaveData Clone()
            => (DungeonRunPlayerBaselineSaveData)MemberwiseClone();

        internal static DungeonRunPlayerBaselineSaveData Capture(PlayerStats player)
            => new DungeonRunPlayerBaselineSaveData
            {
                baseArmorPerTurn = player.baseArmorPerTurn,
                fengRui = player.fengRui, amount = player.amount, bengLie = player.bengLie,
                ningShen = player.ningShen, shouPai = player.shouPai, yinJuan = player.yinJuan,
                chouPai = player.chouPai, tianYun = player.tianYun, lianYu = player.lianYu,
                mingWu = player.mingWu, hunHun = player.hunHun, faLi = player.faLi,
                zhaoSha = player.zhaoSha, recovery = player.recovery
            };

        internal void ApplyTo(PlayerStats player)
        {
            player.baseArmorPerTurn = baseArmorPerTurn;
            player.fengRui = fengRui; player.amount = amount; player.bengLie = bengLie;
            player.ningShen = ningShen; player.shouPai = shouPai; player.yinJuan = yinJuan;
            player.chouPai = chouPai; player.tianYun = tianYun; player.lianYu = lianYu;
            player.mingWu = mingWu; player.hunHun = hunHun; player.faLi = faLi;
            player.zhaoSha = zhaoSha; player.recovery = recovery;
        }
    }

    [Serializable]
    internal sealed class DungeonRunCardSaveData
    {
        public string contentId;
        public int runtimeInstanceId;
        public int limitBreakCount;
        public bool hasGemSlotCapacity;
        public int gemSlotCapacity;
        public List<CardSealSpec> seals = new List<CardSealSpec>();
        // v38: explicit provenance plus complete frozen base/current author documents.
        // LegacyUnknown uses explicit empty carriers, never today-derived author claims.
        public int lineageVersion;
        public int lineageState;
        public string baseAuthorJson;
        public string currentAuthorJson;
    }

    [Serializable]
    internal sealed class DungeonRunCrawlerResidenceSaveData
    {
        public string contentId;
        public int remainingTriggers;
    }

    internal enum DungeonRunSealAssignmentDisposition
    {
        Installed = 0,
        EvolutionConsumed = 1
    }

    [Serializable]
    internal sealed class DungeonRunSealAssignmentSaveData
    {
        public string effectId;
        public string sealContentId;
        public int cardRuntimeInstanceId;
        public int slotIndex;
        public DungeonRunSealAssignmentDisposition disposition;
    }

    [Serializable]
    internal sealed class DungeonRunStatisticsSaveData
    {
        public int enemiesDefeated;
        public int maxCombo;
        public int totalHealing;
        public int freeCardsPlayed;
        public int crawlerActionsTriggered;
        public List<DungeonRunCardStatisticsSaveData> cards =
            new List<DungeonRunCardStatisticsSaveData>();
    }

    [Serializable]
    internal sealed class DungeonRunCardStatisticsSaveData
    {
        public string contentId;
        public int timesPlayed;
        public int damageDealt;
    }

    [Serializable]
    internal sealed class DungeonRunConclusionSaveData
    {
        public bool hasResult;
        public DungeonRunResultStatus status;
        public string runId;
        public string dungeonContentId;
        public string dungeonDisplayName;
        public string floorContentId;
        public string floorDisplayName;
        public int floorReached;
        public int floorsCleared;
        public int encounterCount;
        public int victoryCount;
        public int levelReached;
        public int currentExperience;
        public int runJadeEarned;
        public int elapsedRunSeconds;
        public int dungeonBonusJade;
        public int levelBonusJade;
        public bool hasCardPlayGrowthState;
        public DungeonRunCardPlayGrowthSaveData cardPlayGrowth;
        public int baseManaBonus;
        public int baseArmorPerTurnBonus;
        public int mightBonus;
        public int handLimitBonus;
        public int areaBonus;
        public int runLuckBonus;
        public int runGreedBonus;
        public int runGrowthBonus;
        public int currentHealth;
        public int maxHealth;
        public int deckCardCount;
        public int enemiesDefeated;
        public int maxCombo;
        public int totalHealing;
        public int freeCardsPlayed;
        public int crawlerActionsTriggered;
        public List<string> crawlerContentIds = new List<string>();
        public List<DungeonRunResultCardSaveData> cards =
            new List<DungeonRunResultCardSaveData>();
        public List<DungeonRunResultRewardSaveData> rewards =
            new List<DungeonRunResultRewardSaveData>();
    }

    [Serializable]
    internal sealed class DungeonRunResultCardSaveData
    {
        public string contentId;
        public int copies;
        public int highestLimitBreak;
        public int sealCount;
        public int timesPlayed;
        public int damageDealt;
    }

    [Serializable]
    internal sealed class DungeonRunResultRewardSaveData
    {
        public string grantContentId;
        public LevelRewardKind kind;
        public DungeonRunRewardCategory category;
        public string targetContentId;
        public string displayName;
        public bool newlyGranted;
    }
}
