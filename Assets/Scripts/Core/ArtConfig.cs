using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 美术资源配置（全局美术管线入口）
    /// 管理占位精灵、默认边框、颜色调色板等
    /// </summary>
    [CreateAssetMenu(fileName = "ArtConfig", menuName = "万界长卷/美术配置")]
    public class ArtConfig : ScriptableObject
    {
        [Header("占位精灵")]
        [Tooltip("卡牌默认占位图（无美术时使用）")]
        public Sprite defaultCardArt;

        [Tooltip("敌人默认占位图")]
        public Sprite defaultEnemyArt;

        [Tooltip("英灵默认占位图")]
        public Sprite defaultCrawlerArt;

        [Header("卡牌边框（按颜色）")]
        [Tooltip("七色卡牌边框（索引对应 CardColor 枚举：0=Huo 1=Tu 2=Mu 3=Shui 4=Jin 5=Yang 6=Yin）")]
        public Sprite[] cardBorders = new Sprite[7];

        [Header("UI 元素")]
        public Sprite scrollBackground;   // 卷轴背景
        public Sprite bambooBackground;   // 竹简背景
        public Sprite sealButton;         // 印章按钮
        public Sprite logo;               // Logo 图
        public Sprite inkDrop;            // 墨迹散点

        [Header("字体")]
        public Font primaryFont;   // 楷体
        public Font titleFont;     // 隶书

        // ===== 运行时回退（自动从 Resources 加载） =====

        /// <summary>卷轴背景（带运行时回退）</summary>
        public Sprite ScrollBackground => scrollBackground != null ? scrollBackground : RuntimeArtLoader.GetScrollPanel();

        /// <summary>印章按钮（带运行时回退）</summary>
        public Sprite SealButton => sealButton != null ? sealButton : RuntimeArtLoader.GetStartButton();

        /// <summary>Logo 图（带运行时回退）</summary>
        public Sprite Logo => logo != null ? logo : RuntimeArtLoader.GetLogo();

        /// <summary>获取卡牌边框</summary>
        public Sprite GetCardBorder(CardColor color)
        {
            int idx = (int)color;
            if (cardBorders != null && idx < cardBorders.Length && cardBorders[idx] != null)
                return cardBorders[idx];
            return null;
        }

        /// <summary>获取卡面图（有则用，无则占位）</summary>
        public Sprite GetCardArt(CardConfig card)
        {
            if (card.cardArt != null) return card.cardArt;
            return defaultCardArt;
        }

        /// <summary>获取敌人图</summary>
        public Sprite GetEnemyArt(EnemyStats enemy)
        {
            if (enemy.enemyArt != null) return enemy.enemyArt;
            return defaultEnemyArt;
        }

        /// <summary>获取英灵立绘</summary>
        public Sprite GetCrawlerArt(CrawlerConfig crawler)
        {
            if (crawler.portraitArt != null) return crawler.portraitArt;
            if (defaultCrawlerArt != null) return defaultCrawlerArt;
            return RuntimeArtLoader.GetWukongPortrait();
        }
    }

    /// <summary>
    /// 卡牌颜色调色板（GDD 11.2 配色体系）
    /// </summary>
    public static class CardColorPalette
    {
        /// <summary>主色</summary>
        public static Color GetMainColor(CardColor color)
        {
            return color switch
            {
                CardColor.Huo  => HexToColor("#FF4500"), // 火·赤朱红
                CardColor.Tu   => HexToColor("#8B5A2B"), // 土·土褐
                CardColor.Mu   => HexToColor("#2E8B57"), // 木·苍翠绿
                CardColor.Shui => HexToColor("#191970"), // 水·深蓝黑
                CardColor.Jin  => HexToColor("#FFD700"), // 金·香槟金
                CardColor.Yang => HexToColor("#FFFAF0"), // 阳·日光白
                CardColor.Yin  => HexToColor("#1A001A"), // 阴·暗夜紫
                _ => Color.white
            };
        }

        /// <summary>辅色（边框）</summary>
        public static Color GetBorderColor(CardColor color)
        {
            return color switch
            {
                CardColor.Huo  => HexToColor("#8B0000"), // 暗红边
                CardColor.Tu   => HexToColor("#5C3A0E"), // 深褐边
                CardColor.Mu   => HexToColor("#1B5E20"), // 深绿边
                CardColor.Shui => HexToColor("#0A0A3A"), // 深渊蓝边
                CardColor.Jin  => HexToColor("#B8860B"), // 暗金边
                CardColor.Yang => HexToColor("#F5E6CA"), // 暖光边
                CardColor.Yin  => HexToColor("#0D0D0D"), // 虚无黑边
                _ => Color.gray
            };
        }

        /// <summary>中式名</summary>
        public static string GetChineseName(CardColor color)
        {
            return color switch
            {
                CardColor.Huo  => "火",
                CardColor.Tu   => "土",
                CardColor.Mu   => "木",
                CardColor.Shui => "水",
                CardColor.Jin  => "金",
                CardColor.Yang => "阳",
                CardColor.Yin  => "阴",
                _ => "?"
            };
        }

        private static Color HexToColor(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }
    }
}
