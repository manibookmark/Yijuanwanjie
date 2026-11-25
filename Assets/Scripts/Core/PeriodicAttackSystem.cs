using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Owns encounter periodic sources, frozen-plan refresh, additive birds, turn consumption and mementos.
    /// Commands register a completed base attack or consume a player-turn boundary; damage/target
    /// rules remain in AttackResolver/AttackPlanBuilder. No CardConfig or mutable author spec is kept.
    /// Replaying commands rebuilds independent owners; restoring a memento replaces state silently.
    /// One turn-end batch owns shared policy/row spatial progress. It is neither persisted nor
    /// reused by ordinary casts. Frozen sources share that cursor; each bird rebuilds its current-stat
    /// plan with a fresh cursor. Both kinds retain independent once-per-turn lifecycle identities.
    /// </summary>
    public sealed class PeriodicAttackSystem
    {
        private readonly CombatState _state;
        private readonly List<PeriodicAttackSource> _active = new List<PeriodicAttackSource>();
        private int _nextRuntimeId = 1;
        private bool _triggering;

        public IReadOnlyList<PeriodicAttackSource> Active { get; }
        public event Action<PeriodicAttackEvent> Changed;
        internal event Action<AttackResolutionResult> AttackResolved;

        internal PeriodicAttackSystem(CombatState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            Active = _active.AsReadOnly();
        }

        internal void RegisterResolvedAttack(CardConfig card, CardEffectSpec effect, AttackPlan plan)
        {
            if (effect?.repeatAtPlayerTurnEnd != true || effect.effectType != EffectType.Damage ||
                plan == null || plan.SubHits.Count == 0 || _state.IsBattleOver || _state.IsDefeat)
                return;
            if (card == null || card.runtimeInstanceId <= 0 ||
                card.runtimeInstanceId != plan.SourceCardInstanceId)
                throw new ArgumentException("Periodic attacks require the played runtime card identity.");

            if (plan.ResidentBirdCount > 0)
            {
                RegisterBirdAttack(card, effect, plan.ResidentBirdCount);
                return;
            }
            int index = _active.FindIndex(s => !s.IsBird && s.SourceCardInstanceId == card.runtimeInstanceId);
            PeriodicAttackSource old = index >= 0 ? _active[index] : null;
            var source = new PeriodicAttackSource(old?.EffectRuntimeId ?? _nextRuntimeId++,
                card.contentId, FreezeAutomaticPlan(plan), old?.LastTriggeredTurn ?? -1);
            if (index >= 0) _active[index] = source;
            else _active.Add(source);
            Changed?.Invoke(new PeriodicAttackEvent(old == null
                ? PeriodicAttackChangeKind.Placed : PeriodicAttackChangeKind.Refreshed,
                source, _state.CurrentTurn));
        }

        private void RegisterBirdAttack(CardConfig card, CardEffectSpec effect, int count)
        {
            if (effect.damageSpec?.birdAttack == null ||
                count != effect.damageSpec.pattern?.baseAttackInstanceCount)
                throw new ArgumentException("Bird registration requires the resolved fixed bird shape.");
            if (_nextRuntimeId > int.MaxValue - count)
                throw new InvalidOperationException("Periodic attack lifecycle identities are exhausted.");
            var bird = new ResolvedBirdAttackSpec(effect.damageSpec.birdAttack);
            int castId = _nextRuntimeId;
            var added = new List<PeriodicAttackSource>();
            for (int i = 0; i < count; i++)
            {
                AttackPlan template = AttackPlanBuilder.BuildBirdEndTurn(bird,
                    new CardAttackSnapshot(card.runtimeInstanceId, 0), i, count,
                    new CombatStatSnapshot(null, 0));
                added.Add(new PeriodicAttackSource(castId + i, card.contentId, template, -1,
                    PeriodicAttackSourceKind.Bird, castId, i, count, bird));
            }
            // Commit the complete cast before publishing; playing the same physical card adds a new cast.
            _active.AddRange(added);
            _nextRuntimeId += count;
            foreach (PeriodicAttackSource source in added)
                Changed?.Invoke(new PeriodicAttackEvent(PeriodicAttackChangeKind.Placed,
                    source, _state.CurrentTurn));
        }

        /// <summary>Commit the turn before publishing callbacks; reentry, refresh and restore cannot double-fire it.</summary>
        internal void OnPlayerTurnEnded()
        {
            if (_triggering) return;
            if (_state.IsBattleOver || _state.IsDefeat) { Clear(); return; }
            _triggering = true;
            try
            {
                var due = new List<PeriodicAttackSource>(_active);
                var spatialProgress = new AttackTargetSelector.SpatialProgress();
                foreach (PeriodicAttackSource pending in due)
                {
                    if (_state.IsDefeat || _state.IsBattleOver) { Clear(); break; }
                    int index = _active.FindIndex(s => s.EffectRuntimeId == pending.EffectRuntimeId);
                    if (index < 0) continue;
                    PeriodicAttackSource current = _active[index];
                    if (current.LastTriggeredTurn >= _state.CurrentTurn) continue;
                    PeriodicAttackSource source = CopySource(current, current.Snapshot, _state.CurrentTurn);
                    _active[index] = source;
                    if (AttackResolver.GetAttackableTargets(_state.EnemyRoster).Count == 0) continue;

                    AttackPlan plan = source.IsBird
                        ? AttackPlanBuilder.BuildBirdEndTurn(source.BirdAttack,
                            new CardAttackSnapshot(source.SourceCardInstanceId, 0),
                            source.BirdIndex, source.BirdCount, new CombatStatSnapshot(_state.Player, 0))
                        : source.Snapshot;
                    var before = new List<CombatEffectTarget>();
                    for (int i = 0; i < _state.Enemies.Count; i++)
                        if (_state.Enemies[i] != null)
                            before.Add(new CombatEffectTarget(_state.Enemies[i], i));
                    AttackResolutionResult result = AttackResolver.Execute(plan,
                        _state.EnemyRoster, ResolutionMode.Actual,
                        _state.Random ?? DefaultRandomSource.Instance,
                        _state.EnemyAi?.GenerateIntents(),
                        spatialProgress: source.IsBird ? null : spatialProgress);
                    var ids = new HashSet<int>();
                    foreach (DamageEvent hit in result.Events) ids.Add(hit.TargetEnemyRuntimeId);
                    before.RemoveAll(t => !ids.Contains(t.RuntimeId));
                    PeriodicAttackSource presented = source.IsBird
                        ? CopySource(source, plan, _state.CurrentTurn) : source;
                    Changed?.Invoke(new PeriodicAttackEvent(PeriodicAttackChangeKind.Triggered,
                        presented, _state.CurrentTurn, result, before.AsReadOnly()));
                    AttackResolved?.Invoke(result);
                }
            }
            finally { _triggering = false; }
        }

        private static PeriodicAttackSource CopySource(PeriodicAttackSource source, AttackPlan plan, int turn)
            => new PeriodicAttackSource(source.EffectRuntimeId, source.SourceCardContentId, plan, turn,
                source.Kind, source.CastRuntimeId, source.BirdIndex, source.BirdCount, source.BirdAttack);

        internal void Clear()
        {
            var removed = new List<PeriodicAttackSource>(_active);
            _active.Clear();
            foreach (PeriodicAttackSource source in removed)
                Changed?.Invoke(new PeriodicAttackEvent(PeriodicAttackChangeKind.Removed,
                    source, _state.CurrentTurn));
        }

        /// <summary>Detached serializable state, including the consumed turn and the next lifecycle identity.</summary>
        public PeriodicAttackMemento CaptureMemento()
        {
            var result = new PeriodicAttackMemento { nextRuntimeId = _nextRuntimeId };
            foreach (PeriodicAttackSource source in _active)
                result.sources.Add(PeriodicAttackMemento.Source.Capture(source));
            return result;
        }

        /// <summary>
        /// Restore on the owning combat checkpoint after its turn/roster is restored. Atomic validation,
        /// no damage, registration or historical events. Commands/RNG replay needs no extra registration.
        /// </summary>
        public void RestoreMemento(PeriodicAttackMemento saved)
        {
            if (saved == null || (saved.version != 1 && saved.version != 2) ||
                saved.nextRuntimeId < 1 || saved.sources == null)
                throw new ArgumentException("Invalid periodic attack memento.", nameof(saved));
            if (_triggering) throw new InvalidOperationException("Cannot restore during periodic resolution.");
            var restored = new List<PeriodicAttackSource>();
            var ids = new HashSet<int>();
            var frozenCards = new HashSet<int>();
            var birdSlots = new HashSet<long>();
            var casts = new Dictionary<int, PeriodicAttackSource>();
            var castCounts = new Dictionary<int, int>();
            foreach (PeriodicAttackMemento.Source source in saved.sources)
            {
                if (source == null || source.runtimeId <= 0 || source.runtimeId >= saved.nextRuntimeId ||
                    source.cardInstanceId <= 0 || source.lastTurn < -1 ||
                    source.lastTurn > _state.CurrentTurn ||
                    !ids.Add(source.runtimeId))
                    throw new ArgumentException("Duplicate or invalid periodic source identity.", nameof(saved));
                PeriodicAttackSource live = source.Restore(saved.version);
                if (!live.IsBird)
                {
                    if (!frozenCards.Add(live.SourceCardInstanceId))
                        throw new ArgumentException("Duplicate frozen card source.", nameof(saved));
                }
                else
                {
                    if (live.CastRuntimeId <= 0 || live.BirdIndex < 0 || live.BirdIndex >= live.BirdCount ||
                        (long)live.CastRuntimeId + live.BirdIndex != live.EffectRuntimeId ||
                        !birdSlots.Add(((long)live.CastRuntimeId << 32) | (uint)live.BirdIndex))
                        throw new ArgumentException("Duplicate or invalid bird source identity.", nameof(saved));
                    if (casts.TryGetValue(live.CastRuntimeId, out PeriodicAttackSource first))
                    {
                        if (first.SourceCardInstanceId != live.SourceCardInstanceId ||
                            first.SourceCardContentId != live.SourceCardContentId ||
                            first.BirdCount != live.BirdCount ||
                            first.BirdAttack.MissilesPerBird != live.BirdAttack.MissilesPerBird ||
                            first.BirdAttack.EndTurnMissilesPerBird != live.BirdAttack.EndTurnMissilesPerBird ||
                            first.BirdAttack.EndTurnDamagePerHit != live.BirdAttack.EndTurnDamagePerHit ||
                            first.BirdAttack.EndTurnAreaCoefficient != live.BirdAttack.EndTurnAreaCoefficient)
                            throw new ArgumentException("Inconsistent bird cast configuration.", nameof(saved));
                        castCounts[live.CastRuntimeId]++;
                    }
                    else
                    {
                        casts.Add(live.CastRuntimeId, live);
                        castCounts.Add(live.CastRuntimeId, 1);
                    }
                }
                restored.Add(live);
            }
            // Birds currently enter as a complete cast and leave only through encounter-wide Clear.
            // Unique bounded indices plus N sources guarantee all indices 0..N-1 are present.
            foreach (KeyValuePair<int, PeriodicAttackSource> cast in casts)
                if (castCounts[cast.Key] != cast.Value.BirdCount)
                    throw new ArgumentException("Incomplete saved bird cast.", nameof(saved));
            _active.Clear();
            if (!_state.IsBattleOver && !_state.IsDefeat) _active.AddRange(restored);
            _nextRuntimeId = saved.nextRuntimeId;
        }

        private static AttackPlan FreezeAutomaticPlan(AttackPlan resolved)
        {
            var plan = new AttackPlan(resolved.SourceCardInstanceId, resolved.AreaStat,
                resolved.DisplayedCardDamage, canTriggerSecondaryEffects: false,
                residentBirdCount: resolved.ResidentBirdCount);
            foreach (AttackSubHit hit in resolved.SubHits) plan.AddSubHit(hit);
            return plan;
        }
    }

    /// <summary>Serialization DTO only; never shares mutable data with a live periodic owner.</summary>
    [Serializable]
    public sealed class PeriodicAttackMemento
    {
        public int version = 2;
        public int nextRuntimeId = 1;
        public List<Source> sources = new List<Source>();

        [Serializable]
        public sealed class Source
        {
            public int runtimeId, cardInstanceId, lastTurn, areaStat, displayedDamage;
            public string cardContentId;
            public PeriodicAttackSourceKind kind;
            public int castRuntimeId, birdIndex, birdCount;
            public BirdAttackSpec birdAttack;
            public List<Hit> hits = new List<Hit>();

            internal static Source Capture(PeriodicAttackSource source)
            {
                var saved = new Source { runtimeId = source.EffectRuntimeId,
                    cardInstanceId = source.SourceCardInstanceId, cardContentId = source.SourceCardContentId,
                    lastTurn = source.LastTriggeredTurn, areaStat = source.Snapshot.AreaStat,
                    displayedDamage = source.Snapshot.DisplayedCardDamage,
                    kind = source.Kind, castRuntimeId = source.CastRuntimeId,
                    birdIndex = source.BirdIndex, birdCount = source.BirdCount,
                    birdAttack = source.IsBird ? new BirdAttackSpec {
                        missilesPerBird = source.BirdAttack.MissilesPerBird,
                        endTurnMissilesPerBird = source.BirdAttack.EndTurnMissilesPerBird,
                        endTurnDamagePerHit = source.BirdAttack.EndTurnDamagePerHit,
                        endTurnAreaCoefficient = source.BirdAttack.EndTurnAreaCoefficient } : null };
                foreach (AttackSubHit hit in source.Snapshot.SubHits) saved.hits.Add(Hit.Capture(hit));
                return saved;
            }

            internal PeriodicAttackSource Restore(int version)
            {
                if (hits == null || hits.Count == 0 || hits.Count > AttackPlanBuilder.MaximumSubHitsPerPlan)
                    throw new ArgumentException("Invalid periodic attack hit count.");
                PeriodicAttackSourceKind restoredKind = version == 1 ? PeriodicAttackSourceKind.FrozenPlan : kind;
                if (!Enum.IsDefined(typeof(PeriodicAttackSourceKind), restoredKind))
                    throw new ArgumentException("Invalid periodic source kind.");
                bool isBird = restoredKind == PeriodicAttackSourceKind.Bird;
                if (isBird && (birdAttack == null || birdCount <= 0 ||
                    (long)birdCount * birdAttack.missilesPerBird > AttackPlanBuilder.MaximumSubHitsPerPlan ||
                    (long)birdCount * birdAttack.endTurnMissilesPerBird > AttackPlanBuilder.MaximumSubHitsPerPlan))
                    throw new ArgumentException("Invalid saved bird attack configuration.");
                ResolvedBirdAttackSpec bird = isBird ? new ResolvedBirdAttackSpec(birdAttack) : null;
                var plan = new AttackPlan(cardInstanceId, areaStat, displayedDamage, false,
                    residentBirdCount: isBird ? birdCount : 0);
                foreach (Hit hit in hits)
                {
                    if (hit == null || hit.damage < 0 || hit.targetCount < 0 ||
                        !Enum.IsDefined(typeof(TargetPolicy), hit.policy) || hit.policy == TargetPolicy.PlayerSelected)
                        throw new ArgumentException("Invalid periodic attack hit.");
                    plan.AddSubHit(new AttackSubHit(hit.attackIndex, hit.subHitIndex, hit.damage,
                        hit.policy, hit.targetCount, hit.hasCrit ? hit.crit : null,
                        hit.hasArea ? hit.area : null, hit.hasBounce ? hit.bounce : null,
                        hit.hasTerrain ? hit.terrain : null, hit.onHit));
                }
                if (isBird)
                {
                    if (areaStat != 0 || hits.Count != bird.EndTurnMissilesPerBird ||
                        displayedDamage != (long)bird.EndTurnMissilesPerBird * bird.EndTurnDamagePerHit)
                        throw new ArgumentException("Invalid bird periodic template.");
                    for (int i = 0; i < plan.SubHits.Count; i++)
                    {
                        AttackSubHit hit = plan.SubHits[i];
                        if (hit.AttackInstanceIndex != birdIndex || hit.SubHitIndex != i ||
                            hit.NormalHitDamage != bird.EndTurnDamagePerHit ||
                            hit.TargetPolicy != TargetPolicy.LeftToRight || hit.DirectTargetCount != 1 ||
                            hit.Crit?.enabled == true || hit.Bounce != null || hit.TerrainEffect != null ||
                            hit.OnHitEffects.Count > 0 || hit.Area == null ||
                            hit.Area.enabled != bird.Area.enabled || hit.Area.coefficient != bird.Area.coefficient ||
                            hit.Area.triggerMode != bird.Area.triggerMode)
                            throw new ArgumentException("Invalid bird periodic hit.");
                    }
                }
                return new PeriodicAttackSource(runtimeId, cardContentId, plan, lastTurn, restoredKind,
                    isBird ? castRuntimeId : 0, isBird ? birdIndex : 0, isBird ? birdCount : 0, bird);
            }
        }

        [Serializable]
        public sealed class Hit
        {
            public int attackIndex, subHitIndex, damage, targetCount;
            public TargetPolicy policy;
            // JsonUtility can materialize omitted nested DTOs; presence is explicit.
            public bool hasCrit, hasArea, hasBounce, hasTerrain;
            public CritSpec crit;
            public AreaSpec area;
            public BounceSpec bounce;
            public TerrainEffectSpec terrain;
            public List<OnHitEffectSpec> onHit;

            internal static Hit Capture(AttackSubHit hit)
            {
                var saved = new Hit { attackIndex = hit.AttackInstanceIndex, subHitIndex = hit.SubHitIndex,
                    damage = hit.NormalHitDamage, targetCount = hit.DirectTargetCount, policy = hit.TargetPolicy,
                    hasCrit = hit.Crit != null, hasArea = hit.Area != null,
                    hasBounce = hit.Bounce != null, hasTerrain = hit.TerrainEffect != null,
                    crit = hit.Crit == null ? null : new CritSpec { enabled = hit.Crit.enabled,
                        chance = hit.Crit.chance, multiplier = hit.Crit.multiplier, areaDamageBasis = hit.Crit.areaDamageBasis },
                    area = hit.Area == null ? null : new AreaSpec { enabled = hit.Area.enabled,
                        coefficient = hit.Area.coefficient, triggerMode = hit.Area.triggerMode },
                    bounce = hit.Bounce == null ? null : new BounceSpec { enabled = hit.Bounce.enabled,
                        sequenceMode = hit.Bounce.sequenceMode, hitsPerDirectHit = hit.Bounce.hitsPerDirectHit,
                        damageIncrementPerBounce = hit.Bounce.damageIncrementPerBounce },
                    terrain = hit.TerrainEffect == null ? null : new TerrainEffectSpec {
                        effectId = hit.TerrainEffect.effectId, effectKind = hit.TerrainEffect.effectKind,
                        triggerTiming = hit.TerrainEffect.triggerTiming, damageKind = hit.TerrainEffect.damageKind,
                        magnitudeScale = hit.TerrainEffect.magnitudeScale, triggerCount = hit.TerrainEffect.triggerCount },
                    onHit = new List<OnHitEffectSpec>() };
                foreach (ResolvedOnHitEffectSpec effect in hit.OnHitEffects)
                    saved.onHit.Add(new OnHitEffectSpec { effectType = effect.effectType,
                        baseChance = effect.baseChance, useRuleDefaultChance = effect.useRuleDefaultChance });
                return saved;
            }
        }
    }
}
