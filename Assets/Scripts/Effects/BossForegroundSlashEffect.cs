using UnityEngine;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// A texture-free foreground cleave for boss attacks. The stroke is built
    /// in viewport space, then rendered over the battlefield and below HUD UI.
    /// </summary>
    public sealed class BossForegroundSlashEffect : MonoBehaviour
    {
        public const string RootName = "BossForegroundSlashEffect";
        public const int OuterSortingOrder = 90;
        public const int CoreSortingOrder = 91;
        public const int AccentSortingOrder = 92;
        public const int DebrisSortingOrder = 93;
        public const float LifetimeSeconds = 0.42f;

        private const int PathPointCount = 14;
        private const int DebrisCount = 12;
        private const float RevealDuration = 0.085f;

        private static readonly Color InkColor =
            new Color(0.09f, 0.071f, 0.055f, 0.88f);
        private static readonly Color PaperEdgeColor =
            new Color(0.94f, 0.82f, 0.63f, 0.97f);
        private static readonly Color CinnabarColor =
            new Color(0.65f, 0.17f, 0.125f, 0.78f);
        private static readonly Color CrackColor =
            new Color(0.12f, 0.085f, 0.055f, 0.82f);

        private LineRenderer _outerStroke;
        private LineRenderer _paperEdge;
        private LineRenderer _cinnabarTrace;
        private LineRenderer[] _impactCracks;
        private Vector3[] _path;
        private ParticleSystem _paperDebris;
        private Material _alphaMaterial;
        private float _elapsed;
        private int _visiblePointCount;

        public Camera PresentationCamera { get; private set; }
        public ParticleSystem PaperDebris => _paperDebris;
        public int StrokeLayerCount => 3;
        public Bounds VisibleWorldBounds { get; private set; }

        public static GameObject Play(
            Transform parent,
            Camera presentationCamera,
            Vector3 playerAnchor)
        {
            if (parent == null || presentationCamera == null)
                return null;

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, true);
            var effect = root.AddComponent<BossForegroundSlashEffect>();
            effect.Build(presentationCamera, playerAnchor);
            return root;
        }

        private void Build(Camera presentationCamera, Vector3 playerAnchor)
        {
            PresentationCamera = presentationCamera;
            float planeZ = playerAnchor.z;
            float viewHeight = GetVisibleHeight(presentationCamera, planeZ);

            Vector3 start = ViewportToPlane(
                presentationCamera, new Vector2(0.82f, 0.78f), planeZ);
            Vector3 control = ViewportToPlane(
                presentationCamera, new Vector2(0.61f, 0.50f), planeZ);
            Vector3 anchorViewport =
                presentationCamera.WorldToViewportPoint(playerAnchor);
            Vector3 end = ViewportToPlane(
                presentationCamera,
                new Vector2(
                    Mathf.Clamp(anchorViewport.x, 0.46f, 0.54f),
                    Mathf.Clamp(anchorViewport.y, 0.13f, 0.22f)),
                planeZ);
            Vector3 center = (start + end) * 0.5f;
            transform.position = center;

            _path = BuildTornPath(start, control, end, center, viewHeight);
            VisibleWorldBounds = CalculateWorldBounds(_path, center);

            _outerStroke = CreateStroke(
                "InkPaperTear",
                viewHeight * 0.066f,
                InkColor,
                OuterSortingOrder,
                _path);
            _paperEdge = CreateStroke(
                "PaperBladeEdge",
                viewHeight * 0.028f,
                PaperEdgeColor,
                CoreSortingOrder,
                _path);
            _cinnabarTrace = CreateStroke(
                "CinnabarAfterTrace",
                viewHeight * 0.0075f,
                CinnabarColor,
                AccentSortingOrder,
                _path);

            Vector3 direction = (_path[_path.Length - 1] - _path[0]).normalized;
            Vector3 normal = new Vector3(-direction.y, direction.x, 0f);
            _cinnabarTrace.transform.localPosition =
                normal * (viewHeight * 0.004f);

            Vector3 localImpact = _path[_path.Length - 1];
            _impactCracks = BuildImpactCracks(localImpact, viewHeight);
            _paperDebris = CreatePaperDebris(localImpact, viewHeight);

            SetVisiblePointCount(2);
            transform.localScale = new Vector3(0.66f, 0.66f, 1f);
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;

            float reveal = Mathf.Clamp01(_elapsed / RevealDuration);
            int revealCount = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Lerp(2f, PathPointCount, reveal)),
                2,
                PathPointCount);
            SetVisiblePointCount(revealCount);

            float scale;
            if (_elapsed < 0.105f)
            {
                float approach = Mathf.Clamp01(_elapsed / 0.105f);
                scale = Mathf.LerpUnclamped(
                    0.66f,
                    1.065f,
                    EaseOutCubic(approach));
            }
            else
            {
                float settle = Mathf.InverseLerp(
                    0.105f, LifetimeSeconds, _elapsed);
                scale = Mathf.Lerp(
                    1.065f,
                    1f,
                    Mathf.SmoothStep(0f, 1f, settle));
            }
            transform.localScale = new Vector3(scale, scale, 1f);

            float fade = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.12f, LifetimeSeconds, _elapsed));
            SetStrokeAlpha(_outerStroke, InkColor, fade);
            SetStrokeAlpha(_paperEdge, PaperEdgeColor, fade);
            SetStrokeAlpha(_cinnabarTrace, CinnabarColor, fade * 0.9f);

            float crackReveal = Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(0.035f, 0.085f, _elapsed));
            float crackFade = 1f - Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(0.13f, 0.29f, _elapsed));
            float crackAlpha = crackReveal * crackFade;
            for (int i = 0; i < _impactCracks.Length; i++)
                SetStrokeAlpha(_impactCracks[i], CrackColor, crackAlpha);

            if (_elapsed >= LifetimeSeconds)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_alphaMaterial != null)
            {
                Destroy(_alphaMaterial);
                _alphaMaterial = null;
            }
        }

        private static Vector3[] BuildTornPath(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            Vector3 center,
            float viewHeight)
        {
            var path = new Vector3[PathPointCount];
            for (int i = 0; i < PathPointCount; i++)
            {
                float t = i / (PathPointCount - 1f);
                Vector3 a = Vector3.Lerp(start, control, t);
                Vector3 b = Vector3.Lerp(control, end, t);
                Vector3 point = Vector3.Lerp(a, b, t);

                Vector3 tangent = Vector3.Lerp(
                    control - start,
                    end - control,
                    t).normalized;
                Vector3 normal = new Vector3(-tangent.y, tangent.x, 0f);
                float endpointMask = Mathf.Sin(t * Mathf.PI);
                float tornOffset =
                    Mathf.Sin((i + 1) * 2.17f) *
                    viewHeight * 0.0048f * endpointMask;
                path[i] = point - center + normal * tornOffset;
            }
            return path;
        }

        private LineRenderer CreateStroke(
            string objectName,
            float width,
            Color color,
            int sortingOrder,
            Vector3[] points)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 2;
            line.numCapVertices = 1;
            line.widthMultiplier = width;
            line.widthCurve = CreatePerspectiveWidthCurve();
            line.sharedMaterial = GetAlphaMaterial();
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = sortingOrder;
            line.positionCount = points.Length;
            line.SetPositions(points);
            return line;
        }

        private LineRenderer[] BuildImpactCracks(
            Vector3 impact,
            float viewHeight)
        {
            float unit = viewHeight * 0.052f;
            return new[]
            {
                CreateCrack("PaperCrackLeft", impact, new[]
                {
                    new Vector3(-0.12f, -0.08f, 0f),
                    new Vector3(-0.34f, -0.25f, 0f),
                    new Vector3(-0.54f, -0.31f, 0f)
                }, unit),
                CreateCrack("PaperCrackDown", impact, new[]
                {
                    new Vector3(-0.02f, -0.14f, 0f),
                    new Vector3(0.04f, -0.38f, 0f),
                    new Vector3(-0.08f, -0.62f, 0f)
                }, unit),
                CreateCrack("PaperCrackRight", impact, new[]
                {
                    new Vector3(0.13f, -0.08f, 0f),
                    new Vector3(0.39f, -0.19f, 0f),
                    new Vector3(0.58f, -0.12f, 0f)
                }, unit),
                CreateCrack("PaperCrackUpper", impact, new[]
                {
                    new Vector3(0.08f, 0.04f, 0f),
                    new Vector3(0.29f, 0.15f, 0f),
                    new Vector3(0.43f, 0.10f, 0f)
                }, unit)
            };
        }

        private LineRenderer CreateCrack(
            string objectName,
            Vector3 localOrigin,
            Vector3[] offsets,
            float scale)
        {
            var points = new Vector3[offsets.Length + 1];
            points[0] = localOrigin;
            for (int i = 0; i < offsets.Length; i++)
                points[i + 1] = localOrigin + offsets[i] * scale;

            LineRenderer crack = CreateStroke(
                objectName,
                scale * 0.045f,
                CrackColor,
                OuterSortingOrder - 1,
                points);
            crack.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.85f),
                new Keyframe(1f, 0f));
            return crack;
        }

        private ParticleSystem CreatePaperDebris(
            Vector3 impact,
            float viewHeight)
        {
            var child = new GameObject("PaperDebris");
            child.transform.SetParent(transform, false);
            child.transform.localPosition = Vector3.zero;
            var particles = child.AddComponent<ParticleSystem>();
            particles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.05f;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = DebrisCount + 4;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.38f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(
                viewHeight * 0.007f,
                viewHeight * 0.016f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                -Mathf.PI,
                Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.27f, 0.20f, 0.15f, 0.92f),
                new Color(0.88f, 0.76f, 0.57f, 0.9f));
            main.gravityModifier = 0.38f;

            var emission = particles.emission;
            emission.enabled = false;

            var shape = particles.shape;
            shape.enabled = false;

            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-5.5f, 5.5f);

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.92f, 0.62f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = GetAlphaMaterial();
            renderer.sortingOrder = DebrisSortingOrder;

            var particlesToEmit = new ParticleSystem.Particle[DebrisCount];
            for (int i = 0; i < DebrisCount; i++)
            {
                float angle = Mathf.Lerp(-165f, 35f, i / (DebrisCount - 1f))
                    * Mathf.Deg2Rad;
                float speed = Mathf.Lerp(0.8f, 1.9f, (i % 5) / 4f);
                particlesToEmit[i].position = impact;
                particlesToEmit[i].velocity = new Vector3(
                    Mathf.Cos(angle) * speed,
                    Mathf.Sin(angle) * speed + 0.48f,
                    0f);
                particlesToEmit[i].startLifetime = Mathf.Lerp(
                    0.24f,
                    0.38f,
                    (i % 4) / 3f);
                particlesToEmit[i].remainingLifetime =
                    particlesToEmit[i].startLifetime;
                particlesToEmit[i].startSize = Mathf.Lerp(
                    viewHeight * 0.007f,
                    viewHeight * 0.016f,
                    (i % 3) / 2f);
                particlesToEmit[i].rotation = i * 31f;
                particlesToEmit[i].startColor = i % 3 == 0
                    ? new Color32(224, 194, 146, 230)
                    : new Color32(69, 51, 38, 235);
            }
            particles.SetParticles(particlesToEmit, particlesToEmit.Length);
            // SetParticles can populate a stopped system without advancing it.
            // Start simulation so velocity, gravity, rotation and fade animate.
            particles.Play(false);
            return particles;
        }

        private void SetVisiblePointCount(int count)
        {
            if (_visiblePointCount == count) return;
            _visiblePointCount = count;
            ApplyVisiblePath(_outerStroke, count);
            ApplyVisiblePath(_paperEdge, count);
            ApplyVisiblePath(_cinnabarTrace, count);
        }

        private void ApplyVisiblePath(LineRenderer line, int count)
        {
            if (line == null) return;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
                line.SetPosition(i, _path[i]);
        }

        private static void SetStrokeAlpha(
            LineRenderer line,
            Color baseColor,
            float alpha)
        {
            if (line == null) return;
            float visibleAlpha = baseColor.a * Mathf.Clamp01(alpha);
            line.startColor = new Color(
                baseColor.r,
                baseColor.g,
                baseColor.b,
                visibleAlpha * 0.55f);
            line.endColor = new Color(
                baseColor.r,
                baseColor.g,
                baseColor.b,
                visibleAlpha);
        }

        private static AnimationCurve CreatePerspectiveWidthCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.08f, 0.24f),
                new Keyframe(0.50f, 0.62f),
                new Keyframe(0.86f, 1f),
                new Keyframe(1f, 0.06f));
        }

        private static Bounds CalculateWorldBounds(
            Vector3[] localPoints,
            Vector3 center)
        {
            var bounds = new Bounds(center + localPoints[0], Vector3.zero);
            for (int i = 1; i < localPoints.Length; i++)
                bounds.Encapsulate(center + localPoints[i]);
            return bounds;
        }

        private static Vector3 ViewportToPlane(
            Camera camera,
            Vector2 viewport,
            float planeZ)
        {
            float distance = Vector3.Dot(
                new Vector3(0f, 0f, planeZ) - camera.transform.position,
                camera.transform.forward);
            distance = Mathf.Max(0.01f, Mathf.Abs(distance));
            Vector3 world = camera.ViewportToWorldPoint(
                new Vector3(viewport.x, viewport.y, distance));
            world.z = planeZ;
            return world;
        }

        private static float GetVisibleHeight(Camera camera, float planeZ)
        {
            if (camera.orthographic)
                return camera.orthographicSize * 2f;

            float distance = Mathf.Abs(
                planeZ - camera.transform.position.z);
            return 2f * distance *
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        private static float EaseOutCubic(float value)
        {
            float inverse = 1f - Mathf.Clamp01(value);
            return 1f - inverse * inverse * inverse;
        }

        private Material GetAlphaMaterial()
        {
            if (_alphaMaterial != null) return _alphaMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;

            _alphaMaterial = new Material(shader)
            {
                name = "BossForegroundSlashAlpha",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
            return _alphaMaterial;
        }
    }
}
