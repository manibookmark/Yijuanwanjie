using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Meta
{
    /// <summary>
    /// Power-Up 配置（ScriptableObject）
    /// 局外永久属性升级，3 Tier 解锁，免费退款
    /// </summary>
    [CreateAssetMenu(fileName = "NewPowerUp", menuName = "万界长卷/Power-Up")]
    public class PowerUpConfig : ScriptableObject
    {
        [Tooltip("稳定内容 ID；新存档关联不得使用显示名称")]
        public string contentId;
        public string powerUpName;
        [TextArea(1, 2)] public string description;
        public StatType statType;
        public int amountPerLevel;   // 每级增量
        public int maxLevel;         // 最大等级
        public int totalPrice;       // 升满总价（灵玉）
        public UnlockTier tier;      // 解锁 Tier
        public bool usePriceTier;
        public EconomyPriceTier priceTier = EconomyPriceTier.Tier2;

        public string StableId => string.IsNullOrWhiteSpace(contentId) ? null : contentId.Trim();

        public int GetLevelPrice(int level, EconomyConfig economy = null)
        {
            if (statType == StatType.LianYu && maxLevel == 4)
            {
                int[] greedPrices = { 500, 750, 1000, 1500 };
                return greedPrices[Mathf.Clamp(level - 1, 0, greedPrices.Length - 1)];
            }
            if (usePriceTier)
                return (economy ?? new EconomyConfig()).GetPrice(priceTier);
            // 线性推算：总价 / 最大等级
            return maxLevel > 0 ? totalPrice / maxLevel : 0;
        }

        public string GetEffectText(int level)
        {
            return $"+{amountPerLevel * level} {statType}";
        }
    }

    public enum UnlockTier { Tier1, Tier2, Tier3, Special }
}
