using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WanjieScroll.Core
{
    [Serializable]
    public sealed class CritSpec
    {
        public bool enabled;
        public float chance = DamageSystem.DefaultCritChance;
        public float multiplier = DamageSystem.DefaultCritMultiplier;
        public AreaDamageBasis areaDamageBasis = AreaDamageBasis.NormalHitDamage;
    }

    [Serializable]
    public sealed class AreaSpec
    {
        public bool enabled = true;
        public float coefficient = 0.2f;
        public AreaTriggerMode triggerMode = AreaTriggerMode.PerDirectTarget;
    }

    public enum TerrainEffectKind
    {
        Damage
    }

    public enum TerrainEffectTiming
    {
        EnemyTurnStarted
    }

    public enum TerrainTargetMode
    {
        CellSnapshot,
        ColumnDynamicFront
    }

    /// <summary>
    /// Direct Hit 在命中格留下的地形效果。这里只描述规则和快照方式，
    /// 格子实例的叠层、触发与消退由 TerrainEffectSystem 统一管理。
    /// effectId 是内容与表现共用的稳定 ID，不得使用显示名称关联。
    /// </summary>
    [Serializable]
    public sealed class TerrainEffectSpec
    {
        public string effectId;
        public TerrainEffectKind effectKind = TerrainEffectKind.Damage;
        public TerrainEffectTiming triggerTiming = TerrainEffectTiming.EnemyTurnStarted;
        public DamageKind damageKind = DamageKind.Burn;
        public float magnitudeScale = 1f;
        public int triggerCount = 1;
        public TerrainTargetMode targetMode;
        public int baseMagnitude;
        public float areaCoefficient = 0.2f;
        public bool persistUntilCombatEnd;
    }

    public enum OnHitEffectType
    {
        Knockback,
        Disarm
    }

    /// <summary>弹跳时序与数量。伤害沿用本 SubHit 的普通命中值，不吃独立暴击。</summary>
    [Serializable]
    public sealed class BounceSpec
    {
        public bool enabled;
        public BounceSequenceMode sequenceMode = BounceSequenceMode.PerDirectHit;
        /// <summary>
        /// PerDirectHit 模式为每次直击后的追加命中数；同步波次模式在构建计划后
        /// 表示 Direct 轮之后的 Bounce 轮数；原生逐物模式为 P-1，
        /// 其中 P=基础攻击物数+数量，不含叠势和突破。
        /// </summary>
        public int hitsPerDirectHit = 1;
        public int damageIncrementPerBounce;
    }

    /// <summary>每个 Direct Hit 独立判定的攻击附带效果。</summary>
    [Serializable]
    public sealed class OnHitEffectSpec
    {
        public OnHitEffectType effectType;
        public float baseChance;
        /// <summary>
        /// 概率是否来自 CombatRuleSet。内容只声明效果类型时为 true；
        /// AttackPlanBuilder 会在每次取计划时注入当前战斗的规则值。
        /// </summary>
        public bool useRuleDefaultChance;
    }

    /// <summary>
    /// AttackPlan 内部使用的只读规则快照。作者 Spec 仍保持 Unity 可序列化的
    /// 可变字段；计划一旦构建完成，就不再持有那些共享配置对象的引用。
    /// </summary>
    public sealed class ResolvedCritSpec
    {
        public bool enabled { get; }
        public float chance { get; }
        public float multiplier { get; }
        public AreaDamageBasis areaDamageBasis { get; }

        internal ResolvedCritSpec(CritSpec source)
        {
            enabled = source?.enabled == true;
            chance = source?.chance ?? 0f;
            multiplier = source?.multiplier ?? 1f;
            areaDamageBasis = source?.areaDamageBasis ??
                AreaDamageBasis.NormalHitDamage;
        }
    }

    public sealed class ResolvedAreaSpec
    {
        public bool enabled { get; }
        public float coefficient { get; }
        public AreaTriggerMode triggerMode { get; }

        internal ResolvedAreaSpec(AreaSpec source)
        {
            enabled = source?.enabled == true;
            coefficient = source?.coefficient ?? 0f;
            triggerMode = source?.triggerMode ?? AreaTriggerMode.Disabled;
        }
    }

    public sealed class ResolvedBounceSpec
    {
        public bool enabled { get; }
        public BounceSequenceMode sequenceMode { get; }
        public int hitsPerDirectHit { get; }
        public int damageIncrementPerBounce { get; }

        internal ResolvedBounceSpec(BounceSpec source)
        {
            enabled = source?.enabled == true;
            sequenceMode = source?.sequenceMode ??
                BounceSequenceMode.PerDirectHit;
            hitsPerDirectHit = Math.Max(0,
                source?.hitsPerDirectHit ?? 0);
            damageIncrementPerBounce = source?.damageIncrementPerBounce ?? 0;
        }
    }

    public sealed class ResolvedTerrainEffectSpec
    {
        public string effectId { get; }
        public TerrainEffectKind effectKind { get; }
        public TerrainEffectTiming triggerTiming { get; }
        public DamageKind damageKind { get; }
        public float magnitudeScale { get; }
        public int triggerCount { get; }
        public TerrainTargetMode targetMode { get; }
        public int baseMagnitude { get; }
        public float areaCoefficient { get; }
        public bool persistUntilCombatEnd { get; }

        internal ResolvedTerrainEffectSpec(TerrainEffectSpec source)
        {
            effectId = source?.effectId;
            effectKind = source?.effectKind ?? TerrainEffectKind.Damage;
            triggerTiming = source?.triggerTiming ??
                TerrainEffectTiming.EnemyTurnStarted;
            damageKind = source?.damageKind ?? DamageKind.Burn;
            magnitudeScale = source?.magnitudeScale ?? 0f;
            triggerCount = Math.Max(0, source?.triggerCount ?? 0);
            targetMode = source?.targetMode ?? TerrainTargetMode.CellSnapshot;
            baseMagnitude = Math.Max(0, source?.baseMagnitude ?? 0);
            areaCoefficient = Math.Max(0f, source?.areaCoefficient ?? 0.2f);
            persistUntilCombatEnd = source?.persistUntilCombatEnd == true;
        }
    }

    public readonly struct ResolvedOnHitEffectSpec
    {
        public OnHitEffectType effectType { get; }
        public float baseChance { get; }
        public bool useRuleDefaultChance { get; }

        internal ResolvedOnHitEffectSpec(OnHitEffectSpec source)
        {
            effectType = source?.effectType ?? OnHitEffectType.Knockback;
            baseChance = source?.baseChance ?? 0f;
            useRuleDefaultChance = source?.useRuleDefaultChance == true;
        }
    }

    [Serializable]
    public sealed class AttackPatternSpec
    {
        public AttackPattern patternType = AttackPattern.SingleShot;
        public int baseAttackInstanceCount = 1;
        public TargetPolicy targetPolicy = TargetPolicy.LeftToRight;
        public int directTargetsPerSubHit = 1;
        public ComboPatternMode comboMode = ComboPatternMode.ScaleHitDamage;
        public AmountPatternMode amountMode =
            AmountPatternMode.ExtraAttackInstance;
        public List<SubHitSpec> subHits = new List<SubHitSpec>();
    }

    [Serializable]
    public sealed class SubHitSpec
    {
        public int baseDamageOverride;
        public float damageScale = 1f;
        public bool overrideTargetPolicy;
        public TargetPolicy targetPolicyOverride;
        public int directTargetCountOverride = -1;
        public CritSpec critOverride;
        public AreaSpec areaOverride;
    }

    [Serializable]
    public sealed class SpecialAttackSpec
    {
        public DamageKind kind;
        public int maxTriggersPerCard = 1;
        public int maxGeneration = 1;
        public bool canCrit;
        public bool canTriggerArea;
        public bool canTriggerOtherSpecials;
        public bool canTriggerSelf;
        public BounceTargetMode bounceTargetMode = BounceTargetMode.UniqueThenStop;
    }

    [Serializable]
    public sealed class DamageSpec
    {
        public int baseHitDamage;
        /// <summary>
        /// 每层卡牌突破追加的攻击物数。实例的当前突破层数不属于作者配置，
        /// 由 CardAttackSnapshot 在构建计划时注入。
        /// </summary>
        public int limitBreakAttackInstancesPerLevel;
        public AttackPatternSpec pattern = new AttackPatternSpec();
        public CritSpec crit = new CritSpec();
        public AreaSpec area = new AreaSpec();
        public BounceSpec bounce;
        public List<SpecialAttackSpec> specials = new List<SpecialAttackSpec>();
        public List<OnHitEffectSpec> onHitEffects = new List<OnHitEffectSpec>();
        public TerrainEffectSpec terrainEffect;
        [UnityEngine.SerializeReference]
        public RicochetProjectileSpec ricochetProjectile;
        /// <summary>鸟攻击形状； managed reference保留Unity克隆后的null禁用语义。</summary>
        [UnityEngine.SerializeReference]
        public BirdAttackSpec birdAttack;
        public float cardLocalDamageMultiplier = 1f;
        /// <summary>Multiplier using the frozen pre-payment available mana and authored base cost; zero disables it.</summary>
        public float manaDamageCoefficient;
        public bool comboScalable = true;
        public bool amountScalable = true;
    }

    public readonly struct CombatStatSnapshot
    {
        public readonly int Might;
        public readonly int Area;
        public readonly int Amount;
        public readonly int ComboValue;
        public readonly int Luck;
        public readonly float GlobalDamageMultiplier;
        public readonly long ManaBeforePayment;

        public CombatStatSnapshot(PlayerStats player, int comboValue, float globalDamageMultiplier = 1f,
            long manaBeforePayment = 0, int runLuckBonus = 0)
        {
            Might = player?.fengRui ?? 0;
            Area = player?.bengLie ?? 0;
            Amount = player?.amount ?? 0;
            ComboValue = Math.Max(0, comboValue);
            // PlayerStats retains the baseline; the Run owner supplies only its
            // independent delta. Keep signed baseline crit modifiers and freeze once.
            Luck = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue,
                (long)(player?.tianYun ?? 0) + runLuckBonus));
            GlobalDamageMultiplier = Math.Max(0f, globalDamageMultiplier);
            ManaBeforePayment = Math.Max(0L, manaBeforePayment);
        }
    }

    /// <summary>
    /// 一次出牌开始时冻结的实例级攻击状态。它只复制 CardInstance 的值，
    /// 不成为第二个可变状态源；低层算法测试可以显式构造该快照。
    /// </summary>
    public readonly struct CardAttackSnapshot
    {
        public readonly int SourceCardInstanceId;
        public readonly int LimitBreakCount;
        public readonly int BaseManaCost;

        public CardAttackSnapshot(
            int sourceCardInstanceId,
            int limitBreakCount,
            int baseManaCost = 0)
        {
            if (sourceCardInstanceId < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(sourceCardInstanceId));
            if (limitBreakCount < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(limitBreakCount));
            SourceCardInstanceId = sourceCardInstanceId;
            LimitBreakCount = limitBreakCount;
            BaseManaCost = Math.Max(0, baseManaCost);
        }

        public static CardAttackSnapshot Capture(CardConfig card)
            => card == null
                ? default
                : new CardAttackSnapshot(
                    card.runtimeInstanceId,
                    card.LimitBreakCount,
                    card.manaCost);
    }

    public sealed class AttackSubHit
    {
        public int AttackInstanceIndex { get; }
        public int SubHitIndex { get; }
        public int NormalHitDamage { get; }
        /// <summary>Damage after effect stages but before the target's banker's rounding.</summary>
        public double UnroundedHitDamage { get; }
        public TargetPolicy TargetPolicy { get; }
        public int DirectTargetCount { get; }
        /// <summary>非负时只攻击此列当前前排；不冻结敌人身份。普通自动攻击为 -1。</summary>
        public int TargetColumn { get; }
        public ResolvedCritSpec Crit { get; }
        public ResolvedAreaSpec Area { get; }
        public ResolvedBounceSpec Bounce { get; }
        public ResolvedTerrainEffectSpec TerrainEffect { get; }
        public IReadOnlyList<ResolvedOnHitEffectSpec> OnHitEffects { get; }

        internal AttackSubHit(
            int attackInstanceIndex,
            int subHitIndex,
            int normalHitDamage,
            TargetPolicy targetPolicy,
            int directTargetCount,
            CritSpec crit,
            AreaSpec area,
            BounceSpec bounce,
            TerrainEffectSpec terrainEffect,
            IReadOnlyList<OnHitEffectSpec> onHitEffects,
            int targetColumn = -1,
            double unroundedHitDamage = -1d)
        {
            if (targetColumn < -1 || targetColumn >= EnemyFormationLayout.Columns)
                throw new ArgumentOutOfRangeException(nameof(targetColumn));
            AttackInstanceIndex = attackInstanceIndex;
            SubHitIndex = subHitIndex;
            NormalHitDamage = Math.Max(0, normalHitDamage);
            UnroundedHitDamage = unroundedHitDamage >= 0f ? unroundedHitDamage : NormalHitDamage;
            TargetPolicy = targetPolicy;
            DirectTargetCount = Math.Max(0, directTargetCount);
            TargetColumn = targetColumn;
            Crit = crit == null ? null : new ResolvedCritSpec(crit);
            Area = area == null ? null : new ResolvedAreaSpec(area);
            Bounce = bounce == null ? null : new ResolvedBounceSpec(bounce);
            TerrainEffect = terrainEffect == null
                ? null
                : new ResolvedTerrainEffectSpec(terrainEffect);
            OnHitEffects = FreezeOnHitEffects(onHitEffects);
        }

        private static IReadOnlyList<ResolvedOnHitEffectSpec> FreezeOnHitEffects(
            IReadOnlyList<OnHitEffectSpec> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<ResolvedOnHitEffectSpec>();
            var snapshot = new List<ResolvedOnHitEffectSpec>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                    snapshot.Add(new ResolvedOnHitEffectSpec(source[i]));
            }
            return snapshot.AsReadOnly();
        }
    }

    public sealed class AttackPlan
    {
        /// <summary>
        /// 单次计划允许产生的最大伤害事件数。这是执行时间与
        /// 结果内存的技术预算，不是卡牌平衡上限。
        /// </summary>
        public const int MaximumResolutionEventsPerPlan = 16_384;

        private readonly List<AttackSubHit> _subHits =
            new List<AttackSubHit>();
        private readonly ReadOnlyCollection<AttackSubHit> _subHitView;

        public int SourceCardInstanceId { get; }
        public int AreaStat { get; }
        /// <summary>本次召唤的固定驻场鸟数；普通攻击为零，与导弹或命中数量无关。</summary>
        public int ResidentBirdCount { get; }
        public ResolvedRicochetProjectileSpec RicochetProjectile { get; }
        /// <summary>Secondary attacks can retain attribution without re-triggering host on-hit seals.</summary>
        public bool CanTriggerSecondaryEffects { get; }
        /// <summary>
        /// 卡面名义总伤：每个 SubHit 的普通伤害只计一次。
        /// 同一 SubHit 复制到多个直接目标的额外伤害不计入卡面数字。
        /// </summary>
        public int DisplayedCardDamage { get; private set; }
        public IReadOnlyList<AttackSubHit> SubHits => _subHitView;

        internal AttackPlan(
            int sourceCardInstanceId,
            int areaStat,
            int displayedCardDamage = 0,
            bool canTriggerSecondaryEffects = true,
            int residentBirdCount = 0,
            ResolvedRicochetProjectileSpec ricochetProjectile = null)
        {
            SourceCardInstanceId = sourceCardInstanceId;
            AreaStat = areaStat;
            DisplayedCardDamage = displayedCardDamage;
            CanTriggerSecondaryEffects = canTriggerSecondaryEffects;
            ResidentBirdCount = Math.Max(0, residentBirdCount);
            RicochetProjectile = ricochetProjectile;
            _subHitView = _subHits.AsReadOnly();
        }

        internal void AddSubHit(AttackSubHit subHit)
        {
            if (subHit == null)
                throw new ArgumentNullException(nameof(subHit));
            _subHits.Add(subHit);
        }

        internal void AddDisplayedDamage(int damage)
        {
            if (damage <= 0) return;
            DisplayedCardDamage = checked(DisplayedCardDamage + damage);
        }

        /// <summary>目标充足时，本计划最多产生的 Direct 伤害事件数。</summary>
        public int MaximumDirectEventCount
        {
            get
            {
                long total = 0;
                for (int i = 0; i < SubHits.Count; i++)
                    total += Math.Max(0, SubHits[i].DirectTargetCount);
                return (int)Math.Min(int.MaxValue, total);
            }
        }

        /// <summary>目标充足时，本计划最多产生的 Bounce 伤害事件数。</summary>
        public int MaximumBounceEventCount
        {
            get
            {
                long total = 0;
                for (int i = 0; i < SubHits.Count; i++)
                {
                    AttackSubHit hit = SubHits[i];
                    if (hit.Bounce == null || !hit.Bounce.enabled)
                        continue;
                    long hitMaximum =
                        (long)Math.Max(0, hit.DirectTargetCount) *
                        Math.Max(0, hit.Bounce.hitsPerDirectHit);
                    total = hitMaximum >= int.MaxValue - total
                        ? int.MaxValue
                        : total + hitMaximum;
                }
                return (int)Math.Min(int.MaxValue, total);
            }
        }

        /// <summary>
        /// 按当前敌人集合规模估算最坏伤害事件数。直击、弹跳和
        /// 范围伤害在这里共用一个预算；计算在 long 中完成并于
        /// “已超预算”时饱和，不容许整数回绕绕过拒绝边界。
        /// </summary>
        internal long EstimateMaximumResolutionEventCount(
            int enemyRosterCount)
        {
            if (enemyRosterCount < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(enemyRosterCount));

            if (RicochetProjectile != null)
            {
                long ricochetTotal = 0;
                // Ricochet splashes use GetAttackableTargets: at most one
                // distinct front enemy per column, including wide enemies.
                // Retained dead or hidden roster entries cannot enlarge a
                // collision's splash batch beyond the formation width.
                long splashTargetMaximum = Math.Min(
                    enemyRosterCount, EnemyFormationLayout.Columns);
                foreach (AttackSubHit hit in SubHits)
                {
                    long eventsPerCollision = AreaStat > 0 && hit.Area?.enabled == true &&
                        hit.Area.triggerMode != AreaTriggerMode.Disabled && hit.Area.coefficient > 0f
                            ? 1L + splashTargetMaximum : 1L;
                    ricochetTotal += RicochetProjectile.MaximumCollisionsPerProjectile * eventsPerCollision;
                    if (ricochetTotal > MaximumResolutionEventsPerPlan)
                        return MaximumResolutionEventsPerPlan + 1L;
                }
                return ricochetTotal;
            }

            long total = 0;
            long rosterCount = enemyRosterCount;
            for (int i = 0; i < SubHits.Count; i++)
            {
                AttackSubHit hit = SubHits[i];
                long targetBound = hit.TargetPolicy == TargetPolicy.FrozenAttackableSet
                    ? Math.Min(rosterCount, EnemyFormationLayout.Columns) : rosterCount;
                long directCount = rosterCount == 0
                    ? 0
                    : hit.TargetPolicy == TargetPolicy.AllAttackable ||
                      hit.DirectTargetCount <= 0
                        ? targetBound
                        // MultiHit can fill its entire budget against one
                        // enemy. Bound repeated events before any HP mutation.
                        : hit.TargetPolicy == TargetPolicy.RandomColumnBagMultiHit
                            ? (long)hit.DirectTargetCount
                            : Math.Min(targetBound, (long)hit.DirectTargetCount);
                total = AddBudgetedProduct(total, directCount, 1);

                long bouncePerDirect = 0;
                bool nativeBounce = hit.Bounce?.enabled == true &&
                    hit.Bounce.sequenceMode == BounceSequenceMode.NativePerAttackInstance;
                if (hit.Bounce != null && hit.Bounce.enabled)
                {
                    bouncePerDirect = nativeBounce || hit.Bounce.sequenceMode ==
                        BounceSequenceMode.SynchronizedWavesByAttackInstanceCount
                            ? hit.Bounce.hitsPerDirectHit
                            : Math.Min(
                                (long)hit.Bounce.hitsPerDirectHit,
                                Math.Max(0L, rosterCount - 1L));
                    total = AddBudgetedProduct(
                        total,
                        directCount,
                        bouncePerDirect);
                }

                if (hit.Area != null && hit.Area.enabled &&
                    hit.Area.triggerMode != AreaTriggerMode.Disabled &&
                    AreaStat > 0 && directCount > 0)
                {
                    long areaRepeats = hit.Area.triggerMode ==
                        AreaTriggerMode.PerDirectTarget
                            ? directCount
                            : 1L;
                    // Native bounce can revisit an earlier enemy. Every real
                    // hit, including a bounce, owns its own front-row Area batch.
                    if (nativeBounce) areaRepeats = directCount * (1L + bouncePerDirect);
                    total = AddBudgetedProduct(
                        total,
                        areaRepeats,
                        nativeBounce ? Math.Min(rosterCount, EnemyFormationLayout.Columns) : targetBound);
                }
            }
            return total;
        }

        internal void RequireResolutionEventBudget(int enemyRosterCount)
        {
            long maximum = EstimateMaximumResolutionEventCount(
                enemyRosterCount);
            if (maximum > MaximumResolutionEventsPerPlan)
            {
                throw new InvalidOperationException(
                    $"攻击计划在当前敌人集合上可能超过 " +
                    $"{MaximumResolutionEventsPerPlan} 个伤害事件的运行时安全上限。");
            }
        }

        private static long AddBudgetedProduct(
            long current,
            long left,
            long right)
        {
            const long saturated = MaximumResolutionEventsPerPlan + 1L;
            if (current >= saturated || left <= 0 || right <= 0)
                return Math.Min(current, saturated);
            long remaining = saturated - current;
            if (left > remaining / right)
                return saturated;
            return current + left * right;
        }
    }

    /// <summary>
    /// 唯一拥有作者伤害形状到只读计划的数学转换，预览与实际结算使用相同入口。
    /// 规模审查：超过800行仍是单一计划构建知识，无来源集合、计时或表现状态；
    /// 鸟形状只改变固定攻击物内的子击数，周期来源和存档仍归PeriodicAttackSystem。
    /// 下一项独立攻击算法或生命周期接入前重新审查，不按行数建立透传Helper。
    /// </summary>
    public static class AttackPlanBuilder
    {
        /// <summary>
        /// 单次出牌允许冻结的最大 SubHit 数。它是运行时内存安全边界，
        /// 不是策划突破上限；超过时明确拒绝损坏配置/存档，绝不部分结算。
        /// </summary>
        public const int MaximumSubHitsPerPlan = 4096;

        /// <summary>
        /// 从卡牌的权威配置取得伤害规格。结构化 DamageSpec 优先；旧字段只作为
        /// 尚未迁移内容的兼容输入。调用方不应自行重复判断 effects/baseDamage。
        /// </summary>
        public static bool TryFromCard(CardConfig card, CombatRuleSet rules,
            out DamageSpec damageSpec)
        {
            damageSpec = null;
            if (card == null) return false;
            rules ??= new CombatRuleSet();

            // 克隆的 CardConfig（Instantiate/Run 恢复）经 Unity 序列化后 effects
            // 是空 List 而非 null；仅判 != null 会把纯 legacy 伤害卡误判为无 Damage。
            if (card.effects != null && card.effects.Count > 0)
            {
                for (int i = 0; i < card.effects.Count; i++)
                {
                    CardEffectSpec effect = card.effects[i];
                    if (TryFromDamageEffect(
                            card,
                            effect,
                            rules,
                            out damageSpec))
                        return true;
                }
                return false;
            }

            if (card.baseDamage <= 0) return false;
            damageSpec = FromLegacy(card, card.baseDamage,
                Math.Max(1, card.projectileCount), rules);
            damageSpec = CardSealRules.ApplyDamageMultiplier(
                damageSpec, card.seals);
            return true;
        }

        /// <summary>
        /// 解析一个结构化 Damage 效果。Unity 克隆有时会把原本为 null 的
        /// damageSpec 还原为全零空壳；空壳不是作者规格，必须回退到
        /// effect.amount/count。这一判定只在此处存在。
        /// </summary>
        internal static bool TryFromDamageEffect(
            CardConfig card,
            CardEffectSpec effect,
            CombatRuleSet rules,
            out DamageSpec damageSpec)
        {
            damageSpec = null;
            if (card == null || effect == null ||
                effect.effectType != EffectType.Damage)
                return false;

            rules ??= new CombatRuleSet();
            damageSpec = effect.damageSpec != null &&
                effect.damageSpec.baseHitDamage > 0
                    ? effect.damageSpec
                    : effect.amount > 0
                        ? FromLegacy(
                            card,
                            effect.amount,
                            Math.Max(1, effect.count),
                            rules)
                        : null;
            if (damageSpec == null || damageSpec.baseHitDamage <= 0)
                return false;
            damageSpec = ResolveRuleDefaultOnHitChances(
                damageSpec,
                rules);
            damageSpec = CardSealRules.ApplyDamageMultiplier(
                damageSpec,
                card.seals);
            return true;
        }

        /// <summary>
        /// 正式调用方的单一入口：解析作者规格、应用卡牌局部修正，并冻结当前
        /// CardInstance 的身份和突破进度。调用方不需要分别传递实例字段。
        /// </summary>
        public static bool TryBuildFromCard(
            CardConfig card,
            CombatRuleSet rules,
            CombatStatSnapshot stats,
            out AttackPlan plan)
        {
            plan = null;
            if (!TryFromCard(card, rules, out DamageSpec damageSpec))
                return false;
            plan = Build(
                damageSpec,
                stats,
                CardAttackSnapshot.Capture(card));
            return true;
        }

        public static DamageSpec FromLegacy(CardConfig card, int baseDamage, int baseAttackInstanceCount,
            CombatRuleSet rules = null)
        {
            rules ??= new CombatRuleSet();
            var spec = new DamageSpec
            {
                baseHitDamage = baseDamage,
                pattern = new AttackPatternSpec
                {
                    patternType = card.attackPattern,
                    baseAttackInstanceCount = Math.Max(1, baseAttackInstanceCount),
                    targetPolicy = card.targetPolicy,
                    directTargetsPerSubHit = card.directTargetsPerHit
                },
                crit = new CritSpec
                {
                    enabled = card.canCrit || card.statusEffect == StatusEffect.BaoJi,
                    chance = card.statusEffect == StatusEffect.BaoJi && card.statusChance > 0f
                        ? card.statusChance : card.critChance,
                    multiplier = card.critMultiplier,
                    areaDamageBasis = AreaDamageBasis.NormalHitDamage
                },
                area = new AreaSpec
                {
                    enabled = card.areaTriggerMode != AreaTriggerMode.Disabled,
                    coefficient = card.areaCoefficient,
                    triggerMode = card.areaTriggerMode
                }
            };
            if (card.statusEffect == StatusEffect.ZhenTui)
                spec.onHitEffects.Add(new OnHitEffectSpec
                {
                    effectType = OnHitEffectType.Knockback,
                    // 旧 statusChance 是整卡判定时代的数据，不再沿用；
                    // 特殊卡需在 DamageSpec.onHitEffects 中显式关闭规则默认值并覆盖。
                    baseChance = rules.knockbackBaseChance,
                    useRuleDefaultChance = true
                });
            else if (card.statusEffect == StatusEffect.JiaoXie)
                spec.onHitEffects.Add(new OnHitEffectSpec
                {
                    effectType = OnHitEffectType.Disarm,
                    baseChance = rules.disarmBaseChance,
                    useRuleDefaultChance = true
                });
            return spec;
        }

        internal static DamageSpec ResolveRuleDefaultOnHitChances(
            DamageSpec source,
            CombatRuleSet rules)
        {
            rules ??= new CombatRuleSet();
            if (source?.onHitEffects == null ||
                !source.onHitEffects.Exists(effect =>
                    effect != null && effect.useRuleDefaultChance))
                return source;

            var resolvedEffects = new List<OnHitEffectSpec>(
                source.onHitEffects.Count);
            foreach (OnHitEffectSpec effect in source.onHitEffects)
            {
                if (effect == null || !effect.useRuleDefaultChance)
                {
                    resolvedEffects.Add(effect);
                    continue;
                }

                resolvedEffects.Add(new OnHitEffectSpec
                {
                    effectType = effect.effectType,
                    baseChance = effect.effectType switch
                    {
                        OnHitEffectType.Knockback => rules.knockbackBaseChance,
                        OnHitEffectType.Disarm => rules.disarmBaseChance,
                        _ => effect.baseChance
                    },
                    useRuleDefaultChance = true
                });
            }

            // DamageSpec 来自卡牌配置并会被预览与实际结算复用；不要把某场战斗的
            // 规则值写回共享配置，只复制本次需要解析的浅层规格与 OnHit 列表。
            return new DamageSpec
            {
                baseHitDamage = source.baseHitDamage,
                limitBreakAttackInstancesPerLevel =
                    source.limitBreakAttackInstancesPerLevel,
                pattern = source.pattern,
                crit = source.crit,
                area = source.area,
                bounce = source.bounce,
                specials = source.specials,
                onHitEffects = resolvedEffects,
                terrainEffect = source.terrainEffect,
                ricochetProjectile = source.ricochetProjectile,
                birdAttack = source.birdAttack?.Clone(),
                cardLocalDamageMultiplier = source.cardLocalDamageMultiplier,
                manaDamageCoefficient = source.manaDamageCoefficient,
                comboScalable = source.comboScalable,
                amountScalable = source.amountScalable
            };
        }

        internal static AttackPlan Build(
            DamageSpec spec,
            CombatStatSnapshot snapshot)
            => Build(spec, snapshot, default(CardAttackSnapshot));

        /// <summary>低层兼容入口；新生产代码应传 CardAttackSnapshot。</summary>
        [Obsolete(
            "此入口无法携带实例突破状态；请改用 CardAttackSnapshot 或 TryBuildFromCard。")]
        internal static AttackPlan Build(
            DamageSpec spec,
            CombatStatSnapshot snapshot,
            int sourceCardInstanceId)
            => Build(
                spec,
                snapshot,
                new CardAttackSnapshot(sourceCardInstanceId, 0));

        public static AttackPlan Build(
            DamageSpec spec,
            CombatStatSnapshot snapshot,
            CardAttackSnapshot source,
            bool canTriggerSecondaryEffects = true)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            AttackPatternSpec pattern = spec.pattern ?? new AttackPatternSpec();
            bool isBirdAttack = spec.birdAttack != null;
            if (spec.ricochetProjectile?.enabled == true)
            {
                spec.ricochetProjectile.RequireValid();
                if (isBirdAttack || spec.bounce?.enabled == true ||
                    !string.IsNullOrWhiteSpace(spec.terrainEffect?.effectId) || spec.crit?.enabled == true ||
                    pattern.directTargetsPerSubHit != 1 ||
                    (pattern.subHits != null && pattern.subHits.Count > 0) ||
                    (spec.onHitEffects != null && spec.onHitEffects.Count > 0))
                    throw new ArgumentException("Ricochet projectiles cannot mix another trajectory or on-hit rule.");
            }
            if (isBirdAttack)
            {
                spec.birdAttack.RequireValid();
                if (pattern.baseAttackInstanceCount <= 0 || pattern.directTargetsPerSubHit != 1 ||
                    (pattern.subHits != null && pattern.subHits.Count > 0))
                    throw new ArgumentException("Bird attacks require fixed birds, one target per missile and no custom sub-hits.");
                if ((long)pattern.baseAttackInstanceCount * spec.birdAttack.endTurnMissilesPerBird >
                    MaximumSubHitsPerPlan)
                    throw new InvalidOperationException("Bird end-turn attack exceeds the sub-hit safety budget.");
            }
            long comboMultiplier = spec.comboScalable &&
                (isBirdAttack || pattern.comboMode != ComboPatternMode.Disabled)
                    ? 1L + snapshot.ComboValue
                    : 1L;
            int amount = spec.amountScalable && (isBirdAttack || pattern.amountMode != AmountPatternMode.Disabled)
                ? snapshot.Amount : 0;
            int baseCount = Math.Max(0, pattern.baseAttackInstanceCount);
            // 重复攻击物是逐卡 Pattern 语义；默认卡牌保持基础攻击物数，
            // 由连击放大每个 Hit，避免飞刀等既有内容被全局迁移。
            long repeatedBase = pattern.comboMode ==
                    ComboPatternMode.RepeatBasePattern
                ? (long)baseCount * comboMultiplier
                : baseCount;
            long limitBreakAttackInstances =
                (long)Math.Max(
                    0,
                    spec.limitBreakAttackInstancesPerLevel) *
                source.LimitBreakCount;
            // 鸟数不吃Combo/Amount/LB；已核实的L0合同只缩放每鸟初击导弹。
            // 升级与LB临时鸟另待产品确认，不借通用攻击物公式增加驻留鸟。
            // Garlic 等原牌把 Amount 合入伤害倍率，而不是生成额外攻击物。
            // 该逐卡模式不改变既有模式的数量、突破或鸟类合同。
            bool amountScalesHitDamage = !isBirdAttack &&
                pattern.amountMode == AmountPatternMode.ScaleHitDamage;
            long resolvedCount = isBirdAttack ? baseCount : repeatedBase +
                (amountScalesHitDamage ? 0 : Math.Max(0, amount)) +
                limitBreakAttackInstances;
            long resolvedSubHitCount = isBirdAttack
                ? (long)spec.birdAttack.missilesPerBird * comboMultiplier + Math.Max(0, amount)
                : pattern.subHits != null && pattern.subHits.Count > 0
                    ? pattern.subHits.Count : 1;
            if (resolvedCount > MaximumSubHitsPerPlan ||
                (resolvedCount > 0 &&
                 resolvedSubHitCount > MaximumSubHitsPerPlan / resolvedCount))
                throw new InvalidOperationException(
                    $"攻击计划超过 {MaximumSubHitsPerPlan} 个 SubHit 的运行时安全上限，" +
                    "请检查 Amount、Combo、卡牌配置或突破存档。");
            int count = (int)resolvedCount;
            int subHitCount = (int)resolvedSubHitCount;
            float hitComboMultiplier = isBirdAttack || pattern.comboMode == ComboPatternMode.RepeatBasePattern ||
                pattern.comboMode == ComboPatternMode.Disabled
                    ? 1f
                    : (float)comboMultiplier;
            // 原生 DamageEffect.GetDamagePerHit 先相加 ComboMultiplier 与 Amount
            // 再乘基础伤害；不应把两个属性倍率相乘。
            if (amountScalesHitDamage)
                hitComboMultiplier += Math.Max(0, amount);
            BounceSpec resolvedBounce = ResolveBounce(spec.bounce, count, baseCount, amount);
            if (resolvedBounce != null && resolvedBounce.enabled &&
                (resolvedBounce.sequenceMode == BounceSequenceMode.SynchronizedWavesByAttackInstanceCount ||
                 resolvedBounce.sequenceMode == BounceSequenceMode.NativePerAttackInstance))
            {
                long totalSubHits = resolvedCount * subHitCount;
                long waveCount = 1L + resolvedBounce.hitsPerDirectHit;
                if (totalSubHits >
                    AttackPlan.MaximumResolutionEventsPerPlan / waveCount)
                {
                    throw new InvalidOperationException(
                        $"弹跳计划超过 " +
                        $"{AttackPlan.MaximumResolutionEventsPerPlan} 个伤害事件的" +
                        "运行时安全上限，请检查 Amount、Combo、卡牌配置或突破存档。");
                }
            }
            var plan = new AttackPlan(
                source.SourceCardInstanceId,
                snapshot.Area,
                canTriggerSecondaryEffects: canTriggerSecondaryEffects,
                residentBirdCount: isBirdAttack ? count : 0,
                ricochetProjectile: spec.ricochetProjectile?.enabled == true
                    ? new ResolvedRicochetProjectileSpec(spec.ricochetProjectile, count, baseCount) : null);
            for (int i = 0; i < count; i++)
            {
                for (int subIndex = 0; subIndex < subHitCount; subIndex++)
                {
                    SubHitSpec configured = isBirdAttack || (subHitCount == 1 && (pattern.subHits == null || pattern.subHits.Count == 0))
                        ? null : pattern.subHits[subIndex];
                    int baseDamage = configured != null && configured.baseDamageOverride > 0
                        ? configured.baseDamageOverride : spec.baseHitDamage;
                    float scale = configured?.damageScale ?? 1f;
                    int baseComputed = DamageSystem.RoundToEven((double)baseDamage * scale
                        * hitComboMultiplier
                        * (1f + snapshot.Might / 100f)
                        * Math.Max(0f, spec.cardLocalDamageMultiplier)
                        * snapshot.GlobalDamageMultiplier);
                    float manaBonus = Math.Max(0f, spec.manaDamageCoefficient) *
                        (float)Math.Max(source.BaseManaCost, snapshot.ManaBeforePayment);
                    double unrounded = Math.Max(0d, baseComputed * (1d + manaBonus));
                    int normal = DamageSystem.RoundToEven(unrounded);
                    int directCount = configured != null && configured.directTargetCountOverride >= 0
                        ? configured.directTargetCountOverride : pattern.directTargetsPerSubHit;
                    plan.AddSubHit(new AttackSubHit(
                        i,
                        subIndex,
                        normal,
                        configured != null && configured.overrideTargetPolicy
                            ? configured.targetPolicyOverride
                            : pattern.targetPolicy,
                        directCount,
                        ResolvePlayerCrit(configured?.critOverride ?? spec.crit, snapshot.Luck),
                        configured?.areaOverride ?? spec.area,
                        resolvedBounce,
                        spec.terrainEffect,
                        spec.onHitEffects,
                        unroundedHitDamage: unrounded));
                    // 卡面数字表示名义 Hit 序列的总伤，不是当前战场的全场总伤。
                    // MultiTarget / AllAttackable 只复制本 SubHit 的 Direct Event，
                    // 不应让卡面数字随目标数变化。
                    plan.AddDisplayedDamage(normal);
                }
            }
            return plan;
        }

        // Critical chance belongs to the player attack snapshot, never the shared card asset.
        // Disabled critical attacks remain disabled regardless of Luck.
        private static CritSpec ResolvePlayerCrit(CritSpec source, int luck)
        {
            if (source == null) return null;
            float chance = source.enabled
                ? Math.Max(0f, Math.Min(1f, source.chance * (1f + luck / 100f)))
                : source.chance;
            return new CritSpec { enabled = source.enabled, chance = chance,
                multiplier = source.multiplier, areaDamageBasis = source.areaDamageBasis };
        }

        /// <summary>
        /// 每鸟独立周期攻击：当前Might/Area，固定导弹数；不重放宿主的Combo、Amount、
        /// 卡牌灵印、手持加伤、LB、暴击、弹跳或附带效果。初击与周期共享同一伤害取整规则。
        /// </summary>
        internal static AttackPlan BuildBirdEndTurn(ResolvedBirdAttackSpec bird,
            CardAttackSnapshot source, int birdIndex, int birdCount, CombatStatSnapshot stats)
        {
            if (bird == null) throw new ArgumentNullException(nameof(bird));
            if (birdCount <= 0 || birdIndex < 0 || birdIndex >= birdCount ||
                (long)birdCount * bird.EndTurnMissilesPerBird > MaximumSubHitsPerPlan)
                throw new ArgumentException("Invalid bird periodic attack shape.");
            var plan = new AttackPlan(source.SourceCardInstanceId, stats.Area,
                canTriggerSecondaryEffects: false, residentBirdCount: birdCount);
            int damage = DamageSystem.RoundToEven((double)bird.EndTurnDamagePerHit * (1f + stats.Might / 100f));
            var area = new AreaSpec { enabled = bird.Area.enabled,
                coefficient = bird.Area.coefficient, triggerMode = bird.Area.triggerMode };
            var crit = new CritSpec { enabled = false };
            for (int i = 0; i < bird.EndTurnMissilesPerBird; i++)
            {
                plan.AddSubHit(new AttackSubHit(birdIndex, i, damage, TargetPolicy.LeftToRight,
                    1, crit, area, null, null, null));
                plan.AddDisplayedDamage(damage);
            }
            return plan;
        }

        /// <summary>
        /// 构建不受 Might、Combo、Amount、Crit、Area 或 LB 影响的单次固定直击。
        /// Shield Bash 等规则调用此意图入口，不再从外部拼装可变 AttackPlan。
        /// </summary>
        public static AttackPlan BuildFixedDirect(
            CardAttackSnapshot source,
            int damage,
            TargetPolicy targetPolicy = TargetPolicy.FirstAttackable,
            bool canTriggerSecondaryEffects = true)
        {
            int safeDamage = Math.Max(0, damage);
            var plan = new AttackPlan(
                source.SourceCardInstanceId,
                areaStat: 0,
                displayedCardDamage: safeDamage,
                canTriggerSecondaryEffects: canTriggerSecondaryEffects);
            plan.AddSubHit(new AttackSubHit(
                0,
                0,
                safeDamage,
                targetPolicy,
                1,
                new CritSpec { enabled = false },
                new AreaSpec
                {
                    enabled = false,
                    triggerMode = AreaTriggerMode.Disabled
                },
                bounce: null,
                terrainEffect: null,
                onHitEffects: Array.Empty<OnHitEffectSpec>()));
            return plan;
        }

        private static BounceSpec ResolveBounce(
            BounceSpec configured,
            int attackInstanceCount,
            int baseAttackInstanceCount,
            int amount)
        {
            if (configured == null || !configured.enabled)
                return null;

            if (configured.sequenceMode == BounceSequenceMode.NativePerAttackInstance)
            {
                long nativeHitCount = (long)Math.Max(0, baseAttackInstanceCount) + Math.Max(0, amount);
                if (nativeHitCount > MaximumSubHitsPerPlan)
                    throw new InvalidOperationException("Native bounce hit budget exceeds the attack plan limit.");
                return new BounceSpec
                {
                    enabled = true,
                    sequenceMode = configured.sequenceMode,
                    hitsPerDirectHit = (int)Math.Max(0L, nativeHitCount - 1L)
                };
            }

            if (configured.sequenceMode ==
                BounceSequenceMode.SynchronizedWavesByAttackInstanceCount)
            {
                return new BounceSpec
                {
                    enabled = true,
                    sequenceMode = configured.sequenceMode,
                    // N 个攻击物共执行 N 轮；第一轮是 Direct，余下 N-1 轮是 Bounce。
                    hitsPerDirectHit = Math.Max(0, attackInstanceCount - 1)
                };
            }

            return configured.hitsPerDirectHit > 0 ? configured : null;
        }
    }

    public readonly struct EnemyDefeatResult
    {
        public readonly int TargetEnemyRuntimeId;
        public readonly int TargetIndex;
        public readonly DefeatCause Cause;
        public readonly bool WasImmune;

        internal EnemyDefeatResult(int runtimeId, int targetIndex, DefeatCause cause, bool immune)
        {
            TargetEnemyRuntimeId = runtimeId;
            TargetIndex = targetIndex;
            Cause = cause;
            WasImmune = immune;
        }
    }

    internal static class DefeatResolver
    {
        /// <summary>
        /// Returns whether a target is protected from deterministic Erase.
        /// BossRank is the encounter role authority: ordinary enemies and
        /// floor bosses may be erased, while the stage boss remains immune.
        /// The legacy isBoss ability tag is intentionally ignored here so a
        /// reused boss-tagged enemy with no encounter rank does not become an
        /// accidental Erase exception.
        /// </summary>
        public static bool IsEraseImmune(EnemyStats target)
            => target == null || target.bossRank == BossRank.Stage;

        public static EnemyDefeatResult ResolveErase(IList<EnemyStats> enemies, int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= enemies.Count)
                return new EnemyDefeatResult(0, -1, DefeatCause.Erase, true);
            EnemyStats target = enemies[targetIndex];
            if (IsEraseImmune(target) || target.IsDead)
                return new EnemyDefeatResult(target.runtimeId, targetIndex, DefeatCause.Erase, true);
            target.currentHealth = 0;
            return new EnemyDefeatResult(target.runtimeId, targetIndex, DefeatCause.Erase, false);
        }
    }
}
