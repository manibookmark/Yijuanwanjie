using System;
using System.Collections.Generic;

namespace WanjieScroll.Core
{
    [Serializable]
    public sealed class RicochetProjectileSpec
    {
        public bool enabled;
        public float minimumLifetime = 0.5f;
        public float maximumLifetime = 1.5f;
        public float amountSpeedCoefficient = 0.1f;

        internal void RequireValid()
        {
            if (float.IsNaN(minimumLifetime) || float.IsNaN(maximumLifetime) ||
                minimumLifetime < 0.5f || maximumLifetime > 1.5f ||
                maximumLifetime < minimumLifetime)
                throw new ArgumentException("Ricochet lifetime must remain within the original 0.5–1.5 second interval.");
            if (float.IsNaN(amountSpeedCoefficient) || float.IsInfinity(amountSpeedCoefficient) ||
                amountSpeedCoefficient < 0f)
                throw new ArgumentException("Ricochet amount speed coefficient must be finite and nonnegative.");
        }
    }

    public sealed class ResolvedRicochetProjectileSpec
    {
        public float MinimumLifetime { get; }
        public float MaximumLifetime { get; }
        public float SpeedMultiplier { get; }
        public float Speed { get; }
        public float LaunchDelayMultiplier { get; }
        internal int MaximumCollisionsPerProjectile { get; }
        internal ResolvedRicochetProjectileSpec(RicochetProjectileSpec spec, int projectileCount, int baseProjectileCount)
        {
            spec.RequireValid();
            MinimumLifetime = spec.minimumLifetime;
            MaximumLifetime = spec.maximumLifetime;
            if (baseProjectileCount <= 0)
                throw new ArgumentException("Ricochet requires a positive base projectile count.");
            // Projectile.OnSpawned: 1 + ((DamageParams.Amount / IDamageEffect.BaseAmount - 1) * coefficient).
            // Runetracer's base count is its original L0 count; its final Amount already contains Combo and player Amount.
            double multiplier = 1d + ((double)projectileCount / baseProjectileCount - 1d) * spec.amountSpeedCoefficient;
            double speed = RicochetProjectileResolver.BaseBoardSpeed * multiplier;
            if (speed <= 0d || speed > float.MaxValue)
                throw new ArgumentException("Ricochet resolved speed must be positive and finite.");
            SpeedMultiplier = (float)multiplier;
            Speed = (float)speed;
            // Native WeaponConfig: min + (1-min) * powf(rate, actual Amount - L0 Amount).
            const float rate = 0.815f, minimum = 0.1f;
            LaunchDelayMultiplier = minimum + (1f - minimum) *
                (float)Math.Pow(rate, projectileCount - baseProjectileCount);
            // A line crosses no more than |dx| + |dy| + two boundaries. Each segment locks every hit column.
            // Bound the complete path before mutation; excessive plans fail instead of dropping later collisions.
            double segments = Math.Ceiling(Speed * (double)MaximumLifetime * Math.Sqrt(2d)) + 3d;
            // Initial model damage precedes flight and is an extra receipt batch,
            // independent of how many visible contacts the path can make.
            MaximumCollisionsPerProjectile = (int)Math.Min(AttackPlan.MaximumResolutionEventsPerPlan + 1d,
                1d + segments * EnemyFormationLayout.Columns);
        }
    }

    public enum ProjectileTracePointKind { Start, Hit, Boundary, End, InitialFeedback, InitialFeedbackFallback }

    /// <summary>Absolute seconds on the volley rule clock, with authoritative
    /// board coordinates: x left-to-right, y player-to-far-edge, both 0–1.</summary>
    public readonly struct ProjectileTracePoint
    {
        public float Time { get; }
        public float X { get; }
        public float Y { get; }
        public ProjectileTracePointKind Kind { get; }
        public int TargetEnemyRuntimeId { get; }
        public int TargetIndex { get; }
        public int DamageSubHitIndex { get; }
        internal ProjectileTracePoint(float time, float x, float y, ProjectileTracePointKind kind,
            int runtimeId = 0, int targetIndex = -1, int damageSubHitIndex = -1)
        {
            Time = time; X = x; Y = y; Kind = kind; TargetEnemyRuntimeId = runtimeId;
            TargetIndex = targetIndex; DamageSubHitIndex = damageSubHitIndex;
        }
    }

    /// <summary>One projectile's immutable timeline. Lifetime is a duration;
    /// BirthTime and EndTime are absolute times shared by every trace in the volley.</summary>
    public sealed class ProjectileTrace
    {
        public int AttackInstanceIndex { get; }
        public int SubHitIndex { get; }
        public float Lifetime { get; }
        public float BirthTime => Points[0].Time;
        public float EndTime => Points[Points.Count - 1].Time;
        public IReadOnlyList<ProjectileTracePoint> Points { get; }
        public RicochetInitialStrike InitialStrike { get; }
        internal ProjectileTrace(AttackSubHit hit, float lifetime, List<ProjectileTracePoint> points,
            RicochetInitialStrike initialStrike = null)
        {
            AttackInstanceIndex = hit.AttackInstanceIndex;
            SubHitIndex = hit.SubHitIndex;
            Lifetime = lifetime;
            Points = new List<ProjectileTracePoint>(points).AsReadOnly();
            InitialStrike = initialStrike;
        }
    }

    public sealed class ProjectileTraceBatch
    {
        public int SourceCardInstanceId { get; }
        public IReadOnlyList<ProjectileTrace> Traces { get; }
        internal ProjectileTraceBatch(int sourceCardInstanceId, IReadOnlyList<ProjectileTrace> traces)
        {
            SourceCardInstanceId = sourceCardInstanceId;
            Traces = new List<ProjectileTrace>(traces).AsReadOnly();
        }
    }

    /// <summary>
    /// Owns the board adaptation of viewport ricochets and the volley rule clock.
    /// Geometry is independent of scene transforms; Effects only renders immutable paths and receipts.
    /// Lifetime bounds, rather than a fixed bounce count or additional damage, terminate each projectile.
    /// </summary>
    internal static class RicochetProjectileResolver
    {
        internal const float BaseBoardSpeed = 3.2f;
        private const float Epsilon = 0.00001f;

        internal static AttackResolutionResult Execute(AttackPlan plan, IList<EnemyStats> enemies,
            ResolutionMode mode, IRandomSource random, BossOverkillSession bossOverkill)
        {
            random ??= DefaultRandomSource.Instance;
            var result = new AttackResolutionResult { DisplayedCardDamage = plan.DisplayedCardDamage,
                HitCount = plan.SubHits.Count,
                HitDamage = plan.SubHits.Count > 0 ? plan.SubHits[0].NormalHitDamage : 0 };
            var strikes = new RicochetInitialStrikeOwner(plan, enemies, result, mode, random, bossOverkill);
            var flights = new List<Flight>();
            float birthTime = 0f;
            for (int projectile = 0; projectile < plan.SubHits.Count; projectile++)
            {
                AttackSubHit hit = plan.SubHits[projectile];
                // Native minimum lifetime gates offscreen despawn, not a random
                // duration. Board ricochets stay visible, so both resolution and
                // estimates use the hard maximum. Direction and per-emission
                // delays share the recorded RNG; estimates do not use live RNG.
                float lifetime = plan.RicochetProjectile.MaximumLifetime;
                flights.Add(CreateFlight(projectile, hit, lifetime, plan.RicochetProjectile.Speed,
                    strikes.GetInitialStrike(projectile), mode, random, birthTime));
                // Last emission also consumes its native random interval. The
                // user-confirmed rule clock excludes frame quantization/overshoot.
                float interval = mode == ResolutionMode.Preview ? 0.125f : random.Range(0.1f, 0.15f);
                birthTime += interval * plan.RicochetProjectile.LaunchDelayMultiplier;
            }
            float elapsed = 0f;
            while (TryNextEvent(flights, enemies, bossOverkill, strikes, elapsed, out Flight flight,
                out ProjectileTracePointKind kind, out int target, out float duration))
            {
                foreach (Flight active in flights)
                {
                    if (!active.Started || active.Finished) continue;
                    active.X = ClampBoard(active.X + active.Vx * duration);
                    active.Y = ClampBoard(active.Y + active.Vy * duration);
                }
                elapsed += duration;
                if (kind == ProjectileTracePointKind.Start)
                {
                    flight.Started = true;
                    continue;
                }
                if (kind == ProjectileTracePointKind.End)
                {
                    if (strikes.HasUndeliveredInitial(flight.Projectile))
                    {
                        strikes.DeliverInitial(flight.Projectile, fallback: true);
                        AddInitialFeedback(flight, strikes.GetInitialStrike(flight.Projectile), elapsed,
                            ProjectileTracePointKind.InitialFeedbackFallback);
                    }
                    flight.Points.Add(new ProjectileTracePoint(flight.EndTime, flight.X, flight.Y, kind));
                    flight.Finished = true;
                    continue;
                }
                if (kind == ProjectileTracePointKind.Boundary)
                {
                    flight.Points.Add(new ProjectileTracePoint(elapsed, flight.X, flight.Y, kind));
                    if ((flight.X <= Epsilon && flight.Vx < 0) || (flight.X >= 1f - Epsilon && flight.Vx > 0))
                        flight.Vx = -flight.Vx;
                    if ((flight.Y <= Epsilon && flight.Vy < 0) || (flight.Y >= 1f - Epsilon && flight.Vy > 0))
                        flight.Vy = -flight.Vy;
                    strikes.BeginReflectedSegment(flight.Projectile);
                    continue;
                }
                if (kind == ProjectileTracePointKind.InitialFeedback)
                {
                    strikes.DeliverInitial(flight.Projectile);
                    AddInitialFeedback(flight, strikes.GetInitialStrike(flight.Projectile), elapsed, kind);
                    continue;
                }
                int ordinal = strikes.ResolveAdditionalHit(flight.Projectile, target);
                flight.Points.Add(new ProjectileTracePoint(elapsed, flight.X, flight.Y,
                    ProjectileTracePointKind.Hit, enemies[target].runtimeId, target, ordinal));
            }
            foreach (Flight flight in flights)
                result.AddProjectileTrace(new ProjectileTrace(flight.Hit, flight.Lifetime, flight.Points,
                    strikes.GetInitialStrike(flight.Projectile)));
            return result;
        }

        private sealed class Flight
        {
            internal readonly AttackSubHit Hit;
            internal readonly int Projectile;
            internal readonly float Lifetime;
            internal readonly float BirthTime;
            internal float EndTime => BirthTime + Lifetime;
            internal readonly List<ProjectileTracePoint> Points = new List<ProjectileTracePoint>();
            internal float X, Y, Vx, Vy;
            internal bool Started, Finished;
            internal Flight(int projectile, AttackSubHit hit, float lifetime, float x, float y,
                float vx, float vy, float birthTime)
            {
                Projectile = projectile;
                Hit = hit; Lifetime = lifetime; BirthTime = birthTime; X = x; Y = y; Vx = vx; Vy = vy;
                Points.Add(new ProjectileTracePoint(birthTime, X, Y, ProjectileTracePointKind.Start));
            }
        }

        private static Flight CreateFlight(int projectile, AttackSubHit hit, float lifetime, float speed,
            RicochetInitialStrike initial, ResolutionMode mode, IRandomSource random, float birthTime)
        {
            if (mode == ResolutionMode.Preview)
                return new Flight(projectile, hit, lifetime, initial.AnchorX, 0f, 0f, speed, birthTime);
            float targetX = initial.AnchorX, targetY = initial.AnchorY;
            // Native RuneTracerDirection constructs a source just outside a random viewport edge,
            // offset by a signed 20–50 degree tangent, then WeaponDirection aims from it at the selected target.
            int edge = random.Range(0, 4);
            int sign = random.Range(0, 2) * 2 - 1;
            float tangent = (float)Math.Tan(random.Range(20f, 50f) * sign * Math.PI / 180d);
            float x, y;
            switch (edge)
            {
                case 0: x = targetX + tangent * targetY; y = -0.1f; break;
                case 1: x = -0.1f; y = targetY + tangent * targetX; break;
                case 2: x = targetX + tangent * (1f - targetY); y = 1.1f; break;
                default: x = 1.1f; y = targetY + tangent * (1f - targetX); break;
            }
            float dx = targetX - x, dy = targetY - y;
            float enter = 0f, exit = 1f;
            if (!ClipAxis(x, dx, 0f, 1f, ref enter, ref exit) ||
                !ClipAxis(y, dy, 0f, 1f, ref enter, ref exit))
                throw new InvalidOperationException("Ricochet's target ray does not enter the board.");
            // The off-board lead-in is visual only. The original lifetime starts at board entry in this board adaptation.
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            return new Flight(projectile, hit, lifetime, ClampBoard(x + dx * enter), ClampBoard(y + dy * enter),
                dx / length * speed, dy / length * speed, birthTime);
        }

        private static bool TryNextEvent(List<Flight> flights, IList<EnemyStats> enemies,
            BossOverkillSession bossOverkill, RicochetInitialStrikeOwner strikes, float elapsed,
            out Flight selected, out ProjectileTracePointKind kind, out int target, out float duration)
        {
            selected = null; kind = ProjectileTracePointKind.End; target = -1; duration = float.PositiveInfinity;
            foreach (Flight flight in flights)
            {
                if (flight.Finished) continue;
                if (!flight.Started)
                {
                    float untilBirth = Math.Max(0f, flight.BirthTime - elapsed);
                    if (untilBirth < duration)
                    {
                        selected = flight; kind = ProjectileTracePointKind.Start;
                        target = -1; duration = untilBirth;
                    }
                    continue;
                }
                float toX = Math.Abs(flight.Vx) < Epsilon ? float.PositiveInfinity
                    : ((flight.Vx > 0f ? 1f : 0f) - flight.X) / flight.Vx;
                float toY = Math.Abs(flight.Vy) < Epsilon ? float.PositiveInfinity
                    : ((flight.Vy > 0f ? 1f : 0f) - flight.Y) / flight.Vy;
                float boundary = Math.Max(0f, Math.Min(toX, toY));
                float remaining = Math.Max(0f, flight.EndTime - elapsed);
                float next = Math.Min(remaining, boundary);
                ProjectileTracePointKind nextKind = remaining <= boundary
                    ? ProjectileTracePointKind.End : ProjectileTracePointKind.Boundary;
                int nextTarget = -1;
                if (remaining > Epsilon && TryFindCollision(enemies, bossOverkill, strikes, flight.Projectile,
                    flight.X, flight.Y, flight.Vx, flight.Vy, next, out int collision, out float travel))
                { next = travel; nextTarget = collision; nextKind = ProjectileTracePointKind.Hit; }
                // Native cached feedback is checked before live collisions. Equal-time contacts
                // therefore deliver its pre-applied receipt first and lock this segment's column.
                if (remaining > Epsilon && strikes.CanDeliverInitial(flight.Projectile) &&
                    TryFindInitialFeedback(strikes.GetInitialStrike(flight.Projectile), flight, next,
                        out float feedbackTime) && feedbackTime <= next)
                { next = feedbackTime; nextTarget = -1; nextKind = ProjectileTracePointKind.InitialFeedback; }
                // Same-time ties follow stable attack instance order, after every earlier receipt refreshed the roster.
                if (next >= duration) continue;
                selected = flight; kind = nextKind; target = nextTarget; duration = next;
            }
            return selected != null;
        }

        private static void AddInitialFeedback(Flight flight, RicochetInitialStrike initial, float elapsed,
            ProjectileTracePointKind kind)
        {
            flight.Points.Add(new ProjectileTracePoint(elapsed, flight.X, flight.Y,
                kind, initial.TargetEnemyRuntimeId, initial.TargetIndex, 0));
        }

        private static bool TryFindInitialFeedback(RicochetInitialStrike initial, Flight flight,
            float duration, out float time)
        {
            // A cached column anchor is distinct from the current enemy's logical rectangle.
            // Use a swept point-radius query on the common rule clock, preserving the board
            // adaptation without depending on a render transform or a playback frame.
            const float radius = 0.2f / EnemyFormationLayout.Columns;
            float dx = flight.X - initial.AnchorX, dy = flight.Y - initial.AnchorY;
            float c = dx * dx + dy * dy - radius * radius;
            time = 0f;
            if (c <= 0f) return true;
            float a = flight.Vx * flight.Vx + flight.Vy * flight.Vy;
            float b = 2f * (dx * flight.Vx + dy * flight.Vy);
            float discriminant = b * b - 4f * a * c;
            if (a <= Epsilon || discriminant < 0f) return false;
            time = (-b - (float)Math.Sqrt(discriminant)) / (2f * a);
            return time >= 0f && time <= duration;
        }

        private static float ClampBoard(float value) => Math.Max(0f, Math.Min(1f, value));

        private static bool TryFindCollision(IList<EnemyStats> enemies, BossOverkillSession bossOverkill,
            RicochetInitialStrikeOwner strikes, int projectile,
            float x, float y, float vx, float vy, float duration, out int index, out float time)
        {
            index = -1; time = duration + Epsilon;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyStats enemy = enemies[i];
                if (enemy == null || !enemy.IsDeployed || enemy.IsWithdrawn ||
                    !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy) ||
                    (!EnemyFormationLayout.IsColumnFront(enemies, i) && bossOverkill?.IsActiveTarget(i) != true)) continue;
                int column = Column(enemy, i);
                if (!strikes.CanDamageEnemy(projectile, i)) continue;
                // Collision bounds belong to the logical grid, not sprite or particle sizes.
                float left = (column + 0.1f) / EnemyFormationLayout.Columns;
                float right = (column + enemy.GridWidth - 0.1f) / EnemyFormationLayout.Columns;
                float bottom = (enemy.row + 0.1f) / EnemyFormationLayout.VisibleRows;
                float top = (enemy.row + enemy.GridDepth - 0.1f) / EnemyFormationLayout.VisibleRows;
                float enter = 0f, exit = duration;
                if (!ClipAxis(x, vx, left, right, ref enter, ref exit) ||
                    !ClipAxis(y, vy, bottom, top, ref enter, ref exit) || enter > time) continue;
                index = i; time = Math.Max(0f, enter);
            }
            return index >= 0;
        }

        private static bool ClipAxis(float p, float v, float min, float max, ref float enter, ref float exit)
        {
            if (Math.Abs(v) < Epsilon) return p >= min && p <= max;
            float a = (min - p) / v, b = (max - p) / v;
            if (a > b) { float swap = a; a = b; b = swap; }
            enter = Math.Max(enter, a); exit = Math.Min(exit, b);
            return enter <= exit && exit >= 0f;
        }

        private static int Column(EnemyStats enemy, int index)
            => enemy.column >= 0 ? enemy.column : index % EnemyFormationLayout.Columns;
    }
}
