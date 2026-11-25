using UnityEngine;

namespace WanjieScroll.Effects.Projectiles
{
    /// <summary>
    /// Runtime-only impact layers for ProceduralAxe. The slash, shock ring,
    /// sparks and debris are generated without imported texture assets.
    /// </summary>
    public sealed class ProceduralAxeImpactVisual : MonoBehaviour
    {
        public const string ImpactRootName = "ProceduralAxeImpact";
        public const string SparkRootName = "ProceduralAxeSparks";

        private const float ImpactLifetime = 0.42f;
        private LineRenderer _slashOuter;
        private LineRenderer _slashCore;
        private LineRenderer _shockRing;
        private LineRenderer[] _cracks;
        private Transform _slashRoot;
        private Transform _shockRoot;
        private float _elapsed;
        private float _lifetime;
        private float _scale;
        private Color _edgeColor;
        private Color _accentColor;
        private Color _debrisColor;

        private static Material _additiveMaterial;
        private static Material _alphaMaterial;

        public static GameObject PlayImpact(
            Transform parent, Vector3 position, VfxProfile profile, bool isCrit)
        {
            var root = new GameObject(ImpactRootName);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var visual = root.AddComponent<ProceduralAxeImpactVisual>();
            visual.BuildImpact(profile, isCrit);
            return root;
        }

        public static GameObject PlaySparks(
            Transform parent, Vector3 position, VfxProfile profile)
        {
            var root = new GameObject(SparkRootName);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var visual = root.AddComponent<ProceduralAxeImpactVisual>();
            visual.BuildSparks(profile);
            if (root.transform.childCount == 0)
            {
                Destroy(root);
                return null;
            }
            return root;
        }

        private void BuildImpact(VfxProfile profile, bool isCrit)
        {
            _lifetime = ImpactLifetime;
            _scale = isCrit ? 1.18f : 1f;
            _edgeColor = profile != null
                ? profile.proceduralAxeEdgeColor
                : new Color(1f, 0.78f, 0.22f, 1f);
            _accentColor = profile != null
                ? profile.proceduralAxeAccentColor
                : new Color(0.78f, 0.16f, 0.045f, 1f);
            _debrisColor = profile != null
                ? profile.secondaryParticleColor
                : new Color(0.24f, 0.12f, 0.055f, 0.95f);

            _slashRoot = CreateChild("AxeSlashArc");
            _slashRoot.localRotation = Quaternion.Euler(0f, 0f, -24f);
            _slashOuter = CreateArc(
                _slashRoot, "SlashOuter", 0.66f, 0.15f,
                _accentColor, 34);
            _slashCore = CreateArc(
                _slashRoot, "SlashCore", 0.64f, 0.055f,
                Color.Lerp(Color.white, _edgeColor, 0.35f), 35);

            _shockRoot = CreateChild("AxeShockwave");
            _shockRing = CreateRing(
                _shockRoot, "ShockRing", 0.48f, 0.065f,
                _edgeColor, 33);

            _cracks = new[]
            {
                CreateCrack("GroundCrackLeft", new[]
                {
                    new Vector3(-0.02f, -0.16f, 0f),
                    new Vector3(-0.18f, -0.27f, 0f),
                    new Vector3(-0.34f, -0.31f, 0f),
                    new Vector3(-0.48f, -0.42f, 0f)
                }),
                CreateCrack("GroundCrackCenter", new[]
                {
                    new Vector3(0f, -0.17f, 0f),
                    new Vector3(0.03f, -0.31f, 0f),
                    new Vector3(-0.04f, -0.45f, 0f),
                    new Vector3(0.02f, -0.57f, 0f)
                }),
                CreateCrack("GroundCrackRight", new[]
                {
                    new Vector3(0.03f, -0.16f, 0f),
                    new Vector3(0.2f, -0.25f, 0f),
                    new Vector3(0.33f, -0.37f, 0f),
                    new Vector3(0.5f, -0.4f, 0f)
                })
            };

            int debrisCount = profile != null
                ? Mathf.Max(0, profile.secondaryParticleCount)
                : 8;
            float debrisSize = profile != null
                ? Mathf.Max(0.055f, profile.secondaryParticleSize)
                : 0.075f;
            float debrisSpeed = profile != null
                ? Mathf.Max(1.8f, profile.secondaryParticleSpeed)
                : 2.4f;
            float debrisLifetime = profile != null
                ? Mathf.Max(0.24f, profile.secondaryParticleLifetime)
                : 0.32f;
            if (debrisCount > 0)
            {
                CreateBurst(
                    "AxeDebris", debrisCount, debrisSize, debrisSpeed,
                    debrisLifetime, _debrisColor, false, 31);
            }
        }

        private void BuildSparks(VfxProfile profile)
        {
            _lifetime = profile != null
                ? Mathf.Max(0.28f, profile.hitParticleLifetime + 0.08f)
                : 0.33f;
            Color color = profile != null
                ? profile.hitParticleColor
                : new Color(1f, 0.72f, 0.18f, 1f);
            int count = profile != null
                ? Mathf.Max(0, profile.hitParticleCount)
                : 16;
            float size = profile != null
                ? Mathf.Max(0.065f, profile.hitParticleSize)
                : 0.09f;
            float speed = profile != null
                ? Mathf.Max(3f, profile.hitParticleSpeed)
                : 4.2f;
            float lifetime = profile != null
                ? Mathf.Max(0.18f, profile.hitParticleLifetime)
                : 0.25f;

            if (count > 0)
            {
                CreateBurst(
                    "AxeSparks", count, size, speed, lifetime,
                    color, true, 36);
            }
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _lifetime > 0f
                ? Mathf.Clamp01(_elapsed / _lifetime)
                : 1f;

            if (_slashRoot != null)
            {
                float reveal = Mathf.Lerp(
                    0.42f, 1.12f, Mathf.SmoothStep(0f, 1f, t));
                _slashRoot.localScale = Vector3.one * reveal * _scale;
                _slashRoot.localRotation = Quaternion.Euler(
                    0f, 0f, Mathf.Lerp(-24f, -8f, t));
                float fade = Mathf.Clamp01((t - 0.2f) / 0.66f);
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, fade);
                SetLineAlpha(_slashOuter, _accentColor, alpha * 0.82f);
                SetLineAlpha(
                    _slashCore,
                    Color.Lerp(Color.white, _edgeColor, 0.35f),
                    alpha);
            }

            if (_shockRoot != null)
            {
                float shockScale = Mathf.Lerp(0.3f, 1.45f, t) * _scale;
                _shockRoot.localScale = Vector3.one * shockScale;
                float fade = Mathf.Clamp01((t - 0.08f) / 0.72f);
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, fade);
                SetLineAlpha(_shockRing, _edgeColor, alpha * 0.72f);
            }

            if (_cracks != null)
            {
                float fade = Mathf.Clamp01((t - 0.42f) / 0.58f);
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, fade);
                for (int i = 0; i < _cracks.Length; i++)
                    SetLineAlpha(_cracks[i], _debrisColor, alpha * 0.8f);
            }

            if (_elapsed >= _lifetime)
                Destroy(gameObject);
        }

        private Transform CreateChild(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            return child.transform;
        }

        private static LineRenderer CreateArc(
            Transform parent, string name, float radius, float width,
            Color color, int sortingOrder)
        {
            LineRenderer line = CreateLine(
                parent, name, width, color, sortingOrder, true);
            const int points = 22;
            line.positionCount = points;
            for (int i = 0; i < points; i++)
            {
                float angle = Mathf.Lerp(-72f, 72f, i / (points - 1f))
                    * Mathf.Deg2Rad;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius * 0.78f,
                    0f));
            }
            return line;
        }

        private static LineRenderer CreateRing(
            Transform parent, string name, float radius, float width,
            Color color, int sortingOrder)
        {
            LineRenderer line = CreateLine(
                parent, name, width, color, sortingOrder, true);
            const int points = 36;
            line.loop = true;
            line.positionCount = points;
            for (int i = 0; i < points; i++)
            {
                float angle = i / (float)points * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius * 0.52f,
                    0f));
            }
            return line;
        }

        private LineRenderer CreateCrack(string name, Vector3[] points)
        {
            LineRenderer line = CreateLine(
                transform, name, 0.035f, _debrisColor, 30, false);
            line.positionCount = points.Length;
            line.SetPositions(points);
            return line;
        }

        private static LineRenderer CreateLine(
            Transform parent, string name, float width, Color color,
            int sortingOrder, bool additive)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.widthMultiplier = width;
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.14f, 1f),
                new Keyframe(0.86f, 1f),
                new Keyframe(1f, 0f));
            line.sharedMaterial = additive
                ? GetAdditiveMaterial()
                : GetAlphaMaterial();
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = sortingOrder;
            return line;
        }

        private void CreateBurst(
            string name, int count, float size, float speed, float lifetime,
            Color color, bool stretched, int sortingOrder)
        {
            Transform child = CreateChild(name);
            var ps = child.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.08f;
            main.maxParticles = Mathf.Max(16, count + 4);
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                lifetime * 0.72f, lifetime * 1.18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                speed * 0.62f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(
                size * 0.65f, size * 1.25f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                -Mathf.PI, Mathf.PI);
            main.startColor = color;
            main.gravityModifier = stretched ? 0.25f : 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = stretched ? 0.08f : 0.14f;

            var colorLifetime = ps.colorOverLifetime;
            colorLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(color, 0.18f),
                    new GradientColorKey(color * 0.55f, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(color.a, 0f),
                    new GradientAlphaKey(color.a, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorLifetime.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = stretched
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.lengthScale = stretched ? 3.2f : 1f;
            renderer.velocityScale = stretched ? 0.16f : 0f;
            renderer.sharedMaterial = stretched
                ? GetAdditiveMaterial()
                : GetAlphaMaterial();
            renderer.sortingOrder = sortingOrder;

            ps.Emit(count);
        }

        private static void SetLineAlpha(
            LineRenderer line, Color color, float alpha)
        {
            if (line == null) return;
            Color visible = new Color(
                color.r, color.g, color.b, Mathf.Clamp01(alpha));
            line.startColor = visible;
            line.endColor = new Color(
                visible.r, visible.g, visible.b, visible.a * 0.35f);
        }

        private static Material GetAdditiveMaterial()
        {
            if (_additiveMaterial != null) return _additiveMaterial;
            _additiveMaterial = CreateMaterial("ProceduralAxeAdditive", true);
            return _additiveMaterial;
        }

        private static Material GetAlphaMaterial()
        {
            if (_alphaMaterial != null) return _alphaMaterial;
            _alphaMaterial = CreateMaterial("ProceduralAxeAlpha", false);
            return _alphaMaterial;
        }

        private static Material CreateMaterial(string name, bool additive)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;

            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
            if (additive)
            {
                material.SetInt("_SrcBlend",
                    (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.One);
            }
            return material;
        }
    }
}
