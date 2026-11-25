using System.Collections;
using Unity.Profiling;
using UnityEngine;

namespace WanjieScroll.Effects
{
    internal enum BossInfinityParticleWarmupState
    {
        NotStarted = 0,
        Loading = 1,
        PreparingNativeRuntime = 2,
        Ready = 3,
        Consumed = 4,
        Failed = 5,
        Cancelled = 6
    }

    /// <summary>
    /// Owns the battle-scoped resource request and the single active Infinity
    /// entrance host. Callers express only Begin and TryPlay; prefab paths,
    /// native-frame gates, cancellation generations and fallback policy stay
    /// inside this module.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class BossInfinityParticleWarmup : MonoBehaviour
    {
        private static readonly ProfilerMarker CompleteLoadMarker =
            new ProfilerMarker("BossInfinity.Warmup.CompleteLoad");

        private BossEntranceEffectMode _mode;
        private BossInfinityParticleField _preparedField;
        private BossEntranceEffectPreset _preparedPreset;
        private Coroutine _loadRoutine;
        private int _generation;
        private bool _beginRequested;

        public BossEntranceEffectMode Mode => _mode;
        public BossInfinityParticleWarmupState State { get; private set; } =
            BossInfinityParticleWarmupState.NotStarted;
        public bool IsReady =>
            State == BossInfinityParticleWarmupState.Ready &&
            _preparedField != null &&
            _preparedField.IsRuntimeReady;
        public string FailureReason { get; private set; }

        public bool TryGetPreparedPreset(
            out BossEntranceEffectPreset preset)
        {
            preset = _preparedPreset;
            return preset != null && preset.TryValidate(out _);
        }

        public static BossInfinityParticleWarmup Create(
            Transform activeParent,
            BossEntranceEffectMode mode)
        {
            var host = new GameObject("BossEntranceInfinityWarmup");
            if (activeParent != null)
                host.transform.SetParent(activeParent, false);
            BossInfinityParticleWarmup warmup =
                host.AddComponent<BossInfinityParticleWarmup>();
            warmup._mode = mode;
            return warmup;
        }

        /// <summary>
        /// Starts preparation once the combat presentation hierarchy is active.
        /// Repeated calls are idempotent and never create a second Infinity host.
        /// </summary>
        public void Begin()
        {
            if (State == BossInfinityParticleWarmupState.Loading ||
                State == BossInfinityParticleWarmupState.PreparingNativeRuntime ||
                State == BossInfinityParticleWarmupState.Ready ||
                State == BossInfinityParticleWarmupState.Consumed)
            {
                return;
            }

            _beginRequested = true;
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                return;
            StartLoad();
        }

        public bool TryPlay(
            Vector3 worldPosition,
            string instanceName,
            System.Collections.Generic.IReadOnlyList<
                BossInfinityParticleSeed> seeds,
            BossInfinityParticleAppearance appearance,
            int sortingLayerId,
            int sortingOrder,
            out BossInfinityParticleField field)
        {
            field = null;
            if (!IsReady)
                return false;

            if (!_preparedField.TryPlayPrepared(
                    worldPosition,
                    instanceName,
                    seeds,
                    appearance,
                    sortingLayerId,
                    sortingOrder))
            {
                return false;
            }

            field = _preparedField;
            State = BossInfinityParticleWarmupState.Consumed;
            return true;
        }

        private void StartLoad()
        {
            if (_loadRoutine != null)
                return;
            _beginRequested = false;
            FailureReason = null;
            int generation = ++_generation;
            State = BossInfinityParticleWarmupState.Loading;
            _loadRoutine = StartCoroutine(LoadAndPrepare(generation));
        }

        private IEnumerator LoadAndPrepare(int generation)
        {
            ResourceRequest prefabRequest = Resources.LoadAsync<GameObject>(
                BossInfinityParticleField.ResourcePath);
            prefabRequest.priority = -1;
            ResourceRequest presetRequest =
                Resources.LoadAsync<BossEntranceEffectPreset>(
                    BossEntranceEffectPreset.DefaultResourcePath);
            presetRequest.priority = -1;
            yield return prefabRequest;
            yield return presetRequest;
            if (!IsCurrent(generation))
                yield break;

            GameObject prefab;
            using (CompleteLoadMarker.Auto())
            {
                prefab = prefabRequest.asset as GameObject;
                _preparedPreset =
                    presetRequest.asset as BossEntranceEffectPreset;
            }
            if (prefab == null)
            {
                Fail($"缺少 BOSS Infinity 粒子资源：" +
                     BossInfinityParticleField.ResourcePath);
                yield break;
            }

            string presetError = null;
            if (_preparedPreset == null ||
                !_preparedPreset.TryValidate(out presetError))
            {
                Fail(string.IsNullOrWhiteSpace(presetError)
                    ? "缺少 BOSS 默认登场预设：" +
                      BossEntranceEffectPreset.DefaultResourcePath
                    : presetError);
                yield break;
            }

            State = BossInfinityParticleWarmupState.PreparingNativeRuntime;
            _preparedField = BossInfinityParticleField.CreatePrepared(
                prefab,
                transform,
                BossEntranceEffect.ResolveParticleAppearance(
                    _preparedPreset,
                    _mode));
            if (_preparedField == null)
            {
                Fail("BOSS Infinity 预热实例创建失败。");
                yield break;
            }

            while (IsCurrent(generation) && _preparedField != null &&
                   !_preparedField.IsRuntimeReady)
            {
                yield return null;
            }
            if (!IsCurrent(generation))
                yield break;
            if (_preparedField == null || !_preparedField.IsRuntimeReady)
            {
                Fail("BOSS Infinity 原生模块未能完成稳定帧初始化。");
                yield break;
            }

            _loadRoutine = null;
            State = BossInfinityParticleWarmupState.Ready;
        }

        private bool IsCurrent(int generation)
        {
            return generation == _generation &&
                State != BossInfinityParticleWarmupState.Cancelled;
        }

        private void Fail(string reason)
        {
            _loadRoutine = null;
            FailureReason = string.IsNullOrWhiteSpace(reason)
                ? "未知 BOSS Infinity 预热错误。"
                : reason;
            State = BossInfinityParticleWarmupState.Failed;
            Debug.LogWarning($"[BossWarmup] {FailureReason}", this);
        }

        private void OnEnable()
        {
            // This callback starts only an asynchronous resource request. The
            // native adapter still owns its separate two-stable-frame gate.
            if (_beginRequested && _loadRoutine == null)
                StartLoad();
        }

        private void OnDisable()
        {
            // Do not touch Infinity modules from this callback stack.
            _generation++;
            StopAllCoroutines();
            _loadRoutine = null;
            if (State != BossInfinityParticleWarmupState.Consumed)
                State = BossInfinityParticleWarmupState.Cancelled;
        }

        private void OnDestroy()
        {
            _generation++;
            _loadRoutine = null;
            _preparedField = null;
            _preparedPreset = null;
        }
    }
}
