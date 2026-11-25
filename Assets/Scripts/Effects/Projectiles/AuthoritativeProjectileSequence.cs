using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// The profile explicitly owns whether a standard flying attack waits for
    /// authoritative DamageEvents. This applies to single- and multi-object
    /// flights alike; presentation must not silently fall back to the earlier
    /// compatibility target merely because the card is SingleShot.
    /// </summary>
    public static class AuthoritativeProjectileSequencePolicy
    {
        public static bool ShouldPlay(VfxProfile profile)
        {
            return profile != null &&
                profile.useAuthoritativeHitSequence;
        }
    }

    /// <summary>
    /// A recorded damage event plus the world-space anchor captured when Core
    /// resolved it. Pending-hit storage remains owned by CombatVisualBridge;
    /// this immutable value is the narrow hand-off to projectile presentation.
    /// </summary>
    internal readonly struct AuthoritativeProjectileHit
    {
        public readonly DamageEvent DamageEvent;
        public readonly Vector3 Position;
        public readonly int PendingHitIndex;

        public AuthoritativeProjectileHit(
            DamageEvent damageEvent,
            Vector3 position,
            int pendingHitIndex)
        {
            DamageEvent = damageEvent;
            Position = position;
            PendingHitIndex = pendingHitIndex;
        }
    }

    /// <summary>
        /// One Direct hit followed by its zero-or-more Bounce hits and associated
        /// Area receipts. Area changes feedback only, never the flight path. A new sequence
    /// starts for every Direct event, so multi-target attacks never invent a
    /// path from one unrelated direct target to another.
    /// </summary>
    internal sealed class AuthoritativeProjectileSequence
    {
        public readonly List<AuthoritativeProjectileHit> Hits =
            new List<AuthoritativeProjectileHit>();
    }

    internal static class AuthoritativeProjectileSequenceBuilder
    {
        public static List<AuthoritativeProjectileSequence> Build(
            IReadOnlyList<AuthoritativeProjectileHit> source)
        {
            var result = new List<AuthoritativeProjectileSequence>();
            var openSequences = new Dictionary<long,
                AuthoritativeProjectileSequence>();
            if (source == null) return result;

            for (int i = 0; i < source.Count; i++)
            {
                AuthoritativeProjectileHit hit = source[i];
                DamageEvent damageEvent = hit.DamageEvent;
                long key = ComposeKey(
                    damageEvent.AttackInstanceIndex,
                    damageEvent.SubHitIndex);

                if (damageEvent.Kind == DamageKind.Direct)
                {
                    var sequence = new AuthoritativeProjectileSequence();
                    sequence.Hits.Add(hit);
                    result.Add(sequence);
                    openSequences[key] = sequence;
                    continue;
                }

                if ((damageEvent.Kind == DamageKind.Bounce || damageEvent.Kind == DamageKind.Splash) &&
                    openSequences.TryGetValue(
                        key,
                        out AuthoritativeProjectileSequence open))
                {
                    open.Hits.Add(hit);
                }
            }

            return result;
        }

        private static long ComposeKey(int attackIndex, int subHitIndex)
        {
            return ((long)attackIndex << 32) | (uint)subHitIndex;
        }
    }

    /// <summary>
    /// Plays a staggered volley while keeping each Direct→Bounce chain
    /// sequential. It owns only timing; projectile creation and pooling stay
    /// behind VisualEffectManager.
    /// </summary>
    internal static class AuthoritativeProjectileSequencePlayer
    {
        public static IEnumerator Play(
            MonoBehaviour coroutineHost,
            VisualEffectManager manager,
            IReadOnlyList<AuthoritativeProjectileSequence> sequences,
            VfxProfile profile,
            Action<AuthoritativeProjectileHit> onEachArrive,
            Vector3? castOrigin = null,
            bool completeEachSequenceBeforeNext = false,
            AttackEffectCompletion completion = null)
        {
            if (coroutineHost == null || manager == null ||
                sequences == null || sequences.Count == 0 ||
                manager.playerAttackOrigin == null)
            {
                yield break;
            }

            int launched = 0;
            int completed = 0;
            float launchDelay = profile != null
                ? Mathf.Max(0f, profile.multiProjectileDelay)
                : 0.06f;
            if (profile != null && (profile.visualType == ProceduralKnifeVisual.VisualTypeName ||
                profile.visualType == ProceduralFireTalismanVisual.VisualTypeName))
                launchDelay = Mathf.Min(launchDelay, 0.60f / Mathf.Max(1, sequences.Count - 1));

            for (int i = 0; i < sequences.Count; i++)
            {
                if (completion != null && completion.IsCancelled ||
                    coroutineHost == null || !coroutineHost.isActiveAndEnabled || manager == null)
                    yield break;
                AuthoritativeProjectileSequence sequence = sequences[i];
                if (sequence == null || sequence.Hits.Count == 0)
                    continue;

                int sequenceIndex = i;
                launched++;
                IEnumerator playback = PlayOneSequence(
                    coroutineHost, manager,
                    sequence,
                    sequenceIndex,
                    profile,
                    onEachArrive,
                    castOrigin,
                    () => completed++, completion);
                if (completeEachSequenceBeforeNext)
                    yield return playback;
                else
                    coroutineHost.StartCoroutine(playback);

                if (i < sequences.Count - 1 && launchDelay > 0f)
                    yield return new WaitForSeconds(launchDelay);
            }

            while (completed < launched && coroutineHost != null &&
                coroutineHost.isActiveAndEnabled && manager != null &&
                (completion == null || !completion.IsCancelled))
                yield return null;
        }

        private static IEnumerator PlayOneSequence(
            MonoBehaviour coroutineHost, VisualEffectManager manager,
            AuthoritativeProjectileSequence sequence,
            int sequenceIndex,
            VfxProfile profile,
            Action<AuthoritativeProjectileHit> onEachArrive,
            Vector3? castOrigin,
            Action onComplete, AttackEffectCompletion completion)
        {
            try
            {
                float startOffset = ((sequenceIndex % 3) - 1) * 0.075f;
                Vector3 start = (castOrigin ?? manager.playerAttackOrigin.position) +
                    new Vector3(0f, startOffset, 0f);

                for (int i = 0; i < sequence.Hits.Count; i++)
                {
                    if (manager == null || coroutineHost == null || !coroutineHost.isActiveAndEnabled ||
                        (completion != null && completion.IsCancelled)) yield break;
                    AuthoritativeProjectileHit hit = sequence.Hits[i];
                    if (hit.DamageEvent.Kind == DamageKind.Splash)
                    {
                        onEachArrive?.Invoke(hit);
                        continue;
                    }
                    bool arrived = false;
                    bool isBounce = hit.DamageEvent.Kind == DamageKind.Bounce;
                    manager.FireProjectileBetweenPositions(
                        start,
                        hit.Position,
                        profile,
                        isBounce,
                        () =>
                        {
                            if (completion == null || !completion.IsCancelled)
                                onEachArrive?.Invoke(hit);
                            arrived = true;
                        }, completion);

                    while (!arrived && manager != null && coroutineHost != null &&
                        coroutineHost.isActiveAndEnabled &&
                        (completion == null || !completion.IsCancelled))
                        yield return null;

                    start = hit.Position;
                }

            }
            finally { onComplete?.Invoke(); }
        }
    }
}
