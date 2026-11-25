using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Meta
{
    /// <summary>
    /// 宝石配置（ScriptableObject）
    /// 5 档稀有度，掉落权重
    /// </summary>
    [CreateAssetMenu(fileName = "NewGem", menuName = "万界长卷/宝石")]
    public class GemConfig : ScriptableObject
    {
        [Tooltip("稳定内容 ID；不得使用显示名称建立运行时关联")]
        public string contentId;
        public string gemName;
        [TextArea(1, 2)] public string description;
        public GemRarity rarity;
        public GemEffect effect;
        public int effectValue = 0;      // 效果数值
        public float effectMultiplier = 0f; // 百分比效果

        // 掉落权重（覆盖默认）
        public bool useCustomWeight = false;
        public int customWeight = 0;

        public CardSealSpec ToCardSealSpec()
        {
            return effect switch
            {
                GemEffect.Luck => new CardSealSpec
                {
                    contentId = contentId,
                    effect = CardSealEffect.Luck,
                    value = effectValue > 0 ? effectValue : 25,
                    comboScalable = false
                },
                GemEffect.Greed => new CardSealSpec
                {
                    contentId = contentId,
                    effect = CardSealEffect.Greed,
                    value = effectValue > 0 ? effectValue : 20,
                    comboScalable = false
                },
                GemEffect.DoubleDamage => CardSealSpec.DoubleDamage(),
                GemEffect.TripleDamage => CardSealSpec.TripleDamage(),
                _ => null
            };
        }
    }

    public enum GemRarity
    {
        Common,     // 凡品
        Uncommon,   // 良品
        Rare,       // 上品
        VeryRare,   // 珍品
        UltraRare   // 仙品
    }

    public enum GemEffect
    {
        None,
        DoubleDamage,      // 双倍伤害
        TripleDamage,      // 三倍伤害
        Amount,            // 数量
        Area,               // 溅射
        Armor,              // 护甲
        Might,              // 武威
        Luck,               // 机缘
        Draw,               // 抽牌
        Duration,           // 驻留
        Growth,             // 悟性
        Greed,              // 贪狼
        ReverseCombo,       // 逆转连击
        ComboBreaker,       // 连击终结
        Evolution,          // 进化
        FreeToPlay,         // 免费出牌
        Wild,               // 通卡化
        ManaRebate,         // 法力返还
        Uncrackable,        // 不可碎裂
        Refund,             // 法力退还
        Retain,             // 保留
        Return,             // 回手
        QuickDraw,          // 先抽
        Recycle,            // 循环
        RestoreHealth,      // 回血
        Fireproof,          // 免疫销毁
        Drain,              // 吸血
        Magnetic,           // 磁吸
        Copy,                // 复制
        Echo,                // 回响
        Countdown,           // 倒计时
        Leader,              // 领袖
        KillCount,           // 击杀计数
        CoinCount,           // 金币计数
        CoinCard,            // 金币卡
        Decimate,            // 斩杀
        Bombard,             // 炮击
        CrawlerCaller,       // 英灵召唤
        Remote,              // 矿车
        MagicHat,            // 魔帽
        Midas,               // 点金
        Mug,                 // 掠夺
        Nduja,               // 辣味
        YinYang,             // 阴阳
        Freeze,              // 冰封
        Mild,                // 温和
        XMana,               // X法力
        ArmorStrike,         // 护甲打击
        RedTrigger,          // 红色触发
        BlueTrigger,         // 蓝色触发
        YellowTrigger,       // 黄色触发
        PurpleTrigger,       // 紫色触发
        Rainbow              // 彩虹
    }

    /// <summary>
    /// 宝石稀有度权重表
    /// </summary>
    public static class GemRarityWeights
    {
        // 升级奖励（受机缘影响，仙品不出现在升级中）
        public static readonly int[] UpgradeWeights = { 60, 30, 20, 6, 0 };
        public static readonly float[] LuckFactors = { 0f, 0.035f, 0.065f, 0.3f, 0f };

        // 宝箱（机缘不影响分布）
        public static readonly int[] ChestWeights = { 160, 120, 80, 60, 40 };

        public static int GetWeight(GemRarity rarity, bool fromChest)
        {
            int idx = (int)rarity;
            return fromChest ? ChestWeights[idx] : UpgradeWeights[idx];
        }
    }
}
