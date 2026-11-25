using System;
using System.Collections.Generic;
using WanjieScroll.Core;

namespace WanjieScroll.Content
{
    /// <summary>
    /// Owns the author-to-runtime damage shape boundary. Runtime serialization
    /// and card instances may change their mutable specs without touching the
    /// catalog or another factory result. Copy values without normalizing null
    /// objects, null lists or null entries; those shapes also belong to the
    /// frozen content fingerprint. New spec fields require an explicit mapping.
    /// </summary>
    internal static class ContentDamageSpecCopy
    {
        internal static DamageSpec Copy(DamageSpec source) => source == null ? null : new DamageSpec
        {
            baseHitDamage = source.baseHitDamage,
            limitBreakAttackInstancesPerLevel = source.limitBreakAttackInstancesPerLevel,
            pattern = Copy(source.pattern),
            crit = Copy(source.crit),
            area = Copy(source.area),
            bounce = Copy(source.bounce),
            specials = CopyList(source.specials, Copy),
            onHitEffects = CopyList(source.onHitEffects, Copy),
            terrainEffect = Copy(source.terrainEffect),
            ricochetProjectile = Copy(source.ricochetProjectile),
            birdAttack = source.birdAttack?.Clone(),
            cardLocalDamageMultiplier = source.cardLocalDamageMultiplier,
            manaDamageCoefficient = source.manaDamageCoefficient,
            comboScalable = source.comboScalable,
            amountScalable = source.amountScalable
        };

        private static AttackPatternSpec Copy(AttackPatternSpec source) => source == null ? null : new AttackPatternSpec
        {
            patternType = source.patternType,
            baseAttackInstanceCount = source.baseAttackInstanceCount,
            targetPolicy = source.targetPolicy,
            directTargetsPerSubHit = source.directTargetsPerSubHit,
            comboMode = source.comboMode,
            amountMode = source.amountMode,
            subHits = CopyList(source.subHits, Copy)
        };

        private static SubHitSpec Copy(SubHitSpec source) => source == null ? null : new SubHitSpec
        {
            baseDamageOverride = source.baseDamageOverride,
            damageScale = source.damageScale,
            overrideTargetPolicy = source.overrideTargetPolicy,
            targetPolicyOverride = source.targetPolicyOverride,
            directTargetCountOverride = source.directTargetCountOverride,
            critOverride = Copy(source.critOverride),
            areaOverride = Copy(source.areaOverride)
        };

        private static CritSpec Copy(CritSpec source) => source == null ? null : new CritSpec
        {
            enabled = source.enabled,
            chance = source.chance,
            multiplier = source.multiplier,
            areaDamageBasis = source.areaDamageBasis
        };

        private static AreaSpec Copy(AreaSpec source) => source == null ? null : new AreaSpec
        {
            enabled = source.enabled,
            coefficient = source.coefficient,
            triggerMode = source.triggerMode
        };

        private static BounceSpec Copy(BounceSpec source) => source == null ? null : new BounceSpec
        {
            enabled = source.enabled,
            sequenceMode = source.sequenceMode,
            hitsPerDirectHit = source.hitsPerDirectHit,
            damageIncrementPerBounce = source.damageIncrementPerBounce
        };

        private static SpecialAttackSpec Copy(SpecialAttackSpec source) => source == null ? null : new SpecialAttackSpec
        {
            kind = source.kind,
            maxTriggersPerCard = source.maxTriggersPerCard,
            maxGeneration = source.maxGeneration,
            canCrit = source.canCrit,
            canTriggerArea = source.canTriggerArea,
            canTriggerOtherSpecials = source.canTriggerOtherSpecials,
            canTriggerSelf = source.canTriggerSelf,
            bounceTargetMode = source.bounceTargetMode
        };

        private static OnHitEffectSpec Copy(OnHitEffectSpec source) => source == null ? null : new OnHitEffectSpec
        {
            effectType = source.effectType,
            baseChance = source.baseChance,
            useRuleDefaultChance = source.useRuleDefaultChance
        };

        private static TerrainEffectSpec Copy(TerrainEffectSpec source) => source == null ? null : new TerrainEffectSpec
        {
            effectId = source.effectId,
            effectKind = source.effectKind,
            triggerTiming = source.triggerTiming,
            damageKind = source.damageKind,
            magnitudeScale = source.magnitudeScale,
            triggerCount = source.triggerCount,
            targetMode = source.targetMode,
            baseMagnitude = source.baseMagnitude,
            areaCoefficient = source.areaCoefficient,
            persistUntilCombatEnd = source.persistUntilCombatEnd
        };

        private static RicochetProjectileSpec Copy(RicochetProjectileSpec source) => source == null ? null : new RicochetProjectileSpec
        {
            enabled = source.enabled,
            minimumLifetime = source.minimumLifetime,
            maximumLifetime = source.maximumLifetime,
            amountSpeedCoefficient = source.amountSpeedCoefficient
        };

        private static List<T> CopyList<T>(List<T> source, Func<T, T> copy)
        {
            if (source == null) return null;
            var result = new List<T>(source.Count);
            foreach (T item in source) result.Add(copy(item));
            return result;
        }
    }
}
