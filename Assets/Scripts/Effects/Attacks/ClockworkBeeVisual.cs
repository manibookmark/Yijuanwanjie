using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Effects.Projectiles;

namespace WanjieScroll.Effects.Attacks
{
    /// <summary>One articulated brass bee. Owns its meshes, pose and wing motion;
    /// the resident presenter owns group identity and flight, Core owns attacks.</summary>
    internal sealed class ClockworkBeeVisual : IPeriodicAttackerVisual
    {
        private sealed class Geometry
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();

            internal void Polygon(Color color, params Vector2[] outline)
            {
                int first = _vertices.Count;
                Vector2 center = Vector2.zero;
                foreach (Vector2 point in outline) center += point;
                Add(center / outline.Length, color);
                foreach (Vector2 point in outline) Add(point, color);
                for (int i = 0; i < outline.Length; i++)
                {
                    _triangles.Add(first); _triangles.Add(first + i + 1);
                    _triangles.Add(first + (i + 1) % outline.Length + 1);
                }
            }
            private void Add(Vector2 point, Color color)
            { _vertices.Add(point); _colors.Add(color); _uv.Add(new Vector2(.5f, .5f)); }
            internal Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices); mesh.SetColors(_colors); mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_triangles, 0); mesh.RecalculateBounds();
                return mesh;
            }
        }

        private readonly GameObject _root;
        private readonly Transform _frontWing, _backWing;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly float _scale;
        private bool _disposed;
        private float _pulse;

        public Vector3 ProjectileOrigin => !_disposed && _root != null
            ? _root.transform.TransformPoint(new Vector3(-.48f, -.04f, 0f)) : Vector3.zero;

        internal ClockworkBeeVisual(Transform parent, VfxProfile profile)
        {
            _root = new GameObject("ClockworkBee_Hover");
            _root.transform.SetParent(parent, false);
            _scale = Mathf.Max(.2f, profile.fireCrowScale);
            Color brass = profile.beeBodyColor;
            Color dark = new Color(.15f, .09f, .035f, 1f);
            Color edge = new Color(1f, .83f, .38f, 1f);
            _backWing = Wing("BackWing", profile.beeWingColor, -.11f, 64);
            _frontWing = Wing("FrontWing", profile.beeWingColor, .13f, 66);
            var body = new Geometry();
            body.Polygon(dark, new Vector2(-.48f, -.03f), new Vector2(-.72f, -.02f),
                new Vector2(-.46f, .055f));
            body.Polygon(brass, new Vector2(-.48f, -.025f), new Vector2(-.37f, -.20f),
                new Vector2(-.08f, -.25f), new Vector2(.20f, -.16f), new Vector2(.26f, .07f),
                new Vector2(.08f, .22f), new Vector2(-.21f, .24f), new Vector2(-.42f, .14f));
            // Dark joints separate the plated abdomen from a luminous chest.
            body.Polygon(dark, new Vector2(-.31f, -.22f), new Vector2(-.23f, -.24f),
                new Vector2(-.18f, .245f), new Vector2(-.27f, .23f));
            body.Polygon(dark, new Vector2(-.07f, -.25f), new Vector2(.005f, -.235f),
                new Vector2(.055f, .225f), new Vector2(-.025f, .24f));
            body.Polygon(edge, new Vector2(-.39f, .13f), new Vector2(-.10f, .20f),
                new Vector2(.11f, .15f), new Vector2(.08f, .11f), new Vector2(-.36f, .08f));
            body.Polygon(dark, new Vector2(.15f, -.14f), new Vector2(.37f, -.15f),
                new Vector2(.50f, -.025f), new Vector2(.42f, .17f), new Vector2(.22f, .18f));
            body.Polygon(edge, new Vector2(.28f, .015f), new Vector2(.41f, .025f),
                new Vector2(.405f, .12f), new Vector2(.28f, .115f));
            for (int i = 0; i < 2; i++)
            {
                float x = .26f + i * .12f;
                body.Polygon(dark, new Vector2(x, .14f), new Vector2(x + .045f, .16f),
                    new Vector2(x + .11f, .40f), new Vector2(x + .065f, .41f));
            }
            for (int i = 0; i < 3; i++)
            {
                float x = -.22f + i * .16f;
                body.Polygon(dark, new Vector2(x, -.15f), new Vector2(x + .04f, -.16f),
                    new Vector2(x + .10f, -.37f), new Vector2(x - .035f, -.40f),
                    new Vector2(x - .05f, -.36f), new Vector2(x + .045f, -.33f));
            }
            Part("Body", body, 67);
        }

        private Transform Wing(string name, Color color, float offset, int order)
        {
            var wing = new Geometry();
            wing.Polygon(color, new Vector2(-.04f, .08f), new Vector2(-.55f + offset, .42f),
                new Vector2(-.66f + offset, .66f), new Vector2(-.40f + offset, .75f),
                new Vector2(.08f + offset, .39f), new Vector2(.08f, .14f));
            Color vein = new Color(.70f, .48f, .19f, .8f);
            wing.Polygon(vein, new Vector2(-.03f, .13f), new Vector2(-.52f + offset, .59f),
                new Vector2(-.49f + offset, .62f), new Vector2(.01f, .15f));
            return Part(name, wing, order);
        }

        private Transform Part(string name, Geometry geometry, int order)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(_root.transform, false);
            Mesh mesh = geometry.Build("ClockworkBee_" + name);
            _meshes.Add(mesh); part.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
            renderer.sortingOrder = order;
            return part.transform;
        }
        public void SetPose(Vector3 position, bool rightward)
        {
            if (_disposed || _root == null) return;
            _root.transform.position = position;
            float size = _scale * (1f + _pulse * .08f);
            _root.transform.localScale = new Vector3(rightward ? size : -size, size, size);
        }
        public void Pulse() { if (!_disposed) _pulse = 1f; }
        public void Tick(float delta, float time)
        {
            if (_disposed) return;
            _pulse = Mathf.MoveTowards(_pulse, 0f, delta * 5f);
            float flap = Mathf.Sin(time * 25f) * 20f;
            _frontWing.localRotation = Quaternion.Euler(0f, 0f, flap);
            _backWing.localRotation = Quaternion.Euler(0f, 0f, -flap - 12f);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_root != null) Object.Destroy(_root);
            foreach (Mesh mesh in _meshes) Object.Destroy(mesh);
            _meshes.Clear();
        }
    }
}
