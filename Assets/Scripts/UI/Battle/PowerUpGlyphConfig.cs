using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// 属性表现的元数据配置：唯一拥有 StatType 的玩家名称、词义与 HUD 图标。
    /// 左侧 6 个为战斗临时增益，右侧 5 个为永久增益。
    /// 卡牌与结算面板复用这里的词义，不保存独立文案或战斗状态。
    /// </summary>
    public static class PowerUpGlyphConfig
    {
        public const string SpritePathPrefix = "Sprites/UI/Battle/BuffIcons/";

        // Canvas reference units: every stat/resource/resident transfer uses
        // one core size, independent of the destination icon's layout.
        public const float GainGlyphSize = 96f;
        public const float GainTrailSize = GainGlyphSize * 0.72f;

        public readonly struct GlyphInfo
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string SpritePath;
            public readonly StatType? StatType;
            public readonly bool IsPercentage;
            public readonly string Description;

            public GlyphInfo(
                string id,
                string displayName,
                StatType? statType,
                bool isPercentage,
                string description = "")
            {
                Id = id;
                DisplayName = displayName;
                SpritePath = SpritePathPrefix + id;
                StatType = statType;
                IsPercentage = isPercentage;
                Description = statType.HasValue
                    ? GetStatDescription(statType.Value) : description;
            }
        }

        /// <summary>
        /// Visual semantics for a stat gain. Cards only configure StatType;
        /// this catalog owns the matching HUD color language for every card.
        /// </summary>
        public readonly struct GainPresentationStyle
        {
            public readonly Color CoreColor;
            public readonly Color TrailColor;

            public GainPresentationStyle(
                Color coreColor,
                Color trailColor)
            {
                CoreColor = coreColor;
                TrailColor = trailColor;
            }
        }

        public static GainPresentationStyle GetGainPresentationStyle(
            StatType stat)
        {
            return stat switch
            {
                StatType.FengRui => new GainPresentationStyle(
                    new Color(1f, 0.96f, 0.82f, 1f),
                    new Color(0.28f, 0.88f, 0.52f, 0.72f)),
                StatType.Amount => new GainPresentationStyle(
                    new Color(0.86f, 0.96f, 1f, 1f),
                    new Color(0.35f, 0.74f, 1f, 0.72f)),
                StatType.BengLie => new GainPresentationStyle(
                    new Color(1f, 0.88f, 0.68f, 1f),
                    new Color(1f, 0.42f, 0.18f, 0.72f)),
                StatType.NingShen => new GainPresentationStyle(
                    new Color(0.92f, 0.88f, 1f, 1f),
                    new Color(0.62f, 0.48f, 0.96f, 0.72f)),
                StatType.TianYun => new GainPresentationStyle(
                    new Color(1f, 0.95f, 0.72f, 1f),
                    new Color(0.94f, 0.72f, 0.2f, 0.72f)),
                StatType.Recovery => new GainPresentationStyle(
                    new Color(0.72f, 1f, 0.88f, 1f),
                    new Color(0.24f, 0.82f, 0.62f, 0.72f)),
                _ => new GainPresentationStyle(
                    Color.white,
                    new Color(0.72f, 0.84f, 0.76f, 0.68f)),
            };
        }

        /// <summary>按属性类型查找唯一的 HUD 图形语义。</summary>
        public static bool TryGetGlyph(StatType stat, out GlyphInfo glyph)
        {
            for (int i = 0; i < LeftBarGlyphs.Length; i++)
            {
                if (LeftBarGlyphs[i].StatType == stat)
                {
                    glyph = LeftBarGlyphs[i];
                    return true;
                }
            }
            for (int i = 0; i < RightBarGlyphs.Length; i++)
            {
                if (RightBarGlyphs[i].StatType == stat)
                {
                    glyph = RightBarGlyphs[i];
                    return true;
                }
            }

            glyph = default;
            return false;
        }

        /// <summary>所有属性表现共用的玩家名称；没有图标的属性也在此定义。</summary>
        public static string GetStatDisplayName(StatType stat)
        {
            if (TryGetGlyph(stat, out GlyphInfo glyph))
                return glyph.DisplayName;
            return stat switch
            {
                StatType.ChouPai => "引卷",
                StatType.FaLi => "灵力",
                StatType.HuJia => "护甲",
                StatType.Recovery => "调息",
                _ => stat.ToString()
            };
        }

        /// <summary>通用属性词义；不附加卡牌生效范围或复述具体增量。</summary>
        public static string GetStatDescription(StatType stat) => stat switch
        {
            StatType.FengRui => "提高卡牌伤害",
            StatType.Amount => "增加攻击物数量",
            StatType.BengLie => "提高攻击的溅射伤害",
            StatType.NingShen => "给所有在场英灵增加触发次数；无人时留给下次召唤",
            StatType.ShouPai => "每回合开始时抽牌数",
            StatType.ChouPai => "抽牌效果额外获得的牌数",
            StatType.TianYun => "提高暴击几率，升级奖励更容易多一个选项，也更容易遇到高品质灵印",
            StatType.LianYu => "提高灵玉获取",
            StatType.MingWu => "提高经验获取",
            StatType.HunHun => "暂未启用",
            StatType.FaLi => "用于支付出牌费用",
            StatType.HuJia => "每点抵挡1点伤害，抵挡后消耗",
            StatType.ZhaoSha => "提高敌人伤害与击败敌人获得的经验",
            StatType.Recovery => "战斗结束时恢复气血",
            _ => "改变对应战斗属性"
        };

        /// <summary>格式化一次属性增量，例如“数量 +1”“锋锐 +10%”。</summary>
        public static string FormatDelta(StatType stat, int amount)
        {
            string sign = amount >= 0 ? "+" : string.Empty;
            if (TryGetGlyph(stat, out GlyphInfo glyph))
            {
                string suffix = glyph.IsPercentage ? "%" : string.Empty;
                return $"{glyph.DisplayName} {sign}{amount}{suffix}";
            }
            return $"{GetStatDisplayName(stat)} {sign}{amount}";
        }

        /// <summary>左侧临时增益（6 个）</summary>
        public static readonly GlyphInfo[] LeftBarGlyphs =
        {
            new GlyphInfo("might",     "锋锐", StatType.FengRui, true),
            new GlyphInfo("area",      "裂空", StatType.BengLie, true),
            new GlyphInfo("amount",    "数量", StatType.Amount,  false),
            new GlyphInfo("duration",  "凝神", StatType.NingShen, false),
            new GlyphInfo("hand",      "展卷", StatType.ShouPai, false),
            new GlyphInfo("recovery",  "调息", StatType.Recovery, false),
        };

        /// <summary>右侧永久增益（5 个）</summary>
        public static readonly GlyphInfo[] RightBarGlyphs =
        {
            new GlyphInfo("growth",  "明悟", StatType.MingWu,  true),
            new GlyphInfo("greed",   "敛玉", StatType.LianYu,  true),
            new GlyphInfo("luck",    "天运", StatType.TianYun, true),
            new GlyphInfo("curse",   "招煞", StatType.ZhaoSha, true),
            new GlyphInfo("revival", "还魂", StatType.HunHun,  false),
        };

        /// <summary>回合抽牌基础值；历史常量名保留，不表示手牌容量。</summary>
        public const int BaseHandLimit = 5;

        /// <summary>根据 GlyphInfo 和 PlayerStats 读取对应的值</summary>
        public static int GetStatValue(PlayerStats stats, GlyphInfo info)
        {
            if (stats == null) return 0;

            // recovery 无 StatType 映射，直接读字段
            if (info.Id == "recovery")
                return stats.recovery;

            if (info.StatType == null) return 0;
            return info.StatType.Value switch
            {
                Core.StatType.FengRui   => stats.fengRui,
                Core.StatType.Amount   => stats.amount,
                Core.StatType.BengLie  => stats.bengLie,
                Core.StatType.NingShen   => stats.ningShen,
                Core.StatType.ShouPai  => stats.shouPai,
                Core.StatType.ChouPai  => stats.yinJuan,
                Core.StatType.TianYun   => stats.tianYun,
                Core.StatType.LianYu  => stats.lianYu,
                Core.StatType.MingWu   => stats.mingWu,
                Core.StatType.HunHun   => stats.hunHun,
                Core.StatType.FaLi     => stats.faLi,
                Core.StatType.HuJia    => stats.armor,
                Core.StatType.ZhaoSha  => stats.zhaoSha,
                _ => 0,
            };
        }

        /// <summary>格式化数值显示文本</summary>
        public static string FormatValue(GlyphInfo info, int rawValue)
        {
            if (info.Id == "hand")
                return $"+{Mathf.Max(0, rawValue - BaseHandLimit)}";

            if (info.Id == "revival")
                return rawValue > 0 ? "✓" : "";

            if (info.IsPercentage)
                return $"+{rawValue}%";

            return $"+{rawValue}";
        }

        /// <summary>格式化描述文本（将 {value} 替换为实际值）</summary>
        public static string FormatDescription(GlyphInfo info, int rawValue)
        {
            if (string.IsNullOrEmpty(info.Description))
                return info.DisplayName;

            int displayValue = info.Id == "hand"
                ? Mathf.Max(0, rawValue - BaseHandLimit)
                : rawValue;

            return info.Description.Replace("{value}", displayValue.ToString());
        }
    }
}
