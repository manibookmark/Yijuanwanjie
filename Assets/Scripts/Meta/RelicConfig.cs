using UnityEngine;

namespace WanjieScroll.Meta
{
    /// <summary>遗珍稳定内容 ID。显示名称变更不得影响运行时关联。</summary>
    public static class RelicContentIds
    {
        public const string Jiudie = "relic.tutorial.jiudie.v1";
        public const string WuxingDisc =
            "relic.tutorial.wuxing-disc.v1";
        public const string Yuyin = "relic.xiyou.yuyin.v1";
        public const string Polentir = "relic.xiyou.polentir.v1";
        /// <summary>G2 F5 的终局观星台；与 F1 Polentír 不同一物品。</summary>
        public const string GuanxingTai = "relic.xiyou.guanxing-tai.v1";

        /// <summary>早期开发存档中的“连击开关”ID，只用于读取迁移。</summary>
        public const string LegacyComboSwitch =
            "legacy.relic.common.acf8e191";
    }

    /// <summary>
    /// 遗珍配置（内部沿用 Relic 类型名）
    /// 拾取永久生效，解锁新机制或新设施
    /// </summary>
    [CreateAssetMenu(fileName = "NewRelic", menuName = "万界长卷/遗珍")]
    public class RelicConfig : ScriptableObject
    {
        [Tooltip("稳定内容 ID；不得使用显示名称建立运行时关联")]
        public string contentId;
        public string relicName;
        [TextArea(1, 3)] public string description;
        public RelicType type;
        public string unlockCondition;    // 获取位置/条件描述
        public bool enabled = true;       // 可在宝物馆开关
    }

    public enum RelicType
    {
        ComboStack,           // 九叠：启用叠势（内部仍沿用 Combo）
        GemHammer,            // 玉印（兼容原宝石锤序列值；解锁即时钤印）
        Polentir,             // Polentír（解锁 Fortune Teller's Arcane Tent）
        GuidingLight,         // 引路灯（小地图）
        GrimGrimoire,        // 符录（进化图鉴）
        ArcanaFinder,         // 秘法罗盘（解锁Arcana系统）
        StardustAnvil,        // 星铁砧（解锁鉴宝斋）
        Overkill,             // 余威（解锁余威灵玉）
        BombaInfernale,       // 爆裂符（加速刷怪）
        UltimateUltraOverkill,// 至极余威
        SorceressTears,        // 仙泪（疾行Mode，后续）
        LapidaryLoupe,        // 宝镜（宝石稀有度定制）
        Ovenkilt,             // 烘炉围裙（余威扩展）
        SeventhTrumpet,       // 末世号角（无尽Mode，后续）
        LedaSpecial,          // Leda 彩蛋
        Custom = 15,          // 自定义；显式保留既有序列化数值
        WuxingDisc = 16       // 五行盘：启用五行相生
    }
}
