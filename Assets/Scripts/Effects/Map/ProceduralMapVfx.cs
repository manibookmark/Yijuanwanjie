using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WanjieScroll.Effects.Map
{
    public enum MapVfxKind
    {
        ManaPearl,
        Seal,
        HealthPearl
    }

    public enum MapVfxState
    {
        Hidden,
        Locked,
        Available,
        Hovered,
        Completing,
        Completed
    }

    /// <summary>
    /// Owns the complete visual lifecycle of one map interaction effect.
    /// Callers provide only semantic state; particle topology, procedural
    /// textures and seal geometry remain private so they can change without
    /// affecting map rules, input handling or save data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralMapVfx : MonoBehaviour
    {
        private static readonly Color SealSoul =
            new Color(0.94f, 0.72f, 0.28f, 0.94f);
        private static readonly Color SealBinding =
            new Color(0.58f, 0.08f, 0.055f, 0.92f);
        private static readonly Color SealAnchor =
            new Color(0.20f, 0.055f, 0.045f, 0.96f);
        private static readonly Color SealInk =
            new Color(0.10f, 0.045f, 0.035f, 0.82f);

        [SerializeField] private MapVfxKind kind = MapVfxKind.ManaPearl;
        [SerializeField] private MapVfxState state = MapVfxState.Locked;
        [SerializeField] private int baseSortingOrder = 0;

        private readonly List<ParticleSystem> _loopingParticles =
            new List<ParticleSystem>();
        private readonly List<ParticleSystem> _sealAnchors =
            new List<ParticleSystem>();
        private readonly List<ParticleSystem> _sealBindings =
            new List<ParticleSystem>();
        private readonly List<ParticleSystem> _sealAnchorBursts =
            new List<ParticleSystem>();
        private readonly List<Vector3> _sealAnchorPositions =
            new List<Vector3>();

        private Transform _visualRoot;
        private ManaSpiritInfinityEffect _spiritPearlEffect;
        private Transform _sealSoulRoot;
        private ParticleSystem _sealSoulGlow;
        private ParticleSystem _sealSoulRise;
        private ParticleSystem _sealPressureDust;
        private ParticleSystem _sealSoulRelease;
        private ParticleSystem _completionDust;
        private Coroutine _completionRoutine;
        private bool _built;
        private float _stateEnteredAt;

        private static Material _softAlphaMaterial;
        private static Material _softAdditiveMaterial;
        private static Material _paperAlphaMaterial;

        public MapVfxKind Kind => kind;
        public MapVfxState State => state;
        public bool IsBuilt => _built;
        private bool UsesSpiritPearl =>
            kind == MapVfxKind.ManaPearl ||
            kind == MapVfxKind.HealthPearl;

        private void Start()
        {
            if (!_built)
                Initialize(kind, state);
        }

        private void Update()
        {
            if (!_built || _visualRoot == null ||
                state == MapVfxState.Hidden ||
                state == MapVfxState.Locked ||
                state == MapVfxState.Completed ||
                state == MapVfxState.Completing)
                return;

            if (kind == MapVfxKind.Seal)
                AnimateSealConstraint(Time.unscaledTime - _stateEnteredAt);
        }

        /// <summary>
        /// Builds or reuses the requested effect and applies an initial state.
        /// This is the only setup API needed by map presentation code.
        /// </summary>
        public void Initialize(
            MapVfxKind visualKind,
            MapVfxState initialState = MapVfxState.Locked,
            int sortingOrderBase = 0)
        {
            if (_built && kind != visualKind)
                ClearVisual();

            kind = visualKind;
            baseSortingOrder = sortingOrderBase;
            EnsureBuilt();
            SetState(initialState, true);
        }

        /// <summary>
        /// Applies a semantic interaction state. The effect decides how that
        /// state changes emission, color, scale and structural visibility.
        /// </summary>
        public void SetState(MapVfxState nextState, bool immediate = false)
        {
            EnsureBuilt();
            if (UsesSpiritPearl)
            {
                state = nextState;
                _stateEnteredAt = Time.unscaledTime;
                _spiritPearlEffect?.SetState(nextState, immediate);
                return;
            }

            if (_completionRoutine != null)
            {
                StopCoroutine(_completionRoutine);
                _completionRoutine = null;
            }

            state = nextState;
            _stateEnteredAt = Time.unscaledTime;
            ResetVisualTransform();
            ApplyState(immediate);
        }

        /// <summary>
        /// Plays the kind-specific terminal transition: mana is collected and
        /// flies upward, while an intact seal breaks into paper and ink debris.
        /// </summary>
        public void PlayCompletion()
        {
            EnsureBuilt();
            if (state == MapVfxState.Completing ||
                state == MapVfxState.Completed)
                return;

            if (UsesSpiritPearl)
            {
                state = MapVfxState.Completing;
                if (_spiritPearlEffect == null ||
                    !_spiritPearlEffect.PlayCollection(
                        () => state = MapVfxState.Completed))
                {
                    state = MapVfxState.Completed;
                }
                return;
            }

            if (_completionRoutine != null)
                StopCoroutine(_completionRoutine);
            _completionRoutine = StartCoroutine(PlaySealBreak());
        }

        /// <summary>
        /// Plays the reward-specific gather-and-settle entrance while reusing
        /// the exact same Infinity field as the production map mana node.
        /// </summary>
        public void PlayRewardArrival()
        {
            EnsureBuilt();
            if (!UsesSpiritPearl ||
                _spiritPearlEffect == null)
                return;
            state = MapVfxState.Available;
            _spiritPearlEffect.PlayRewardArrival();
        }

        private void EnsureBuilt()
        {
            if (_built) return;

            var root = new GameObject("VisualRoot");
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;

            if (UsesSpiritPearl)
                BuildSpiritPearl();
            else
                BuildSeal();

            _built = true;
        }

        private void BuildSpiritPearl()
        {
            bool health = kind == MapVfxKind.HealthPearl;
            var pearlObject = new GameObject(
                health ? "HealthSpiritInfinity" : "ManaSpiritInfinity");
            pearlObject.transform.SetParent(_visualRoot, false);
            _spiritPearlEffect =
                pearlObject.AddComponent<ManaSpiritInfinityEffect>();
            _spiritPearlEffect.Initialize(
                baseSortingOrder,
                health
                    ? SpiritPearlPalette.Health
                    : SpiritPearlPalette.Mana);
        }

        private void BuildSeal()
        {
            var soulObject = new GameObject("SealedSoulRoot");
            soulObject.transform.SetParent(_visualRoot, false);
            _sealSoulRoot = soulObject.transform;

            _sealSoulGlow = CreateLoop(
                "SealedSoulGlow", GetSoftAdditiveMaterial(), SealSoul,
                7f, 1.05f, 0.46f, 0f, 0f, 28, baseSortingOrder + 9, false);
            _sealSoulGlow.transform.SetParent(_sealSoulRoot, false);
            EnableBreathingSize(_sealSoulGlow, 0.66f, 1.08f, 0.58f);

            _sealSoulRise = CreateLoop(
                "SealedSoulRise", GetPaperAlphaMaterial(), SealSoul,
                15f, 0.92f, 0.085f, 0.15f, 0.16f, 42, baseSortingOrder + 10, true);
            _sealSoulRise.transform.SetParent(_sealSoulRoot, false);
            _sealSoulRise.transform.localScale = new Vector3(0.72f, 1.5f, 1f);
            SetVelocityRanges(
                _sealSoulRise,
                new Vector2(-0.04f, 0.04f),
                new Vector2(0.30f, 0.48f),
                Vector2.zero);

            Vector3[] anchorPositions =
            {
                new Vector3(-0.72f, 0.83f, 0f),
                new Vector3(0.66f, 0.96f, 0f),
                new Vector3(0.80f, -0.76f, 0f),
                new Vector3(-0.63f, -0.92f, 0f)
            };

            for (int i = 0; i < anchorPositions.Length; i++)
                CreateSealConstraint(i, anchorPositions[i]);

            _sealPressureDust = CreateLoop(
                "SealPressureDust", GetSoftAlphaMaterial(), SealInk,
                7f, 1.4f, 0.09f, 0.92f, -0.38f, 30, baseSortingOrder + 3, true);
            _sealPressureDust.transform.localScale =
                new Vector3(1f, 1.18f, 1f);

            _completionDust = CreateBurst(
                "SealBindingRelease", GetSoftAlphaMaterial(), SealInk,
                0.22f, 1.55f, 0.95f, baseSortingOrder + 12, true);
            _sealSoulRelease = CreateBurst(
                "SealedSoulRelease", GetSoftAdditiveMaterial(), SealSoul,
                0.16f, 1.15f, 0.82f, baseSortingOrder + 16, true, 0.18f);
        }

        private ParticleSystem CreateLoop(
            string name,
            Material material,
            Color color,
            float rate,
            float lifetime,
            float size,
            float radius,
            float orbitalSpeed,
            int maxParticles,
            int sortingOrder,
            bool useShape)
        {
            GameObject child = new GameObject(name, typeof(ParticleSystem));
            child.transform.SetParent(_visualRoot, false);
            ParticleSystem ps = child.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(
                -Mathf.PI, Mathf.PI);
            main.startColor = color;
            main.maxParticles = maxParticles;
            ps.randomSeed = StableSeed(name);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = useShape;
            if (useShape)
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = radius;
                shape.radiusThickness = 1f;
            }

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = useShape;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(orbitalSpeed);

            var colorLife = ps.colorOverLifetime;
            colorLife.enabled = true;
            colorLife.color = CreateFadeGradient(color);

            ParticleSystemRenderer renderer =
                ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortingOrder = sortingOrder;

            _loopingParticles.Add(ps);
            ps.Play();
            ps.Emit(Mathf.Clamp(Mathf.CeilToInt(rate * lifetime * 0.5f), 1, 12));
            return ps;
        }

        private ParticleSystem CreateBurst(
            string name,
            Material material,
            Color color,
            float size,
            float speed,
            float lifetime,
            int sortingOrder,
            bool soft,
            float shapeRadius = -1f)
        {
            GameObject child = new GameObject(name, typeof(ParticleSystem));
            child.transform.SetParent(_visualRoot, false);
            ParticleSystem ps = child.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                lifetime * 0.72f, lifetime * 1.18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                speed * 0.55f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(
                size * 0.6f, size * 1.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                -Mathf.PI, Mathf.PI);
            main.startColor = color;
            main.gravityModifier = soft ? -0.03f : 0.26f;
            main.maxParticles = 80;
            ps.randomSeed = StableSeed(name);

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = shapeRadius > 0f
                ? shapeRadius
                : kind == MapVfxKind.Seal ? 0.82f : 0.24f;

            var colorLife = ps.colorOverLifetime;
            colorLife.enabled = true;
            colorLife.color = CreateFadeGradient(color);

            var sizeLife = ps.sizeOverLifetime;
            sizeLife.enabled = true;
            sizeLife.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.35f),
                    new Keyframe(0.18f, 1f),
                    new Keyframe(1f, soft ? 1.35f : 0.28f)));

            ParticleSystemRenderer renderer =
                ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = soft
                ? ParticleSystemRenderMode.Billboard
                : ParticleSystemRenderMode.Stretch;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.lengthScale = soft ? 1f : 2.2f;
            renderer.velocityScale = soft ? 0f : 0.2f;
            renderer.sortingOrder = sortingOrder;
            return ps;
        }

        private void CreateSealConstraint(int index, Vector3 anchorPosition)
        {
            _sealAnchorPositions.Add(anchorPosition);

            ParticleSystem anchor = CreateLoop(
                $"SealAnchor{index + 1}", GetPaperAlphaMaterial(), SealAnchor,
                10f, 1.15f, 0.15f, 0.11f,
                index % 2 == 0 ? 0.82f : -0.82f,
                26, baseSortingOrder + 14, true);
            anchor.transform.localPosition = anchorPosition;
            anchor.transform.localScale = new Vector3(1.2f, 0.72f, 1f);
            _sealAnchors.Add(anchor);

            const float travelTime = 0.78f;
            ParticleSystem binding = CreateLoop(
                $"SealBindingStream{index + 1}",
                GetSoftAdditiveMaterial(), SealBinding,
                22f, travelTime, 0.075f, 0f, 0f, 36, baseSortingOrder + 11, false);
            binding.transform.localPosition = anchorPosition;
            SetVelocityConstants(
                binding,
                -anchorPosition.x / travelTime,
                -anchorPosition.y / travelTime,
                0f);
            EnableParticleTrails(binding, 0.17f);
            binding.Clear(true);
            binding.Play();
            binding.Emit(8);
            _sealBindings.Add(binding);

            ParticleSystem burst = CreateBurst(
                $"SealAnchorBreak{index + 1}",
                GetPaperAlphaMaterial(), SealBinding,
                0.15f, 2.1f, 0.62f, baseSortingOrder + 15, false, 0.10f);
            burst.transform.localPosition = anchorPosition;
            _sealAnchorBursts.Add(burst);
        }

        private void AnimateSealConstraint(float elapsed)
        {
            float hoverScale = state == MapVfxState.Hovered ? 1.08f : 1f;
            _visualRoot.localScale = Vector3.one * hoverScale;

            float clampSpeed = state == MapVfxState.Hovered
                ? 3.1f
                : state == MapVfxState.Available ? 2.35f : 1.55f;
            float wave = Mathf.Max(0f, Mathf.Sin(elapsed * clampSpeed));
            float clamp = wave * wave * wave * wave;
            float travel = clamp * (state == MapVfxState.Locked ? 0.035f : 0.075f);

            if (_sealSoulRoot != null)
            {
                _sealSoulRoot.localPosition = Vector3.down * clamp * 0.055f;
                _sealSoulRoot.localScale = new Vector3(
                    1f - clamp * 0.11f,
                    1f - clamp * 0.20f,
                    1f);
            }

            for (int i = 0; i < _sealAnchorPositions.Count; i++)
            {
                Vector3 position = _sealAnchorPositions[i] * (1f - travel);
                if (i < _sealAnchors.Count && _sealAnchors[i] != null)
                    _sealAnchors[i].transform.localPosition = position;
                if (i < _sealBindings.Count && _sealBindings[i] != null)
                    _sealBindings[i].transform.localPosition = position;
            }
        }

        private static void SetVelocityConstants(
            ParticleSystem ps, float x, float y, float z)
        {
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // Tuanjie requires all three curves in one mode.
            velocity.x = new ParticleSystem.MinMaxCurve(x);
            velocity.y = new ParticleSystem.MinMaxCurve(y);
            velocity.z = new ParticleSystem.MinMaxCurve(z);
        }

        private static void SetVelocityRanges(
            ParticleSystem ps, Vector2 x, Vector2 y, Vector2 z)
        {
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            // Even a zero-width range uses TwoConstants so modes stay equal.
            velocity.x = new ParticleSystem.MinMaxCurve(x.x, x.y);
            velocity.y = new ParticleSystem.MinMaxCurve(y.x, y.y);
            velocity.z = new ParticleSystem.MinMaxCurve(z.x, z.y);
        }

        private static void EnableParticleTrails(
            ParticleSystem ps, float lifetime)
        {
            var trails = ps.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.lifetime = lifetime;
            trails.minVertexDistance = 0.06f;
            trails.dieWithParticles = true;
            trails.sizeAffectsWidth = true;
            trails.inheritParticleColor = true;

            ParticleSystemRenderer renderer =
                ps.GetComponent<ParticleSystemRenderer>();
            renderer.trailMaterial = GetSoftAdditiveMaterial();
        }

        private void ApplyState(bool immediate)
        {
            bool visible = state != MapVfxState.Hidden &&
                           state != MapVfxState.Completed;
            _visualRoot.gameObject.SetActive(visible);
            if (!visible) return;

            if (UsesSpiritPearl)
                _spiritPearlEffect?.SetState(state, immediate);
            else
                ApplySealState();

            if (immediate)
                SimulateWarmStart();
        }

        private void ApplySealState()
        {
            float soulRate;
            float riseRate;
            float anchorRate;
            float bindingRate;
            float dustRate;
            float alpha;
            switch (state)
            {
                case MapVfxState.Locked:
                    soulRate = 3f;
                    riseRate = 5f;
                    anchorRate = 4f;
                    bindingRate = 9f;
                    dustRate = 2f;
                    alpha = 0.42f;
                    break;
                case MapVfxState.Hovered:
                    soulRate = 11f;
                    riseRate = 24f;
                    anchorRate = 16f;
                    bindingRate = 34f;
                    dustRate = 11f;
                    alpha = 1f;
                    break;
                default:
                    soulRate = 7f;
                    riseRate = 15f;
                    anchorRate = 10f;
                    bindingRate = 22f;
                    dustRate = 7f;
                    alpha = 0.82f;
                    break;
            }

            ConfigureLoop(
                _sealSoulGlow, WithAlpha(SealSoul, alpha), soulRate);
            ConfigureLoop(
                _sealSoulRise, WithAlpha(SealSoul, alpha * 0.92f), riseRate);
            ConfigureLoop(
                _sealPressureDust, WithAlpha(SealInk, alpha * 0.72f), dustRate);

            foreach (ParticleSystem anchor in _sealAnchors)
                ConfigureLoop(
                    anchor, WithAlpha(SealAnchor, alpha), anchorRate);
            foreach (ParticleSystem binding in _sealBindings)
                ConfigureLoop(
                    binding, WithAlpha(SealBinding, alpha), bindingRate);
        }

        private IEnumerator PlaySealBreak()
        {
            state = MapVfxState.Completing;
            int[] breakOrder = { 0, 2, 1, 3 };
            foreach (int index in breakOrder)
            {
                StopEmission(_sealAnchors[index]);
                StopEmission(_sealBindings[index]);
                _sealAnchorBursts[index].transform.localPosition =
                    _sealAnchors[index].transform.localPosition;
                _sealAnchorBursts[index].Emit(12);
                yield return new WaitForSecondsRealtime(0.09f);
            }

            StopEmission(_sealPressureDust);
            _completionDust.Emit(30);
            _sealSoulRelease.Emit(28);

            const float duration = 0.78f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                _sealSoulRoot.localPosition =
                    Vector3.up * Mathf.Lerp(0f, 1.28f, eased);
                _sealSoulRoot.localScale = Vector3.one *
                    Mathf.Lerp(1f, 1.38f, eased);
                yield return null;
            }

            StopLoopEmission();
            yield return new WaitForSecondsRealtime(0.32f);
            state = MapVfxState.Completed;
            _visualRoot.gameObject.SetActive(false);
            _completionRoutine = null;
        }

        private void ConfigureLoop(
            ParticleSystem ps, Color color, float emissionRate)
        {
            if (ps == null) return;
            var main = ps.main;
            main.startColor = color;
            var emission = ps.emission;
            emission.enabled = emissionRate > 0f;
            emission.rateOverTime = emissionRate;
            if (emissionRate > 0f && !ps.isPlaying)
                ps.Play();
        }

        private void SimulateWarmStart()
        {
            if (UsesSpiritPearl)
                return;
            foreach (ParticleSystem ps in _loopingParticles)
            {
                if (ps == null || !ps.gameObject.activeInHierarchy) continue;
                ps.Simulate(0.35f, true, true, true);
                ps.Play();
            }
        }

        private void StopLoopEmission()
        {
            foreach (ParticleSystem ps in _loopingParticles)
                StopEmission(ps);
        }

        private static void StopEmission(ParticleSystem ps)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.enabled = false;
        }

        private void ResetVisualTransform()
        {
            if (_visualRoot == null) return;
            _visualRoot.localPosition = Vector3.zero;
            _visualRoot.localRotation = Quaternion.identity;
            _visualRoot.localScale = Vector3.one;
            if (_sealSoulRoot != null)
            {
                _sealSoulRoot.localPosition = Vector3.zero;
                _sealSoulRoot.localRotation = Quaternion.identity;
                _sealSoulRoot.localScale = Vector3.one;
            }

            for (int i = 0; i < _sealAnchorPositions.Count; i++)
            {
                if (i < _sealAnchors.Count && _sealAnchors[i] != null)
                    _sealAnchors[i].transform.localPosition =
                        _sealAnchorPositions[i];
                if (i < _sealBindings.Count && _sealBindings[i] != null)
                    _sealBindings[i].transform.localPosition =
                        _sealAnchorPositions[i];
            }

            ClearTransient(_completionDust);
            ClearTransient(_sealSoulRelease);
            foreach (ParticleSystem burst in _sealAnchorBursts)
                ClearTransient(burst);
        }

        private static void ClearTransient(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void ClearVisual()
        {
            if (_completionRoutine != null)
                StopCoroutine(_completionRoutine);
            if (_visualRoot != null)
            {
                if (Application.isPlaying)
                    Destroy(_visualRoot.gameObject);
                else
                    DestroyImmediate(_visualRoot.gameObject);
            }
            _loopingParticles.Clear();
            _sealAnchors.Clear();
            _sealBindings.Clear();
            _sealAnchorBursts.Clear();
            _sealAnchorPositions.Clear();
            _visualRoot = null;
            _spiritPearlEffect = null;
            _sealSoulRoot = null;
            _built = false;
        }

        private static void EnableBreathingSize(
            ParticleSystem ps, float minimum, float maximum, float midpoint)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, minimum),
                    new Keyframe(midpoint, maximum),
                    new Keyframe(1f, minimum)));
        }

        private static ParticleSystem.MinMaxGradient CreateFadeGradient(
            Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(color.a, 0.18f),
                    new GradientAlphaKey(color.a * 0.7f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static uint StableSeed(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in value)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return hash;
            }
        }

        private static Material GetSoftAlphaMaterial()
        {
            if (_softAlphaMaterial == null)
                _softAlphaMaterial = CreateMaterial(
                    "MapVfxSoftAlpha", CreateSoftCircleTexture(48), false);
            return _softAlphaMaterial;
        }

        private static Material GetSoftAdditiveMaterial()
        {
            if (_softAdditiveMaterial == null)
                _softAdditiveMaterial = CreateMaterial(
                    "MapVfxSoftAdditive", CreateSoftCircleTexture(48), true);
            return _softAdditiveMaterial;
        }

        private static Material GetPaperAlphaMaterial()
        {
            if (_paperAlphaMaterial == null)
                _paperAlphaMaterial = CreateMaterial(
                    "MapVfxPaperAlpha", CreatePaperShardTexture(48), false);
            return _paperAlphaMaterial;
        }

        private static Material CreateMaterial(
            string name, Texture texture, bool additive)
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
                mainTexture = texture
            };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt(
                "_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
            return material;
        }

        private static Texture2D CreateSoftCircleTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MapVfxSoftCircle",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float center = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - center) / center;
                    float dy = (y + 0.5f - center) / center;
                    float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            return texture;
        }

        private static Texture2D CreatePaperShardTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MapVfxPaperShard",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float center = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f - center) / center);
                    float ny = Mathf.Abs((y + 0.5f - center) / center);
                    float edge = 1f - (nx * 0.72f + ny * 1.12f);
                    float alpha = Mathf.SmoothStep(0f, 1f, edge * 8f);
                    float fiber = 0.88f + Mathf.Sin((x * 13f + y * 7f) * 0.17f) * 0.06f;
                    texture.SetPixel(x, y, new Color(fiber, fiber, fiber, alpha));
                }
            }
            texture.Apply();
            return texture;
        }
    }
}
