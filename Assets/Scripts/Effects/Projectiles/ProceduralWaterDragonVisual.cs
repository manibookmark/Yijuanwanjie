using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>Jade water-dragon outline. Its body and water whiskers remain
    /// attached to one recorded attack object; no resident copy is created.
    /// Owns local geometry only; the flight owner controls pose and opacity.</summary>
    public sealed class ProceduralWaterDragonVisual : IProjectileVisual
    {
        public const string VisualTypeName = "ProceduralWaterDragon";
        private static Material _material;
        private GameObject _root;
        private LineRenderer _body, _light;
        private readonly Vector3[] _bodyPoints = new Vector3[32];
        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            SpriteRenderer sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null) { sprite.sprite = null; sprite.enabled = false; }
            host.transform.localScale = Vector3.one * scale;
            _root = new GameObject("WaterDragonGeometry");
            _root.transform.SetParent(host.transform, false);
            // The mouth remains the host origin. Horizontal mirroring keeps
            // the horns upright at both swimming turns; no compressed body.
            _root.transform.localPosition = new Vector3(-0.64f, -0.02f, 0f);
            if (_material == null) _material = new Material(Shader.Find("Sprites/Default"))
                { hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };
            UpdateBody(0f);
            _body = Draw(_root.transform, "JadeDragonBody", new Color(0.08f, 0.57f, 0.53f, 0.64f), 0.14f, _bodyPoints);
            _light = Draw(_root.transform, "WaterDragonLight", new Color(0.63f, 1f, 0.91f, 0.80f), 0.045f, _bodyPoints);
            _body.widthMultiplier = 0.14f;
            _light.widthMultiplier = 0.045f;
            _body.widthCurve = _light.widthCurve = AnimationCurve.Linear(0f, 0.18f, 1f, 1f);
            Draw(_root.transform, "DragonHead", new Color(0.30f, 0.86f, 0.71f), 0.050f,
                new Vector3(0.18f, 0f, 0f), new Vector3(0.33f, 0.13f, 0f),
                new Vector3(0.49f, 0.08f, 0f), new Vector3(0.64f, 0.02f, 0f),
                new Vector3(0.47f, -0.06f, 0f), new Vector3(0.27f, -0.09f, 0f), new Vector3(0.18f, 0f, 0f));
            Draw(_root.transform, "DragonHorn", new Color(0.82f, 1f, 0.76f), 0.025f,
                new Vector3(0.31f, 0.10f, 0f), new Vector3(0.27f, 0.24f, 0f), new Vector3(0.17f, 0.27f, 0f));
            Draw(_root.transform, "DragonWhisker", new Color(0.63f, 1f, 0.91f, 0.66f), 0.016f,
                new Vector3(0.50f, -0.04f, 0f), new Vector3(0.30f, -0.19f, 0f),
                new Vector3(0.07f, -0.14f, 0f), new Vector3(-0.07f, -0.22f, 0f));
            Draw(_root.transform, "DragonEye", new Color(1f, 0.95f, 0.64f), 0.04f,
                new Vector3(0.43f, 0.067f, 0f), new Vector3(0.46f, 0.067f, 0f));
            for (int i = 0; i < 3; i++)
            {
                float x = -0.43f + i * 0.24f;
                Draw(_root.transform, "WaterFin", new Color(0.44f, 0.90f, 0.79f, 0.5f), 0.025f,
                    new Vector3(x, 0f, 0f), new Vector3(x - 0.08f, 0.15f, 0f), new Vector3(x - 0.18f, 0.08f, 0f));
            }
        }
        internal void Sample(float flightAge)
        {
            UpdateBody(flightAge);
            _body?.SetPositions(_bodyPoints);
            _light?.SetPositions(_bodyPoints);
        }
        private void UpdateBody(float age)
        {
            for (int i = 0; i < _bodyPoints.Length; i++)
            {
                float f = i / (float)(_bodyPoints.Length - 1);
                _bodyPoints[i] = new Vector3(Mathf.Lerp(-0.85f, 0.23f, f),
                    Mathf.Sin(f * 9f - age * 8f) * 0.14f * (1f - f), 0f);
            }
        }
        private static LineRenderer Draw(Transform parent, string name, Color color, float width, params Vector3[] points)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            // Local part order; the flight owner's SortingGroup supplies board depth.
            line.sortingOrder = 0;
            line.numCornerVertices = 3;
            return line;
        }
        public void OnFlightStart() { }
        public void OnFlightEnd() { }
        public Vector3 GetTrailAnchorOffset() => new Vector3(-0.82f, 0f, 0f);
        public void Cleanup()
        {
            if (_root != null) { _root.SetActive(false); Object.Destroy(_root); _root = null; }
            _body = _light = null;
        }
    }
}
