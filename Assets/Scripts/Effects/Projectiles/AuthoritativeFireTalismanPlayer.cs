using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Owns one card-to-centre talisman and the subsequent receipt-backed fire
    /// volley. The paper stays at centre; it never flies to or chooses enemies.
    /// Every cast owns its roots, opacity, particle simulation and cancellation.
    /// Core groups and captured landing positions remain immutable inputs.
    /// </summary>
    public sealed class AuthoritativeFireTalismanPlayer : MonoBehaviour
    {
        public const string VisualTypeName = ProceduralFireTalismanVisual.VisualTypeName;

        private sealed class Cast
        {
            public GameObject Root;
            public Transform Symbol;
            public ProceduralFireTalismanVisual Paper;
            public readonly List<FireInfinityParticleField> Fires = new List<FireInfinityParticleField>();
            public Func<bool> Active;
        }

        private readonly HashSet<Cast> _casts = new HashSet<Cast>();
        private readonly List<Cast> _cancelled = new List<Cast>();

        internal static IEnumerator Play(MonoBehaviour host, VisualEffectManager manager,
            IReadOnlyList<int[]> groups, IReadOnlyList<Vector3> positions,
            VfxProfile profile, Vector3? cardOrigin, Func<bool> active,
            Action<int> onContact)
        {
            if (host == null || manager == null || profile == null ||
                groups == null || positions == null || !active()) yield break;
            var owner = host.GetComponent<AuthoritativeFireTalismanPlayer>() ??
                host.gameObject.AddComponent<AuthoritativeFireTalismanPlayer>();
            yield return owner.Animate(manager, groups, positions, profile,
                cardOrigin ?? manager.playerAttackOrigin?.position ?? Vector3.zero,
                active, onContact);
        }

        private IEnumerator Animate(VisualEffectManager manager,
            IReadOnlyList<int[]> groups, IReadOnlyList<Vector3> positions,
            VfxProfile profile, Vector3 origin, Func<bool> active, Action<int> onContact)
        {
            // Only the symbol carries the 3/4-cost distinction. Both cards use
            // exactly the same fire per real attack object, including repeats.
            var cast = new Cast { Root = new GameObject("AuthorityFireTalismanCast"), Active = active };
            cast.Root.transform.SetParent(transform, false);
            _casts.Add(cast);
            try
            {
                var symbol = new GameObject("CentralFireTalisman");
                symbol.transform.SetParent(cast.Root.transform, false);
                // Only the paper/glyph group rises above enemies. Ground fire
                // remains outside this group at its original battlefield order.
                var sorting = symbol.AddComponent<UnityEngine.Rendering.SortingGroup>();
                sorting.sortingLayerID = 0;
                sorting.sortingOrder = 100;
                cast.Symbol = symbol.transform;
                cast.Paper = new ProceduralFireTalismanVisual();
                cast.Paper.Initialize(symbol, profile, 1f);
                cast.Paper.OnFlightStart();
                cast.Paper.SetOpacity(0f);
                cast.Paper.SetStrokeReveal(0f);
                int count = Mathf.Min(groups.Count, positions.Count);
                // Prepare every real object during the existing brush/hold phase.
                // Ignition never waits for native setup or changes contact timing.
                float density = Mathf.Clamp(8f / Mathf.Max(1, count), 0.3f, 1f);
                for (int i = 0; i < count; i++)
                    cast.Fires.Add(groups[i] == null || groups[i].Length == 0 ? null :
                        FireInfinityParticleField.PrepareIgnition(cast.Root.transform, this, active, density));

                float scale = Mathf.Clamp(profile.selfEffectScale, 0.7f, 2.5f);
                Vector3 centre = ResolveCentre(origin, profile.selfEffectOffset);
                float riseDuration = Mathf.Clamp(profile.flightDuration, 0.18f, 0.4f);
                // Finish the brush strokes, then let the whole symbol remain
                // readable before it ignites the receipt-backed fire volley.
                float drawCompletedAt = riseDuration + 0.7f;
                float igniteAt = Mathf.Max(drawCompletedAt + 0.5f, profile.selfEffectDuration);
                float delay = Mathf.Min(Mathf.Max(0f, profile.multiProjectileDelay),
                    0.32f / Mathf.Max(1, count - 1));
                float lastIgnition = igniteAt + Mathf.Max(0, count - 1) * delay;
                float end = lastIgnition + 0.5f;
                float elapsed = 0f;
                int next = 0;
                var contactedTargets = new HashSet<int>();
                while (cast.Root != null && manager != null && isActiveAndEnabled && active() && elapsed < end)
                {
                    elapsed += Time.deltaTime;
                    float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / riseDuration));
                    Vector3 point = Vector3.Lerp(origin, centre, rise);
                    point.y += Mathf.Sin(rise * Mathf.PI) * 0.18f;
                    cast.Symbol.position = point;
                    float charge = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(riseDuration, drawCompletedAt, elapsed));
                    cast.Paper.SetStrokeReveal(charge);
                    float pulse = elapsed < igniteAt ? charge * 0.08f :
                        0.10f * Mathf.Exp(-(elapsed - igniteAt) * 12f);
                    cast.Symbol.localScale = Vector3.one * scale *
                        (Mathf.Lerp(0.64f, 1f, rise) + pulse);
                    cast.Symbol.localRotation = Quaternion.Euler(0f, 0f,
                        Mathf.Lerp(64f, 90f, rise) + Mathf.Sin(elapsed * 4f) * 1.5f);
                    float fade = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(lastIgnition + 0.20f, end, elapsed));
                    cast.Paper.SetOpacity(rise * fade);
                    while (next < count && cast.Root != null && manager != null &&
                        isActiveAndEnabled && elapsed >= igniteAt + next * delay && active())
                    {
                        int index = next++; // Advance before callbacks can cancel the cast.
                        int[] group = groups[index];
                        if (group == null || group.Length == 0) continue;
                        Ignite(cast, positions[index], index);
                        manager.PlaySmallHitEffect(positions[index], profile, emitParticles: false);
                        contactedTargets.Clear();
                        for (int j = 0; j < group.Length && cast.Root != null &&
                            isActiveAndEnabled && active(); j++)
                            if (contactedTargets.Add(group[j])) onContact?.Invoke(group[j]);
                    }
                    yield return null;
                }
            }
            finally { Cleanup(cast); }
        }

        private static Vector3 ResolveCentre(Vector3 origin, Vector3 offset)
        {
            Camera camera = Camera.main;
            if (camera == null) return origin + Vector3.up * 2.1f + offset;
            float depth = camera.WorldToViewportPoint(origin).z;
            if (depth <= 0f) return origin + Vector3.up * 2.1f + offset;
            return camera.ViewportToWorldPoint(new Vector3(0.5f, 0.48f, depth)) + offset;
        }

        private static void Ignite(Cast cast, Vector3 target, int index)
        {
            FireInfinityParticleField field = cast.Fires[index];
            if (field != null) field.Begin(target);
        }

        private void Update()
        {
            _cancelled.Clear();
            foreach (Cast cast in _casts)
                if (cast.Root == null || !cast.Active()) _cancelled.Add(cast);
            for (int i = 0; i < _cancelled.Count; i++) Cleanup(_cancelled[i]);
        }

        private void Cleanup(Cast cast)
        {
            _casts.Remove(cast);
            cast.Paper?.OnFlightEnd();
            cast.Paper?.Cleanup();
            cast.Paper = null;
            cast.Fires.Clear();
            if (cast.Root != null) { cast.Root.SetActive(false); Destroy(cast.Root); }
            cast.Root = null;
        }

        public void CancelAll()
        {
            _cancelled.Clear();
            _cancelled.AddRange(_casts);
            for (int i = 0; i < _cancelled.Count; i++) Cleanup(_cancelled[i]);
            _cancelled.Clear();
        }

        private void OnDisable() => CancelAll();
    }
}
