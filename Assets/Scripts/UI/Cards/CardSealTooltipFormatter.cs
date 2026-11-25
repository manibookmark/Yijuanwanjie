using System;
using WanjieScroll.Core;
using WanjieScroll.Meta;

namespace WanjieScroll.UI.Cards
{
    /// <summary>
    /// Formats one installed seal as a complete card-tooltip line. The catalog owns
    /// formal names and long descriptions; the installed spec owns the value that
    /// combat will actually apply. Keeping that boundary here prevents UI callers
    /// from duplicating effect switches or hiding stale runtime data behind a
    /// hard-coded display multiplier.
    /// </summary>
    public static class CardSealTooltipFormatter
    {
        /// <summary>
        /// Returns “印名：效果”, optionally coloring the name with a rich-text hex
        /// color. Null means that the installed seal has no positive runtime effect.
        /// </summary>
        public static string Format(CardSealSpec seal, string labelHex = null)
        {
            if (seal == null || seal.value <= 0)
                return null;

            bool hasMatchingAuthor =
                LingyinCatalog.TryGet(seal.contentId, out LingyinConfig authored) &&
                authored != null &&
                authored.effectType == seal.effect;
            bool matchesAuthorContract = hasMatchingAuthor &&
                authored.effectValue == seal.value;

            string name = hasMatchingAuthor &&
                          !string.IsNullOrWhiteSpace(authored.displayName)
                ? authored.displayName.Trim()
                : GetFallbackName(seal.effect);
            string effect = FormatEffect(
                seal,
                matchesAuthorContract ? authored.description : null);
            if (string.IsNullOrWhiteSpace(effect))
                return null;

            string normalizedHex = string.IsNullOrWhiteSpace(labelHex)
                ? null
                : labelHex.Trim().TrimStart('#');
            string formattedName = string.IsNullOrWhiteSpace(normalizedHex)
                ? name
                : $"<color=#{normalizedHex}>{name}</color>";
            return $"{formattedName}：{effect}";
        }

        private static string FormatEffect(
            CardSealSpec seal,
            string matchingAuthorDescription)
        {
            int value = seal.value;
            switch (seal.effect)
            {
                case CardSealEffect.Luck:
                    return $"天运+{value}%";
                case CardSealEffect.Greed:
                    return $"敛玉+{value}%";
                case CardSealEffect.DoubleDamage:
                    return $"伤害×{Math.Max(2, value)}";
                case CardSealEffect.TripleDamage:
                    return $"伤害×{Math.Max(3, value)}";
                case CardSealEffect.Draw:
                    return $"抽{Math.Max(1, value)}张牌";
                case CardSealEffect.Armor:
                    return $"护甲+{value}";
                case CardSealEffect.Amount:
                    return $"数量+{value}";
                case CardSealEffect.BengLie:
                    return $"裂空+{value}%";
            }

            if (!string.IsNullOrWhiteSpace(matchingAuthorDescription))
                return matchingAuthorDescription.Trim();

            // These fallbacks remain truthful when a persisted spec no longer
            // matches its author asset. They deliberately use the installed value.
            return seal.effect switch
            {
                CardSealEffect.Evolution => "将卡牌进化为更强形态，占用1个印位，保留原有灵印",
                CardSealEffect.Might => $"锋锐+{value}%",
                CardSealEffect.Refund => "返还此次实际支付的灵力",
                CardSealEffect.ManaRebate => $"耗尽灵力时按实际支付量×{value}回涌",
                CardSealEffect.Return => $"每次抽到后可回手{value}次",
                CardSealEffect.Nduja => $"宿主结算后对最近一排造成{value}点伤害",
                CardSealEffect.YellowTrigger => "获得金色英灵触发资格",
                CardSealEffect.Midas => $"宿主在手时，其他牌每次出牌获得基础{value}灵玉",
                CardSealEffect.Leader => $"宿主在手时，全部攻击牌伤害+{value}%",
                CardSealEffect.ArmorStrike => "按当前护甲造成等值伤害并清空当前护甲",
                CardSealEffect.Copy => $"宿主结算后生成{value}张无印临时副本",
                CardSealEffect.Retain => "回合结束时留在手牌",
                CardSealEffect.ReverseCombo => "下一张数字牌可升一或降一续势",
                CardSealEffect.Mug => $"标记敌人死亡时基础+{value}灵玉",
                _ => $"效果值+{value}"
            };
        }

        private static string GetFallbackName(CardSealEffect effect)
            => effect switch
            {
                CardSealEffect.Luck => "天运印",
                CardSealEffect.Greed => "敛玉印",
                CardSealEffect.DoubleDamage => "双伤印",
                CardSealEffect.TripleDamage => "三伤印",
                CardSealEffect.Draw => "引卷印",
                CardSealEffect.Armor => "罡气印",
                CardSealEffect.Amount => "数印",
                CardSealEffect.BengLie => "裂空印",
                CardSealEffect.Evolution => "化形印",
                CardSealEffect.Might => "锋锐印",
                CardSealEffect.Refund => "返灵印",
                CardSealEffect.ManaRebate => "灵涌印",
                CardSealEffect.Return => "回手印",
                CardSealEffect.Nduja => "炎息印",
                CardSealEffect.YellowTrigger => "金纹印",
                CardSealEffect.Midas => "聚财印",
                CardSealEffect.Leader => "将印",
                CardSealEffect.ArmorStrike => "倾甲印",
                CardSealEffect.Copy => "化影印",
                CardSealEffect.Retain => "留手印",
                CardSealEffect.ReverseCombo => "逆行印",
                CardSealEffect.Mug => "掠夺印",
                _ => "灵印"
            };
    }
}
