using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns short casting artifacts: a golden thunder ring or a wooden sword
    /// that conducts lightning. Each cast owns its sampled body and motion lines;
    /// this owner alone animates, cancels and releases them. Enemy strikes,
    /// audio, hit timing and damage remain in the authoritative attack player.
    /// Static body particle state belongs to ArtifactParticleBody; this player
    /// owns the cast clock and cancellation. No Infinity or combat callbacks.
    /// </summary>
    public sealed class LightningArtifactSelfEffectPlayer : MonoBehaviour
    {
        private enum AccentRole { Ring, Electric, Tassel, Mote }

        private sealed class Accent
        {
            public LineRenderer Renderer;
            public Vector3[] Points;
            public Color Color;
            public AccentRole Role;
            public int Index;
        }

        private sealed class Cast
        {
            public GameObject Root;
            public readonly List<ArtifactParticleBody> Bodies = new List<ArtifactParticleBody>();
            public readonly List<Accent> Accents = new List<Accent>();
            public Transform InnerRing;
            public bool IsSword;
        }

        private readonly HashSet<Cast> _casts = new HashSet<Cast>();
        private Material _material;

        public void Play(Vector3 position, VfxProfile profile)
        {
            if (!isActiveAndEnabled || profile == null ||
                (profile.selfEffectStyle != CardSelfEffectStyle.LightningRing &&
                 profile.selfEffectStyle != CardSelfEffectStyle.ThunderWoodSword)) return;
            StartCoroutine(Animate(position, profile));
        }

        private IEnumerator Animate(Vector3 position, VfxProfile profile)
        {
            // A newly added owner must settle in the active hierarchy before
            // allocating renderers. Cancelling during this frame creates none.
            yield return null;
            if (!isActiveAndEnabled || profile == null || !EnsureMaterial()) yield break;
            var cast = new Cast
            {
                Root = new GameObject("CastingArtifact_" + profile.selfEffectStyle),
                IsSword = profile.selfEffectStyle == CardSelfEffectStyle.ThunderWoodSword
            };
            position = ResolveArtifactOrigin(position, profile, cast.IsSword);
            cast.Root.transform.SetParent(transform, false);
            cast.Root.transform.position = position;
            _casts.Add(cast);
            try
            {
                if (cast.IsSword) BuildSword(cast, profile);
                else BuildRing(cast, profile);
                AddMotes(cast, profile);
                float duration = Mathf.Clamp(profile.selfEffectDuration, 0.55f, 0.8f);
                float scale = Mathf.Clamp(profile.selfEffectScale, 0.5f, 1.4f);
                float elapsed = 0f;
                SetBodyFrame(cast, 0f, 0f);
                while (elapsed < duration && cast.Root != null && isActiveAndEnabled)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float appear = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.16f));
                    float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, t));
                    float alpha = appear * fade;
                    cast.Root.transform.localScale = Vector3.one * scale * Mathf.Lerp(0.86f, 1f, appear);
                    cast.Root.transform.position = position + Vector3.up * (0.08f * t);
                    cast.Root.transform.localRotation = Quaternion.Euler(0f, 0f,
                        cast.IsSword ? Mathf.Lerp(-12f, -5f, appear) : Mathf.Sin(t * Mathf.PI) * 3f);
                    if (cast.InnerRing != null)
                        cast.InnerRing.localRotation = Quaternion.Euler(0f, 0f, t * 36f);
                    for (int i = 0; i < cast.Accents.Count; i++) UpdateAccent(cast.Accents[i], t, alpha);
                    SetBodyFrame(cast, alpha, appear);
                    yield return null;
                }
            }
            finally
            {
                Cleanup(cast);
            }
        }

        private void BuildRing(Cast cast, VfxProfile profile)
        {
            Color gold = profile.selfEffectPrimaryColor;
            Color blue = profile.selfEffectSecondaryColor;
            var body = new ArtifactParticleBody.Builder(0.02f);
            body.AddStroke(Circle(0.46f, 96), gold, 0.065f, true);
            body.AddStroke(Circle(0.39f, 80), gold, 0.024f, true);
            // The outer blue line conducts lightning; it is not a second metal
            // silhouette. The fixed metal is rendered only by sampled particles.
            AddLine(cast, AccentRole.Ring, 0, blue, 0.015f, true, Circle(0.53f, 64));
            if (profile.lightningRingLayerCount > 1)
            {
                Vector3[] inner = Circle(0.3f, 56);
                for (int i = 0; i < inner.Length; i++) inner[i].y *= 0.78f;
                var innerRoot = new GameObject("InnerThunderRing");
                innerRoot.transform.SetParent(cast.Root.transform, false);
                cast.InnerRing = innerRoot.transform;
                var innerBody = new ArtifactParticleBody.Builder(0.02f);
                innerBody.AddStroke(inner, gold, 0.06f, true);
                cast.Bodies.Add(innerBody.Create(cast.InnerRing));
                for (int i = 0; i < 3; i++)
                {
                    float angle = i * Mathf.PI * 2f / 3f + 0.4f;
                    Vector3 center = new Vector3(Mathf.Cos(angle) * 0.46f,
                        Mathf.Sin(angle) * 0.46f, 0f);
                    Vector3[] gem = Circle(0.052f, 16);
                    for (int j = 0; j < gem.Length; j++) gem[j] += center;
                    body.AddStroke(gem, new Color(1f, 0.32f, 0.065f, 1f), 0.045f, true);
                    AddLine(cast, AccentRole.Electric, 4 + i, blue, 0.029f, false,
                        center, center * 0.84f + Vector3.right * 0.04f,
                        center * 0.65f - Vector3.right * 0.035f);
                }
            }
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 tangent = new Vector3(-radial.y, radial.x, 0f);
                body.AddStroke(new[]
                {
                    radial * 0.39f - tangent * 0.028f,
                    radial * 0.45f - tangent * 0.028f,
                    radial * 0.45f + tangent * 0.028f,
                    radial * 0.48f + tangent * 0.028f
                }, gold, 0.019f);
            }
            cast.Bodies.Add(body.Create(cast.Root.transform));
            for (int i = 0; i < 4; i++)
            {
                var points = new Vector3[11];
                for (int j = 0; j < points.Length; j++)
                {
                    float f = j / (float)(points.Length - 1);
                    float angle = i * Mathf.PI * 0.5f + f * 1.1f;
                    float radius = 0.56f + Mathf.Sin(f * Mathf.PI) * 0.065f;
                    points[j] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                }
                AddLine(cast, AccentRole.Electric, i, blue, 0.023f, false, points);
            }
        }

        private static Vector3 ResolveArtifactOrigin(Vector3 cardPosition, VfxProfile profile, bool sword)
        {
            Vector3 origin = cardPosition + profile.selfEffectOffset;
            if (!sword) return origin;
            Camera camera = Camera.main;
            if (camera == null) return cardPosition + new Vector3(profile.selfEffectOffset.x,
                Mathf.Max(1.65f, profile.selfEffectOffset.y), profile.selfEffectOffset.z);
            Vector3 sourceViewport = camera.WorldToViewportPoint(cardPosition);
            Vector3 originViewport = camera.WorldToViewportPoint(origin);
            if (sourceViewport.z <= 0f || originViewport.z <= 0f) return origin;
            float scale = Mathf.Clamp(profile.selfEffectScale, 0.5f, 1.4f);
            Vector3 oneScaleUp = camera.WorldToViewportPoint(cardPosition + Vector3.up * scale);
            float unitHeight = Mathf.Abs(oneScaleUp.y - sourceViewport.y);
            // The tassel/handle extends below the root. Raise that lowest point,
            // rather than just the blade centre, clear of the Overlay card.
            float bottomMargin = unitHeight * 1.02f;
            float topMargin = unitHeight * 1.4f;
            float uiScale = Mathf.Max(0.1f, Screen.height / 1080f);
            float cameraHeight = Mathf.Max(1f, camera.pixelRect.height);
            float cardClearance = (profile.castingArtifactCardClearancePixels + 18f) * uiScale / cameraHeight;
            float minimumRoot = Mathf.Max(sourceViewport.y + cardClearance, 0.36f) + bottomMargin;
            float maximumRoot = Mathf.Max(0.1f, 0.97f - topMargin);
            originViewport.y = Mathf.Clamp(Mathf.Max(originViewport.y, minimumRoot), 0.1f, maximumRoot);
            originViewport.x = Mathf.Clamp(originViewport.x, 0.055f, 0.945f);
            // Viewport coordinates are relative to this camera's pixelRect,
            // including its offset and non-fullscreen dimensions. Keep depth.
            return camera.ViewportToWorldPoint(originViewport);
        }

        private void BuildSword(Cast cast, VfxProfile profile)
        {
            Color wood = profile.selfEffectPrimaryColor;
            Color gold = profile.selfEffectSecondaryColor;
            var body = new ArtifactParticleBody.Builder(0.02f);
            var blade = new[]
            {
                new Vector3(-0.1f, -0.17f, 0f), new Vector3(-0.14f, 0.56f, 0f),
                new Vector3(0f, 0.96f, 0f), new Vector3(0.14f, 0.56f, 0f),
                new Vector3(0.1f, -0.17f, 0f)
            };
            body.AddPolygon(blade, wood);
            body.AddStroke(blade, gold, 0.024f, true);
            body.AddPolygon(new[]
            {
                new Vector3(-0.3f, -0.16f, 0f), new Vector3(-0.3f, -0.25f, 0f),
                new Vector3(0.3f, -0.25f, 0f), new Vector3(0.3f, -0.16f, 0f)
            }, wood);
            body.AddStroke(new[] { new Vector3(-0.3f, -0.16f), new Vector3(0.3f, -0.16f) }, gold, 0.028f);
            body.AddStroke(new[] { new Vector3(0f, -0.25f), new Vector3(0f, -0.49f) },
                wood * new Color(0.75f, 0.75f, 0.75f, 1f), 0.1f);
            body.AddStroke(new[] { new Vector3(-0.055f, -0.49f), new Vector3(0.055f, -0.49f) }, gold, 0.035f);
            body.AddStroke(new[]
            {
                new Vector3(-0.018f, -0.1f), new Vector3(-0.04f, 0.3f),
                new Vector3(-0.01f, 0.69f)
            }, gold, 0.015f);
            for (int i = 0; i < 4; i++)
            {
                float y = 0.025f + i * 0.155f;
                body.AddStroke(new[]
                {
                    new Vector3(-0.06f, y + 0.04f), new Vector3(0.055f, y + 0.04f),
                    new Vector3(-0.04f, y - 0.025f), new Vector3(0.045f, y - 0.025f)
                }, gold, 0.018f);
            }
            cast.Bodies.Add(body.Create(cast.Root.transform));
            Color red = new Color(0.86f, 0.12f, 0.07f, 0.95f);
            AddLine(cast, AccentRole.Tassel, 0, red, 0.035f, false,
                new Vector3(0f, -0.48f), new Vector3(0.12f, -0.57f),
                new Vector3(0.17f, -0.68f), new Vector3(0.2f, -0.86f));
            AddLine(cast, AccentRole.Tassel, 1, red, 0.025f, false,
                new Vector3(0.15f, -0.63f), new Vector3(0.12f, -0.83f));
            AddLine(cast, AccentRole.Tassel, 2, red, 0.021f, false,
                new Vector3(0.15f, -0.63f), new Vector3(0.25f, -0.81f));
            AddLine(cast, AccentRole.Electric, 0, gold, 0.026f, false,
                new Vector3(0f, 0.91f), new Vector3(0.08f, 1.04f),
                new Vector3(-0.035f, 1.15f), new Vector3(0.045f, 1.29f));
            AddLine(cast, AccentRole.Electric, 1, gold, 0.018f, false,
                new Vector3(-0.06f, 0.63f), new Vector3(-0.27f, 0.77f),
                new Vector3(-0.22f, 0.93f), new Vector3(-0.36f, 1.02f));
        }

        private void AddMotes(Cast cast, VfxProfile profile)
        {
            // The densely sampled artifact carries the silhouette. A few quiet
            // motes suffice; extra orbiting specks would hide the ring or hilt.
            int count = Mathf.Clamp(profile.selfEffectParticleCount, 0, 6);
            for (int i = 0; i < count; i++)
                AddLine(cast, AccentRole.Mote, i, i % 2 == 0 ?
                    profile.selfEffectSecondaryColor : profile.selfEffectPrimaryColor,
                    0.018f, false, new Vector3(-0.015f, 0f), new Vector3(0.015f, 0f));
        }

        private static void UpdateAccent(Accent accent, float t, float alpha)
        {
            if (accent.Renderer == null) return;
            if (accent.Role == AccentRole.Ring)
                accent.Renderer.transform.localRotation = Quaternion.Euler(0f, 0f, t * 36f);
            else if (accent.Role == AccentRole.Mote)
            {
                float angle = accent.Index * 2.39996f + t * 1.1f;
                float radius = 0.57f + (accent.Index % 3) * 0.06f + t * 0.11f;
                accent.Renderer.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius + 0.1f * t, 0f);
                alpha *= 0.42f;
            }
            if (accent.Role == AccentRole.Electric || accent.Role == AccentRole.Tassel)
            {
                for (int j = 0; j < accent.Points.Length; j++)
                {
                    float f = j / (float)(accent.Points.Length - 1);
                    float sway = accent.Role == AccentRole.Tassel ?
                        Mathf.Sin(t * 11f + accent.Index * 0.6f) * f * 0.035f :
                        Mathf.Sin(t * 95f + j * 2.3f + accent.Index) * Mathf.Sin(f * Mathf.PI) * 0.028f;
                    accent.Renderer.SetPosition(j, accent.Points[j] + Vector3.right * sway);
                }
                if (accent.Role == AccentRole.Electric)
                    alpha *= 0.68f + 0.32f * Mathf.Abs(Mathf.Sin(t * 36f + accent.Index));
            }
            Color tint = accent.Color;
            tint.a *= alpha;
            accent.Renderer.startColor = tint;
            accent.Renderer.endColor = tint;
        }

        private void AddLine(Cast cast, AccentRole role, int index, Color color,
            float width, bool loop, params Vector3[] points)
        {
            var host = new GameObject(role + "_" + index);
            host.transform.SetParent(cast.Root.transform, false);
            var line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = _material;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.loop = loop;
            line.startWidth = width;
            line.endWidth = width;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.sortingOrder = 86;
            line.startColor = line.endColor = new Color(color.r, color.g, color.b, 0f);
            cast.Accents.Add(new Accent { Renderer = line, Points = points,
                Color = color, Role = role, Index = index });
        }

        private static void SetBodyFrame(Cast cast, float alpha, float formation)
        {
            for (int i = 0; i < cast.Bodies.Count; i++)
                cast.Bodies[i].SetFrame(alpha, formation);
        }

        private static Vector3[] Circle(float radius, int count)
        {
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                points[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            }
            return points;
        }

        private bool EnsureMaterial()
        {
            if (_material != null) return true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return false;
            _material = new Material(shader) { name = "CastingArtifactMaterial",
                mainTexture = Texture2D.whiteTexture, hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        private void Cleanup(Cast cast)
        {
            if (!_casts.Remove(cast)) return;
            // Dispose while the owned hierarchy is still active. Cancellation
            // and the coroutine's finally block share this idempotent guard.
            for (int i = 0; i < cast.Bodies.Count; i++) cast.Bodies[i].Dispose();
            cast.Bodies.Clear();
            if (cast.Root != null)
            {
                cast.Root.SetActive(false);
                DestroyOwned(cast.Root);
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            var remaining = new List<Cast>(_casts);
            for (int i = 0; i < remaining.Count; i++) Cleanup(remaining[i]);
            DestroyOwned(_material);
            _material = null;
        }

        private static void DestroyOwned(Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }
    }
}
