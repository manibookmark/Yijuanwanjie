using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// 开山斧的程序化投射物外观。斧刃、刃口、斧柄和箍环均由运行时 Mesh 生成，
    /// 斧体沿弧线飞行时绕自身中心高速旋转，并由刃尖 TrailRenderer 留下圆弧残影。
    /// </summary>
    public sealed class ProceduralAxeVisual : IProjectileVisual
    {
        public const string VisualTypeName = "ProceduralAxe";

        private Transform _root;
        private AxeSpinAnchor _spin;
        private MeshRenderer _handleRenderer;
        private MeshRenderer _steelRenderer;
        private MeshRenderer _edgeRenderer;
        private MeshRenderer _accentRenderer;
        private TrailRenderer _bladeTipTrail;
        private Mesh _handleMesh;
        private Mesh _steelMesh;
        private Mesh _edgeMesh;
        private Mesh _accentMesh;

        private static Material _meshMaterial;
        private static Material _trailMaterial;

        static ProceduralAxeVisual()
        {
            ProjectileVisualFactory.Register(
                VisualTypeName, () => new ProceduralAxeVisual());
        }

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            SpriteRenderer spriteRenderer = host.GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = null;
            spriteRenderer.enabled = false;

            host.transform.localScale = Vector3.one * scale;
            _root = EnsureChild(host.transform, "ProceduralAxeRoot");
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            _spin = _root.GetComponent<AxeSpinAnchor>();
            if (_spin == null)
                _spin = _root.gameObject.AddComponent<AxeSpinAnchor>();
            _spin.SpinSpeed = profile != null
                ? Mathf.Clamp(profile.proceduralAxeSpinSpeed, 360f, 1800f)
                : 1080f;

            Color steel = profile != null
                ? profile.proceduralAxeSteelColor
                : new Color(0.22f, 0.25f, 0.28f, 1f);
            Color edge = profile != null
                ? profile.proceduralAxeEdgeColor
                : new Color(0.92f, 0.78f, 0.34f, 1f);
            Color handle = profile != null
                ? profile.proceduralAxeHandleColor
                : new Color(0.22f, 0.08f, 0.035f, 1f);
            Color accent = profile != null
                ? profile.proceduralAxeAccentColor
                : new Color(0.78f, 0.22f, 0.08f, 1f);
            float headScale = profile != null
                ? Mathf.Clamp(profile.proceduralAxeHeadScale, 0.75f, 1.35f)
                : 1f;

            _handleMesh = CreateQuadMesh("AxeHandleMesh",
                new Vector2(-0.68f, -0.055f), new Vector2(0.34f, 0.055f), handle);
            _steelMesh = CreatePolygonMesh("AxeSteelMesh", new[]
            {
                new Vector2(0.02f, -0.12f * headScale),
                new Vector2(0.12f, -0.43f * headScale),
                new Vector2(0.42f, -0.52f * headScale),
                new Vector2(0.63f, -0.28f * headScale),
                new Vector2(0.69f, 0f),
                new Vector2(0.63f, 0.28f * headScale),
                new Vector2(0.42f, 0.52f * headScale),
                new Vector2(0.12f, 0.43f * headScale),
                new Vector2(0.02f, 0.12f * headScale)
            }, steel);
            _edgeMesh = CreateMesh("AxeEdgeMesh", new[]
                {
                    new Vector2(0.38f, -0.51f * headScale),
                    new Vector2(0.63f, -0.28f * headScale),
                    new Vector2(0.69f, 0f),
                    new Vector2(0.63f, 0.28f * headScale),
                    new Vector2(0.38f, 0.51f * headScale),
                    new Vector2(0.48f, 0.39f * headScale),
                    new Vector2(0.57f, 0.22f * headScale),
                    new Vector2(0.61f, 0f),
                    new Vector2(0.57f, -0.22f * headScale),
                    new Vector2(0.48f, -0.39f * headScale)
                },
                new[]
                {
                    0, 1, 8, 0, 8, 9,
                    1, 2, 7, 1, 7, 8,
                    2, 3, 6, 2, 6, 7,
                    3, 4, 5, 3, 5, 6
                }, edge);
            _accentMesh = CreateQuadMesh("AxeCollarMesh",
                new Vector2(-0.01f, -0.15f), new Vector2(0.18f, 0.15f), accent);

            _handleRenderer = ConfigureMeshPart("AxeHandle", _handleMesh, 20);
            _steelRenderer = ConfigureMeshPart("AxeSteel", _steelMesh, 21);
            _edgeRenderer = ConfigureMeshPart("AxeEdge", _edgeMesh, 22);
            _accentRenderer = ConfigureMeshPart("AxeCollar", _accentMesh, 23);
            _bladeTipTrail = ConfigureBladeTipTrail(edge, profile);

            SetVisible(false);
        }

        public void OnFlightStart()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            _root.localRotation = Quaternion.identity;
            if (_spin != null) _spin.enabled = true;
            SetVisible(true);
            if (_bladeTipTrail != null)
            {
                _bladeTipTrail.Clear();
                _bladeTipTrail.emitting = true;
            }
        }

        public void OnFlightEnd()
        {
            if (_spin != null) _spin.enabled = false;
            SetVisible(false);
            if (_bladeTipTrail != null)
                _bladeTipTrail.emitting = false;
        }

        public Vector3 GetTrailAnchorOffset() => Vector3.zero;

        public void Cleanup()
        {
            DestroyMesh(_handleMesh);
            DestroyMesh(_steelMesh);
            DestroyMesh(_edgeMesh);
            DestroyMesh(_accentMesh);
        }

        private MeshRenderer ConfigureMeshPart(string name, Mesh mesh, int sortingOrder)
        {
            Transform part = EnsureChild(_root, name);
            MeshFilter filter = part.GetComponent<MeshFilter>();
            if (filter == null) filter = part.gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = part.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = GetMeshMaterial();
            renderer.sortingOrder = sortingOrder;
            part.localPosition = Vector3.zero;
            part.localRotation = Quaternion.identity;
            part.localScale = Vector3.one;
            return renderer;
        }

        private TrailRenderer ConfigureBladeTipTrail(
            Color color, VfxProfile profile)
        {
            Transform tip = EnsureChild(_root, "AxeBladeTipTrail");
            tip.localPosition = new Vector3(0.61f, 0.28f, 0f);
            TrailRenderer trail = tip.GetComponent<TrailRenderer>();
            if (trail == null) trail = tip.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = GetTrailMaterial();
            Color trailColor = profile != null
                ? Color.Lerp(color, profile.trailColor, 0.35f)
                : color;
            trail.startColor = new Color(
                trailColor.r, trailColor.g, trailColor.b, 0.9f);
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trail.time = profile != null
                ? Mathf.Max(0.14f, profile.trailTime * 0.85f)
                : 0.14f;
            trail.widthMultiplier = profile != null
                ? Mathf.Max(0.075f, profile.trailWidth * 0.8f)
                : 0.075f;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.22f, 1f),
                new Keyframe(1f, 0f));
            trail.minVertexDistance = 0.015f;
            trail.numCornerVertices = 2;
            trail.numCapVertices = 2;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.sortingOrder = 19;
            trail.emitting = false;
            return trail;
        }

        private void SetVisible(bool visible)
        {
            if (_handleRenderer != null) _handleRenderer.enabled = visible;
            if (_steelRenderer != null) _steelRenderer.enabled = visible;
            if (_edgeRenderer != null) _edgeRenderer.enabled = visible;
            if (_accentRenderer != null) _accentRenderer.enabled = visible;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var childObject = new GameObject(name);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        private static Mesh CreateQuadMesh(
            string name, Vector2 min, Vector2 max, Color color)
        {
            return CreateMesh(name,
                new[]
                {
                    new Vector2(min.x, min.y),
                    new Vector2(min.x, max.y),
                    new Vector2(max.x, max.y),
                    new Vector2(max.x, min.y)
                },
                new[] { 0, 1, 2, 0, 2, 3 }, color);
        }

        private static Mesh CreatePolygonMesh(
            string name, Vector2[] vertices, Color color)
        {
            int[] triangles = new int[(vertices.Length - 2) * 3];
            for (int i = 0; i < vertices.Length - 2; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
            return CreateMesh(name, vertices, triangles, color);
        }

        private static Mesh CreateMesh(
            string name, Vector2[] sourceVertices, int[] triangles, Color color)
        {
            var vertices = new Vector3[sourceVertices.Length];
            var colors = new Color[sourceVertices.Length];
            var uv = new Vector2[sourceVertices.Length];
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                vertices[i] = sourceVertices[i];
                colors[i] = color;
                uv[i] = sourceVertices[i] + Vector2.one * 0.5f;
            }

            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                colors = colors,
                uv = uv,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material GetMeshMaterial()
        {
            if (_meshMaterial != null) return _meshMaterial;
            Shader shader = FindTransparentShader();
            if (shader == null) return null;
            _meshMaterial = new Material(shader)
            {
                name = "ProceduralAxeMeshMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
            return _meshMaterial;
        }

        private static Material GetTrailMaterial()
        {
            if (_trailMaterial != null) return _trailMaterial;
            Shader shader = FindTransparentShader();
            if (shader == null) return null;
            _trailMaterial = new Material(shader)
            {
                name = "ProceduralAxeTrailMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
            _trailMaterial.SetInt("_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _trailMaterial.SetInt("_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.One);
            return _trailMaterial;
        }

        private static Shader FindTransparentShader()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            return shader;
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Object.Destroy(mesh);
            else Object.DestroyImmediate(mesh);
        }
    }

    internal sealed class AxeSpinAnchor : MonoBehaviour
    {
        public float SpinSpeed { get; set; } = 1080f;

        private void Update()
        {
            transform.Rotate(0f, 0f, -SpinSpeed * Time.deltaTime,
                Space.Self);
        }
    }
}
