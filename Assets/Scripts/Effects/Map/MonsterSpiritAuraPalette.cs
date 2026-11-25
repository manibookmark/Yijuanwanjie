using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>
    /// 怪物妖气的唯一色板。地图持续灵气与 BOSS 登场烟潮共同引用，避免
    /// 两处分别手填“近似黑紫”后再次出现色差。
    /// </summary>
    public static class MonsterSpiritAuraPalette
    {
        public static readonly Color Deep =
            new Color(0.008f, 0.004f, 0.018f, 1f);
        public static readonly Color Core =
            new Color(0.18f, 0.045f, 0.30f, 1f);
        public static readonly Color Highlight =
            new Color(0.72f, 0.30f, 1.00f, 1f);
    }
}
