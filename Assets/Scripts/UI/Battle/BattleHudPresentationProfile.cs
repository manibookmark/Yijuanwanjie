using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 战斗 HUD 的权威表现资产。
    /// 场景只提供战斗状态，调用方不需要知道素材路径、内部层级或像素尺寸。
    /// </summary>
    [CreateAssetMenu(
        fileName = "DefaultBattleHudPresentation",
        menuName = "万界长卷/UI/战斗 HUD 表现配置")]
    public sealed class BattleHudPresentationProfile : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public const string DefaultResourcePath =
            "UI/Battle/DefaultBattleHudPresentation";

        [Header("身份与版本")]
        public int schemaVersion = CurrentSchemaVersion;
        public string profileId = "ui.battle-hud.standard.v1";
        [Min(1)] public int revision = 1;

        [Header("顶部进度")]
        public Sprite progressEmpty;
        public Sprite progressFull;
        public Vector2 progressSize = new Vector2(608f, 68f);
        public Vector2 progressOffset = new Vector2(0f, -18f);

        [Header("左侧英灵与状态")]
        public Sprite statusPlate;
        public Sprite heroAvatar;
        public Sprite heroAvatarFrame;
        public Sprite heroInfoPanel;
        public Sprite heroInfoLeftCap;
        public Sprite heroInfoCenterTile;
        public Sprite heroInfoRightCap;
        public Vector2 statusPlateSize = new Vector2(140f, 64f);
        public Vector2 heroGroupSize = new Vector2(390f, 140f);
        public Vector2 heroGroupOffset = new Vector2(24f, -65f);

        [Header("气血与灵力")]
        public Sprite hpEmpty;
        public Sprite hpFull;
        public Sprite mpEmpty;
        public Sprite mpFull;
        public Vector2 resourceSize = new Vector2(270f, 268f);
        public Vector2 resourceEdgeOffset = new Vector2(12f, 10f);

        [Header("增益图标")]
        public Vector2 buffIconSize = new Vector2(48f, 48f);
        public float buffIconSpacing = 6f;
        public Vector2 leftBuffBarOffset = new Vector2(24f, 80f);
        public Vector2 rightBuffBarOffset = new Vector2(-24f, 80f);
        public Color buffBackgroundColor = new Color(0.1f, 0.08f, 0.06f, 0.68f);
        public Color buffActiveColor = Color.white;
        public Color buffInactiveColor = new Color(0.15f, 0.12f, 0.08f, 0.5f);
        [Min(1)] public int buffValueFontSize = 14;

        [Header("操作按钮")]
        public Sprite drawNormal;
        public Sprite drawHover;
        public Sprite playAllNormal;
        public Sprite playAllHover;
        public Sprite endTurnNormal;
        public Sprite endTurnHover;
        public Sprite discardNormal;
        public Sprite discardHover;
        public Vector2 actionButtonSize = new Vector2(66f, 62f);

        [Header("文字")]
        public Font titleFont;
        public Font bodyFont;
        [Min(1)] public int progressFontSize = 26;
        [Min(1)] public int resourceFontSize = 34;
        [Min(1)] public int statusFontSize = 17;
        public Color primaryTextColor = new Color(1f, 0.86f, 0.48f);
        public Color resourceTextColor = Color.white;

        private static BattleHudPresentationProfile _runtimeFallback;

        public static BattleHudPresentationProfile LoadDefault()
        {
            BattleHudPresentationProfile profile =
                Resources.Load<BattleHudPresentationProfile>(
                    DefaultResourcePath);
            return profile != null ? profile : RuntimeFallback;
        }

        public void ResetLayoutToDefaults()
        {
            schemaVersion = CurrentSchemaVersion;
            profileId = "ui.battle-hud.standard.v1";
            revision = Mathf.Max(1, revision);
            progressSize = new Vector2(608f, 68f);
            progressOffset = new Vector2(0f, -18f);
            statusPlateSize = new Vector2(140f, 64f);
            heroGroupSize = new Vector2(390f, 140f);
            heroGroupOffset = new Vector2(24f, -65f);
            resourceSize = new Vector2(270f, 268f);
            resourceEdgeOffset = new Vector2(12f, 10f);
            actionButtonSize = new Vector2(66f, 62f);
            progressFontSize = 26;
            resourceFontSize = 34;
            statusFontSize = 17;
            primaryTextColor = new Color(1f, 0.86f, 0.48f);
            resourceTextColor = Color.white;
            buffIconSize = new Vector2(48f, 48f);
            buffIconSpacing = 6f;
            leftBuffBarOffset = new Vector2(24f, 80f);
            rightBuffBarOffset = new Vector2(-24f, 80f);
            buffBackgroundColor = new Color(0.1f, 0.08f, 0.06f, 0.68f);
            buffActiveColor = Color.white;
            buffInactiveColor = new Color(0.15f, 0.12f, 0.08f, 0.5f);
            buffValueFontSize = 14;
        }

        public List<string> ValidateProfile(bool requireSprites = true)
        {
            var errors = new List<string>();
            if (schemaVersion != CurrentSchemaVersion)
                errors.Add($"schemaVersion 必须为 {CurrentSchemaVersion}。");
            if (string.IsNullOrWhiteSpace(profileId))
                errors.Add("profileId 不能为空。");
            if (!IsPositive(progressSize))
                errors.Add("progressSize 必须大于 0。");
            if (!IsPositive(statusPlateSize))
                errors.Add("statusPlateSize 必须大于 0。");
            if (!IsPositive(heroGroupSize))
                errors.Add("heroGroupSize 必须大于 0。");
            if (!IsPositive(resourceSize))
                errors.Add("resourceSize 必须大于 0。");
            if (!IsPositive(actionButtonSize))
                errors.Add("actionButtonSize 必须大于 0。");

            if (requireSprites)
            {
                Require(progressEmpty, nameof(progressEmpty), errors);
                Require(progressFull, nameof(progressFull), errors);
                Require(statusPlate, nameof(statusPlate), errors);
                Require(heroAvatar, nameof(heroAvatar), errors);
                Require(heroAvatarFrame, nameof(heroAvatarFrame), errors);
                Require(heroInfoPanel, nameof(heroInfoPanel), errors);
                Require(heroInfoLeftCap, nameof(heroInfoLeftCap), errors);
                Require(heroInfoCenterTile, nameof(heroInfoCenterTile), errors);
                Require(heroInfoRightCap, nameof(heroInfoRightCap), errors);
                Require(hpEmpty, nameof(hpEmpty), errors);
                Require(hpFull, nameof(hpFull), errors);
                Require(mpEmpty, nameof(mpEmpty), errors);
                Require(mpFull, nameof(mpFull), errors);
                Require(drawNormal, nameof(drawNormal), errors);
                Require(drawHover, nameof(drawHover), errors);
                Require(playAllNormal, nameof(playAllNormal), errors);
                Require(playAllHover, nameof(playAllHover), errors);
                Require(endTurnNormal, nameof(endTurnNormal), errors);
                Require(endTurnHover, nameof(endTurnHover), errors);
                Require(discardNormal, nameof(discardNormal), errors);
                Require(discardHover, nameof(discardHover), errors);
            }

            return errors;
        }

        private static BattleHudPresentationProfile RuntimeFallback
        {
            get
            {
                if (_runtimeFallback != null) return _runtimeFallback;
                _runtimeFallback =
                    CreateInstance<BattleHudPresentationProfile>();
                _runtimeFallback.name = "RuntimeBattleHudPresentationFallback";
                _runtimeFallback.hideFlags = HideFlags.HideAndDontSave;
                _runtimeFallback.ResetLayoutToDefaults();
                return _runtimeFallback;
            }
        }

        private static bool IsPositive(Vector2 value)
        {
            return value.x > 0f && value.y > 0f;
        }

        private static void Require(
            Object value,
            string fieldName,
            ICollection<string> errors)
        {
            if (value == null)
                errors.Add($"{fieldName} 未绑定。");
        }
    }
}
