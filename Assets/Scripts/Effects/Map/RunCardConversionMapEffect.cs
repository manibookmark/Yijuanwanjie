using System;
using System.Collections;
using Tuanjie.Infinity;
using UnityEngine;

namespace WanjieScroll.Effects.Map
{
    [Serializable]
    public sealed class RunCardConversionMapEffectTuning
    {
        [Header("碑体状态")]
        [InspectorName("锁定颜色")]
        public Color lockedTint =
            new Color(0.38f, 0.42f, 0.39f, 0.72f);
        [InspectorName("可用颜色")]
        public Color availableTint =
            new Color(0.92f, 1f, 0.97f, 1f);
        [InspectorName("悬停颜色")]
        public Color hoveredTint = Color.white;
        [InspectorName("完成颜色")]
        public Color completedTint =
            new Color(0.62f, 0.72f, 0.68f, 0.86f);

        [Header("呼吸与悬停")]
        [Min(0f), InspectorName("可用呼吸速度")]
        public float availablePulseSpeed = 1.45f;
        [Range(0f, 0.12f), InspectorName("可用呼吸幅度")]
        public float availablePulseAmplitude = 0.012f;
        [Range(1f, 1.25f), InspectorName("悬停放大")]
        public float hoveredScale = 1.045f;
        [Min(0.01f), InspectorName("缩放跟随速度")]
        public float scaleFollowSpeed = 10f;
        [Min(0), InspectorName("悬停附加粒子数")]
        public int hoverAccentCount = 4;

        [Header("锁定与完成余韵")]
        [Range(0.1f, 1f), InspectorName("锁定灵气范围")]
        public float lockedFlowScale = 0.68f;
        [Min(0.05f), InspectorName("锁定粒子间隔（秒）")]
        public float lockedEmissionInterval = 0.72f;
        [Min(0), InspectorName("锁定初始粒子数")]
        public int lockedInitialCount = 2;
        [Range(0.1f, 1f), InspectorName("完成余韵范围")]
        public float completedFlowScale = 0.52f;
        [Min(0.05f), InspectorName("完成余韵间隔（秒）")]
        public float completedEmissionInterval = 1.35f;
        [Min(0), InspectorName("完成初始粒子数")]
        public int completedInitialCount = 1;

        [Header("归元完成确认")]
        // 以下字段仅为旧 Prefab 的序列化兼容保留；卡牌迁移已由 Canvas
        // 粒子系统负责，地图完成段不再发射或平移粒子。
        [HideInInspector]
        public int committedBurstCount = 10;
        [Range(0.08f, 0.22f), InspectorName("完成确认时长（秒）")]
        public float gatherDuration = 0.18f;
        [HideInInspector]
        public float gatherTargetY = 2.45f;
        [HideInInspector]
        public float gatherHorizontalFactor = 0.18f;
        [HideInInspector]
        public float gatherStartScale = 1.045f;
        [HideInInspector]
        public float gatherEndScale = 0.965f;
        [HideInInspector]
        public int coreFlashCount = 9;
        [HideInInspector]
        public float sealDuration = 0.48f;
        [Range(0f, 0.2f), InspectorName("碑体确认脉冲幅度")]
        public float sealReboundAmplitude = 0.035f;
    }

    /// <summary>
    /// Owns the complete visual lifecycle of the Guiyuan stele map node.
    /// Callers provide semantic map states or request one committed finish;
    /// Infinity topology, sprite tinting and one-shot timing remain private.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunCardConversionMapEffect : MonoBehaviour,
        IMapNodeStateVisual,
        IMapNodeInteractionAnchor
    {
        [SerializeField] private RunCardConversionMapEffectTuning tuning =
            new RunCardConversionMapEffectTuning();

        [Header("生成器绑定（通常不手动修改）")]
        [SerializeField] private Transform motionRoot;
        [SerializeField] private SpriteRenderer steleRenderer;
        [SerializeField] private Transform flowRoot;
        [SerializeField] private InfinityParticleSystem flow;
        [SerializeField] private ParticleSystem coreFlash;

        private MapVfxState _state = MapVfxState.Locked;
        private Coroutine _committedRoutine;
        private Coroutine _runtimeActivation;
        private Action _pendingCommittedCallback;
        private Vector3 _flowIdlePosition;
        private Vector3 _flowIdleScale = Vector3.one;
        private Vector3 _motionBaseScale = Vector3.one;
        private int _queuedFlowEmissionCount;
        private int _flowEmissionEarliestFrame = int.MaxValue;
        private bool _initialized;
        private bool _runtimeReady;
        private bool _stateApplicationPending;
        private bool _committedPending;

        public MapVfxState State => _state;
        public bool RetainsCompletedVisual => true;
        public bool IsPlayingCommitted => _committedRoutine != null;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            if (!_initialized)
                return;

            FlushQueuedFlowEmission();
            if (_state == MapVfxState.Completing || motionRoot == null)
                return;

            float targetScale = 1f;
            if (_state == MapVfxState.Available)
            {
                targetScale += Mathf.Sin(
                    Time.unscaledTime * tuning.availablePulseSpeed) *
                    tuning.availablePulseAmplitude;
            }
            else if (_state == MapVfxState.Hovered)
            {
                targetScale = tuning.hoveredScale;
            }

            motionRoot.localScale = Vector3.Lerp(
                motionRoot.localScale,
                _motionBaseScale * targetScale,
                1f - Mathf.Exp(
                    -Time.unscaledDeltaTime *
                    Mathf.Max(0.01f, tuning.scaleFollowSpeed)));
        }

        /// <summary>
        /// Applies one persistent interaction state. Entering any persistent
        /// state cancels an unfinished committed preview and restores authored
        /// transforms before changing emission.
        /// </summary>
        public void SetState(MapVfxState state, bool immediate = true)
        {
            EnsureInitialized();
            CancelCommitted();

            if (state == MapVfxState.Completing)
                state = MapVfxState.Available;

            _state = state;
            if (state == MapVfxState.Hidden)
            {
                if (_runtimeReady)
                    StopParticles();
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
            RestoreAuthoredPose();
            ApplyStateTint();
            if (_runtimeReady)
                ApplyPersistentRuntimeState();
            else
            {
                _stateApplicationPending = true;
                ScheduleRuntimeActivation();
            }
        }

        private void ApplyStateTint()
        {
            switch (_state)
            {
                case MapVfxState.Available:
                    steleRenderer.color = tuning.availableTint;
                    break;
                case MapVfxState.Hovered:
                    steleRenderer.color = tuning.hoveredTint;
                    break;
                case MapVfxState.Completed:
                    steleRenderer.color = tuning.completedTint;
                    break;
                default:
                    steleRenderer.color = tuning.lockedTint;
                    break;
            }
        }

        private void ApplyPersistentRuntimeState()
        {
            _stateApplicationPending = false;
            switch (_state)
            {
                case MapVfxState.Available:
                    StartFlow(emitAccent: false);
                    break;
                case MapVfxState.Hovered:
                    StartFlow(emitAccent: true);
                    break;
                case MapVfxState.Completed:
                    StartSparseFlow(completed: true);
                    break;
                default:
                    StartSparseFlow(completed: false);
                    break;
            }
        }

        public bool PlayCompletion(Action onComplete = null)
        {
            return PlayCommitted(onComplete);
        }

        public bool TryGetInteractionWorldAnchor(out Vector3 worldPosition)
        {
            EnsureInitialized();
            Transform anchor = coreFlash != null
                ? coreFlash.transform
                : steleRenderer != null
                    ? steleRenderer.transform
                    : transform;
            worldPosition = anchor.position;
            return gameObject.activeInHierarchy;
        }

        /// <summary>
        /// Plays the one-shot map-space confirmation pulse after the Canvas
        /// card-to-map-anchor convergence. The callback fires once after the effect
        /// has entered Completed.
        /// </summary>
        public bool PlayCommitted(Action onComplete = null)
        {
            EnsureInitialized();
            if (_committedRoutine != null || _committedPending)
                return false;

            _state = MapVfxState.Completing;
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
            if (_runtimeReady)
                BeginCommitted(onComplete);
            else
            {
                _committedPending = true;
                _pendingCommittedCallback = onComplete;
                ScheduleRuntimeActivation();
            }
            return true;
        }

        private void BeginCommitted(Action onComplete)
        {
            _committedPending = false;
            _pendingCommittedCallback = null;
            _committedRoutine = StartCoroutine(CommittedRoutine(onComplete));
        }

        private IEnumerator CommittedRoutine(Action onComplete)
        {
            _state = MapVfxState.Completing;
            RestoreAuthoredPose();
            steleRenderer.color = tuning.hoveredTint;
            // 卡牌向碑芯的迁移已由奖励窗的程序化粒子系统完整表达。
            // 地图这里只做落印确认：停止继续发射，让存量流光自然消退，
            // 绝不再平移整个 local-simulation 粒子根撞向碑芯。
            _queuedFlowEmissionCount = 0;
            _flowEmissionEarliestFrame = int.MaxValue;
            if (flow != null && flow.gameObject.activeInHierarchy)
            {
                flow.Stop(
                    true, ParticleSystemStopBehavior.StopEmitting);
            }
            float gatherDuration = Mathf.Clamp(
                tuning.gatherDuration, 0.08f, 0.22f);
            float elapsed = 0f;
            while (elapsed < gatherDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / gatherDuration);
                float eased = t * t * (3f - 2f * t);
                float glow = Mathf.Sin(t * Mathf.PI);
                motionRoot.localScale = _motionBaseScale *
                    (1f + glow * tuning.sealReboundAmplitude);
                Color charged = Color.Lerp(
                    tuning.hoveredTint,
                    Color.white,
                    glow * 0.22f);
                steleRenderer.color = Color.Lerp(
                    charged, tuning.completedTint, eased);
                yield return null;
            }

            StopParticles();
            RestoreAuthoredPose();
            steleRenderer.color = tuning.completedTint;
            _state = MapVfxState.Completed;
            _committedRoutine = null;
            onComplete?.Invoke();
        }

        private void StartFlow(bool emitAccent)
        {
            flowRoot.localScale = _flowIdleScale;
            EnsureFlowPlaybackReady();
            if (emitAccent && tuning.hoverAccentCount > 0)
                QueueOrEmitFlow(tuning.hoverAccentCount);
        }

        private void StartSparseFlow(bool completed)
        {
            StopParticles();
            flowRoot.localScale = _flowIdleScale *
                (completed
                    ? tuning.completedFlowScale
                    : tuning.lockedFlowScale);
            // Infinity 1.5.1 无法安全支持 StopEmittingAndClear 后的稀疏
            // 手动 Emit：Burst SpawnSystem 会访问失效的 archetype line，
            // 最终令 Editor SIGSEGV。锁定/完成态保留碑体与范围缩放，粒子
            // 只在 Available/Hovered/Completing 的正常 Play 生命周期中运行。
        }

        private void StopParticles()
        {
            _queuedFlowEmissionCount = 0;
            _flowEmissionEarliestFrame = int.MaxValue;
            if (_runtimeReady && flow != null &&
                flow.gameObject.activeInHierarchy)
            {
                flow.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (coreFlash != null)
            {
                coreFlash.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void EnsureFlowPlaybackReady()
        {
            if (!_runtimeReady || flow == null ||
                !flow.gameObject.activeInHierarchy)
                return;

            bool restarting = !flow.isPlaying;
            if (!flow.isPlaying)
                flow.Play();
            if (restarting)
            {
                // Infinity 1.5.1 在 StopEmittingAndClear 后同帧 Emit 会让
                // Burst SpawnSystem 访问尚未重建的 archetype line。至少跨过
                // 两个引擎帧，让 OnEnable/Play 的原生结构先稳定下来。
                _flowEmissionEarliestFrame = Time.frameCount + 2;
            }
            else if (_flowEmissionEarliestFrame == int.MaxValue)
            {
                _flowEmissionEarliestFrame = Time.frameCount;
            }
        }

        private void QueueOrEmitFlow(int count)
        {
            if (!_runtimeReady || count <= 0 || flow == null)
                return;

            if (!flow.isPlaying ||
                Time.frameCount < _flowEmissionEarliestFrame)
            {
                _queuedFlowEmissionCount += count;
                return;
            }

            flow.Emit(count);
        }

        private void FlushQueuedFlowEmission()
        {
            if (!_runtimeReady || _queuedFlowEmissionCount <= 0 || flow == null ||
                !flow.isPlaying ||
                Time.frameCount < _flowEmissionEarliestFrame)
                return;

            int count = _queuedFlowEmissionCount;
            _queuedFlowEmissionCount = 0;
            flow.Emit(count);
        }

        private void CancelCommitted()
        {
            _committedPending = false;
            _pendingCommittedCallback = null;
            if (_committedRoutine == null)
                return;
            StopCoroutine(_committedRoutine);
            _committedRoutine = null;
        }

        private void RestoreAuthoredPose()
        {
            if (motionRoot != null)
                motionRoot.localScale = _motionBaseScale;
            if (flowRoot != null)
            {
                flowRoot.localPosition = _flowIdlePosition;
                flowRoot.localScale = _flowIdleScale;
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;
            if (motionRoot == null || steleRenderer == null ||
                flowRoot == null || flow == null)
            {
                throw new InvalidOperationException(
                    "归元碑样机 Prefab 缺少生成器绑定的表现引用。");
            }

            _motionBaseScale = motionRoot.localScale;
            _flowIdlePosition = flowRoot.localPosition;
            _flowIdleScale = flowRoot.localScale;
            _initialized = true;
        }

        private void OnDisable()
        {
            CancelCommitted();
            if (_runtimeActivation != null)
            {
                StopCoroutine(_runtimeActivation);
                _runtimeActivation = null;
            }
            _runtimeReady = false;
            _stateApplicationPending = false;
            _queuedFlowEmissionCount = 0;
            _flowEmissionEarliestFrame = int.MaxValue;
        }

        private void OnEnable()
        {
            if (_initialized)
                ScheduleRuntimeActivation();
        }

        private void ScheduleRuntimeActivation()
        {
            if (!Application.isPlaying || !_initialized ||
                !isActiveAndEnabled || _runtimeActivation != null)
                return;
            _runtimeActivation = StartCoroutine(ActivateRuntimeAfterEnable());
        }

        private IEnumerator ActivateRuntimeAfterEnable()
        {
            yield return null;
            _runtimeActivation = null;
            if (!isActiveAndEnabled || flow == null ||
                !flow.gameObject.activeInHierarchy)
                yield break;

            _runtimeReady = true;
            if (_committedPending)
            {
                Action callback = _pendingCommittedCallback;
                BeginCommitted(callback);
            }
            else if (_stateApplicationPending)
            {
                ApplyPersistentRuntimeState();
            }
        }

        private void OnValidate()
        {
            if (tuning == null)
                tuning = new RunCardConversionMapEffectTuning();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor tooling persists only this pure-value DTO. Runtime child
        /// references and transient particle state are intentionally excluded.
        /// </summary>
        public string CaptureTuningJson()
        {
            if (tuning == null)
                tuning = new RunCardConversionMapEffectTuning();
            return JsonUtility.ToJson(tuning);
        }

        public void ApplyTuningJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException(
                    "归元碑调参 JSON 不能为空。", nameof(json));
            if (tuning == null)
                tuning = new RunCardConversionMapEffectTuning();
            JsonUtility.FromJsonOverwrite(json, tuning);
        }
#endif
    }
}
