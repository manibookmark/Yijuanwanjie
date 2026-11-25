using System;
using System.Collections;
using System.Collections.Generic;
using Tuanjie.Infinity;
using UnityEngine;
using UnityEngine.Infinity;
using Unity.Profiling;

namespace WanjieScroll.Effects
{
    /// <summary>一个由调用方确定初始状态、随后交给 Infinity 自然演化的粒子。</summary>
    public readonly struct BossInfinityParticleSeed
    {
        public BossInfinityParticleSeed(
            Vector3 position,
            Vector3 velocity,
            float lifetime,
            float size,
            float rotation,
            Color color,
            float visualVariant = 0f)
        {
            Position = position;
            Velocity = velocity;
            Lifetime = lifetime;
            Size = size;
            Rotation = rotation;
            Color = color;
            VisualVariant = visualVariant;
        }

        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float Lifetime { get; }
        public float Size { get; }
        public float Rotation { get; }
        public Color Color { get; }
        public float VisualVariant { get; }
    }

    /// <summary>
    /// 一次 Infinity 粒子场的材质外观。动作碎片与登场烟缕共用生命周期，
    /// 但由这个值对象明确隔离轮廓知识，避免继续扩张 Play 的参数列表。
    /// </summary>
    public readonly struct BossInfinityParticleAppearance
    {
        public BossInfinityParticleAppearance(
            Color deepColor,
            Color coreColor,
            Color highlightColor,
            float opacity,
            float softness,
            float wispShape,
            float smokeBlend = 0f,
            float smokeStretch = 1f,
            float smokeCurl = 0f)
        {
            DeepColor = deepColor;
            CoreColor = coreColor;
            HighlightColor = highlightColor;
            Opacity = opacity;
            Softness = softness;
            WispShape = wispShape;
            SmokeBlend = smokeBlend;
            SmokeStretch = smokeStretch;
            SmokeCurl = smokeCurl;
        }

        public Color DeepColor { get; }
        public Color CoreColor { get; }
        public Color HighlightColor { get; }
        public float Opacity { get; }
        public float Softness { get; }
        public float WispShape { get; }
        public float SmokeBlend { get; }
        public float SmokeStretch { get; }
        public float SmokeCurl { get; }
    }

    /// <summary>
    /// BOSS 短时粒子的唯一 Infinity 生命周期适配器。正式效果共用一个
    /// 无贴图、无自动发射的 prefab；实例稳定两帧后配置并显式 Play，再等待
    /// 两个完整帧才首次 Emit。之后只由 Infinity manager 以 unscaled 时钟推进，
    /// 避免第二模拟所有者和原生生命周期竞争。
    ///
    /// 规模审查：本类超过 500 行，但所有状态仍只属于一个 active Infinity
    /// 原生 host 的准备、发射、自然收尾和销毁。异步请求与战斗租约已由
    /// BossInfinityParticleWarmup 隔离；若未来支持并发通道或重复播放策略，
    /// 必须先重新审查，不能继续把第二套状态机叠加到这里。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossInfinityParticleField : MonoBehaviour
    {
        internal const string ResourcePath =
            "Effects/Boss/BossNaturalBurst";
        private const int MaxSupportedParticles = 512;
        internal const int MaxEmitsPerFrame = 32;

        private static readonly ProfilerMarker InstantiateMarker =
            new ProfilerMarker("BossInfinity.Instantiate");
        private static readonly ProfilerMarker ConfigureMarker =
            new ProfilerMarker("BossInfinity.Configure");
        private static readonly ProfilerMarker ShaderWarmupMarker =
            new ProfilerMarker("BossInfinity.ShaderWarmup");
        private static readonly ProfilerMarker EmitBatchMarker =
            new ProfilerMarker("BossInfinity.EmitBatch");

        private InfinityParticleSystem _system;
        private BossInfinityParticleSeed[] _seeds;
        private Material _runtimeMaterial;
        private BossInfinityParticleAppearance _appearance;
        private int _sortingLayerId;
        private int _sortingOrder;
        private bool _runtimeReady;
        private bool _keepAliveAfterPlayback;
        private Coroutine _lifecycleRoutine;

        public InfinityParticleSystem System => _system;
        public int RequestedParticleCount => _seeds?.Length ?? 0;
        public int RuntimeParticleCount => _runtimeReady && _system != null
            ? _system.particleCount
            : 0;
        public bool HasEmitted { get; private set; }
        public bool IsRuntimeReady => _runtimeReady;
        public bool IsPlaybackPending => _lifecycleRoutine != null;

        public void StopAndDestroy()
        {
            StopAllCoroutines();
            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        public static BossInfinityParticleField Play(
            Transform parent,
            Vector3 worldPosition,
            string instanceName,
            IReadOnlyList<BossInfinityParticleSeed> seeds,
            BossInfinityParticleAppearance appearance,
            int sortingLayerId,
            int sortingOrder)
        {
            if (seeds == null || seeds.Count == 0)
                return null;

            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"缺少 BOSS Infinity 粒子资源：{ResourcePath}");
                return null;
            }

            BossInfinityParticleField field;
            using (InstantiateMarker.Auto())
            {
                field = CreateInstance(
                    prefab,
                    parent,
                    worldPosition,
                    instanceName);
            }
            if (!field.Initialize(
                    seeds,
                    appearance,
                    sortingLayerId,
                    sortingOrder))
            {
                if (Application.isPlaying)
                    Destroy(field.gameObject);
                else
                    DestroyImmediate(field.gameObject);
                return null;
            }
            return field;
        }

        /// <summary>
        /// Creates the active, empty native host used by the battle-scoped warmup
        /// session. Resource loading remains outside this adapter; after two
        /// stable frames this instance owns all Infinity module access until it
        /// is destroyed with the battle scene.
        /// </summary>
        internal static BossInfinityParticleField CreatePrepared(
            GameObject prefab,
            Transform parent,
            BossInfinityParticleAppearance appearance)
        {
            if (prefab == null)
                return null;

            BossInfinityParticleField field;
            using (InstantiateMarker.Auto())
            {
                field = CreateInstance(
                    prefab,
                    parent,
                    Vector3.zero,
                    "BossEntranceInfinityPrepared");
            }
            if (!field.InitializePrepared(appearance))
            {
                if (Application.isPlaying)
                    Destroy(field.gameObject);
                else
                    DestroyImmediate(field.gameObject);
                return null;
            }
            return field;
        }

        private static BossInfinityParticleField CreateInstance(
            GameObject prefab,
            Transform parent,
            Vector3 worldPosition,
            string instanceName)
        {
            GameObject instance = parent != null
                ? Instantiate(prefab, parent, false)
                : Instantiate(prefab);
            instance.name = string.IsNullOrWhiteSpace(instanceName)
                ? "BossInfinityParticles"
                : instanceName;
            instance.transform.position = worldPosition;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance.AddComponent<BossInfinityParticleField>();
        }

        private bool Initialize(
            IReadOnlyList<BossInfinityParticleSeed> seeds,
            BossInfinityParticleAppearance appearance,
            int sortingLayerId,
            int sortingOrder)
        {
            if (!CaptureSystem() || !CopySeeds(seeds))
                return false;
            _appearance = appearance;
            _sortingLayerId = sortingLayerId;
            _sortingOrder = sortingOrder;
            _keepAliveAfterPlayback = false;

            // EditMode 只允许静态检查配置，不驱动 Infinity 原生生命周期。
            if (Application.isPlaying)
                _lifecycleRoutine = StartCoroutine(EmitAfterInitialization());
            return true;
        }

        private bool InitializePrepared(
            BossInfinityParticleAppearance appearance)
        {
            if (!CaptureSystem())
                return false;
            _appearance = appearance;
            _keepAliveAfterPlayback = true;
            if (Application.isPlaying)
                _lifecycleRoutine = StartCoroutine(PrepareRuntime());
            return true;
        }

        private bool CaptureSystem()
        {
            _system = GetComponent<InfinityParticleSystem>();
            if (_system != null)
                return true;
            Debug.LogError("BossNaturalBurst 缺少 InfinityParticleSystem。", this);
            return false;
        }

        private bool CopySeeds(IReadOnlyList<BossInfinityParticleSeed> seeds)
        {
            if (seeds == null || seeds.Count == 0)
                return false;
            int count = Mathf.Min(seeds.Count, MaxSupportedParticles);
            _seeds = new BossInfinityParticleSeed[count];
            for (int i = 0; i < count; i++)
                _seeds[i] = seeds[i];
            return true;
        }

        private IEnumerator PrepareRuntime()
        {
            // The instantiated Infinity root stays active for its entire lease.
            // Native modules are first touched only after two stable frames.
            yield return null;
            yield return null;
            if (!CanAccessRuntime())
            {
                _lifecycleRoutine = null;
                yield break;
            }

            using (ConfigureMarker.Auto())
            {
                ConfigureRuntime();
            }

            // Submit one fully transparent particle while minions are active.
            // Merely loading a Material does not force the Editor/driver to
            // compile its render pass; this moves that deterministic cold cost
            // out of the Boss reveal without producing a visible warmup frame.
            _system.Play(withChildren: false);
            yield return null;
            yield return null;
            if (!CanAccessRuntime())
            {
                _lifecycleRoutine = null;
                yield break;
            }
            using (ShaderWarmupMarker.Auto())
            {
                Emit(
                    new BossInfinityParticleSeed(
                        Vector3.zero,
                        Vector3.zero,
                        0.05f,
                        0.002f,
                        0f,
                        new Color(0f, 0f, 0f, 0f)),
                    0);
            }
            // Four render boundaries give the transparent draw time to compile
            // and expire naturally. No Stop/Clear touches the warmed native host.
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            HasEmitted = false;
            _runtimeReady = true;
            _lifecycleRoutine = null;
        }

        /// <summary>
        /// Queues one playback on the already loaded and configured host. A
        /// false result is a bounded fallback signal: callers must not perform
        /// a synchronous load or instantiate on the reveal frame.
        /// </summary>
        internal bool TryPlayPrepared(
            Vector3 worldPosition,
            string instanceName,
            IReadOnlyList<BossInfinityParticleSeed> seeds,
            BossInfinityParticleAppearance appearance,
            int sortingLayerId,
            int sortingOrder)
        {
            if (!_runtimeReady || _lifecycleRoutine != null ||
                !CanAccessRuntime() || !CopySeeds(seeds))
            {
                return false;
            }

            name = string.IsNullOrWhiteSpace(instanceName)
                ? "BossInfinityParticles"
                : instanceName;
            transform.position = worldPosition;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            bool appearanceChanged = !HasSameAppearance(
                _appearance,
                appearance);
            _appearance = appearance;
            _sortingLayerId = sortingLayerId;
            _sortingOrder = sortingOrder;
            HasEmitted = false;

            using (ConfigureMarker.Auto())
            {
                // Production warmup already configured the same preset and
                // compiled its material. The reveal frame normally changes
                // only sorting, keeping palette/native simulation writes out
                // of the latency-sensitive boundary. Custom callers still get
                // a correct full reconfiguration when appearance really differs.
                if (appearanceChanged)
                    ConfigureRuntime();
                else
                    ConfigureSorting();
            }
            _lifecycleRoutine = StartCoroutine(EmitPrepared());
            return true;
        }

        private IEnumerator EmitAfterInitialization()
        {
            // Infinity 1.5.1 激活后需要完成原生 archetype 初始化；不要在
            // Instantiate 的同一帧访问模块或切换播放状态。
            yield return null;
            yield return null;
            if (!CanAccessRuntime())
            {
                _lifecycleRoutine = null;
                yield break;
            }

            using (ConfigureMarker.Auto())
            {
                ConfigureRuntime();
            }
            _runtimeReady = true;
            yield return EmitPrepared();
        }

        private IEnumerator EmitPrepared()
        {
            if (!CanAccessRuntime() || _seeds == null || _seeds.Length == 0)
            {
                _lifecycleRoutine = null;
                yield break;
            }

            // 不依赖 Emit 的隐式 Play。显式切换播放状态后再给原生系统两个
            // 完整帧建立可写 archetype，首次手动 Emit 才是安全的。
            _system.Play(withChildren: false);
            yield return null;
            yield return null;
            if (!CanAccessRuntime())
            {
                _lifecycleRoutine = null;
                yield break;
            }

            int firstBatch = GetNextEmissionBatchCount(_seeds.Length);
            EmitRange(0, firstBatch);
            HasEmitted = _system.particleCount > 0;
            if (!HasEmitted)
            {
                // A cold native archetype can miss the first write. Retry only
                // through an explicit Play gate; never depend on Emit's implicit
                // transition from Stopped.
                _system.Play(withChildren: false);
                yield return null;
                yield return null;
                if (!CanAccessRuntime())
                {
                    _lifecycleRoutine = null;
                    yield break;
                }
                EmitRange(0, firstBatch);
                HasEmitted = _system.particleCount > 0;
            }

            if (!HasEmitted)
            {
                Debug.LogError(
                    $"{name} 的 Infinity 粒子发射失败，" +
                    $"请求数量：{RequestedParticleCount}。",
                    this);
                FinishPlayback(destroyOneShot: true);
                yield break;
            }

            for (int offset = firstBatch; offset < _seeds.Length;)
            {
                yield return null;
                if (!CanAccessRuntime())
                {
                    _lifecycleRoutine = null;
                    yield break;
                }
                int batch = GetNextEmissionBatchCount(
                    _seeds.Length - offset);
                EmitRange(offset, batch);
                offset += batch;
            }

            float longestLifetime = GetLongestLifetime(_seeds);
            float elapsed = 0f;
            float tailDuration = longestLifetime + 0.22f;
            while (_system != null && elapsed < tailDuration)
            {
                yield return null;
                if (_system == null)
                    yield break;
                float delta = Mathf.Clamp(
                    Time.unscaledDeltaTime,
                    0f,
                    0.1f);
                if (delta <= 0f)
                    continue;
                elapsed += delta;
            }

            FinishPlayback(destroyOneShot: true);
        }

        internal static int GetNextEmissionBatchCount(int remaining)
        {
            return Mathf.Clamp(remaining, 0, MaxEmitsPerFrame);
        }

        private void EmitRange(int offset, int count)
        {
            int end = Mathf.Min(_seeds.Length, offset + count);
            using (EmitBatchMarker.Auto())
            {
                for (int i = Mathf.Max(0, offset); i < end; i++)
                    Emit(_seeds[i], i);
            }
        }

        private static float GetLongestLifetime(
            IReadOnlyList<BossInfinityParticleSeed> seeds)
        {
            float longest = 0.05f;
            for (int i = 0; i < seeds.Count; i++)
                longest = Mathf.Max(longest, seeds[i].Lifetime);
            return longest;
        }

        private bool CanAccessRuntime()
        {
            return _system != null && _system.enabled &&
                _system.gameObject.activeInHierarchy;
        }

        private void ConfigureRuntime()
        {
            // BOSS 短时表现与调用方一样使用 unscaled 时钟。Infinity 的
            // main/renderer 均是 native module，必须由这个 owner 统一配置。
            var main = _system.main;
            main.useUnscaledTime = true;
            ConfigureRenderer();
            ConfigureSimulation(_system, _appearance);
        }

        private void ConfigureSorting()
        {
            var renderer = _system.renderer;
            renderer.sortingLayerID = _sortingLayerId;
            renderer.sortingOrder = _sortingOrder;
            renderer.customBatchKey = _sortingOrder;
        }

        private static bool HasSameAppearance(
            BossInfinityParticleAppearance left,
            BossInfinityParticleAppearance right)
        {
            return left.DeepColor == right.DeepColor &&
                   left.CoreColor == right.CoreColor &&
                   left.HighlightColor == right.HighlightColor &&
                   Mathf.Approximately(left.Opacity, right.Opacity) &&
                   Mathf.Approximately(left.Softness, right.Softness) &&
                   Mathf.Approximately(left.WispShape, right.WispShape) &&
                   Mathf.Approximately(left.SmokeBlend, right.SmokeBlend) &&
                   Mathf.Approximately(left.SmokeStretch, right.SmokeStretch) &&
                   Mathf.Approximately(left.SmokeCurl, right.SmokeCurl);
        }

        private void FinishPlayback(bool destroyOneShot)
        {
            _lifecycleRoutine = null;
            _seeds = null;
            if (_keepAliveAfterPlayback || !destroyOneShot || this == null)
                return;
            Destroy(gameObject);
        }

        private void Emit(BossInfinityParticleSeed seed, int index)
        {
            float lifetime = Mathf.Max(0.05f, seed.Lifetime);
            Color encodedColor = seed.Color;
            // BossActionParticle 的烟雾分支借 startColor.r 携带三层标记：
            // 0=侧向雾体、0.5=主气脉、1=速度亮丝。非烟雾动作不读取 RGB，
            // 因此既有拳击、横扫、防御保持原样。
            encodedColor.r = Mathf.Clamp01(seed.VisualVariant);
            var emit = new InfinityParticleSystem.EmitParams
            {
                position = seed.Position,
                velocity = seed.Velocity,
                startLifetime = lifetime,
                startSize = Mathf.Max(0.002f, seed.Size),
                rotation = seed.Rotation,
                startColor = (Color32)encodedColor,
                randomSeed = unchecked(
                    0x9e3779b9u + (uint)index * 2654435761u)
            };
            _system.Emit(emit, 1);
        }

        private void ConfigureRenderer()
        {
            var renderer = _system.renderer;
            if (_runtimeMaterial == null)
            {
                Material source = renderer.material;
                if (source != null)
                {
                    _runtimeMaterial = new Material(source)
                    {
                        name = $"{name}_InfinityMaterial"
                    };
                    renderer.material = _runtimeMaterial;
                }
            }
            if (_runtimeMaterial != null)
            {
                SetPaletteColor(
                    _runtimeMaterial,
                    "_DeepColor",
                    _appearance.DeepColor);
                SetPaletteColor(
                    _runtimeMaterial,
                    "_CoreColor",
                    _appearance.CoreColor);
                SetPaletteColor(
                    _runtimeMaterial,
                    "_HighlightColor",
                    _appearance.HighlightColor);
                _runtimeMaterial.SetFloat(
                    "_Opacity",
                    Mathf.Clamp01(_appearance.Opacity));
                _runtimeMaterial.SetFloat(
                    "_Softness",
                    Mathf.Clamp(_appearance.Softness, 0.05f, 0.9f));
                _runtimeMaterial.SetFloat(
                    "_WispShape",
                    Mathf.Clamp01(_appearance.WispShape));
                _runtimeMaterial.SetFloat(
                    "_SmokeBlend",
                    Mathf.Clamp01(_appearance.SmokeBlend));
                _runtimeMaterial.SetFloat(
                    "_SmokeStretch",
                    Mathf.Clamp(_appearance.SmokeStretch, 1f, 4f));
                _runtimeMaterial.SetFloat(
                    "_SmokeCurl",
                    Mathf.Clamp01(_appearance.SmokeCurl));
            }
            ConfigureSorting();
            if (_appearance.SmokeBlend >= 0.5f)
            {
                // Shader 已分别生成宽雾与细流丝，不能再由 Renderer 按速度
                // 二次拉伸；双重拉伸正是实战“像鞭子”的直接来源。
                renderer.renderMode = InfinityRenderMode.Billboard;
                renderer.lengthScale = 1f;
                renderer.velocityScale = 0f;
                renderer.cameraVelocityScale = 0f;
                renderer.freeformStretching = false;
                renderer.rotateWithStretchDirection = false;
            }
        }

        /// <summary>
        /// 只给登场烟缕配置同旋向中心涡流；共享 infinity 资产保持不变，
        /// 因此拳击、横扫、防御和落地尘不会被登场调参污染。
        /// </summary>
        public static void ConfigureSimulation(
            InfinityParticleSystem system,
            BossInfinityParticleAppearance appearance)
        {
            if (system == null || appearance.SmokeBlend < 0.5f)
                return;

            var noise = system.noise;
            noise.enabled = false;

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            var zero = new InfinityMinMaxCurve(0f);
            velocity.x = zero;
            velocity.y = zero;
            velocity.z = zero;
            velocity.orbitalX = zero;
            velocity.orbitalY = zero;
            velocity.orbitalOffsetX = zero;
            velocity.orbitalOffsetY = zero;
            velocity.orbitalOffsetZ = zero;
            velocity.radial = zero;
            velocity.orbitalZ = new InfinityMinMaxCurve(
                BossEntranceConvergenceParticlePattern.GetOrbitalSpeed(
                    appearance.SmokeCurl));
            velocity.speedModifier = CreateConvergenceSpeedCurve();
            velocity.space = InfinitySimulationSpace.Local;
        }

        private static InfinityMinMaxCurve CreateConvergenceSpeedCurve()
        {
            float freeze =
                BossEntranceConvergenceParticlePattern.MotionFreezeNormalized;
            var curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(Mathf.Max(0f, freeze - 0.008f), 1f),
                new Keyframe(freeze, 0f),
                new Keyframe(1f, 0f));
            return new InfinityMinMaxCurve(
                InfinityCurveStateMode.Curve,
                0f,
                1f,
                1f,
                curve,
                curve);
        }

        private static void SetPaletteColor(
            Material material,
            string property,
            Color color)
        {
            color.a = 1f;
            material.SetColor(property, color);
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial == null)
                return;
            if (Application.isPlaying)
                Destroy(_runtimeMaterial);
            else
                DestroyImmediate(_runtimeMaterial);
            _runtimeMaterial = null;
        }

        private void OnDisable()
        {
            // Infinity owns native cleanup for the same callback stack.
            StopAllCoroutines();
            _lifecycleRoutine = null;
            _runtimeReady = false;
        }
    }
}
