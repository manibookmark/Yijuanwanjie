using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects.Attacks
{
    /// <summary>
    /// One visible fire crow per authoritative attack instance. Owns three
    /// articulated mesh groups and their pose; has no counts, combat state or
    /// strike presentation. Rockets are rendered by the projectile owner.
    /// </summary>
    internal sealed class FireCrowVisual : IPeriodicAttackerVisual
    {
        private sealed class Geometry
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();

            public void Polygon(Color color, params Vector2[] outline)
            {
                int first = _vertices.Count;
                Vector2 center = Vector2.zero;
                foreach (Vector2 point in outline) center += point;
                AddVertex(center / outline.Length, color);
                foreach (Vector2 point in outline) AddVertex(point, color);
                for (int i = 0; i < outline.Length; i++)
                {
                    _triangles.Add(first);
                    _triangles.Add(first + i + 1);
                    _triangles.Add(first + (i + 1) % outline.Length + 1);
                }
            }

            private void AddVertex(Vector2 position, Color color)
            {
                _vertices.Add(position);
                _colors.Add(color);
                _uv.Add(new Vector2(0.5f, 0.5f));
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetColors(_colors);
                mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private readonly GameObject _root;
        private readonly Transform _leftWing;
        private readonly Transform _rightWing;
        private readonly List<Mesh> _meshes = new List<Mesh>(3);
        private readonly Color _fire;
        private readonly float _scale;
        private float _pulse;
        private bool _disposed;

        public Vector3 ProjectileOrigin => !_disposed && _root != null
            ? _root.transform.TransformPoint(new Vector3(0f, -0.18f, 0f))
            : Vector3.zero;

        public FireCrowVisual(Transform parent, VfxProfile profile)
        {
            _fire = profile.fireCrowFlameColor;
            _scale = Mathf.Max(0.2f, profile.fireCrowScale);
            Color body = profile.fireCrowBodyColor;
            _root = new GameObject("FireCrow_Hover");
            _root.transform.SetParent(parent, false);
            _root.transform.localScale = Vector3.one * _scale;
            _leftWing = Wing("LeftWing", -1f, body);
            _rightWing = Wing("RightWing", 1f, body);

            var geometry = new Geometry();
            for (int i = 0; i < 3; i++)
            {
                float x = -0.14f + i * 0.095f;
                geometry.Polygon(body,
                    new Vector2(x, -0.12f), new Vector2(x + 0.10f, -0.16f),
                    new Vector2(x - 0.10f, -0.58f - i * 0.025f));
                geometry.Polygon(_fire,
                    new Vector2(x - 0.02f, -0.28f), new Vector2(x + 0.04f, -0.32f),
                    new Vector2(x - 0.10f, -0.66f - i * 0.025f));
            }
            geometry.Polygon(body,
                new Vector2(-0.15f, -0.23f), new Vector2(0.13f, -0.18f),
                new Vector2(0.17f, 0.15f), new Vector2(-0.08f, 0.20f));
            // Keep a readable side profile: the compact head sits above the
            // tail and the beak/eye are deliberately separated from the wings
            // so a turn never reads as a back-facing silhouette.
            geometry.Polygon(body,
                new Vector2(-0.05f, 0.10f), new Vector2(0.16f, 0.11f),
                new Vector2(0.25f, 0.25f), new Vector2(0.11f, 0.39f),
                new Vector2(-0.06f, 0.30f));
            geometry.Polygon(_fire,
                new Vector2(0.17f, 0.18f), new Vector2(0.48f, 0.21f),
                new Vector2(0.23f, 0.29f));
            // Dark eye socket plus a warm iris and tiny highlight make the
            // facing direction legible even when the crow overlaps another.
            geometry.Polygon(new Color(0.015f, 0.01f, 0.008f, 1f),
                new Vector2(0.08f, 0.265f), new Vector2(0.16f, 0.265f),
                new Vector2(0.16f, 0.335f), new Vector2(0.08f, 0.335f));
            geometry.Polygon(new Color(1f, 0.91f, 0.32f),
                new Vector2(0.105f, 0.278f), new Vector2(0.145f, 0.278f),
                new Vector2(0.145f, 0.322f), new Vector2(0.105f, 0.322f));
            geometry.Polygon(Color.white,
                new Vector2(0.112f, 0.305f), new Vector2(0.126f, 0.305f),
                new Vector2(0.126f, 0.319f), new Vector2(0.112f, 0.319f));
            MeshPart("Body", geometry, 65);
        }

        private Transform Wing(string name, float side, Color body)
        {
            var geometry = new Geometry();
            for (int i = 0; i < 5; i++)
            {
                float tip = 0.83f - i * 0.095f;
                float y = 0.44f - i * 0.145f;
                geometry.Polygon(body,
                    new Vector2(side * 0.04f, 0.07f), new Vector2(side * 0.35f, 0.20f),
                    new Vector2(side * tip, y), new Vector2(side * (tip - 0.11f), y - 0.09f));
                geometry.Polygon(_fire,
                    new Vector2(side * (tip - 0.28f), y - 0.035f),
                    new Vector2(side * (tip + 0.10f), y + 0.01f),
                    new Vector2(side * (tip - 0.09f), y - 0.09f));
            }

            // One compact black-iron rocket pod below each wing, with a copper collar.
            float x = side * 0.32f;
            Color iron = new Color(0.055f, 0.06f, 0.065f);
            Color copper = new Color(0.80f, 0.55f, 0.19f);
            geometry.Polygon(iron,
                new Vector2(x - 0.17f, -0.21f), new Vector2(x + 0.15f, -0.21f),
                new Vector2(x + 0.15f, -0.10f), new Vector2(x - 0.17f, -0.10f));
            geometry.Polygon(copper,
                new Vector2(x - 0.04f, -0.22f), new Vector2(x + 0.01f, -0.22f),
                new Vector2(x + 0.01f, -0.09f), new Vector2(x - 0.04f, -0.09f));
            geometry.Polygon(_fire,
                new Vector2(x + 0.15f, -0.21f), new Vector2(x + 0.23f, -0.155f),
                new Vector2(x + 0.15f, -0.10f));
            geometry.Polygon(copper,
                new Vector2(x - 0.17f, -0.155f), new Vector2(x - 0.23f, -0.24f),
                new Vector2(x - 0.10f, -0.21f));
            return MeshPart(name, geometry, 64);
        }

        private Transform MeshPart(string name, Geometry geometry, int sortingOrder)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(_root.transform, false);
            Mesh mesh = geometry.Build("FireCrow_" + name);
            _meshes.Add(mesh);
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
            renderer.sortingOrder = sortingOrder;
            return part.transform;
        }

        public void SetPose(Vector3 position, bool rightward)
        {
            if (_disposed || _root == null) return;
            _root.transform.position = position;
            float size = _scale * (1f + _pulse * 0.08f);
            _root.transform.localScale = new Vector3(rightward ? size : -size, size, size);
        }

        public void Pulse()
        {
            if (!_disposed) _pulse = 1f;
        }

        public void Tick(float delta, float time)
        {
            if (_disposed) return;
            _pulse = Mathf.MoveTowards(_pulse, 0f, delta * 4f);
            float flap = Mathf.Sin(time * 13f) * 18f;
            _leftWing.localRotation = Quaternion.Euler(0f, 0f, flap);
            _rightWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            foreach (Mesh mesh in _meshes) UnityEngine.Object.Destroy(mesh);
            _meshes.Clear();
        }
    }
}
