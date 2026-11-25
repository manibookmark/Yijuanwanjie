using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.Persistence;
using WanjieScroll.Progression;
using WanjieScroll.UI.Presentations;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the editor-only F5 map session boundary. Higher-floor direct entry
    /// restores a reviewed standard checkpoint; player builds can only observe
    /// the inert persistence policy.
    /// Scale review: this change stays within F5 lifecycle/save admission and
    /// adds no mutable state or second save owner. Independent features must
    /// be separated before this already-large module grows another concern.
    /// </summary>
    public static class EditorLevelMapQuickPlayRuntime
    {
        private static bool _isTransientSession;

        public static bool IsTransientSession => _isTransientSession;
        public static bool CanPersist => !_isTransientSession;

        /// <summary>
        /// Strict gameplay transactions may persist an isolated F5 Run, while
        /// ordinary transient autosaves remain disabled. A captured service must
        /// still target the active slot; a transient Run additionally requires
        /// the explicit F5 redirect and its dedicated test slot.
        /// </summary>
        public static bool CanPersistTransaction(GameSaveService save)
        {
            if (save == null || !string.Equals(save.SavePath,
                    Path.GetFullPath(SaveSlotStore.Current.GetSlotPath(SaveSlotStore.Current.ActiveSlot)),
                    StringComparison.OrdinalIgnoreCase))
                return false;
            if (!_isTransientSession) return true;
#if UNITY_EDITOR
            return HasRedirectedActiveSlot &&
                SaveSlotStore.Current.ActiveSlot == QuickPlayTestSlot;
#else
            return false;
#endif
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayStart()
        {
            _isTransientSession = false;
#if UNITY_EDITOR
            // 上一次 F5 会话若因编辑器异常退出而残留槽位重定向，在此兜底还原。
            RestoreActiveSlotIfRedirected();
#endif
        }

        public static void EndTransientSession()
        {
            // 只结束临时 Run 边界，不还原活动槽：RouteToHub 等局内导航后，
            // Hub 仍应读取测试槽的预设账号（用于验收设施解锁状态）。
            // 槽位还原只属于 Play 会话边界（编辑器侧 hook / 下次进 Play 兜底）。
            _isTransientSession = false;
        }

        /// <summary>
        /// Carries the already-settled test account across a new-Run transition.
        /// This narrow exception never enables general transient persistence:
        /// only the explicitly redirected F5 slot is writable, never the real slot.
        /// It preserves earned unlocks instead of rebuilding a preset account.
        /// </summary>
        public static bool TrySaveContinuationAccount(MetaProgression account,
            string preparedPayload, out string error)
        {
            error = null;
            if (account == null || string.IsNullOrWhiteSpace(preparedPayload))
            {
                error = "跨关测试存档缺少账号或新探索载荷。";
                return false;
            }
#if UNITY_EDITOR
            if (!_isTransientSession || !HasRedirectedActiveSlot ||
                SaveSlotStore.Current.ActiveSlot != QuickPlayTestSlot)
            {
                error = "跨关测试存档仅允许写入已重定向的 F5 专用测试槽。";
                return false;
            }
            try
            {
                SaveSlotStore.Current.GetSlotService(QuickPlayTestSlot)
                    .SaveTransactional(account, preparedPayload);
                return true;
            }
            catch (Exception exception)
            {
                error = "保存跨关测试账号失败：" + exception.Message;
                return false;
            }
#else
            error = "玩家构建不允许写入 F5 测试存档。";
            return false;
#endif
        }

#if UNITY_EDITOR
        // ── F5 预设进度档 ──────────────────────────────────────
        // 按目标关卡的进度阶段自动生成测试账号，写入固定测试槽并临时
        // 把活动槽切到测试槽；真实进度档全程不被触碰，退出 Play 自动还原。

        /// <summary>F5 快速测试固定写入的测试存档槽；真实进度档不受影响。</summary>
        public const int QuickPlayTestSlot = 3;

        private const string ShuangchalingDungeonContentId =
            "dungeon.xiyou.shuangchaling";
        private const string YingchoujianDungeonContentId =
            "dungeon.xiyou.yingchoujian";
        private const string LiushaheDungeonContentId = "dungeon.xiyou.liushahe";
        private const string LiushaheFirstFloorContentId = "level.xiyou.liushahe.f01";
        private const string ZhubajieCrawlerContentId = "crawler.xiyou.zhubajie";
        private const string GaolaozhuangDungeonContentId =
            "dungeon.xiyou.gaolaozhuang";
        private const string GaolaozhuangFirstFloorContentId =
            "level.xiyou.gaolaozhuang.v1";
        private const string GaolaozhuangSecondFloorContentId =
            "floor.xiyou.gaolaozhuang.f02";
        private const string GaolaozhuangThirdFloorContentId =
            "floor.xiyou.gaolaozhuang.f03";
        private const string GaolaozhuangFourthFloorContentId =
            "floor.xiyou.gaolaozhuang.f04";
        private const string GaolaozhuangFinalFloorContentId =
            "floor.xiyou.gaolaozhuang.f05";
        private const string YingchoujianFinalFloorContentId =
            "floor.xiyou.yingchoujian.f05";
        private const string YingchoujianSecondFloorContentId =
            "floor.xiyou.yingchoujian.f02";
        private const string YingchoujianThirdFloorContentId =
            "floor.xiyou.yingchoujian.f03";
        private const string YingchoujianFourthFloorContentId =
            "floor.xiyou.yingchoujian.f04";
        private const string YingchoujianFirstFloorContentId =
            "level.xiyou.yingchoujian.v1";
        private const string TangSanzangCrawlerContentId =
            "crawler.xiyou.tangsanzang.v1";
        private const string BailongmaCrawlerContentId =
            "crawler.xiyou.bailongma.v1";

        // 跨 Play 边界的会话标记必须走 PlayerPrefs：进入 Play 会触发域重载，
        // 编辑模式设置的静态字段会被清空；编辑器异常退出后也能兜底清理。
        private const string SlotRedirectedPrefKey =
            "WanjieScroll.QuickPlay.SlotRedirected";
        private const string PreQuickPlayActiveSlotPrefKey =
            "WanjieScroll.QuickPlay.PreviousActiveSlot";
        private const string HubDirectPlaySessionPrefKey =
            "WanjieScroll.QuickPlay.HubDirectPlaySession";
        private const string QuickPlayPresetPrefKey =
            "WanjieScroll.QuickPlay.Preset";

        private static string _lastStagedPresetDescription;

        /// <summary>最近一次为 F5 会话生成的预设进度档描述（供关卡选择器显示）。</summary>
        public static string LastStagedPresetDescription =>
            !string.IsNullOrWhiteSpace(_lastStagedPresetDescription)
                ? _lastStagedPresetDescription
                : TryGetLastStagedPreset(out TestAccountPreset preset)
                    ? DescribePreset(preset)
                    : null;

        /// <summary>
        /// 读取跨域重载保留的最近测试预设。只在测试槽重定向期间有效。
        /// </summary>
        public static bool TryGetLastStagedPreset(
            out TestAccountPreset preset)
        {
            preset = default(TestAccountPreset);
            if (!HasRedirectedActiveSlot)
                return false;

            int raw = PlayerPrefs.GetInt(QuickPlayPresetPrefKey, -1);
            if (!Enum.IsDefined(typeof(TestAccountPreset), raw))
                return false;
            preset = (TestAccountPreset)raw;
            return true;
        }

        /// <summary>F5 会话是否已把活动槽重定向到测试槽。</summary>
        public static bool HasRedirectedActiveSlot =>
            PlayerPrefs.GetInt(SlotRedirectedPrefKey, 0) == 1;

        /// <summary>是否直接进入神域（F1 或对 Hub 场景 F4），Play 内全程有效。</summary>
        public static bool HasHubDirectPlaySession =>
            PlayerPrefs.GetInt(HubDirectPlaySessionPrefKey, 0) == 1;

        /// <summary>神域调试窗口的显隐门槛：直进或已挂测试档的会话。</summary>
        public static bool IsQuickPlayHubSession =>
            HasHubDirectPlaySession || HasRedirectedActiveSlot;

        /// <summary>F1/F4 编辑器入口在进入 Play 前标记“直进神域”会话。</summary>
        public static void BeginHubDirectPlaySession()
        {
            PlayerPrefs.SetInt(HubDirectPlaySessionPrefKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>编辑器 hook 在退出 Play 时调用，清除直进标记。</summary>
        public static void EndHubDirectPlaySession()
        {
            PlayerPrefs.SetInt(HubDirectPlaySessionPrefKey, 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 把目标关卡映射到对应的进度预设：教程关用全新档（走真实拾取流程），
        /// G1 系列用教程通关档；G2F1 用 G1 通关档，G2F2–F4 用
        /// G2F1 已完成档，G2F5 用前四层累计入口档；G3F1–F5 均用 G2
        /// 通关档；G4 已登记楼层用 G3 整关通关档。G3高层另加载显式代表
        /// 等级、牌组和成长，G4未准入高层拒绝；均不冒充自然路线检查点。
        /// </summary>
        public static TestAccountPreset? ResolvePresetForLevel(
            LevelRunProfile profile)
        {
            if (profile == null) return null;
            if (string.Equals(
                    profile.contentId,
                    RunCombatRules.TutorialLevelContentId,
                    StringComparison.Ordinal))
                return TestAccountPreset.FreshAccount;
            if (string.Equals(
                    profile.dungeonContentId,
                    ShuangchalingDungeonContentId,
                    StringComparison.Ordinal))
                return TestAccountPreset.PostTutorial;
            if (string.Equals(
                    profile.dungeonContentId,
                    GaolaozhuangDungeonContentId,
                    StringComparison.Ordinal) &&
                IsKnownGaolaozhuangFloor(profile.contentId))
                return TestAccountPreset.G2Cleared;
            if (string.Equals(profile.dungeonContentId, LiushaheDungeonContentId, StringComparison.Ordinal) &&
                IsKnownLiushaheFloor(profile.contentId))
                return TestAccountPreset.G3Cleared;
            if (string.Equals(
                    profile.dungeonContentId,
                    YingchoujianDungeonContentId,
                    StringComparison.Ordinal))
            {
                if (string.Equals(profile.contentId,
                        YingchoujianFinalFloorContentId,
                        StringComparison.Ordinal))
                    return TestAccountPreset.G2F4Cleared;
                if (string.Equals(profile.contentId,
                        YingchoujianFirstFloorContentId,
                        StringComparison.Ordinal))
                    return TestAccountPreset.G1Cleared;
                if (string.Equals(profile.contentId,
                        YingchoujianSecondFloorContentId,
                        StringComparison.Ordinal) ||
                    string.Equals(profile.contentId,
                        YingchoujianThirdFloorContentId,
                        StringComparison.Ordinal) ||
                    string.Equals(profile.contentId,
                        YingchoujianFourthFloorContentId,
                        StringComparison.Ordinal))
                    return TestAccountPreset.G2F1Cleared;
                // A same-dungeon placeholder or future floor must be
                // registered explicitly before it can borrow a progress
                // account.  Falling back to F2 made an unknown F5 selection
                // look valid while silently using the wrong unlock state.
                return null;
            }
            return null;
        }

        // Stable registration only; topology and opening state remain asset-owned.
        private static bool IsKnownLiushaheFloor(string contentId)
        {
            switch (contentId)
            {
                case "level.xiyou.liushahe.f01":
                case "level.xiyou.liushahe.f02":
                case "level.xiyou.liushahe.f03":
                case "level.xiyou.liushahe.f04":
                case "level.xiyou.liushahe.f05": return true;
                default: return false;
            }
        }

        private static bool IsKnownGaolaozhuangFloor(string contentId)
        {
            return string.Equals(contentId,
                       GaolaozhuangFirstFloorContentId,
                       StringComparison.Ordinal) ||
                string.Equals(contentId,
                    GaolaozhuangSecondFloorContentId,
                    StringComparison.Ordinal) ||
                string.Equals(contentId,
                    GaolaozhuangThirdFloorContentId,
                    StringComparison.Ordinal) ||
                string.Equals(contentId,
                    GaolaozhuangFourthFloorContentId,
                    StringComparison.Ordinal) ||
                string.Equals(contentId,
                    GaolaozhuangFinalFloorContentId,
                    StringComparison.Ordinal);
        }

        private static bool IsFormalQuickPlayFirstFloor(string contentId)
        {
            return string.Equals(contentId, GaolaozhuangFirstFloorContentId,
                       StringComparison.Ordinal) ||
                   string.Equals(contentId, LiushaheFirstFloorContentId,
                       StringComparison.Ordinal);
        }

        /// <summary>
        /// F5 快选的当前英灵只由目标楼层所属关区决定。G2 的预设已回放
        /// G1F4 解锁，因此选唐三藏；G3 选 G2 终局解救的白龙马；
        /// G4 选 G3 终局已解锁八戒；其余
        /// 既有关区沿用教程解锁的孙悟空。正式 Hub 入口仍保留玩家选择。
        /// </summary>
        public static string ResolveQuickPlayCrawlerForLevel(
            LevelRunProfile profile)
        {
            if (profile != null && string.Equals(profile.dungeonContentId,
                    LiushaheDungeonContentId, StringComparison.Ordinal) &&
                IsKnownLiushaheFloor(profile.contentId))
                return ZhubajieCrawlerContentId;
            if (profile != null && string.Equals(profile.dungeonContentId,
                    GaolaozhuangDungeonContentId, StringComparison.Ordinal) &&
                IsKnownGaolaozhuangFloor(profile.contentId))
                return BailongmaCrawlerContentId;
            return profile != null &&
                string.Equals(profile.dungeonContentId,
                    YingchoujianDungeonContentId,
                    StringComparison.Ordinal) &&
                IsKnownYingchoujianFloor(profile.contentId)
                ? TangSanzangCrawlerContentId
                : TutorialProgressionGate.WukongCrawlerContentId;
        }

        private static bool IsKnownYingchoujianFloor(string contentId)
        {
            return string.Equals(contentId,
                       YingchoujianFirstFloorContentId,
                       StringComparison.Ordinal) ||
                string.Equals(contentId,
                    YingchoujianSecondFloorContentId,
                    StringComparison.Ordinal) ||
                string.Equals(contentId,
                    YingchoujianThirdFloorContentId,
                    StringComparison.Ordinal) ||
                string.Equals(contentId,
                    YingchoujianFourthFloorContentId,
                    StringComparison.Ordinal) ||
                string.Equals(contentId,
                    YingchoujianFinalFloorContentId,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// F5 入口统一调用：按目标关卡的进度阶段生成预设测试档并临时把
        /// 活动槽切换到测试槽。同一会话内切换关卡会按新关卡重新生成。
        /// </summary>
        public static void ApplyPresetAccountForLevel(LevelRunProfile profile)
        {
            // A formal F5 Run already owns the test-account projection used
            // for admission. BuildNow's legacy follow-up staging must not erase
            // that account or change its selected hero after the Run is frozen.
            // Reuse the existing typed/slot boundary; no second staging flag.
            DungeonRunSession prepared = DungeonRunSessionStore.Current;
            if (profile != null && _isTransientSession && HasRedirectedActiveSlot &&
                SaveSlotStore.Current.ActiveSlot == QuickPlayTestSlot &&
                prepared != null && prepared.PlayerBaselineOrigin == DungeonRunPlayerBaselineOrigin.RunEntry &&
                (IsFormalQuickPlayFirstFloor(prepared.FirstFloorContentId) ||
                 (prepared.CurrentFloor.Profile.floorIndex > 1 &&
                  IsKnownGaolaozhuangFloor(prepared.CurrentFloorContentId))) &&
                string.Equals(prepared.CurrentFloorContentId, profile.contentId, StringComparison.Ordinal))
                return;
            TestAccountPreset? resolved = ResolvePresetForLevel(profile);
            if (resolved == null)
            {
                _lastStagedPresetDescription = null;
                Debug.LogWarning(
                    "[QuickPlay] 未识别关卡的进度阶段，沿用测试槽现有测试档：" +
                    (profile == null ? "<null>" : profile.contentId));
                return;
            }
            ApplyPresetAccount(
                resolved.Value,
                ResolveQuickPlayCrawlerForLevel(profile));
        }

        /// <summary>
        /// 按进度预设生成测试档并临时把活动槽切换到测试槽。
        /// F5 关卡映射与神域阶段选择窗口共用同一入口。
        /// </summary>
        public static void ApplyPresetAccount(TestAccountPreset preset)
        {
            ApplyPresetAccount(
                preset,
                TutorialProgressionGate.WukongCrawlerContentId);
        }

        private static void ApplyPresetAccount(
            TestAccountPreset preset,
            string selectedCrawlerContentId)
        {
            if (string.IsNullOrWhiteSpace(selectedCrawlerContentId))
            {
                selectedCrawlerContentId =
                    TutorialProgressionGate.WukongCrawlerContentId;
            }

            WritePresetSave(
                preset,
                QuickPlayTestSlot,
                selectedCrawlerContentId);
            if (!HasRedirectedActiveSlot)
                PlayerPrefs.SetInt(
                    PreQuickPlayActiveSlotPrefKey,
                    SaveSlotStore.Current.ActiveSlot);
            SaveSlotStore.Current.ActiveSlot = QuickPlayTestSlot;
            // LevelMap 的 F5 初始化会在预设存档写入前建立临时 Run；因此不只
            // 写盘，还必须同步当前会话的选择态，后续进入 Combat 才会拿到
            // 与该关区一致的起始牌组。
            GameSession.Current.SelectedCrawlerId = selectedCrawlerContentId;
            PlayerPrefs.SetInt(SlotRedirectedPrefKey, 1);
            PlayerPrefs.SetInt(QuickPlayPresetPrefKey, (int)preset);
            PlayerPrefs.Save();
            _lastStagedPresetDescription = DescribePreset(preset);
            Debug.Log(
                "[QuickPlay] F5 预设进度档已生成：" +
                $"{_lastStagedPresetDescription}（写入槽 {QuickPlayTestSlot}）");
        }

        /// <summary>
        /// 按预设生成账号并原子写入指定槽（旧档自动备份 .bak），返回存档路径。
        /// 唯一写盘原语：F5 自动档与编辑器“生成测试存档”菜单共用。
        /// </summary>
        public static string WritePresetSave(TestAccountPreset preset, int slot)
        {
            return WritePresetSave(
                preset,
                slot,
                TutorialProgressionGate.WukongCrawlerContentId);
        }

        private static string WritePresetSave(
            TestAccountPreset preset,
            int slot,
            string selectedCrawlerContentId)
        {
            if (string.IsNullOrWhiteSpace(selectedCrawlerContentId))
            {
                selectedCrawlerContentId =
                    TutorialProgressionGate.WukongCrawlerContentId;
            }

            // 账号回放必须从教程已解锁的孙悟空开始：唐三藏是在 G1F4
            // 回放之后才可用。选择态在下方单独写入，不能反过来传给 Build。
            MetaProgression account = ProgressAccountBlueprints.Build(
                preset, TutorialProgressionGate.WukongCrawlerContentId);
            // 教程通关后的账号必然已到访过仙域：补记首访演出完成状态，
            // 避免测试档每次进仙域都被当作第一次到访而重播复苏仪式。
            CompleteHubArrivalPresentation(account);

            // 存档捕获从 GameSession 读取跨场景态；写测试档期间将其归零并
            // 指向目标英灵，写完立即恢复，避免污染当前会话。
            GameSession session = GameSession.Current;
            int volume = session.SelectedVolumeId;
            int dungeon = session.SelectedDungeonIndex;
            string crawler = session.SelectedCrawlerId;
            int lingyu = session.PlayerLingyu;
            int cleared = session.ClearedLevels;
            try
            {
                session.SelectedVolumeId = 0;
                session.SelectedDungeonIndex = 0;
                session.SelectedCrawlerId = selectedCrawlerContentId;
                session.PlayerLingyu = 0;
                session.ClearedLevels = DisplayClearedLevelCount(preset);
                // 空载荷：测试档不得携带旧槽遗留的进行中 Run。
                SaveSlotStore.Current.GetSlotService(slot)
                    .Save(account, string.Empty);
            }
            finally
            {
                session.SelectedVolumeId = volume;
                session.SelectedDungeonIndex = dungeon;
                session.SelectedCrawlerId = crawler;
                session.PlayerLingyu = lingyu;
                session.ClearedLevels = cleared;
            }
            return SaveSlotStore.Current.GetSlotPath(slot);
        }

        /// <summary>
        /// 回放“教程通关玩家必然已看过”的神域首访演出完成记录。repeatKey
        /// 经正式档案与角色演出目录解析，与 HubArrivalPresentationCoordinator
        /// 的判定口径一致；档案/目录异常只告警跳过，不得让 F5 入口失败。
        /// </summary>
        public static void CompleteHubArrivalPresentation(
            MetaProgression account)
        {
            if (account == null ||
                !TutorialProgressionGate.IsCompleted(account))
            {
                // 教程未完成 = 该演出尚未解锁，全新档保持未观看状态。
                return;
            }
            try
            {
                HubArrivalPresentationProfile profile =
                    Resources.Load<HubArrivalPresentationProfile>(
                        HubArrivalPresentationProfile.ResourcesPath);
                if (profile == null)
                {
                    Debug.LogWarning(
                        "[QuickPlay] 找不到神域首访演出档案，测试档将不含演出完成记录。");
                    return;
                }
                if (!CharacterPresentationCatalog.TryGet(
                        profile.CharacterPresentationId,
                        out CharacterPresentationProfile presentation) ||
                    !presentation.TryGetVariant(
                        profile.CharacterPresentationVariantId,
                        out CharacterPresentationVariant variant))
                {
                    Debug.LogWarning(
                        "[QuickPlay] 神域首访演出配置无法解析，测试档将不含演出完成记录。");
                    return;
                }
                if (presentation.repeatMode !=
                    CharacterPresentationRepeatMode.OncePerSave)
                {
                    return;
                }
                account.TryCompletePresentation(
                    presentation.ResolveRepeatKey(variant));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[QuickPlay] 补记神域首访演出失败（忽略）：" +
                    exception.Message);
            }
        }

        /// <summary>退出 Play 后由编辑器侧调用：把活动槽还原为 F5 之前的槽。</summary>
        public static void RestoreActiveSlotIfRedirected()
        {
            if (!HasRedirectedActiveSlot) return;
            int previous = PlayerPrefs.GetInt(
                PreQuickPlayActiveSlotPrefKey,
                SaveSlotStore.Current.ActiveSlot);
            if (previous < 1 || previous > SaveSlotStore.SlotCount)
                previous = 1;
            PlayerPrefs.SetInt(SlotRedirectedPrefKey, 0);
            PlayerPrefs.DeleteKey(QuickPlayPresetPrefKey);
            PlayerPrefs.Save();
            SaveSlotStore.Current.ActiveSlot = previous;
            _lastStagedPresetDescription = null;
            Debug.Log(
                "[QuickPlay] F5 会话结束，活动存档槽已还原为槽 " + previous);
        }

        internal static string DescribePreset(TestAccountPreset preset)
        {
            switch (preset)
            {
                case TestAccountPreset.PostTutorial:
                    return "教程通关（G1 入口，含九叠/五行盘）";
                case TestAccountPreset.G1Cleared:
                    return "G1 通关（G2 入口，含 G1 各层解锁与道藏）";
                case TestAccountPreset.G2F1Cleared:
                    return "G2F1 开发完成数据（F1 交互已领，观星设施已解锁）";
                case TestAccountPreset.G2F4Cleared:
                    return "G2F1–F4 实测投影数据（G2F5 入口；代表牌组模板；终局奖励未预支）";
                case TestAccountPreset.G3Cleared:
                    return "G3 整关通关（G4 入口；八戒已解锁；沙悟净未预支；不带旧牌组）";
                case TestAccountPreset.G2Cleared:
                    return "G2 通关后测试（白龙马／观星台已解锁；仙域验收，不是进行中牌组）";
                default:
                    return "全新账号（验收教程关/首启）";
            }
        }

        internal static int DisplayClearedLevelCount(TestAccountPreset preset)
        {
            switch (preset)
            {
                case TestAccountPreset.PostTutorial: return 1;
                case TestAccountPreset.G1Cleared: return 5;
                case TestAccountPreset.G2F1Cleared: return 6;
                case TestAccountPreset.G2F4Cleared: return 9;
                case TestAccountPreset.G2Cleared: return 10;
                // Legacy save-summary clearance count, never a Run/card entry level.
                case TestAccountPreset.G3Cleared: return 15;
                default: return 0;
            }
        }

        private const string ShuangchalingRecipeFile =
            "tools/simulation/checkpoint-recipes/" +
            "g1-shuangchaling-standard.json";
        private const string ShuangchalingFinalFloorContentId =
            "floor.xiyou.shuangchaling.f04";
        // 这份文件只提供编辑器快选投影所需的代表牌组与已确认卡牌选择。
        // 下面的逐层表是 2026-09-19 评审通过的 quick-play 入口合同，
        // 不是自然路线 checkpoint，也不能被当作连续 Run 的数值证据。
        // F2 的 200 灵玉沿用该合同；生产记录中出现的 158 灵玉仍属于
        // ISS-04 未决的真实路线差异，不能在这里静默替换。
        private const string YingchoujianRepresentativeBuildTemplateFile =
            "tools/simulation/checkpoints/" +
            "g2-representative-build-template.json";
        private const int ShuangchalingFinalFloorMinimumHealth = 30;

        private sealed class ObservedG2EntryProjection
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
            public int expectedRunCardCount;
        }

        private static readonly IReadOnlyDictionary<string,
            ObservedG2EntryProjection> ObservedG2Entries =
                new Dictionary<string, ObservedG2EntryProjection>(
                    StringComparer.Ordinal)
                {
                    ["floor.xiyou.yingchoujian.f02"] =
                    new ObservedG2EntryProjection
                        { level = 8, jade = 200, might = 20,
                          maxHealth = 50, currentHealth = 50,
                          maxFixedCardInstanceId = 12,
                          expectedRunCardCount = 19 },
                    ["floor.xiyou.yingchoujian.f03"] =
                    new ObservedG2EntryProjection
                        { level = 13, jade = 400, might = 20, hand = 1,
                          maxHealth = 50, currentHealth = 50,
                          maxFixedCardInstanceId = 14,
                          expectedRunCardCount = 21 },
                    ["floor.xiyou.yingchoujian.f04"] =
                    new ObservedG2EntryProjection
                        { level = 15, jade = 600, baseMana = 1,
                          might = 20, area = 10, hand = 1,
                          maxHealth = 50, currentHealth = 50,
                          maxFixedCardInstanceId = 17,
                          expectedRunCardCount = 24 },
                    [YingchoujianFinalFloorContentId] =
                    new ObservedG2EntryProjection
                        { level = 25, jade = 800, baseMana = 1,
                          might = 40, area = 10, hand = 2,
                          maxHealth = 50, currentHealth = 50,
                          maxFixedCardInstanceId = 19,
                          expectedRunCardCount = 26 }
                };

        private static readonly IReadOnlyDictionary<string, string> RecipeFiles =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ShuangchalingDungeonContentId] = ShuangchalingRecipeFile
            };

        private static readonly IReadOnlyDictionary<string, string>
            RepresentativeBuildTemplateFiles =
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [YingchoujianFinalFloorContentId] =
                        YingchoujianRepresentativeBuildTemplateFile
                };

        /// <summary>
        /// Creates an F5-only Run when the global store is empty. Existing Runs
        /// always win and are never replaced. G3 高层允许以目标层为临时入口，
        /// 因此 F5 可以直接检查任意一层，不需要伪造前层战斗结果。
        /// </summary>
        public static bool TryInitializeMissingRun(
            LevelRunProfile targetProfile,
            string requestedSnapshotId,
            out LevelRunSession session,
            out string source,
            out string error)
        {
            session = LevelRunSessionStore.Current;
            source = null;
            error = null;
            if (session != null)
            {
                source = "existing-run";
                return true;
            }
            if (targetProfile == null)
            {
                error = "目标楼层配置为空。";
                return false;
            }

            // EditMode/headless F5 callers do not run BeforeSceneLoad hooks.
            // Install the same lazy production resolver before any new Run.
            CommonEncounterRunComposition.EnsureInstalled();

            string snapshotId = string.IsNullOrWhiteSpace(requestedSnapshotId)
                ? targetProfile.ResolveDefaultSnapshotId()
                : requestedSnapshotId;
            if (string.IsNullOrWhiteSpace(snapshotId))
            {
                error = $"楼层 {targetProfile.contentId} 没有可用的入口快照。";
                return false;
            }

            if (IsFormalQuickPlayFirstFloor(targetProfile.contentId))
            {
                // F5 uses the redirected account through the same admission and
                // manual-card gate as Hub. A custom snapshot is diagnostic only;
                // it cannot replace the explicit formal entry snapshot.
                if (!RunEntryLoadoutProfileCatalog.TryFindForFirstFloor(targetProfile.contentId,
                        out RunEntryLoadoutProfile entry, out error))
                {
                    error = error ?? "首层缺少正式入口：" + targetProfile.contentId;
                    return false;
                }
                if (!string.Equals(snapshotId, entry.entrySnapshotId, StringComparison.Ordinal))
                {
                    error = "首层快速测试必须使用正式入口快照；自定义起点另走诊断入口。";
                    return false;
                }
                try
                {
                    // Unlike legacy scene-seeded entries, formal admission needs
                    // its declared account before constructing the Run. Stage
                    // only the dedicated F5 slot; never read the user's old slot
                    // as the test account or import its upgrades/hero choice.
                    ApplyPresetAccountForLevel(targetProfile);
                    var account = new MetaProgression();
                    if (!SaveSlotStore.Current.GetActiveService().TryLoad(account, out error)) return false;
                    if (!RunStartService.TryPrepare(entry.contentId, account,
                            GameSession.Current.SelectedCrawlerId, DaozangProgressionRules.ApplyToPlayerStats,
                            out DungeonRunSession prepared, out error)) return false;
                    if (!DungeonRunSessionStore.TryAdoptPrepared(prepared, out error)) return false;
                    session = prepared.CurrentFloor;
                    source = (string.Equals(targetProfile.contentId,
                        GaolaozhuangFirstFloorContentId, StringComparison.Ordinal)
                        ? "g3-formal-run:" : "g4-formal-run:") + entry.contentId;
                    _isTransientSession = true;
                    return true;
                }
                catch (Exception exception)
                {
                    error = "F5 测试账号或正式入口初始化失败：" + exception.Message;
                    return false;
                }
            }

            if (targetProfile.floorIndex <= 1)
            {
                try
                {
                    GameSession.Current.StartNewRun();
                    session = LevelRunSessionStore.Begin(
                        targetProfile,
                        snapshotId);
                    source = "fresh-run";
                    _isTransientSession = true;
                    return true;
                }
                catch (Exception exception)
                {
                    LevelRunSessionStore.Clear();
                    GameSession.Current.StartNewRun();
                    error = $"新建 F5 首层 Run 失败：{exception.Message}";
                    return false;
                }
            }

            if ((targetProfile.dungeonContentId == GaolaozhuangDungeonContentId &&
                    IsKnownGaolaozhuangFloor(targetProfile.contentId)) ||
                (targetProfile.dungeonContentId == LiushaheDungeonContentId &&
                    IsKnownLiushaheFloor(targetProfile.contentId)))
            {
                try
                {
                    ApplyPresetAccountForLevel(targetProfile);
                    var account = new MetaProgression();
                    if (!SaveSlotStore.Current.GetActiveService().TryLoad(account, out error)) return false;
                    if (!EditorQuickPlayRepresentativeEntries.TryPrepare(targetProfile, snapshotId, account,
                            ResolveQuickPlayCrawlerForLevel(targetProfile), out DungeonRunSession prepared,
                            out source, out error)) return false;
                    if (!DungeonRunSessionStore.TryAdoptQuickPlayRepresentative(prepared, out error)) return false;
                    session = prepared.CurrentFloor;
                    _isTransientSession = true;
                    return true;
                }
                catch (Exception exception)
                {
                    error = "F5高层代表入口失败：" + exception.Message;
                    return false;
                }
            }

            // G2 高层快选使用实测入口等级与项目已落地累计状态。完整的
            // Lv9–Lv25 选牌没有实测记录，因此沿用已确认的 Lv2–Lv8 选择，
            // 不伪造未知升级卡；正式流程仍由真实 Run 跨层推进。
            bool hasRepresentativeBuildTemplate =
                RepresentativeBuildTemplateFiles.TryGetValue(
                    targetProfile.contentId ?? string.Empty,
                    out string representativeBuildTemplateFile);
            string recipeFile = null;
            bool hasRecipe = !hasRepresentativeBuildTemplate &&
                (RecipeFiles.TryGetValue(
                    targetProfile.dungeonContentId ?? string.Empty,
                    out recipeFile) ||
                 RecipeFiles.TryGetValue(
                    targetProfile.contentId ?? string.Empty,
                    out recipeFile));
            if (!hasRepresentativeBuildTemplate && !hasRecipe)
            {
                if (!string.Equals(
                        targetProfile.dungeonContentId,
                        YingchoujianDungeonContentId,
                        StringComparison.Ordinal))
                {
                    error = $"地牢 {targetProfile.dungeonContentId} 尚未登记 F5 " +
                            "标准入口 checkpoint recipe。";
                    return false;
                }

                return TryInitializeObservedG2Entry(
                    targetProfile, snapshotId,
                    out session, out source, out error);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                ".."));
            string checkpointFile;
            string checkpointPath;
            if (hasRepresentativeBuildTemplate)
            {
                checkpointFile = representativeBuildTemplateFile;
                if (!TryResolveProjectFile(
                        projectRoot,
                        checkpointFile,
                        out checkpointPath,
                        out error))
                {
                    return false;
                }
            }
            else if (!TryResolveCheckpointFile(
                         projectRoot,
                         targetProfile,
                         recipeFile,
                         out checkpointFile,
                         out checkpointPath,
                         out error))
            {
                return false;
            }

            string payload;
            try
            {
                payload = File.ReadAllText(checkpointPath, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                error = $"读取 F5 快选入口 {checkpointFile} 失败：" +
                        exception.Message;
                return false;
            }

            // Historical artifacts predate Generated species. They are a source
            // of reviewed player/card state, never evidence for today's rosters.
            GameSession.Current.StartNewRun();
            LevelRunSessionStore.Begin(targetProfile, snapshotId);
            string freshTargetPayload = DungeonRunSessionStore.Current.Serialize();
            string rebasedPayload;
            if (ObservedG2Entries.ContainsKey(targetProfile.contentId))
            {
                if (!TryProjectObservedG2State(payload, targetProfile, snapshotId,
                        out DungeonRunSession projectedRun, out error, freshTargetPayload)) return false;
                rebasedPayload = projectedRun.Serialize();
            }
            else if (!DungeonRunQuickPlayLegacyEntry.TryCreate(payload, freshTargetPayload,
                         targetProfile, out rebasedPayload, out error)) return false;
            if (!DungeonRunSessionStore.TryRestore(rebasedPayload, out error))
            {
                GameSession.Current.StartNewRun();
                error = $"恢复 F5 快选入口 {checkpointFile} 失败：{error}";
                return false;
            }

            DungeonRunSession restored = DungeonRunSessionStore.Current;
            if (!TryValidateRestoredEntry(
                    restored,
                    targetProfile,
                    snapshotId,
                    checkpointFile,
                    out error))
            {
                LevelRunSessionStore.Clear();
                GameSession.Current.StartNewRun();
                return false;
            }

            if (string.Equals(
                    targetProfile.contentId,
                    ShuangchalingFinalFloorContentId,
                    StringComparison.Ordinal) &&
                !restored.TryEnsureTransientEntryMinimumHealth(
                    ShuangchalingFinalFloorMinimumHealth,
                    out error))
            {
                LevelRunSessionStore.Clear();
                GameSession.Current.StartNewRun();
                error = $"规范化 F5 第四层入口生命失败：{error}";
                return false;
            }

            session = restored.CurrentFloor;
            source = checkpointFile;
            _isTransientSession = true;
            return true;
        }

        private static bool TryInitializeObservedG2Entry(
            LevelRunProfile targetProfile,
            string snapshotId,
            out LevelRunSession session,
            out string source,
            out string error)
        {
            session = null;
            source = null;
            error = null;
            try
            {
                GameSession.Current.StartNewRun();
                LevelRunSessionStore.Begin(targetProfile, snapshotId);
                string freshPayload = DungeonRunSessionStore.Current.Serialize();
                string projectRoot = Path.GetFullPath(Path.Combine(
                    Application.dataPath, ".."));
                if (!TryResolveProjectFile(projectRoot,
                        YingchoujianRepresentativeBuildTemplateFile,
                        out string representativePath, out error))
                    return false;
                string representativePayload = File.ReadAllText(
                    representativePath, Encoding.UTF8);
                if (!TryProjectObservedG2State(
                        representativePayload, targetProfile, snapshotId,
                        out DungeonRunSession restored, out error,
                        freshPayload))
                    return false;
                session = restored.CurrentFloor;
                source = "observed-g2-entry:" + targetProfile.contentId;
                _isTransientSession = true;
                return true;
            }
            catch (Exception exception)
            {
                LevelRunSessionStore.Clear();
                GameSession.Current.StartNewRun();
                error = $"建立 G2 实测快选入口失败：{exception.Message}";
                return false;
            }
        }

        private static bool TryProjectObservedG2State(
            string representativePayload,
            LevelRunProfile targetProfile,
            string snapshotId,
            out DungeonRunSession restored,
            out string error,
            string freshTargetPayload = null)
        {
            restored = null;
            error = null;
            if (!ObservedG2Entries.TryGetValue(
                    targetProfile.contentId,
                    out ObservedG2EntryProjection projection))
            {
                error = $"楼层 {targetProfile.contentId} 没有实测快选投影。";
                return false;
            }

            string freshPayload = freshTargetPayload;
            if (string.IsNullOrWhiteSpace(freshPayload))
            {
                GameSession.Current.StartNewRun();
                LevelRunSessionStore.Begin(targetProfile, snapshotId);
                freshPayload = DungeonRunSessionStore.Current.Serialize();
            }
            if (!DungeonRunQuickPlayProjection.TryCreate(
                    representativePayload,
                    freshPayload,
                    targetProfile,
                    new DungeonRunQuickPlayEntryState
                    {
                        level = projection.level,
                        experience = projection.experience,
                        xpFractionCarryUnits = projection.xpFractionCarryUnits,
                        jade = projection.jade,
                        runLuck = projection.runLuck,
                        runGreed = projection.runGreed,
                        runGrowth = projection.runGrowth,
                        baseMana = projection.baseMana,
                        baseArmor = projection.baseArmor,
                        might = projection.might,
                        area = projection.area,
                        hand = projection.hand,
                        rewardRerolls = projection.rewardRerolls,
                        maxHealth = projection.maxHealth,
                        currentHealth = projection.currentHealth,
                        pendingForcedOpeningHandCardInstanceId =
                            projection.pendingForcedOpeningHandCardInstanceId,
                        maxFixedCardInstanceId = projection.maxFixedCardInstanceId
                    },
                    out string projectedPayload,
                    out error))
                return false;
            if (!DungeonRunSessionStore.TryRestore(
                    projectedPayload, out error))
                return false;
            restored = DungeonRunSessionStore.Current;
            if (restored == null ||
                !string.Equals(restored.CurrentFloorContentId,
                    targetProfile.contentId, StringComparison.Ordinal))
                return false;
            return ValidateProjectedG2Entry(
                restored, targetProfile, projection, out error);
        }

        private static bool ValidateProjectedG2Entry(
            DungeonRunSession run,
            LevelRunProfile targetProfile,
            ObservedG2EntryProjection expected,
            out string error)
        {
            error = null;
            if (run.CombatProgression.CurrentLevel != Math.Max(1, expected.level) ||
                run.CombatProgression.CurrentExperience !=
                    Math.Max(0, expected.experience) ||
                run.CombatProgression.XpFractionCarryUnits !=
                    Math.Max(0, expected.xpFractionCarryUnits))
            {
                error = $"G2 快选入口 {targetProfile.contentId} 的等级/经验投影不一致。";
                return false;
            }
            if (run.RunProgression.RunJadeEarned != Math.Max(0, expected.jade) ||
                run.RunProgression.RunLuckBonus != Math.Max(0, expected.runLuck) ||
                run.RunProgression.RunGreedBonus != Math.Max(0, expected.runGreed) ||
                run.RunProgression.RunGrowthBonus != Math.Max(0, expected.runGrowth))
            {
                error = $"G2 快选入口 {targetProfile.contentId} 的 Run 增益投影不一致。";
                return false;
            }
            if (run.BaseManaBonus != Math.Max(0, expected.baseMana) ||
                run.BaseArmorPerTurnBonus != Math.Max(0, expected.baseArmor) ||
                run.MightBonus != Math.Max(0, expected.might) ||
                run.AreaBonus != Math.Max(0, expected.area) ||
                run.HandLimitBonus != Math.Max(0, expected.hand) ||
                run.RewardRerollsRemaining != Math.Max(0, expected.rewardRerolls))
            {
                error = $"G2 快选入口 {targetProfile.contentId} 的楼层增益投影不一致。";
                return false;
            }
            if (run.MaxHealth != Math.Max(1, expected.maxHealth) ||
                run.CurrentHealth != Math.Max(
                    0,
                    Math.Min(Math.Max(1, expected.maxHealth), expected.currentHealth)))
            {
                error = $"G2 快选入口 {targetProfile.contentId} 的生命投影不一致。";
                return false;
            }
            if (run.PendingForcedOpeningHandCardInstanceId != Math.Max(
                    0, expected.pendingForcedOpeningHandCardInstanceId))
            {
                error = $"G2 快选入口 {targetProfile.contentId} 的首手卡状态不一致。";
                return false;
            }
            if (expected.expectedRunCardCount > 0 &&
                run.RunCardCount != expected.expectedRunCardCount)
            {
                error = $"G2 快选入口 {targetProfile.contentId} 的牌组数量不一致：" +
                        $"期望 {expected.expectedRunCardCount}，实际 {run.RunCardCount}。";
                return false;
            }
            if (run.RunProgression.CardPlayGrowth.Enabled ||
                run.RunProgression.CardPlayGrowth.TotalRealPlays != 0 ||
                run.RunProgression.CardPlayGrowth.SettledCycles != 0 ||
                run.RunProgression.CardPlayGrowth.BaseManaBonus != 0)
            {
                error = $"G2 快选入口 {targetProfile.contentId} 残留了卡牌成长状态。";
                return false;
            }
            return true;
        }

        private static bool TryResolveCheckpointFile(
            string projectRoot,
            LevelRunProfile targetProfile,
            string recipeFile,
            out string checkpointFile,
            out string checkpointPath,
            out string error)
        {
            checkpointFile = null;
            checkpointPath = null;
            error = null;
            if (string.IsNullOrWhiteSpace(recipeFile))
            {
                error = $"楼层 {targetProfile.contentId} 尚未登记 F5 " +
                        "标准入口 checkpoint recipe。";
                return false;
            }
            if (!TryResolveProjectFile(
                    projectRoot,
                    recipeFile,
                    out string recipePath,
                    out error))
            {
                return false;
            }

            CheckpointRecipeManifest recipe;
            try
            {
                string recipeJson = File.ReadAllText(recipePath, Encoding.UTF8);
                recipe = JsonUtility.FromJson<CheckpointRecipeManifest>(
                    recipeJson);
            }
            catch (Exception exception)
            {
                error = $"读取 F5 checkpoint recipe {recipeFile} 失败：" +
                        exception.Message;
                return false;
            }
            if (recipe?.outputs == null)
            {
                error = $"F5 checkpoint recipe {recipeFile} 没有 outputs。";
                return false;
            }

            for (int i = 0; i < recipe.outputs.Length; i++)
            {
                CheckpointOutput output = recipe.outputs[i];
                if (output == null ||
                    !string.Equals(
                        output.floorContentId,
                        targetProfile.contentId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(checkpointFile))
                {
                    error = $"F5 checkpoint recipe {recipeFile} 为楼层 " +
                            $"{targetProfile.contentId} 声明了重复 output。";
                    return false;
                }
                checkpointFile = output.checkpointFile;
            }
            if (string.IsNullOrWhiteSpace(checkpointFile))
            {
                error = $"F5 checkpoint recipe {recipeFile} 没有楼层 " +
                        $"{targetProfile.contentId} 的标准入口。";
                return false;
            }
            return TryResolveProjectFile(
                projectRoot,
                checkpointFile,
                out checkpointPath,
                out error);
        }

        private static bool TryResolveProjectFile(
            string projectRoot,
            string relativePath,
            out string fullPath,
            out string error)
        {
            fullPath = null;
            error = null;
            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath))
            {
                error = $"F5 标准入口文件必须使用仓库相对路径：{relativePath}";
                return false;
            }

            string normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(
                normalizedRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(
                    rootPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = $"F5 标准入口文件越出仓库：{relativePath}";
                return false;
            }
            if (!File.Exists(candidate))
            {
                error = $"找不到 F5 标准入口文件：{relativePath}";
                return false;
            }
            fullPath = candidate;
            return true;
        }

        private static bool TryValidateRestoredEntry(
            DungeonRunSession restored,
            LevelRunProfile targetProfile,
            string snapshotId,
            string checkpointFile,
            out string error)
        {
            error = null;
            if (restored == null)
            {
                error = $"F5 标准入口 {checkpointFile} 未恢复出 Run。";
                return false;
            }
            if (!string.Equals(
                    restored.CurrentFloorContentId,
                    targetProfile.contentId,
                    StringComparison.Ordinal))
            {
                error = $"F5 标准入口 {checkpointFile} 当前层为 " +
                        $"{restored.CurrentFloorContentId}，目标层为 " +
                        $"{targetProfile.contentId}。";
                return false;
            }
            if (!string.Equals(
                    restored.CurrentFloor.SnapshotId,
                    snapshotId,
                    StringComparison.Ordinal))
            {
                error = $"F5 标准入口 {checkpointFile} 快照为 " +
                        $"{restored.CurrentFloor.SnapshotId}，目标快照为 " +
                        $"{snapshotId}。";
                return false;
            }
            if (!restored.HasPlayerState ||
                restored.CurrentHealth <= 0 ||
                restored.RunCardCount <= 0)
            {
                error = $"F5 标准入口 {checkpointFile} 缺少存活角色或正式牌组。";
                return false;
            }

            LevelRunSession floor = restored.CurrentFloor;
            if (floor.ActiveEncounter.HasValue ||
                !string.IsNullOrWhiteSpace(floor.ActiveInteractionNodeId) ||
                floor.TryGetPendingReturn(out _))
            {
                error = $"F5 标准入口 {checkpointFile} 不是静止的地图入口状态。";
                return false;
            }

            CombatProgressionState progression = restored.CombatProgression;
            int requiredExperience = progression.GetRequiredExperience(
                progression.CurrentLevel);
            if (progression.CurrentExperience >= requiredExperience)
            {
                error = $"F5 标准入口 {checkpointFile} 仍有未结算升级。";
                return false;
            }
            return HasEarlierFloorChain(restored, targetProfile, out error);
        }

        private static bool HasEarlierFloorChain(
            DungeonRunSession restored,
            LevelRunProfile targetProfile,
            out string error)
        {
            error = null;
            string cursor = restored.FirstFloorContentId;
            bool advanced = false;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrWhiteSpace(cursor) && visited.Add(cursor))
            {
                if (string.Equals(
                        cursor,
                        targetProfile.contentId,
                        StringComparison.Ordinal))
                {
                    if (advanced)
                        return true;
                    break;
                }

                LevelRunProfile floor;
                try
                {
                    floor = LevelRunProfileCatalog.Get(cursor);
                }
                catch (Exception exception)
                {
                    error = $"F5 标准入口的前层 {cursor} 无法解析：" +
                            exception.Message;
                    return false;
                }
                cursor = floor?.nextFloorContentId;
                advanced = true;
            }

            error = $"F5 标准入口未证明从 {restored.FirstFloorContentId} 到 " +
                    $"{targetProfile.contentId} 的前层成长链。";
            return false;
        }

        [Serializable]
        private sealed class CheckpointRecipeManifest
        {
            public CheckpointOutput[] outputs;
        }

        [Serializable]
        private sealed class CheckpointOutput
        {
            public string floorContentId;
            public string checkpointFile;
        }
#endif
    }
}
