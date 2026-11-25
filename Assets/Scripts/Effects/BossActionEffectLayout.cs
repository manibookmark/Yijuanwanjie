using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>一次 BOSS 动作特效在归一化视口中的中心与尺度。</summary>
    public readonly struct BossActionEffectLayoutSample
    {
        public BossActionEffectLayoutSample(
            Vector2 center,
            Vector2 radius,
            float lineWidth,
            float particleSize)
        {
            Center = center;
            Radius = radius;
            LineWidth = lineWidth;
            ParticleSize = particleSize;
        }

        public Vector2 Center { get; }
        public Vector2 Radius { get; }
        public float LineWidth { get; }
        public float ParticleSize { get; }
    }

    /// <summary>
    /// BOSS 动作特效尺寸适配的唯一所有者。调用方只提供角色实际视口边界；
    /// 重拳、防御与横扫如何取中心、半径和粒径都被隐藏在这里，F10 与实战
    /// 因而不会各自复制一套比例常量。
    /// </summary>
    public static class BossActionEffectLayout
    {
        private static readonly Rect FallbackSourceRect =
            new Rect(0.35f, 0.18f, 0.3f, 0.48f);

        public static BossActionEffectLayoutSample Resolve(
            BossActionEffectVisualKind kind,
            Rect sourceViewportRect)
        {
            Rect source = NormalizeSourceRect(sourceViewportRect);
            float sourceHeight = Mathf.Clamp(source.height, 0.18f, 0.78f);

            switch (kind)
            {
                case BossActionEffectVisualKind.HeavyPunchImpact:
                    float punchRadius = Mathf.Clamp(
                        0.105f + sourceHeight * 0.12f,
                        0.14f,
                        0.205f);
                    return new BossActionEffectLayoutSample(
                        new Vector2(0.5f, 0.5f),
                        new Vector2(punchRadius, punchRadius * 0.9f),
                        Mathf.Clamp(0.007f + sourceHeight * 0.009f,
                            0.009f, 0.014f),
                        Mathf.Clamp(0.011f + sourceHeight * 0.015f,
                            0.014f, 0.023f));

                case BossActionEffectVisualKind.ArmorCompress:
                    Vector2 guardCenter = new Vector2(
                        Mathf.Clamp(source.center.x, 0.16f, 0.84f),
                        Mathf.Clamp(source.center.y, 0.19f, 0.78f));
                    float guardRadiusY = Mathf.Clamp(
                        sourceHeight * 0.50f,
                        0.19f,
                        0.36f);
                    return new BossActionEffectLayoutSample(
                        guardCenter,
                        new Vector2(
                            guardRadiusY * 0.45f,
                            guardRadiusY),
                        Mathf.Clamp(
                            sourceHeight * 0.026f,
                            0.0065f, 0.013f),
                        Mathf.Clamp(sourceHeight * 0.034f,
                            0.012f, 0.024f));

                default:
                    return new BossActionEffectLayoutSample(
                        new Vector2(0.5f, 0.34f),
                        new Vector2(0.56f,
                            Mathf.Clamp(0.065f + sourceHeight * 0.055f,
                                0.075f, 0.108f)),
                        Mathf.Clamp(0.009f + sourceHeight * 0.012f,
                            0.011f, 0.017f),
                        Mathf.Clamp(0.01f + sourceHeight * 0.012f,
                            0.012f, 0.019f));
            }
        }

        /// <summary>
        /// Keeps the selected effect's geometry while moving its contact point.
        /// A projected anchor must be finite, in front of the camera and inside
        /// the viewport; invalid input never silently becomes a central hit.
        /// </summary>
        public static bool TryResolveAtViewportAnchor(
            BossActionEffectVisualKind kind,
            Rect sourceViewportRect,
            Vector3 projectedAnchor,
            out BossActionEffectLayoutSample layout)
        {
            layout = default;
            if (!Finite(projectedAnchor.x) || !Finite(projectedAnchor.y) ||
                !Finite(projectedAnchor.z) || projectedAnchor.z <= 0f ||
                projectedAnchor.x < 0f || projectedAnchor.x > 1f ||
                projectedAnchor.y < 0f || projectedAnchor.y > 1f)
            {
                return false;
            }

            BossActionEffectLayoutSample source = Resolve(kind, sourceViewportRect);
            layout = new BossActionEffectLayoutSample(
                new Vector2(projectedAnchor.x, projectedAnchor.y),
                source.Radius,
                source.LineWidth,
                source.ParticleSize);
            return true;
        }

        public static bool TryGetRendererViewportRect(
            Camera camera,
            Renderer renderer,
            out Rect viewportRect)
        {
            viewportRect = default;
            if (camera == null || renderer == null)
                return false;

            Bounds bounds = renderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            bool found = false;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 viewport = camera.WorldToViewportPoint(
                            new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z));
                        if (viewport.z <= 0f)
                            continue;
                        found = true;
                        minX = Mathf.Min(minX, viewport.x);
                        minY = Mathf.Min(minY, viewport.y);
                        maxX = Mathf.Max(maxX, viewport.x);
                        maxY = Mathf.Max(maxY, viewport.y);
                    }
                }
            }

            if (!found || maxX <= minX || maxY <= minY)
                return false;
            viewportRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        public static Rect BuildFallbackSourceRect(
            Camera camera,
            Vector3 worldAnchor)
        {
            if (camera == null)
                return FallbackSourceRect;
            Vector3 viewport = camera.WorldToViewportPoint(worldAnchor);
            if (viewport.z <= 0f)
                return FallbackSourceRect;
            return new Rect(
                viewport.x - FallbackSourceRect.width * 0.5f,
                viewport.y - FallbackSourceRect.height * 0.5f,
                FallbackSourceRect.width,
                FallbackSourceRect.height);
        }

        private static Rect NormalizeSourceRect(Rect source)
        {
            if (source.width <= 0.001f || source.height <= 0.001f ||
                float.IsNaN(source.x) || float.IsNaN(source.y) ||
                float.IsNaN(source.width) || float.IsNaN(source.height))
            {
                return FallbackSourceRect;
            }

            return source;
        }

        private static bool Finite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
