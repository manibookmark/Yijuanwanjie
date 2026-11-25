using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>Initial authoritative shot: one burning feather, never a generic orb or card sprite.</summary>
    public sealed class ProceduralFireCrowFeatherVisual : IProjectileVisual
    {
        public const string VisualTypeName = "ProceduralFireCrowFeather";
        private GameObject _root;
        private Mesh _mesh;

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            Cleanup();
            var sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null) { sprite.sprite = null; sprite.enabled = false; }
            _root = new GameObject("BurningCrowFeather", typeof(MeshFilter), typeof(MeshRenderer));
            _root.transform.SetParent(host.transform, false);
            host.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);
            Color flame = profile != null ? profile.fireCrowFlameColor : new Color(1f, 0.24f, 0.035f);
            Color core = new Color(1f, 0.93f, 0.40f);
            _mesh = new Mesh { name = "ProceduralFireCrowFeather" };
            _mesh.vertices = new[] {
                new Vector3(-0.95f, 0f, 0f), new Vector3(-0.40f, 0.23f, 0f), new Vector3(0.20f, 0.18f, 0f),
                new Vector3(0.88f, 0f, 0f), new Vector3(0.20f, -0.18f, 0f), new Vector3(-0.40f, -0.23f, 0f),
                new Vector3(-0.70f, 0.025f, 0f), new Vector3(0.70f, 0.025f, 0f),
                new Vector3(0.70f, -0.025f, 0f), new Vector3(-0.70f, -0.025f, 0f)
            };
            _mesh.triangles = new[] {0,1,2, 0,2,3, 0,3,4, 0,4,5, 6,7,8, 6,8,9};
            _mesh.colors = new[] {flame,flame,flame,core,flame,flame,core,core,core,core};
            var uv = new Vector2[10];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(0.5f, 0.5f);
            _mesh.uv = uv;
            _mesh.RecalculateBounds();
            _root.GetComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer renderer = _root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralParticleMaterial.GetAlphaBlendedBox();
            renderer.sortingOrder = 66;
        }

        public void OnFlightStart() { }
        public void OnFlightEnd() { }
        public Vector3 GetTrailAnchorOffset() => new Vector3(-0.8f, 0f, 0f);
        public void Cleanup()
        {
            if (_root != null) Object.Destroy(_root);
            if (_mesh != null) Object.Destroy(_mesh);
            _root = null;
            _mesh = null;
        }
    }
}
