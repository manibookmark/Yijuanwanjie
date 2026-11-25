using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>旧调用兼容快照；新代码使用 CombatStatSnapshot。</summary>
    public readonly struct PrePlayStatsSnapshot
    {
        public readonly int Might, Area, Amount, Combo;
        public PrePlayStatsSnapshot(PlayerStats player, int combo)
        { Might = player?.fengRui ?? 0; Area = player?.bengLie ?? 0; Amount = player?.amount ?? 0; Combo = Math.Max(0, combo); }
        public PrePlayStatsSnapshot(int might, int area, int amount, int combo)
        { Might = might; Area = area; Amount = amount; Combo = Math.Max(0, combo); }
        public CombatStatSnapshot ToCombatSnapshot()
        {
            var player = new PlayerStats { fengRui = Might, bengLie = Area, amount = Amount };
            return new CombatStatSnapshot(player, Combo);
        }
    }

    public readonly struct DamageEvent
    {
        public readonly int SourceCardInstanceId;
        public readonly int TargetEnemyRuntimeId;
        public readonly int TargetIndex; // 旧 UI 兼容
        public readonly int AttackInstanceIndex;
        public readonly int SubHitIndex;
        public readonly int HitIndex; // 旧 UI 兼容
        public readonly DamageKind Kind;
        public readonly int RequestedDamage;
        public readonly int AppliedDamage;
        public readonly int ShieldDamage;
        public readonly int HpDamage;
        public readonly int OverkillDamage;
        public readonly bool IsCritical;
        public readonly bool IsFatal;
        public readonly bool CanTriggerArea;
        public readonly bool CanTriggerSecondaryEffects;
        public int ResolvedDamage => AppliedDamage + OverkillDamage;

        internal DamageEvent(int sourceCardInstanceId, int targetRuntimeId, int targetIndex,
            int attackInstanceIndex, int subHitIndex, DamageKind kind, int requested,
            int applied, int shield, int hp, bool critical, bool fatal,
            bool canTriggerArea = false, bool canTriggerSecondaryEffects = false,
            int overkillDamage = 0)
        {
            SourceCardInstanceId = sourceCardInstanceId;
            TargetEnemyRuntimeId = targetRuntimeId;
            TargetIndex = targetIndex;
            AttackInstanceIndex = attackInstanceIndex;
            SubHitIndex = subHitIndex;
            HitIndex = attackInstanceIndex;
            Kind = kind;
            RequestedDamage = requested;
            AppliedDamage = applied;
            ShieldDamage = shield;
            HpDamage = hp;
            OverkillDamage = overkillDamage;
            IsCritical = critical;
            IsFatal = fatal;
            CanTriggerArea = canTriggerArea;
            CanTriggerSecondaryEffects = canTriggerSecondaryEffects;
        }
    }

    public sealed class AttackResolutionResult
    {
        private readonly List<DamageEvent> _events = new List<DamageEvent>();
        private readonly List<int> _directTargetColumns = new List<int>();
        private readonly List<EnemyDefeatResult> _defeats =
            new List<EnemyDefeatResult>();
        private readonly List<ControlEffectResult> _controlEffects =
            new List<ControlEffectResult>();
        private readonly List<TerrainEffectPlacement> _terrainEffectPlacements =
            new List<TerrainEffectPlacement>();
        private readonly List<ProjectileTrace> _projectileTraces = new List<ProjectileTrace>();

        public int HitDamage { get; internal set; }
        public int HitCount { get; internal set; }
        public int DisplayedCardDamage { get; internal set; }
        public IReadOnlyList<DamageEvent> Events { get; }
        /// <summary>直击选列记录，含有序攻击物的空列占位；保留重复，不含范围或弹跳。</summary>
        public IReadOnlyList<int> DirectTargetColumns { get; }
        public IReadOnlyList<EnemyDefeatResult> Defeats { get; }
        public IReadOnlyList<ControlEffectResult> ControlEffects { get; }
        public IReadOnlyList<TerrainEffectPlacement> TerrainEffectPlacements { get; }
        public IReadOnlyList<ProjectileTrace> ProjectileTraces { get; }
        public int TotalAppliedDamage { get { int n = 0; foreach (var e in _events) n += e.AppliedDamage; return n; } }
        public int TotalResolvedDamage { get { int n = 0; foreach (var e in _events) n += e.ResolvedDamage; return n; } }

        internal AttackResolutionResult()
        {
            Events = new ReadOnlyCollection<DamageEvent>(_events);
            DirectTargetColumns = _directTargetColumns.AsReadOnly();
            Defeats = new ReadOnlyCollection<EnemyDefeatResult>(_defeats);
            ControlEffects = new ReadOnlyCollection<ControlEffectResult>(
                _controlEffects);
            TerrainEffectPlacements =
                new ReadOnlyCollection<TerrainEffectPlacement>(
                    _terrainEffectPlacements);
            ProjectileTraces = _projectileTraces.AsReadOnly();
        }

        internal bool AddDamage(
            EnemyDamageReceipt receipt,
            ICollection<EnemyDefeatResult> currentBatchDefeats = null)
        {
            if (_events.Count >= AttackPlan.MaximumResolutionEventsPerPlan)
            {
                throw new InvalidOperationException(
                    $"攻击结算已达 " +
                    $"{AttackPlan.MaximumResolutionEventsPerPlan} 个伤害事件的" +
                    "运行时安全上限。");
            }
            _events.Add(receipt.Event);
            if (receipt.TryGetDefeat(out EnemyDefeatResult defeat))
            {
                _defeats.Add(defeat);
                currentBatchDefeats?.Add(defeat);
                return true;
            }
            return false;
        }

        internal void AddDefeat(EnemyDefeatResult defeat)
            => _defeats.Add(defeat);

        internal void RecordDirectTargetColumn(int column)
            => _directTargetColumns.Add(column);

        internal void AddControlEffect(ControlEffectResult effect)
            => _controlEffects.Add(effect);

        internal void AddTerrainEffectPlacement(
            TerrainEffectPlacement placement)
            => _terrainEffectPlacements.Add(placement);
        internal void AddProjectileTrace(ProjectileTrace trace) => _projectileTraces.Add(trace);
    }

    /// <summary>
    /// 一次 Direct Hit 产生的地形放置结果。它记录命中瞬间的格子、
    /// 规则快照和表现定位信息，不持有敌人引用。
    /// </summary>
    public readonly struct TerrainEffectPlacement
    {
        public readonly string EffectId;
        public readonly TerrainEffectKind EffectKind;
        public readonly TerrainEffectTiming TriggerTiming;
        public readonly DamageKind DamageKind;
        public readonly int SourceCardInstanceId;
        public readonly int AttackInstanceIndex;
        public readonly int TargetIndex;
        public readonly int Column;
        public readonly int Row;
        public readonly int MagnitudePerStack;
        public readonly int TriggerCount;
        public readonly TerrainTargetMode TargetMode;
        public readonly float AreaCoefficient;
        public readonly bool PersistUntilCombatEnd;

        public TerrainEffectPlacement(
            string effectId,
            TerrainEffectKind effectKind,
            TerrainEffectTiming triggerTiming,
            DamageKind damageKind,
            int sourceCardInstanceId,
            int attackInstanceIndex,
            int targetIndex,
            int column,
            int row,
            int magnitudePerStack,
            int triggerCount,
            TerrainTargetMode targetMode = TerrainTargetMode.CellSnapshot,
            float areaCoefficient = 0.2f,
            bool persistUntilCombatEnd = false)
        {
            EffectId = effectId ?? string.Empty;
            EffectKind = effectKind;
            TriggerTiming = triggerTiming;
            DamageKind = damageKind;
            SourceCardInstanceId = sourceCardInstanceId;
            AttackInstanceIndex = attackInstanceIndex;
            TargetIndex = targetIndex;
            Column = column;
            Row = row;
            MagnitudePerStack = Math.Max(0, magnitudePerStack);
            TriggerCount = Math.Max(1, triggerCount);
            TargetMode = targetMode;
            AreaCoefficient = Math.Max(0f, areaCoefficient);
            PersistUntilCombatEnd = persistUntilCombatEnd;
        }
    }

    public readonly struct ControlEffectResult
    {
        public readonly OnHitEffectType EffectType;
        public readonly int SourceCardInstanceId;
        public readonly int AttackInstanceIndex;
        public readonly int SubHitIndex;
        public readonly int TargetEnemyRuntimeId;
        public readonly int TargetIndex;
        public readonly IReadOnlyList<int> AffectedIndices;
        /// <summary>Frozen at this individual control result, before later hits move the same actors again.</summary>
        public readonly IReadOnlyList<FormationAdvanceMove> Moves;
        /// <summary>
        /// 本次控制效果中由可见第三排越过到隐藏区的敌人索引。
        /// 仅记录边界穿越，不把原本就在隐藏区的后备波次算入。
        /// </summary>
        public readonly IReadOnlyList<int> PushedBeyondVisibleIndices;

        public ControlEffectResult(OnHitEffectType effectType, int runtimeId, int targetIndex,
            IReadOnlyList<int> affectedIndices,
            IReadOnlyList<int> pushedBeyondVisibleIndices = null,
            int sourceCardInstanceId = 0, int attackInstanceIndex = -1, int subHitIndex = -1,
            IReadOnlyList<FormationAdvanceMove> moves = null)
        {
            EffectType = effectType;
            SourceCardInstanceId = sourceCardInstanceId;
            AttackInstanceIndex = attackInstanceIndex;
            SubHitIndex = subHitIndex;
            TargetEnemyRuntimeId = runtimeId;
            TargetIndex = targetIndex;
            AffectedIndices = Freeze(affectedIndices);
            PushedBeyondVisibleIndices = Freeze(pushedBeyondVisibleIndices);
            Moves = Freeze(moves);
        }

        private static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T> values)
        {
            if (values == null || values.Count == 0) return Array.Empty<T>();
            var snapshot = new T[values.Count];
            for (int i = 0; i < snapshot.Length; i++) snapshot[i] = values[i];
            return Array.AsReadOnly(snapshot);
        }
    }

    public readonly struct EnemyDamagePreview
    {
        public readonly int TargetEnemyRuntimeId;
        public readonly int TargetIndex;
        /// <summary>
        /// 兼容协议的已结算总量：实际扣除与余威之和。
        /// 界面伤害数字使用 AppliedDamage，不把余威混入扣除量。
        /// </summary>
        public readonly int Damage;
        /// <summary>
        /// 本次攻击向该目标发出的完整名义威力，保留用于规则观察。
        /// </summary>
        public readonly int RequestedDamage;
        /// <summary>本牌即时实际扣除的护甲与生命之和。</summary>
        public readonly int AppliedDamage;
        /// <summary>合法 Boss 尸体伤害，独立于护甲与生命扣除。</summary>
        public readonly int OverkillDamage;
        /// <summary>抹除导致的击败；不制造伤害数值。</summary>
        public readonly bool IsErased;
        public readonly bool WillKill;
        /// <summary>
        /// True when the RNG-free estimate omits target/critical/control decisions.
        /// In that case WillKill describes the estimate, not a promised outcome.
        /// A frozen actual projection can still be marked as an estimate when
        /// authored prerequisites outside its attack plan remain unresolved.
        /// </summary>
        public readonly bool IsEstimate;

        public EnemyDamagePreview(int runtimeId, int targetIndex, int damage, bool willKill)
            : this(runtimeId, targetIndex, damage, damage, willKill)
        {
        }

        public EnemyDamagePreview(
            int runtimeId,
            int targetIndex,
            int damage,
            int requestedDamage,
            bool willKill,
            bool isEstimate = false)
            : this(runtimeId, targetIndex, damage, 0, requestedDamage,
                willKill, isEstimate)
        {
        }

        public EnemyDamagePreview(
            int runtimeId,
            int targetIndex,
            int appliedDamage,
            int overkillDamage,
            int requestedDamage,
            bool willKill,
            bool isEstimate = false,
            bool isErased = false)
        {
            TargetEnemyRuntimeId = runtimeId;
            TargetIndex = targetIndex;
            AppliedDamage = Math.Max(0, appliedDamage);
            OverkillDamage = Math.Max(0, overkillDamage);
            Damage = checked(AppliedDamage + OverkillDamage);
            RequestedDamage = Math.Max(0, requestedDamage);
            WillKill = willKill;
            IsEstimate = isEstimate;
            IsErased = isErased;
        }
    }

    /// <summary>
    /// 首次 Hover 在克隆战场上形成的完整攻击投影。伤害预览供表现层读取，
    /// 随机带只允许对应的实际攻击计划消费一次。
    /// </summary>
    internal sealed class AttackProjection
    {
        public AttackPlan Plan { get; }
        public IReadOnlyList<EnemyDamagePreview> DamagePreviews { get; }
        public IReadOnlyList<EnemyIntent> UpcomingIntents { get; }
        private RecordedRandomTape RandomTape { get; }

        public AttackProjection(
            AttackPlan plan,
            IReadOnlyList<EnemyDamagePreview> damagePreviews,
            RecordedRandomTape randomTape,
            IReadOnlyList<EnemyIntent> upcomingIntents)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            DamagePreviews = damagePreviews ??
                throw new ArgumentNullException(nameof(damagePreviews));
            RandomTape = randomTape ??
                throw new ArgumentNullException(nameof(randomTape));
            UpcomingIntents = upcomingIntents == null
                ? (IReadOnlyList<EnemyIntent>)Array.Empty<EnemyIntent>()
                : new List<EnemyIntent>(upcomingIntents).AsReadOnly();
        }

        public IRandomSource CreateReplayRandomSource()
            => RandomTape.CreateReplaySource();
    }

    /// <summary>
    /// AttackPlan 的唯一执行器；Preview 与 Actual 只通过 ResolutionMode 区分随机策略。
    /// 规模审查：本执行器超过800行但仍只拥有计划执行与结果聚合知识；周期空间游标
    /// 由批次owner显式传入，不在此持久保存，也不改变冻结计划/伤害/目标资格。
    /// 后续独立规则仍须按原有resolver边界封装。
    /// </summary>
    internal static class AttackResolver
    {
        // Resolution remains the single ordered damage/placement boundary. Its
        // 800-line review keeps new coverage state in TerrainAttackCoverage;
        // adding another independent attack family requires a new boundary review.
        public static AttackResolutionResult Execute(AttackPlan plan, IList<EnemyStats> enemies,
            ResolutionMode mode, IRandomSource random = null,
            IReadOnlyList<EnemyIntent> upcomingIntents = null,
            BossOverkillSession bossOverkill = null,
            AttackTargetSelector.SpatialProgress spatialProgress = null,
            IReadOnlyList<TerrainEffectInstance> activeTerrainEffects = null,
            TerrainAttackCoverage terrainCoverage = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            ValidateStableRuntimeIds(enemies);
            bossOverkill?.RequireRoster(enemies);
            plan.RequireResolutionEventBudget(enemies.Count);
            if (plan.RicochetProjectile != null)
                return RicochetProjectileResolver.Execute(plan, enemies, mode, random, bossOverkill);
            if (NativeBounceAttackResolver.IsRequired(plan))
                return NativeBounceAttackResolver.Execute(plan, enemies, bossOverkill);
            if (SynchronizedBounceWaveResolver.IsRequired(plan))
                return SynchronizedBounceWaveResolver.Execute(
                    plan,
                    enemies,
                    bossOverkill);

            var result = new AttackResolutionResult
            {
                HitCount = plan.SubHits.Count,
                HitDamage = plan.SubHits.Count > 0 ? plan.SubHits[0].NormalHitDamage : 0,
                DisplayedCardDamage = plan.DisplayedCardDamage
            };
            FrozenAttackTargetSet frozenTargets = null;
            for (int i = 0; i < plan.SubHits.Count; i++)
                if (plan.SubHits[i].TargetPolicy == TargetPolicy.FrozenAttackableSet)
                {
                    frozenTargets = new FrozenAttackTargetSet(enemies, bossOverkill);
                    break;
                }
            TerrainAttackCoverage coverage = terrainCoverage ??
                new TerrainAttackCoverage(activeTerrainEffects);
            var targetSelector = new AttackTargetSelector(
                mode,
                random,
                upcomingIntents,
                bossOverkill,
                spatialProgress,
                frozenTargets,
                coverage);

            for (int i = 0; i < plan.SubHits.Count; i++)
            {
                AttackSubHit subHit = plan.SubHits[i];
                var batchDefeats = new List<EnemyDefeatResult>();
                targetSelector.BeginSubHit(
                    enemies,
                    subHit.TargetPolicy,
                    subHit.DirectTargetCount,
                    subHit.TargetColumn,
                    subHit.TerrainEffect);
                var directTargets = new List<int>();
                int effectiveDirectCount = 0;
                int largestFinalDirect = subHit.NormalHitDamage;
                while (targetSelector.TrySelectNext(
                           enemies,
                           out int target))
                {
                    // 每个目标位都读取上一个目标结算后的阵型。先处理仍能在本轮
                    // 威胁玩家的近排；同排目标不足时，余下目标位继续选择后排。
                    directTargets.Add(target);
                    if (target < 0 || target >= enemies.Count ||
                        enemies[target] == null ||
                        !enemies[target].IsDeployed ||
                        (enemies[target].IsDead &&
                         bossOverkill?.IsActiveTarget(target) != true))
                    {
                        continue;
                    }
                    effectiveDirectCount++;
                    EnemyStats directTarget = enemies[target];
                    int groundColumn = directTarget.column >= 0
                        ? directTarget.column
                        : target % EnemyFormationLayout.Columns;
                    int groundRow = Math.Max(0, directTarget.row);
                    result.RecordDirectTargetColumn(subHit.TargetColumn >= 0
                        ? subHit.TargetColumn : groundColumn);
                    bool crit = ShouldCrit(subHit.Crit, mode, random);
                    int finalDirect = crit
                        ? DamageSystem.RoundToEven(subHit.UnroundedHitDamage * Math.Max(1f, subHit.Crit.multiplier))
                        : subHit.NormalHitDamage;
                    largestFinalDirect = Math.Max(largestFinalDirect, finalDirect);
                    EnemyDamageReceipt directReceipt = ApplyDamage(
                        enemies, target, plan.SourceCardInstanceId,
                        subHit.AttackInstanceIndex, subHit.SubHitIndex, DamageKind.Direct, finalDirect, crit,
                        subHit.Area != null && subHit.Area.enabled,
                        plan.CanTriggerSecondaryEffects);
                    result.AddDamage(directReceipt, batchDefeats);

                    // areaHitsAllPierceTargets: resolve this direct hit's Area
                    // immediately. Its deaths change the next column's front.
                    if (subHit.Area?.triggerMode == AreaTriggerMode.PerDirectTarget)
                        ResolveArea(plan, subHit, enemies, result, batchDefeats,
                            subHit.Crit?.areaDamageBasis == AreaDamageBasis.FinalDirectDamage
                                ? finalDirect : subHit.UnroundedHitDamage,
                            subHit.TargetPolicy == TargetPolicy.FrozenAttackableSet ? frozenTargets : null);

                    // 弹跳（Bone 骨架）：每次直击命中后，沿同一目标策略序列追加命中
                    // 下一名可攻击敌人，不重复本次挥击已命中的目标；伤害沿用本 Hit 普通值。
                    if (subHit.Bounce != null)
                    {
                        var bounceVisited = new HashSet<int> { target };
                        for (int b = 0; b < subHit.Bounce.hitsPerDirectHit; b++)
                        {
                            int bounceTarget = PickBounceTarget(enemies, bounceVisited, subHit.TargetPolicy);
                            if (bounceTarget < 0) break;
                            bounceVisited.Add(bounceTarget);
                            EnemyDamageReceipt bounceReceipt = ApplyDamage(
                                enemies, bounceTarget, plan.SourceCardInstanceId,
                                subHit.AttackInstanceIndex, subHit.SubHitIndex, DamageKind.Bounce,
                                subHit.NormalHitDamage +
                                subHit.Bounce.damageIncrementPerBounce * (b + 1),
                                false);
                            result.AddDamage(bounceReceipt, batchDefeats);
                        }
                    }

                    if (subHit.TerrainEffect != null &&
                        subHit.TerrainEffect.magnitudeScale > 0f &&
                        subHit.TerrainEffect.triggerCount > 0 &&
                        !string.IsNullOrWhiteSpace(subHit.TerrainEffect.effectId))
                    {
                        int magnitude = subHit.TerrainEffect.targetMode == TerrainTargetMode.ColumnDynamicFront
                            ? subHit.TerrainEffect.baseMagnitude
                            : DamageSystem.RoundHalfUp(
                                subHit.NormalHitDamage * subHit.TerrainEffect.magnitudeScale);
                        if (magnitude > 0)
                        {
                            var placement = new TerrainEffectPlacement(
                                subHit.TerrainEffect.effectId,
                                subHit.TerrainEffect.effectKind,
                                subHit.TerrainEffect.triggerTiming,
                                subHit.TerrainEffect.damageKind,
                                plan.SourceCardInstanceId,
                                subHit.AttackInstanceIndex,
                                target,
                                groundColumn,
                                subHit.TerrainEffect.targetMode == TerrainTargetMode.ColumnDynamicFront
                                    ? 0 : groundRow,
                                magnitude,
                                subHit.TerrainEffect.triggerCount,
                                subHit.TerrainEffect.targetMode,
                                subHit.TerrainEffect.areaCoefficient,
                                subHit.TerrainEffect.persistUntilCombatEnd);
                            result.AddTerrainEffectPlacement(placement);
                            coverage.ObservePlacement(placement);
                        }
                    }
                }
                // Empty ordered projectiles keep a selected column for one-turn
                // replay, but never manufacture damage, kills or Area triggers.
                if (directTargets.Count == 0)
                {
                    // An exhausted frozen universe only ends this sub-hit;
                    // later ordinary sub-hits may still have legal targets.
                    if (subHit.TargetPolicy == TargetPolicy.FrozenAttackableSet)
                        continue;
                    if (subHit.DirectTargetCount != 1 ||
                        !targetSelector.TrySelectEmptyColumn(out int emptyColumn))
                        break;
                    result.RecordDirectTargetColumn(emptyColumn);
                    // Consume every remaining projectile, rather than stopping
                    // the plan at the first empty slot.
                    continue;
                }

                // 所有 Direct 及死亡先完成；随后按“每列最前”刷新 Area 目标。
                if (subHit.Area != null && subHit.Area.enabled &&
                    subHit.Area.triggerMode == AreaTriggerMode.PerSubHit &&
                    plan.AreaStat > 0 && effectiveDirectCount > 0)
                {
                    ResolveArea(plan, subHit, enemies, result, batchDefeats,
                        subHit.Crit?.areaDamageBasis == AreaDamageBasis.FinalDirectDamage
                            ? largestFinalDirect : subHit.UnroundedHitDamage,
                        subHit.TargetPolicy == TargetPolicy.FrozenAttackableSet ? frozenTargets : null);
                }

                // Direct 与 Area 完成后，按每个 Direct Hit 独立判定附带控制效果。
                if (mode == ResolutionMode.Actual && subHit.OnHitEffects != null)
                {
                    for (int d = 0; d < directTargets.Count; d++)
                    {
                        int target = directTargets[d];
                        if (target < 0 || target >= enemies.Count || enemies[target].IsDead) continue;
                        for (int effectIndex = 0; effectIndex < subHit.OnHitEffects.Count; effectIndex++)
                        {
                            ResolvedOnHitEffectSpec effect =
                                subHit.OnHitEffects[effectIndex];
                            float multiplier = effect.effectType == OnHitEffectType.Knockback
                                ? enemies[target].EffectiveKnockbackMultiplier
                                : enemies[target].EffectiveDisarmMultiplier;
                            if ((random?.Value01() ?? UnityEngine.Random.value) >=
                                Mathf.Clamp01(effect.baseChance * Math.Max(0f, multiplier))) continue;

                            if (effect.effectType == OnHitEffectType.Disarm)
                            {
                                enemies[target].isDisarmed = true;
                                result.AddControlEffect(new ControlEffectResult(effect.effectType,
                                    enemies[target].runtimeId, target, new[] { target },
                                    sourceCardInstanceId: plan.SourceCardInstanceId,
                                    attackInstanceIndex: subHit.AttackInstanceIndex,
                                    subHitIndex: subHit.SubHitIndex));
                            }
                            else if (TryResolveKnockback(
                                         enemies,
                                         target,
                                         out List<int> affected,
                                         out List<int> pushedBeyondVisible))
                            {
                                // Knockback has already moved each affected actor exactly one row.
                                // Freeze these per-hit facts now; later sub-hits may move them again.
                                var moves = new List<FormationAdvanceMove>(affected.Count);
                                for (int moved = 0; moved < affected.Count; moved++)
                                {
                                    int index = affected[moved];
                                    EnemyStats enemy = enemies[index];
                                    moves.Add(new FormationAdvanceMove(enemy.runtimeId, index,
                                        new FormationFootprint(enemy.row - 1, enemy.column,
                                            enemy.GridWidth, enemy.GridDepth),
                                        new FormationFootprint(enemy.row, enemy.column,
                                            enemy.GridWidth, enemy.GridDepth)));
                                }
                                result.AddControlEffect(new ControlEffectResult(effect.effectType,
                                    enemies[target].runtimeId, target, affected,
                                    pushedBeyondVisible, plan.SourceCardInstanceId,
                                    subHit.AttackInstanceIndex, subHit.SubHitIndex, moves));
                            }
                        }
                    }
                }

                // 余威目标只在整次 SubHit（含 Area/附带效果）完成后激活，避免尸体吃到本 Hit 的 Area。
                bossOverkill?.TryBeginAfterDamageBatch(batchDefeats);
            }
            return result;
        }

        private static void ResolveArea(AttackPlan plan, AttackSubHit hit,
            IList<EnemyStats> enemies, AttackResolutionResult result,
            List<EnemyDefeatResult> defeats, double basis,
            FrozenAttackTargetSet frozenTargets)
        {
            if (hit.Area?.enabled != true || plan.AreaStat <= 0) return;
            int splash = DamageSystem.CalculateSplashDamage(basis, plan.AreaStat, hit.Area.coefficient);
            if (splash <= 0) return;
            List<int> targets = frozenTargets != null
                ? frozenTargets.SelectAreaTargets(enemies)
                : GetAttackableTargets(enemies);
            foreach (int target in targets)
                result.AddDamage(ApplyDamage(enemies, target, plan.SourceCardInstanceId,
                    hit.AttackInstanceIndex, hit.SubHitIndex, DamageKind.Splash, splash, false), defeats);
        }

        internal static AttackResolutionResult Resolve(CardConfig card, int baseDamage, int baseAttackInstanceCount,
            PrePlayStatsSnapshot snapshot, IList<EnemyStats> enemies, int ignoredPlayedTarget,
            Func<bool> rollCrit = null, Func<int, int, int> randomRange = null)
        {
            DamageSpec spec = AttackPlanBuilder.FromLegacy(card, baseDamage, baseAttackInstanceCount);
            AttackPlan plan = AttackPlanBuilder.Build(spec, snapshot.ToCombatSnapshot());
            IRandomSource random = rollCrit == null && randomRange == null ? null : new DelegateRandomSource(rollCrit, randomRange);
            return Execute(plan, enemies, ResolutionMode.Actual, random);
        }

        internal static List<EnemyDamagePreview> Preview(CardConfig card, int baseDamage, int baseAttackInstanceCount,
            PrePlayStatsSnapshot snapshot, IList<EnemyStats> enemies, int ignoredPlayedTarget,
            IReadOnlyList<bool> ignoredPredictedCrits = null,
            IReadOnlyList<TerrainEffectInstance> activeTerrainEffects = null)
        {
            DamageSpec spec = AttackPlanBuilder.FromLegacy(card, baseDamage, baseAttackInstanceCount);
            AttackPlan plan = AttackPlanBuilder.Build(spec, snapshot.ToCombatSnapshot());
            return Preview(plan, enemies, activeTerrainEffects: activeTerrainEffects);
        }

        public static List<EnemyDamagePreview> Preview(AttackPlan plan, IList<EnemyStats> enemies,
            IReadOnlyList<EnemyIntent> upcomingIntents = null,
            BossOverkillSession bossOverkill = null,
            IReadOnlyList<TerrainEffectInstance> activeTerrainEffects = null)
        {
            ValidateStableRuntimeIds(enemies);
            bossOverkill?.RequireRoster(enemies);
            List<EnemyStats> clones = CloneEnemies(enemies);
            BossOverkillSession previewOverkill =
                bossOverkill?.CreatePreviewSession(clones);
            AttackResolutionResult resolved = Execute(plan, clones, ResolutionMode.Preview,
                upcomingIntents: upcomingIntents,
                bossOverkill: previewOverkill,
                activeTerrainEffects: activeTerrainEffects);
            return AggregatePreview(clones, resolved, HasUnresolvedPreviewDecisions(plan));
        }

        // The estimate deliberately does not inspect or advance the live RNG.
        // Keep its uncertainty beside the rules it omits, not in each consumer.
        public static bool HasUnresolvedPreviewDecisions(AttackPlan plan)
        {
            if (plan == null) return false;
            if (plan.RicochetProjectile != null) return true;
            foreach (AttackSubHit hit in plan.SubHits)
            {
                if (hit.TargetPolicy == TargetPolicy.RandomAttackable ||
                    hit.TargetPolicy == TargetPolicy.RandomColumnBag ||
                    hit.TargetPolicy == TargetPolicy.RandomColumnBagMultiHit ||
                    (hit.Crit != null && hit.Crit.enabled && hit.Crit.chance > 0f))
                    return true;
                if (hit.OnHitEffects == null) continue;
                foreach (ResolvedOnHitEffectSpec effect in hit.OnHitEffects)
                    if (effect.baseChance > 0f) return true;
            }
            return false;
        }

        /// <summary>
        /// 正式 Hover 入口：在克隆状态上按 Actual 规则只 Roll 一次，并冻结该次
        /// 目标、逐 Hit 暴击与附带效果消费的完整随机序列。真实战场不会被修改。
        /// </summary>
        public static AttackProjection Project(
            AttackPlan plan,
            IList<EnemyStats> enemies,
            IRandomSource random,
            IReadOnlyList<EnemyIntent> upcomingIntents = null,
            BossOverkillSession bossOverkill = null,
            bool isEstimate = false,
            IReadOnlyList<TerrainEffectInstance> activeTerrainEffects = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            ValidateStableRuntimeIds(enemies);
            bossOverkill?.RequireRoster(enemies);
            List<EnemyStats> clones = CloneEnemies(enemies);
            BossOverkillSession previewOverkill =
                bossOverkill?.CreatePreviewSession(clones);
            var recording = new RecordingRandomSource(
                random ?? DefaultRandomSource.Instance);
            AttackResolutionResult resolved = Execute(
                plan,
                clones,
                ResolutionMode.Actual,
                recording,
                upcomingIntents,
                previewOverkill,
                activeTerrainEffects: activeTerrainEffects);
            return new AttackProjection(
                plan,
                AggregatePreview(clones, resolved, isEstimate).AsReadOnly(),
                recording.Freeze(),
                upcomingIntents);
        }

        private static List<EnemyDamagePreview> AggregatePreview(
            IReadOnlyList<EnemyStats> resolvedEnemies,
            AttackResolutionResult resolved,
            bool isEstimate = false)
        {
            var appliedTotals = new Dictionary<int, int>();
            var overkillTotals = new Dictionary<int, int>();
            var requestedTotals = new Dictionary<int, int>();
            foreach (DamageEvent e in resolved.Events)
            {
                int key = e.TargetEnemyRuntimeId;
                appliedTotals[key] = appliedTotals.TryGetValue(
                        key, out int oldApplied)
                    ? oldApplied + e.AppliedDamage
                    : e.AppliedDamage;
                overkillTotals[key] = overkillTotals.TryGetValue(
                        key, out int oldOverkill)
                    ? oldOverkill + e.OverkillDamage
                    : e.OverkillDamage;
                requestedTotals[key] = requestedTotals.TryGetValue(
                        key, out int oldRequested)
                    ? oldRequested + e.RequestedDamage
                    : e.RequestedDamage;
            }
            var previews = new List<EnemyDamagePreview>();
            for (int i = 0; i < resolvedEnemies.Count; i++)
            {
                int key = resolvedEnemies[i].runtimeId;
                if (!requestedTotals.TryGetValue(
                        key, out int requestedDamage) ||
                    requestedDamage <= 0)
                {
                    continue;
                }

                appliedTotals.TryGetValue(key, out int appliedDamage);
                overkillTotals.TryGetValue(key, out int overkillDamage);
                previews.Add(new EnemyDamagePreview(
                    resolvedEnemies[i].runtimeId,
                    i,
                    appliedDamage,
                    overkillDamage,
                    requestedDamage,
                    resolvedEnemies[i].IsDead,
                    isEstimate));
            }
            return previews;
        }

        public static List<int> GetAttackableTargets(List<EnemyStats> enemies)
            => GetAttackableTargetsCore(enemies.Count, index => enemies[index]);

        public static List<int> GetAttackableTargets(IList<EnemyStats> enemies)
            => GetAttackableTargetsCore(enemies.Count, index => enemies[index]);

        public static List<int> GetAttackableTargets(IReadOnlyList<EnemyStats> enemies)
            => GetAttackableTargetsCore(enemies.Count, index => enemies[index]);

        private static List<int> GetAttackableTargetsCore(int enemyCount, Func<int, EnemyStats> getEnemy)
        {
            // 玩家的可攻击集合 = 每列最前的可见存活敌人。不同列可以暴露在
            // 不同深度；返回顺序先按排、再按列，供自动选敌优先消除近排威胁。
            var result = new List<int>();
            var seenIndices = new HashSet<int>();
            for (int column = 0; column < EnemyFormationLayout.Columns; column++)
            {
                int best = -1;
                int bestRow = int.MaxValue;
                for (int i = 0; i < enemyCount; i++)
                {
                    EnemyStats enemy = getEnemy(i);
                    if (enemy == null || enemy.IsDead ||
                        !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy))
                    {
                        continue;
                    }
                    int enemyColumn = enemy.column >= 0
                        ? enemy.column
                        : i % EnemyFormationLayout.Columns;
                    if (enemyColumn > column ||
                        enemyColumn + enemy.GridWidth <= column)
                    {
                        continue;
                    }
                    if (enemy.row < bestRow)
                    {
                        best = i;
                        bestRow = enemy.row;
                    }
                }
                if (best >= 0 && seenIndices.Add(best)) result.Add(best);
            }
            result.Sort((a, b) =>
            {
                EnemyStats left = getEnemy(a);
                EnemyStats right = getEnemy(b);
                int rowOrder = EnemyFormationLayout
                    .GetPlayerTargetPriorityRow(left)
                    .CompareTo(EnemyFormationLayout
                        .GetPlayerTargetPriorityRow(right));
                if (rowOrder != 0) return rowOrder;
                int leftColumn = left.column >= 0
                    ? left.column
                    : a % EnemyFormationLayout.Columns;
                int rightColumn = right.column >= 0
                    ? right.column
                    : b % EnemyFormationLayout.Columns;
                int columnOrder = leftColumn.CompareTo(rightColumn);
                return columnOrder != 0 ? columnOrder : a.CompareTo(b);
            });
            return result;
        }

        internal static AttackResolutionResult ResolveBounce(int baseDamage, int count,
            PrePlayStatsSnapshot snapshot, IList<EnemyStats> enemies, int ignoredTarget,
            BounceTargetMode mode = BounceTargetMode.UniqueThenStop,
            int excludedRuntimeId = 0,
            BossOverkillSession bossOverkill = null,
            int sourceCardInstanceId = 0)
        {
            if (count > AttackPlan.MaximumResolutionEventsPerPlan)
            {
                throw new InvalidOperationException(
                    $"弹跳结算超过 " +
                    $"{AttackPlan.MaximumResolutionEventsPerPlan} 个伤害事件的" +
                    "运行时安全上限。");
            }
            var result = new AttackResolutionResult { HitDamage = DamageSystem.RoundToEven((double)baseDamage * (1f + snapshot.Might / 100f)) };
            var visited = new HashSet<int>();
            if (excludedRuntimeId != 0) visited.Add(excludedRuntimeId);
            for (int i = 0; i < Math.Max(0, count); i++)
            {
                List<int> attackable = GetResolutionTargets(
                    enemies,
                    bossOverkill);
                attackable.RemoveAll(index => visited.Contains(enemies[index].runtimeId));
                if (attackable.Count == 0 && mode == BounceTargetMode.UniqueThenRepeat)
                {
                    visited.Clear();
                    if (excludedRuntimeId != 0) visited.Add(excludedRuntimeId);
                    attackable = GetResolutionTargets(
                        enemies,
                        bossOverkill);
                    attackable.RemoveAll(index => visited.Contains(enemies[index].runtimeId));
                }
                if (attackable.Count == 0) break;
                int target = PickNearestTierTarget(
                    attackable,
                    enemies,
                    reverse: true);
                visited.Add(enemies[target].runtimeId);
                EnemyDamageReceipt receipt = ApplyDamage(
                    enemies,
                    target,
                    sourceCardInstanceId,
                    i,
                    0,
                    DamageKind.Bounce,
                    result.HitDamage,
                    false);
                var batchDefeats = new List<EnemyDefeatResult>();
                result.AddDamage(receipt, batchDefeats);
                bossOverkill?.TryBeginAfterDamageBatch(batchDefeats);
            }
            result.HitCount = result.Events.Count;
            result.DisplayedCardDamage = result.HitDamage * result.HitCount;
            return result;
        }

        [Obsolete("Erase 不属于伤害；请使用 DefeatResolver.ResolveErase。")]
        internal static EnemyDefeatResult ResolveErase(IList<EnemyStats> enemies, int targetIndex)
            => DefeatResolver.ResolveErase(enemies, targetIndex);

        internal static EnemyDamageReceipt ApplyDamage(
            IList<EnemyStats> enemies,
            int targetIndex,
            int sourceCardId, int attackIndex, int subHitIndex, DamageKind kind, int amount, bool critical,
            bool canTriggerArea = false, bool canTriggerSecondaryEffects = false)
        {
            return DamageSystem.ResolveEnemyDamage(
                enemies,
                targetIndex,
                new EnemyDamageRequest(
                    sourceCardId,
                    attackIndex,
                    subHitIndex,
                    kind,
                    amount,
                    critical,
                    canTriggerArea,
                    canTriggerSecondaryEffects));
        }

        internal static List<int> GetResolutionTargets(
            IList<EnemyStats> enemies,
            BossOverkillSession bossOverkill)
        {
            List<int> result = GetAttackableTargets(enemies);
            if (result.Count == 0 && bossOverkill != null &&
                bossOverkill.TryGetActiveTargetIndex(
                    out int overkillTarget))
                result.Add(overkillTarget);
            return result;
        }

        /// <summary>
        /// 原子结算一次连锁撞退：后方任一单位更大、不可位移或越过隐藏区
        /// 上限时整条链失败。允许暂时进入隐藏排；连锁单位不再进行概率判定。
        /// </summary>
        internal static bool TryResolveKnockback(
            IList<EnemyStats> enemies,
            int targetIndex,
            out List<int> affected,
            out List<int> pushedBeyondVisible)
        {
            affected = new List<int>();
            pushedBeyondVisible = new List<int>();
            if (targetIndex < 0 || targetIndex >= enemies.Count) return false;
            EnemyStats primary = enemies[targetIndex];
            if (primary == null || primary.IsDead || !primary.IsDeployed ||
                !primary.canBeDisplaced)
            {
                return false;
            }

            var chain = new HashSet<int> { targetIndex };
            var queue = new Queue<int>();
            queue.Enqueue(targetIndex);
            while (queue.Count > 0)
            {
                int movingIndex = queue.Dequeue();
                EnemyStats moving = enemies[movingIndex];
                // 允许击退到隐藏排（row >= VisibleRows），只防止超出合理上限
                if (moving.row + 1 + moving.GridDepth > EnemyFormationLayout.VisibleRows + EnemyFormationLayout.HiddenRowLimit)
                    return false;

                for (int i = 0; i < enemies.Count; i++)
                {
                    if (chain.Contains(i)) continue;
                    EnemyStats blocker = enemies[i];
                    if (blocker == null || blocker.IsDead ||
                        !blocker.IsDeployed)
                    {
                        continue;
                    }
                    if (!RectanglesOverlap(moving.column, moving.row + 1, moving.GridWidth, moving.GridDepth,
                            blocker.column, blocker.row, blocker.GridWidth, blocker.GridDepth)) continue;
                    if (!blocker.canBeDisplaced || blocker.bodySize > primary.bodySize)
                        return false;
                    chain.Add(i);
                    queue.Enqueue(i);
                }
            }

            affected.AddRange(chain);
            affected.Sort((a, b) => enemies[b].row.CompareTo(enemies[a].row));
            for (int i = 0; i < affected.Count; i++)
            {
                EnemyStats moving = enemies[affected[i]];
                bool wasInside = EnemyFormationLayout.IsFullyInsideVisibleRows(moving);
                moving.row++;
                if (wasInside && !EnemyFormationLayout.IsFullyInsideVisibleRows(moving))
                {
                    pushedBeyondVisible.Add(affected[i]);
                }
            }
            return true;
        }

        private static bool RectanglesOverlap(int ax, int ay, int aw, int ah,
            int bx, int by, int bw, int bh)
            => ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

        private static bool ShouldCrit(
            ResolvedCritSpec crit,
            ResolutionMode mode,
            IRandomSource random)
        {
            return mode == ResolutionMode.Actual && crit != null && crit.enabled
                && (random?.Value01() ?? UnityEngine.Random.value) < Mathf.Clamp01(crit.chance);
        }

        /// <summary>
        /// 弹跳选敌：跳过本次挥击已命中的目标，先取离玩家最近的一排，
        /// 再按卡牌方向选择；无其他可攻击敌人时返回 -1。
        /// </summary>
        private static int PickBounceTarget(
            IList<EnemyStats> enemies, HashSet<int> visited, TargetPolicy policy)
        {
            List<int> attackable = GetAttackableTargets(enemies);
            attackable.RemoveAll(index => visited.Contains(index));
            if (attackable.Count == 0) return -1;
            bool reverse = policy == TargetPolicy.SequentialRightToLeft ||
                policy == TargetPolicy.PlayedTargetThenSequential;
            return PickNearestTierTarget(attackable, enemies, reverse);
        }

        private static int PickNearestTierTarget(
            List<int> orderedAttackable,
            IList<EnemyStats> enemies,
            bool reverse)
        {
            int nearestRow = EnemyFormationLayout.GetPlayerTargetPriorityRow(
                enemies[orderedAttackable[0]]);
            int tierEnd = 0;
            while (tierEnd + 1 < orderedAttackable.Count &&
                EnemyFormationLayout.GetPlayerTargetPriorityRow(
                    enemies[orderedAttackable[tierEnd + 1]]) == nearestRow)
            {
                tierEnd++;
            }
            return reverse
                ? orderedAttackable[tierEnd]
                : orderedAttackable[0];
        }

        private static List<EnemyStats> CloneEnemies(IList<EnemyStats> enemies)
        {
            ValidateStableRuntimeIds(enemies);
            var clones = new List<EnemyStats>(enemies.Count);
            foreach (EnemyStats source in enemies)
                clones.Add(source.CloneForSimulation());
            return clones;
        }

        private static void ValidateStableRuntimeIds(
            IList<EnemyStats> enemies)
        {
            if (enemies == null)
                throw new ArgumentNullException(nameof(enemies));

            var ids = new HashSet<int>();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null)
                    throw new ArgumentException(
                        $"敌人集合第 {i} 项为空。",
                        nameof(enemies));
                if (enemy.runtimeId <= 0 || !ids.Add(enemy.runtimeId))
                {
                    throw new ArgumentException(
                        "AttackResolver 要求每个敌人拥有单场唯一且大于 0 的 runtimeId。",
                        nameof(enemies));
                }
            }
        }

        private sealed class DelegateRandomSource : IRandomSource
        {
            private readonly Func<bool> _bool; private readonly Func<int, int, int> _range;
            public DelegateRandomSource(Func<bool> value, Func<int, int, int> range) { _bool = value; _range = range; }
            public float Value01() => _bool != null && _bool() ? 0f : 1f;
            public int Range(int min, int max) => _range != null ? _range(min, max) : min;
            public float Range(float min, float max) => min + (max - min) * Value01();
            public void Shuffle<T>(IList<T> list) { }
        }
    }
}
