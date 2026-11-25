#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Dungeon
{
    /// <summary>Explicit diagnostic values, never a production balance profile or completion receipt.</summary>
    public sealed class DungeonRunQuickPlayRepresentativeState
    {
        public string contentId;
        public int seed, level, cardCount, jade, luck;
        public int baseManaBonus, baseArmorBonus, handBonus, rerolls;

        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(contentId) || seed <= 0 || level < 2 || cardCount < 1)
                return "代表入口缺少稳定ID、Seed、非首层等级或牌组规模。";
            if (jade < 0 || luck < 0 || baseManaBonus < 0 || baseArmorBonus < 0 || handBonus < 0 || rerolls < 0)
                return "代表入口的成长值不能为负数。";
            return null;
        }
    }

    /// <summary>
    /// Owns detached diagnostic Run construction and the save-schema projection.
    /// The admitted first-floor baseline supplies current account/hero rules; a fresh
    /// target owns every map, source, formation and interaction ledger. No historical
    /// floor is manufactured. The global session pointer and account remain untouched.
    /// </summary>
    public static class DungeonRunQuickPlayRepresentativeEntry
    {
        public static bool TryPrepare(DungeonRunSession admittedBaseline,
            LevelRunProfile target, string snapshotId,
            IReadOnlyList<CardConfig> seedDeck, IReadOnlyList<CardConfig> cardPool,
            DungeonRunQuickPlayRepresentativeState state,
            Func<string, LevelRunProfile> resolveProfile,
            out DungeonRunSession prepared, out string error)
        {
            prepared = null;
            error = state?.Validate() ?? (state == null ? "缺少代表入口状态。" : null);
            if (error != null) return false;
            List<CardConfig> physical = null;
            try
            {
                if (admittedBaseline == null || !admittedBaseline.HasPlayerState ||
                    admittedBaseline.PlayerBaselineOrigin != DungeonRunPlayerBaselineOrigin.RunEntry ||
                    admittedBaseline.CurrentFloor.Profile.floorIndex != 1 || target == null || target.floorIndex <= 1 ||
                    !string.Equals(admittedBaseline.DungeonContentId, target.dungeonContentId, StringComparison.Ordinal))
                    throw new InvalidOperationException("代表入口需要同关正式首层的已准入角色基线和合法高层。 ");
                if (admittedBaseline.SideEvents.HasSourcePlan)
                    throw new InvalidOperationException("具有来源时间线的高层不能用代表状态绕过来源准入；请提供获准检查点。");
                if (seedDeck == null || seedDeck.Count != state.cardCount || resolveProfile == null)
                    throw new InvalidOperationException("代表牌组张数或楼层解析器与入口配置不一致。");
                if (snapshotId != target.ResolveDefaultSnapshotId())
                    throw new InvalidOperationException("代表入口仅支持当前作者默认快照，不能套入自定义节点起点。");
                var player = new PlayerStats();
                if (!admittedBaseline.TryProjectPlayerBaseline(player))
                    throw new InvalidOperationException("正式入口缺少可投影的角色基线。");
                player.currentHealth = player.maxHealth;
                var candidate = DungeonRunSession.Begin(target, snapshotId,
                    "quickplay.representative." + state.contentId,
                    new SeededInstanceIdGenerator(1), runSeed: state.seed,
                    cardPlayGrowthEnabled: admittedBaseline.RunProgression.CardPlayGrowth.Enabled,
                    leaderCrawlerContentId: admittedBaseline.LeaderCrawlerContentId,
                    commonEncounterSources: admittedBaseline.CommonEncounterSources);
                candidate.RunProgression.CrawlerDurationReserve.Bank(
                    admittedBaseline.RunProgression.CrawlerDurationReserve.PendingBonus);
                if (!candidate.TryPrepareCombat(player, seedDeck, cardPool, out physical, out error))
                    return false;
                var data = JsonUtility.FromJson<DungeonRunSaveData>(candidate.Serialize());
                data.currentLevel = state.level;
                data.currentExperience = 0;
                data.xpFractionCarryUnits = 0;
                data.baseManaBonus = state.baseManaBonus;
                data.baseArmorPerTurnBonus = state.baseArmorBonus;
                data.handLimitBonus = state.handBonus;
                data.rewardRerollsRemaining = state.rerolls;
                data.runLuckBonus = state.luck;
                data.runJadeEarned = state.jade;
                data.mapCurrentMana = checked(player.faLi + state.baseManaBonus);
                if (!DungeonRunSession.TryRestore(JsonUtility.ToJson(data), resolveProfile,
                        new SeededInstanceIdGenerator(state.cardCount + 1), null,
                        out prepared, out error, commonEncounterSources: admittedBaseline.CommonEncounterSources))
                    return false;
                if (prepared.RunCardCount != state.cardCount || !prepared.HasPlayerState ||
                    prepared.CurrentHealth != player.maxHealth || prepared.CombatProgression.CurrentLevel != state.level ||
                    prepared.CaptureCompletedFloorContentIds().Count != 0 || prepared.CurrentFloor.ActiveEncounter.HasValue ||
                    !string.IsNullOrWhiteSpace(prepared.CurrentFloor.ActiveInteractionNodeId))
                    throw new InvalidOperationException("代表入口冻结后的状态不完整或混入了历史运行状态。");
                return true;
            }
            catch (Exception exception)
            {
                prepared = null;
                error = "建立人工验收代表入口失败：" + exception.Message;
                return false;
            }
            finally
            {
                foreach (CardConfig card in physical ?? new List<CardConfig>())
                    DungeonRunCardCodec.Release(card);
            }
        }
    }
}
#endif
