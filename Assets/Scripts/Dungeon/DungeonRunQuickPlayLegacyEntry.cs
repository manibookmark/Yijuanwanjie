using System;
using UnityEngine;

namespace WanjieScroll.Dungeon
{
    /// <summary>
    /// Explicit editor diagnostic import of reviewed historical player/card state.
    /// It does not upgrade an old gameplay save: today's fresh target supplies all
    /// generated rosters, map states and durable journals. Historical completion,
    /// species, kill facts and receipts are never invented or copied forward.
    /// </summary>
    public static class DungeonRunQuickPlayLegacyEntry
    {
        public static bool TryCreate(string historicalPayload, string freshTargetPayload,
            LevelRunProfile target, out string payload, out string error)
        {
            payload = null; error = null;
            try
            {
                var historical = JsonUtility.FromJson<DungeonRunSaveData>(historicalPayload);
                if (historical?.currentFloor?.profileContentId != target?.contentId)
                    throw new InvalidOperationException("历史标准入口与目标层不一致。");
                // A newly regenerated standard checkpoint must keep its real
                // full-route proof; strict production restore will validate it.
                if (historical.schemaVersion >= 14) { payload = historicalPayload; return true; }
                payload = JsonUtility.ToJson(Rebase(historicalPayload, freshTargetPayload, target));
                return true;
            }
            catch (Exception exception) { error = "历史F5入口代表投影失败：" + exception.Message; return false; }
        }

        internal static DungeonRunSaveData Rebase(string historicalPayload,
            string freshTargetPayload, LevelRunProfile target)
        {
            var old = JsonUtility.FromJson<DungeonRunSaveData>(historicalPayload);
            var fresh = JsonUtility.FromJson<DungeonRunSaveData>(freshTargetPayload);
            if (old == null || fresh?.currentFloor == null || target == null ||
                old.schemaVersion < 1 || old.schemaVersion > 13 || fresh.schemaVersion < 38 ||
                old.dungeonContentId != target.dungeonContentId || fresh.currentFloor.profileContentId != target.contentId ||
                string.IsNullOrWhiteSpace(old.firstFloorContentId) || !old.hasPlayerState || old.maxHealth < 1 ||
                old.currentHealth < 1 || old.currentHealth > old.maxHealth || old.currentLevel < 1)
                throw new InvalidOperationException("历史入口不是受支持的已评审旧版角色/牌组，或目标不是当前新建层。");
            fresh.cards = DungeonRunCardCodec.ReadRun(historicalPayload, old.schemaVersion);
            if (fresh.cards.Count == 0) throw new InvalidOperationException("历史入口没有牌组。");
            fresh.firstFloorContentId = old.firstFloorContentId;
            fresh.currentLevel = old.currentLevel;
            fresh.currentExperience = old.currentExperience;
            fresh.xpFractionCarryUnits = old.xpFractionCarryUnits;
            fresh.runLuckBonus = old.runLuckBonus;
            fresh.runGreedBonus = old.runGreedBonus;
            fresh.runGrowthBonus = old.runGrowthBonus;
            fresh.runJadeEarned = old.runJadeEarned;
            fresh.baseManaBonus = old.baseManaBonus;
            fresh.baseArmorPerTurnBonus = old.baseArmorPerTurnBonus;
            fresh.mightBonus = old.mightBonus;
            fresh.handLimitBonus = old.handLimitBonus;
            fresh.areaBonus = old.areaBonus;
            fresh.rewardRerollsRemaining = old.rewardRerollsRemaining;
            fresh.hasPlayerState = true;
            fresh.maxHealth = old.maxHealth;
            fresh.currentHealth = old.currentHealth;
            fresh.pendingForcedOpeningHandCardInstanceId = old.pendingForcedOpeningHandCardInstanceId;
            // The historical file contains no frozen account baseline. Preserve
            // that unknown provenance instead of blessing today's account as old.
            fresh.hasPlayerBaseline = false;
            fresh.playerBaseline = null;
            fresh.playerBaselineOrigin = DungeonRunPlayerBaselineOrigin.LegacyMissing;
            // All maps, species, lifecycle journals and statistics remain fresh.
            // Installed physical seals carry their own legacy state, not fake
            // earlier-floor assignment transactions or victory receipts.
            return fresh;
        }
    }
}
