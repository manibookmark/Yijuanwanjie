using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// A compact physical black-iron fire rocket, facing local X+. Owns only
    /// its mesh/root; the shared material and flight/hit timing belong to their owners.
    /// </summary>
    public sealed class ProceduralFireCrowRocketVisual : IProjectileVisual
    {
        public const string VisualTypeName = "ProceduralFireCrowRocket";
        private GameObject _root;
        private Mesh _mesh;

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            Cleanup();
            var sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null) { sprite.sprite = null; sprite.enabled = false; }
            _root = new GameObject("FireCrowRocket", typeof(MeshFilter), typeof(MeshRenderer));
            _root.transform.SetParent(host.transform, false);
            host.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            void Polygon(Color color, params Vector2[] outline)
            {
                int first = vertices.Count;
                Vector2 center = Vector2.zero;
                foreach (Vector2 point in outline) center += point;
                vertices.Add(center / outline.Length);
                colors.Add(color);
                uv.Add(new Vector2(0.5f, 0.5f));
                foreach (Vector2 point in outline)
                {
                    vertices.Add(point);
                    colors.Add(color);
                    uv.Add(new Vector2(0.5f, 0.5f));
                }
                for (int i = 0; i < outline.Length; i++)
                {
                    triangles.Add(first);
                    triangles.Add(first + i + 1);
                    triangles.Add(first + (i + 1) % outline.Length + 1);
                }
            }

            Color fire = profile != null ? profile.fireCrowFlameColor : new Color(1f, 0.24f, 0.035f);
            Color core = new Color(1f, 0.88f, 0.26f);
            Color iron = new Color(0.065f, 0.075f, 0.085f);
            Color copper = new Color(0.85f, 0.57f, 0.20f);
            // Short exhaust, separate from the solid casing and its pointed nose.
            Polygon(fire, new Vector2(-0.72f, 0f), new Vector2(-0.39f, 0.12f),
                new Vector2(-0.29f, 0f), new Vector2(-0.39f, -0.12f));
            Polygon(core, new Vector2(-0.60f, 0f), new Vector2(-0.35f, 0.055f),
                new Vector2(-0.28f, 0f), new Vector2(-0.35f, -0.055f));
            Polygon(copper, new Vector2(-0.33f, 0.065f), new Vector2(-0.42f, 0.23f),
                new Vector2(-0.11f, 0.09f));
            Polygon(copper, new Vector2(-0.33f, -0.065f), new Vector2(-0.42f, -0.23f),
                new Vector2(-0.11f, -0.09f));
            Polygon(iron, new Vector2(-0.35f, -0.095f), new Vector2(0.27f, -0.095f),
                new Vector2(0.27f, 0.095f), new Vector2(-0.35f, 0.095f));
            Polygon(new Color(0.24f, 0.27f, 0.29f),
                new Vector2(-0.28f, 0.035f), new Vector2(0.24f, 0.035f),
                new Vector2(0.24f, 0.06f), new Vector2(-0.28f, 0.06f));
            Polygon(copper, new Vector2(-0.23f, -0.105f), new Vector2(-0.16f, -0.105f),
                new Vector2(-0.16f, 0.105f), new Vector2(-0.23f, 0.105f));
            Polygon(copper, new Vector2(0.15f, -0.105f), new Vector2(0.22f, -0.105f),
                new Vector2(0.22f, 0.105f), new Vector2(0.15f, 0.105f));
            Polygon(fire, new Vector2(0.27f, -0.095f), new Vector2(0.43f, 0f),
                new Vector2(0.27f, 0.095f));

            _mesh = new Mesh { name = VisualTypeName };
            _mesh.SetVertices(vertices);
            _mesh.SetColors(colors);
            _mesh.SetUVs(0, uv);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateBounds();
            _root.GetComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
            renderer.sortingOrder = 66;
        }

        public void OnFlightStart() { }
        public void OnFlightEnd() { }
        public Vector3 GetTrailAnchorOffset() => new Vector3(-0.52f, 0f, 0f);

        public void Cleanup()
        {
            if (_root != null) Object.Destroy(_root);
            if (_mesh != null) Object.Destroy(_mesh);
            _root = null;
            _mesh = null;
        }
    }
}
