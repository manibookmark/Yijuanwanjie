using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects.Attacks
{
    /// <summary>Owns resident flying groups, takeoff and projectile launch timing. Each
    /// resolved attack instance has one bird within its Core source.
    /// Core owns damage and bird identities; the attack queue owns hit completion.
    /// This module consumes those facts and never selects or damages targets.
    /// Periodic volleys retain their flock at publication and finish in Core
    /// receipt order; cancel invalidates waiting and launched work together.
    /// Scale review: the batch clock, arrival buffers and flock leases remain
    /// one presentation lifecycle here. Damage, target choice and impact/death
    /// completion stay with their existing owners, not this scheduler.</summary>
    public sealed class PeriodicAttackPresenter : MonoBehaviour
    {
        private sealed class Bird
        {
            internal int AttackIndex;
            internal IPeriodicAttackerVisual Visual;
            internal Vector3 Origin;
            internal float Age;
        }
        private sealed class Flock
        {
            internal int CardId, CastRuntimeId, QueuedVolleys, ActiveVolleys;
            internal readonly HashSet<int> SourceIds = new HashSet<int>();
            internal bool Persistent, InitialPlayed;
            internal VfxProfile Profile;
            internal Vector3 SkyFallback;
            internal readonly List<Bird> Birds = new List<Bird>();
        }
        private sealed class PeriodicVolley
        {
            internal PeriodicAttackSource Source;
            internal Flock Flock;
            internal int Generation;
            internal bool Claimed, Completed, HoldsFlock;
            internal VfxProfile Profile;
            internal IReadOnlyList<AuthoritativeProjectileSequence> Sequences;
            internal Action<AuthoritativeProjectileHit> OnArrive;
            internal Action<bool> OnFinished;
            internal AttackEffectCompletion Completion;
            internal readonly List<PeriodicArrival> Arrivals = new List<PeriodicArrival>();
            internal int NextArrival;
        }
        private sealed class PeriodicArrival
        {
            internal AuthoritativeProjectileHit Hit;
            internal bool Arrived;
        }
        private sealed class PeriodicLaunch
        {
            internal PeriodicVolley Volley;
            internal Bird Bird;
            internal AuthoritativeProjectileSequence Sequence;
            internal readonly Dictionary<int, PeriodicArrival> Arrivals = new Dictionary<int, PeriodicArrival>();
            internal float Offset;
        }
        private readonly List<Flock> _flocks = new List<Flock>();
        private readonly List<PeriodicVolley> _periodicVolleys = new List<PeriodicVolley>();
        private readonly Dictionary<int, Vector3> _origins = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> _preparedSkies = new Dictionary<int, Vector3>();
        private CombatController _combat;
        private VisualEffectManager _manager;
        private Camera _camera;
        private Func<Bounds?> _boardBounds;
        private bool _subscribed;
        private float _time;
        private int _generation;
        private const float TakeoffDuration = .62f;
        private const float PeriodicLaunchWindow = 1.8f;
        private Coroutine _periodicPlayback;

        public void Bind(CombatController combat, VisualEffectManager manager, Camera camera,
            Func<Bounds?> boardBounds = null)
        {
            Unsubscribe();
            Clear();
            _combat = combat; _manager = manager; _camera = camera;
            _boardBounds = boardBounds;
            if (isActiveAndEnabled) Subscribe();
        }
        public void PrepareCardOrigin(int cardRuntimeId, Vector3 world) { _origins[cardRuntimeId] = world; }
        public void PrepareInitialAttack(int cardRuntimeId, VfxProfile profile)
        {
            _preparedSkies[cardRuntimeId] = CaptureSkyCenter(profile.fireCrowHoverHeight);
        }
        public void ForgetCardOrigin(int cardRuntimeId)
        {
            _origins.Remove(cardRuntimeId);
            _preparedSkies.Remove(cardRuntimeId);
        }
        private void OnEnable() { Subscribe(); }
        private void Subscribe()
        {
            if (_subscribed || _combat == null || _manager == null) return;
            foreach (PeriodicAttackSource source in _combat.State.PeriodicAttacks.Active) Register(source);
            _combat.OnPeriodicAttackPlanResolved += PrimeInitial;
            _combat.OnPeriodicAttackChanged += Present;
            _subscribed = true;
        }
        private void PrimeInitial(CardConfig card, AttackPlan plan)
        {
            VfxProfile profile = VfxProfileRegistry.Get(card);
            if (profile == null || !profile.periodicFireCrow || plan == null) return;
            // Each cast has an independent takeoff, even when the same runtime
            // card is replayed before its previous projectiles have arrived.
            var flock = new Flock { CardId = card.runtimeInstanceId, Profile = profile };
            flock.SkyFallback = _preparedSkies.TryGetValue(card.runtimeInstanceId, out Vector3 sky)
                ? sky : CaptureSkyCenter(profile.fireCrowHoverHeight);
            _flocks.Add(flock);
            flock.InitialPlayed = false;
            Vector3 origin = _origins.TryGetValue(card.runtimeInstanceId, out Vector3 prepared)
                ? prepared : (_manager.playerAttackOrigin != null ? _manager.playerAttackOrigin.position : Vector3.down * 3f);
            _origins.Remove(card.runtimeInstanceId);
            _preparedSkies.Remove(card.runtimeInstanceId);
            Resize(flock, plan, origin, true);
        }
        private void Present(PeriodicAttackEvent change)
        {
            if (change?.Source == null) return;
            Flock flock = FindSource(change.Source.EffectRuntimeId);
            switch (change.Kind)
            {
                case PeriodicAttackChangeKind.Placed:
                case PeriodicAttackChangeKind.Refreshed: Register(change.Source); break;
                case PeriodicAttackChangeKind.Removed:
                    if (flock != null)
                    {
                        flock.SourceIds.Remove(change.Source.EffectRuntimeId);
                        flock.Persistent = flock.SourceIds.Count > 0;
                        TryRetire(flock);
                    }
                    break;
            }
        }
        private void Register(PeriodicAttackSource source)
        {
            VfxProfile profile = VfxProfileRegistry.Get(source.SourceCardContentId);
            if (profile == null || !profile.periodicFireCrow) return;
            Flock flock = source.IsBird
                ? _flocks.Find(item => item.CastRuntimeId == source.CastRuntimeId)
                : FindCard(source.SourceCardInstanceId);
            if (source.IsBird && flock == null)
                flock = _flocks.Find(item => item.CardId == source.SourceCardInstanceId &&
                    item.CastRuntimeId == 0 && item.SourceIds.Count == 0);
            if (flock == null)
            {
                flock = new Flock { CardId = source.SourceCardInstanceId, Profile = profile, InitialPlayed = true };
                flock.SkyFallback = CaptureSkyCenter(profile.fireCrowHoverHeight);
                _flocks.Add(flock);
                Resize(flock, source.Snapshot, CaptureSkyCenter(profile.fireCrowHoverHeight), false,
                    source.IsBird ? source.BirdCount : 0);
            }
            if (source.IsBird) flock.CastRuntimeId = source.CastRuntimeId;
            Flock previous = FindSource(source.EffectRuntimeId);
            if (previous != null && previous != flock)
            {
                previous.SourceIds.Remove(source.EffectRuntimeId);
                previous.Persistent = previous.SourceIds.Count > 0;
                TryRetire(previous);
            }
            flock.SourceIds.Add(source.EffectRuntimeId);
            flock.Persistent = true;
        }
        private void Resize(Flock flock, AttackPlan plan, Vector3 origin, bool takeoff, int birdCount = 0)
        {
            var indices = new SortedSet<int>();
            int residents = birdCount > 0 ? birdCount : plan.ResidentBirdCount;
            if (residents > 0)
                for (int i = 0; i < residents; i++) indices.Add(i);
            else
                foreach (AttackSubHit hit in plan.SubHits) indices.Add(hit.AttackInstanceIndex);
            foreach (Bird bird in flock.Birds) bird.Visual.Dispose();
            flock.Birds.Clear();
            int ordinal = 0;
            foreach (int index in indices)
            {
                IPeriodicAttackerVisual visual = flock.Profile.periodicAttackerVisualKind ==
                    PeriodicAttackerVisualKind.ClockworkBee
                    ? new ClockworkBeeVisual(transform, flock.Profile)
                    : (IPeriodicAttackerVisual)new FireCrowVisual(transform, flock.Profile);
                visual.SetPose(origin, true);
                flock.Birds.Add(new Bird { AttackIndex = index, Visual = visual, Origin = origin,
                    Age = takeoff ? -Mathf.Min(ordinal++ * .015f, .20f) : TakeoffDuration });
            }
        }
        private Flock FindCard(int id) => _flocks.FindLast(flock => flock.CardId == id);
        private Flock FindSource(int id) => _flocks.Find(flock => flock.SourceIds.Contains(id));

        // The bridge hands off the source inside Triggered publication, only
        // when it has created a matching impact presentation to consume it.
        internal void QueuePeriodicAttack(PeriodicAttackSource source)
        {
            if (!isActiveAndEnabled || source == null) return;
            Flock flock = FindSource(source.EffectRuntimeId);
            if (flock == null) return;
            flock.QueuedVolleys++;
            _periodicVolleys.Add(new PeriodicVolley {
                Source = source, Flock = flock, Generation = _generation });
        }

        internal void DiscardPeriodicAttack(PeriodicAttackSource source)
        {
            PeriodicVolley volley = _periodicVolleys.Find(item =>
                ReferenceEquals(item.Source, source) && !item.Claimed);
            if (volley != null) ReleasePeriodicVolley(volley);
        }

        private void ReleasePeriodicVolley(PeriodicVolley volley)
        {
            bool removed = _periodicVolleys.Remove(volley);
            volley.Completed = true;
            if (volley.HoldsFlock)
            {
                volley.HoldsFlock = false;
                volley.Flock.ActiveVolleys--;
            }
            // Cancel resets queued counts. A late old iterator must not
            // consume a newly queued volley on a retained persistent flock.
            if (removed && volley.Generation == _generation)
                volley.Flock.QueuedVolleys = Mathf.Max(0, volley.Flock.QueuedVolleys - 1);
            volley.Arrivals.Clear();
            volley.Sequences = null;
            volley.OnArrive = null;
            volley.OnFinished = null;
            TryRetire(volley.Flock);
        }
        private void Update()
        {
            if (_manager == null || _combat == null) { Clear(); return; }
            float delta = Time.deltaTime;
            _time += delta;
            int slot = 0;
            foreach (Flock flock in _flocks)
            {
                Vector3 center = CaptureSkyCenter(flock.Profile.fireCrowHoverHeight, flock.SkyFallback);
                float radius = Mathf.Max(1.4f, flock.Profile.fireCrowOrbitWidth * 2f);
                // The formation view owns board geometry. Bootstrap supplies a
                // value-only query so patrol width never depends on survivors.
                Bounds? board = _boardBounds?.Invoke();
                if (board.HasValue && board.Value.size.x > 0f)
                {
                    center.x = board.Value.center.x;
                    radius = board.Value.extents.x * .95f;
                }
                for (int i = 0; i < flock.Birds.Count; i++, slot++)
                {
                    Bird bird = flock.Birds[i];
                    bird.Age += delta;
                    float phase = _time * Mathf.Max(.05f, flock.Profile.fireCrowOrbitSpeed) * Mathf.PI * 2f + slot * 1.7f;
                    Vector3 sky = center + new Vector3(
                        Mathf.Sin(phase) * radius,
                        (slot % 3 - 1) * .12f + Mathf.Sin(phase * 2f) * .12f, 0f);
                    sky = ClampInsideCamera(sky);
                    float t = Mathf.Clamp01(bird.Age / TakeoffDuration);
                    float eased = t * t * (3f - 2f * t);
                    Vector3 position = Vector3.Lerp(bird.Origin, sky, eased) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .45f);
                    bird.Visual.SetPose(position, Mathf.Cos(phase) >= 0f);
                    bird.Visual.Tick(delta, _time + i * .2f);
                }
            }
        }
        private Vector3 CaptureSkyCenter(float clearance, Vector3? capturedFallback = null)
        {
            float left = float.PositiveInfinity, right = float.NegativeInfinity, top = float.NegativeInfinity;
            Vector3 fallback = _manager.playerAttackOrigin != null ? _manager.playerAttackOrigin.position + Vector3.up * 3f : Vector3.up * 3f;
            if (capturedFallback.HasValue) fallback = capturedFallback.Value;
            for (int i = 0; i < _combat.Enemies.Count; i++)
            {
                EnemyStats enemy = _combat.Enemies[i];
                if (enemy == null || !EnemyFormationLayout.IsFullyInsideVisibleRows(enemy)) continue;
                Transform target = _manager.GetEnemyTransform(i);
                SpriteRenderer sprite = target != null ? target.GetComponent<SpriteRenderer>() : null;
                if (sprite == null || sprite.sprite == null || !sprite.enabled || !sprite.gameObject.activeInHierarchy || sprite.color.a <= 0f) continue;
                left = Mathf.Min(left, sprite.bounds.center.x); right = Mathf.Max(right, sprite.bounds.center.x);
                top = Mathf.Max(top, sprite.bounds.max.y); fallback.z = sprite.bounds.center.z;
            }
            return float.IsNegativeInfinity(top) ? fallback : new Vector3((left + right) * .5f, top + clearance, fallback.z);
        }
        private Vector3 ClampInsideCamera(Vector3 position)
        {
            if (_camera == null) return position;
            Vector3 viewport = _camera.WorldToViewportPoint(position);
            viewport.x = Mathf.Clamp(viewport.x, .02f, .98f);
            viewport.y = Mathf.Clamp(viewport.y, .52f, .84f);
            return _camera.ViewportToWorldPoint(viewport);
        }
        internal IEnumerator PlayInitialAttack(int cardId, IReadOnlyList<AuthoritativeProjectileSequence> sequences,
            VfxProfile profile, Action<AuthoritativeProjectileHit> onArrive,
            AttackEffectCompletion completion = null)
        {
            Flock flock = _flocks.Find(item => item.CardId == cardId && !item.InitialPlayed);
            if (flock == null) yield break;
            flock.InitialPlayed = true;
            yield return PlayVolley(flock, sequences, profile, onArrive, completion);
        }
        internal IEnumerator PlayPeriodicAttack(PeriodicAttackSource source, IReadOnlyList<AuthoritativeProjectileSequence> sequences,
            VfxProfile profile, Action<AuthoritativeProjectileHit> onArrive, Action<bool> onFinished,
            AttackEffectCompletion completion = null)
        {
            PeriodicVolley volley = _periodicVolleys.Find(item =>
                ReferenceEquals(item.Source, source) && !item.Claimed);
            if (volley == null) yield break;
            volley.Claimed = true;
            volley.Profile = profile;
            volley.Sequences = sequences;
            volley.OnArrive = onArrive;
            volley.OnFinished = onFinished;
            volley.Completion = completion;
            volley.HoldsFlock = true;
            volley.Flock.ActiveVolleys++;
            if (_periodicPlayback == null)
                _periodicPlayback = StartCoroutine(PlayPeriodicBatches(_generation));
            try
            {
                while (volley.Generation == _generation && _manager != null && !volley.Completed &&
                    (completion == null || !completion.IsCancelled))
                    yield return null;
            }
            finally
            {
                ReleasePeriodicVolley(volley);
            }
        }
        private IEnumerator PlayPeriodicBatches(int generation)
        {
            try
            {
                // Triggered is synchronous; the bridge claims its recorded
                // sequences next frame. Gather that complete publication batch.
                yield return null;
                while (generation == _generation && _manager != null && _periodicVolleys.Count > 0)
                {
                    int turn = _periodicVolleys[0].Source.LastTriggeredTurn;
                    var batch = new List<PeriodicVolley>();
                    foreach (PeriodicVolley volley in _periodicVolleys)
                    {
                        if (volley.Source.LastTriggeredTurn != turn) break;
                        batch.Add(volley);
                    }
                    while (generation == _generation && _manager != null &&
                        batch.Exists(volley => !volley.Completed && (!volley.Claimed ||
                            volley.Flock.Birds.Exists(bird => bird.Age < TakeoffDuration))))
                        yield return null;
                    if (generation != _generation || _manager == null) yield break;

                    List<PeriodicLaunch> launches = BuildPeriodicLaunches(batch);
                    float startedAt = _time;
                    int nextLaunch = 0, nextVolley = 0;
                    while (generation == _generation && _manager != null && nextVolley < batch.Count)
                    {
                        if (Time.timeScale > 0f)
                        {
                            float elapsed = _time - startedAt;
                            // Catch up all slots due this frame rather than
                            // imposing a minimum one-frame wait per projectile.
                            while (generation == _generation && _manager != null && Time.timeScale > 0f &&
                                nextLaunch < launches.Count && launches[nextLaunch].Offset <= elapsed)
                            {
                                PeriodicLaunch launch = launches[nextLaunch++];
                                if (launch.Volley.Completed ||
                                    (launch.Volley.Completion != null && launch.Volley.Completion.IsCancelled)) continue;
                                StartCoroutine(PlayChain(launch.Bird, launch.Sequence, launch.Volley.Profile,
                                    generation, hit => RecordPeriodicArrival(launch, hit, generation), () => { }, launch.Volley.Completion));
                            }
                            DrainPeriodicArrivals(batch, ref nextVolley, generation);
                        }
                        if (nextVolley < batch.Count) yield return null;
                    }
                }
            }
            finally
            {
                if (generation == _generation)
                {
                    foreach (PeriodicVolley volley in _periodicVolleys.ToArray()) ReleasePeriodicVolley(volley);
                    _periodicPlayback = null;
                }
            }
        }
        private List<PeriodicLaunch> BuildPeriodicLaunches(List<PeriodicVolley> batch)
        {
            var launches = new List<PeriodicLaunch>();
            float span = 0f;
            foreach (PeriodicVolley volley in batch)
            {
                if (volley.Completed || volley.Sequences == null) continue;
                foreach (AuthoritativeProjectileSequence sequence in volley.Sequences)
                {
                    if (sequence.Hits.Count == 0) continue;
                    Bird bird = volley.Flock.Birds.Find(item =>
                        item.AttackIndex == sequence.Hits[0].DamageEvent.AttackInstanceIndex);
                    if (bird == null) continue;
                    var launch = new PeriodicLaunch { Volley = volley, Bird = bird, Sequence = sequence, Offset = span };
                    foreach (AuthoritativeProjectileHit hit in sequence.Hits)
                    {
                        var arrival = new PeriodicArrival { Hit = hit };
                        volley.Arrivals.Add(arrival);
                        launch.Arrivals.Add(hit.PendingHitIndex, arrival);
                    }
                    launches.Add(launch);
                    span += Mathf.Max(0f, volley.Profile.multiProjectileDelay);
                }
                // Index order is the actual receipt order, including bounce
                // chains whose physical arrival may overtake other direct hits.
                volley.Arrivals.Sort((left, right) => left.Hit.PendingHitIndex.CompareTo(right.Hit.PendingHitIndex));
            }
            float finalOffset = launches.Count > 0 ? launches[launches.Count - 1].Offset : 0f;
            float compression = finalOffset > PeriodicLaunchWindow ? PeriodicLaunchWindow / finalOffset : 1f;
            foreach (PeriodicLaunch launch in launches) launch.Offset *= compression;
            return launches;
        }
        private void RecordPeriodicArrival(PeriodicLaunch launch, AuthoritativeProjectileHit hit, int generation)
        {
            if (generation == _generation && !launch.Volley.Completed &&
                launch.Arrivals.TryGetValue(hit.PendingHitIndex, out PeriodicArrival arrival))
                arrival.Arrived = true;
        }
        private void DrainPeriodicArrivals(List<PeriodicVolley> batch, ref int nextVolley, int generation)
        {
            while (generation == _generation && _manager != null && Time.timeScale > 0f && nextVolley < batch.Count)
            {
                PeriodicVolley volley = batch[nextVolley];
                if (volley.Completed) { nextVolley++; continue; }
                if (volley.Completion != null && volley.Completion.IsCancelled)
                {
                    ReleasePeriodicVolley(volley);
                    nextVolley++;
                    continue;
                }
                while (generation == _generation && _manager != null && !volley.Completed && Time.timeScale > 0f &&
                    volley.NextArrival < volley.Arrivals.Count && volley.Arrivals[volley.NextArrival].Arrived)
                {
                    PeriodicArrival arrival = volley.Arrivals[volley.NextArrival++];
                    volley.OnArrive?.Invoke(arrival.Hit);
                }
                if (generation != _generation || Time.timeScale <= 0f ||
                    volley.NextArrival < volley.Arrivals.Count) return;
                // Complete this impact source synchronously before delivering
                // any later source. The bridge flushes splash/terrain and closes
                // fatal-hit holds here, so enemy actions keep their old barrier.
                volley.OnFinished?.Invoke(nextVolley == batch.Count - 1);
                ReleasePeriodicVolley(volley);
                nextVolley++;
            }
        }
        private IEnumerator PlayVolley(Flock flock, IReadOnlyList<AuthoritativeProjectileSequence> sequences,
            VfxProfile profile, Action<AuthoritativeProjectileHit> onArrive,
            AttackEffectCompletion completion)
        {
            int generation = _generation;
            flock.ActiveVolleys++;
            try
            {
                while (generation == _generation && flock.Birds.Exists(bird => bird.Age < TakeoffDuration) &&
                    (completion == null || !completion.IsCancelled)) yield return null;
                if (generation != _generation || _manager == null ||
                    (completion != null && completion.IsCancelled)) yield break;
                int launched = 0, completed = 0;
                foreach (AuthoritativeProjectileSequence sequence in sequences)
                {
                    if (generation != _generation || _manager == null ||
                        (completion != null && completion.IsCancelled)) yield break;
                    if (sequence.Hits.Count == 0) continue;
                    Bird bird = flock.Birds.Find(item => item.AttackIndex == sequence.Hits[0].DamageEvent.AttackInstanceIndex);
                    if (bird == null) continue;
                    launched++;
                    StartCoroutine(PlayChain(bird, sequence, profile, generation, onArrive, () => completed++, completion));
                    if (profile.multiProjectileDelay > 0f) yield return new WaitForSeconds(profile.multiProjectileDelay);
                }
                while (generation == _generation && _manager != null && completed < launched &&
                    (completion == null || !completion.IsCancelled)) yield return null;
            }
            finally { flock.ActiveVolleys--; TryRetire(flock); }
        }
        private IEnumerator PlayChain(Bird bird, AuthoritativeProjectileSequence sequence, VfxProfile profile,
            int generation, Action<AuthoritativeProjectileHit> onArrive, Action onComplete,
            AttackEffectCompletion completion)
        {
            try
            {
                bird.Visual.Pulse();
                Vector3 start = bird.Visual.ProjectileOrigin;
                foreach (AuthoritativeProjectileHit hit in sequence.Hits)
                {
                    if (generation != _generation || _manager == null ||
                        (completion != null && completion.IsCancelled)) yield break;
                    bool arrived = false;
                    _manager.FireProjectileBetweenPositions(start, hit.Position, profile, hit.DamageEvent.Kind == DamageKind.Bounce,
                        () => { if (generation == _generation && (completion == null || !completion.IsCancelled)) onArrive?.Invoke(hit); arrived = true; }, completion);
                    while (generation == _generation && _manager != null && !arrived &&
                        (completion == null || !completion.IsCancelled)) yield return null;
                    start = hit.Position;
                }
            }
            finally { onComplete(); }
        }
        private void TryRetire(Flock flock)
        {
            if (flock.Persistent || !flock.InitialPlayed || flock.QueuedVolleys > 0 || flock.ActiveVolleys > 0) return;
            foreach (Bird bird in flock.Birds) bird.Visual.Dispose();
            flock.Birds.Clear(); _flocks.Remove(flock);
        }
        public void CancelFlights()
        {
            _generation++;
            StopPeriodicPlayback();
            _origins.Clear();
            _preparedSkies.Clear();
            foreach (Flock flock in _flocks.ToArray())
            {
                flock.InitialPlayed = true;
                flock.QueuedVolleys = 0;
                foreach (Bird bird in flock.Birds) bird.Age = TakeoffDuration;
                TryRetire(flock);
            }
        }
        private void Unsubscribe()
        {
            if (_subscribed && _combat != null)
            {
                _combat.OnPeriodicAttackPlanResolved -= PrimeInitial;
                _combat.OnPeriodicAttackChanged -= Present;
            }
            _subscribed = false;
        }
        private void Clear()
        {
            _generation++;
            StopPeriodicPlayback();
            foreach (Flock flock in _flocks)
            {
                foreach (Bird bird in flock.Birds) bird.Visual.Dispose();
                flock.Birds.Clear();
            }
            _flocks.Clear(); _origins.Clear(); _preparedSkies.Clear();
        }
        private void StopPeriodicPlayback()
        {
            if (_periodicPlayback != null) StopCoroutine(_periodicPlayback);
            _periodicPlayback = null;
            foreach (PeriodicVolley volley in _periodicVolleys.ToArray()) ReleasePeriodicVolley(volley);
        }
        private void OnDisable() { Unsubscribe(); Clear(); }
        private void OnDestroy() { Unsubscribe(); Clear(); }
    }
}
