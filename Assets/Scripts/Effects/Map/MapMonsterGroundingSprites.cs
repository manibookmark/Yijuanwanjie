using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    /// <summary>集中生成并缓存怪物贴地层使用的程序化 Sprite。</summary>
    internal static class MapMonsterGroundingSprites
    {
        private static Sprite _softInk;
        private static Sprite _footVeil;

        public static Sprite SoftInk => _softInk != null
            ? _softInk
            : _softInk = BuildSoftInk();

        public static Sprite FootVeil => _footVeil != null
            ? _footVeil
            : _footVeil = BuildFootVeil();

        public static float ResolveVisibleBottom(Sprite sprite)
        {
            Vector2[] vertices = sprite.vertices;
            if (vertices == null || vertices.Length == 0)
                return sprite.bounds.min.y;

            float bottom = vertices[0].y;
            for (int i = 1; i < vertices.Length; i++)
                bottom = Mathf.Min(bottom, vertices[i].y);
            return bottom;
        }

        public static Color WithStateAlpha(
            Color color,
            MapVfxState state)
        {
            float stateAlpha = state switch
            {
                MapVfxState.Locked => 0.42f,
                MapVfxState.Available => 1f,
                MapVfxState.Hovered => 1.12f,
                _ => 0.72f
            };
            color.a = Mathf.Clamp01(color.a * stateAlpha);
            return color;
        }

        private static Sprite BuildSoftInk()
        {
            const int size = 96;
            var texture = CreateTexture("MapMonsterSoftInk", size, size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float ny = ((y + 0.5f) / size) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size) * 2f - 1f;
                    float angle = Mathf.Atan2(ny, nx);
                    float edge = 0.91f +
                                 Mathf.Sin(angle * 5f + 0.7f) * 0.035f +
                                 Mathf.Sin(angle * 9f - 1.1f) * 0.025f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny) / edge;
                    float alpha = Mathf.Clamp01((1f - radius) / 0.42f);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    float grain = 0.86f +
                                  Mathf.PerlinNoise(x * 0.11f, y * 0.11f) *
                                  0.14f;
                    pixels[y * size + x] = new Color(
                        1f, 1f, 1f, alpha * grain);
                }
            }
            texture.SetPixels32(pixels);
            return CreateSprite(texture, size, size);
        }

        private static Sprite BuildFootVeil()
        {
            const int width = 128;
            const int height = 64;
            var texture = CreateTexture(
                "MapMonsterFootVeil", width, height);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float vertical = (y + 0.5f) / height;
                float verticalFade = Mathf.Sin(vertical * Mathf.PI);
                for (int x = 0; x < width; x++)
                {
                    float nx = ((x + 0.5f) / width) * 2f - 1f;
                    float horizontalFade = Mathf.Exp(-nx * nx * 2.8f);
                    float drift = 0.78f + 0.22f * Mathf.Sin(
                        nx * 8.5f + vertical * 6.2f);
                    float alpha = Mathf.Pow(verticalFade, 0.72f) *
                                  horizontalFade * drift;
                    pixels[y * width + x] = new Color(
                        1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }
            texture.SetPixels32(pixels);
            return CreateSprite(texture, width, height);
        }

        private static Texture2D CreateTexture(
            string name,
            int width,
            int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private static Sprite CreateSprite(
            Texture2D texture,
            int width,
            int height)
        {
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                width);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
