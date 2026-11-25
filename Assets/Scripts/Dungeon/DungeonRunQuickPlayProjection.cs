using System;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// The complete scalar state owned by an editor quick-play entry.  The
    /// Bootstrap module supplies a reviewed entry contract; this type keeps
    /// save-schema details out of that module and prevents a long list of
    /// loosely ordered scalar arguments at the call site.
    /// </summary>
    public sealed class DungeonRunQuickPlayEntryState
    {
        public int level;
        public int experience;
        public int xpFractionCarryUnits;
        public int jade;
        public int runLuck;
        public int runGreed;
        public int runGrowth;
        public int baseMana;
        public int baseArmor;
        public int might;
        public int area;
        public int hand;
        public int rewardRerolls;
        public int maxHealth;
        public int currentHealth;
        public int pendingForcedOpeningHandCardInstanceId;
        public int maxFixedCardInstanceId;
    }

    /// <summary>
    /// Builds an editor quick-play payload from a reviewed representative deck
    /// and a fresh target-floor snapshot. Save-schema details stay inside the
    /// Dungeon module; Bootstrap supplies only confirmed progression values.
    /// </summary>
    public static class DungeonRunQuickPlayProjection
    {
        public static bool TryCreate(
            string representativePayload,
            string freshTargetPayload,
            LevelRunProfile targetProfile,
            DungeonRunQuickPlayEntryState entry,
            out string projectedPayload,
            out string error)
        {
            projectedPayload = null;
            error = null;
            if (targetProfile == null)
            {
                error = "快速入口缺少目标楼层。";
                return false;
            }
            if (entry == null)
            {
                error = "快速入口缺少逐层初始化数据。";
                return false;
            }
            DungeonRunSaveData historical =
                JsonUtility.FromJson<DungeonRunSaveData>(representativePayload);
            DungeonRunSaveData fresh =
                JsonUtility.FromJson<DungeonRunSaveData>(freshTargetPayload);
            if (historical == null || fresh?.currentFloor == null)
            {
                error = "快速入口无法解析代表构筑或目标楼层初始状态。";
                return false;
            }

            DungeonRunSaveData data;
            try { data = DungeonRunQuickPlayLegacyEntry.Rebase(representativePayload, freshTargetPayload, targetProfile); }
            catch (Exception exception) { error = exception.Message; return false; }
            // Old assignment rows are used solely to trim this representative
            // physical deck. They do not become new map-completion receipts.
            var historicalAssignments = historical.sealAssignments ?? new List<DungeonRunSealAssignmentSaveData>();

            data.currentFloor = fresh.currentFloor;
            data.statistics = fresh.statistics ?? new DungeonRunStatisticsSaveData();
            data.conclusion = fresh.conclusion ?? new DungeonRunConclusionSaveData();
            data.interaction = fresh.interaction ?? new DungeonRunInteractionSaveData();
            data.doubleKeyPayments = fresh.doubleKeyPayments;
            data.runId = fresh.runId;
            data.dungeonContentId = targetProfile.dungeonContentId;
            data.firstFloorContentId = "level.xiyou.yingchoujian.v1";
            data.currentFloorContentId = targetProfile.contentId;
            data.currentLevel = Math.Max(1, entry.level);
            data.currentExperience = Math.Max(0, entry.experience);
            data.xpFractionCarryUnits = Math.Max(0, entry.xpFractionCarryUnits);
            data.runLuckBonus = Math.Max(0, entry.runLuck);
            data.runGreedBonus = Math.Max(0, entry.runGreed);
            data.runGrowthBonus = Math.Max(0, entry.runGrowth);
            data.runJadeEarned = Math.Max(0, entry.jade);
            data.baseManaBonus = Math.Max(0, entry.baseMana);
            data.baseArmorPerTurnBonus = Math.Max(0, entry.baseArmor);
            data.mightBonus = Math.Max(0, entry.might);
            data.areaBonus = Math.Max(0, entry.area);
            data.handLimitBonus = Math.Max(0, entry.hand);
            data.rewardRerollsRemaining = Math.Max(0, entry.rewardRerolls);
            data.elapsedRunSeconds = 0;
            data.dungeonBonusGranted = false;
            data.dungeonBonusJade = 0;
            data.hasPlayerState = true;
            data.maxHealth = Math.Max(1, entry.maxHealth);
            data.currentHealth = Math.Max(
                0,
                Math.Min(data.maxHealth, entry.currentHealth));
            data.pendingForcedOpeningHandCardInstanceId = Math.Max(
                0,
                entry.pendingForcedOpeningHandCardInstanceId);

            // A representative template is allowed to carry a future schema
            // field, but it must never silently become part of a reviewed
            // floor entry.  Quick-play entries start with a disabled card-play
            // growth ledger unless a future entry contract explicitly adds it.
            if (data.schemaVersion >= 18)
            {
                data.hasCardPlayGrowthState = true;
                data.cardPlayGrowth = new DungeonRunCardPlayGrowthSaveData
                {
                    version = 1,
                    enabled = false,
                    totalRealPlays = 0,
                    settledCycles = 0,
                    baseManaBonus = 0
                };
            }
            else
            {
                data.hasCardPlayGrowthState = false;
                data.cardPlayGrowth = null;
            }

            // The target-floor payload is the only source allowed to establish
            // a player baseline.  The representative deck must not carry an
            // account snapshot from a different editor session.
            data.hasPlayerBaseline = fresh.hasPlayerBaseline;
            data.playerBaselineOrigin = fresh.hasPlayerBaseline ? fresh.playerBaselineOrigin : DungeonRunPlayerBaselineOrigin.LegacyMissing;
            data.playerBaseline = fresh.playerBaseline?.Clone();

            // Today's fresh target owns all map/species/history/journal state.
            // Only physical legacy cards and their installed slots are trimmed;
            // the original route is not evidence for today's generated enemies.
            data.completedFloorStates ??= new List<LevelRunSaveData>();
            data.cards ??= new List<DungeonRunCardSaveData>();
            data.sealAssignments ??= new List<DungeonRunSealAssignmentSaveData>();
            data.crawlerResidents ??= new List<DungeonRunCrawlerResidenceSaveData>();
            data.usedCrawlerContentIds ??= new List<string>();
            data.completedFloorStates.RemoveAll(state =>
                ResolveG2FloorIndex(state?.profileContentId) >=
                    targetProfile.floorIndex);
            data.cards.RemoveAll(card =>
                card.runtimeInstanceId > entry.maxFixedCardInstanceId &&
                card.runtimeInstanceId < 20);
            var retainedCardIds = new HashSet<int>();
            for (int i = 0; i < data.cards.Count; i++)
                retainedCardIds.Add(data.cards[i].runtimeInstanceId);
            historicalAssignments.RemoveAll(assignment =>
                !retainedCardIds.Contains(assignment.cardRuntimeInstanceId) ||
                ResolveG2FloorIndex(assignment.effectId) >=
                    targetProfile.floorIndex);
            for (int i = 0; i < data.cards.Count; i++)
            {
                DungeonRunCardSaveData card = data.cards[i];
                if (card.seals == null)
                    continue;
                card.seals.RemoveAll(seal => !historicalAssignments.Exists(
                    assignment => assignment.cardRuntimeInstanceId ==
                                      card.runtimeInstanceId &&
                                  string.Equals(assignment.sealContentId,
                                      seal.contentId,
                                      StringComparison.Ordinal)));
            }

            projectedPayload = JsonUtility.ToJson(data);
            return true;
        }

        private static int ResolveG2FloorIndex(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId)) return 0;
            if (string.Equals(contentId,
                    "level.xiyou.yingchoujian.v1",
                    StringComparison.OrdinalIgnoreCase))
                return 1;
            for (int floor = 1; floor <= 5; floor++)
            {
                if (contentId.IndexOf(".f0" + floor,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    return floor;
            }
            return 0;
        }
    }
}
