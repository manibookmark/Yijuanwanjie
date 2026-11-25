using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Owns transient short swords, their receipt-backed routes and cleanup.
    /// Each authority attack index creates one sword even when it hits the same
    /// enemy twice. Return movement never targets, damages or publishes a hit.
    /// One coroutine samples the entire cast; no child coroutine owns a blade.
    /// </summary>
    public sealed class AuthoritativeReturningBladePlayer : MonoBehaviour
    {
        public const string VisualTypeName = "ReturningBlade";
        private const string SpriteResource = "Sprites/Cards/Cutout/huixuanjian-v1";
        private const float LiftDuration = 0.10f;
        private const float ReturnDuration = 0.24f;
        // Anchors describe this approved sprite's actual straight metal sword,
        // excluding its painted air ribbon and transparent padding.
        private static readonly Vector2 TipUv = new Vector2(0.930f, 0.930f);
        private static readonly Vector2 PommelUv = new Vector2(0.155f, 0.145f);

        private sealed class Contact
        {
            internal readonly List<AuthoritativeProjectileHit> Receipts = new List<AuthoritativeProjectileHit>();
            internal Vector3 Position;
            internal float At;
        }

        private sealed class Blade
        {
            internal int AttackIndex;
            internal readonly List<Contact> Contacts = new List<Contact>();
            internal GameObject Root;
            internal SpriteRenderer Sprite;
            internal TrailRenderer Trail;
            internal Vector3 Origin, Elevated;
            internal float Birth, End;
        }

        private sealed class Cast
        {
            internal readonly List<Blade> Blades = new List<Blade>();
            internal bool Released;
        }

        private readonly HashSet<Cast> _casts = new HashSet<Cast>();

        internal static IEnumerator Play(MonoBehaviour host, VisualEffectManager manager,
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> receipts, VfxProfile profile,
            BoardVisualFrame? board, Vector3? castOrigin, AttackPlan plan,
            Func<bool> active, Action<AuthoritativeProjectileHit> onContact,
            AttackEffectCompletion completion = null)
        {
            if (host == null || manager == null || profile == null || !active()) yield break;
            var owner = host.GetComponent<AuthoritativeReturningBladePlayer>() ??
                host.gameObject.AddComponent<AuthoritativeReturningBladePlayer>();
            yield return owner.Animate(manager, receipts, profile, board, castOrigin,
                plan, active, onContact, completion);
        }

        private IEnumerator Animate(VisualEffectManager manager,
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> receipts, VfxProfile profile,
            BoardVisualFrame? board, Vector3? castOrigin, AttackPlan plan,
            Func<bool> active, Action<AuthoritativeProjectileHit> onContact,
            AttackEffectCompletion completion)
        {
            if (plan == null || plan.SubHits.Count == 0)
            {
                Debug.LogError("回旋剑缺少Core已选定的完整攻击计划；不从部分命中推测攻击物数量。");
                yield break;
            }
            Cast cast = BuildCast(receipts, plan);
            if (cast.Blades.Count == 0) yield break;
            Sprite sprite = profile.projectileSprite != null ? profile.projectileSprite : Resources.Load<Sprite>(SpriteResource);
            if (sprite == null)
            {
                Debug.LogError("回旋剑缺少已批准的短剑Sprite；不生成替代轮廓。");
                yield break;
            }
            Vector3 origin = castOrigin ?? manager.playerAttackOrigin?.position ?? Vector3.zero;
            Vector3 elevated = ResolveElevatedOrigin(origin, profile);
            // This is cosmetic launch spacing only. The count is a Core plan
            // fact, including attack objects whose targets have already died.
            float multiplier = 0.1f + 0.9f * Mathf.Pow(0.815f, plan.SubHits.Count - 1);
            float birth = 0f;
            var arrivals = new List<(Blade blade, Contact contact)>();
            foreach (Blade blade in cast.Blades)
            {
                blade.Origin = origin; blade.Elevated = elevated;
                blade.Birth = birth;
                float at = birth + LiftDuration;
                foreach (Contact contact in blade.Contacts)
                {
                    at += Mathf.Max(0.08f, profile.flightDuration);
                    contact.At = at;
                    arrivals.Add((blade, contact));
                }
                blade.End = at + ReturnDuration;
                birth += UnityEngine.Random.Range(0.125f, 0.175f) * multiplier;
            }
            arrivals.Sort((a, b) =>
            {
                int time = a.contact.At.CompareTo(b.contact.At);
                return time != 0 ? time : a.blade.AttackIndex.CompareTo(b.blade.AttackIndex);
            });
            _casts.Add(cast);
            double started = Time.timeAsDouble;
            int arrived = 0;
            try
            {
                while (!cast.Released && isActiveAndEnabled && active())
                {
                    float elapsed = (float)(Time.timeAsDouble - started);
                    bool unfinished = false;
                    bool faulted = false;
                    try
                    {
                        foreach (Blade blade in cast.Blades)
                        {
                            SampleBlade(blade, elapsed, sprite, profile, board, manager.projectileContainer);
                            unfinished |= elapsed < blade.End;
                        }
                        // Catch up due contacts on a slow frame. Receipt storage
                        // and exactly-once presentation remain in the queue.
                        while (arrived < arrivals.Count && arrivals[arrived].contact.At <= elapsed && active())
                        {
                            Contact contact = arrivals[arrived++].contact;
                            foreach (AuthoritativeProjectileHit hit in contact.Receipts)
                            {
                                if (!active()) break;
                                if (hit.DamageEvent.Kind == DamageKind.Direct || hit.DamageEvent.Kind == DamageKind.Bounce)
                                    manager.PlaySmallHitEffect(hit.Position, profile, completion: completion);
                                onContact?.Invoke(hit);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        faulted = true;
                    }
                    if (faulted || !unfinished) break;
                    yield return null;
                }
            }
            finally { Release(cast); }
        }

        private static Cast BuildCast(IReadOnlyList<CombatAttackPresentationQueue.PendingHit> receipts, AttackPlan plan)
        {
            var result = new Cast();
            var groups = new SortedDictionary<int, Blade>();
            foreach (AttackSubHit subHit in plan.SubHits)
                if (!groups.ContainsKey(subHit.AttackInstanceIndex))
                    groups.Add(subHit.AttackInstanceIndex, new Blade { AttackIndex = subHit.AttackInstanceIndex });
            for (int i = 0; receipts != null && i < receipts.Count; i++)
            {
                var receipt = receipts[i];
                DamageEvent damage = receipt.damageEvent;
                if (!receipt.hasWorldAnchor || (damage.Kind != DamageKind.Direct && damage.Kind != DamageKind.Bounce)) continue;
                if (!groups.TryGetValue(damage.AttackInstanceIndex, out Blade blade)) continue;
                var contact = new Contact { Position = receipt.worldAnchor };
                contact.Receipts.Add(new AuthoritativeProjectileHit(damage, receipt.worldAnchor, i));
                // Splash shares the Direct contact, never a new sword or target
                // flight. Hidden recipients still consume their number/death latch.
                for (int next = i + 1; next < receipts.Count; next++)
                {
                    var extra = receipts[next];
                    if (extra.damageEvent.Kind != DamageKind.Splash ||
                        extra.damageEvent.AttackInstanceIndex != damage.AttackInstanceIndex ||
                        extra.damageEvent.SubHitIndex != damage.SubHitIndex) break;
                    contact.Receipts.Add(new AuthoritativeProjectileHit(extra.damageEvent,
                        extra.hasWorldAnchor ? extra.worldAnchor : contact.Position, next));
                }
                blade.Contacts.Add(contact);
            }
            foreach (Blade blade in groups.Values) result.Blades.Add(blade);
            return result;
        }

        private static void SampleBlade(Blade blade, float elapsed, Sprite sprite,
            VfxProfile profile, BoardVisualFrame? board, Transform parent)
        {
            if (elapsed < blade.Birth) return;
            if (elapsed >= blade.End)
            {
                if (blade.Root != null) { Destroy(blade.Root); blade.Root = null; }
                return;
            }
            if (blade.Root == null)
            {
                blade.Root = new GameObject("AuthorityReturningShortSword", typeof(SpriteRenderer));
                blade.Root.transform.SetParent(parent, false);
                blade.Sprite = blade.Root.GetComponent<SpriteRenderer>();
                blade.Sprite.sprite = sprite;
                Vector2 swordPixels = Vector2.Scale(TipUv - PommelUv, sprite.rect.size);
                float length = swordPixels.magnitude / sprite.pixelsPerUnit;
                blade.Root.transform.localScale = Vector3.one * Mathf.Max(0.1f, profile.projectileScale) / Mathf.Max(0.01f, length);
                if (profile.enableTrail)
                {
                    blade.Trail = blade.Root.AddComponent<TrailRenderer>();
                    blade.Trail.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
                    blade.Trail.time = profile.trailTime;
                    blade.Trail.startWidth = profile.trailWidth;
                    blade.Trail.endWidth = 0f;
                    blade.Trail.startColor = profile.trailColor;
                    Color clear = profile.trailColor; clear.a = 0f;
                    blade.Trail.endColor = clear;
                    blade.Trail.minVertexDistance = 0.03f;
                    blade.Trail.emitting = true;
                }
            }
            float fromTime = blade.Birth;
            Vector3 from = blade.Origin, to = blade.Elevated;
            float until = blade.Birth + LiftDuration;
            int phase = -1;
            if (elapsed >= until)
            {
                fromTime = until; from = blade.Elevated;
                for (int i = 0; i < blade.Contacts.Count; i++)
                {
                    phase = i; to = blade.Contacts[i].Position; until = blade.Contacts[i].At;
                    if (elapsed < until) break;
                    fromTime = until; from = to;
                }
                if (elapsed >= until)
                { phase = blade.Contacts.Count; to = blade.Elevated; until = blade.End; }
            }
            float progress = Mathf.Clamp01((elapsed - fromTime) / Mathf.Max(0.001f, until - fromTime));
            Vector3 arc = phase < 0 ? Vector3.zero : ArcOffset(from, to, phase, blade.AttackIndex);
            Vector3 control = (from + to) * 0.5f + arc;
            float oneMinus = 1f - progress;
            Vector3 position = oneMinus * oneMinus * from + 2f * oneMinus * progress * control + progress * progress * to;
            Vector3 direction = 2f * oneMinus * (control - from) + 2f * progress * (to - control);
            if (direction.sqrMagnitude > 0.00001f)
                blade.Root.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 45f);
            // The moving anchor is the sword TIP. Centering the sprite on a
            // target would overshoot it by half of the visible blade length.
            Vector2 localTip = (Vector2.Scale(TipUv, sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit;
            Vector3 tipOffset = blade.Root.transform.rotation * ((Vector3)localTip * blade.Root.transform.localScale.x);
            blade.Root.transform.position = position - tipOffset;
            int sorting = ResolveSorting(board, position);
            blade.Sprite.sortingOrder = sorting;
            if (blade.Trail != null) blade.Trail.sortingOrder = sorting - 1;
            float alpha = phase == blade.Contacts.Count ? 1f - Mathf.SmoothStep(0f, 1f, progress) :
                Mathf.Clamp01((elapsed - blade.Birth) / 0.05f);
            blade.Sprite.color = new Color(1f, 1f, 1f, alpha);
            if (blade.Trail != null) blade.Trail.emitting = phase != blade.Contacts.Count;
        }

        private static Vector3 ArcOffset(Vector3 from, Vector3 to, int phase, int index)
        {
            Vector3 direction = to - from;
            float side = ((phase + index) & 1) == 0 ? 1f : -1f;
            if (direction.sqrMagnitude < 0.01f)
                return new Vector3(side * 0.82f, 0.58f, 0f);
            return new Vector3(-direction.y, direction.x, 0f).normalized * side *
                Mathf.Clamp(direction.magnitude * 0.16f, 0.30f, 0.75f);
        }

        private static Vector3 ResolveElevatedOrigin(Vector3 origin, VfxProfile profile)
        {
            Camera camera = Camera.main;
            if (camera == null || !camera.orthographic) return origin + Vector3.up * 0.9f;
            float worldPerPixel = 2f * camera.orthographicSize / Mathf.Max(1, Screen.height);
            float clearance = profile.castingArtifactCardClearancePixels * Screen.height / 1080f * worldPerPixel;
            return origin + Vector3.up * (clearance + profile.projectileScale * 0.85f);
        }

        private static int ResolveSorting(BoardVisualFrame? board, Vector3 position)
        {
            if (!board.HasValue) return 44;
            Vector3 front = board.Value.Project(0.5f, 0f), back = board.Value.Project(0.5f, 1f);
            Vector3 axis = back - front;
            float depth = axis.sqrMagnitude > 0.00001f ? Vector3.Dot(position - front, axis) / axis.sqrMagnitude : 0f;
            return Mathf.Max(44, board.Value.SortingOrderAtDepth(Mathf.Clamp01(depth)) + 4);
        }

        private void Release(Cast cast)
        {
            if (cast.Released) return;
            cast.Released = true;
            _casts.Remove(cast);
            foreach (Blade blade in cast.Blades)
                if (blade.Root != null) { Destroy(blade.Root); blade.Root = null; }
        }

        public void CancelAll()
        {
            foreach (Cast cast in new List<Cast>(_casts)) Release(cast);
        }
        private void OnDisable() => CancelAll();
        private void OnDestroy() => CancelAll();
    }
}
