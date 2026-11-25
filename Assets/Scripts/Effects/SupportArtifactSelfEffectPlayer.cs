using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns brief non-attacking support artifacts: a jade bracelet unfolding
    /// shadow rings or a pearl with flowing wind. Geometry, opacity, animation and
    /// cancellation live here; authoritative stat gains remain in Core/HUD.
    /// Dense particle bodies share the mirror's simplified silhouette language;
    /// moving shadow/wind accents remain lines. No Infinity modules are used.
    /// </summary>
    public sealed class SupportArtifactSelfEffectPlayer : MonoBehaviour
    {
        private enum LineRole { Ring, Wind, Shadow, Mote }

        private sealed class AccentLine
        {
            public LineRenderer Renderer;
            public Vector3[] Points;
            public Color Color;
            public LineRole Role;
            public int Index;
        }

        private static Material _lineMaterial;
        private readonly HashSet<GameObject> _roots = new HashSet<GameObject>();

        public void Play(Vector3 position, VfxProfile profile,
            IReadOnlyList<Vector3> destinations = null, string gainLabel = null)
        {
            PlayArtifact(position, profile, null);
        }

        /// <summary>Manifests one artifact and hands its actual position to the
        /// single HUD glyph owner as it contracts. This owner never flies gains.</summary>
        public bool PlayArtifact(Vector3 position, VfxProfile profile,
            Action<Vector3> onManifested)
        {
            if (!isActiveAndEnabled || profile == null) return false;
            Vector2 bodySize = profile.selfEffectStyle == CardSelfEffectStyle.WindOrb
                ? new Vector2(0.74f, 0.74f) : new Vector2(1.3f, 1.13f);
            float scale = CardCastPlacement.SupportScale(position, profile, bodySize);
            Vector3 origin = CardCastPlacement.AboveCard(position, profile, scale);
            StartCoroutine(Animate(origin, profile, scale, onManifested));
            return true;
        }

        private IEnumerator Animate(Vector3 position, VfxProfile profile, float scale,
            Action<Vector3> onManifested)
        {
            // Let a newly added owner enter the active hierarchy before creating
            // renderer state. Every cast has an independent transient root.
            yield return null;
            if (!isActiveAndEnabled) yield break;
            var root = new GameObject("SupportArtifact_" + profile.selfEffectStyle);
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            _roots.Add(root);
            var lines = new List<AccentLine>();
            bool windOrb = profile.selfEffectStyle == CardSelfEffectStyle.WindOrb;
            float duration = Mathf.Clamp(profile.selfEffectDuration, 0.35f, 1.5f);
            float elapsed = 0f;
            bool manifested = false;
            ArtifactParticleBody body = null;
            try
            {
                body = windOrb ? BuildWindOrb(root.transform, profile, lines)
                    : BuildQuantityBracelet(root.transform, profile, lines);
                while (elapsed < duration && root != null && isActiveAndEnabled)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.12f));
                    float fade = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.42f, 0.68f, t));
                    float alpha = appear * fade;
                    root.transform.localScale = Vector3.one * scale *
                        Mathf.Lerp(0.72f, 1f, appear) * Mathf.Lerp(0.35f, 1f, fade);
                    root.transform.position = position + Vector3.up * t * 0.2f;
                    body.SetFrame(alpha, appear);
                    for (int i = 0; i < lines.Count; i++)
                        UpdateLine(lines[i], t, alpha, windOrb);
                    if (!manifested && t >= 0.5f)
                    {
                        manifested = true;
                        onManifested?.Invoke(root.transform.position);
                    }
                    if (t >= 0.68f) break;
                    yield return null;
                }
            }
            finally
            {
                body?.Dispose();
                _roots.Remove(root);
                if (root != null) Destroy(root);
            }
        }

        private static ArtifactParticleBody BuildQuantityBracelet(Transform root, VfxProfile profile,
            List<AccentLine> lines)
        {
            Color jade = profile.selfEffectPrimaryColor;
            Color gold = profile.selfEffectSecondaryColor;
            Color shade = Color.Lerp(jade, new Color(0.015f, 0.12f, 0.085f, jade.a), 0.58f);
            Color light = Color.Lerp(jade, new Color(0.85f, 1f, 0.91f, jade.a), 0.58f);
            // Dense color blocks form one complete bangle, leaving its centre
            // transparent. A narrow highlight gives jade thickness without glow.
            var body = new ArtifactParticleBody.Builder();
            body.AddStroke(BraceletArc(0f, Mathf.PI * 2f, 56,
                new Vector3(0f, -0.035f)), shade, 0.20f, true);
            body.AddStroke(BraceletArc(0f, Mathf.PI * 2f, 56, Vector3.zero), jade, 0.16f, true);
            body.AddStroke(BraceletArc(0.16f, Mathf.PI * 0.78f, 28,
                new Vector3(0f, 0.035f)), light, 0.028f);
            for (int i = 0; i < 2; i++)
                body.AddStroke(BraceletArc(i * Mathf.PI + 0.48f, 0.12f, 7,
                    Vector3.zero), gold, 0.17f);

            // Retain the serialized field for existing assets. This counts
            // decorative shadow rings only; the receipt supplies the real gain.
            int count = Mathf.Clamp(profile.quantityMechanismCount, 1, 2);
            for (int i = 0; i < count; i++)
            {
                Color shadow = jade;
                shadow.a *= 0.35f;
                AddLine(root, lines, LineRole.Shadow, i, shadow, 0.13f, true,
                    BraceletArc(0f, Mathf.PI * 2f, 56, Vector3.zero));
                Color rim = gold;
                rim.a *= 0.6f;
                AddLine(root, lines, LineRole.Shadow, i, rim, 0.021f, true,
                    BraceletArc(0f, Mathf.PI * 2f, 56, Vector3.zero));
                lines[lines.Count - 1].Renderer.sortingOrder = 83;
            }
            AddMotes(root, profile, lines);
            return body.Create(root);
        }

        private static Vector3[] BraceletArc(float start, float span, int count,
            Vector3 offset)
        {
            var points = new Vector3[count];
            Quaternion tilt = Quaternion.Euler(0f, 0f, 22f);
            bool loop = span >= Mathf.PI * 2f - 0.001f;
            for (int i = 0; i < count; i++)
            {
                float angle = start + span * i / (loop ? count : count - 1f);
                points[i] = tilt * (new Vector3(Mathf.Cos(angle) * 0.58f,
                    Mathf.Sin(angle) * 0.37f, 0f) + offset);
            }
            return points;
        }

        private static ArtifactParticleBody BuildWindOrb(Transform root, VfxProfile profile,
            List<AccentLine> lines)
        {
            Color jade = profile.selfEffectPrimaryColor;
            Color gold = profile.selfEffectSecondaryColor;
            Color shade = Color.Lerp(jade, new Color(0.03f, 0.24f, 0.21f, jade.a), 0.45f);
            Color light = Color.Lerp(jade, new Color(0.78f, 1f, 0.92f, jade.a), 0.62f);
            var body = new ArtifactParticleBody.Builder();
            body.AddDisc(Vector3.zero, 0.32f, jade);
            body.AddStroke(Circle(0.34f, 0.34f, 48, Vector3.zero), gold, 0.04f, true);
            body.AddStroke(new[] { new Vector3(-0.23f, -0.09f), new Vector3(-0.14f, -0.24f),
                new Vector3(0.06f, -0.26f), new Vector3(0.22f, -0.13f) }, shade, 0.055f);
            body.AddStroke(new[] { new Vector3(-0.20f, 0.11f), new Vector3(-0.13f, 0.21f),
                new Vector3(0.02f, 0.25f), new Vector3(0.16f, 0.17f) }, light, 0.035f);
            // A restrained cloud curl makes the pearl distinct from a plain ring.
            body.AddStroke(new[] { new Vector3(-0.16f, 0.015f), new Vector3(-0.10f, 0.07f),
                new Vector3(0.04f, 0.07f), new Vector3(0.13f, 0.015f),
                new Vector3(0.06f, -0.05f), new Vector3(-0.05f, -0.05f) }, light, 0.025f);
            AddLine(root, lines, LineRole.Ring, 0, gold, 0.026f, true,
                Circle(0.31f, 0.11f, 32, Vector3.zero));
            AddLine(root, lines, LineRole.Ring, 1, jade, 0.021f, true,
                Circle(0.51f, 0.14f, 40, Vector3.zero));
            for (int i = 0; i < 3; i++)
            {
                var points = new Vector3[26];
                for (int j = 0; j < points.Length; j++)
                {
                    float f = j / (float)(points.Length - 1);
                    float angle = f * Mathf.PI * 1.3f + i * Mathf.PI * 2f / 3f;
                    float radius = Mathf.Lerp(0.32f, 0.54f, f);
                    points[j] = new Vector3(Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius * 0.66f, 0f);
                }
                AddLine(root, lines, LineRole.Wind, i,
                    i == 1 ? gold : jade, 0.018f, false, points);
            }
            AddMotes(root, profile, lines);
            return body.Create(root);
        }

        private static void AddMotes(Transform root, VfxProfile profile,
            List<AccentLine> lines)
        {
            // These are ornament only. Keep a few quiet points around the dense
            // body instead of another expanding ring of detached fragments.
            int count = Mathf.Min(8, Mathf.Clamp(profile.selfEffectParticleCount, 0, 24));
            for (int i = 0; i < count; i++)
                AddLine(root, lines, LineRole.Mote, i,
                    i % 3 == 0 ? profile.selfEffectSecondaryColor : profile.selfEffectPrimaryColor,
                    0.016f, false, new Vector3(-0.011f, 0f), new Vector3(0.011f, 0f));
        }

        private static void UpdateLine(AccentLine line, float t, float alpha, bool windOrb)
        {
            if (line.Renderer == null) return;
            float spin = line.Role == LineRole.Wind ? t * 150f :
                line.Role == LineRole.Ring ? t * (windOrb ? 180f : 220f) : 0f;
            if (line.Role == LineRole.Shadow)
            {
                float unfold = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((t - 0.12f - line.Index * 0.06f) / 0.48f));
                float side = line.Index == 0 ? 1f : -1f;
                line.Renderer.transform.localScale = Vector3.one *
                    Mathf.Lerp(0.92f, 1.28f, unfold);
                line.Renderer.transform.localPosition = new Vector3(
                    side * 0.18f, side * 0.14f + 0.12f, 0f) * unfold;
                spin = side * 10f * unfold;
                alpha *= unfold * (1f - Mathf.SmoothStep(0.55f, 0.86f, t));
            }
            if (line.Role == LineRole.Mote)
            {
                float angle = line.Index * 2.39996f + t * 2.5f;
                float radius = windOrb ? 0.36f + (line.Index % 3) * 0.04f + t * 0.035f
                    : 0.47f + (line.Index % 3) * 0.055f + t * 0.055f;
                line.Renderer.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * (windOrb ? 0.8f : 0.7f)
                        + t * 0.06f, 0f);
                alpha *= 0.42f;
            }
            line.Renderer.transform.localRotation = Quaternion.Euler(0f, 0f, spin);
            for (int i = 0; i < line.Points.Length; i++)
                line.Renderer.SetPosition(i, line.Points[i]);
            Color color = line.Color;
            color.a *= alpha;
            line.Renderer.startColor = color;
            line.Renderer.endColor = color;
        }

        private static void AddLine(Transform root, List<AccentLine> lines,
            LineRole role, int index, Color color, float width, bool loop,
            params Vector3[] points)
        {
            var host = new GameObject(role + "_" + index);
            host.transform.SetParent(root, false);
            var renderer = host.AddComponent<LineRenderer>();
            renderer.useWorldSpace = false;
            renderer.sharedMaterial = GetMaterial();
            renderer.positionCount = points.Length;
            renderer.SetPositions(points);
            renderer.loop = loop;
            renderer.startWidth = width;
            renderer.endWidth = width;
            renderer.numCornerVertices = 2;
            renderer.numCapVertices = 2;
            renderer.sortingOrder = role == LineRole.Shadow ? 82 : 86;
            renderer.startColor = renderer.endColor = new Color(color.r, color.g, color.b, 0f);
            lines.Add(new AccentLine { Renderer = renderer, Points = points,
                Color = color, Role = role, Index = index });
        }

        private static Vector3[] Circle(float radiusX, float radiusY,
            int count, Vector3 center)
        {
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)count * Mathf.PI * 2f;
                points[i] = center + new Vector3(Mathf.Cos(angle) * radiusX,
                    Mathf.Sin(angle) * radiusY, 0f);
            }
            return points;
        }

        private static Material GetMaterial()
        {
            if (_lineMaterial != null) return _lineMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;
            _lineMaterial = new Material(shader) { name = "SupportArtifactLines",
                mainTexture = Texture2D.whiteTexture, hideFlags = HideFlags.HideAndDontSave };
            return _lineMaterial;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            var remaining = new List<GameObject>(_roots);
            _roots.Clear();
            for (int i = 0; i < remaining.Count; i++)
                if (remaining[i] != null) Destroy(remaining[i]);
        }
    }
}
