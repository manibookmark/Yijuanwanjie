using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Owns one continuous dragon per recorded attack instance and one playback
    /// clock for the cast. Core's Direct/Splash receipts define contacts; neither
    /// overlap nor the dragon's body can select an enemy or cause damage.
    /// A Direct opens a contact and only its following Splash receipts belong
    /// to that contact, even when all five Directs share one SubHitIndex.
    /// The attack queue alone owns receipt deduplication and source completion.
    /// </summary>
    public sealed class AuthoritativeWaterDragonSweepPlayer : MonoBehaviour
    {
        private const float SweepDuration = 0.72f;
        private const float FadeDuration = 0.18f;
        private const float FrontDepth = 1f / 6f;
        private const float MouthToTailLength = 1.49f;
        private const int RowOverlayOffset = 6;

        private sealed class Cast
        {
            internal readonly List<Dragon> Dragons;
            internal bool Released;
            internal Cast(List<Dragon> dragons) { Dragons = dragons; }
        }

        private readonly HashSet<Cast> _casts = new HashSet<Cast>();

        internal static IEnumerator Play(MonoBehaviour host, VisualEffectManager manager,
            CombatAttackPresentationQueue.Context context, BoardVisualFrame? board,
            Func<bool> active, Action<AuthoritativeProjectileHit> onContact)
        {
            if (host == null || manager == null || context?.Profile == null || active == null || !active())
                yield break;
            var owner = host.GetComponent<AuthoritativeWaterDragonSweepPlayer>() ??
                host.gameObject.AddComponent<AuthoritativeWaterDragonSweepPlayer>();
            yield return owner.Animate(manager, context, board, active, onContact);
        }

        private IEnumerator Animate(VisualEffectManager manager,
            CombatAttackPresentationQueue.Context context, BoardVisualFrame? board,
            Func<bool> active, Action<AuthoritativeProjectileHit> onContact)
        {
            BoardVisualFrame frame = board ?? new BoardVisualFrame(
                new Vector3(-4.6f, 0f, 0f), new Vector3(4.6f, 0f, 0f),
                new Vector3(-3.2f, 3f, 0f), new Vector3(3.2f, 3f, 0f));
            List<Dragon> dragons = BuildDragons(context, frame);
            var cast = new Cast(dragons);
            _casts.Add(cast);
            float launchDelay = Mathf.Max(0f, context.Profile.multiProjectileDelay);
            float nextBirth = 0f, previousLastContact = 0f;
            bool hasPreviousContact = false;
            foreach (Dragon dragon in dragons)
            {
                if (dragon.Finished) continue;
                dragon.Birth = nextBirth;
                if (hasPreviousContact)
                    dragon.Birth = Mathf.Max(dragon.Birth,
                        previousLastContact + 0.02f - dragon.Contacts[0].Time);
                // Entry/exit may overlap, but a later dragon cannot display
                // damage behind a boss before the preceding contact kills it.
                previousLastContact = dragon.Birth + dragon.Contacts[dragon.Contacts.Count - 1].Time;
                hasPreviousContact = true;
                nextBirth = dragon.Birth + launchDelay;
            }
            double startedAt = Time.timeAsDouble;
            try
            {
                while (!cast.Released && isActiveAndEnabled && manager != null &&
                    manager.isActiveAndEnabled && active())
                {
                    bool unfinished = false;
                    bool faulted = false;
                    try
                    {
                        float elapsed = (float)(Time.timeAsDouble - startedAt);
                        for (int i = 0; i < dragons.Count && !cast.Released && isActiveAndEnabled && active(); i++)
                        {
                            Dragon dragon = dragons[i];
                            if (elapsed >= dragon.Birth)
                                dragon.Advance(manager, context.Profile, frame,
                                    elapsed - dragon.Birth, active, onContact, context.EffectsCompletion);
                            unfinished |= !dragon.Finished;
                        }
                    }
                    catch (Exception exception)
                    {
                        // Cleanup then return normally. The bridge flushes any
                        // undisplayed authority receipts and retires the source.
                        Debug.LogException(exception);
                        faulted = true;
                    }
                    if (faulted || !unfinished) break;
                    yield return null;
                }
            }
            finally { Release(cast); }
        }

        public void CancelAll()
        {
            foreach (Cast cast in new List<Cast>(_casts)) Release(cast);
        }

        private void OnDisable() => CancelAll();
        private void OnDestroy() => CancelAll();

        private void Release(Cast cast)
        {
            if (cast.Released) return;
            cast.Released = true;
            _casts.Remove(cast);
            foreach (Dragon dragon in cast.Dragons)
            {
                try { dragon.Dispose(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private static List<Dragon> BuildDragons(CombatAttackPresentationQueue.Context context,
            BoardVisualFrame frame)
        {
            var groups = new SortedDictionary<int, Dragon>();
            // A victory can truncate damage publication. The full plan still
            // supplies the real dragon count; current buffs never supply it.
            if (context.AttackPlan != null)
                foreach (AttackSubHit subHit in context.AttackPlan.SubHits)
                    if (!groups.ContainsKey(subHit.AttackInstanceIndex))
                        groups.Add(subHit.AttackInstanceIndex, new Dragon(subHit.AttackInstanceIndex));

            var openContacts = new Dictionary<int, Contact>();
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> receipts = context.Hits;
            for (int i = 0; i < receipts.Count; i++)
            {
                var pending = receipts[i];
                DamageEvent damage = pending.damageEvent;
                if ((damage.Kind != DamageKind.Direct && damage.Kind != DamageKind.Splash) ||
                    (context.CardInstanceId != 0 && damage.SourceCardInstanceId != 0 &&
                     context.CardInstanceId != damage.SourceCardInstanceId)) continue;
                if (!groups.TryGetValue(damage.AttackInstanceIndex, out Dragon dragon))
                {
                    // Compatibility sources may have receipts but no plan.
                    // Never invent additional objects beyond a present plan.
                    if (context.AttackPlan != null || damage.Kind != DamageKind.Direct) continue;
                    groups.Add(damage.AttackInstanceIndex, dragon = new Dragon(damage.AttackInstanceIndex));
                }
                var hit = new AuthoritativeProjectileHit(damage, pending.worldAnchor, i);
                if (damage.Kind == DamageKind.Direct)
                {
                    var contact = new Contact(hit, pending.hasWorldAnchor);
                    dragon.Contacts.Add(contact);
                    openContacts[damage.AttackInstanceIndex] = contact;
                }
                else if (openContacts.TryGetValue(damage.AttackInstanceIndex, out Contact contact) &&
                    contact.Direct.DamageEvent.SubHitIndex == damage.SubHitIndex)
                {
                    // Event order, not the shared SubHitIndex, separates the
                    // five contacts. Splash changes feedback, never the route.
                    contact.Receipts.Add(hit);
                }
            }
            var result = new List<Dragon>(groups.Values);
            foreach (Dragon dragon in result) dragon.BuildRoute(frame);
            return result;
        }

        private sealed class Contact
        {
            internal readonly AuthoritativeProjectileHit Direct;
            internal readonly bool Visible;
            internal readonly List<AuthoritativeProjectileHit> Receipts;
            internal float Time;
            internal Contact(AuthoritativeProjectileHit direct, bool visible)
            {
                Direct = direct;
                Visible = visible;
                Receipts = new List<AuthoritativeProjectileHit> { direct };
            }
        }

        private readonly struct Node
        {
            internal readonly Vector3 Position;
            internal readonly float Time, Depth;
            internal Node(Vector3 position, float time, float depth)
            { Position = position; Time = time; Depth = depth; }
        }

        // This batch is the sole writer of roots, pose, opacity and progress.
        // It starts no child coroutines and retains no resident dragon objects.
        private sealed class Dragon
        {
            internal readonly List<Contact> Contacts = new List<Contact>();
            internal bool Finished { get; private set; }
            internal float Birth;
            private readonly int _attackIndex;
            private readonly List<Node> _route = new List<Node>();
            private GameObject _root;
            private ProceduralWaterDragonVisual _visual;
            private UnityEngine.Rendering.SortingGroup _sorting;
            private LineRenderer[] _lines;
            private Color[] _colors;
            private int _createdFrame, _nextContact;
            private float _scale;
            private bool _ready, _disposed, _hasVisibleContacts;

            internal Dragon(int attackIndex) { _attackIndex = attackIndex; }

            internal void BuildRoute(BoardVisualFrame frame)
            {
                if (Contacts.Count == 0) { Finished = true; return; }
                float depth = FrontDepth;
                Vector3 first = frame.Project(0f, depth);
                foreach (Contact contact in Contacts)
                    if (contact.Visible)
                    {
                        _hasVisibleContacts = true;
                        first = contact.Direct.Position;
                        depth = EstimateDepth(frame, first);
                        break;
                    }
                // A plan instance is a rule budget. Without visible Direct
                // contacts it must not swim through actors without attacking.
                if (!_hasVisibleContacts) return;
                Vector3 left = frame.Project(0f, depth);
                Vector3 right = frame.Project(1f, depth);
                Vector3 rowAxis = (right - left).normalized;
                Vector3 entry = left - rowAxis * 0.2f;
                entry.y = first.y;
                _route.Add(new Node(entry, 0f, depth));
                float previousTime = 0f;
                foreach (Contact contact in Contacts)
                {
                    if (!contact.Visible)
                    {
                        // A hidden receipt still reaches the queue exactly
                        // once, but supplies no invented contact or particles.
                        contact.Time = previousTime;
                        continue;
                    }
                    Vector3 position = contact.Direct.Position;
                    depth = EstimateDepth(frame, position);
                    left = frame.Project(0f, depth);
                    right = frame.Project(1f, depth);
                    Vector3 axis = right - left;
                    float across = axis.sqrMagnitude > 0.00001f
                        ? Mathf.Clamp01(Vector3.Dot(position - left, axis) / axis.sqrMagnitude) : 0.5f;
                    contact.Time = Mathf.Max(previousTime, 0.06f + across * 0.60f);
                    _route.Add(new Node(position, contact.Time, depth));
                    previousTime = contact.Time;
                }
                Node last = _route[_route.Count - 1];
                right = frame.Project(1f, last.Depth);
                rowAxis = (right - frame.Project(0f, last.Depth)).normalized;
                Vector3 exit = right + rowAxis * 0.55f;
                exit.y = last.Position.y;
                _route.Add(new Node(exit, SweepDuration, last.Depth));
            }

            internal void Advance(VisualEffectManager manager, VfxProfile profile, BoardVisualFrame frame,
                float age, Func<bool> active, Action<AuthoritativeProjectileHit> onContact,
                AttackEffectCompletion completion)
            {
                if (Finished) return;
                if (!_hasVisibleContacts)
                {
                    foreach (Contact contact in Contacts)
                        foreach (AuthoritativeProjectileHit hit in contact.Receipts)
                        {
                            if (!active()) return;
                            onContact?.Invoke(hit);
                        }
                    Finished = true;
                    return;
                }
                if (_root == null)
                {
                    _root = new GameObject($"AuthorityWaterDragonSweep-{_attackIndex}", typeof(SpriteRenderer));
                    _root.transform.SetParent(manager.projectileContainer ?? manager.transform, false);
                    _root.transform.position = _route[0].Position;
                    _createdFrame = UnityEngine.Time.frameCount;
                    return;
                }
                if (!_ready)
                {
                    if (UnityEngine.Time.frameCount <= _createdFrame) return;
                    if (!_root.activeInHierarchy) { Finished = true; Dispose(); return; }
                    _sorting = _root.AddComponent<UnityEngine.Rendering.SortingGroup>();
                    _visual = new ProceduralWaterDragonVisual();
                    _visual.Initialize(_root, profile, profile.projectileScale);
                    _visual.OnFlightStart();
                    float width = Vector3.Distance(frame.Project(0f, FrontDepth), frame.Project(1f, FrontDepth));
                    _scale = width * 0.12f * Mathf.Clamp(profile.projectileScale, 0.1f, 3f) / MouthToTailLength;
                    _lines = _root.GetComponentsInChildren<LineRenderer>();
                    _colors = new Color[_lines.Length];
                    for (int i = 0; i < _lines.Length; i++) _colors[i] = _lines[i].startColor;
                    _ready = true;
                }
                if (!_root.activeInHierarchy) { Finished = true; Dispose(); return; }
                while (_nextContact < Contacts.Count && Contacts[_nextContact].Time <= age && active())
                {
                    Contact contact = Contacts[_nextContact++];
                    if (contact.Visible)
                    {
                        SetPose(frame, contact.Direct.Position, EstimateDepth(frame, contact.Direct.Position));
                        // The bridge supplies the primary impact/ripple once;
                        // this supplies only the contact sound/secondary layer.
                        manager.PlaySmallHitEffect(contact.Direct.Position, profile, completion: completion);
                    }
                    foreach (AuthoritativeProjectileHit hit in contact.Receipts)
                    {
                        if (!active()) break;
                        onContact?.Invoke(hit);
                    }
                }
                if (_disposed || _root == null || !active()) return;
                SamplePose(frame, Mathf.Min(age, SweepDuration));
                _visual.Sample(age);
                float alpha = Mathf.SmoothStep(0f, 1f, age / 0.08f) *
                    Mathf.Clamp01(1f - (age - SweepDuration) / FadeDuration);
                for (int i = 0; i < _lines.Length; i++)
                {
                    Color color = _colors[i]; color.a *= alpha;
                    _lines[i].startColor = _lines[i].endColor = color;
                }
                if (age >= SweepDuration + FadeDuration) { Finished = true; Dispose(); }
            }

            private void SamplePose(BoardVisualFrame frame, float age)
            {
                int fromIndex = 0;
                while (fromIndex + 1 < _route.Count && _route[fromIndex + 1].Time <= age) fromIndex++;
                Node from = _route[fromIndex], to = _route[Mathf.Min(fromIndex + 1, _route.Count - 1)];
                float interval = to.Time - from.Time;
                float progress = interval > 0f ? Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((age - from.Time) / interval)) : 1f;
                SetPose(frame, Vector3.Lerp(from.Position, to.Position, progress),
                    Mathf.Lerp(from.Depth, to.Depth, progress));
            }

            private void SetPose(BoardVisualFrame frame, Vector3 position, float depth)
            {
                _root.transform.position = position;
                _root.transform.rotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one * _scale;
                _sorting.sortingOrder = frame.SortingOrderAtDepth(depth) + RowOverlayOffset;
            }

            internal void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Finished = true;
                try { _visual?.OnFlightEnd(); }
                finally
                {
                    try { _visual?.Cleanup(); }
                    finally { if (_root != null) UnityEngine.Object.Destroy(_root); }
                }
            }
        }

        private static float EstimateDepth(BoardVisualFrame frame, Vector3 position)
        {
            Vector3 front = frame.Project(0.5f, 0f);
            Vector3 span = frame.Project(0.5f, 1f) - front;
            return span.sqrMagnitude > 0.00001f
                ? Mathf.Clamp01(Vector3.Dot(position - front, span) / span.sqrMagnitude) : FrontDepth;
        }
    }
}
