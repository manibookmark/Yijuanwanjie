using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Owns the sand crest and receipt-backed contact timing. Each authoritative
    /// AttackInstanceIndex is one real wave; Amount and combo never clone it.
    /// Captured anchors, including Splash anchors, are immutable inputs only.
    /// Native sand/dust playback belongs to SandWaveInfinityVisual; this owner
    /// alone advances the recorded crest and delivers the contact receipts.
    /// </summary>
    public static class AuthoritativeSandWavePlayer
    {
        public const string VisualTypeName = "ProceduralSandWave";

        private const float WaveDuration = 0.62f;
        private const float WaveLaunchInterval = 0.04f;
        private const int ConcurrentGrainBudget = 12288;

        internal static IEnumerator Play(MonoBehaviour host, VisualEffectManager manager,
            IReadOnlyList<CombatAttackPresentationQueue.PendingHit> receipts,
            VfxProfile profile, BoardVisualFrame? board, Vector3? castOrigin,
            Func<bool> active, Action<AuthoritativeProjectileHit> onContact)
        {
            if (manager == null || profile == null) yield break;
            var groups = new SortedDictionary<int, List<AuthoritativeProjectileHit>>();
            for (int i = 0; receipts != null && i < receipts.Count; i++)
            {
                var hit = receipts[i];
                if (!hit.hasWorldAnchor || (hit.damageEvent.Kind != DamageKind.Direct &&
                    hit.damageEvent.Kind != DamageKind.Splash)) continue;
                int key = hit.damageEvent.AttackInstanceIndex;
                if (!groups.TryGetValue(key, out var group))
                    groups.Add(key, group = new List<AuthoritativeProjectileHit>());
                group.Add(new AuthoritativeProjectileHit(hit.damageEvent, hit.worldAnchor, i));
            }
            // Even an empty attack can create a symbol. The visual crest is not
            // conditional on a hit, while every contact still requires a receipt.
            if (groups.Count == 0) groups.Add(0, new List<AuthoritativeProjectileHit>());
            // Reduce cosmetic density for overlapping waves, never their real
            // count. The existing launch/contact cadence remains unchanged.
            int overlapping = Mathf.Min(groups.Count,
                Mathf.CeilToInt((WaveDuration + SandWaveInfinityVisual.TailDuration) / WaveLaunchInterval) + 2);
            int capacity = Mathf.Min(1500, ConcurrentGrainBudget / overlapping);
            int running = 0;
            foreach (var group in groups)
            {
                if (!active()) break;
                running++;
                host.StartCoroutine(PlayWave(host, manager, group.Value, profile, board, castOrigin,
                    capacity, group.Key, active, onContact, () => running--));
                if (groups.Count > 1) yield return new WaitForSeconds(WaveLaunchInterval);
            }
            while (running > 0 && active()) yield return null;
        }

        private static IEnumerator PlayWave(MonoBehaviour host, VisualEffectManager manager,
            IReadOnlyList<AuthoritativeProjectileHit> hits, VfxProfile profile,
            BoardVisualFrame? board, Vector3? castOrigin, int capacity, int waveIndex, Func<bool> active,
            Action<AuthoritativeProjectileHit> onContact, Action complete)
        {
            Vector3 left = board?.Project(0f, 0f) ?? new Vector3(-4.6f, 0f, 0f);
            Vector3 right = board?.Project(1f, 0f) ?? new Vector3(4.6f, 0f, 0f);
            Vector3 origin = castOrigin ?? manager.playerAttackOrigin?.position ?? left;
            float flowingHeight = left.y;
            SandWaveInfinityVisual visual = null;
            var contacted = new bool[hits.Count];
            float elapsed = 0f;
            bool completed = false;
            try
            {
                visual = SandWaveInfinityVisual.Create(manager.transform, host, active,
                    waveIndex, capacity, WaveDuration);
                yield return visual.Prepare();
                if (!active() || visual == null || !visual.isActiveAndEnabled) yield break;
                visual.Begin(origin);
                while (elapsed < WaveDuration && active() &&
                    visual != null && visual.isActiveAndEnabled)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / WaveDuration);
                    float travel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.9f, t));
                    Vector3 crest = Vector3.Lerp(left - Vector3.right * 0.4f,
                        right + Vector3.right * 0.4f, travel);
                    // The crest follows the recorded front occupant of each
                    // column; an exposed rear enemy is not painted at row zero.
                    Vector3 nearest = crest;
                    float nearestDistance = float.MaxValue;
                    for (int h = 0; h < hits.Count; h++)
                    {
                        if (hits[h].DamageEvent.Kind != DamageKind.Direct) continue;
                        float distance = Mathf.Abs(hits[h].Position.x - crest.x);
                        if (distance < nearestDistance)
                        { nearestDistance = distance; nearest = hits[h].Position; }
                    }
                    crest.y = Mathf.Lerp(crest.y, nearest.y, Mathf.Clamp01(1f - nearestDistance / 1.6f));
                    // Smooth only the grain cloud's height at a column boundary.
                    // The authoritative crest/contact test below is unchanged.
                    flowingHeight = Mathf.Lerp(flowingHeight, crest.y,
                        1f - Mathf.Exp(-Time.deltaTime * 22f));
                    Vector3 flowingCrest = new Vector3(crest.x, flowingHeight, crest.z);
                    if (t < 0.16f)
                        flowingCrest = Vector3.Lerp(origin, flowingCrest, Mathf.SmoothStep(0f, 1f, t / 0.16f));
                    visual.SetPosition(flowingCrest, t);
                    for (int h = 0; h < hits.Count; h++)
                    {
                        if (contacted[h]) continue;
                        // Splash follows its direct contact in receipt order,
                        // regardless of the rear recipient's screen x position.
                        float contactX = hits[h].Position.x;
                        if (hits[h].DamageEvent.Kind == DamageKind.Splash)
                            for (int p = h - 1; p >= 0; p--)
                                if (hits[p].DamageEvent.Kind == DamageKind.Direct)
                                { contactX = hits[p].Position.x; break; }
                        if (crest.x < contactX && t < 0.98f) continue;
                        contacted[h] = true;
                        if (hits[h].DamageEvent.Kind == DamageKind.Splash)
                            StartSplashRing(manager, hits[h].Position, profile.selfEffectPrimaryColor);
                        onContact?.Invoke(hits[h]);
                    }
                    yield return null;
                }
                completed = elapsed >= WaveDuration;
            }
            finally
            {
                if (visual != null) visual.Release(completed);
                complete?.Invoke();
            }
        }

        private static void StartSplashRing(VisualEffectManager manager, Vector3 position, Color color)
            => manager.StartCoroutine(SplashRing(manager.transform, position, color));

        private static IEnumerator SplashRing(Transform parent, Vector3 position, Color color)
        {
            var root = new GameObject("AuthorityThinSandRipple", typeof(LineRenderer));
            root.transform.SetParent(parent, false);
            var line = root.GetComponent<LineRenderer>();
            line.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
            line.useWorldSpace = true; line.loop = true; line.positionCount = 30;
            line.startWidth = line.endWidth = 0.018f; line.sortingOrder = 44;
            float elapsed = 0f;
            try
            {
                while (elapsed < 0.22f)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / 0.22f);
                    Color tint = color; tint.a = (1f - t) * 0.45f;
                    line.startColor = line.endColor = tint;
                    for (int i = 0; i < 30; i++)
                    {
                        float a = i * Mathf.PI * 2f / 30;
                        line.SetPosition(i, position + new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.4f, 0f) * (0.15f + t * 0.65f));
                    }
                    yield return null;
                }
            }
            finally { if (root != null) UnityEngine.Object.Destroy(root); }
        }
    }
}
