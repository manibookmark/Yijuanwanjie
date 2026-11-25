using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.UI
{
    /// <summary>
    /// 运行时生成 9-slice 圆角矩形 Sprite，用于 Mask 裁切和 ThinBorder 边线。
    /// 使用超采样抗锯齿，按 cornerRadius 缓存。
    /// </summary>
    public static class RoundedRectSpriteGenerator
    {
        private const float PixelsPerUnit = 100f;
        private const int SSAA = 4;

        private static readonly Dictionary<int, Sprite> _filledCache = new();
        private static readonly Dictionary<int, Sprite> _outlineCache = new();

        public static Sprite GetFilled(int radius)
        {
            radius = Mathf.Clamp(radius, 0, 32);
            if (_filledCache.TryGetValue(radius, out Sprite s) && s != null)
                return s;

            int size = Mathf.Max(128, radius * 8 + 16);
            var tex = CreateTexture(size, size, radius, true, 0);
            s = CreateSprite(tex, radius);
            _filledCache[radius] = s;
            return s;
        }

        public static Sprite GetOutline(int radius, int borderWidth = 1)
        {
            radius = Mathf.Clamp(radius, 0, 32);
            borderWidth = Mathf.Clamp(borderWidth, 1, 5);
            int key = radius * 10 + borderWidth;
            if (_outlineCache.TryGetValue(key, out Sprite s) && s != null)
                return s;

            int size = Mathf.Max(128, radius * 8 + 16);
            var tex = CreateTexture(size, size, radius, false, borderWidth);
            s = CreateSprite(tex, radius);
            _outlineCache[key] = s;
            return s;
        }

        private static Sprite CreateSprite(Texture2D tex, int radius)
        {
            int size = tex.width;
            var s = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit,
                extrude: 0,
                meshType: SpriteMeshType.FullRect,
                border: new Vector4(radius, radius, radius, radius));
            s.name = tex.name;
            return s;
        }

        private static Texture2D CreateTexture(
            int width, int height, int radius, bool filled, int borderWidth)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[width * height];

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float alpha = 0f;
                    for (int sy = 0; sy < SSAA; sy++)
                    {
                        for (int sx = 0; sx < SSAA; sx++)
                        {
                            float px = x + (sx + 0.5f) / SSAA;
                            float py = y + (sy + 0.5f) / SSAA;
                            float d = SignedDistanceToRoundedRect(
                                px, py, width, height, radius);

                            if (filled)
                            {
                                // Smooth edge: full inside, fade at border
                                alpha += Mathf.Clamp01(0.5f - d);
                            }
                            else
                            {
                                // Outline: white in [-borderWidth, 0] band
                                float outer = Mathf.Clamp01(0.5f - d);
                                float inner = Mathf.Clamp01(d + borderWidth + 0.5f);
                                alpha += outer * inner;
                            }
                        }
                    }
                    alpha /= (SSAA * SSAA);
                    pixels[y * width + x] =
                        new Color32(255, 255, 255, (byte)(alpha * 255f + 0.5f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            tex.name = filled
                ? $"rounded-filled-r{radius}"
                : $"rounded-outline-r{radius}-b{borderWidth}";
            return tex;
        }

        /// <summary>
        /// 精确的有符号距离：负值在圆角矩形内部，正值在外部。
        /// 使用中心对称方法：计算到最近边/角的距离。
        /// </summary>
        private static float SignedDistanceToRoundedRect(
            float px, float py, int width, int height, int radius)
        {
            if (radius <= 0)
            {
                // Plain rectangle: distance to nearest edge
                float dx = Mathf.Min(px, width - 1 - px);
                float dy = Mathf.Min(py, height - 1 - py);
                return -Mathf.Min(dx, dy);
            }

            // Map to centered coordinates
            float cx = px - width * 0.5f;
            float cy = py - height * 0.5f;

            // Inner rectangle half-size (excluding corners)
            float innerHalfW = width * 0.5f - radius;
            float innerHalfH = height * 0.5f - radius;

            // Distance to nearest edge of the inner rect
            float qx = Mathf.Abs(cx) - innerHalfW;
            float qy = Mathf.Abs(cy) - innerHalfH;

            // If both negative: we're in the inner rect (distance = -min)
            // If one positive: we're in the edge band (distance = -other)
            // If both positive: we're in the corner (distance = sqrt(qx²+qy²) - radius)

            float outsideDist = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                           Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) - radius;
            float insideDist = Mathf.Min(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f));

            return outsideDist != 0f ? outsideDist : -insideDist;
        }
    }
}
