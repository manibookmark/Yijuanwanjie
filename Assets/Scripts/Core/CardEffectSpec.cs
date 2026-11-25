using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 类型化卡牌效果规格。数据驱动，可序列化。
    /// 使用有限操作集覆盖当前玩法，不引入脚本语言。
    /// </summary>
    [Serializable]
    public sealed class CardEffectSpec
    {
        /// <summary>效果类型</summary>
        public EffectType effectType;

        /// <summary>目标规则</summary>
        public TargetRule target = TargetRule.PlayedTarget;

        /// <summary>数值（伤害量、护甲量、治疗量、抽牌数等）</summary>
        public int amount;

        /// <summary>数量（投射物数、生成卡牌数等）</summary>
        public int count = 1;

        /// <summary>Damage 类型的结构化配置；为空时由 amount/count 和卡牌旧字段迁移生成。</summary>
        public DamageSpec damageSpec;

        /// <summary>成功执行此基础 Damage 后，按该次计划登记本遭遇回合末攻击源。</summary>
        public bool repeatAtPlayerTurnEnd;

        /// <summary>直击登记可重复列序列；仅本回合末按当前属性补打一次，与长期周期源互斥。</summary>
        public bool replayColumnsAtPlayerTurnEnd;

        /// <summary>状态效果 ID</summary>
        public StatusEffect statusId = StatusEffect.None;

        /// <summary>触发概率（0~1）</summary>
        public float chance = 1f;

        /// <summary>持续回合数</summary>
        public int duration;

        /// <summary>条件列表（全部满足时才执行）</summary>
        public List<ConditionRule> conditions;

        /// <summary>属性类型（ModifyStat 时使用）</summary>
        public StatType statType;

        /// <summary>费用修改值（ModifyCost 时使用）</summary>
        public int costModifier;

        /// <summary>
        /// RandomizeHandCosts 的闭区间随机费用范围；只作用于当前手牌
        /// 中的 Default 数字卡，本回合结束由 CardInstance 清除。
        /// randomCostRelative=true 时范围解释为相对原费用的偏移量。
        /// </summary>
        public int randomCostMin;
        public int randomCostMax;
        public bool randomCostRelative;

        /// <summary>创建卡牌的 definitionId（CreateCard 时使用）</summary>
        public string createCardDefinitionId;

        /// <summary>召唤英灵的稳定 contentId（SummonCrawler 时使用）</summary>
        public string crawlerContentId;

        /// <summary>
        /// RemoveEnemyIntent 的目标过滤。启用后只候选当前有攻击行动的敌人，
        /// 对齐 VC RemoveIntentEffect 的 EnemyActionDamage 过滤；不改变 count 和抗性判定。
        /// </summary>
        public bool attackIntentOnly;

        /// <summary>
        /// 对齐 VC RemoveIntentEffect 的 _removeAllIntents：跳过
        /// actionToRemove/EnemyActionDamage 类型过滤；count 仍是随机候选预算，
        /// 纯等待意图由 AI 层跳过，抗性仍按敌人逐个独立判定。
        /// </summary>
        public bool removeAllIntents;

        /// <summary>
        /// 内容作者的三态编译输入：Default / Enabled / Disabled。
        /// 空字符串只供旧 C# 构造器与历史资产兼容；权威 JSON
        /// 必须显式选择三态之一，再由 Content 编译为 comboScalable。
        /// </summary>
        public string comboScaling = "";

        /// <summary>
        /// 运行时的已编译二态值。Content 之外不应再解释
        /// comboScaling；所有结算只读取本字段。
        /// </summary>
        public bool comboScalable = false;

        // ========== 工厂方法 ==========

        public static CardEffectSpec Damage(int amount, TargetRule target = TargetRule.PlayedTarget)
            => new CardEffectSpec
            {
                effectType = EffectType.Damage,
                amount = amount,
                target = target,
                comboScalable = ComboScalingAuthoring.IsEnabledByDefault(
                    EffectType.Damage)
            };

        public static CardEffectSpec GainArmor(int amount)
            => new CardEffectSpec
            {
                effectType = EffectType.GainArmor,
                amount = amount,
                target = TargetRule.Self,
                comboScalable = ComboScalingAuthoring.IsEnabledByDefault(
                    EffectType.GainArmor)
            };

        public static CardEffectSpec Heal(int amount)
            => new CardEffectSpec
            {
                effectType = EffectType.Heal,
                amount = amount,
                target = TargetRule.Self,
                comboScalable = ComboScalingAuthoring.IsEnabledByDefault(
                    EffectType.Heal)
            };

        public static CardEffectSpec GainMana(int amount)
            => new CardEffectSpec
            {
                effectType = EffectType.GainMana,
                amount = amount,
                target = TargetRule.Self,
                comboScalable = ComboScalingAuthoring.IsEnabledByDefault(
                    EffectType.GainMana)
            };

        public static CardEffectSpec DrawCards(int count)
            => new CardEffectSpec
            {
                effectType = EffectType.DrawCards,
                count = count,
                target = TargetRule.Self,
                comboScalable = ComboScalingAuthoring.IsEnabledByDefault(
                    EffectType.DrawCards)
            };

        public static CardEffectSpec DiscardCards(int count)
            => new CardEffectSpec { effectType = EffectType.DiscardCards, count = count, target = TargetRule.Self };

        public static CardEffectSpec ApplyStatus(StatusEffect status, float chance = 1f, int duration = 0,
            TargetRule target = TargetRule.PlayedTarget)
            => new CardEffectSpec
            {
                effectType = EffectType.ApplyStatus,
                statusId = status,
                chance = chance,
                duration = duration,
                target = target
            };

        public static CardEffectSpec ModifyStat(StatType stat, int amount)
            => new CardEffectSpec
            {
                effectType = EffectType.ModifyStat,
                statType = stat,
                amount = amount,
                target = TargetRule.Self,
                comboScalable =
                    ComboScalingAuthoring.IsEnabledByDefault(
                        EffectType.ModifyStat,
                        stat)
            };

        public static CardEffectSpec ModifyCost(int modifier)
            => new CardEffectSpec { effectType = EffectType.ModifyCost, costModifier = modifier, target = TargetRule.Self };

        public static CardEffectSpec RandomizeHandCosts(
            int minInclusive = 0,
            int maxInclusive = 4)
            => new CardEffectSpec
            {
                effectType = EffectType.RandomizeHandCosts,
                target = TargetRule.Self,
                randomCostMin = minInclusive,
                randomCostMax = maxInclusive,
                comboScaling = ComboScalingAuthoring.Disabled,
                comboScalable = false
            };

        public static CardEffectSpec CreateCard(string definitionId, int count = 1)
            => new CardEffectSpec { effectType = EffectType.CreateCard, createCardDefinitionId = definitionId, count = count, target = TargetRule.Self };

        public static CardEffectSpec DestroySelf()
            => new CardEffectSpec { effectType = EffectType.DestroySelf, target = TargetRule.Self };

        public static CardEffectSpec BanishReward()
            => new CardEffectSpec { effectType = EffectType.BanishReward, target = TargetRule.Self };

        public static CardEffectSpec Bounce(int amount, int count)
            => new CardEffectSpec { effectType = EffectType.Bounce, amount = amount, count = count, target = TargetRule.PlayedTarget };

        public static CardEffectSpec Burn(int stacks, int duration = 0)
            => new CardEffectSpec { effectType = EffectType.Burn, amount = stacks, duration = duration, target = TargetRule.PlayedTarget };

        public static CardEffectSpec Erase()
            => new CardEffectSpec { effectType = EffectType.Erase, target = TargetRule.PlayedTarget };

        /// <summary>随机选取至多 count 个当前有意图的敌人，各按抗性尝试取消整条意图。</summary>
        public static CardEffectSpec RemoveEnemyIntent(int count = 1)
            => new CardEffectSpec
            {
                effectType = EffectType.RemoveEnemyIntent,
                target = TargetRule.AllEnemies,
                count = count,
                comboScaling = ComboScalingAuthoring.Disabled,
                comboScalable = false
            };

        /// <summary>随机选取至多 count 个攻击意图并按抗性尝试取消。</summary>
        public static CardEffectSpec RemoveEnemyAttackIntent(int count = 1)
            => new CardEffectSpec
            {
                effectType = EffectType.RemoveEnemyIntent,
                target = TargetRule.AllEnemies,
                count = count,
                attackIntentOnly = true,
                comboScaling = ComboScalingAuthoring.Disabled,
                comboScalable = false
            };

        public static CardEffectSpec GainJade(int amount)
            => new CardEffectSpec { effectType = EffectType.GainJade, amount = amount, target = TargetRule.Self };

        /// <summary>
        /// 永久提高本局玩家的最大生命；当前生命保持原值，默认受连击缩放。
        /// </summary>
        public static CardEffectSpec IncreaseMaxHealth(int amount)
            => new CardEffectSpec
            {
                effectType = EffectType.IncreaseMaxHealth,
                amount = amount,
                target = TargetRule.Self,
                comboScalable = ComboScalingAuthoring.IsEnabledByDefault(
                    EffectType.IncreaseMaxHealth)
            };

        /// <summary>
        /// 通过战斗唯一经验入口获得基础经验；受现有明悟倍率影响，
        /// 不受招煞或叠势倍率缩放。
        /// </summary>
        public static CardEffectSpec GainExperience(int amount)
            => new CardEffectSpec
            {
                effectType = EffectType.GainExperience,
                amount = amount,
                target = TargetRule.Self,
                comboScalable = false
            };

        /// <summary>增加指定在场英灵的剩余凝神；不复活已离场英灵。</summary>
        public static CardEffectSpec IncreaseCrawlerDuration(
            string crawlerContentId,
            int amount)
            => new CardEffectSpec
            {
                effectType = EffectType.IncreaseCrawlerDuration,
                target = TargetRule.Self,
                crawlerContentId = crawlerContentId,
                amount = amount,
                comboScalable = false
            };

        /// <summary>续时所有在场英灵；无人驻场时储存给下一次召唤。</summary>
        public static CardEffectSpec ExtendCrawlerDuration(int amount)
            => new CardEffectSpec
            {
                effectType = EffectType.ExtendCrawlerDuration,
                target = TargetRule.Self,
                amount = amount,
                comboScalable = true
            };

        /// <summary>是否满足所有条件</summary>
        public bool MeetsConditions(EffectResolveInput input)
        {
            return ConditionEvaluator.CheckAll(conditions, input);
        }
    }

    /// <summary>
    /// 逐效果叠势的作者三态合同。该模块唯一拥有
    /// “效果类型默认是否缩放”的知识；Content 只负责调用它编译。
    /// </summary>
    public static class ComboScalingAuthoring
    {
        public const string Default = "Default";
        public const string Enabled = "Enabled";
        public const string Disabled = "Disabled";

        public static bool TryResolve(
            CardEffectSpec effect,
            out bool comboScalable)
        {
            if (effect == null)
            {
                comboScalable = false;
                return false;
            }

            string mode = effect.comboScaling?.Trim();
            if (string.IsNullOrWhiteSpace(mode))
            {
                // 历史 C# / 资产兼容：旧对象没有作者三态，
                // 保留其已有二态值，不重现按类型强制覆盖。
                comboScalable = effect.comboScalable;
                return true;
            }

            if (string.Equals(mode, Default,
                    StringComparison.OrdinalIgnoreCase))
            {
                comboScalable = IsEnabledByDefault(
                    effect.effectType,
                    effect.statType);
                return true;
            }
            if (string.Equals(mode, Enabled,
                    StringComparison.OrdinalIgnoreCase))
            {
                comboScalable = true;
                return true;
            }
            if (string.Equals(mode, Disabled,
                    StringComparison.OrdinalIgnoreCase))
            {
                comboScalable = false;
                return true;
            }

            comboScalable = false;
            return false;
        }

        public static bool IsEnabledByDefault(
            EffectType effectType,
            StatType statType = default)
        {
            switch (effectType)
            {
                case EffectType.Damage:
                case EffectType.GainArmor:
                case EffectType.Heal:
                case EffectType.GainMana:
                case EffectType.DrawCards:
                case EffectType.IncreaseMaxHealth:
                case EffectType.ExtendCrawlerDuration:
                    return true;
                case EffectType.ModifyStat:
                    return statType != StatType.TianYun &&
                           statType != StatType.LianYu &&
                           statType != StatType.MingWu;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// 第一版有限操作集。不引入脚本语言和逐卡 C# 类。
    /// </summary>
    public enum EffectType
    {
        None,
        Damage,
        GainArmor,
        Heal,
        GainMana,
        DrawCards,
        DiscardCards,
        ApplyStatus,
        ModifyStat,
        ModifyCost,
        CreateCard,
        DestroySelf,
        BanishReward,
        Bounce,
        Burn,
        Erase,
        GainJade,
        SummonCrawler,
        IncreaseCrawlerDuration,
        // 既有 JSON 以数字序列化 EffectType；新类型只能追加，禁止插入重排。
        GainExperience,
        IncreaseMaxHealth
        ,
        /// <summary>将当前手牌数字卡费用随机到作者指定闭区间。</summary>
        RandomizeHandCosts,
        /// <summary>独立于攻击命中的整条敌人意图取消；只作用于当前回合。</summary>
        RemoveEnemyIntent,
        /// <summary>续所有在场英灵；无驻场英灵时仅为下一次召唤储存凝神。</summary>
        ExtendCrawlerDuration,
        /// <summary>本场独立随机每名驻场英灵的触发色，不改变技能或凝神。</summary>
        RandomizeCrawlerTriggerColors
    }

    /// <summary>
    /// 目标规则。决定效果作用于谁。
    /// </summary>
    public enum TargetRule
    {
        /// <summary>玩家打出时选择的目标</summary>
        PlayedTarget,
        /// <summary>玩家自身</summary>
        Self,
        /// <summary>所有敌人</summary>
        AllEnemies,
        /// <summary>随机敌人</summary>
        RandomEnemy,
        /// <summary>当前卡牌实例自身（用于 DestroySelf 等）</summary>
        ThisCard,
        /// <summary>执行开始时最近合法逻辑行；冻结身份，不追补位，Erase 仅对 Stage Boss 免疫。</summary>
        NearestAttackableRow
    }

    /// <summary>
    /// 条件规则。决定效果是否执行。
    /// </summary>
    [Serializable]
    public sealed class ConditionRule
    {
        public ConditionTypeId type;
        public int intValue;
        public float floatValue;
        public string stringValue;
    }

    /// <summary>条件类型枚举</summary>
    public enum ConditionTypeId
    {
        None,
        ComboAtLeast,
        HealthBelow,
        EnemyIsBoss,
        RandomChance
    }
}
