using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>A short plated bee dart facing X+. Owns geometry only; flight and impacts use receipts.</summary>
    public sealed class ProceduralBeeStingVisual : IProjectileVisual
    {
        public const string VisualTypeName = "ProceduralBeeSting";
        private GameObject _root;
        private Mesh _mesh;

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            Cleanup();
            var sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null) { sprite.sprite = null; sprite.enabled = false; }
            _root = new GameObject("BeeSting", typeof(MeshFilter), typeof(MeshRenderer));
            _root.transform.SetParent(host.transform, false);
            host.transform.localScale = Vector3.one * Mathf.Max(.05f, scale);
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            void Polygon(Color color, params Vector2[] outline)
            {
                int first = vertices.Count;
                Vector2 center = Vector2.zero;
                foreach (Vector2 point in outline) center += point;
                vertices.Add(center / outline.Length); colors.Add(color); uv.Add(new Vector2(.5f, .5f));
                foreach (Vector2 point in outline) { vertices.Add(point); colors.Add(color); uv.Add(new Vector2(.5f, .5f)); }
                for (int i = 0; i < outline.Length; i++)
                { triangles.Add(first); triangles.Add(first + i + 1); triangles.Add(first + (i + 1) % outline.Length + 1); }
            }
            Color brass = profile != null ? profile.beeBodyColor : new Color(.84f, .55f, .18f);
            Color ivory = new Color(1f, .90f, .56f, .9f);
            Color dark = new Color(.16f, .09f, .025f, 1f);
            Polygon(ivory, new Vector2(-.56f, 0f), new Vector2(-.30f, .07f),
                new Vector2(-.20f, 0f), new Vector2(-.30f, -.07f));
            Polygon(brass, new Vector2(-.28f, -.07f), new Vector2(.12f, -.07f),
                new Vector2(.40f, 0f), new Vector2(.12f, .07f), new Vector2(-.28f, .07f));
            Polygon(ivory, new Vector2(-.19f, .025f), new Vector2(.14f, .025f),
                new Vector2(.28f, 0f), new Vector2(.08f, .055f), new Vector2(-.19f, .05f));
            Polygon(dark, new Vector2(-.22f, -.08f), new Vector2(-.13f, -.08f),
                new Vector2(-.13f, .08f), new Vector2(-.22f, .08f));
            Polygon(brass, new Vector2(-.23f, .035f), new Vector2(-.39f, .20f), new Vector2(-.07f, .055f));
            Polygon(brass, new Vector2(-.23f, -.035f), new Vector2(-.39f, -.20f), new Vector2(-.07f, -.055f));
            _mesh = new Mesh { name = VisualTypeName };
            _mesh.SetVertices(vertices); _mesh.SetColors(colors); _mesh.SetUVs(0, uv);
            _mesh.SetTriangles(triangles, 0); _mesh.RecalculateBounds();
            _root.GetComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
            renderer.sortingOrder = 68;
        }
        public void OnFlightStart() { }
        public void OnFlightEnd() { }
        public Vector3 GetTrailAnchorOffset() => new Vector3(-.35f, 0f, 0f);
        public void Cleanup()
        {
            if (_root != null) Object.Destroy(_root);
            if (_mesh != null) Object.Destroy(_mesh);
            _root = null; _mesh = null;
        }
    }
}
