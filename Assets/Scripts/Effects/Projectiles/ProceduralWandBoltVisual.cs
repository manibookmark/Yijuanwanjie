using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// 法杖类卡牌共用的程序化灵弹。核心、外焰与法环均由运行时几何生成，
    /// 烈焰杖和天师杖只通过 VfxProfile 配色与法环开关形成不同语言。
    /// </summary>
    public sealed class ProceduralWandBoltVisual : IProjectileVisual
    {
        public const string VisualTypeName = "ProceduralWandBolt";

        private Transform _root;
        private Transform _aura;
        private Transform _runeRoot;
        private MeshRenderer[] _renderers;
        private LineRenderer[] _rings;
        private Mesh[] _meshes;
        private WandBoltPulseAnchor _pulse;
        private float _trailAnchorX;

        private static Material _meshMaterial;
        private static Material _lineMaterial;

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            SpriteRenderer sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null)
            {
                sprite.sprite = null;
                sprite.enabled = false;
            }

            float length = profile != null
                ? Mathf.Clamp(profile.wandBoltLengthScale, 0.5f, 2.5f)
                : 1f;
            float width = profile != null
                ? Mathf.Clamp(profile.wandBoltWidthScale, 0.5f, 2.5f)
                : 1f;
            Color core = profile != null
                ? profile.wandBoltCoreColor
                : new Color(1f, 0.86f, 0.28f, 1f);
            Color aura = profile != null
                ? profile.wandBoltAuraColor
                : new Color(1f, 0.28f, 0.04f, 0.72f);
            Color rune = profile != null
                ? profile.wandBoltRuneColor
                : new Color(1f, 0.92f, 0.58f, 0.9f);
            bool showRuneRing = profile != null && profile.wandBoltRuneRing;

            host.transform.localScale = Vector3.one * scale;
            _root = EnsureChild(host.transform, "ProceduralWandBoltRoot");
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            _aura = EnsureChild(_root, "Aura");
            _aura.localPosition = Vector3.zero;
            _aura.localRotation = Quaternion.identity;
            _aura.localScale = Vector3.one;

            _meshes = new[]
            {
                CreateBoltMesh("WandBoltAura", 0.66f * length,
                    0.22f * width, aura),
                CreateBoltMesh("WandBoltCore", 0.54f * length,
                    0.105f * width, core),
                CreateFinMesh("WandBoltFins", 0.46f * length,
                    0.28f * width, rune)
            };
            _renderers = new[]
            {
                ConfigureMesh(_aura, "AuraMesh", _meshes[0], 20),
                ConfigureMesh(_root, "CoreMesh", _meshes[1], 22),
                ConfigureMesh(_root, "FinMesh", _meshes[2], 21)
            };

            _runeRoot = EnsureChild(_root, "RuneRings");
            _runeRoot.localPosition = Vector3.zero;
            _runeRoot.localRotation = Quaternion.identity;
            _runeRoot.localScale = Vector3.one;
            _rings = new[]
            {
                ConfigureRing(_runeRoot, "OuterRuneRing", rune,
                    0.31f * width, 0.018f, 24),
                ConfigureRing(_runeRoot, "InnerRuneRing", core,
                    0.205f * width, 0.012f, 23)
            };

            _pulse = _root.GetComponent<WandBoltPulseAnchor>();
            if (_pulse == null)
                _pulse = _root.gameObject.AddComponent<WandBoltPulseAnchor>();
            _pulse.Configure(
                _aura,
                _runeRoot,
                profile != null ? profile.wandBoltSpinSpeed : 360f,
                profile != null ? profile.wandBoltPulseSpeed : 6f,
                showRuneRing);

            _trailAnchorX = -0.58f * length;
            SetVisible(false, showRuneRing);
        }

        public void OnFlightStart()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            if (_pulse != null) _pulse.enabled = true;
            SetVisible(true, _pulse != null && _pulse.ShowRuneRing);
        }

        public void OnFlightEnd()
        {
            if (_pulse != null) _pulse.enabled = false;
            SetVisible(false, false);
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public Vector3 GetTrailAnchorOffset()
        {
            return new Vector3(_trailAnchorX, 0f, 0f);
        }

        public void Cleanup()
        {
            if (_meshes == null) return;
            for (int i = 0; i < _meshes.Length; i++)
                DestroyObject(_meshes[i]);
            _meshes = null;
        }

        private void SetVisible(bool visible, bool showRuneRing)
        {
            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                        _renderers[i].enabled = visible;
                }
            }
            if (_rings == null) return;
            for (int i = 0; i < _rings.Length; i++)
            {
                if (_rings[i] != null)
                    _rings[i].enabled = visible && showRuneRing;
            }
        }

        private static MeshRenderer ConfigureMesh(
            Transform parent,
            string name,
            Mesh mesh,
            int sortingOrder)
        {
            Transform child = EnsureChild(parent, name);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            MeshFilter filter = child.GetComponent<MeshFilter>();
            if (filter == null)
                filter = child.gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = child.gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = GetMeshMaterial();
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static LineRenderer ConfigureRing(
            Transform parent,
            string name,
            Color color,
            float radius,
            float width,
            int sortingOrder)
        {
            Transform child = EnsureChild(parent, name);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            LineRenderer line = child.GetComponent<LineRenderer>();
            if (line == null)
                line = child.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 20;
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
            line.sharedMaterial = GetLineMaterial();
            line.sortingOrder = sortingOrder;
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = Mathf.PI * 2f * i / line.positionCount;
                float serration = i % 2 == 0 ? 1f : 0.84f;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius * serration,
                    Mathf.Sin(angle) * radius * serration,
                    0f));
            }
            return line;
        }

        private static Mesh CreateBoltMesh(
            string name,
            float halfLength,
            float halfWidth,
            Color color)
        {
            return CreateMesh(
                name,
                new[]
                {
                    new Vector2(-halfLength, 0f),
                    new Vector2(-halfLength * 0.34f, halfWidth),
                    new Vector2(halfLength * 0.58f, halfWidth * 0.78f),
                    new Vector2(halfLength, 0f),
                    new Vector2(halfLength * 0.58f, -halfWidth * 0.78f),
                    new Vector2(-halfLength * 0.34f, -halfWidth)
                },
                new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 },
                color);
        }

        private static Mesh CreateFinMesh(
            string name,
            float length,
            float width,
            Color color)
        {
            return CreateMesh(
                name,
                new[]
                {
                    new Vector2(-length, 0f),
                    new Vector2(-length * 0.48f, width),
                    new Vector2(-length * 0.22f, 0f),
                    new Vector2(-length * 0.48f, -width)
                },
                new[] { 0, 1, 2, 0, 2, 3 },
                color);
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

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Material GetMeshMaterial()
        {
            if (_meshMaterial != null) return _meshMaterial;
            _meshMaterial = CreateMaterial("ProceduralWandBoltMaterial");
            return _meshMaterial;
        }

        private static Material GetLineMaterial()
        {
            if (_lineMaterial != null) return _lineMaterial;
            _lineMaterial = CreateMaterial("ProceduralWandBoltLineMaterial");
            return _lineMaterial;
        }

        private static Material CreateMaterial(string name)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;
            return new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
        }

        private static void DestroyObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }

    internal sealed class WandBoltPulseAnchor : MonoBehaviour
    {
        private Transform _aura;
        private Transform _runeRoot;
        private float _spinSpeed;
        private float _pulseSpeed;

        public bool ShowRuneRing { get; private set; }

        public void Configure(
            Transform aura,
            Transform runeRoot,
            float spinSpeed,
            float pulseSpeed,
            bool showRuneRing)
        {
            _aura = aura;
            _runeRoot = runeRoot;
            _spinSpeed = Mathf.Clamp(spinSpeed, 0f, 1080f);
            _pulseSpeed = Mathf.Clamp(pulseSpeed, 0f, 12f);
            ShowRuneRing = showRuneRing;
        }

        private void Update()
        {
            float pulse = 1f + Mathf.Sin(Time.time * _pulseSpeed) * 0.12f;
            if (_aura != null)
                _aura.localScale = new Vector3(1f, pulse, 1f);
            if (_runeRoot == null || !ShowRuneRing) return;
            _runeRoot.Rotate(
                0f,
                0f,
                _spinSpeed * Time.deltaTime,
                Space.Self);
        }
    }
}
