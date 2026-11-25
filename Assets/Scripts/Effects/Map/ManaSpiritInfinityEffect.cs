using System;
using System.Collections;
using System.Collections.Generic;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;
using WanjieScroll.Core;
using WanjieScroll.Effects;

namespace WanjieScroll.Effects.Map
{
    public enum SpiritPearlPalette
    {
        Mana,
        Health
    }

    /// <summary>
    /// Owns the Infinity particle field, quiet core and transition timing for
    /// spirit-mana. Map nodes and reward reveals send semantic states only;
    /// resource paths, emission density and motion remain private here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ManaSpiritInfinityEffect : MonoBehaviour
    {
        public const int CurrentVisualLanguageVersion =
            ManaSpiritFlowLanguage.CurrentVersion;

        private const string PrefabResourcePath =
            "Effects/Map/ManaSpiritInfinityField";
        private const string FallbackPrefabResourcePath =
            "Effects/Map/StandardSpiritAura";

        private static readonly Color ManaCoreColor = WithAlpha(
            Color.Lerp(SpiritEnergyColorPalette.ManaFull,
                SpiritEnergyColorPalette.ManaHighlight, 0.45f),
            0.98f);
        private static readonly Color ManaHaloColor = WithAlpha(
            SpiritEnergyColorPalette.ManaHighlight,
            0.18f);
        private static readonly Color HealthCoreColor =
            new Color(0.82f, 0.19f, 0.16f, 0.98f);
        private static readonly Color HealthHaloColor =
            new Color(1f, 0.48f, 0.36f, 0.10f);
        private static readonly Color HealthParticleDeep =
            new Color(0.24f, 0.018f, 0.012f, 1f);
        private static readonly Color HealthParticleCore =
            new Color(0.94f, 0.20f, 0.14f, 1f);
        private static readonly Color HealthParticleHighlight =
            new Color(1f, 0.74f, 0.62f, 1f);

        [SerializeField] private int visualLanguageVersion =
            CurrentVisualLanguageVersion;

        private readonly List<InfinityParticleSystem> _systems =
            new List<InfinityParticleSystem>(4);
        private readonly List<float> _authoredEmissionRates =
            new List<float>(4);

        private GameObject _fieldRoot;
        private SpriteRenderer _core;
        private SpriteRenderer _halo;
        private Material _runtimeSpiritMaterial;
        private Coroutine _transition;
        private Coroutine _runtimeActivation;
        private MapVfxState _state = MapVfxState.Hidden;
        private Action _completionCallback;
        private int _sortingOrder;
        private float _motionSeed;
        private bool _initialized;
        private bool _runtimeReady;
        private bool _rewardArrivalPending;
        private bool _collectionPending;
        private bool _warmStartPending;
        private SpiritPearlPalette _palette = SpiritPearlPalette.Mana;

        public int VisualLanguageVersion => visualLanguageVersion;
        public MapVfxState State => _state;
        public int ParticleSystemCount => _systems.Count;

        public bool Initialize(
            int sortingOrder,
            SpiritPearlPalette palette = SpiritPearlPalette.Mana)
        {
            _sortingOrder = sortingOrder;
            if (!_initialized)
            {
                _palette = palette;
                if (!BuildParticleField())
                    return false;
                BuildCore();
                _motionSeed = Mathf.Repeat(
                    transform.position.x * 0.73f +
                    transform.position.y * 0.41f + 0.37f,
                    7.31f);
                _initialized = true;
            }

            if (_runtimeReady)
                ApplySorting();
            else
                ScheduleRuntimeActivation();
            return true;
        }

        public void SetState(MapVfxState state, bool immediate = false)
        {
            if (!_initialized && !Initialize(_sortingOrder))
                return;

            StopTransition();
            _rewardArrivalPending = false;
            _collectionPending = false;
            _warmStartPending = immediate;
            _state = state;
            ResetLocalMotion();
            switch (state)
            {
                case MapVfxState.Hidden:
                case MapVfxState.Completed:
                    SetCoreVisible(false, 0f);
                    StopAndClearField();
                    break;
                case MapVfxState.Locked:
                    StopAndClearField();
                    SetCoreVisible(true, 0.24f);
                    break;
                case MapVfxState.Hovered:
                    SetCoreVisible(true, 1f);
                    PlayField(1.55f, immediate);
                    break;
                default:
                    SetCoreVisible(true, 0.82f);
                    PlayField(1f, immediate);
                    break;
            }
            if (_runtimeReady)
                _warmStartPending = false;
        }

        /// <summary>
        /// Reward-only entrance: loose qi gathers once, then settles into the
        /// same breathing field used by the map node.
        /// </summary>
        public void PlayRewardArrival()
        {
            if (!_initialized && !Initialize(_sortingOrder))
                return;

            StopTransition();
            _state = MapVfxState.Available;
            _collectionPending = false;
            _rewardArrivalPending = true;
            _warmStartPending = false;
            ResetLocalMotion();
            SetCoreVisible(true, 0.35f);
            if (_runtimeReady)
                BeginRewardArrival();
            else
                ScheduleRuntimeActivation();
        }

        /// <summary>
        /// Map collection: the field condenses before lifting away. Completion
        /// is reported only after all Infinity systems have been safely hidden.
        /// </summary>
        public bool PlayCollection(Action onComplete)
        {
            if (!_initialized && !Initialize(_sortingOrder))
                return false;
            if (_state == MapVfxState.Completing ||
                _state == MapVfxState.Completed)
                return false;

            StopTransition();
            _state = MapVfxState.Completing;
            _completionCallback = onComplete;
            _rewardArrivalPending = false;
            _collectionPending = true;
            _warmStartPending = false;
            if (_runtimeReady)
                BeginCollection();
            else
                ScheduleRuntimeActivation();
            return true;
        }

        private void Update()
        {
            if (!_initialized || _transition != null ||
                (_state != MapVfxState.Available &&
                 _state != MapVfxState.Hovered))
                return;

            float time = Time.unscaledTime;
            float breath = Mathf.PerlinNoise(
                _motionSeed, time * 0.21f);
            float drift = Mathf.PerlinNoise(
                _motionSeed + 3.17f, time * 0.13f);
            float hoverScale = _state == MapVfxState.Hovered ? 1.08f : 1f;
            transform.localScale = Vector3.one *
                Mathf.Lerp(0.965f, 1.045f, breath) * hoverScale;
            transform.localRotation = Quaternion.Euler(
                0f, 0f, Mathf.Lerp(-2.2f, 2.2f, drift));
        }

        private IEnumerator RewardArrivalRoutine()
        {
            ResetLocalMotion();
            transform.localScale = Vector3.one * 1.08f;
            SetCoreVisible(true, 0.45f);
            PlayField(1.45f, false);

            const float gatherDuration = 0.42f;
            for (float elapsed = 0f; elapsed < gatherDuration;
                 elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / gatherDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                transform.localScale = Vector3.one * Mathf.Lerp(
                    1.08f, 0.96f, eased);
                SetCoreAlpha(Mathf.Lerp(0.45f, 1f, eased));
                yield return null;
            }

            const float settleDuration = 0.32f;
            for (float elapsed = 0f; elapsed < settleDuration;
                 elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / settleDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                transform.localScale = Vector3.one * Mathf.Lerp(
                    0.96f, 1f, eased);
                yield return null;
            }

            PlayField(1f, false);
            ResetLocalMotion();
            _transition = null;
        }

        private IEnumerator CollectionRoutine()
        {
            bool mana = _palette == SpiritPearlPalette.Mana;
            PlayField(mana ? 1.65f : 2.65f, false);
            Vector3 startPosition = transform.localPosition;

            float condenseDuration = mana ? 0.16f : 0.20f;
            for (float elapsed = 0f; elapsed < condenseDuration;
                 elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / condenseDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                transform.localScale = Vector3.one * Mathf.Lerp(
                    1f, 0.54f, eased);
                SetCoreAlpha(Mathf.Lerp(0.82f, 1f, eased));
                yield return null;
            }

            Vector3 destination = startPosition +
                new Vector3(-1.35f, 2.45f, 0f);
            float liftDuration = mana ? 0.42f : 0.58f;
            for (float elapsed = 0f; elapsed < liftDuration;
                 elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / liftDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                Vector3 arc = Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.36f;
                transform.localPosition =
                    Vector3.Lerp(startPosition, destination, eased) + arc;
                transform.localScale = Vector3.one * Mathf.Lerp(
                    0.54f, 0.08f, eased);
                SetCoreAlpha(1f - eased);
                yield return null;
            }

            StopAndClearField();
            SetCoreVisible(false, 0f);
            _state = MapVfxState.Completed;
            _transition = null;
            InvokeCompletionCallback();
        }

        private bool BuildParticleField()
        {
            GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
            bool usingFallback = false;
            if (prefab == null)
            {
                prefab = Resources.Load<GameObject>(
                    FallbackPrefabResourcePath);
                usingFallback = true;
            }
            if (prefab == null)
            {
                Debug.LogError(
                    $"找不到灵力 Infinity 资源 {PrefabResourcePath}。", this);
                return false;
            }

            _fieldRoot = Instantiate(prefab, transform, false);
            _fieldRoot.name = usingFallback
                ? "ManaSpiritInfinityFallback"
                : "ManaSpiritInfinityField";

            MapNodeAvailabilityHalo fallbackHalo =
                _fieldRoot.GetComponent<MapNodeAvailabilityHalo>();
            if (fallbackHalo != null)
            {
                if (Application.isPlaying)
                    Destroy(fallbackHalo);
                else
                    DestroyImmediate(fallbackHalo);
            }

            _systems.Clear();
            _systems.AddRange(
                _fieldRoot.GetComponentsInChildren<
                    InfinityParticleSystem>(true));
            if (_systems.Count == 0)
            {
                Destroy(_fieldRoot);
                _fieldRoot = null;
                Debug.LogError("灵力资源缺少 InfinityParticleSystem。", this);
                return false;
            }

            _authoredEmissionRates.Clear();
            for (int index = 0; index < _systems.Count; index++)
            {
                InfinityParticleSystem system = _systems[index];
                _authoredEmissionRates.Add(ResolveAuthoredRate(system.name));
            }

            return true;
        }

        private void BuildCore()
        {
            bool mana = _palette == SpiritPearlPalette.Mana;
            _halo = CreateCoreLayer(
                "ManaQuietHalo", ResolveHaloColor(), mana ? 1.24f : 1.00f,
                _sortingOrder - 1);
            _core = CreateCoreLayer(
                "ManaQuietCore", ResolveCoreColor(), mana ? 0.62f : 0.76f,
                _sortingOrder + 4);
            if (mana)
            {
                _halo.sprite = ManaSpiritCoreSprites.SoftHalo;
                _core.sprite = ManaSpiritCoreSprites.LuminousManaCore;
            }
        }

        private SpriteRenderer CreateCoreLayer(
            string name,
            Color color,
            float diameter,
            int sortingOrder)
        {
            var layer = new GameObject(name, typeof(SpriteRenderer));
            layer.transform.SetParent(transform, false);
            SpriteRenderer renderer = layer.GetComponent<SpriteRenderer>();
            renderer.sprite = ManaSpiritCoreSprites.SoftOrb;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Vector2 spriteSize = renderer.sprite.bounds.size;
            float spriteDiameter = Mathf.Max(spriteSize.x, spriteSize.y);
            float scale = diameter / Mathf.Max(0.001f, spriteDiameter);
            renderer.transform.localScale = new Vector3(scale, scale, 1f);
            return renderer;
        }

        private void ApplySorting()
        {
            if (_halo != null)
                _halo.sortingOrder = _sortingOrder - 1;
            if (_core != null)
                _core.sortingOrder = _sortingOrder + 4;

            if (!_runtimeReady)
                return;

            for (int index = 0; index < _systems.Count; index++)
            {
                InfinityParticleSystem system = _systems[index];
                if (system == null)
                    continue;
                int order = _sortingOrder + index;
                var renderer = system.renderer;
                renderer.sortingOrder = order;
                renderer.customBatchKey = order;
            }
        }

        private void PlayField(float emissionMultiplier, bool warmStart)
        {
            if (!_runtimeReady || _fieldRoot == null ||
                !_fieldRoot.activeInHierarchy)
                return;

            for (int index = 0; index < _systems.Count; index++)
            {
                InfinityParticleSystem system = _systems[index];
                if (system == null || !system.enabled ||
                    !system.gameObject.activeInHierarchy)
                    continue;

                var emission = system.emission;
                emission.rateOverTime = new InfinityMinMaxCurve(
                    _authoredEmissionRates[index] * emissionMultiplier);
                if (warmStart && system.isStopped)
                    system.Simulate(0.42f, true, true, true);
                if (!system.isPlaying)
                    system.Play();
            }
        }

        private void StopAndClearField()
        {
            if (!_runtimeReady || _fieldRoot == null ||
                !_fieldRoot.activeInHierarchy)
                return;

            for (int index = 0; index < _systems.Count; index++)
            {
                InfinityParticleSystem system = _systems[index];
                if (system == null || !system.enabled ||
                    !system.gameObject.activeInHierarchy || system.isStopped)
                    continue;
                system.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ScheduleRuntimeActivation()
        {
            if (!_initialized || !isActiveAndEnabled ||
                _fieldRoot == null || _runtimeActivation != null)
                return;

            _runtimeActivation = StartCoroutine(ActivateRuntimeAfterEnable());
        }

        private IEnumerator ActivateRuntimeAfterEnable()
        {
            // Infinity 1.5.1 creates and tears down unsafe native module data in
            // OnEnable/OnDisable. Never touch its runtime API in either callback
            // stack: keep the field active and allow one stable frame first.
            yield return null;
            yield return null;
            _runtimeActivation = null;
            if (!isActiveAndEnabled || _fieldRoot == null ||
                !_fieldRoot.activeInHierarchy)
                yield break;

            _runtimeReady = true;
            ApplyRuntimeSpiritLanguage();
            ApplySorting();
            if (_collectionPending)
            {
                BeginCollection();
                yield break;
            }
            if (_rewardArrivalPending)
            {
                BeginRewardArrival();
                yield break;
            }

            ApplyRuntimeState();
        }

        private void ApplyRuntimeSpiritLanguage()
        {
            if (!_runtimeReady)
                return;

            if (_runtimeSpiritMaterial == null)
            {
                _runtimeSpiritMaterial =
                    SpiritQiParticleStyle.CreateRuntimeMaterial(
                        _palette == SpiritPearlPalette.Health
                            ? "HealthSpiritQi_Runtime"
                            : "ManaSpiritQi_Runtime",
                        ResolveParticleDeepColor(),
                        ResolveParticleCoreColor(),
                        ResolveParticleHighlightColor());
            }

            for (int index = 0; index < _systems.Count; index++)
            {
                InfinityParticleSystem system = _systems[index];
                if (!CanUseRuntimeState(system) ||
                    !ManaSpiritFlowLanguage.TryResolve(
                        system.name,
                        out ManaSpiritFlowLayerStyle style))
                {
                    continue;
                }

                if (index < _authoredEmissionRates.Count)
                    _authoredEmissionRates[index] = style.EmissionRate *
                        (_palette == SpiritPearlPalette.Mana ? 0.72f : 1f);

                var main = system.main;
                main.loop = true;
                main.prewarm = false;
                main.startLifetime = CreateRange(style.Lifetime);
                main.startSpeed = new InfinityMinMaxCurve(0f);
                main.startSize3D = false;
                main.startSize = CreateRange(style.Size);
                main.startRotation3D = false;
                main.startRotation = new InfinityMinMaxCurve(0f);
                main.maxParticles = style.Capacity;

                var emission = system.emission;
                emission.rateOverTime = new InfinityMinMaxCurve(0f);
                emission.rateOverDistance = new InfinityMinMaxCurve(0f);

                var shape = system.shape;
                shape.enabled = true;
                shape.shapeType = InfinityShapeType.Circle;
                shape.radius = style.Radius;
                shape.radiusThickness = style.RadiusThickness;
                shape.arcValue = style.Arc;
                shape.arcMode = ResolveArcMode(style.ArcMotion);
                shape.arcSpread = style.ArcSpread;
                shape.arcSpeed = new InfinityMinMaxCurve(style.ArcSpeed);
                shape.rotation = new Vector3(
                    0f,
                    0f,
                    style.ShapeRotationZ);
                shape.scale = Vector3.one;

                var velocity = system.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = InfinitySimulationSpace.Local;
                velocity.x = CreateRange(style.LinearX);
                velocity.y = CreateRange(style.LinearY);
                velocity.z = new InfinityMinMaxCurve(0f);
                velocity.radial =
                    new InfinityMinMaxCurve(style.RadialVelocity);
                velocity.orbitalZ =
                    new InfinityMinMaxCurve(style.OrbitalVelocity);

                var noise = system.noise;
                noise.enabled = false;

                if (_runtimeSpiritMaterial != null)
                {
                    var renderer = system.renderer;
                    renderer.material = _runtimeSpiritMaterial;
                    renderer.renderMode = InfinityRenderMode.Billboard;
                    renderer.lengthScale = 1f;
                    renderer.velocityScale = 0f;
                }
            }
        }

        private void ApplyRuntimeState()
        {
            bool warmStart = _warmStartPending;
            _warmStartPending = false;
            switch (_state)
            {
                case MapVfxState.Hidden:
                case MapVfxState.Completed:
                case MapVfxState.Locked:
                    StopAndClearField();
                    break;
                case MapVfxState.Hovered:
                    PlayField(1.55f, warmStart);
                    break;
                default:
                    PlayField(1f, warmStart);
                    break;
            }
        }

        private void BeginRewardArrival()
        {
            _rewardArrivalPending = false;
            StopAndClearField();
            _transition = StartCoroutine(RewardArrivalRoutine());
        }

        private void BeginCollection()
        {
            _collectionPending = false;
            _transition = StartCoroutine(CollectionRoutine());
        }

        private void SetCoreVisible(bool visible, float alpha)
        {
            if (_core != null)
                _core.gameObject.SetActive(visible);
            if (_halo != null)
                _halo.gameObject.SetActive(visible);
            if (visible)
                SetCoreAlpha(alpha);
        }

        private void SetCoreAlpha(float alpha)
        {
            if (_core != null)
            {
                Color color = ResolveCoreColor();
                color.a *= Mathf.Clamp01(alpha);
                _core.color = color;
            }
            if (_halo != null)
            {
                Color color = ResolveHaloColor();
                color.a *= Mathf.Clamp01(alpha);
                _halo.color = color;
            }
        }

        private Color ResolveCoreColor()
        {
            return _palette == SpiritPearlPalette.Health
                ? HealthCoreColor
                : ManaCoreColor;
        }

        private Color ResolveHaloColor()
        {
            return _palette == SpiritPearlPalette.Health
                ? HealthHaloColor
                : ManaHaloColor;
        }

        private Color ResolveParticleDeepColor()
        {
            return _palette == SpiritPearlPalette.Health
                ? HealthParticleDeep
                : SpiritEnergyColorPalette.ManaDeep;
        }

        private Color ResolveParticleCoreColor()
        {
            return _palette == SpiritPearlPalette.Health
                ? HealthParticleCore
                : SpiritEnergyColorPalette.ManaFull;
        }

        private Color ResolveParticleHighlightColor()
        {
            return _palette == SpiritPearlPalette.Health
                ? HealthParticleHighlight
                : SpiritEnergyColorPalette.ManaHighlight;
        }

        private void ResetLocalMotion()
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private void StopTransition()
        {
            if (_transition == null)
                return;
            StopCoroutine(_transition);
            _transition = null;
        }

        private void OnDisable()
        {
            bool wasCompleting = _state == MapVfxState.Completing;
            StopTransition();
            if (_runtimeActivation != null)
            {
                StopCoroutine(_runtimeActivation);
                _runtimeActivation = null;
            }
            _runtimeReady = false;
            _rewardArrivalPending = false;
            _collectionPending = false;
            _warmStartPending = false;
            if (!wasCompleting)
                return;

            SetCoreVisible(false, 0f);
            _state = MapVfxState.Completed;
            InvokeCompletionCallback();
        }

        private void OnEnable()
        {
            if (_initialized)
                ScheduleRuntimeActivation();
        }

        private void InvokeCompletionCallback()
        {
            Action callback = _completionCallback;
            _completionCallback = null;
            callback?.Invoke();
        }

        private static float ResolveAuthoredRate(string systemName)
        {
            return ManaSpiritFlowLanguage.TryResolve(
                systemName,
                out ManaSpiritFlowLayerStyle style)
                ? style.EmissionRate
                : 0f;
        }

        private static InfinityMinMaxCurve CreateRange(Vector2 range)
        {
            return new InfinityMinMaxCurve(
                InfinityCurveStateMode.TwoConstants,
                Mathf.Min(range.x, range.y),
                Mathf.Max(range.x, range.y),
                1f,
                null,
                null);
        }

        private static InfinityMultiModeValue ResolveArcMode(
            ManaSpiritArcMotion motion)
        {
            return motion switch
            {
                ManaSpiritArcMotion.Loop => InfinityMultiModeValue.Loop,
                ManaSpiritArcMotion.PingPong =>
                    InfinityMultiModeValue.PingPong,
                _ => InfinityMultiModeValue.Random
            };
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static bool CanUseRuntimeState(
            InfinityParticleSystem system)
        {
            return system != null &&
                   system.enabled &&
                   system.gameObject.activeInHierarchy;
        }

        private void OnDestroy()
        {
            if (_runtimeSpiritMaterial != null)
                Destroy(_runtimeSpiritMaterial);
            _runtimeSpiritMaterial = null;
        }
    }
}
