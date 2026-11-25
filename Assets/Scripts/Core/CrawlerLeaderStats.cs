using System;

namespace WanjieScroll.Core
{
    /// <summary>
    /// MainFcc modifiers are new-Run entry data, distinct from playing its card.
    /// Projection changes the captured baseline once; Duration is returned to
    /// the Run admission owner for its shared, next-summon-only reserve.
    /// </summary>
    [Serializable]
    public sealed class CrawlerLeaderStats
    {
        public int healthModifier;
        public int armorModifier;
        public int manaModifier;
        public int dealCountModifier;
        public int mightModifier;
        public int amountModifier;
        public int areaModifier;
        public int growthModifier;
        public int greedModifier;
        public int luckModifier;
        public int recoveryModifier;
        public int magnetModifier;
        public int revivalModifier;
        public int curseModifier;
        public int durationModifier;

        public CrawlerLeaderStats Clone() => (CrawlerLeaderStats)MemberwiseClone();

        public int ProjectNewRunBaseline(PlayerStats player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            checked
            {
                player.maxHealth += healthModifier;
                player.baseArmorPerTurn += armorModifier;
                player.faLi += manaModifier;
                player.shouPai += dealCountModifier;
                player.chouPai = player.shouPai;
                player.fengRui += mightModifier;
                player.amount += amountModifier;
                player.bengLie += areaModifier;
                player.mingWu += growthModifier;
                player.lianYu += greedModifier;
                player.tianYun += luckModifier;
                player.recovery += recoveryModifier;
                player.yinJuan += magnetModifier;
                player.hunHun += revivalModifier;
                player.zhaoSha += curseModifier;
            }
            return durationModifier;
        }
    }
}
