using System;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;
using UnityEngine.Rendering;

namespace WanjieScroll.Effects
{
    /// <summary>
    /// Deep adapter for the six Infinity systems behind one mirrored ruyi-cloud
    /// stroke. It alone owns host/channel native state and runtime materials.
    /// Every native module is configured while stopped; after the sole Play
    /// boundary, choreography moves only ordinary Transforms along the Bezier
    /// path. Re-review before adding another resource family or playback cycle.
    /// </summary>
    internal sealed class RewardWindowBloomInfinityEmitter
    {
        private sealed class Channel
        {
            public int SideIndex;
            public int ChannelIndex;
            public bool Active;
            public InfinityParticleSystem System;
            public Transform MotionRoot;
            public TrailRenderer Filament;
        }

        private const string ManaFieldResourcePath =
            "Effects/Map/ManaSpiritInfinityField";
        private const int ParticleSortingBase = 174;

        private static readonly string[] ChannelSystemNames =
        {
            "CondensedCore",
            "ReturningCurrent",
            "AscendingWisps"
        };

        private readonly Channel[,] _channels = new Channel[
            RewardWindowBloomLanguage.SideCount,
            RewardWindowBloomLanguage.MaxChannelCount];
        private readonly Material[] _sourceMaterials =
            new Material[RewardWindowBloomLanguage.MaxChannelCount];
        private readonly Material[] _particleMaterials =
            new Material[RewardWindowBloomLanguage.MaxChannelCount];

        private Component _owner;
        private GameObject _host;
        private RewardWindowBloomGradeStyle _grade;
        private uint _seed;
        private bool _configured;
        private bool _playIssued;
        private bool _failed;
        private bool _runtimeReady;
        private bool _strokeActive;

        public bool IsActiveInHierarchy =>
            !_failed && _host != null && _host.activeInHierarchy;
        public int ParticleSystemCount => _host != null
            ? RewardWindowBloomLanguage.SideCount *
              RewardWindowBloomLanguage.MaxChannelCount
            : 0;

        public bool Build(Transform parent, int layer, uint seed)
        {
            _owner = parent;
            _seed = seed == 0u ? 1u : seed;
            GameObject prefab = Resources.Load<GameObject>(
                ManaFieldResourcePath);
            if (prefab == null)
            {
                Fail($"找不到 Resources/{ManaFieldResourcePath}.prefab。", null);
                return false;
            }

            _host = new GameObject("RewardWindowRuyiCloudInfinityHost");
            _host.layer = layer;
            _host.transform.SetParent(parent, false);
            // Authored play-on-awake state remains outside the render texture
            // until every native module has crossed both stable-frame gates.
            _host.transform.localPosition = Vector3.up * 100f;
            _host.SetActive(false);

            for (int sideIndex = 0;
                 sideIndex < RewardWindowBloomLanguage.SideCount;
                 sideIndex++)
            {
                GameObject instance = UnityEngine.Object.Instantiate(
                    prefab,
                    _host.transform,
                    false);
                instance.name = sideIndex == 0
                    ? "SpiritRuyiCloudLeft"
                    : "SpiritRuyiCloudRight";
                SetLayerRecursively(instance, layer);
                CollectChannels(instance, sideIndex);
            }

            if (!HasCompleteKnownChannelSet())
            {
                Fail(
                    "共用灵力 Prefab 缺少三种已知灵气通道；" +
                    "为避免回退为旧灰白粒子，本次保持空场。",
                    null);
                DiscardHost();
                return false;
            }

            _host.SetActive(true);
            return true;
        }

        /// <summary>
        /// Called after the first two-frame gate. This is the only method that
        /// writes Infinity modules, and it deliberately leaves every system
        /// stopped.
        /// </summary>
        public bool ConfigureAfterNativeReady(
            RewardWindowBloomGradeStyle grade)
        {
            if (!IsActiveInHierarchy || _configured)
                return false;

            try
            {
                _grade = grade;
                PrepareRuntimeMaterials(grade);
                for (int sideIndex = 0;
                     sideIndex < RewardWindowBloomLanguage.SideCount;
                     sideIndex++)
                {
                    for (int channelIndex = 0;
                         channelIndex < RewardWindowBloomLanguage.MaxChannelCount;
                         channelIndex++)
                    {
                        ConfigureChannel(
                            _channels[sideIndex, channelIndex],
                            grade);
                    }
                }
                _configured = true;
                return true;
            }
            catch (Exception exception)
            {
                Fail(
                    "Infinity 如意流云配置失败；本次保持空场。",
                    exception);
                return false;
            }
        }

        // The second gate moves stopped systems into view without native access.
        public bool ReleaseAfterConfigurationGate()
        {
            if (!IsActiveInHierarchy || !_configured || _playIssued)
                return false;

            _host.transform.localPosition = Vector3.zero;
            _runtimeReady = true;
            return true;
        }

        /// <summary>
        /// Sole Infinity Play boundary. Position and filament preparation are
        /// ordinary Unity operations and happen before Play.
        /// </summary>
        public bool BeginStroke()
        {
            if (!_runtimeReady || _playIssued || !IsActiveInHierarchy)
                return false;

            try
            {
                SetStrokeProgress(0f);
                for (int sideIndex = 0;
                     sideIndex < RewardWindowBloomLanguage.SideCount;
                     sideIndex++)
                {
                    for (int channelIndex = 0;
                         channelIndex < RewardWindowBloomLanguage.MaxChannelCount;
                         channelIndex++)
                    {
                        Channel channel = _channels[sideIndex, channelIndex];
                        if (!channel.Active)
                            continue;
                        channel.Filament.Clear();
                        channel.Filament.emitting = true;
                        channel.System.Play(
                            withChildren: false,
                            restart: false);
                    }
                }
                _playIssued = true;
                _strokeActive = true;
                return true;
            }
            catch (Exception exception)
            {
                Fail(
                    "Infinity 如意流云启动失败；未尝试 Emit 隐式重启。",
                    exception);
                return false;
            }
        }

        /// <summary>
        /// Managed-only motion after Play. World-space particles remain behind
        /// and reveal the fixed Bezier path while no native module is touched.
        /// </summary>
        public void SetStrokeProgress(float normalizedProgress)
        {
            if (!_strokeActive && !_runtimeReady)
                return;

            float progress = Mathf.Clamp01(normalizedProgress);
            for (int sideIndex = 0;
                 sideIndex < RewardWindowBloomLanguage.SideCount;
                 sideIndex++)
            {
                for (int channelIndex = 0;
                     channelIndex < _grade.ChannelCount;
                     channelIndex++)
                {
                    Channel channel = _channels[sideIndex, channelIndex];
                    float channelProgress =
                        RewardWindowBloomLanguage.ResolveChannelProgress(
                            _grade,
                            channelIndex,
                            progress);
                    PositionChannel(channel, channelProgress);
                }
            }
        }

        /// <summary>Stops only the managed filament; Infinity expires naturally.</summary>
        public void EndStroke()
        {
            if (!_strokeActive)
                return;
            SetFilamentsEmitting(false, clear: false);
            _strokeActive = false;
        }

        // Pure managed invalidation; the next enable must cross both gates.
        public void InvalidateAfterDisable()
        {
            SetFilamentsEmitting(false, clear: true);
            _configured = false;
            _playIssued = false;
            _runtimeReady = false;
            _strokeActive = false;
            if (_host != null)
                _host.transform.localPosition = Vector3.up * 100f;
        }

        /// <summary>Releases managed runtime materials without native access.</summary>
        public void DisposeManagedResources()
        {
            for (int index = 0;
                 index < RewardWindowBloomLanguage.MaxChannelCount;
                 index++)
            {
                DestroyMaterial(_particleMaterials[index]);
                _particleMaterials[index] = null;
            }
            _runtimeReady = false;
        }

        private void CollectChannels(GameObject instance, int sideIndex)
        {
            InfinityParticleSystem[] systems =
                instance.GetComponentsInChildren<InfinityParticleSystem>(true);
            for (int systemIndex = 0;
                 systemIndex < systems.Length;
                 systemIndex++)
            {
                InfinityParticleSystem candidate = systems[systemIndex];
                int channelIndex = ResolveChannelIndex(candidate.name);
                if (channelIndex < 0 ||
                    _channels[sideIndex, channelIndex] != null)
                {
                    candidate.gameObject.SetActive(false);
                    continue;
                }

                Material source = candidate.assetRendererModule.material;
                if (!UsesKnownSpiritMaterial(source))
                {
                    candidate.gameObject.SetActive(false);
                    continue;
                }

                if (_sourceMaterials[channelIndex] == null)
                    _sourceMaterials[channelIndex] = source;
                _channels[sideIndex, channelIndex] = new Channel
                {
                    SideIndex = sideIndex,
                    ChannelIndex = channelIndex,
                    System = candidate,
                    MotionRoot = candidate.transform,
                    Filament = CreateFilament(candidate.gameObject, channelIndex)
                };
            }
        }

        private void ConfigureChannel(
            Channel channel,
            RewardWindowBloomGradeStyle grade)
        {
            InfinityParticleSystem system = channel.System;
            system.Stop(
                withChildren: false,
                stopBehavior:
                    ParticleSystemStopBehavior.StopEmittingAndClear);

            RewardWindowBloomChannelStyle style =
                grade.GetChannel(channel.ChannelIndex);
            channel.Active = channel.ChannelIndex < grade.ChannelCount;
            float rate = channel.Active
                ? grade.ParticleRate * style.RateMultiplier
                : 0f;
            PositionChannel(channel, 0f);

            var main = system.main;
            main.duration = Mathf.Max(0.10f, grade.StrokeDuration);
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.useUnscaledTime = true;
            main.simulationSpace = InfinitySimulationSpace.World;
            main.startLifetime = CreateRange(grade.ParticleLifetime);
            main.startSpeed = new InfinityMinMaxCurve(0f);
            main.startSize3D = false;
            main.startSize = CreateRange(
                grade.ParticleSize * style.WidthMultiplier);
            main.startRotation3D = false;
            main.startRotation = CreateRange(new Vector2(-0.08f, 0.08f));
            main.maxParticles = grade.ResolveParticleCapacity(
                channel.ChannelIndex);
            main.autoRandomSeed = false;
            main.randomSeed = ResolveSeed(
                channel.SideIndex,
                channel.ChannelIndex);

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = new InfinityMinMaxCurve(rate);
            emission.rateOverDistance = new InfinityMinMaxCurve(0f);
            emission.SetBursts(Array.Empty<InfinityEmissionBurst>());

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = InfinityShapeType.Circle;
            shape.radius = grade.EmitterRadius;
            shape.radiusThickness = 1f;
            shape.arcValue = 360f;
            shape.arcMode = InfinityMultiModeValue.Random;
            shape.arcSpread = 0f;
            shape.rotation = Vector3.zero;
            shape.scale = Vector3.one;

            var velocity = system.velocityOverLifetime;
            velocity.enabled = false;
            var noise = system.noise;
            noise.enabled = false;

            var renderer = system.renderer;
            renderer.material = _particleMaterials[channel.ChannelIndex];
            renderer.renderMode = style.Stretched
                ? InfinityRenderMode.Stretch
                : InfinityRenderMode.Billboard;
            renderer.lengthScale = style.LengthScale;
            renderer.velocityScale = style.VelocityScale;
            renderer.sortingOrder =
                ParticleSortingBase + channel.ChannelIndex;
            renderer.customBatchKey =
                ParticleSortingBase + channel.ChannelIndex;

            ConfigureFilament(
                channel.Filament,
                grade,
                style,
                channel.ChannelIndex);
        }

        private void PositionChannel(Channel channel, float progress)
        {
            RewardWindowBloomChannelStyle style =
                _grade.GetChannel(channel.ChannelIndex);
            RewardWindowBloomLanguage.SamplePath(
                channel.SideIndex,
                progress,
                style.OutwardOffset,
                out Vector2 point,
                out Vector2 tangent);
            float patternScale =
                RewardWindowBloomLanguage.ResolvePatternScale(
                    _grade,
                    progress);
            point *= patternScale;
            channel.MotionRoot.localPosition = new Vector3(
                point.x,
                point.y,
                channel.ChannelIndex * -0.004f);
            float angle = Mathf.Atan2(tangent.y, tangent.x) *
                          Mathf.Rad2Deg;
            channel.MotionRoot.localRotation =
                Quaternion.Euler(0f, 0f, angle);
        }

        private void PrepareRuntimeMaterials(
            RewardWindowBloomGradeStyle grade)
        {
            for (int channelIndex = 0;
                 channelIndex < RewardWindowBloomLanguage.MaxChannelCount;
                 channelIndex++)
            {
                if (_particleMaterials[channelIndex] == null)
                {
                    _particleMaterials[channelIndex] = new Material(
                        _sourceMaterials[channelIndex])
                    {
                        name = $"RewardSpiritRuyiCloud_{channelIndex}_Runtime",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                RewardWindowBloomChannelStyle style =
                    grade.GetChannel(channelIndex);
                Material material = _particleMaterials[channelIndex];
                material.SetColor("_DeepColor", style.DeepColor);
                material.SetColor("_CoreColor", style.CoreColor);
                material.SetColor("_HighlightColor", style.HighlightColor);
                material.SetFloat("_Opacity", grade.Opacity);
                material.SetFloat("_Softness", 0.48f);
                material.SetFloat("_WispShape", style.WispShape);
            }
        }

        private static TrailRenderer CreateFilament(
            GameObject owner,
            int channelIndex)
        {
            TrailRenderer trail = owner.AddComponent<TrailRenderer>();
            trail.name = $"SpiritRuyiFilament_{channelIndex}";
            trail.emitting = false;
            trail.autodestruct = false;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.numCapVertices = 4;
            trail.numCornerVertices = 3;
            trail.minVertexDistance = 0.018f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sortingOrder = ParticleSortingBase - 1 + channelIndex;
            return trail;
        }

        private void ConfigureFilament(
            TrailRenderer trail,
            RewardWindowBloomGradeStyle grade,
            RewardWindowBloomChannelStyle style,
            int channelIndex)
        {
            trail.material = _particleMaterials[channelIndex];
            trail.time = grade.FilamentTime;
            trail.widthMultiplier =
                grade.FilamentWidth * style.WidthMultiplier;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.14f, 1f),
                new Keyframe(0.72f, 0.64f),
                new Keyframe(1f, 0f));
            Color head = Color.white;
            head.a = Mathf.Clamp01(grade.Opacity * 0.62f);
            trail.startColor = head;
            trail.endColor = new Color(1f, 1f, 1f, 0f);
            trail.emitting = false;
            trail.Clear();
        }

        private void SetFilamentsEmitting(bool emitting, bool clear)
        {
            for (int sideIndex = 0;
                 sideIndex < RewardWindowBloomLanguage.SideCount;
                 sideIndex++)
            {
                for (int channelIndex = 0;
                     channelIndex < RewardWindowBloomLanguage.MaxChannelCount;
                     channelIndex++)
                {
                    TrailRenderer filament =
                        _channels[sideIndex, channelIndex]?.Filament;
                    if (filament == null)
                        continue;
                    filament.emitting = emitting;
                    if (clear)
                        filament.Clear();
                }
            }
        }

        private bool HasCompleteKnownChannelSet()
        {
            for (int channelIndex = 0;
                 channelIndex < RewardWindowBloomLanguage.MaxChannelCount;
                 channelIndex++)
            {
                if (_sourceMaterials[channelIndex] == null)
                    return false;
                for (int sideIndex = 0;
                     sideIndex < RewardWindowBloomLanguage.SideCount;
                     sideIndex++)
                {
                    if (_channels[sideIndex, channelIndex] == null)
                        return false;
                }
            }
            return true;
        }

        private static bool UsesKnownSpiritMaterial(Material material)
        {
            return material != null &&
                   material.shader != null &&
                   string.Equals(
                       material.shader.name,
                       SpiritQiParticleStyle.ShaderName,
                       StringComparison.Ordinal) &&
                   material.HasProperty("_WispShape");
        }

        private static int ResolveChannelIndex(string systemName)
        {
            for (int index = 0; index < ChannelSystemNames.Length; index++)
            {
                if (string.Equals(
                    systemName,
                    ChannelSystemNames[index],
                    StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return -1;
        }

        private int ResolveSeed(int sideIndex, int channelIndex)
        {
            unchecked
            {
                uint value = _seed +
                             (uint)(sideIndex * 1009) +
                             (uint)(channelIndex * 313);
                return (int)(value == 0u ? 1u : value);
            }
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

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
                UnityEngine.Object.Destroy(material);
        }

        private void Fail(string message, Exception exception)
        {
            if (_failed)
                return;
            _failed = true;
            Debug.LogError($"[RewardWindowBloom] {message}", _owner);
            if (exception != null)
                Debug.LogException(exception, _owner);
        }

        private void DiscardHost()
        {
            if (_host != null)
            {
                _host.SetActive(false);
                UnityEngine.Object.Destroy(_host);
            }
            _host = null;
        }
    }
}
