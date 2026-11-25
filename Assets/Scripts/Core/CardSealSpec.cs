using System;

namespace WanjieScroll.Core
{
    public enum CardSealEffect
    {
        Luck,
        Greed,
        DoubleDamage,
        TripleDamage,
        Draw,
        Armor,
        Amount,
        /// <summary>卡牌打出后的裂空（Area）增量。</summary>
        BengLie,
        /// <summary>消耗一个空印位，把宿主变成其已配置的进化目标；不留作战斗触发印。</summary>
        Evolution,
        /// <summary>宿主基础效果后增加本遭遇锋锐。</summary>
        Might,
        /// <summary>宿主基础效果后返还实际支付的临时灵力。</summary>
        Refund,
        /// <summary>实际正支付耗尽总可用灵力时，按支付快照回涌。</summary>
        ManaRebate,
        /// <summary>真正抽到后获得有限回手次数；回手这次不改变叠势链。</summary>
        Return,
        /// <summary>宿主后独立攻击最近可攻击行，不继承宿主攻击修饰。</summary>
        Nduja,
        /// <summary>增加金色英灵触发资格，不改宿主本色。</summary>
        YellowTrigger,
        /// <summary>宿主仍在手中时，其他真实出牌获得灵玉。</summary>
        Midas,
        /// <summary>宿主在手时提供加算的攻击伤害光环，不修改基础锋锐。</summary>
        Leader,
        /// <summary>宿主后以当前护甲作一次固定直击，成功提交时消耗该护甲。</summary>
        ArmorStrike,
        /// <summary>宿主成功使用后按印位生成无附印、强制销毁的基础模板临时副本。</summary>
        Copy = 18,
        /// <summary>正常回合弃牌保留宿主，不改变主动使用去向。</summary>
        Retain = 19,
        /// <summary>宿主正式进入叠势链后，下一数字可升一或降一。</summary>
        ReverseCombo = 20,
        /// <summary>宿主有效命中挂来源标记；目标被伤害击杀后领取灵玉。</summary>
        Mug = 21
    }

    /// <summary>镶在卡牌实例上的灵印触发规则。</summary>
    [Serializable]
    public sealed class CardSealSpec
    {
        public string contentId;
        public CardSealEffect effect;
        public int value;
        public bool comboScalable;

        public CardSealSpec Clone()
        {
            return new CardSealSpec
            {
                contentId = contentId,
                effect = effect,
                value = value,
                comboScalable = comboScalable
            };
        }

        public static CardSealSpec Luck(int value = 25)
            => new CardSealSpec
            {
                contentId = "builtin.seal.luck",
                effect = CardSealEffect.Luck,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec Greed(int value = 20)
            => new CardSealSpec
            {
                contentId = "builtin.seal.greed",
                effect = CardSealEffect.Greed,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec DoubleDamage()
            => new CardSealSpec
            {
                contentId = "seal.xiyou.double-damage.common.v1",
                effect = CardSealEffect.DoubleDamage,
                value = 2,
                comboScalable = false
            };

        public static CardSealSpec TripleDamage()
            => new CardSealSpec
            {
                contentId = "seal.xiyou.triple-damage.rare.v1",
                effect = CardSealEffect.TripleDamage,
                value = 3,
                comboScalable = false
            };

        public static CardSealSpec Draw(int value = 1)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.draw.common.v1",
                effect = CardSealEffect.Draw,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec Armor(int value = 2)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.armor.common.v1",
                effect = CardSealEffect.Armor,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec Amount(int value = 1)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.amount.common.v1",
                effect = CardSealEffect.Amount,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec BengLie(int value = 10)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.benglie.common.v1",
                effect = CardSealEffect.BengLie,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec Evolution()
            => new CardSealSpec
            {
                contentId = "seal.common.huaxing.immortal.v1",
                effect = CardSealEffect.Evolution,
                value = 1,
                comboScalable = false
            };

        public static CardSealSpec Might(int value = 10)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.might.common.v1",
                effect = CardSealEffect.Might,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec Refund()
            => new CardSealSpec
            {
                contentId = "seal.xiyou.refund.uncommon.v1",
                effect = CardSealEffect.Refund,
                value = 1,
                comboScalable = false
            };

        public static CardSealSpec ManaRebate()
            => new CardSealSpec
            {
                contentId = "seal.xiyou.mana-rebate.ultra-rare.v1",
                effect = CardSealEffect.ManaRebate,
                value = 2,
                comboScalable = false
            };

        public static CardSealSpec Return(int value = 1)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.return.rare.v1",
                effect = CardSealEffect.Return,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec Nduja(int value = 16)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.nduja.uncommon.v1",
                effect = CardSealEffect.Nduja,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec YellowTrigger()
            => new CardSealSpec
            {
                contentId = "seal.xiyou.yellow-trigger.uncommon.v1",
                effect = CardSealEffect.YellowTrigger,
                value = 1,
                comboScalable = false
            };

        public static CardSealSpec Midas(int value = 5)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.midas.very-rare.v1",
                effect = CardSealEffect.Midas,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec Leader(int value = 10)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.leader.very-rare.v1",
                effect = CardSealEffect.Leader,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec ArmorStrike()
            => new CardSealSpec
            {
                contentId = "seal.xiyou.armor-strike.very-rare.v1",
                effect = CardSealEffect.ArmorStrike,
                value = 1,
                comboScalable = false
            };

        public static CardSealSpec Copy(int value = 1)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.copy.very-rare.v1",
                effect = CardSealEffect.Copy,
                value = value,
                comboScalable = false
            };

        public static CardSealSpec Retain()
            => new CardSealSpec
            {
                contentId = "seal.xiyou.retain.uncommon.v1",
                effect = CardSealEffect.Retain,
                value = 1,
                comboScalable = false
            };

        public static CardSealSpec ReverseCombo()
            => new CardSealSpec
            {
                contentId = "seal.xiyou.reverse-combo.rare.v1",
                effect = CardSealEffect.ReverseCombo,
                value = 1,
                comboScalable = false
            };

        public static CardSealSpec Mug(int value = 3)
            => new CardSealSpec
            {
                contentId = "seal.xiyou.mug.common.v1",
                effect = CardSealEffect.Mug,
                value = value,
                comboScalable = false
            };
    }
}
