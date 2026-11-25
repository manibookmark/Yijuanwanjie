using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>
    /// Owns the procedural round sprite used by the mana pearl. The mana core
    /// must not reuse the irregular ground-ink texture used under monsters.
    /// </summary>
    internal static class ManaSpiritCoreSprites
    {
        private static Sprite _softOrb;
        private static Sprite _softHalo;
        private static Sprite _luminousManaCore;

        public static Sprite SoftOrb => _softOrb != null
            ? _softOrb
            : _softOrb = BuildSoftOrb();

        /// <summary>
        /// A continuously fading aura with a transparent safety margin. It is
        /// separate from SoftOrb so large reward pulses never expose the square
        /// sprite bounds while the accepted pearl silhouette stays unchanged.
        /// </summary>
        public static Sprite SoftHalo => _softHalo != null
            ? _softHalo
            : _softHalo = BuildSoftHalo();

        /// <summary>Mana-only light core; the accepted health pearl is unchanged.</summary>
        public static Sprite LuminousManaCore => _luminousManaCore != null
            ? _luminousManaCore
            : _luminousManaCore = BuildLuminousManaCore();

        private static Sprite BuildLuminousManaCore()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ManaLuminousCore",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny);
                    float fade = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.05f, 0.88f, radius));
                    float light = Mathf.Exp(-radius * radius * 13f);
                    float alpha = fade * (0.28f + light * 0.46f);
                    float brightness = Mathf.Lerp(0.82f, 1f, light);
                    pixels[y * size + x] = alpha > 0.0005f
                        ? new Color(brightness, brightness, brightness, alpha)
                        : Color.clear;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), size, 0u, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite BuildSoftOrb()
        {
            const int size = 192;
            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false)
            {
                name = "ManaSpiritSoftOrb",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = ((y + 0.5f) / size) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size) * 2f - 1f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny);
                    // Keep the pearl itself clearly circular. The separate halo
                    // and Infinity wisps own the soft, irregular outer aura.
                    // Mathf.SmoothStep(from, to, t) is an interpolation API,
                    // not GLSL smoothstep(edge0, edge1, value). Normalize the
                    // radius explicitly so every pixel outside the pearl is
                    // truly transparent instead of retaining a square 0.5%
                    // alpha floor.
                    float edgeProgress = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(0f, 0.995f, radius));
                    float edge = 0.10f * (1f - edgeProgress);
                    float body = 1f - Mathf.SmoothStep(
                        0.08f, 0.88f, radius);
                    float highlightDistance = Mathf.Sqrt(
                        Mathf.Pow(nx + 0.24f, 2f) +
                        Mathf.Pow(ny - 0.24f, 2f));
                    float highlight =
                        1f - Mathf.SmoothStep(0f, 0.52f, highlightDistance);
                    float brightness = Mathf.Lerp(
                        0.68f,
                        1f,
                        Mathf.Clamp01(body * 0.74f + highlight * 0.26f));
                    pixels[y * size + x] = edge > 0.0005f
                        ? new Color(
                            brightness,
                            brightness,
                            brightness,
                            Mathf.Clamp01(edge))
                        : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size,
                0u,
                SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite BuildSoftHalo()
        {
            const int size = 192;
            const float visibleRadius = 0.78f;
            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false)
            {
                name = "ManaSpiritSoftHalo",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = ((y + 0.5f) / size) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size) * 2f - 1f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny);
                    float fade = Mathf.Clamp01(
                        1f - radius / visibleRadius);
                    float alpha = Mathf.Pow(fade, 2.6f);
                    pixels[y * size + x] = alpha > 0.0005f
                        ? new Color(1f, 1f, 1f, alpha)
                        : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size,
                0u,
                SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
