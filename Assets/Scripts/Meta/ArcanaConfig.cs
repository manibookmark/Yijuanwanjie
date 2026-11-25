using UnityEngine;

namespace WanjieScroll.Meta
{
    /// <summary>
    /// 秘法配置（ScriptableObject）
    /// 出发前装备 1 张，修改局内规则
    /// </summary>
    [CreateAssetMenu(fileName = "NewArcana", menuName = "万界长卷/秘法")]
    public class ArcanaConfig : ScriptableObject
    {
        [Tooltip("稳定内容 ID；不得使用显示名称建立运行时关联")]
        public string contentId;
        public string arcanaName;
        [TextArea(1, 3)] public string description;
        public ArcanaEffect effect;
        public int effectValue = 0;
        public string unlockCondition; // 解锁条件
    }

    /// <summary>
    /// 天机的稳定内容 ID。该类不承担可用池或 UI 选择职责，只避免
    /// Polentír 的自动解锁依赖显示名称或临时资源路径。
    /// </summary>
    public static class ArcanaContentIds
    {
        public const string ExperimentalMedicine =
            "arcana.xiyou.experimental-medicine.v1";
        public const string ShieldBash =
            "arcana.xiyou.shield-bash.v1";
        public const string BaishuZhoutian =
            "arcana.xiyou.baishu-zhoutian.v1";
    }

    public enum ArcanaEffect
    {
        ShieldBash,         // 护盾猛击：护甲转伤害
        OverTheTop,         // 居高临下：保留连击乘区
        YourShieldMyLiege,  // 以盾护主：护甲增益
        ChainLink,           // 连锁：连击跨回合保留（需12+连击解锁）
        SharpMind,           // 精明：法力跨回合保留（首发可能不做）
        WildStrength,        // 蛮力
        VoluntaryAmnesia,    // 主动遗忘
        JestersHat,          // 小丑帽：每层送1张太极牌
        SwollenFist,         // 肿拳
        DoubleEdge,          // 双刃剑
        CardMaster,          // 卡牌大师
        PersistentHunter,    // 执着猎手
        Custom,              // 自定义
        // 既有 ScriptableObject 以枚举序列化；新增项只能追加。
        ExperimentalMedicine, // 回春秘药：卡牌／英灵／灵印／特殊治疗 ×2
        BaishuZhoutian       // 百术周天：冻结于Run入口，每100次真实出牌增加1基础灵力
    }
}
