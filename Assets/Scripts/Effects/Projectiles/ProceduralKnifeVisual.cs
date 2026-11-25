using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>Silver short knife, wrapped grip and red ribbon; owns only the
    /// projectile geometry. Flight timing and actual targets remain external.</summary>
    public sealed class ProceduralKnifeVisual : IProjectileVisual
    {
        public const string VisualTypeName = "ProceduralKnife";
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private static Material _material;
        private GameObject _root;

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            SpriteRenderer sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null) { sprite.sprite = null; sprite.enabled = false; }
            host.transform.localScale = Vector3.one * scale;
            _root = new GameObject("KnifeVisualGeometry");
            _root.transform.SetParent(host.transform, false);
            MeshPart(_root.transform, "SilverKnife", profile.proceduralBladeColor, 24,
                new Vector3(-0.13f, -0.11f, 0f), new Vector3(-0.13f, 0.11f, 0f),
                new Vector3(0.49f, 0.065f, 0f), new Vector3(0.7f, 0f, 0f),
                new Vector3(0.49f, -0.065f, 0f));
            MeshPart(_root.transform, "ColdKnifeEdge", profile.proceduralBladeEdgeColor, 25,
                new Vector3(-0.11f, -0.1f, 0f), new Vector3(0.7f, 0f, 0f),
                new Vector3(0.45f, -0.028f, 0f), new Vector3(-0.11f, -0.055f, 0f));
            MeshPart(_root.transform, "WrappedGrip", new Color(0.29f, 0.20f, 0.12f), 24,
                new Vector3(-0.43f, -0.06f, 0f), new Vector3(-0.43f, 0.06f, 0f),
                new Vector3(-0.13f, 0.06f, 0f), new Vector3(-0.13f, -0.06f, 0f));
            for (int i = 0; i < 5; i++)
            {
                float x = -0.40f + i * 0.05f;
                MeshPart(_root.transform, "GripWrap", new Color(0.65f, 0.56f, 0.40f), 25,
                    new Vector3(x, -0.062f, 0f), new Vector3(x + 0.025f, -0.062f, 0f),
                    new Vector3(x + 0.045f, 0.062f, 0f), new Vector3(x + 0.02f, 0.062f, 0f));
            }
            MeshPart(_root.transform, "RedRibbon", new Color(0.69f, 0.08f, 0.045f), 25,
                new Vector3(-0.38f, 0.01f, 0f), new Vector3(-0.68f, 0.14f, 0f),
                new Vector3(-0.81f, 0.075f, 0f), new Vector3(-0.62f, 0.065f, 0f),
                new Vector3(-0.38f, -0.035f, 0f));
        }

        private void MeshPart(Transform parent, string name, Color color, int order, params Vector3[] points)
        {
            if (_material == null)
                _material = new Material(Shader.Find("Sprites/Default"))
                    { hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = points;
            var triangles = new int[(points.Length - 2) * 3];
            for (int i = 0; i < points.Length - 2; i++)
            { triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = i + 2; }
            mesh.triangles = triangles;
            var colors = new Color[points.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            var child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            child.transform.SetParent(parent, false);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = order;
        }

        public void OnFlightStart() { }
        public void OnFlightEnd() { }
        public Vector3 GetTrailAnchorOffset() => new Vector3(-0.47f, 0f, 0f);
        public void Cleanup()
        {
            if (_root != null) { _root.SetActive(false); Object.Destroy(_root); _root = null; }
            foreach (Mesh mesh in _meshes) Object.Destroy(mesh);
            _meshes.Clear();
        }
    }
}
