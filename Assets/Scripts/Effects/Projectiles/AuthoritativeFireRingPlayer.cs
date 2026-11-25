using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Owns the rising casting ring, overlapping ring flights and local bursts.
    /// Receipts alone define contacts: captured anchors are never retargeted.
    /// Owns both family variants and their source-keyed replay reservations;
    /// retaining a main ring never holds the attack receipt context open.
    /// Animation and idle updates are mutually exclusive writers; disable
    /// releases all geometry. This remains one visual family state machine.
    /// </summary>
    public sealed class AuthoritativeFireRingPlayer : MonoBehaviour
    {
        public const string VisualTypeName = "ProceduralFireRing";

        private sealed class Ring
        {
            public GameObject Root;
            public readonly List<LineRenderer> Lines = new List<LineRenderer>();
            public readonly List<Color> Colors = new List<Color>();
            public Transform Inner;
            public FireRingEnergyGeometry Energy;
        }

        private sealed class Flight
        {
            public AuthoritativeProjectileSequence Sequence;
            public Ring Ring;
            public Vector3 Start;
            public float LaunchTime;
            public int HitIndex;
            public bool Finished;
        }

        private sealed class Burst
        {
            public Ring Ring;
            public float Started;
        }

        internal sealed class ReplayClaim { }

        private sealed class ReplayBatch
        {
            public int Frame;
            public int Count;
        }

        private sealed class Cast
        {
            public GameObject Root;
            public Func<bool> Active;
            public Ring Main;
            public Vector3 Origin;
            public float Rotation;
            public bool Retain;
            public bool Claimed;
            public bool Idle;
            public int SourceRuntimeId;
            public ReplayClaim Claim;
        }

        private readonly Dictionary<ReplayClaim, Cast> _claimed = new Dictionary<ReplayClaim, Cast>();
        private readonly Dictionary<int, Queue<Cast>> _retained = new Dictionary<int, Queue<Cast>>();
        private readonly HashSet<Cast> _casts = new HashSet<Cast>();
        private readonly List<Cast> _cancelled = new List<Cast>();
        private ReplayBatch _replayBatch;

        internal static IEnumerator Play(MonoBehaviour host, VisualEffectManager manager,
            IReadOnlyList<AuthoritativeProjectileSequence> sequences, VfxProfile profile,
            Vector3? castOrigin, Func<bool> active, Action<AuthoritativeProjectileHit> onContact, int sourceRuntimeId = 0,
            ReplayClaim replayClaim = null, bool isReplay = false, Func<bool> canRetainForReplay = null)
        {
            if (host == null || manager == null || profile == null || !active())
            {
                DiscardReplay(host, replayClaim);
                yield break;
            }
            var owner = host.GetComponent<AuthoritativeFireRingPlayer>() ??
                host.gameObject.AddComponent<AuthoritativeFireRingPlayer>();
            Cast replay = null;
            if (replayClaim != null)
            {
                owner._claimed.TryGetValue(replayClaim, out replay);
                owner._claimed.Remove(replayClaim);
                if (replay != null) replay.Claim = null;
            }
            if (isReplay)
            {
                // Core publishes every due cast synchronously. Collect that
                // frame's batch before spacing its flights so identical paths
                // remain readable; even a large batch adds at most 0.72s.
                if (owner._replayBatch == null || owner._replayBatch.Frame != Time.frameCount)
                    owner._replayBatch = new ReplayBatch { Frame = Time.frameCount };
                ReplayBatch batch = owner._replayBatch;
                int ordinal = batch.Count++;
                yield return null;
                float delay = ordinal * Mathf.Min(0.16f, 0.72f / Mathf.Max(1, batch.Count - 1));
                float elapsed = 0f;
                while (elapsed < delay && owner != null && owner.isActiveAndEnabled && active())
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                if (owner == null || !owner.isActiveAndEnabled || !active())
                {
                    if (owner != null && replay != null) owner.Cleanup(replay);
                    yield break;
                }
            }
            yield return owner.Animate(sequences, profile,
                castOrigin ?? manager.playerAttackOrigin?.position ?? Vector3.zero, active,
                sourceRuntimeId, replay, isReplay, canRetainForReplay, hit =>
                {
                    manager.PlaySmallHitEffect(hit.Position, profile, emitParticles: false);
                    onContact?.Invoke(hit);
                });
        }

        private IEnumerator Animate(IReadOnlyList<AuthoritativeProjectileSequence> sequences,
            VfxProfile profile, Vector3 cardOrigin, Func<bool> active,
            int sourceRuntimeId, Cast replay, bool isReplay, Func<bool> canRetainForReplay,
            Action<AuthoritativeProjectileHit> onContact)
        {
            bool evolved = profile.lightningRingLayerCount > 1;
            // The replay claims its source before Core publishes TurnEnd; it can
            // start after the initial fade without losing its original position.
            while (replay != null && _casts.Contains(replay) && replay.Root != null && !replay.Idle && active())
                yield return null;
            if (!active()) { if (replay != null) Cleanup(replay); yield break; }
            if (replay != null && (!_casts.Contains(replay) || replay.Root == null)) yield break;
            var cast = replay != null && replay.Root != null ? replay :
                new Cast { Root = new GameObject("AuthorityFireRingCast") };
            cast.Root.transform.SetParent(transform, false);
            cast.Active = active;
            cast.Idle = false;
            cast.Claimed = false;
            cast.SourceRuntimeId = sourceRuntimeId;
            cast.Retain = evolved && !isReplay && sourceRuntimeId != 0 &&
                (canRetainForReplay == null || canRetainForReplay());
            _casts.Add(cast);
            Ring main = cast.Main ?? BuildRing(cast.Root.transform, profile, false);
            cast.Main = main;
            SetOpacity(main, 0f);
            float scale = Mathf.Clamp(profile.selfEffectScale, 0.4f, 1.8f);
            Vector3 elevated = replay != null && replay.Root != null ? cast.Origin :
                ResolveOrigin(cardOrigin, profile, scale);
            cast.Origin = elevated;
            if (cast.Retain)
            {
                if (!_retained.TryGetValue(sourceRuntimeId, out Queue<Cast> queued))
                    _retained.Add(sourceRuntimeId, queued = new Queue<Cast>());
                queued.Enqueue(cast);
            }
            Vector3 riseStart = isReplay ? elevated : elevated - Vector3.up * 0.38f;
            var flights = new List<Flight>();
            var bursts = new List<Burst>();
            float launchAt = isReplay ? 0.22f : 0.50f; // 0.2s rise, then a readable 0.3s rotation.
            float flightDuration = Mathf.Max(0.08f, profile.flightDuration);
            float delay = Mathf.Min(Mathf.Max(0f, profile.multiProjectileDelay),
                0.60f / Mathf.Max(1, (sequences?.Count ?? 1) - 1));
            for (int i = 0; sequences != null && i < sequences.Count; i++)
            {
                if (sequences[i] == null || sequences[i].Hits.Count == 0) continue;
                Ring ring = BuildRing(cast.Root.transform, profile, true);
                ring.Root.SetActive(false);
                flights.Add(new Flight { Sequence = sequences[i], Ring = ring,
                    Start = elevated, LaunchTime = launchAt + flights.Count * delay });
            }
            float elapsed = 0f;
            float finishedAt = -1f;
            try
            {
                while (_casts.Contains(cast) && cast.Root != null && isActiveAndEnabled && active())
                {
                    elapsed += Time.deltaTime;
                    float rise = isReplay ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.2f));
                    main.Root.transform.position = Vector3.Lerp(riseStart, elevated, rise);
                    main.Root.transform.localScale = Vector3.one * scale * Mathf.Lerp(0.75f, 1f, rise);
                    cast.Rotation += Time.deltaTime * 240f;
                    RotateRing(main, cast.Rotation);
                    if (isReplay && elapsed < launchAt)
                        main.Root.transform.localScale *= 1f + 0.12f * Mathf.Sin(elapsed / launchAt * Mathf.PI);
                    int unfinished = 0;
                    for (int i = 0; i < flights.Count; i++)
                    {
                        Flight flight = flights[i];
                        if (flight.Finished) continue;
                        unfinished++;
                        if (elapsed < flight.LaunchTime) continue;
                        flight.Ring.Root.SetActive(true);
                        AuthoritativeProjectileHit hit = flight.Sequence.Hits[flight.HitIndex];
                        float t = Mathf.Clamp01((elapsed - flight.LaunchTime) / flightDuration);
                        Vector3 point = Vector3.Lerp(flight.Start, hit.Position, t);
                        point.y += Mathf.Sin(t * Mathf.PI) * 0.16f;
                        flight.Ring.Root.transform.position = point;
                        RotateRing(flight.Ring, elapsed * 960f + i * 47f);
                        flight.Ring.Energy.SetFlightWake(point, flight.Start);
                        flight.Ring.Root.transform.localScale = Vector3.one * scale * (evolved ? 0.42f : 0.34f);
                        SetOpacity(flight.Ring, 1f);
                        if (t < 1f) continue;
                        // Advance before callback: a receipt can never be replayed,
                        // even if its callback cancels the entire presentation.
                        flight.HitIndex++;
                        flight.Start = hit.Position;
                        flight.LaunchTime = elapsed;
                        if (flight.HitIndex == flight.Sequence.Hits.Count)
                        {
                            flight.Finished = true;
                            Destroy(flight.Ring.Root);
                        }
                        Ring burst = BuildBurst(cast.Root.transform, profile);
                        burst.Root.transform.position = hit.Position;
                        bursts.Add(new Burst { Ring = burst, Started = elapsed });
                        onContact?.Invoke(hit);
                        if (!active()) break;
                    }
                    for (int i = bursts.Count - 1; i >= 0; i--)
                    {
                        Burst burst = bursts[i];
                        float t = (elapsed - burst.Started) / (evolved ? 0.32f : 0.20f);
                        if (t >= 1f)
                        {
                            Destroy(burst.Ring.Root);
                            bursts.RemoveAt(i);
                            continue;
                        }
                        burst.Ring.Root.transform.localScale = Vector3.one * (evolved ? 1.48f : 0.88f) * Mathf.Lerp(0.35f, 1f, Mathf.Sqrt(t));
                        SetOpacity(burst.Ring, 1f - t);
                    }
                    if (unfinished == 0 && bursts.Count == 0 && elapsed >=
                        Mathf.Max(launchAt, profile.selfEffectDuration) && finishedAt < 0f)
                        finishedAt = elapsed;
                    float fade = finishedAt < 0f ? 1f : 1f - (elapsed - finishedAt) / 0.16f;
                    float appearance = isReplay ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.10f)) : rise;
                    SetOpacity(main, appearance * Mathf.Clamp01(fade));
                    if (fade <= 0f)
                    {
                        // Keep only an invisible replay reservation. An ended
                        // cast must never leave a visible ring stacked at center.
                        cast.Idle = !isReplay && (cast.Retain || cast.Claimed);
                        break;
                    }
                    yield return null;
                }
            }
            finally { if (!cast.Idle) Cleanup(cast); }
        }

        private static Ring BuildRing(Transform parent, VfxProfile profile, bool small)
        {
            bool evolved = profile.lightningRingLayerCount > 1;
            var ring = new Ring { Root = new GameObject(small ? "FlyingFireRing" : "HoveringFireRing") };
            ring.Root.transform.SetParent(parent, false);
            Color gold = profile.selfEffectPrimaryColor;
            AddLine(ring, new Color(1f, .34f, .025f, .94f), .085f, true, Circle(.46f, 64));
            AddLine(ring, gold, .034f, true, Circle(.425f, 64));
            AddLine(ring, new Color(1f, .94f, .62f, 1f), .016f, true, Circle(.398f, 56));
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 tangent = new Vector3(-radial.y, radial.x, 0f);
                AddLine(ring, gold, .022f, false, radial * .35f - tangent * .028f,
                    radial * .44f - tangent * .028f, radial * .44f + tangent * .035f);
            }
            if (evolved)
            {
                var inner = new GameObject("CounterRotatingThunderRing");
                inner.transform.SetParent(ring.Root.transform, false);
                ring.Inner = inner.transform;
                Color electric = new Color(.45f, .86f, 1f, 1f);
                AddLine(ring, new Color(.12f, .52f, 1f, .7f), .070f, true, Circle(.29f, 56));
                ring.Lines[ring.Lines.Count - 1].transform.SetParent(ring.Inner, false);
                AddLine(ring, electric, .025f, true, Circle(.29f, 56));
                ring.Lines[ring.Lines.Count - 1].transform.SetParent(ring.Inner, false);
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * .25f;
                    Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    Vector3 tangent = new Vector3(-radial.y, radial.x, 0f);
                    AddLine(ring, electric, .032f, false, radial * .19f,
                        radial * .32f + tangent * .05f, radial * .26f + tangent * .09f,
                        radial * .38f + tangent * .12f);
                    ring.Lines[ring.Lines.Count - 1].transform.SetParent(ring.Inner, false);
                }
            }
            ring.Energy = ring.Root.AddComponent<FireRingEnergyGeometry>();
            ring.Energy.Initialize(evolved, false, small);
            return ring;
        }

        private static Ring BuildBurst(Transform parent, VfxProfile profile)
        {
            bool evolved = profile.lightningRingLayerCount > 1;
            var ring = new Ring { Root = new GameObject(evolved ? "LocalThunderFireDetonation" : "LocalThunderFireBurst") };
            ring.Root.transform.SetParent(parent, false);
            // A compact hot core and expanding flame rings remain local to the
            // receipt. Their larger visual footprint never creates splash damage.
            AddLine(ring, new Color(1f, .93f, .56f, 1f), evolved ? .25f : .16f, true, Circle(.065f, 32));
            AddLine(ring, new Color(1f, .39f, .025f, .88f), evolved ? .12f : .075f, true, Circle(.27f, 40));
            if (evolved)
            {
                AddLine(ring, new Color(1f, .64f, .10f, .75f), .065f, true, Circle(.48f, 48));
                AddLine(ring, new Color(.42f, .86f, 1f, .78f), .035f, true, Circle(.55f, 48));
            }
            ring.Energy = ring.Root.AddComponent<FireRingEnergyGeometry>();
            ring.Energy.Initialize(evolved, true, false);
            return ring;
        }

        private static void RotateRing(Ring ring, float angle)
        {
            ring.Root.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            if (ring.Inner != null)
                ring.Inner.localRotation = Quaternion.Euler(0f, 0f, -2f * angle);
        }

        // Reservations use the stable runtime source and FIFO cast order.
        // They never keep the attack receipt context open while the ring is idle.
        internal static ReplayClaim ClaimReplay(MonoBehaviour host, int sourceRuntimeId)
        {
            if (host == null) return null;
            var owner = host.GetComponent<AuthoritativeFireRingPlayer>();
            if (owner == null || !owner._retained.TryGetValue(sourceRuntimeId, out Queue<Cast> casts))
                return null;
            while (casts.Count > 0)
            {
                Cast cast = casts.Dequeue();
                if (casts.Count == 0) owner._retained.Remove(sourceRuntimeId);
                if (!owner._casts.Contains(cast) || cast.Root == null) continue;
                cast.Claimed = true;
                cast.Retain = false;
                var claim = new ReplayClaim();
                cast.Claim = claim;
                owner._claimed.Add(claim, cast);
                return claim;
            }
            return null;
        }

        internal static void DiscardReplay(MonoBehaviour host, ReplayClaim claim)
        {
            if (host == null || claim == null) return;
            var owner = host.GetComponent<AuthoritativeFireRingPlayer>();
            if (owner == null || !owner._claimed.TryGetValue(claim, out Cast cast)) return;
            owner._claimed.Remove(claim);
            owner.Cleanup(cast);
        }

        internal void ClearRetainedRings()
        {
            _retained.Clear();
            _cancelled.Clear();
            foreach (Cast cast in _casts)
            {
                if (cast.Claimed) continue;
                cast.Retain = false;
                if (cast.Idle) _cancelled.Add(cast);
            }
            for (int i = 0; i < _cancelled.Count; i++) Cleanup(_cancelled[i]);
        }

        internal void CancelAll() => OnDisable();

        private static void AddLine(Ring ring, Color color, float width, bool loop, params Vector3[] points)
        {
            var child = new GameObject("RingStroke", typeof(LineRenderer));
            child.transform.SetParent(ring.Root.transform, false);
            var line = child.GetComponent<LineRenderer>();
            line.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
            line.useWorldSpace = false;
            line.loop = loop;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.sortingOrder = 49;
            ring.Lines.Add(line);
            ring.Colors.Add(color);
        }

        private static Vector3[] Circle(float radius, int count)
        {
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                points[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            }
            return points;
        }

        private static void SetOpacity(Ring ring, float alpha)
        {
            ring.Energy.Advance(Time.time, alpha);
            for (int i = 0; i < ring.Lines.Count; i++)
            {
                Color tint = ring.Colors[i];
                tint.a *= alpha;
                ring.Lines[i].startColor = ring.Lines[i].endColor = tint;
            }
        }

        private static Vector3 ResolveOrigin(Vector3 card, VfxProfile profile, float scale)
        {
            Vector3 origin = card + profile.selfEffectOffset;
            Camera camera = Camera.main;
            if (camera == null) return origin;
            Vector3 source = camera.WorldToViewportPoint(card);
            Vector3 viewport = camera.WorldToViewportPoint(origin);
            if (source.z <= 0f || viewport.z <= 0f) return origin;
            float radius = Mathf.Abs(camera.WorldToViewportPoint(card + Vector3.up * FireRingEnergyGeometry.OuterRadius * scale).y - source.y);
            float clearance = profile.castingArtifactCardClearancePixels *
                (Screen.height / 1080f) / Mathf.Max(1f, camera.pixelRect.height);
            viewport.y = Mathf.Clamp(Mathf.Max(viewport.y, source.y + clearance + radius,
                0.36f + radius), radius + 0.02f, Mathf.Max(radius + 0.02f, 0.97f - radius));
            float horizontalRadius = Mathf.Abs(camera.WorldToViewportPoint(card +
                Vector3.right * FireRingEnergyGeometry.OuterRadius * scale).x - source.x);
            viewport.x = Mathf.Clamp(viewport.x, horizontalRadius + .02f,
                Mathf.Max(horizontalRadius + .02f, .98f - horizontalRadius));
            return camera.ViewportToWorldPoint(viewport);
        }

        private void Update()
        {
            // Host coroutine shutdown does not necessarily dispose an iterator.
            // This owner still retires its transient roots when context is released.
            _cancelled.Clear();
            foreach (Cast cast in _casts)
            {
                if (cast.Root == null || (!cast.Idle && !cast.Active()))
                    _cancelled.Add(cast);
                // Idle reservations have already faded to zero. No geometry
                // update runs until their replay resumes the animation owner.
            }
            for (int i = 0; i < _cancelled.Count; i++) Cleanup(_cancelled[i]);
        }

        private void Cleanup(Cast cast)
        {
            _casts.Remove(cast);
            if (cast.Claim != null) _claimed.Remove(cast.Claim);
            if (_retained.TryGetValue(cast.SourceRuntimeId, out Queue<Cast> queued))
            {
                int count = queued.Count;
                for (int i = 0; i < count; i++)
                {
                    Cast pending = queued.Dequeue();
                    if (pending != cast) queued.Enqueue(pending);
                }
                if (queued.Count == 0) _retained.Remove(cast.SourceRuntimeId);
            }
            cast.Claim = null;
            cast.Retain = cast.Claimed = cast.Idle = false;
            if (cast.Root != null) Destroy(cast.Root);
        }

        private void OnDisable()
        {
            foreach (Cast cast in _casts)
                if (cast.Root != null) Destroy(cast.Root);
            _casts.Clear();
            _retained.Clear();
            _claimed.Clear();
            _replayBatch = null;
        }
    }
}
