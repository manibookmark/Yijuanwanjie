using System;
using System.Collections.Generic;
using WanjieScroll.Content;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>测试/验收账号的进度预设。</summary>
    public enum TestAccountPreset
    {
        /// <summary>全新账号：无通关记录、无解锁内容；用于验收教程关与首启流程。</summary>
        FreshAccount = 0,
        /// <summary>教程通关（G1 入口）：回放教程拾取与自动奖励，含九叠/五行盘授权。</summary>
        PostTutorial = 1,
        /// <summary>G1 通关（G2 入口）：教程授权 + G1 各层回放 + G1 关卡通关记录。</summary>
        G1Cleared = 2,
        /// <summary>
        /// G2F1 开发完成数据：在 G1 通关账号上回放 F1 的完成效果，
        /// 并按神域快速测试口径解锁观星设施；仅供开发面板使用。
        /// </summary>
        G2F1Cleared = 3,
        /// <summary>
        /// G2F5 固定累计测试入口账号：仅回放 G2F1–F4 已完成的永久解锁；
        /// G2 终局通关、白龙马与观星台仍由 F5 正式结算授予。
        /// </summary>
        G2F4Cleared = 4,
        /// <summary>G2 通关后仙域测试：回放全关自动奖励，含白龙马、观星台及通关记录。</summary>
        G2Cleared = 5,
        /// <summary>G3 整关通关（G4 入口）：只回放永久奖励，不导入临时牌组或预支沙悟净。</summary>
        G3Cleared = 6,
    }

    /// <summary>
    /// 按官方授权语义构造指定进度点的测试账号（MetaProgression）。
    /// 只回放关卡资产实际声明的拾取与自动奖励，不臆造任何数值；货币统一归零，
    /// 与 G1 验收起点账号口径一致（货币/经诀/装备秘法由正式玩法产生，测试档不导入）。
    /// 唯一所有者：RunAcceptanceSession.PrepareStartingAccount 与编辑器测试存档菜单
    /// 都必须经由本类获得账号语义，不得各自复制回放逻辑。
    /// </summary>
    public static class ProgressAccountBlueprints
    {
        private const string G2FinalFloorContentId =
            "floor.xiyou.yingchoujian.f05";

        /// <summary>构造一份指定进度的账号。调用方决定是否写盘。</summary>
        public static MetaProgression Build(
            TestAccountPreset preset,
            string selectedHero)
        {
            var account = new MetaProgression();
            switch (preset)
            {
                case TestAccountPreset.FreshAccount:
                    return account;
                case TestAccountPreset.PostTutorial:
                    ReplayTutorial(account, selectedHero);
                    return account;
                case TestAccountPreset.G1Cleared:
                    ReplayTutorial(account, selectedHero);
                    ReplayLevelClearance(
                        account,
                        MetaProgression.G1FacilityUnlockLevelContentId);
                    return account;
                case TestAccountPreset.G2F1Cleared:
                    ReplayTutorial(account, selectedHero);
                    ReplayLevelClearance(
                        account,
                        MetaProgression.G1FacilityUnlockLevelContentId);
                    ReplaySingleFloorCompletionData(
                        account,
                        MetaProgression.G2FacilityUnlockLevelContentId);
                    ReplayG2F1HubUnlockData(account);
                    return account;
                case TestAccountPreset.G2F4Cleared:
                    ReplayTutorial(account, selectedHero);
                    ReplayLevelClearance(
                        account,
                        MetaProgression.G1FacilityUnlockLevelContentId);
                    ReplayFloorsBefore(
                        account,
                        MetaProgression.G2FacilityUnlockLevelContentId,
                        G2FinalFloorContentId);
                    return account;
                case TestAccountPreset.G2Cleared:
                    ReplayTutorial(account, selectedHero);
                    ReplayLevelClearance(
                        account,
                        MetaProgression.G1FacilityUnlockLevelContentId);
                    ReplayLevelClearance(
                        account,
                        MetaProgression.G2FacilityUnlockLevelContentId);
                    return account;
                case TestAccountPreset.G3Cleared:
                    ReplayTutorial(account, selectedHero);
                    ReplayLevelClearance(account, MetaProgression.G1FacilityUnlockLevelContentId);
                    ReplayLevelClearance(account, MetaProgression.G2FacilityUnlockLevelContentId);
                    ReplayLevelClearance(account, "level.xiyou.gaolaozhuang.v1");
                    return account;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(preset), preset, null);
            }
        }

        /// <summary>
        /// 回放教程关的拾取与自动奖励并记录通关：拾取九叠/五行盘、
        /// 解锁入场英灵、通关记录，随后归零货币。
        /// 语义与原 RunAcceptanceSession.PrepareStartingAccount 完全一致。
        /// </summary>
        public static void ReplayTutorial(
            MetaProgression account,
            string selectedHero)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));
            LevelRunProfile tutorial = LevelRunProfileCatalog.Get(
                RunCombatRules.TutorialLevelContentId);
            ReplayFloor(account, tutorial);
            new LevelRewardGrantRules(account)
                .RecordLevelCleared(tutorial.contentId);
            if (!account.IsCrawlerUnlocked(selectedHero))
                throw new InvalidOperationException(
                    "Selected hero is not available in the declared post-tutorial slice.");
            // Currency is explicitly normalized for this slice; no upgrades,
            // equipped arcana or later-account progression are imported.
            account.lingyu = 0;
            account.totalLingyuEarned = 0;
        }

        /// <summary>
        /// 回放一层多楼层关卡的拾取与自动奖励，并以首层（关卡稳定 contentId）
        /// 记录通关——与 DungeonRunSettlement 的 RecordLevelCleared(FirstFloor)
        /// 口径一致。玩家可选奖励（钤印三选一等）不属于账号解锁状态，不回放。
        /// </summary>
        public static void ReplayLevelClearance(
            MetaProgression account,
            string firstFloorContentId)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));
            if (string.IsNullOrWhiteSpace(firstFloorContentId))
                throw new ArgumentException(
                    "关卡首层 contentId 不能为空。", nameof(firstFloorContentId));

            var visited = new HashSet<string>(StringComparer.Ordinal);
            string cursor = firstFloorContentId;
            while (!string.IsNullOrWhiteSpace(cursor) && visited.Add(cursor))
            {
                LevelRunProfile floor = LevelRunProfileCatalog.Get(cursor);
                ReplayFloor(account, floor);
                cursor = floor.nextFloorContentId;
            }
            new LevelRewardGrantRules(account)
                .RecordLevelCleared(firstFloorContentId);
        }

        private static void ReplayFloor(
            MetaProgression account,
            LevelRunProfile floor)
        {
            var rewards = new LevelRewardGrantRules(account);
            foreach (LevelNode node in floor.nodes)
            {
                if (node == null) continue;
                foreach (LevelInteractionEffect effect in node.completionEffects)
                    if (effect.kind == LevelInteractionEffectKind.GrantRelic)
                        GrantRelic(account, effect.targetContentId);
                rewards.Apply(LevelRewardGrantRules.GetAutomaticGrants(node));
            }
        }

        /// <summary>
        /// 回放单个 G2F1 楼层的完成效果供神域/内容调试使用。
        /// 与整关结算不同，这里不沿
        /// <c>nextFloorContentId</c> 继续回放后续楼层。
        /// </summary>
        private static void ReplaySingleFloorCompletionData(
            MetaProgression account,
            string floorContentId)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));
            if (string.IsNullOrWhiteSpace(floorContentId))
                throw new ArgumentException(
                    "楼层 contentId 不能为空。", nameof(floorContentId));

            LevelRunProfile floor = LevelRunProfileCatalog.Get(floorContentId);
            ReplayFloor(account, floor);
        }

        /// <summary>
        /// 回放多楼层关卡在终局前的永久效果，但不写整关通关记录，也不触发
        /// 终局楼层的自动奖励。用于让 F5 固定累计测试入口与实际完成 F1–F4 的
        /// 账号解锁口径一致，同时防止白龙马／观星台被提前授予。
        /// </summary>
        private static void ReplayFloorsBefore(
            MetaProgression account,
            string firstFloorContentId,
            string exclusiveFloorContentId)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));
            if (string.IsNullOrWhiteSpace(firstFloorContentId))
                throw new ArgumentException(
                    "关卡首层 contentId 不能为空。", nameof(firstFloorContentId));
            if (string.IsNullOrWhiteSpace(exclusiveFloorContentId))
                throw new ArgumentException(
                    "终局楼层 contentId 不能为空。",
                    nameof(exclusiveFloorContentId));

            var visited = new HashSet<string>(StringComparer.Ordinal);
            string cursor = firstFloorContentId;
            while (!string.Equals(
                       cursor,
                       exclusiveFloorContentId,
                       StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(cursor))
                    throw new InvalidOperationException(
                        "关卡楼层链未抵达声明的终局楼层：" +
                        exclusiveFloorContentId);
                if (!visited.Add(cursor))
                    throw new InvalidOperationException(
                        "关卡楼层链存在循环，无法构造终局入口：" + cursor);

                LevelRunProfile floor = LevelRunProfileCatalog.Get(cursor);
                ReplayFloor(account, floor);
                cursor = floor.nextFloorContentId;
            }
        }

        /// <summary>
        /// G2F1 快速测试档的神域落点：F1 完成后直接展示观星设施。
        /// 这是开发面板的验收数据，不改正式 G2 结算流程；正式结算仍由
        /// DungeonRunSettlement 按完整关卡合同提交白龙马、观星台和通关记录。
        /// </summary>
        private static void ReplayG2F1HubUnlockData(MetaProgression account)
        {
            GrantRelic(account, RelicContentIds.GuanxingTai);
            new LevelRewardGrantRules(account)
                .RecordLevelCleared(MetaProgression.G2FacilityUnlockLevelContentId);
        }

        private static void GrantRelic(MetaProgression account, string contentId)
        {
            if (!ContentCatalog.Current.TryGetRelic(contentId, out RelicData relic) ||
                !Enum.IsDefined(typeof(RelicType), relic.relicType))
                throw new InvalidOperationException(
                    "关卡声明的遗珍缺失或类型无效：" + contentId);
            account.TryUnlockRelic(
                relic.contentId,
                (RelicType)relic.relicType,
                relic.name,
                relic.description);
        }
    }
}
