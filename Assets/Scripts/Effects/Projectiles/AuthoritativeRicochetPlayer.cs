using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>Immutable board projection supplied by the formation view.</summary>
    public readonly struct BoardVisualFrame
    {
        private readonly Vector3 _frontLeft, _frontRight, _backLeft, _backRight;
        private readonly Func<float, int> _sortingAtDepth;
        public BoardVisualFrame(Vector3 frontLeft, Vector3 frontRight, Vector3 backLeft, Vector3 backRight,
            Func<float, int> sortingAtDepth = null)
        {
            _frontLeft = frontLeft; _frontRight = frontRight; _backLeft = backLeft; _backRight = backRight;
            _sortingAtDepth = sortingAtDepth;
        }
        public Vector3 Project(float x, float y)
            => Vector3.Lerp(Vector3.Lerp(_frontLeft, _frontRight, x), Vector3.Lerp(_backLeft, _backRight, x), y);
        public int SortingOrderAtDepth(float y) => _sortingAtDepth?.Invoke(y) ?? 0;
    }

    /// <summary>
    /// Owns each transient flight and its one playback clock. Water dragons swim
    /// through recorded Direct contacts; other visuals project immutable Core waypoints. Collision
    /// targets, lifetime and count are never reselected by presentation.
    /// A collision's Direct and Splash receipts are delivered together. The
    /// attack queue remains the sole owner of receipt-consumption state.
    /// </summary>
    internal static class AuthoritativeRicochetPlayer
    {
        public static IEnumerator Play(VisualEffectManager manager,
            IReadOnlyList<ProjectileTrace> traces,
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> hits,
            VfxProfile profile, BoardVisualFrame? frame,
            Func<bool> stillActive, Action<AuthoritativeProjectileHit> onHit,
            AttackEffectCompletion completion = null)
        {
            if (manager == null || traces == null || frame == null || profile == null) yield break;
            Dictionary<long, List<int>> collisions = IndexCollisions(hits);
            var visuals = new List<FlightVisual>();
            var visualsByInstance = new Dictionary<int, FlightVisual>();
            var arrivals = new List<TimedCollision>();
            foreach (ProjectileTrace trace in traces)
            {
                if (trace?.Points == null || trace.Points.Count < 2) continue;
                Vector3? FindRecordedAnchor(ProjectileTracePoint point)
                {
                    if (!collisions.TryGetValue(CollisionKey(trace.AttackInstanceIndex,
                        point.DamageSubHitIndex), out List<int> receipts)) return null;
                    foreach (int index in receipts)
                    {
                        var pending = hits[index];
                        if (pending.damageEvent.Kind == DamageKind.Direct && pending.hasWorldAnchor &&
                            pending.damageEvent.TargetEnemyRuntimeId == point.TargetEnemyRuntimeId)
                            return pending.worldAnchor;
                    }
                    return null;
                }
                var path = profile.visualType == ProceduralWaterDragonVisual.VisualTypeName
                    ? new WaterDragonSwimPath(trace, frame.Value, profile.projectileScale, FindRecordedAnchor)
                    : null;
                var flightVisual = new FlightVisual(trace, path);
                visuals.Add(flightVisual);
                visualsByInstance[trace.AttackInstanceIndex] = flightVisual;
                foreach (ProjectileTracePoint point in trace.Points)
                    if (point.Kind == ProjectileTracePointKind.Hit ||
                        point.Kind == ProjectileTracePointKind.InitialFeedback ||
                        point.Kind == ProjectileTracePointKind.InitialFeedbackFallback)
                        arrivals.Add(new TimedCollision(trace.AttackInstanceIndex, point));
            }
            arrivals.Sort((a, b) =>
            {
                int time = a.Point.Time.CompareTo(b.Point.Time);
                if (time != 0) return time;
                int instance = a.AttackInstanceIndex.CompareTo(b.AttackInstanceIndex);
                return instance != 0 ? instance : a.Point.DamageSubHitIndex.CompareTo(b.Point.DamageSubHitIndex);
            });
            double startedAt = Time.timeAsDouble;
            int arrived = 0;
            try
            {
                while (stillActive())
                {
                    // One scaled playback clock samples absolute Core times.
                    // A slow frame catches up every due receipt, never stretches
                    // each segment or changes rule damage/launch decisions.
                    float elapsed = (float)(Time.timeAsDouble - startedAt);
                    bool unfinished = false;
                    bool faulted = false;
                    try
                    {
                        foreach (FlightVisual visual in visuals)
                        {
                            visual.Sample(elapsed, manager, profile, frame.Value);
                            unfinished |= !visual.Finished;
                        }
                        while (arrived < arrivals.Count && arrivals[arrived].Point.Time <= elapsed && stillActive())
                        {
                            TimedCollision arrival = arrivals[arrived++];
                            ProjectileTracePoint point = arrival.Point;
                            Vector3 impact = frame.Value.Project(point.X, point.Y);
                            if (!collisions.TryGetValue(CollisionKey(arrival.AttackInstanceIndex,
                                point.DamageSubHitIndex), out List<int> receipts)) continue;
                            foreach (int index in receipts)
                            {
                                if (!stillActive()) break;
                                var pending = hits[index];
                                if (pending.damageEvent.Kind == DamageKind.Direct)
                                {
                                    if (pending.damageEvent.TargetEnemyRuntimeId != point.TargetEnemyRuntimeId) continue;
                                    bool contact = point.Kind != ProjectileTracePointKind.InitialFeedbackFallback;
                                    if (contact && visualsByInstance.TryGetValue(arrival.AttackInstanceIndex,
                                        out FlightVisual flightVisual))
                                        flightVisual.PlaceContact(point.Time,
                                            pending.hasWorldAnchor ? pending.worldAnchor : impact, point.Y);
                                    if (contact && pending.hasWorldAnchor)
                                        manager.PlaySmallHitEffect(
                                            profile.visualType == ProceduralWaterDragonVisual.VisualTypeName
                                                ? pending.worldAnchor : impact, profile, completion: completion);
                                }
                                // Enemy anchors locate real responses, including exposed enemies
                                // in deeper rows. Swimming over an actor is not another hit.
                                onHit?.Invoke(new AuthoritativeProjectileHit(pending.damageEvent,
                                    pending.hasWorldAnchor ? pending.worldAnchor : impact, index));
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        // Exit normally after cleanup so the bridge can flush
                        // authoritative receipts and release its context.
                        Debug.LogException(exception);
                        faulted = true;
                    }
                    if (faulted || !unfinished) break;
                    yield return null;
                }
            }
            finally
            {
                foreach (FlightVisual visual in visuals)
                {
                    try { visual.Dispose(); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            }
        }

        private readonly struct TimedCollision
        {
            internal readonly int AttackInstanceIndex;
            internal readonly ProjectileTracePoint Point;
            internal TimedCollision(int instance, ProjectileTracePoint point)
            { AttackInstanceIndex = instance; Point = point; }
        }

        // Owned by this batch coroutine; no child coroutines or shared clocks.
        private sealed class FlightVisual
        {
            private readonly ProjectileTrace _trace;
            private readonly WaterDragonSwimPath _swimPath;
            private GameObject _root;
            private IProjectileVisual _visual;
            private UnityEngine.Rendering.SortingGroup _sorting;
            private LineRenderer[] _renderers;
            private Color[] _colors;
            private int _point;
            private bool _disposed;
            internal bool Finished { get; private set; }
            internal FlightVisual(ProjectileTrace trace, WaterDragonSwimPath swimPath)
            { _trace = trace; _swimPath = swimPath; }

            internal void Sample(float elapsed, VisualEffectManager manager, VfxProfile profile, BoardVisualFrame frame)
            {
                if (Finished || elapsed < _trace.BirthTime) return;
                bool swimmingDragon = _swimPath != null;
                float flightAge = Mathf.Clamp(elapsed - _trace.BirthTime, 0f,
                    Mathf.Max(0f, _trace.EndTime - _trace.BirthTime));
                if (_root == null)
                {
                    _root = new GameObject("AuthorityWaterDragon", typeof(SpriteRenderer));
                    _root.transform.SetParent(manager.projectileContainer, false);
                    _sorting = _root.AddComponent<UnityEngine.Rendering.SortingGroup>();
                    _visual = ProjectileVisualFactory.Create(profile.visualType);
                    _visual.Initialize(_root, profile, profile.projectileScale);
                    _visual.OnFlightStart();
                    _renderers = _root.GetComponentsInChildren<LineRenderer>();
                    _colors = new Color[_renderers.Length];
                    for (int i = 0; i < _colors.Length; i++) _colors[i] = _renderers[i].startColor;
                }
                if (swimmingDragon)
                {
                    ApplyPose(_swimPath.Sample(elapsed));
                    (_visual as ProceduralWaterDragonVisual)?.Sample(flightAge);
                }
                else
                {
                    while (_point + 1 < _trace.Points.Count && _trace.Points[_point + 1].Time <= elapsed) _point++;
                    ProjectileTracePoint from = _trace.Points[_point];
                    ProjectileTracePoint to = _trace.Points[Mathf.Min(_point + 1, _trace.Points.Count - 1)];
                    float duration = to.Time - from.Time;
                    float progress = duration > 0f ? Mathf.Clamp01((elapsed - from.Time) / duration) : 1f;
                    Vector3 start = frame.Project(from.X, from.Y), end = frame.Project(to.X, to.Y);
                    Vector3 direction = end - start;
                    _root.transform.position = Vector3.Lerp(start, end, progress);
                    _sorting.sortingOrder = frame.SortingOrderAtDepth(Mathf.Lerp(from.Y, to.Y, progress));
                    if (direction.sqrMagnitude > 0.00001f)
                        _root.transform.rotation = Quaternion.Euler(0f, 0f,
                            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                }
                float alpha = Mathf.Clamp01(1f - (elapsed - _trace.EndTime) / 0.3f);
                if (swimmingDragon) alpha *= Mathf.SmoothStep(0f, 1f, flightAge / 0.08f);
                for (int i = 0; i < _renderers.Length; i++)
                {
                    Color color = _colors[i]; color.a *= alpha;
                    _renderers[i].startColor = _renderers[i].endColor = color;
                }
                if (elapsed >= _trace.EndTime + 0.3f) { Finished = true; Dispose(); }
            }

            internal void PlaceContact(float time, Vector3 position, float depth)
            {
                if (Finished || _root == null || _swimPath == null) return;
                // Apply each due contact before its feedback. Keep the last
                // contact pose for this frame without delaying any receipts.
                ApplyPose(_swimPath.AtContact(time, position, depth));
            }

            private void ApplyPose(WaterDragonSwimPath.Pose pose)
            {
                _root.transform.position = pose.Position;
                _root.transform.rotation = Quaternion.identity;
                _root.transform.localScale = pose.Scale;
                _sorting.sortingOrder = pose.SortingOrder;
            }

            internal void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { _visual?.OnFlightEnd(); }
                finally
                {
                    try { _visual?.Cleanup(); }
                    finally { if (_root != null) UnityEngine.Object.Destroy(_root); }
                }
            }
        }

        private static Dictionary<long, List<int>> IndexCollisions(
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> hits)
        {
            var result = new Dictionary<long, List<int>>();
            if (hits == null) return result;
            for (int i = 0; i < hits.Count; i++)
            {
                DamageEvent hit = hits[i].damageEvent;
                if (hit.Kind != DamageKind.Direct && hit.Kind != DamageKind.Splash) continue;
                long key = CollisionKey(hit.AttackInstanceIndex, hit.SubHitIndex);
                if (!result.TryGetValue(key, out List<int> receipts)) result.Add(key, receipts = new List<int>());
                receipts.Add(i);
            }
            return result;
        }

        private static long CollisionKey(int attackInstanceIndex, int subHitIndex)
            => ((long)attackInstanceIndex << 32) | (uint)subHitIndex;
    }
}
