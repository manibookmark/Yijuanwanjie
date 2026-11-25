using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns short-lived book/script outlines and their local ornament. Once the
    /// shape contracts, the HUD owns its one gain glyph and absolute value.
    /// </summary>
    public sealed class GlyphSupportEffectPlayer : MonoBehaviour
    {
        private sealed class GlyphArtifact
        {
            public TalismanPaperVisual Talisman;
            public ArtifactParticleBody Body;
        }

        private readonly Dictionary<GameObject, GlyphArtifact> _active = new Dictionary<GameObject, GlyphArtifact>();
        private static Material _material;

        public void Play(Vector3 origin, VfxProfile profile, IReadOnlyList<Vector3> destinations, int manaGained = 0)
        {
            PlayArtifact(origin, profile, manaGained, null);
        }

        /// <summary>Shows the authored book or script and hands its actual world
        /// position to the HUD once. Local motes never fly to a second target.</summary>
        public bool PlayArtifact(Vector3 origin, VfxProfile profile, int manaGained,
            Action<Vector3> onManifested)
        {
            if (profile == null || !isActiveAndEnabled) return false;
            var root = new GameObject("SupportGlyphTransfer");
            root.transform.SetParent(transform, false);
            root.transform.position = origin;
            _active.Add(root, new GlyphArtifact());
            StartCoroutine(Animate(root, profile, manaGained, onManifested));
            return true;
        }

        private IEnumerator Animate(GameObject root, VfxProfile profile,
            int manaGained, Action<Vector3> onManifested)
        {
            try
            {
                bool book = profile.selfEffectStyle == CardSelfEffectStyle.ManaBook;
                bool talisman = profile.selfEffectStyle == CardSelfEffectStyle.SandSpirit;
                bool script = profile.selfEffectStyle == CardSelfEffectStyle.DealScript || talisman;
                bool manaArtifact = book || talisman;
                Vector3 origin = root.transform.position;
                TalismanInkMotif motif = talisman ? TalismanInkMotif.Mana : TalismanInkMotif.Unfold;
                Vector2 paperSize = TalismanPaperVisual.GetBodySize(profile, motif);
                Vector2 bodySize = book ? new Vector2(1.04f, 0.63f)
                    : new Vector2(paperSize.y, paperSize.x);
                float scale = CardCastPlacement.SupportScale(origin, profile, bodySize);
                // Resolve the complete silhouette after its readable size is known.
                // HUD transfer starts from this resolved position after manifestation.
                root.transform.position = CardCastPlacement.AboveCard(origin, profile, scale);
                Color primary = profile.selfEffectPrimaryColor;
                Color light = profile.selfEffectSecondaryColor;
                Color runeColor = manaArtifact ? Color.Lerp(primary, light, 0.34f) : light;
                var runes = new List<LineRenderer>();
                var body = new ArtifactParticleBody.Builder();
                if (book)
                {
                    // A dense, two-tone particle page replaces the flat fill;
                    // the book's open silhouette and authored ink stay distinct.
                    Color pageColor = Color.Lerp(primary, light, 0.84f);
                    pageColor.a *= 0.46f;
                    Color edgeColor = Color.Lerp(primary, light, 0.44f);
                    edgeColor.a *= 0.82f;
                    var leftPage = new[] {
                        new Vector3(-0.5f, -0.26f), new Vector3(-0.5f, 0.28f),
                        new Vector3(-0.1f, 0.21f), Vector3.zero };
                    var rightPage = new[] {
                        Vector3.zero, new Vector3(0.1f, 0.21f),
                        new Vector3(0.5f, 0.28f), new Vector3(0.5f, -0.26f) };
                    body.AddPolygon(leftPage, pageColor);
                    body.AddPolygon(rightPage, pageColor);
                    body.AddStroke(leftPage, edgeColor, 0.028f, true);
                    body.AddStroke(rightPage, edgeColor, 0.028f, true);
                    for (int p = 1; p < Mathf.Clamp(profile.bookPageLayers, 1, 5); p++)
                    {
                        float y = -p * 0.027f;
                        Color layerColor = edgeColor;
                        layerColor.a *= 0.68f;
                        body.AddStroke(new[] {
                            new Vector3(-0.5f, -0.26f + y, 0f), new Vector3(-0.08f, y, 0f),
                            new Vector3(0.5f, -0.26f + y, 0f) }, layerColor, 0.014f);
                    }
                    // Page contents are authored independently of the shared book
                    // outline. Zero keeps the wordless tome's pages entirely blank.
                    int runeCount = Mathf.Clamp(profile.bookRuneCount, 0, 18);
                    bool ornate = runeCount >= 12;
                    for (int i = 0; i < runeCount; i++)
                    {
                        int sideCount = Mathf.Max(1, (runeCount + 1) / 2);
                        float x = (i < sideCount ? -0.42f : 0.13f) +
                            (i % sideCount) * (0.27f / Mathf.Max(1, sideCount - 1));
                        Vector3[] strokes = ornate
                            ? new[] { new Vector3(x, 0.14f, 0f), new Vector3(x + 0.028f, 0.105f, 0f),
                                new Vector3(x - 0.012f, 0.06f, 0f), new Vector3(x + 0.025f, 0.005f, 0f),
                                new Vector3(x - 0.018f, -0.06f, 0f), new Vector3(x + 0.025f, -0.13f, 0f) }
                            : new[] { new Vector3(x, 0.14f, 0f), new Vector3(x + 0.03f, 0.06f, 0f),
                                new Vector3(x - 0.02f, 0.005f, 0f), new Vector3(x + 0.04f, -0.13f, 0f) };
                        runes.Add(Line(root.transform, "ManaRune", light, ornate ? 0.017f : 0.024f, strokes));
                    }
                    if (ornate)
                    {
                        for (int side = 0; side < 2; side++)
                        {
                            float x = side == 0 ? -0.28f : 0.28f;
                            runes.Add(Line(root.transform, "PageRuneSeal", light, 0.018f,
                                new Vector3(x, -0.07f, 0f), new Vector3(x + 0.06f, -0.13f, 0f),
                                new Vector3(x, -0.19f, 0f), new Vector3(x - 0.06f, -0.13f, 0f),
                                new Vector3(x, -0.07f, 0f)));
                        }
                    }
                }
                else
                {
                    var paperHost = new GameObject("SupportTalismanPaper");
                    paperHost.transform.SetParent(root.transform, false);
                    paperHost.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    var paper = new TalismanPaperVisual();
                    _active[root].Talisman = paper;
                    paper.Initialize(paperHost.transform, profile, motif, 82);
                    paper.SetStrokeReveal(0f);
                    paper.SetOpacity(0f);
                    // Short particles sit inside the existing rim, leaving the
                    // shared paper, ink drawing and fire-card consumers intact.
                    Color cornerColor = Color.Lerp(primary, light, 0.56f);
                    cornerColor.a *= 0.52f;
                    float cornerX = bodySize.x * 0.37f;
                    float cornerY = bodySize.y * 0.44f;
                    for (int side = -1; side <= 1; side += 2)
                        for (int end = -1; end <= 1; end += 2)
                            body.AddStroke(new[] {
                                new Vector3(side * (cornerX - 0.045f), end * cornerY),
                                new Vector3(side * cornerX, end * cornerY),
                                new Vector3(side * cornerX, end * (cornerY - 0.07f)) },
                                cornerColor, 0.012f);
                }
                _active[root].Body = body.Create(root.transform, book ? 84 : 83);
                // The receipt controls visual density only; these points do not
                // represent separate gains or cap the authoritative MP amount.
                int visibleMana = Mathf.Clamp(manaGained, 0, 5);
                int moteCount = book && visibleMana > 0
                    ? Mathf.Min(112, Mathf.Clamp(profile.selfEffectParticleCount, 16, 112) + (visibleMana - 1) * 16)
                    : 0;
                Color moteColor = Color.Lerp(primary, light, 0.68f);
                BookMoteInfinityVisual motes = moteCount > 0
                    ? BookMoteInfinityVisual.Create(root.transform, moteColor, moteCount)
                    : null;
                foreach (LineRenderer rune in runes) rune.sortingOrder = 86;
                // Books release their mana when they open, without a hold.
                // Talisman drawing and its one HUD transfer retain their timing.
                float duration = book ? Mathf.Clamp(profile.selfEffectDuration, 0.45f, 0.65f)
                    : script ? Mathf.Clamp(profile.selfEffectDuration, 1.65f, 2.2f)
                    : !manaArtifact ? Mathf.Clamp(profile.selfEffectDuration, 0.55f, 0.9f)
                    : Mathf.Clamp(profile.selfEffectDuration, 0.85f, 1.4f);
                float fadeStart = book ? 0.36f : script ? 0.82f : 0.42f;
                float endAt = book || script ? 1f : 0.68f;
                float transferAt = book ? 0.18f : script ? fadeStart : 0.5f;
                float elapsed = 0f;
                bool manifested = false;
                while (root != null && elapsed < duration && isActiveAndEnabled)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, endAt, t));
                    float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (script ? 0.1f : book ? 0.18f : 0.23f)));
                    root.transform.localScale = Vector3.one * scale *
                        (script ? 1f : 0.55f + open * 0.45f) * Mathf.Lerp(0.35f, 1f, fade);
                    float inkReveal = Mathf.Clamp01(Mathf.InverseLerp(0.1f, 0.52f, t));
                    _active[root].Body.SetFrame(open * fade *
                        (script ? Mathf.Lerp(0.25f, 1f, inkReveal) : 1f), open);
                    for (int i = 0; i < runes.Count; i++)
                        Tint(runes[i], runeColor, fade * open);
                    if (script)
                    {
                        TalismanPaperVisual paper = _active[root].Talisman;
                        paper.SetOpacity(open * fade);
                        paper.SetStrokeReveal(inkReveal);
                    }
                    if (motes != null)
                    {
                        float handoffFade = book ? 1f - Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(transferAt, fadeStart, t)) : 1f;
                        motes.SetOpacity(open * fade * handoffFade);
                    }
                    if (!manifested && t >= transferAt)
                    {
                        manifested = true;
                        onManifested?.Invoke(root.transform.position);
                    }
                    if (t >= endAt) break;
                    yield return null;
                }
            }
            finally
            {
                DestroyArtifact(root);
            }
        }

        private void DestroyArtifact(GameObject root)
        {
            if (ReferenceEquals(root, null)) return;
            if (_active.TryGetValue(root, out GlyphArtifact artifact))
            {
                _active.Remove(root);
                artifact.Talisman?.Cleanup();
                artifact.Body?.Dispose();
            }
            if (root != null) Destroy(root);
        }

        private static Material GetMaterial()
        {
            if (_material == null)
                _material = new Material(Shader.Find("Sprites/Default"))
                    { hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };
            return _material;
        }

        private static LineRenderer Line(Transform parent, string name, Color color, float width,
            params Vector3[] points)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = GetMaterial();
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.sortingOrder = 85;
            line.numCornerVertices = 3;
            return line;
        }

        private static void Tint(LineRenderer line, Color baseColor, float opacity)
        {
            baseColor.a *= opacity;
            line.startColor = line.endColor = baseColor;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            foreach (GameObject root in new List<GameObject>(_active.Keys)) DestroyArtifact(root);
        }
    }
}
