using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// 离火咒使用的程序化火瓶。瓶体、火液、瓶塞与高光均由运行时 Mesh 组成，
    /// 不依赖卡背或临时图标资源，因此 F6/F7 只要使用同一 VfxProfile 就会一致。
    /// </summary>
    public sealed class ProceduralFireVialVisual : IProjectileVisual
    {
        public const string VisualTypeName = "ProceduralFireVial";

        private Transform _root;
        private FireVialSpinAnchor _spin;
        private MeshRenderer[] _renderers;
        private Mesh[] _meshes;
        private TrailRenderer _trail;

        private static Material _meshMaterial;
        private static Material _trailMaterial;

        static ProceduralFireVialVisual()
        {
            ProjectileVisualFactory.Register(
                VisualTypeName,
                () => new ProceduralFireVialVisual());
        }

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            SpriteRenderer sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null)
            {
                sprite.sprite = null;
                sprite.enabled = false;
            }

            host.transform.localScale = Vector3.one * scale;
            _root = EnsureChild(host.transform, "ProceduralFireVialRoot");
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            _spin = _root.GetComponent<FireVialSpinAnchor>();
            if (_spin == null)
                _spin = _root.gameObject.AddComponent<FireVialSpinAnchor>();
            _spin.SpinSpeed = profile != null
                ? Mathf.Clamp(profile.proceduralVialSpinSpeed, 180f, 1080f)
                : 620f;

            Color glass = profile != null
                ? profile.proceduralVialGlassColor
                : new Color(0.2f, 0.08f, 0.035f, 0.9f);
            Color fire = profile != null
                ? profile.proceduralVialFireColor
                : new Color(1f, 0.28f, 0.035f, 1f);
            Color core = profile != null
                ? profile.proceduralVialCoreColor
                : new Color(1f, 0.86f, 0.25f, 1f);
            Color cork = profile != null
                ? profile.proceduralVialCorkColor
                : new Color(0.24f, 0.095f, 0.035f, 1f);
            float fill = profile != null
                ? Mathf.Clamp(profile.proceduralVialFillRatio, 0.25f, 0.8f)
                : 0.56f;

            Vector2[] bottle =
            {
                new Vector2(-0.27f, -0.48f),
                new Vector2(0.27f, -0.48f),
                new Vector2(0.36f, -0.24f),
                new Vector2(0.31f, 0.18f),
                new Vector2(0.14f, 0.31f),
                new Vector2(0.12f, 0.48f),
                new Vector2(-0.12f, 0.48f),
                new Vector2(-0.14f, 0.31f),
                new Vector2(-0.31f, 0.18f),
                new Vector2(-0.36f, -0.24f)
            };
            Vector2[] liquid =
            {
                new Vector2(-0.255f, -0.43f),
                new Vector2(0.255f, -0.43f),
                new Vector2(0.31f, -0.23f),
                new Vector2(0.295f, -0.43f + fill * 0.72f),
                new Vector2(-0.295f, -0.43f + fill * 0.72f),
                new Vector2(-0.31f, -0.23f)
            };

            _meshes = new[]
            {
                CreatePolygon("FireVialGlass", bottle, glass),
                CreatePolygon("FireVialLiquid", liquid, fire),
                CreateQuad("FireVialCore",
                    new Vector2(-0.12f, -0.33f),
                    new Vector2(0.12f, -0.08f),
                    core),
                CreateQuad("FireVialCork",
                    new Vector2(-0.15f, 0.42f),
                    new Vector2(0.15f, 0.58f),
                    cork),
                CreateQuad("FireVialHighlight",
                    new Vector2(-0.23f, -0.03f),
                    new Vector2(-0.17f, 0.24f),
                    new Color(1f, 0.82f, 0.48f, 0.8f))
            };
            _renderers = new MeshRenderer[_meshes.Length];
            for (int i = 0; i < _meshes.Length; i++)
                _renderers[i] = ConfigurePart(_meshes[i].name, _meshes[i], 21 + i);

            _trail = ConfigureTrail(fire, core, profile);
            SetVisible(false);
        }

        public void OnFlightStart()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            _root.localRotation = Quaternion.Euler(0f, 0f, -18f);
            if (_spin != null) _spin.enabled = true;
            SetVisible(true);
            if (_trail != null)
            {
                _trail.Clear();
                _trail.emitting = true;
            }
        }

        public void OnFlightEnd()
        {
            if (_spin != null) _spin.enabled = false;
            if (_trail != null) _trail.emitting = false;
            SetVisible(false);
        }

        public Vector3 GetTrailAnchorOffset() => new Vector3(0f, -0.28f, 0f);

        public void Cleanup()
        {
            if (_meshes == null) return;
            for (int i = 0; i < _meshes.Length; i++)
                DestroyObject(_meshes[i]);
            _meshes = null;
        }

        private MeshRenderer ConfigurePart(string name, Mesh mesh, int order)
        {
            Transform part = EnsureChild(_root, name);
            MeshFilter filter = part.GetComponent<MeshFilter>();
            if (filter == null) filter = part.gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = part.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = GetMeshMaterial();
            renderer.sortingOrder = order;
            part.localPosition = Vector3.zero;
            part.localRotation = Quaternion.identity;
            part.localScale = Vector3.one;
            return renderer;
        }

        private TrailRenderer ConfigureTrail(
            Color fire,
            Color core,
            VfxProfile profile)
        {
            Transform anchor = EnsureChild(_root, "FireVialTrail");
            anchor.localPosition = new Vector3(0f, -0.32f, 0f);
            TrailRenderer trail = anchor.GetComponent<TrailRenderer>();
            if (trail == null)
                trail = anchor.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = GetTrailMaterial();
            trail.startColor = new Color(core.r, core.g, core.b, 0.88f);
            trail.endColor = new Color(fire.r, fire.g, fire.b, 0f);
            trail.time = profile != null
                ? Mathf.Max(0.12f, profile.trailTime)
                : 0.18f;
            trail.widthMultiplier = profile != null
                ? Mathf.Max(0.07f, profile.trailWidth)
                : 0.09f;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.25f),
                new Keyframe(0.18f, 1f),
                new Keyframe(1f, 0f));
            trail.minVertexDistance = 0.018f;
            trail.alignment = LineAlignment.View;
            trail.sortingOrder = 20;
            trail.emitting = false;
            return trail;
        }

        private void SetVisible(bool visible)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = visible;
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Mesh CreateQuad(
            string name,
            Vector2 min,
            Vector2 max,
            Color color)
        {
            return CreateMesh(name,
                new[]
                {
                    new Vector2(min.x, min.y),
                    new Vector2(min.x, max.y),
                    new Vector2(max.x, max.y),
                    new Vector2(max.x, min.y)
                },
                new[] { 0, 1, 2, 0, 2, 3 },
                color);
        }

        private static Mesh CreatePolygon(
            string name,
            Vector2[] vertices,
            Color color)
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
            string name,
            Vector2[] source,
            int[] triangles,
            Color color)
        {
            var vertices = new Vector3[source.Length];
            var colors = new Color[source.Length];
            var uv = new Vector2[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                vertices[i] = source[i];
                colors[i] = color;
                uv[i] = source[i] + Vector2.one * 0.5f;
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
            Shader shader = FindShader();
            if (shader == null) return null;
            _meshMaterial = new Material(shader)
            {
                name = "ProceduralFireVialMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
            return _meshMaterial;
        }

        private static Material GetTrailMaterial()
        {
            if (_trailMaterial != null) return _trailMaterial;
            Shader shader = FindShader();
            if (shader == null) return null;
            _trailMaterial = new Material(shader)
            {
                name = "ProceduralFireVialTrailMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
            _trailMaterial.SetInt("_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _trailMaterial.SetInt("_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.One);
            return _trailMaterial;
        }

        private static Shader FindShader()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            return shader;
        }

        private static void DestroyObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }

    internal sealed class FireVialSpinAnchor : MonoBehaviour
    {
        public float SpinSpeed { get; set; } = 620f;

        private void Update()
        {
            transform.Rotate(
                0f,
                0f,
                -SpinSpeed * Time.deltaTime,
                Space.Self);
        }
    }
}
