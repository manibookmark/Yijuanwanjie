using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 全局共享的卡牌定义，运行时只读。
    /// 迁移自 CardConfig；定义创建后不可修改。
    /// </summary>
    public class CardDefinition
    {
        // ========== 身份 ==========
        public string contentId;
        public string volumeId = "common";

        // ========== 展示（迁移期保留，Phase 4 替换为 textId） ==========
        public string cardName;
        public string description;

        // ========== 颜色与费用 ==========
        public CardColor color = CardColor.Huo;
        public CostType costType = CostType.Default;
        public int manaCost = 0;
        public bool isFreeToPlay = false;
        public bool hasReverse = false;

        // ========== 战斗属性 ==========
        public int baseDamage = 0;
        public AttackPattern attackPattern = AttackPattern.None;
        public int projectileCount = 1;
        public TargetPolicy targetPolicy = TargetPolicy.LeftToRight;
        public int directTargetsPerHit = 1;
        public bool canCrit = false;
        public float critChance = 0.25f;
        public float critMultiplier = 1.5f;
        public AreaTriggerMode areaTriggerMode = AreaTriggerMode.PerDirectTarget;
        public float areaCoefficient = 0.2f;
        public int armor = 0;
        public int heal = 0;
        public int manaGain = 0;

        // ========== 状态效果 ==========
        public StatusEffect statusEffect = StatusEffect.None;
        public float statusChance = 0f;

        // ========== 特殊标记 ==========
        public bool destroyOnUse = false;
        public int gemSlots = 1;
        public List<CardSealSpec> seals;

        // ========== 类型化效果（Phase 3） ==========
        /// <summary>
        /// 类型化效果列表。非空时由战斗编排层按 Spec 解析；
        /// 为空时回退到旧版字段驱动路径（baseDamage/armor/heal/manaGain）。
        /// </summary>
        public List<CardEffectSpec> effects;

        // ========== 进化（Phase 4 迁移为 evolutionRecipeIds） ==========
        public string evolutionTargetId;

        /// <summary>获取实际费用（Wild=0，X=属性值，其余=manaCost）</summary>
        public int GetCost(int statValue = 0)
        {
            return costType switch
            {
                CostType.Wild => 0,
                CostType.X => UnityEngine.Mathf.FloorToInt(statValue),
                _ => manaCost
            };
        }

        /// <summary>解析三值费用模型。详见 docs/rules/连击系统规范.md §1。</summary>
        public ResolvedCardCost ResolveCost(int statValue = 0)
        {
            int baseValue = manaCost;
            bool isWild = costType == CostType.Wild;
            bool isMild = costType == CostType.Mild;
            bool isX = costType == CostType.X;
            bool isBreaker = costType == CostType.ComboBreaker;

            int comboValue;
            int paymentCost;

            if (isWild)
            {
                comboValue = 0;
                paymentCost = 0;
            }
            else if (isX)
            {
                comboValue = UnityEngine.Mathf.FloorToInt(statValue);
                paymentCost = comboValue;
            }
            else
            {
                comboValue = baseValue;
                paymentCost = baseValue;
            }

            if (isFreeToPlay)
                paymentCost = 0;

            return new ResolvedCardCost(
                baseManaValue: baseValue,
                comboValue: comboValue,
                paymentCost: paymentCost,
                isWild: isWild,
                isFreeToPlay: isFreeToPlay,
                isXCost: isX,
                isComboBreaker: isBreaker,
                isMild: isMild,
                hasReverse: hasReverse
            );
        }

        /// <summary>是否参与连击</summary>
        public bool DoesCardCombo()
        {
            return costType switch
            {
                CostType.Mild => false,
                CostType.Wild => true,
                _ => true
            };
        }
    }
}
