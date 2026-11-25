using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Owns a dense, simplified particle silhouette sampled from local geometry.
    /// The cast player owns motion, timing and gain handoff; this body only owns
    /// immutable samples, its renderer and formation opacity. No Infinity state.
    /// </summary>
    internal sealed class ArtifactParticleBody : IDisposable
    {
        private readonly struct Sample
        {
            public readonly Vector3 Position;
            public readonly Color Color;
            public readonly float Size;

            public Sample(Vector3 position, Color color, float size)
            {
                Position = position;
                Color = color;
                Size = size;
            }
        }

        internal sealed class Builder
        {
            private const int MaximumSamples = 8192;
            private readonly List<Sample> _samples = new List<Sample>();
            private readonly float _spacing;

            public Builder(float spacing = 0.02f)
            {
                if (!Finite(spacing) || spacing < 0.01f || spacing > 0.1f)
                    throw new ArgumentOutOfRangeException(nameof(spacing));
                _spacing = spacing;
            }

            public void AddPolygon(IReadOnlyList<Vector3> vertices, Color color)
            {
                ValidateVertices(vertices, 3);
                Vector2 min = vertices[0];
                Vector2 max = min;
                for (int i = 1; i < vertices.Count; i++)
                {
                    min = Vector2.Min(min, vertices[i]);
                    max = Vector2.Max(max, vertices[i]);
                }
                int columns = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / _spacing));
                int rows = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / _spacing));
                for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    Vector2 point = new Vector2(min.x + (column + 0.5f) * _spacing,
                        min.y + (row + 0.5f) * _spacing);
                    bool inside = false;
                    for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
                    {
                        Vector3 a = vertices[i];
                        Vector3 b = vertices[j];
                        if ((a.y > point.y) != (b.y > point.y) &&
                            point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                            inside = !inside;
                    }
                    if (inside) AddSample(new Vector3(point.x, point.y, vertices[0].z), color);
                }
            }

            public void AddDisc(Vector3 center, float radius, Color color)
            {
                if (!Finite(radius) || radius <= 0f || radius > 2f)
                    throw new ArgumentOutOfRangeException(nameof(radius));
                var edge = new Vector3[64];
                for (int i = 0; i < edge.Length; i++)
                {
                    float angle = i * Mathf.PI * 2f / edge.Length;
                    edge[i] = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                }
                AddPolygon(edge, color);
            }

            public void AddStroke(IReadOnlyList<Vector3> points, Color color,
                float width, bool closed = false)
            {
                ValidateVertices(points, 2);
                if (!Finite(width) || width <= 0f || width > 1f)
                    throw new ArgumentOutOfRangeException(nameof(width));
                int crossCount = Mathf.Max(1, Mathf.CeilToInt(width / _spacing));
                int segmentCount = closed ? points.Count : points.Count - 1;
                for (int i = 0; i < segmentCount; i++)
                {
                    Vector3 a = points[i];
                    Vector3 delta = points[(i + 1) % points.Count] - a;
                    float distance = delta.magnitude;
                    if (distance < 0.00001f) continue;
                    Vector3 normal = new Vector3(-delta.y, delta.x, 0f).normalized;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(distance / _spacing));
                    int sampleCount = !closed && i == segmentCount - 1 ? steps + 1 : steps;
                    for (int s = 0; s < sampleCount; s++)
                    for (int cross = 0; cross < crossCount; cross++)
                    {
                        float offset = ((cross + 0.5f) / crossCount - 0.5f) * width;
                        AddSample(a + delta * (s / (float)steps) + normal * offset, color);
                    }
                }
            }

            public ArtifactParticleBody Create(Transform parent, int sortingOrder = 85)
            {
                if (parent == null || !parent.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Particle body needs an active cast parent.");
                if (_samples.Count == 0)
                    throw new InvalidOperationException("Particle body has no silhouette samples.");
                return new ArtifactParticleBody(parent, _samples.ToArray(), sortingOrder);
            }

            private void AddSample(Vector3 position, Color color)
            {
                if (_samples.Count >= MaximumSamples)
                    throw new InvalidOperationException("Artifact silhouette exceeds its sample budget.");
                if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) ||
                    !Finite(color.r) || !Finite(color.g) || !Finite(color.b) || !Finite(color.a))
                    throw new ArgumentException("Silhouette samples must be finite.");
                color.r = Mathf.Round(Mathf.Clamp01(color.r) * 7f) / 7f;
                color.g = Mathf.Round(Mathf.Clamp01(color.g) * 7f) / 7f;
                color.b = Mathf.Round(Mathf.Clamp01(color.b) * 7f) / 7f;
                color.a = Mathf.Clamp01(color.a);
                _samples.Add(new Sample(position, color, _spacing * 1.22f));
            }

            private static void ValidateVertices(IReadOnlyList<Vector3> vertices, int minimum)
            {
                if (vertices == null || vertices.Count < minimum)
                    throw new ArgumentException("Silhouette needs complete geometry.");
                for (int i = 0; i < vertices.Count; i++)
                    if (!Finite(vertices[i].x) || !Finite(vertices[i].y) || !Finite(vertices[i].z) ||
                        Mathf.Abs(vertices[i].x) > 4f || Mathf.Abs(vertices[i].y) > 4f)
                        throw new ArgumentException("Silhouette vertices exceed local geometry bounds.");
            }
        }

        private readonly Sample[] _samples;
        private readonly ParticleSystem.Particle[] _particles;
        private readonly int _createdFrame;
        private GameObject _host;
        private ParticleSystem _system;
        private bool _playing;

        private ArtifactParticleBody(Transform parent, Sample[] samples, int sortingOrder)
        {
            _samples = samples;
            _particles = new ParticleSystem.Particle[samples.Length];
            _createdFrame = Time.frameCount;
            _host = new GameObject("ArtifactParticleSilhouette");
            _host.transform.SetParent(parent, false);
            _system = _host.AddComponent<ParticleSystem>();
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = samples.Length;
            var emission = _system.emission;
            emission.enabled = false;
            var shape = _system.shape;
            shape.enabled = false;
            var renderer = _system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlended();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;
        }

        public void SetFrame(float opacity, float formation = 1f)
        {
            if (_system == null || _host == null || !_host.activeInHierarchy ||
                Time.frameCount <= _createdFrame) return;
            if (!_playing)
            {
                _system.Play();
                _playing = true;
            }
            float alpha = Mathf.Clamp01(opacity);
            float scatterSize = (1f - Mathf.Clamp01(formation)) * 0.025f;
            for (int i = 0; i < _samples.Length; i++)
            {
                Sample point = _samples[i];
                float phase = i * 2.399963f;
                Vector3 scatter = new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) * scatterSize;
                Color color = point.Color;
                color.a *= alpha;
                _particles[i] = new ParticleSystem.Particle
                {
                    position = point.Position + scatter,
                    startColor = color,
                    startSize = point.Size,
                    startLifetime = 2f,
                    remainingLifetime = 2f
                };
            }
            _system.SetParticles(_particles, _particles.Length);
        }

        public void Dispose()
        {
            // The cast owner cancels before destroying its root. No particle
            // module is accessed during disable or after destruction.
            _system = null;
            if (_host != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_host);
                else UnityEngine.Object.DestroyImmediate(_host);
            }
            _host = null;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
