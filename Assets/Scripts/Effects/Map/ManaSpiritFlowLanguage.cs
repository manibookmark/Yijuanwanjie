using System;
using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    public enum ManaSpiritFlowLayerKind
    {
        Core,
        Returning,
        Ascending
    }

    public enum ManaSpiritArcMotion
    {
        Random,
        Loop,
        PingPong
    }

    /// <summary>One layer of the ordered pearl-centred mana flow.</summary>
    public readonly struct ManaSpiritFlowLayerStyle
    {
        internal ManaSpiritFlowLayerStyle(
            ManaSpiritFlowLayerKind kind,
            float emissionRate,
            Vector2 lifetime,
            Vector2 size,
            float radius,
            float radiusThickness,
            float arc,
            float shapeRotationZ,
            ManaSpiritArcMotion arcMotion,
            float arcSpeed,
            float arcSpread,
            Vector2 linearX,
            Vector2 linearY,
            float radialVelocity,
            float orbitalVelocity,
            int capacity)
        {
            Kind = kind;
            EmissionRate = emissionRate;
            Lifetime = lifetime;
            Size = size;
            Radius = radius;
            RadiusThickness = radiusThickness;
            Arc = arc;
            ShapeRotationZ = shapeRotationZ;
            ArcMotion = arcMotion;
            ArcSpeed = arcSpeed;
            ArcSpread = arcSpread;
            LinearX = linearX;
            LinearY = linearY;
            RadialVelocity = radialVelocity;
            OrbitalVelocity = orbitalVelocity;
            Capacity = capacity;
        }

        public ManaSpiritFlowLayerKind Kind { get; }
        public float EmissionRate { get; }
        public Vector2 Lifetime { get; }
        public Vector2 Size { get; }
        public float Radius { get; }
        public float RadiusThickness { get; }
        public float Arc { get; }
        public float ShapeRotationZ { get; }
        public ManaSpiritArcMotion ArcMotion { get; }
        public float ArcSpeed { get; }
        public float ArcSpread { get; }
        public Vector2 LinearX { get; }
        public Vector2 LinearY { get; }
        public float RadialVelocity { get; }
        public float OrbitalVelocity { get; }
        public int Capacity { get; }
    }

    /// <summary>
    /// Authoritative three-layer grammar for mana: a sprite owns the pearl,
    /// one thin current returns around it, and a few wisps rise from its crown.
    /// Random full-circle spray and ink-drop particles are not part of mana.
    /// </summary>
    public static class ManaSpiritFlowLanguage
    {
        public const int CurrentVersion = 3;

        public static bool TryResolve(
            string systemName,
            out ManaSpiritFlowLayerStyle style)
        {
            if (systemName.IndexOf(
                    "Core",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                style = Resolve(ManaSpiritFlowLayerKind.Core);
                return true;
            }
            if (systemName.IndexOf(
                    "Returning",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                style = Resolve(ManaSpiritFlowLayerKind.Returning);
                return true;
            }
            if (systemName.IndexOf(
                    "Ascending",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                style = Resolve(ManaSpiritFlowLayerKind.Ascending);
                return true;
            }

            style = default;
            return false;
        }

        public static ManaSpiritFlowLayerStyle Resolve(
            ManaSpiritFlowLayerKind kind)
        {
            return kind switch
            {
                ManaSpiritFlowLayerKind.Returning =>
                    new ManaSpiritFlowLayerStyle(
                        kind,
                        3.6f,
                        new Vector2(1.00f, 1.18f),
                        new Vector2(0.045f, 0.065f),
                        0.44f,
                        0.055f,
                        270f,
                        45f,
                        ManaSpiritArcMotion.Loop,
                        0.22f,
                        0.04f,
                        new Vector2(-0.006f, 0.006f),
                        new Vector2(0.015f, 0.035f),
                        -0.055f,
                        0.26f,
                        24),
                ManaSpiritFlowLayerKind.Ascending =>
                    new ManaSpiritFlowLayerStyle(
                        kind,
                        2.2f,
                        new Vector2(0.92f, 1.12f),
                        new Vector2(0.035f, 0.052f),
                        0.19f,
                        0.38f,
                        110f,
                        205f,
                        ManaSpiritArcMotion.PingPong,
                        0.18f,
                        0.08f,
                        new Vector2(-0.012f, 0.012f),
                        new Vector2(0.16f, 0.22f),
                        0f,
                        0.02f,
                        18),
                _ => new ManaSpiritFlowLayerStyle(
                    ManaSpiritFlowLayerKind.Core,
                    0f,
                    new Vector2(0.80f, 0.80f),
                    new Vector2(0.035f, 0.035f),
                    0.04f,
                    1f,
                    360f,
                    0f,
                    ManaSpiritArcMotion.Random,
                    0f,
                    0f,
                    Vector2.zero,
                    Vector2.zero,
                    0f,
                    0f,
                    8)
            };
        }
    }
}
