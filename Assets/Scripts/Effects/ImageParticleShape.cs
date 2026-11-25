using System;
using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Immutable baked silhouette and simplified colour samples. A reference
    /// image is consumed only by the offline baker; this runtime representation
    /// has no texture, sprite, renderer or animation state.
    /// </summary>
    internal sealed class ImageParticleShape
    {
        internal readonly struct Point
        {
            public readonly Vector3 Position;
            public readonly Color Color;
            public readonly float Size;

            public Point(Vector2 position, Color color, float size)
            {
                Position = new Vector3(position.x, position.y, 0f);
                Color = color;
                Size = size;
            }
        }

        [Serializable]
        private sealed class Document
        {
            public int version;
            public Vector2 bodySize;
            public Vector3 reach;
            public Sample[] samples;
        }

        [Serializable]
        private struct Sample
        {
            public Vector2 position;
            public Color color;
            public float size;
        }

        private readonly Point[] _points;
        public Vector2 BodySize { get; }
        public Vector3 Reach { get; }
        public int Count => _points.Length;
        public Point GetPoint(int index) => _points[index];

        private ImageParticleShape(Document document, Point[] points)
        {
            BodySize = document.bodySize;
            Reach = document.reach;
            _points = points;
        }

        public static bool TryRead(TextAsset source, out ImageParticleShape shape)
        {
            shape = null;
            if (source == null) return false;
            try
            {
                Document document = JsonUtility.FromJson<Document>(source.text);
                if (document == null || document.version != 1 ||
                    document.samples == null || document.samples.Length == 0 ||
                    document.samples.Length > 4096 ||
                    !Positive(document.bodySize.x) || !Positive(document.bodySize.y) ||
                    !Positive(document.reach.x) || !Positive(document.reach.y) ||
                    !Positive(document.reach.z))
                    throw new ArgumentException("Invalid baked shape header.");

                var points = new Point[document.samples.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    Sample sample = document.samples[i];
                    if (!Finite(sample.position.x) || !Finite(sample.position.y) ||
                        !Positive(sample.size) || !Channel(sample.color.r) ||
                        !Channel(sample.color.g) || !Channel(sample.color.b) ||
                        !Channel(sample.color.a) ||
                        Mathf.Abs(sample.position.x) + sample.size * 0.5f > document.reach.x + 0.001f ||
                        -sample.position.y + sample.size * 0.5f > document.reach.y + 0.001f ||
                        sample.position.y + sample.size * 0.5f > document.reach.z + 0.001f)
                        throw new ArgumentException("Invalid baked shape point " + i + ".");
                    points[i] = new Point(sample.position, sample.color, sample.size);
                }
                shape = new ImageParticleShape(document, points);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[ImageParticleShape] " + source.name + ": " + exception.Message);
                return false;
            }
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Positive(float value) => Finite(value) && value > 0f;
        private static bool Channel(float value) => Finite(value) && value >= 0f && value <= 1f;
    }
}
