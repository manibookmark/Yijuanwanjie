using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>只拥有太阳弹视觉，轨迹、命中与伤害仍由投射物控制器管理。</summary>
    public sealed class ProceduralSolarBoltVisual : IProjectileVisual, IProjectileLaunchVisual
    {
        public const string VisualTypeName = "ProceduralSolarBolt";
        private Transform _host;
        private GameObject _root;
        private SolarHaloPulse _pulse;
        private Color _core;
        private Color _aura;
        private float _scale;

        public void Initialize(GameObject host, VfxProfile profile, float scale)
        {
            _host = host.transform;
            _scale = scale;
            _core = profile != null ? profile.wandBoltCoreColor : new Color(1f, .91f, .44f);
            _aura = profile != null ? profile.wandBoltAuraColor : new Color(1f, .23f, .025f);
            var sprite = host.GetComponent<SpriteRenderer>();
            if (sprite != null) { sprite.sprite = null; sprite.enabled = false; }
            _host.localScale = Vector3.one * scale;
            _root = new GameObject("SolarBoltRoot");
            _root.transform.SetParent(_host, false);
            _pulse = _root.AddComponent<SolarHaloPulse>();
            _pulse.Configure(_core, _aura, false);
            _root.SetActive(false);
        }

        public IEnumerator PrepareLaunch()
        {
            if (_root == null) yield break;
            _root.SetActive(true);
            _pulse.enabled = false;
            float time = 0f;
            _pulse.SetCharge(0f);
            while (time < .22f)
            {
                if (_root == null || !_root.activeInHierarchy) yield break;
                time += Time.deltaTime;
                _pulse.SetCharge(Mathf.Clamp01(time / .22f));
                yield return null;
            }
        }

        public void OnFlightStart()
        {
            if (_root == null) return;
            _root.SetActive(true);
            _pulse.BeginFlight();
        }

        public void OnFlightEnd()
        {
            if (_root == null || _host == null) return;
            var impact = new GameObject("SolarImpactBurst");
            impact.transform.SetParent(_host.parent, false);
            impact.transform.position = _host.position;
            impact.transform.localScale = Vector3.one * _scale;
            impact.AddComponent<SolarHaloPulse>().Configure(_core, _aura, true);
            _root.SetActive(false);
        }

        public Vector3 GetTrailAnchorOffset() => new Vector3(-.45f, 0f, 0f);

        public void Cleanup()
        {
            if (_root == null) return;
            if (Application.isPlaying) Object.Destroy(_root);
            else Object.DestroyImmediate(_root);
            _root = null;
        }
    }

    /// <summary>
    /// 太阳弹蓄力、飞行与独立命中余焰的单一视觉 owner。
    /// 几何、颜色和时间均在此封装；不访问原生粒子、不写轨迹或玩法状态。
    /// 各阶段共享同一几何语言，新增其他卡牌职责时应重新审查边界。
    /// </summary>
    internal sealed class SolarHaloPulse : MonoBehaviour
    {
        private sealed class Layer
        {
            public Transform transform;
            public Renderer renderer;
            public readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
            public void Pose(Vector3 position, Vector3 scale, float angle, float alpha)
            {
                transform.localPosition = position;
                transform.localScale = scale;
                transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                tint.SetColor("_Color", new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
                renderer.SetPropertyBlock(tint);
            }
        }

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private Material _material;
        private Layer _glow, _shell, _core;
        private Layer[] _flames, _sparks;
        private LineRenderer[] _rings;
        private Color _auraColor, _lightColor;
        private bool _impact;
        private float _elapsed;

        public void Configure(Color core, Color aura, bool impact)
        {
            _impact = impact;
            _auraColor = aura;
            _lightColor = Color.Lerp(core, Color.white, .82f);
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                if (impact)
                {
                    if (Application.isPlaying) Destroy(gameObject);
                    else DestroyImmediate(gameObject);
                }
                return;
            }
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _glow = Disc("SoftHeat", aura, .18f, false, 20);
            _shell = Disc("MoltenShell", Color.Lerp(aura, core, .2f), .75f, true, 22);
            _core = Disc("WhiteGoldMass", _lightColor, .9f, false, 25);
            _flames = new Layer[impact ? 11 : 3];
            for (int i = 0; i < _flames.Length; i++)
                _flames[i] = Flame("Flame" + i, Color.Lerp(aura, core, .35f), 23);
            _sparks = new Layer[impact ? 13 : 8];
            for (int i = 0; i < _sparks.Length; i++)
                _sparks[i] = Flame("Ember" + i, core, 24);
            _rings = new[] { Ring("PressureFront", core, .045f), Ring("DelayedShock", aura, .026f) };
            if (impact) AnimateImpact(0f);
            else SetCharge(0f);
        }

        // Two radial bands retain a solid centre; transparent perimeter removes hard polygon edges.
        private Layer Disc(string name, Color color, float solidRadius, bool jagged, int order)
        {
            const int count = 64;
            var vertices = new Vector3[1 + count * 2];
            var colors = new Color[vertices.Length];
            var triangles = new int[count * 9];
            colors[0] = color;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                float radius = jagged ? 1f + .13f * Mathf.Sin(angle * 7f) + .07f * Mathf.Sin(angle * 13f) : 1f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices[1 + i] = direction * radius * solidRadius;
                vertices[1 + count + i] = direction * radius;
                colors[1 + i] = color;
                colors[1 + count + i] = new Color(color.r, color.g, color.b, 0f);
                int next = (i + 1) % count;
                int t = i * 9;
                triangles[t] = 0; triangles[t + 1] = 1 + i; triangles[t + 2] = 1 + next;
                triangles[t + 3] = 1 + i; triangles[t + 4] = 1 + count + i; triangles[t + 5] = 1 + count + next;
                triangles[t + 6] = 1 + i; triangles[t + 7] = 1 + count + next; triangles[t + 8] = 1 + next;
            }
            return MeshLayer(name, vertices, colors, triangles, order);
        }

        // A white-hot spine with transparent tapered edges; +X is the outward flame direction.
        private Layer Flame(string name, Color color, int order)
        {
            var clear = new Color(color.r, color.g, color.b, 0f);
            return MeshLayer(name,
                new[] { Vector3.zero, new Vector3(.28f, .5f, 0f), new Vector3(1f, .06f, 0f),
                    new Vector3(.28f, -.5f, 0f), new Vector3(.33f, 0f, 0f) },
                new[] { clear, clear, clear, clear, color },
                new[] { 4, 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0 }, order);
        }

        private Layer MeshLayer(string name, Vector3[] vertices, Color[] colors, int[] triangles, int order)
        {
            var child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            child.transform.SetParent(transform, false);
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = order;
            return new Layer { transform = child.transform, renderer = renderer };
        }

        private LineRenderer Ring(string name, Color color, float width)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.loop = true; line.positionCount = 64;
            line.sharedMaterial = _material; line.widthMultiplier = width;
            line.startColor = line.endColor = color; line.sortingOrder = 26;
            for (int i = 0; i < 64; i++)
            {
                float a = i * Mathf.PI * 2f / 64;
                line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            }
            return line;
        }

        private void SetRing(int index, float radius, float alpha)
        {
            var line = _rings[index];
            line.transform.localScale = Vector3.one * radius;
            Color color = index == 0 ? _lightColor : _auraColor;
            color.a *= Mathf.Clamp01(alpha);
            line.startColor = line.endColor = color;
        }

        public void SetCharge(float t)
        {
            if (_material == null) return;
            float gather = t * t;
            _glow.Pose(Vector3.zero, Vector3.one * Mathf.Lerp(.75f, .4f, gather), 0f, .28f);
            _shell.Pose(Vector3.zero, Vector3.one * Mathf.Lerp(.12f, .38f, t), t * 80f, .7f);
            _core.Pose(Vector3.zero, Vector3.one * Mathf.Lerp(.035f, .23f, gather), 0f, 1f);
            for (int i = 0; i < _flames.Length; i++)
                _flames[i].Pose(Vector3.zero, Vector3.zero, 0f, 0f);
            for (int i = 0; i < _sparks.Length; i++)
            {
                float angle = i * 45f + 12f;
                Vector3 direction = Quaternion.Euler(0f, 0f, angle) * Vector3.right;
                _sparks[i].Pose(direction * Mathf.Lerp(.92f, .24f, gather),
                    new Vector3(.25f, .065f, 1f), angle + 180f, Mathf.Sin(t * Mathf.PI));
            }
            SetRing(0, Mathf.Lerp(.88f, .31f, gather), .35f * Mathf.Sin(t * Mathf.PI));
            SetRing(1, 0f, 0f);
        }

        public void BeginFlight()
        {
            _elapsed = 0f;
            enabled = true;
            if (_material != null) AnimateFlight();
        }

        private void Update()
        {
            if (_material == null) return;
            _elapsed += Time.deltaTime;
            if (_impact)
            {
                AnimateImpact(_elapsed);
                if (_elapsed >= .52f) Destroy(gameObject);
            }
            else AnimateFlight();
        }

        private void AnimateFlight()
        {
            float pulse = 1f + .035f * Mathf.Sin(_elapsed * 29f);
            _glow.Pose(Vector3.zero, new Vector3(.66f, .55f, 1f), 0f, .32f);
            _shell.Pose(Vector3.zero, Vector3.one * (.39f * pulse), _elapsed * 130f, 1f);
            _core.Pose(new Vector3(.035f, 0f, 0f), new Vector3(.26f, .22f, 1f), 0f, 1f);
            for (int i = 0; i < _flames.Length; i++)
                _flames[i].Pose(new Vector3(-.14f, (i - 1) * .12f, 0f),
                    new Vector3(1.05f + .18f * Mathf.Sin(_elapsed * 24f + i * 2f), .29f, 1f),
                    180f + (i - 1) * 13f, .85f);
            for (int i = 0; i < _sparks.Length; i++)
            {
                float angle = i * 45f + _elapsed * 95f;
                Vector3 direction = Quaternion.Euler(0f, 0f, angle) * Vector3.right;
                _sparks[i].Pose(direction * .27f,
                    new Vector3(.24f + .07f * Mathf.Sin(_elapsed * 21f + i), .09f, 1f), angle, .8f);
            }
            SetRing(0, 0f, 0f); SetRing(1, 0f, 0f);
        }

        private void AnimateImpact(float time)
        {
            float burst = Mathf.Clamp01(time / .19f);
            float fade = 1f - Mathf.Clamp01(time / .32f);
            _glow.Pose(Vector3.zero, Vector3.one * Mathf.Lerp(.42f, 1f, burst), 0f, fade * .4f);
            _shell.Pose(Vector3.zero, Vector3.one * Mathf.Lerp(.38f, .62f, burst), time * 60f, fade * fade);
            _core.Pose(Vector3.zero, Vector3.one * Mathf.Lerp(.36f, .03f, Mathf.Clamp01(time / .1f)), 0f,
                1f - Mathf.Clamp01(time / .12f));
            for (int i = 0; i < _flames.Length; i++)
            {
                float angle = i * 360f / _flames.Length + 17f;
                Vector3 direction = Quaternion.Euler(0f, 0f, angle) * Vector3.right;
                float variation = .8f + .22f * Mathf.Sin(i * 4.7f);
                _flames[i].Pose(direction * Mathf.Lerp(.05f, .54f, burst),
                    new Vector3(Mathf.Lerp(.3f, .83f, burst) * variation, .23f * fade, 1f), angle, fade);
            }
            for (int i = 0; i < _sparks.Length; i++)
            {
                float angle = i * 137.5f;
                Vector3 direction = Quaternion.Euler(0f, 0f, angle) * Vector3.right;
                float speed = 1.3f + (i % 4) * .32f;
                float emberFade = Mathf.Clamp01((.48f - time) / .25f);
                _sparks[i].Pose(direction * (.25f + time * speed),
                    new Vector3(.09f * emberFade, .05f * emberFade, 1f), angle, emberFade);
            }
            SetRing(0, Mathf.Lerp(.3f, 1.06f, burst), .8f * (1f - burst));
            float second = Mathf.Clamp01((time - .07f) / .3f);
            SetRing(1, Mathf.Lerp(.42f, 1.22f, second), time < .07f ? 0f : .55f * (1f - second));
        }

        private void OnDestroy()
        {
            foreach (var mesh in _meshes)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
            if (_material == null) return;
            if (Application.isPlaying) Destroy(_material);
            else DestroyImmediate(_material);
        }
    }
}
