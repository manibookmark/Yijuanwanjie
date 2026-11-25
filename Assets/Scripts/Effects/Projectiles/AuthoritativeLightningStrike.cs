using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// 将权威 Direct/Bounce 命中序列表现为目标上空直落的天雷。模块独立拥有
    /// 雷线几何、并发节奏、命中时点与清理；CombatVisualBridge 只负责提交已经
    /// 记录的命中序列，VisualEffectManager 继续拥有命中粒子和音效。
    /// </summary>
    internal static class AuthoritativeLightningStrikePlayer
    {
        public const string VisualTypeName = "ProceduralLightningStrike";

        private static Material _lineMaterial;

        public static IEnumerator Play(
            MonoBehaviour coroutineHost,
            VisualEffectManager manager,
            IReadOnlyList<AuthoritativeProjectileSequence> sequences,
            VfxProfile profile,
            Action<AuthoritativeProjectileHit> onEachContact,
            AttackEffectCompletion completion = null)
        {
            if (coroutineHost == null || manager == null ||
                sequences == null || sequences.Count == 0)
            {
                yield break;
            }

            int launched = 0;
            int completed = 0;
            float launchDelay = profile != null
                ? Mathf.Max(0f, profile.multiProjectileDelay)
                : 0.035f;

            for (int i = 0; i < sequences.Count; i++)
            {
                if (completion != null && completion.IsCancelled) yield break;
                AuthoritativeProjectileSequence sequence = sequences[i];
                if (sequence == null || sequence.Hits.Count == 0)
                    continue;

                int sequenceIndex = i;
                launched++;
                coroutineHost.StartCoroutine(PlayOneSequence(
                    manager,
                    sequence,
                    sequenceIndex,
                    profile,
                    onEachContact,
                    () => completed++, completion));

                if (i < sequences.Count - 1 && launchDelay > 0f)
                    yield return WaitUnscaled(launchDelay);
            }

            while (completed < launched && (completion == null || !completion.IsCancelled))
                yield return null;
        }

        private static IEnumerator PlayOneSequence(
            VisualEffectManager manager,
            AuthoritativeProjectileSequence sequence,
            int sequenceIndex,
            VfxProfile profile,
            Action<AuthoritativeProjectileHit> onEachContact,
            Action onComplete, AttackEffectCompletion completion)
        {
            Vector3 previous = default;
            bool hasPrevious = false;
            for (int i = 0; i < sequence.Hits.Count; i++)
            {
                if (completion != null && completion.IsCancelled) yield break;
                AuthoritativeProjectileHit hit = sequence.Hits[i];
                bool isBounce = hit.DamageEvent.Kind == DamageKind.Bounce;
                Vector3 end = hit.Position;
                Vector3 start = isBounce && hasPrevious
                    ? previous
                    : end + Vector3.up * GetSkyHeight(profile) +
                      Vector3.right * SkyOffset(sequenceIndex);
                int seed = ComposeSeed(hit.DamageEvent, sequenceIndex, i);

                yield return PlayStrike(
                    manager,
                    start,
                    end,
                    isBounce,
                    seed,
                    profile,
                    () => onEachContact?.Invoke(hit), completion);

                previous = end;
                hasPrevious = true;
            }

            onComplete?.Invoke();
        }

        private static IEnumerator PlayStrike(
            VisualEffectManager manager,
            Vector3 start,
            Vector3 end,
            bool isBounce,
            int seed,
            VfxProfile profile,
            Action onContact, AttackEffectCompletion completion)
        {
            var root = new GameObject(
                isBounce ? "LightningArc" : "HeavenlyLightningStrike");
            root.transform.SetParent(manager.transform, true);
            bool woodenSword = profile != null &&
                profile.selfEffectStyle == CardSelfEffectStyle.ThunderWoodSword;

            float intensity = profile != null
                ? Mathf.Clamp(profile.lightningIntensity, 0.5f, 2f)
                : 1f;
            int segments = profile != null
                ? Mathf.Clamp(profile.lightningSegments, 7, 24)
                : 13;
            float jitter = (profile != null
                ? Mathf.Clamp(profile.lightningJitter, 0.1f, 1.5f)
                : 0.58f) * (isBounce ? 0.55f : 1f);
            float duration = profile != null
                ? Mathf.Clamp(profile.lightningDuration, 0.12f, 0.6f)
                : 0.24f;
            float contactDelay = profile != null
                ? Mathf.Clamp(profile.lightningContactDelay, 0.01f, duration)
                : 0.045f;

            Color coreColor = profile != null
                ? profile.lightningCoreColor
                : new Color(1f, 0.98f, 0.82f, 1f);
            Color auraColor = profile != null
                ? profile.lightningAuraColor
                : new Color(0.26f, 0.72f, 1f, 0.82f);
            Color groundColor = profile != null
                ? profile.lightningGroundColor
                : new Color(0.66f, 0.36f, 1f, 0.9f);

            LineRenderer aura = CreateLine(
                root.transform,
                "LightningAura",
                segments + 1,
                0.22f * intensity,
                80);
            LineRenderer body = CreateLine(
                root.transform,
                "LightningBody",
                segments + 1,
                0.095f * intensity,
                81);
            LineRenderer core = CreateLine(
                root.transform,
                "LightningCore",
                segments + 1,
                0.035f * intensity,
                82);

            int branchCount = profile != null
                ? Mathf.Clamp(profile.lightningBranchCount, 0, 3)
                : 2;
            var branches = new List<LineRenderer>(branchCount);
            for (int i = 0; i < branchCount; i++)
            {
                branches.Add(CreateLine(
                    root.transform,
                    $"LightningBranch_{i}",
                    5,
                    0.026f * intensity,
                    79));
            }

            LineRenderer groundRing = CreateLine(
                root.transform,
                "LightningGroundSeal",
                33,
                0.038f * intensity,
                78,
                loop: true);
            // The wooden conductor leaves small electrical splinters, not a
            // full expanding seal or the generic wood-chip particle textures.
            if (woodenSword) groundRing.enabled = false;
            var contactSparks = new List<LineRenderer>();
            var residualArcs = new List<LineRenderer>();
            if (woodenSword)
            {
                for (int i = 0; i < 14; i++)
                {
                    LineRenderer spark = CreateLine(root.transform, "WoodSwordElectricSpark_" + i,
                        3, 0.008f + (i % 3) * 0.003f, 84);
                    SetColor(spark, coreColor, 0f);
                    contactSparks.Add(spark);
                }
                for (int i = 0; i < 5; i++)
                {
                    LineRenderer arc = CreateLine(root.transform, "WoodSwordResidualArc_" + i,
                        4, 0.009f, 83);
                    SetColor(arc, auraColor, 0f);
                    residualArcs.Add(arc);
                }
            }

            bool contacted = false;
            int frame = 0;
            float elapsed = 0f;
            while (elapsed < duration && (completion == null || !completion.IsCancelled))
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ignition = Smooth01(Mathf.Clamp01(t / 0.12f));
                float decay = 1f - Smooth01(Mathf.Clamp01(
                    (t - 0.48f) / 0.52f));
                float flicker = 0.78f +
                    0.22f * Mathf.Abs(Mathf.Sin((frame + seed) * 2.17f));
                float opacity = ignition * decay * flicker;
                if (woodenSword)
                    opacity = Smooth01(Mathf.Clamp01(elapsed / 0.018f)) *
                        (1f - Smooth01(Mathf.Clamp01((elapsed - contactDelay - 0.015f) / 0.09f)));

                SetJaggedPath(aura, start, end, segments, seed, frame,
                    jitter * 1.08f);
                SetJaggedPath(body, start, end, segments, seed + 37, frame,
                    jitter * 0.72f);
                SetJaggedPath(core, start, end, segments, seed + 73, frame,
                    jitter * 0.34f);
                SetColor(aura, auraColor, opacity * 0.52f);
                SetColor(body, auraColor, opacity);
                SetColor(core, coreColor, Mathf.Clamp01(opacity * 1.24f));

                for (int i = 0; i < branches.Count; i++)
                {
                    float along = 0.28f + i * 0.18f;
                    Vector3 branchStart = Vector3.Lerp(start, end, along);
                    float side = ((i + seed) & 1) == 0 ? -1f : 1f;
                    Vector3 branchEnd = branchStart + new Vector3(
                        side * jitter * (0.72f + i * 0.16f),
                        -0.42f - i * 0.12f,
                        0f);
                    SetJaggedPath(
                        branches[i],
                        branchStart,
                        branchEnd,
                        4,
                        seed + 109 + i * 19,
                        frame,
                        jitter * 0.24f);
                    SetColor(branches[i], auraColor, opacity * 0.72f);
                }

                float ringRadius = Mathf.Lerp(0.1f, 0.82f * intensity,
                    Smooth01(t));
                SetRing(groundRing, end, ringRadius);
                SetColor(groundRing, groundColor, decay * 0.82f);
                if (woodenSword)
                    UpdateWoodSwordContact(contactSparks, residualArcs, end,
                        elapsed - contactDelay, seed, coreColor, auraColor);

                if (!contacted && elapsed >= contactDelay)
                {
                    contacted = true;
                    // The authoritative contact callback owns hit audio and
                    // response. Replaying a small hit here would double it.
                    if (!woodenSword) manager.PlaySmallHitEffect(end, profile, completion: completion);
                    onContact?.Invoke();
                }

                frame++;
                yield return null;
            }

            if (!contacted && (completion == null || !completion.IsCancelled))
            {
                if (!woodenSword) manager.PlaySmallHitEffect(end, profile, completion: completion);
                onContact?.Invoke();
            }

            if (root != null)
                UnityEngine.Object.Destroy(root);
        }

        private static void UpdateWoodSwordContact(IReadOnlyList<LineRenderer> sparks,
            IReadOnlyList<LineRenderer> residual, Vector3 contact, float age, int seed,
            Color core, Color aura)
        {
            for (int i = 0; i < sparks.Count; i++)
            {
                float lifetime = 0.075f + (i % 5) * 0.022f;
                float t = Mathf.Clamp01(age / lifetime);
                float angle = i * Mathf.PI * 2f / sparks.Count + HashSigned(seed + i * 47) * 0.2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.7f, 0f);
                float distance = Mathf.Max(0f, age) * (1.7f + i % 4 * 0.28f) /
                    (1f + Mathf.Max(0f, age) * 5f);
                Vector3 head = contact + direction * (0.035f + distance) +
                    Vector3.down * Mathf.Max(0f, age) * Mathf.Max(0f, age) * 1.8f;
                float length = Mathf.Lerp(0.045f, 0.012f, t);
                sparks[i].SetPosition(0, head - direction * length);
                sparks[i].SetPosition(1, head - direction * length * 0.3f + Vector3.up * 0.006f);
                sparks[i].SetPosition(2, head);
                SetColor(sparks[i], i % 4 == 0 ? aura : core,
                    age >= 0f ? (1f - t) * (1f - t) * 0.9f : 0f);
            }
            for (int i = 0; i < residual.Count; i++)
            {
                float t = Mathf.Clamp01((age - 0.025f) / 0.25f);
                float angle = i * 2.39996f + HashSigned(seed + i * 83) * 0.18f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f, 0f);
                Vector3 tangent = new Vector3(-direction.y, direction.x, 0f);
                Vector3 start = contact + direction * (0.06f + i * 0.027f);
                for (int p = 0; p < 4; p++)
                    residual[i].SetPosition(p, start + direction * (p * 0.038f) +
                        tangent * (HashSigned(seed + i * 101 + p * 19) * 0.035f));
                // A restrained envelope, with no frame-random jumps or sudden
                // final deletion of bright residual particles.
                SetColor(residual[i], aura, age > 0.025f
                    ? Mathf.Sin(t * Mathf.PI) * (1f - t) * 0.48f : 0f);
            }
        }

        private static LineRenderer CreateLine(
            Transform parent,
            string name,
            int positionCount,
            float width,
            int sortingOrder,
            bool loop = false)
        {
            var lineObject = new GameObject(name, typeof(LineRenderer));
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.GetComponent<LineRenderer>();
            line.sharedMaterial = GetLineMaterial();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.positionCount = positionCount;
            line.startWidth = width;
            line.endWidth = width * 0.36f;
            line.loop = loop;
            line.sortingOrder = sortingOrder;
            return line;
        }

        private static void SetJaggedPath(
            LineRenderer line,
            Vector3 start,
            Vector3 end,
            int segments,
            int seed,
            int frame,
            float jitter)
        {
            if (line == null) return;
            Vector3 direction = end - start;
            Vector3 perpendicular = direction.sqrMagnitude > 0.0001f
                ? new Vector3(-direction.y, direction.x, 0f).normalized
                : Vector3.right;
            line.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float envelope = Mathf.Sin(t * Mathf.PI);
                float noise = HashSigned(seed + i * 97 + frame * 131);
                float secondary = HashSigned(seed + i * 53 - frame * 71);
                Vector3 point = Vector3.Lerp(start, end, t) +
                    perpendicular * noise * jitter * envelope +
                    Vector3.right * secondary * jitter * envelope * 0.18f;
                line.SetPosition(i, point);
            }
        }

        private static void SetRing(
            LineRenderer line,
            Vector3 center,
            float radius)
        {
            if (line == null) return;
            int count = line.positionCount;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / count;
                line.SetPosition(i, center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius * 0.28f,
                    0f));
            }
        }

        private static void SetColor(
            LineRenderer line,
            Color color,
            float opacity)
        {
            if (line == null) return;
            color.a *= Mathf.Clamp01(opacity);
            line.startColor = color;
            line.endColor = color;
        }

        private static IEnumerator WaitUnscaled(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static float GetSkyHeight(VfxProfile profile)
        {
            return profile != null
                ? Mathf.Clamp(profile.lightningSkyHeight, 2f, 8f)
                : 4.8f;
        }

        private static float SkyOffset(int sequenceIndex)
        {
            return ((sequenceIndex % 3) - 1) * 0.18f;
        }

        private static int ComposeSeed(
            DamageEvent damageEvent,
            int sequenceIndex,
            int hitIndex)
        {
            unchecked
            {
                return damageEvent.AttackInstanceIndex * 73856093 ^
                    damageEvent.SubHitIndex * 19349663 ^
                    damageEvent.TargetIndex * 83492791 ^
                    sequenceIndex * 31 ^ hitIndex;
            }
        }

        private static float HashSigned(int value)
        {
            unchecked
            {
                uint hash = (uint)value;
                hash ^= hash >> 16;
                hash *= 0x7feb352d;
                hash ^= hash >> 15;
                hash *= 0x846ca68b;
                hash ^= hash >> 16;
                return hash / (float)uint.MaxValue * 2f - 1f;
            }
        }

        private static Material GetLineMaterial()
        {
            if (_lineMaterial != null) return _lineMaterial;
            Shader shader = Shader.Find("Sprites/Default") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Transparent");
            if (shader == null) return null;
            _lineMaterial = new Material(shader)
            {
                name = "AuthoritativeLightningLineMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
            return _lineMaterial;
        }

        private static float Smooth01(float value)
        {
            return value * value * (3f - 2f * value);
        }
    }
}
