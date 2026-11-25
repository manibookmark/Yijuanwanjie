using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// 三种 BOSS 动作的 Infinity 初始粒子场。几何由既有 Layout/Sampler
    /// 决定；这里只把它转成位置、速度、寿命和粒径，不拥有动作时钟。
    /// </summary>
    public static class BossActionInfinityParticles
    {
        public static BossInfinityParticleField Play(
            Transform effectParent,
            Vector3 worldCenter,
            float viewWidth,
            float viewHeight,
            BossActionEffectLayoutSample layout,
            BossActionEffectPreset preset,
            int sortingLayerId = 0)
        {
            if (preset == null || preset.particleCount <= 0)
                return null;

            int count = Mathf.Clamp(preset.particleCount, 0, 160);
            var seeds = new List<BossInfinityParticleSeed>(count);
            for (int i = 0; i < count; i++)
            {
                seeds.Add(GetSeed(
                    preset,
                    layout,
                    i,
                    count,
                    viewWidth,
                    viewHeight));
            }

            GetMaterialShape(
                preset.visualKind,
                out float softness,
                out float wispShape);
            return BossInfinityParticleField.Play(
                effectParent,
                worldCenter,
                $"BossInfinity_{preset.effectId}",
                seeds,
                new BossInfinityParticleAppearance(
                    preset.debrisColor,
                    preset.primaryColor,
                    preset.coreColor,
                    Mathf.Max(
                        preset.primaryColor.a,
                        preset.coreColor.a),
                    softness,
                    wispShape),
                sortingLayerId,
                BossActionEffectInstance.BaseSortingOrder + 4);
        }

        /// <summary>
        /// 可供 F10 消费的同一确定性种子。传入像素宽高时，返回值即为
        /// 审核画布像素轨迹；传入世界宽高时则供真实 Infinity 使用。
        /// </summary>
        public static BossInfinityParticleSeed GetSeed(
            BossActionEffectPreset preset,
            BossActionEffectLayoutSample layout,
            int index,
            int count,
            float viewWidth,
            float viewHeight)
        {
            float u = (index + 0.5f) / Mathf.Max(1, count);
            float n1 = Hash01(index, 0.173f);
            float n2 = Hash01(index, 0.617f);
            float n3 = Hash01(index, 0.941f);
            float scale = Mathf.Max(0.25f, preset.visualScale);
            Vector3 position;
            Vector3 velocity;
            float lifetime;
            float rotation;

            switch (preset.visualKind)
            {
                case BossActionEffectVisualKind.HorizontalSweep:
                {
                    float pathT = Mathf.Lerp(0f, 0.18f, n1);
                    Vector2 startViewport =
                        BossActionEffectSampler.GetSweepViewportPoint(pathT);
                    startViewport.x += Mathf.Lerp(0.02f, 0.12f, n2);
                    startViewport.y += (n3 - 0.5f) *
                                       layout.Radius.y * 1.35f;
                    Vector2 endViewport =
                        BossActionEffectSampler.GetSweepViewportPoint(
                            Mathf.Lerp(0.82f, 1f, n2));
                    endViewport.y += (n1 - 0.5f) *
                                     layout.Radius.y * 0.9f;
                    position = ViewportDeltaToLocal(
                        startViewport - layout.Center,
                        viewWidth,
                        viewHeight);
                    Vector3 end = ViewportDeltaToLocal(
                        endViewport - layout.Center,
                        viewWidth,
                        viewHeight);
                    lifetime = preset.duration * Mathf.Lerp(0.72f, 1f, n3);
                    velocity = (end - position) /
                               Mathf.Max(0.04f, lifetime);
                    rotation = Mathf.Atan2(velocity.y, velocity.x) *
                               Mathf.Rad2Deg;
                    break;
                }

                case BossActionEffectVisualKind.ArmorCompress:
                {
                    Vector2 shield = GetShieldParticleTarget(
                        index, count);
                    Vector3 target = new Vector3(
                        shield.x * viewWidth * layout.Radius.x * scale,
                        shield.y * viewHeight * layout.Radius.y * scale,
                        0f);
                    Vector2 scatterDirection = UnitDirection(n1);
                    Vector3 scatter = new Vector3(
                        scatterDirection.x * viewWidth *
                        layout.Radius.x * Mathf.Lerp(0.35f, 0.92f, n2),
                        scatterDirection.y * viewHeight *
                        layout.Radius.y * Mathf.Lerp(0.35f, 0.92f, n2),
                        0f);
                    Vector3 tangent = new Vector3(-target.y, target.x, 0f);
                    tangent = tangent.sqrMagnitude > 0.0001f
                        ? tangent.normalized * viewHeight *
                          layout.Radius.y * (n3 - 0.5f) * 0.42f
                        : Vector3.zero;
                    position = target * Mathf.Lerp(1.55f, 2.05f, n1) +
                               scatter + tangent;
                    lifetime = preset.duration * Mathf.Lerp(0.76f, 0.96f, n2);
                    velocity = (target - position) /
                               Mathf.Max(0.05f, lifetime);
                    rotation = Mathf.Lerp(-28f, 28f, n3);
                    break;
                }

                default:
                {
                    float angle = n1 * Mathf.PI * 2f +
                                  (n2 - 0.5f) * 0.42f;
                    Vector2 direction = new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle));
                    float startRadius = Mathf.Lerp(0.04f, 0.20f, n2);
                    float endRadius = Mathf.Lerp(0.92f, 1.55f, n3);
                    position = new Vector3(
                        direction.x * viewWidth * layout.Radius.x *
                        startRadius * scale,
                        direction.y * viewHeight * layout.Radius.y *
                        startRadius * scale,
                        0f);
                    Vector3 end = new Vector3(
                        direction.x * viewWidth * layout.Radius.x *
                        endRadius * scale,
                        direction.y * viewHeight * layout.Radius.y *
                        endRadius * scale,
                        0f);
                    lifetime = preset.duration * Mathf.Lerp(0.64f, 1.02f, n2);
                    velocity = (end - position) /
                               Mathf.Max(0.04f, lifetime);
                    rotation = angle * Mathf.Rad2Deg +
                               Mathf.Lerp(-24f, 24f, n3);
                    break;
                }
            }

            float sizeMultiplier = preset.visualKind switch
            {
                BossActionEffectVisualKind.HorizontalSweep =>
                    Mathf.Lerp(0.42f, 1.05f, n3),
                BossActionEffectVisualKind.ArmorCompress =>
                    Mathf.Lerp(0.32f, 0.78f, n3),
                _ => Mathf.Lerp(0.50f, 1.28f, n3)
            };
            float size = viewHeight * layout.ParticleSize * scale *
                         sizeMultiplier;
            Color color = index % 7 == 0
                ? preset.coreColor
                : index % 4 == 0
                    ? preset.debrisColor
                    : preset.primaryColor;
            color.a *= Mathf.Lerp(0.68f, 1f, n2);
            return new BossInfinityParticleSeed(
                position,
                velocity,
                lifetime,
                size,
                rotation,
                color);
        }

        /// <summary>
        /// 三套复用特效在同一无贴图 shader 上选取不同解析轮廓：重拳为
        /// 锐利碎片、横扫为短尾气压、防御为紧实光点。
        /// </summary>
        public static void GetMaterialShape(
            BossActionEffectVisualKind kind,
            out float softness,
            out float wispShape)
        {
            switch (kind)
            {
                case BossActionEffectVisualKind.HorizontalSweep:
                    softness = 0.22f;
                    wispShape = 1f;
                    break;
                case BossActionEffectVisualKind.ArmorCompress:
                    softness = 0.24f;
                    wispShape = 0.28f;
                    break;
                default:
                    softness = 0.16f;
                    wispShape = 0.68f;
                    break;
            }
        }

        /// <summary>把粒子分配到盾牌外沿、包边、卷纹、竖脊与中心菱形。</summary>
        public static Vector2 GetShieldParticleTarget(int index, int count)
        {
            int safeCount = Mathf.Max(1, count);
            float u = (index + 0.5f) / safeCount;
            switch ((index % 12 + 12) % 12)
            {
                case 6:
                    return BossActionEffectSampler.GetShieldInsetUnitPoint(
                        u, 0.075f);
                case 7:
                    return BossActionEffectSampler.GetShieldScrollUnitPoint(
                        true, u);
                case 8:
                    return BossActionEffectSampler.GetShieldScrollUnitPoint(
                        false, u);
                case 9:
                    return new Vector2(0f, Mathf.Lerp(0.86f, -0.84f, u));
                case 10:
                {
                    float scaled = Mathf.Repeat(u, 1f) * 4f;
                    int corner = Mathf.FloorToInt(scaled);
                    return Vector2.Lerp(
                        BossActionEffectSampler.GetShieldGemUnitPoint(corner),
                        BossActionEffectSampler.GetShieldGemUnitPoint(
                            corner + 1),
                        scaled - corner);
                }
                default:
                    return BossActionEffectSampler.GetShieldUnitPoint(u);
            }
        }

        private static Vector3 ViewportDeltaToLocal(
            Vector2 viewportDelta,
            float viewWidth,
            float viewHeight)
        {
            return new Vector3(
                viewportDelta.x * viewWidth,
                viewportDelta.y * viewHeight,
                0f);
        }

        private static Vector2 UnitDirection(float value)
        {
            float angle = value * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private static float Hash01(int index, float salt)
        {
            return Mathf.Repeat(
                Mathf.Sin((index + 1) * (12.9898f + salt * 37.719f)) *
                43758.5453f,
                1f);
        }
    }
}
