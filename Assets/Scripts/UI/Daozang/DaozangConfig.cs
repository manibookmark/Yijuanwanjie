using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.UI.Daozang
{
    /// <summary>
    /// 道藏部门、图标与文案配置；升级规则统一来自 Meta。
    /// 五部：杀伐(shafa) / 玄机(xuanji) / 灵台(lingtai) / 固命(guming) / 运势(yunshi)
    /// </summary>
    public static class DaozangConfig
    {
        public const string GlyphPathPrefix = "Sprites/UI/Daozang/glyphs/";
        public const string BattleGlyphPathPrefix = "Sprites/UI/Battle/BuffIcons/";

        public readonly struct JingjueInfo
        {
            public readonly string Id;
            public readonly string Name;
            public readonly string Department;
            public readonly string EffectText;
            public readonly int MaxLevel;
            public readonly int PricePerLevel;
            public readonly EconomyPriceTier PriceTier;
            public readonly int AmountPerLevel;
            public readonly UnlockTier Tier;
            public readonly string GlyphSpritePath;

            public JingjueInfo(
                string id, string name, string department,
                string effectText, int maxLevel, int pricePerLevel,
                int amountPerLevel,
                UnlockTier tier, string glyphSpritePath)
                : this(id, department, effectText, glyphSpritePath)
            {
                // Preserve the existing presentation constructor. Gameplay
                // values always resolve by stable ID from the Meta catalog.
                Name = name;
            }

            public JingjueInfo(string id, string department, string effectText, string glyphSpritePath)
            {
                DaozangProgressionRules.Definition definition = DaozangProgressionRules.Get(id);
                Id = definition.Id;
                Name = definition.Name;
                Department = department;
                EffectText = effectText;
                MaxLevel = definition.MaxLevel;
                PricePerLevel = definition.PricePerLevel;
                PriceTier = definition.PriceTier;
                AmountPerLevel = definition.AmountPerLevel;
                Tier = definition.Tier;
                GlyphSpritePath = glyphSpritePath;
            }
        }

        public readonly struct DepartmentInfo
        {
            public readonly string Id;
            public readonly string Name;
            public readonly string HeaderSpritePath;
            public readonly string BodySpritePath;
            public readonly JingjueInfo[] Jingjue;

            public DepartmentInfo(
                string id, string name,
                string headerSpritePath, string bodySpritePath,
                JingjueInfo[] jingjue)
            {
                Id = id;
                Name = name;
                HeaderSpritePath = headerSpritePath;
                BodySpritePath = bodySpritePath;
                Jingjue = jingjue;
            }
        }

        // 11 个战斗复用图标在 BuffIcons 目录，8 个道藏独有在 Daozang/glyphs
        private static string GlyphPath(string id)
        {
            switch (id)
            {
                case "might":
                case "area":
                case "amount":
                case "duration":
                case "hand":
                case "recovery":
                case "growth":
                case "greed":
                case "luck":
                case "curse":
                case "revival":
                    return BattleGlyphPathPrefix + id;
                default:
                    return GlyphPathPrefix + id;
            }
        }

        public static readonly DepartmentInfo[] Departments =
        {
            new DepartmentInfo(
                "shafa", "杀伐部",
                "Sprites/UI/Daozang/department-headers/department-shafa-header",
                "Sprites/UI/Daozang/talisman-bodies/department-shafa",
                new[]
                {
                    new JingjueInfo("might", "shafa", "每级伤害 +25%", GlyphPath("might")),
                    new JingjueInfo("area", "shafa", "每级溅射 +10%", GlyphPath("area")),
                    new JingjueInfo("amount", "shafa", "每级数量 +1", GlyphPath("amount")),
                }),
            new DepartmentInfo(
                "xuanji", "玄机部",
                "Sprites/UI/Daozang/department-headers/department-xuanji-header",
                "Sprites/UI/Daozang/talisman-bodies/department-xuanji",
                new[]
                {
                    new JingjueInfo("reroll", "xuanji", "每级重掷 +2次", GlyphPath("reroll")),
                    new JingjueInfo("skip", "xuanji", "每级弃卷 +2次", GlyphPath("skip")),
                    new JingjueInfo("banish", "xuanji", "每级焚卷 +2选项", GlyphPath("banish")),
                    new JingjueInfo("hand", "xuanji", "每级回合抽牌数 +1", GlyphPath("hand")),
                    new JingjueInfo("magnet", "xuanji", "抽牌效果 +1", GlyphPath("magnet")),
                }),
            new DepartmentInfo(
                "lingtai", "灵台部",
                "Sprites/UI/Daozang/department-headers/department-lingtai-header",
                "Sprites/UI/Daozang/talisman-bodies/department-lingtai",
                new[]
                {
                    new JingjueInfo("duration", "lingtai", "每级英灵时长 +1", GlyphPath("duration")),
                    new JingjueInfo("crawler-slot", "lingtai", "每级灵位 +1", GlyphPath("crawler-slot")),
                    new JingjueInfo("mana", "lingtai", "每级灵力上限 +1", GlyphPath("mana")),
                    new JingjueInfo("growth", "lingtai", "每级经验 +20%", GlyphPath("growth")),
                }),
            new DepartmentInfo(
                "guming", "固命部",
                "Sprites/UI/Daozang/department-headers/department-guming-header",
                "Sprites/UI/Daozang/talisman-bodies/department-guming",
                new[]
                {
                    new JingjueInfo("max-health", "guming", "每级气血上限 +10%", GlyphPath("max-health")),
                    new JingjueInfo("recovery", "guming", "每级战后回血 +1", GlyphPath("recovery")),
                    new JingjueInfo("armor", "guming", "每级护甲 +2", GlyphPath("armor")),
                    new JingjueInfo("revival", "guming", "濒死复活 +1次", GlyphPath("revival")),
                }),
            new DepartmentInfo(
                "yunshi", "运势部",
                "Sprites/UI/Daozang/department-headers/department-yunshi-header",
                "Sprites/UI/Daozang/talisman-bodies/department-yunshi",
                new[]
                {
                    new JingjueInfo("curse", "yunshi", "每级敌人强度 +20%", GlyphPath("curse")),
                    new JingjueInfo("luck", "yunshi", "每级天运 +25%", GlyphPath("luck")),
                    new JingjueInfo("greed", "yunshi", "每级灵玉 +25%", GlyphPath("greed")),
                }),
        };

        /// <summary>获取经诀当前等级</summary>
        public static int GetLevel(MetaProgression meta, string idOrLegacyName)
            => DaozangProgressionRules.GetLevel(meta, idOrLegacyName);

        public static int GetLevelPrice(JingjueInfo info, int nextLevel)
            => DaozangProgressionRules.GetLevelPrice(info.Id, nextLevel);

        /// <summary>经诀是否被锁定（Tier 未解锁）</summary>
        public static bool IsLocked(MetaProgression meta, UnlockTier tier)
            => DaozangProgressionRules.IsLocked(meta, tier);

        /// <summary>获取所有经诀的扁平列表</summary>
        public static System.Collections.Generic.List<JingjueInfo> AllJingjue()
        {
            var list = new System.Collections.Generic.List<JingjueInfo>();
            foreach (var dept in Departments)
                list.AddRange(dept.Jingjue);
            return list;
        }

        /// <summary>从 JingjueInfo 创建临时 PowerUpConfig（供 BuyPowerUp 使用）</summary>
        public static PowerUpConfig CreatePowerUpConfig(JingjueInfo info)
            => DaozangProgressionRules.CreatePowerUpConfig(info.Id);

        /// <summary>
        /// 兼容既有战斗入口；Meta 统一拥有经诀属性投影。
        /// </summary>
        public static void ApplyToPlayerStats(
            MetaProgression meta,
            WanjieScroll.Core.PlayerStats stats)
            => DaozangProgressionRules.ApplyToPlayerStats(meta, stats);
    }
}
