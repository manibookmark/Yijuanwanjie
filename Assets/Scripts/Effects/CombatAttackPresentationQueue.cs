using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns each attack source from synchronous Core resolution through the
    /// last visible arrival or cancellation. Hit identity, captured anchors,
    /// number ordinals and terrain attachments have one writer here; adapters
    /// consume immutable facts and never coordinate these mutable latches.
    /// Source IDs remain unique even when the same runtime card is replayed.
    /// </summary>
    internal sealed class CombatAttackPresentationQueue
    {
        internal readonly struct PendingHit
        {
            internal PendingHit(DamageEvent damage, Vector3 anchor, bool hasAnchor)
            {
                damageEvent = damage;
                worldAnchor = anchor;
                hasWorldAnchor = hasAnchor;
            }

            internal readonly DamageEvent damageEvent;
            internal readonly Vector3 worldAnchor;
            internal readonly bool hasWorldAnchor;
        }

        internal readonly struct PendingTerrainPlacement
        {
            internal PendingTerrainPlacement(TerrainEffectChange placement, Vector3 anchor)
            {
                change = placement;
                worldAnchor = anchor;
            }

            internal readonly TerrainEffectChange change;
            internal readonly Vector3 worldAnchor;
        }

        internal sealed class HitSequence
        {
            internal HitSequence(List<PendingHit> hits, int startingOrdinal)
            {
                Hits = hits.AsReadOnly();
                StartingOrdinal = startingOrdinal;
            }

            internal IReadOnlyList<PendingHit> Hits { get; }
            internal int StartingOrdinal { get; }
        }

        /// <summary>
        /// Immutable source identity with read-only views of its current state.
        /// A released token becomes inert, including callbacks arriving late.
        /// </summary>
        internal sealed class Context
        {
            private readonly CombatAttackPresentationQueue _owner;

            internal Context(CombatAttackPresentationQueue owner, long sourceId,
                int cardInstanceId, VfxProfile profile)
            {
                _owner = owner;
                SourcePresentationId = sourceId;
                CardInstanceId = cardInstanceId;
                Profile = profile;
            }

            internal long SourcePresentationId { get; }
            internal int CardInstanceId { get; }
            internal VfxProfile Profile { get; }
            internal AttackEffectCompletion EffectsCompletion { get; } = new AttackEffectCompletion();
            internal bool AwaitingProjectile => _owner.IsAwaitingFlight(this);
            internal bool CoreResolved => _owner.IsCoreResolved(this);
            internal bool HasCriticalDirectHit => _owner.HasCriticalDirectHit(this);
            internal IReadOnlyList<PendingHit> Hits => _owner.GetHits(this);
            internal IReadOnlyList<ProjectileTrace> ProjectileTraces => _owner.GetProjectileTraces(this);
            internal AttackPlan AttackPlan => _owner.GetAttackPlan(this);
            internal Vector3? CastOrigin => _owner.GetCastOrigin(this);
        }

        private sealed class State
        {
            internal bool AwaitingFlight = true;
            internal bool CoreResolved;
            internal bool HasCriticalDirectHit;
            internal readonly List<PendingHit> Hits = new List<PendingHit>();
            internal readonly ReadOnlyCollection<PendingHit> ReadOnlyHits;
            internal readonly HashSet<int> PresentedHitIndices = new HashSet<int>();
            internal readonly Dictionary<int, int> DamageNumberOrdinals = new Dictionary<int, int>();
            internal readonly Dictionary<int, Vector3> TargetAnchors = new Dictionary<int, Vector3>();
            internal readonly List<PendingTerrainPlacement> TerrainPlacements =
                new List<PendingTerrainPlacement>();
            internal IReadOnlyList<ProjectileTrace> ProjectileTraces = System.Array.Empty<ProjectileTrace>();
            internal AttackPlan AttackPlan;
            internal Vector3? CastOrigin;

            internal State()
            {
                ReadOnlyHits = Hits.AsReadOnly();
            }
        }

        private static readonly IReadOnlyList<PendingHit> EmptyHits =
            new List<PendingHit>().AsReadOnly();
        private readonly List<Context> _active = new List<Context>();
        private readonly ReadOnlyCollection<Context> _readOnlyActive;
        private readonly Dictionary<Context, State> _states = new Dictionary<Context, State>();
        private Context _coreResolving;
        private long _nextSourcePresentationId;

        internal CombatAttackPresentationQueue()
        {
            _readOnlyActive = _active.AsReadOnly();
        }

        internal IReadOnlyList<Context> Active => _readOnlyActive;
        internal int ActiveCount => _active.Count;
        internal Context CoreResolving => _coreResolving;

        internal Context Begin(int cardInstanceId, VfxProfile profile, bool coreResolved = false)
        {
            var context = new Context(this, ++_nextSourcePresentationId, cardInstanceId, profile);
            _active.Add(context);
            _states.Add(context, new State { CoreResolved = coreResolved });
            if (!coreResolved)
                _coreResolving = context;
            return context;
        }

        internal Context Find(int cardInstanceId)
        {
            if (cardInstanceId == 0)
                return _coreResolving;
            for (int i = _active.Count - 1; i >= 0; i--)
                if (_active[i].CardInstanceId == cardInstanceId)
                    return _active[i];
            return null;
        }

        internal void MarkCoreResolved(Context context)
        {
            if (!TryGetState(context, out State state)) return;
            state.CoreResolved = true;
            if (_coreResolving == context)
                _coreResolving = null;
        }

        internal void RecordProjectileTraces(Context context, IReadOnlyList<ProjectileTrace> traces)
        {
            if (TryGetState(context, out State state))
                state.ProjectileTraces = traces ?? System.Array.Empty<ProjectileTrace>();
        }

        // A complete selected Core plan is immutable after publication. Never
        // rebuild counts from partial hit receipts or current post-play buffs.
        internal void RecordAttackPlan(Context context, AttackPlan plan)
        {
            if (TryGetState(context, out State state) && plan != null &&
                (context.CardInstanceId == 0 || context.CardInstanceId == plan.SourceCardInstanceId))
                state.AttackPlan = plan;
        }

        internal void RecordCastOrigin(Context context, Vector3? origin)
        {
            if (TryGetState(context, out State state)) state.CastOrigin = origin;
        }

        private Vector3? GetCastOrigin(Context context)
            => TryGetState(context, out State state) ? state.CastOrigin : null;

        private IReadOnlyList<ProjectileTrace> GetProjectileTraces(Context context)
            => TryGetState(context, out State state) ? state.ProjectileTraces : System.Array.Empty<ProjectileTrace>();

        private AttackPlan GetAttackPlan(Context context)
            => TryGetState(context, out State state) ? state.AttackPlan : null;

        internal Context FindFatalSource(int enemyRuntimeId)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                foreach (PendingHit hit in _states[_active[i]].Hits)
                    if (hit.damageEvent.IsFatal &&
                        hit.damageEvent.TargetEnemyRuntimeId == enemyRuntimeId)
                        return _active[i];
            return null;
        }

        internal void MarkFlightFinished(Context context)
        {
            if (TryGetState(context, out State state))
                state.AwaitingFlight = false;
        }

        internal int EnqueueHit(Context context, DamageEvent damage, Vector3 anchor, bool hasAnchor)
        {
            if (!TryGetState(context, out State state) || !state.AwaitingFlight)
                return -1;
            int index = state.Hits.Count;
            state.Hits.Add(new PendingHit(damage, anchor, hasAnchor));
            if (damage.Kind == DamageKind.Direct && damage.IsCritical)
                state.HasCriticalDirectHit = true;
            return index;
        }

        internal void RecordTarget(Context context, int targetIndex, Vector3 anchor)
        {
            if (TryGetState(context, out State state) && !state.TargetAnchors.ContainsKey(targetIndex))
                state.TargetAnchors.Add(targetIndex, anchor);
        }

        internal bool TryGetTarget(Context context, int targetIndex, out Vector3 anchor)
        {
            anchor = default;
            return TryGetState(context, out State state) &&
                state.TargetAnchors.TryGetValue(targetIndex, out anchor);
        }

        internal bool TryFindNextDirectHit(Context context, int targetIndex,
            out int pendingHitIndex, out DamageEvent damage)
        {
            pendingHitIndex = -1;
            damage = default;
            if (!TryGetState(context, out State state) || !state.AwaitingFlight) return false;
            for (int i = 0; i < state.Hits.Count; i++)
            {
                DamageEvent candidate = state.Hits[i].damageEvent;
                if (state.PresentedHitIndices.Contains(i) || candidate.Kind != DamageKind.Direct ||
                    candidate.TargetIndex != targetIndex)
                    continue;
                pendingHitIndex = i;
                damage = candidate;
                return true;
            }
            return false;
        }

        internal bool TryPresentHit(Context context, int pendingHitIndex,
            out PendingHit hit, out int ordinal)
        {
            hit = default;
            ordinal = 0;
            if (!TryGetState(context, out State state) || !state.AwaitingFlight ||
                pendingHitIndex < 0 || pendingHitIndex >= state.Hits.Count ||
                !state.PresentedHitIndices.Add(pendingHitIndex))
                return false;
            hit = state.Hits[pendingHitIndex];
            ordinal = NextOrdinal(state, hit.damageEvent);
            return true;
        }

        /// <summary>
        /// Consumes unmatched hits exactly once and preserves each target's
        /// original event order and the ordinal already assigned at arrivals.
        /// </summary>
        internal IReadOnlyList<HitSequence> DrainRemainingHits(Context context)
        {
            var result = new List<HitSequence>();
            if (!TryGetState(context, out State state)) return result.AsReadOnly();
            var groupedHits = new List<List<PendingHit>>();
            var groupIndices = new Dictionary<int, int>();
            var startingOrdinals = new List<int>();
            for (int i = 0; i < state.Hits.Count; i++)
            {
                if (!state.PresentedHitIndices.Add(i)) continue;
                PendingHit hit = state.Hits[i];
                int targetKey = TargetKey(hit.damageEvent);
                int ordinal = NextOrdinal(state, hit.damageEvent);
                if (!groupIndices.TryGetValue(targetKey, out int groupIndex))
                {
                    groupIndex = groupedHits.Count;
                    groupIndices.Add(targetKey, groupIndex);
                    groupedHits.Add(new List<PendingHit>());
                    startingOrdinals.Add(ordinal);
                }
                groupedHits[groupIndex].Add(hit);
            }
            for (int i = 0; i < groupedHits.Count; i++)
                result.Add(new HitSequence(groupedHits[i], startingOrdinals[i]));
            return result.AsReadOnly();
        }

        internal void AttachTerrain(Context context, TerrainEffectChange change, Vector3 anchor)
        {
            if (TryGetState(context, out State state))
                state.TerrainPlacements.Add(new PendingTerrainPlacement(change, anchor));
        }

        /// <summary>
        /// Reads the captured ground position without consuming the placement;
        /// the arrival callback remains the sole owner of placement admission.
        /// </summary>
        internal bool TryGetTerrainAnchor(Context context, int attackInstanceIndex,
            int targetIndex, out Vector3 anchor)
        {
            anchor = default;
            if (targetIndex < 0 || !TryGetState(context, out State state)) return false;
            for (int i = 0; i < state.TerrainPlacements.Count; i++)
            {
                PendingTerrainPlacement placement = state.TerrainPlacements[i];
                if (placement.change.TargetIndex != targetIndex ||
                    (attackInstanceIndex >= 0 &&
                     placement.change.AttackInstanceIndex != attackInstanceIndex))
                    continue;
                anchor = placement.worldAnchor;
                return true;
            }
            return false;
        }

        internal bool TryTakeTerrain(Context context, int targetIndex, out PendingTerrainPlacement placement)
        {
            placement = default;
            if (!TryGetState(context, out State state)) return false;
            for (int i = 0; i < state.TerrainPlacements.Count; i++)
            {
                if (state.TerrainPlacements[i].change.TargetIndex != targetIndex) continue;
                placement = state.TerrainPlacements[i];
                state.TerrainPlacements.RemoveAt(i);
                return true;
            }
            return false;
        }

        internal IReadOnlyList<PendingTerrainPlacement> DrainTerrain(Context context)
        {
            if (!TryGetState(context, out State state))
                return new List<PendingTerrainPlacement>().AsReadOnly();
            var result = state.TerrainPlacements.ToArray();
            state.TerrainPlacements.Clear();
            return System.Array.AsReadOnly(result);
        }

        internal bool Release(Context context)
        {
            if (!TryGetState(context, out State state)) return false;
            if (state.AwaitingFlight) context.EffectsCompletion.Cancel();
            else context.EffectsCompletion.CloseSource();
            _states.Remove(context);
            _active.Remove(context);
            if (_coreResolving == context)
                _coreResolving = null;
            return true;
        }

        internal void Clear()
        {
            foreach (Context context in _active) context.EffectsCompletion.Cancel();
            _active.Clear();
            _states.Clear();
            _coreResolving = null;
        }

        private bool TryGetState(Context context, out State state)
        {
            state = null;
            return context != null && _states.TryGetValue(context, out state);
        }

        private bool IsAwaitingFlight(Context context) =>
            TryGetState(context, out State state) && state.AwaitingFlight;

        private bool IsCoreResolved(Context context) =>
            TryGetState(context, out State state) && state.CoreResolved;

        private bool HasCriticalDirectHit(Context context) =>
            TryGetState(context, out State state) && state.HasCriticalDirectHit;

        private IReadOnlyList<PendingHit> GetHits(Context context) =>
            TryGetState(context, out State state) ? state.ReadOnlyHits : EmptyHits;

        private static int TargetKey(DamageEvent damage) => damage.TargetEnemyRuntimeId > 0
            ? damage.TargetEnemyRuntimeId : -(damage.TargetIndex + 1);

        private static int NextOrdinal(State state, DamageEvent damage)
        {
            int key = TargetKey(damage);
            state.DamageNumberOrdinals.TryGetValue(key, out int ordinal);
            state.DamageNumberOrdinals[key] = ordinal + 1;
            return ordinal;
        }
    }
}
