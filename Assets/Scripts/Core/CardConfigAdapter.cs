using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// CardConfig → CardDefinition / CardInstance 兼容适配器。
    /// 迁移期间将遗留 CardConfig 转换为新的定义/实例模型。
    /// 迁移完成后本类删除。
    /// </summary>
    public static class CardConfigAdapter
    {
        /// <summary>从 CardConfig 创建只读 CardDefinition</summary>
        public static CardDefinition ToDefinition(CardConfig config)
        {
            return new CardDefinition
            {
                contentId = config.contentId,
                volumeId = config.volumeId,
                cardName = config.cardName,
                description = config.description,
                color = config.color,
                costType = config.costType,
                manaCost = config.manaCost,
                baseDamage = config.baseDamage,
                attackPattern = config.attackPattern,
                projectileCount = config.projectileCount,
                targetPolicy = config.targetPolicy,
                directTargetsPerHit = config.directTargetsPerHit,
                canCrit = config.canCrit,
                critChance = config.critChance,
                critMultiplier = config.critMultiplier,
                areaTriggerMode = config.areaTriggerMode,
                areaCoefficient = config.areaCoefficient,
                armor = config.armor,
                heal = config.heal,
                manaGain = config.manaGain,
                statusEffect = config.statusEffect,
                statusChance = config.statusChance,
                destroyOnUse = config.destroyOnUse,
                gemSlots = config.gemSlots,
                seals = CloneSeals(config.seals),
                evolutionTargetId = config.evolutionTarget?.contentId,
                // 兼容适配必须无损保留已经结构化的效果；只有旧卡确实没有
                // effects 时才从散落字段生成兼容规格。
                effects = config.HasSpecEffects
                    ? new List<CardEffectSpec>(config.effects)
                    : GenerateSpecs(config)
            };
        }

        /// <summary>
        /// 从 CardConfig 的旧版字段自动生成类型化效果列表。
        /// 保持与旧版字段结算相同的行为和顺序。
        /// </summary>
        public static List<CardEffectSpec> GenerateSpecs(CardConfig config)
        {
            var specs = new List<CardEffectSpec>();

            if (config.baseDamage > 0)
                specs.Add(CardEffectSpec.Damage(config.baseDamage));

            if (config.statusEffect != StatusEffect.None && config.statusChance > 0f)
                specs.Add(CardEffectSpec.ApplyStatus(
                    config.statusEffect, config.statusChance));

            if (config.armor > 0)
                specs.Add(CardEffectSpec.GainArmor(config.armor));

            if (config.heal > 0)
                specs.Add(CardEffectSpec.Heal(config.heal));

            if (config.manaGain > 0)
                specs.Add(CardEffectSpec.GainMana(config.manaGain));

            if (config.destroyOnUse)
                specs.Add(CardEffectSpec.DestroySelf());

            return specs.Count > 0 ? specs : null;
        }

        private static List<CardSealSpec> CloneSeals(List<CardSealSpec> source)
        {
            if (source == null) return null;
            var result = new List<CardSealSpec>(source.Count);
            foreach (CardSealSpec seal in source)
                if (seal != null) result.Add(seal.Clone());
            return result;
        }

        /// <summary>从 CardConfig 创建单张 CardInstance</summary>
        public static CardInstance ToInstance(CardConfig config, int instanceId)
        {
            var instance = new CardInstance(instanceId, ToDefinition(config));
            instance.RestoreLimitBreakCount(config.LimitBreakCount);
            return instance;
        }

        /// <summary>批量将 CardConfig 列表转换为 CardInstance 列表</summary>
        public static List<CardInstance> ToInstanceList(
            IEnumerable<CardConfig> configs,
            IInstanceIdGenerator idGenerator)
        {
            var instances = new List<CardInstance>();
            foreach (var config in configs)
                instances.Add(ToInstance(config, idGenerator.NextId()));
            return instances;
        }

        /// <summary>批量将 CardConfig 列表转换为 CardInstance 列表（使用默认 ID 生成器）</summary>
        public static List<CardInstance> ToInstanceList(IEnumerable<CardConfig> configs)
        {
            return ToInstanceList(configs, DefaultInstanceIdGenerator.Instance);
        }
    }
}
