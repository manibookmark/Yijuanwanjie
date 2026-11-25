using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 全局统一的灵能与灵气粒子色板。八卦阵纵深粒子是权威视觉
    /// 基准；UI 与 Effects 只读取这组值，不再为英灵、地图或卷化反馈
    /// 各自声明另一套“灵气”颜色。
    /// </summary>
    public static class SpiritEnergyColorPalette
    {
        /// <summary>战斗 HUD 满 MP 与灵力珠核心共用的蓝色。</summary>
        public static readonly Color ManaFull =
            new Color(0.30f, 0.50f, 0.90f, 1f);

        /// <summary>临时 MP 与灵力珠核心高光共用的浅蓝色。</summary>
        public static readonly Color ManaHighlight =
            new Color(0.60f, 0.80f, 1f, 1f);

        /// <summary>灵力珠核心背光侧使用的深蓝色。</summary>
        public static readonly Color ManaDeep =
            new Color(0.10f, 0.20f, 0.45f, 1f);

        /// <summary>灵能纹理与无分层光效的基准色。</summary>
        public static readonly Color Energy =
            new Color(0.16f, 0.92f, 0.86f, 1f);

        /// <summary>灵气粒子外围、背面与包络层。</summary>
        public static readonly Color ParticleDeep =
            new Color(0.008f, 0.15f, 0.18f, 1f);

        /// <summary>灵气粒子的默认主体色。</summary>
        public static readonly Color ParticleCore =
            new Color(0.27f, 1f, 0.88f, 1f);

        /// <summary>灵气粒子的近白青高光。</summary>
        public static readonly Color ParticleHighlight =
            new Color(0.82f, 1f, 0.97f, 1f);
    }
}
