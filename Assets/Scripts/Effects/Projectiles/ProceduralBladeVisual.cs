using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// 程序化刀刃模式的投射物外观。
    /// 运行时用 Mesh + ParticleSystem 绘制三角形刀刃，不依赖图片素材。
    /// 飞刀/飞剑/神机箭可复用，通过 VfxProfile 的 proceduralBlade 系列字段配置颜色和形状。
    /// </summary>
    public class ProceduralBladeVisual : IProjectileVisual
    {
        private SpriteRenderer _renderer;
        private ParticleSystem _bladeEdgeParticle;
        private ParticleSystem _bladeCoreParticle;
        private Mesh _bladeMesh;
        private float _tailOffset = -0.55f;
        private float _baseScale = 1f;
        private float _flightStretchX = 2.5f;
        private Transform _hostTransform;

        private static Material _bladeMaterial;

        // 静态注册
        static ProceduralBladeVisual()
        {
            ProjectileVisualFactory.Register("ProceduralBlade", () => new ProceduralBladeVisual());
        }

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            _renderer = host.GetComponent<SpriteRenderer>();
            _renderer.sortingOrder = 20;
            _renderer.sprite = null;
            _renderer.enabled = false;

            // 从 profile 读取程序化刀刃参数
            Color bladeColor = profile.proceduralBladeColor;
            Color edgeColor = profile.proceduralBladeEdgeColor;
            ProceduralBladeShape shape = profile.proceduralBladeShape;
            float lengthScale = Mathf.Clamp(
                profile.proceduralBladeLengthScale,
                0.5f,
                2.5f);
            float widthRatio = Mathf.Clamp(profile.proceduralBladeWidthRatio, 0.08f, 0.5f);
            float edgeScale = Mathf.Clamp(profile.proceduralBladeEdgeScale, 1f, 1.3f);
            bool isSword = shape == ProceduralBladeShape.Sword;
            _tailOffset = (isSword ? -0.58f : -0.5f) * edgeScale;
            _flightStretchX = isSword ? 1.25f : 2.5f;

            CreateBladeMesh(shape, widthRatio, lengthScale);
            _bladeEdgeParticle = EnsureBladeParticle(host.transform, "ProceduralBladeEdge", edgeColor, edgeScale, 20);
            _bladeCoreParticle = EnsureBladeParticle(host.transform, "ProceduralBladeCore", bladeColor, 1f, 21);
            _bladeCoreParticle.transform.localScale = new Vector3(
                1f,
                isSword ? 0.86f : 0.72f,
                1f);

            _hostTransform = host.transform;
            _baseScale = scale;
            _hostTransform.localScale = Vector3.one * scale;
        }

        public void OnFlightStart()
        {
            SetParticlesActive(true);
            if (_hostTransform != null)
                _hostTransform.localScale = new Vector3(
                    _baseScale * _flightStretchX,
                    _baseScale,
                    _baseScale);
        }

        public void OnFlightEnd()
        {
            SetParticlesActive(false);
            if (_hostTransform != null)
                _hostTransform.localScale = Vector3.one * _baseScale;
        }

        public Vector3 GetTrailAnchorOffset()
        {
            return new Vector3(_tailOffset, 0f, 0f);
        }

        public void Cleanup()
        {
            if (_bladeMesh != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_bladeMesh);
                else
                    Object.DestroyImmediate(_bladeMesh);
            }
        }

        // ==================== 内部方法 ====================

        private void CreateBladeMesh(
            ProceduralBladeShape shape,
            float widthRatio,
            float lengthScale)
        {
            if (_bladeMesh == null)
            {
                _bladeMesh = new Mesh
                {
                    name = "ProceduralBladeMesh",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            else
            {
                _bladeMesh.Clear();
            }

            if (shape == ProceduralBladeShape.Sword)
            {
                BuildSwordMesh(widthRatio, lengthScale);
            }
            else
            {
                float halfWidth = widthRatio * 0.5f;
                _bladeMesh.vertices = new[]
                {
                    new Vector3(-0.5f, 0f, 0f),
                    new Vector3(-0.18f, halfWidth, 0f),
                    new Vector3(0.62f * lengthScale, 0f, 0f),
                    new Vector3(-0.18f, -halfWidth, 0f)
                };
                _bladeMesh.uv = new[]
                {
                    new Vector2(0f, 0.5f),
                    new Vector2(0.29f, 1f),
                    new Vector2(1f, 0.5f),
                    new Vector2(0.29f, 0f)
                };
                _bladeMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            }
            _bladeMesh.RecalculateBounds();
        }

        private void BuildSwordMesh(float widthRatio, float lengthScale)
        {
            float halfBlade = widthRatio * 0.5f;
            float bladeTip = 0.72f * lengthScale;
            float bladeShoulder = bladeTip - Mathf.Min(
                0.2f,
                0.14f * lengthScale);
            var vertices = new List<Vector3>(20)
            {
                new Vector3(-0.12f, -halfBlade, 0f),
                new Vector3(-0.12f, halfBlade, 0f),
                new Vector3(bladeShoulder, halfBlade * 0.68f, 0f),
                new Vector3(bladeTip, 0f, 0f),
                new Vector3(bladeShoulder, -halfBlade * 0.68f, 0f)
            };
            var triangles = new List<int>(30)
            {
                0, 1, 2,
                0, 2, 4,
                2, 3, 4
            };

            AddQuad(
                vertices,
                triangles,
                -0.25f,
                -halfBlade * 2.55f,
                -0.1f,
                halfBlade * 2.55f);
            AddQuad(
                vertices,
                triangles,
                -0.52f,
                -halfBlade * 0.64f,
                -0.24f,
                halfBlade * 0.64f);
            AddDiamond(
                vertices,
                triangles,
                new Vector2(-0.55f, 0f),
                halfBlade * 0.9f);

            _bladeMesh.SetVertices(vertices);
            _bladeMesh.SetTriangles(triangles, 0);
            _bladeMesh.uv = new Vector2[vertices.Count];
        }

        private static void AddQuad(
            List<Vector3> vertices,
            List<int> triangles,
            float minX,
            float minY,
            float maxX,
            float maxY)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(minX, minY, 0f));
            vertices.Add(new Vector3(minX, maxY, 0f));
            vertices.Add(new Vector3(maxX, maxY, 0f));
            vertices.Add(new Vector3(maxX, minY, 0f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private static void AddDiamond(
            List<Vector3> vertices,
            List<int> triangles,
            Vector2 center,
            float radius)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(center.x - radius, center.y, 0f));
            vertices.Add(new Vector3(center.x, center.y + radius, 0f));
            vertices.Add(new Vector3(center.x + radius, center.y, 0f));
            vertices.Add(new Vector3(center.x, center.y - radius, 0f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        private ParticleSystem EnsureBladeParticle(Transform host, string name, Color color, float visualScale, int sortingOrder)
        {
            Transform child = host.Find(name);
            if (child == null)
            {
                var childGo = new GameObject(name, typeof(ParticleSystem));
                childGo.transform.SetParent(host, false);
                child = childGo.transform;
            }

            var ps = child.GetComponent<ParticleSystem>();
            ps.gameObject.SetActive(true);
            ps.transform.localPosition = Vector3.zero;
            ps.transform.localRotation = Quaternion.identity;
            ps.transform.localScale = Vector3.one * visualScale;

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 600f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startColor = color;
            main.maxParticles = 1;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.mesh = _bladeMesh;
            renderer.sharedMaterial = GetBladeMaterial();
            renderer.sortingOrder = sortingOrder;

            RestartParticle(ps);
            return ps;
        }

        private void SetParticlesActive(bool active)
        {
            if (_bladeEdgeParticle != null)
                _bladeEdgeParticle.gameObject.SetActive(active);
            if (_bladeCoreParticle != null)
                _bladeCoreParticle.gameObject.SetActive(active);

            if (active)
            {
                RestartParticle(_bladeEdgeParticle);
                RestartParticle(_bladeCoreParticle);
            }
        }

        private static void RestartParticle(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Emit(1);
            ps.Play();
        }

        private static Material GetBladeMaterial()
        {
            if (_bladeMaterial != null) return _bladeMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;

            _bladeMaterial = new Material(shader)
            {
                name = "ProceduralBladeMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
            return _bladeMaterial;
        }
    }
}
