using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WanjieScroll.Core;

namespace WanjieScroll.UI.Views
{
    /// <summary>
    /// One ready-to-present atomic formation transition. The scheduler keeps
    /// dependency state private; the presenter only starts the supplied moves
    /// and reports the opaque transition id when their visual motion ends.
    /// </summary>
    internal readonly struct EnemyFormationTransition
    {
        internal EnemyFormationTransition(
            long transitionId,
            long advanceId,
            IReadOnlyList<FormationAdvanceMove> moves)
        {
            TransitionId = transitionId;
            AdvanceId = advanceId;
            Moves = moves ?? throw new ArgumentNullException(nameof(moves));
        }

        internal long TransitionId { get; }
        internal long AdvanceId { get; }
        internal IReadOnlyList<FormationAdvanceMove> Moves { get; }
    }

    /// <summary>
    /// Owns every dependency that decides when authoritative formation moves
    /// may become visible. A move chain waits only for death footprints that
    /// intersect its destinations; unrelated columns remain independent.
    /// Later events also wait for earlier actor or spatial conflicts, so rapid
    /// resolutions cannot overtake motion that is still on screen.
    /// An attack-owned readiness query is another dependency, not a global
    /// formation pause. This data-only owner stores no effect or Unity object.
    /// </summary>
    internal sealed class EnemyFormationTransitionScheduler
    {
        private enum TransitionStatus
        {
            Waiting,
            Running
        }

        private sealed class TransitionState
        {
            internal long TransitionId;
            internal long AdvanceId;
            internal ReadOnlyCollection<FormationAdvanceMove> Moves;
            internal readonly HashSet<int> RequiredDeathRuntimeIds =
                new HashSet<int>();
            internal readonly HashSet<long> PredecessorTransitionIds =
                new HashSet<long>();
            internal TransitionStatus Status;
            internal Func<bool> AttackEffectCompleted;
        }

        private readonly Dictionary<int, FormationFootprint> _deathHolds =
            new Dictionary<int, FormationFootprint>();
        private readonly HashSet<int> _vacatedDeathRuntimeIds =
            new HashSet<int>();
        private readonly List<TransitionState> _transitions =
            new List<TransitionState>();
        private readonly Dictionary<int, long> _lastTransitionByRuntimeId =
            new Dictionary<int, long>();
        private readonly HashSet<long> _seenAdvanceIds =
            new HashSet<long>();

        private long _nextTransitionId = 1;
        private long _latestAdvanceId;

        internal bool IsSettled =>
            _deathHolds.Count == 0 && _transitions.Count == 0;
        internal int DeathHoldCount => _deathHolds.Count;
        internal int TransitionCount => _transitions.Count;

        internal bool IsDeathHeld(int enemyRuntimeId)
            => enemyRuntimeId > 0 &&
               _deathHolds.ContainsKey(enemyRuntimeId);

        /// <summary>
        /// Registers the exact footprint occupied by a defeated subject. A
        /// vacancy notification is allowed to arrive first and is latched for
        /// the combat lifetime, making every event ordering exactly-once safe.
        /// Returns true only while the subject still blocks presentation.
        /// </summary>
        internal bool RegisterDeath(
            int enemyRuntimeId,
            FormationFootprint footprint)
        {
            if (enemyRuntimeId <= 0)
                return false;
            if (_vacatedDeathRuntimeIds.Contains(enemyRuntimeId))
                return false;
            if (_deathHolds.ContainsKey(enemyRuntimeId))
                return true;

            _deathHolds.Add(enemyRuntimeId, footprint);
            for (int i = 0; i < _transitions.Count; i++)
            {
                TransitionState transition = _transitions[i];
                if (transition.Status == TransitionStatus.Waiting &&
                    TargetsIntersect(transition.Moves, footprint))
                {
                    transition.RequiredDeathRuntimeIds.Add(enemyRuntimeId);
                }
            }
            return true;
        }

        /// <summary>
        /// Releases one death hold and only that hold. Duplicate or early
        /// notifications are idempotent and remain latched until Reset.
        /// </summary>
        internal bool ReleaseDeath(int enemyRuntimeId)
        {
            if (enemyRuntimeId <= 0)
                return false;

            bool firstVacancy = _vacatedDeathRuntimeIds.Add(enemyRuntimeId);
            bool removed = _deathHolds.Remove(enemyRuntimeId);
            if (removed)
            {
                for (int i = 0; i < _transitions.Count; i++)
                {
                    _transitions[i].RequiredDeathRuntimeIds.Remove(
                        enemyRuntimeId);
                }
            }
            return firstVacancy;
        }

        /// <summary>
        /// Converts one immutable Core advance into independent atomic move
        /// chains. Wave deployment remains one group and waits for the whole
        /// previous death boundary; ordinary advances use geometric blockers.
        /// </summary>
        internal bool Enqueue(FormationAdvanceEvent advance)
        {
            if (advance == null)
                throw new ArgumentNullException(nameof(advance));
            if (_seenAdvanceIds.Contains(advance.AdvanceId))
                return false;
            if (advance.AdvanceId <= _latestAdvanceId)
            {
                throw new InvalidOperationException(
                    "Formation advances must be enqueued in monotonic order.");
            }

            _seenAdvanceIds.Add(advance.AdvanceId);
            _latestAdvanceId = advance.AdvanceId;
            List<List<FormationAdvanceMove>> components =
                BuildAtomicComponents(advance);
            return EnqueueComponents(components, advance.AdvanceId, advance.Cause);
        }

        // Individual knockbacks have no backfill advance id. Their frozen
        // snapshots share actor/spatial dependencies and their own source gate.
        internal void EnqueueKnockback(IReadOnlyList<FormationAdvanceMove> moves,
            Func<bool> attackEffectCompleted = null)
        {
            if (moves == null || moves.Count == 0) return;
            EnqueueComponents(new List<List<FormationAdvanceMove>>
                { new List<FormationAdvanceMove>(moves) }, 0,
                FormationAdvanceCause.KnockbackBoundaryReturn, attackEffectCompleted);
        }

        internal bool TryGetScheduledDestination(int runtimeId, out FormationFootprint footprint)
        {
            for (int i = _transitions.Count - 1; i >= 0; i--)
                foreach (FormationAdvanceMove move in _transitions[i].Moves)
                    if (move.RuntimeId == runtimeId)
                    {
                        footprint = move.ToFootprint;
                        return true;
                    }
            footprint = default;
            return false;
        }

        // A subject killed in transit occupies the swept cells until its
        // corpse vacates, not just the last reached cell or future endpoint.
        internal FormationFootprint GetDeathFootprint(int runtimeId, FormationFootprint reached)
        {
            foreach (TransitionState transition in _transitions)
            {
                if (transition.Status != TransitionStatus.Running) continue;
                foreach (FormationAdvanceMove move in transition.Moves)
                {
                    if (move.RuntimeId != runtimeId) continue;
                    int row = Math.Min(reached.Row, move.ToFootprint.Row);
                    int column = Math.Min(reached.Column, move.ToFootprint.Column);
                    return new FormationFootprint(row, column,
                        Math.Max(reached.EndColumnExclusive, move.ToFootprint.EndColumnExclusive) - column,
                        Math.Max(reached.EndRowExclusive, move.ToFootprint.EndRowExclusive) - row);
                }
            }
            return reached;
        }

        private bool EnqueueComponents(List<List<FormationAdvanceMove>> components,
            long advanceId, FormationAdvanceCause cause, Func<bool> attackEffectCompleted = null)
        {
            int previousTransitionCount = _transitions.Count;
            for (int i = 0; i < components.Count; i++)
            {
                List<FormationAdvanceMove> component = components[i];
                if (component.Count == 0)
                    continue;

                var moveSnapshot = new FormationAdvanceMove[component.Count];
                component.CopyTo(moveSnapshot);
                var state = new TransitionState
                {
                    TransitionId = _nextTransitionId++,
                    AdvanceId = advanceId,
                    Moves = new ReadOnlyCollection<FormationAdvanceMove>(
                        moveSnapshot),
                    Status = TransitionStatus.Waiting,
                    AttackEffectCompleted = attackEffectCompleted
                };

                AddDeathDependencies(state, cause);
                AddEarlierTransitionDependencies(
                    state,
                    previousTransitionCount);
                _transitions.Add(state);

                for (int moveIndex = 0;
                     moveIndex < state.Moves.Count;
                     moveIndex++)
                {
                    int runtimeId = state.Moves[moveIndex].RuntimeId;
                    if (runtimeId > 0)
                    {
                        _lastTransitionByRuntimeId[runtimeId] =
                            state.TransitionId;
                    }
                }
            }
            return components.Count > 0;
        }

        /// <summary>
        /// Atomically claims every currently runnable transition. Claimed
        /// transitions will not be returned again until Complete is called.
        /// </summary>
        internal IReadOnlyList<EnemyFormationTransition> TakeReady()
        {
            var ready = new List<EnemyFormationTransition>();
            for (int i = 0; i < _transitions.Count; i++)
            {
                TransitionState state = _transitions[i];
                if (state.Status != TransitionStatus.Waiting ||
                    state.RequiredDeathRuntimeIds.Count > 0 ||
                    state.PredecessorTransitionIds.Count > 0 ||
                    (state.AttackEffectCompleted != null && !state.AttackEffectCompleted()))
                {
                    continue;
                }

                state.Status = TransitionStatus.Running;
                ready.Add(new EnemyFormationTransition(
                    state.TransitionId,
                    state.AdvanceId,
                    state.Moves));
            }
            return ready;
        }

        /// <summary>
        /// Completes one claimed transition and releases only dependants that
        /// named it. Calling Complete more than once is harmless.
        /// </summary>
        internal bool Complete(long transitionId)
        {
            int completedIndex = -1;
            TransitionState completed = null;
            for (int i = 0; i < _transitions.Count; i++)
            {
                if (_transitions[i].TransitionId != transitionId)
                    continue;
                completed = _transitions[i];
                completedIndex = i;
                break;
            }
            if (completed == null ||
                completed.Status != TransitionStatus.Running)
            {
                return false;
            }

            _transitions.RemoveAt(completedIndex);
            for (int i = 0; i < _transitions.Count; i++)
            {
                _transitions[i].PredecessorTransitionIds.Remove(
                    transitionId);
            }

            for (int i = 0; i < completed.Moves.Count; i++)
            {
                int runtimeId = completed.Moves[i].RuntimeId;
                if (runtimeId > 0 &&
                    _lastTransitionByRuntimeId.TryGetValue(
                        runtimeId,
                        out long lastTransitionId) &&
                    lastTransitionId == transitionId)
                {
                    _lastTransitionByRuntimeId.Remove(runtimeId);
                }
            }
            return true;
        }

        internal void Reset()
        {
            _deathHolds.Clear();
            _vacatedDeathRuntimeIds.Clear();
            _transitions.Clear();
            _lastTransitionByRuntimeId.Clear();
            _seenAdvanceIds.Clear();
            _nextTransitionId = 1;
            _latestAdvanceId = 0;
        }

        private void AddDeathDependencies(
            TransitionState state,
            FormationAdvanceCause cause)
        {
            foreach (KeyValuePair<int, FormationFootprint> pair in
                     _deathHolds)
            {
                if (cause == FormationAdvanceCause.WaveDeploy ||
                    TargetsIntersect(state.Moves, pair.Value))
                {
                    state.RequiredDeathRuntimeIds.Add(pair.Key);
                }
            }
        }

        private void AddEarlierTransitionDependencies(
            TransitionState state,
            int earlierTransitionCount)
        {
            for (int moveIndex = 0;
                 moveIndex < state.Moves.Count;
                 moveIndex++)
            {
                int runtimeId = state.Moves[moveIndex].RuntimeId;
                if (runtimeId > 0 &&
                    _lastTransitionByRuntimeId.TryGetValue(
                        runtimeId,
                        out long predecessorId))
                {
                    state.PredecessorTransitionIds.Add(predecessorId);
                }
            }

            for (int i = 0; i < earlierTransitionCount; i++)
            {
                TransitionState earlier = _transitions[i];
                if (TransitionsConflict(state.Moves, earlier.Moves))
                {
                    state.PredecessorTransitionIds.Add(
                        earlier.TransitionId);
                }
            }
        }

        private static List<List<FormationAdvanceMove>>
            BuildAtomicComponents(FormationAdvanceEvent advance)
        {
            int count = advance.Moves.Count;
            var parents = new int[count];
            for (int i = 0; i < count; i++)
                parents[i] = i;

            if (advance.Cause == FormationAdvanceCause.WaveDeploy)
            {
                for (int i = 1; i < count; i++)
                    Union(parents, 0, i);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    for (int j = i + 1; j < count; j++)
                    {
                        if (MovesBelongToSameChain(
                                advance.Moves[i],
                                advance.Moves[j]))
                        {
                            Union(parents, i, j);
                        }
                    }
                }
            }

            var rootToComponent = new Dictionary<int, int>();
            var components = new List<List<FormationAdvanceMove>>();
            for (int i = 0; i < count; i++)
            {
                int root = Find(parents, i);
                if (!rootToComponent.TryGetValue(root, out int componentIndex))
                {
                    componentIndex = components.Count;
                    rootToComponent.Add(root, componentIndex);
                    components.Add(new List<FormationAdvanceMove>());
                }
                components[componentIndex].Add(advance.Moves[i]);
            }
            return components;
        }

        private static bool MovesBelongToSameChain(
            FormationAdvanceMove first,
            FormationAdvanceMove second)
            => first.ToFootprint.Intersects(second.FromFootprint) ||
               second.ToFootprint.Intersects(first.FromFootprint) ||
               first.ToFootprint.Intersects(second.ToFootprint);

        private static bool TransitionsConflict(
            IReadOnlyList<FormationAdvanceMove> later,
            IReadOnlyList<FormationAdvanceMove> earlier)
        {
            for (int i = 0; i < later.Count; i++)
            {
                for (int j = 0; j < earlier.Count; j++)
                {
                    FormationAdvanceMove next = later[i];
                    FormationAdvanceMove previous = earlier[j];
                    if (next.FromFootprint.Intersects(
                            previous.FromFootprint) ||
                        next.FromFootprint.Intersects(
                            previous.ToFootprint) ||
                        next.ToFootprint.Intersects(
                            previous.FromFootprint) ||
                        next.ToFootprint.Intersects(
                            previous.ToFootprint))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool TargetsIntersect(
            IReadOnlyList<FormationAdvanceMove> moves,
            FormationFootprint footprint)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].ToFootprint.Intersects(footprint))
                    return true;
            }
            return false;
        }

        private static int Find(int[] parents, int value)
        {
            int root = value;
            while (parents[root] != root)
                root = parents[root];
            while (parents[value] != value)
            {
                int parent = parents[value];
                parents[value] = root;
                value = parent;
            }
            return root;
        }

        private static void Union(int[] parents, int left, int right)
        {
            int leftRoot = Find(parents, left);
            int rightRoot = Find(parents, right);
            if (leftRoot != rightRoot)
                parents[rightRoot] = leftRoot;
        }
    }
}
