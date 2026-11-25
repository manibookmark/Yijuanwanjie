using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;

namespace WanjieScroll.Progression
{
    /// <summary>
    /// Resolves the formal rules for one battle from its explicit account and
    /// current floor receipts. No scene or ambient player account is consulted.
    /// </summary>
    public static class RunCombatRules
    {
        public const string TutorialLevelContentId = "level.tutorial.huaguoshan.v1";

        public static CombatRuleSet Create(MetaProgression meta, LevelRunSession activeRun,
            DungeonRunSession dungeonRun = null)
        {
            var rules = new CombatRuleSet();
            if (meta != null && meta.hasEquippedArcanaEffect)
            {
                rules.shieldBashEnabled = meta.equippedArcanaEffect == ArcanaEffect.ShieldBash;
                rules.experimentalMedicineEnabled =
                    meta.equippedArcanaEffect ==
                    ArcanaEffect.ExperimentalMedicine;
                rules.preservePlayerArmorBetweenTurns =
                    meta.equippedArcanaEffect == ArcanaEffect.YourShieldMyLiege;
            }
            if (dungeonRun != null)
            {
                if (!ReferenceEquals(dungeonRun.CurrentFloor, activeRun))
                    throw new ArgumentException("战斗规则必须读取同一Run的当前楼层。", nameof(dungeonRun));
                foreach (DungeonRunArcanaDefinition acquired in dungeonRun.Arcanas.Active)
                {
                    DungeonRunArcanaCatalogResolver.RequireImplementedDefinition(acquired);
                    rules.shieldBashEnabled |= acquired.Rule == DungeonRunArcanaRule.ShieldBash;
                    rules.experimentalMedicineEnabled |= acquired.Rule == DungeonRunArcanaRule.ExperimentalMedicine;
                }
            }
            List<RelicType> relics = meta?.unlockedRelicTypes ?? new List<RelicType>();
            bool runGrantedJiudie = activeRun != null &&
                activeRun.HasAppliedInteractionTarget(LevelInteractionEffectKind.GrantRelic, RelicContentIds.Jiudie);
            bool runGrantedWuxingDisc = activeRun != null &&
                activeRun.HasAppliedInteractionTarget(LevelInteractionEffectKind.GrantRelic, RelicContentIds.WuxingDisc);
            // Tutorial replays follow this Run's pickups, even when a previous
            // clear permanently unlocked the relics. Standalone combat keeps
            // its existing defaults; a formal Run requires the actual gates.
            bool isTutorialRun = activeRun != null && string.Equals(
                activeRun.Profile.contentId, TutorialLevelContentId, StringComparison.Ordinal);
            bool metaJiudie = !isTutorialRun &&
                (relics.Contains(RelicType.ComboStack) || (meta?.IsRelicUnlocked(RelicContentIds.Jiudie) ?? false));
            bool metaWuxingDisc = !isTutorialRun &&
                (relics.Contains(RelicType.WuxingDisc) || (meta?.IsRelicUnlocked(RelicContentIds.WuxingDisc) ?? false));
            rules.comboEnabled = activeRun == null || runGrantedJiudie || metaJiudie;
            rules.wuxingEnabled = activeRun == null || runGrantedWuxingDisc || metaWuxingDisc;
            rules.bossOverkillEnabled = relics.Contains(RelicType.Overkill);
            rules.normalOverkillEnabled = false;
            rules.ultraOverkillEnabled = relics.Contains(RelicType.UltimateUltraOverkill);
#if UNITY_EDITOR
            ApplyEditorDebugOverrides(rules);
#endif
            return rules;
        }

#if UNITY_EDITOR
        /// <summary>调试开关的 PlayerPrefs 键（仅编辑器读取，菜单在 Editor 程序集写入）。</summary>
        public const string ForceComboPrefKey =
            "WanjieScroll.Debug.ForceComboEnabled";
        public const string ForceWuxingPrefKey =
            "WanjieScroll.Debug.ForceWuxingEnabled";

        /// <summary>
        /// 编辑器调试入口：仅 Play 模式读取调试偏好并强制规则开启。
        /// EditMode 测试（isPlaying=false）永远读不到该偏好，门禁断言不受影响。
        /// </summary>
        private static void ApplyEditorDebugOverrides(CombatRuleSet rules)
        {
            if (!Application.isPlaying) return;
            ApplyDebugOverrides(
                rules,
                PlayerPrefs.GetInt(ForceComboPrefKey, 0) == 1,
                PlayerPrefs.GetInt(ForceWuxingPrefKey, 0) == 1);
        }

        /// <summary>
        /// 调试开关本体：把被门禁关闭的规则强制打开（只升不降），
        /// 并在日志中留痕，便于从 Editor.log 反查连击为何开启。
        /// </summary>
        public static void ApplyDebugOverrides(
            CombatRuleSet rules,
            bool forceCombo,
            bool forceWuxing)
        {
            if (forceCombo && !rules.comboEnabled)
            {
                rules.comboEnabled = true;
                Debug.Log(
                    "[Diag][Rules] 编辑器调试开关已强制开启叠势（连击）；" +
                    "正式九叠门禁在本存档下为关闭。");
            }
            if (forceWuxing && !rules.wuxingEnabled)
            {
                rules.wuxingEnabled = true;
                Debug.Log(
                    "[Diag][Rules] 编辑器调试开关已强制开启五行；" +
                    "正式五行盘门禁在本存档下为关闭。");
            }
        }
#endif
    }
}
