using System;
using System.Collections;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.SceneManagement;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    public enum CombatScenePreloadState
    {
        Idle,
        Loading,
        Ready,
        Cleaning,
        Activated,
        Failed
    }

    public readonly struct CombatScenePreloadPlan
    {
        public string NodeContentId { get; }
        public string ScenarioId { get; }
        public string RunId { get; }
        public string FloorContentId { get; }

        public CombatScenePreloadPlan(
            string nodeContentId,
            string scenarioId)
            : this(nodeContentId, scenarioId, null, null) { }

        private CombatScenePreloadPlan(string nodeContentId, string scenarioId,
            string runId, string floorContentId)
        {
            NodeContentId = nodeContentId;
            ScenarioId = scenarioId;
            RunId = runId;
            FloorContentId = floorContentId;
        }

        internal bool Matches(CombatScenePreloadPlan other)
            => Matches(other.NodeContentId, other.ScenarioId) &&
                string.Equals(RunId, other.RunId, StringComparison.Ordinal) &&
                string.Equals(FloorContentId, other.FloorContentId, StringComparison.Ordinal);

        public bool Matches(
            string nodeContentId,
            string scenarioId)
        {
            return string.Equals(
                       NodeContentId,
                       nodeContentId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       ScenarioId,
                       scenarioId,
                       StringComparison.Ordinal);
        }

        /// <summary>
        /// 按稳定节点 ID 创建计划。节点必须仍可执行且属于战斗类型；调用不会
        /// 开始遭遇或改变会话状态。
        /// </summary>
        public static bool TryCreate(
            LevelRunSession session,
            string nodeContentId,
            out CombatScenePreloadPlan plan)
        {
            plan = default;
            if (session?.Profile == null ||
                string.IsNullOrWhiteSpace(nodeContentId))
                return false;
            if (session.TryGetPendingReturn(out _))
                return false;

            LevelNode node = session.FindNode(nodeContentId);
            if (!IsEligible(session, node))
                return false;

            plan = new CombatScenePreloadPlan(
                node.contentId,
                node.scenarioId, session.RunId, session.Profile.contentId);
            return true;
        }

        /// <summary>
        /// 仅当当前恰好有一个可执行战斗节点时自动创建计划。多个候选意味着
        /// 玩家尚未表达目标，此时宁可等待 Hover/点击信号，也不依赖数组顺序猜测。
        /// </summary>
        public static bool TryCreateUnambiguous(
            LevelRunSession session,
            out CombatScenePreloadPlan plan)
        {
            plan = default;
            if (session?.Profile == null)
                return false;
            if (session.TryGetPendingReturn(out _))
                return false;

            var nodes = session.Nodes;
            LevelNode candidate = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                LevelNode node = nodes[i];
                if (!IsEligible(session, node))
                    continue;

                if (candidate != null)
                    return false;
                candidate = node;
            }

            if (candidate == null)
                return false;

            plan = new CombatScenePreloadPlan(
                candidate.contentId,
                candidate.scenarioId, session.RunId, session.Profile.contentId);
            return true;
        }

        private static bool IsEligible(
            LevelRunSession session,
            LevelNode node)
        {
            if (node == null ||
                session.GetNodeState(node.contentId) !=
                LevelNodeViewState.Available)
            {
                return false;
            }

            if (node.nodeType != LevelNodeType.Encounter &&
                node.nodeType != LevelNodeType.Finale)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(node.scenarioId);
        }
    }

    /// <summary>
    /// 在关卡地图保持可见时，以 Additive 方式加载并隐藏构建目标战斗。
    /// 它以稳定节点 ID 接受目标意图，并在内部串行完成过期场景清理与重定向；
    /// 调用方不需要管理 Load/Unload 的竞态，也不会提前开始遭遇、回合或抽牌。
    /// 本类唯一拥有预载计划、场景引用与加载/清理协程；同文件的计划值和加载
    /// 句柄只是该状态机的无状态边界。本次扩展仍属于同一知识边界；若文件达到
    /// 800 行或出现第二类独立资源生命周期，必须重新审查是否提取深模块。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatScenePreloader : MonoBehaviour
    {
        private const float ReadyTimeoutSeconds = 30f;
        private static readonly ProfilerMarker PlanMatchMarker =
            new ProfilerMarker("Wanjie.CombatPreload.PlanMatch");
        private static readonly ProfilerMarker PlanMissMarker =
            new ProfilerMarker("Wanjie.CombatPreload.PlanMiss");
        private static readonly ProfilerMarker ActivationMarker =
            new ProfilerMarker("Wanjie.CombatPreload.ActivatePrepared");
        private static readonly ProfilerMarker FallbackCleanupMarker =
            new ProfilerMarker("Wanjie.CombatPreload.FallbackCleanup");

        private CombatScenePreloadPlan _plan;
        private CombatSceneBootstrap _preparedBootstrap;
        private Scene _combatScene;
        private Coroutine _loadRoutine;
        private string _error;
        private CombatScenePreloadPlan _pendingPlan;
        private bool _hasPendingPlan;
        private bool _discardRequested;
        private bool _cleanupEndsFailed;
        private double _loadStartedAt;

        public CombatScenePreloadState State { get; private set; }
            = CombatScenePreloadState.Idle;
        public string ScenarioId => _plan.ScenarioId;
        public string Error => _error;
        public CombatSceneBootstrap PreparedBootstrap => _preparedBootstrap;

        /// <summary>
        /// 没有明确目标时仅预载唯一候选；多个可用战斗节点会等待显式 Prepare。
        /// </summary>
        public void Begin(LevelRunSession session)
        {
            if (State != CombatScenePreloadState.Idle &&
                State != CombatScenePreloadState.Failed)
                return;

            if (!CombatScenePreloadPlan.TryCreateUnambiguous(
                    session,
                    out CombatScenePreloadPlan plan))
                return;

            StartPlan(plan);
        }

        /// <summary>
        /// 请求为稳定节点 ID 准备战斗。目标变化时保留最新请求，并先完整清理
        /// 已开始的 Additive 场景，再串行加载新目标。
        /// </summary>
        public bool Prepare(
            LevelRunSession session,
            string nodeContentId)
        {
            if (!CombatScenePreloadPlan.TryCreate(
                    session,
                    nodeContentId,
                    out CombatScenePreloadPlan plan))
            {
                return false;
            }

            if (State == CombatScenePreloadState.Activated)
                return false;

            if ((State == CombatScenePreloadState.Loading ||
                 State == CombatScenePreloadState.Ready) &&
                _plan.Matches(plan))
            {
                // Hover 在候选间往返时，回到当前目标即可取消尚未开始的重定向。
                _pendingPlan = default;
                _hasPendingPlan = false;
                _discardRequested = false;
                return true;
            }

            if (State == CombatScenePreloadState.Cleaning)
            {
                _pendingPlan = plan;
                _hasPendingPlan = true;
                _cleanupEndsFailed = false;
                return true;
            }

            if (State == CombatScenePreloadState.Loading ||
                State == CombatScenePreloadState.Ready)
            {
                _pendingPlan = plan;
                _hasPendingPlan = true;
                _discardRequested = true;
                if (State == CombatScenePreloadState.Ready)
                    BeginDiscardCleanup();
                return true;
            }

            StartPlan(plan);
            return true;
        }

        private void StartPlan(CombatScenePreloadPlan plan)
        {
            _plan = plan;
            _pendingPlan = default;
            _hasPendingPlan = false;
            _discardRequested = false;
            _cleanupEndsFailed = false;
            _preparedBootstrap = null;
            _combatScene = default;
            _error = null;
            State = CombatScenePreloadState.Loading;
            _loadStartedAt = Time.realtimeSinceStartupAsDouble;
            _loadRoutine = StartCoroutine(LoadCombatScene());
            Debug.Log(
                $"[CombatScenePreloader] 开始后台准备：{_plan.ScenarioId}。");
        }

        /// <summary>
        /// Run 交互会改变下一战的卡组或属性；已经构建的隐藏战斗因此失效。
        /// 调用方须等 IsBusy 结束后再提交变更，避免后台构建读取半途状态。
        /// </summary>
        public void InvalidatePreparedRunState()
        {
            if (State == CombatScenePreloadState.Loading ||
                State == CombatScenePreloadState.Ready ||
                State == CombatScenePreloadState.Cleaning)
                BeginFallbackCleanup();
        }

        public bool IsPreparingFor(
            string nodeContentId,
            string scenarioId)
        {
            if (!IsBusy)
                return false;

            if (_hasPendingPlan)
            {
                return _pendingPlan.Matches(
                    nodeContentId,
                    scenarioId);
            }

            return _plan.Matches(nodeContentId, scenarioId);
        }

        /// <summary>是否正忙于后台准备（加载中或清理中），与具体场景无关。</summary>
        public bool IsBusy =>
            State == CombatScenePreloadState.Loading ||
            State == CombatScenePreloadState.Cleaning;

        public bool IsReadyFor(
            string nodeContentId,
            string scenarioId)
        {
            return State == CombatScenePreloadState.Ready &&
                   _plan.Matches(nodeContentId, scenarioId);
        }

        public bool HasFailedFor(
            string nodeContentId,
            string scenarioId)
        {
            return State == CombatScenePreloadState.Failed &&
                   _plan.Matches(nodeContentId, scenarioId);
        }

        public bool TryClaim(CombatSceneBootstrap bootstrap, out string scenarioId)
        {
            bool claimed = TryClaim(bootstrap, out CombatScenePreloadPlan plan);
            scenarioId = claimed ? plan.ScenarioId : null;
            return claimed;
        }

        /// <summary>Transfers the immutable exact-node launch identity, never only its Scenario alias.</summary>
        public bool TryClaim(CombatSceneBootstrap bootstrap, out CombatScenePreloadPlan plan)
        {
            plan = default;
            if (State != CombatScenePreloadState.Loading ||
                bootstrap == null ||
                bootstrap.gameObject.scene.name != SceneNames.Combat)
            {
                return false;
            }

            if (_preparedBootstrap != null &&
                _preparedBootstrap != bootstrap)
            {
                return false;
            }

            _preparedBootstrap = bootstrap;
            _combatScene = bootstrap.gameObject.scene;
            plan = _plan;
            return true;
        }

        public void MarkReady(CombatSceneBootstrap bootstrap)
        {
            if (State != CombatScenePreloadState.Loading ||
                bootstrap != _preparedBootstrap)
            {
                return;
            }

            if (_discardRequested)
            {
                BeginDiscardCleanup();
                return;
            }

            State = CombatScenePreloadState.Ready;
            Debug.Log(
                $"[CombatScenePreloader] 隐藏战斗已准备完成：{_plan.ScenarioId}。");
            Debug.Log("[CombatPreloadTiming] scenario=" + _plan.ScenarioId +
                " ready=" + ((Time.realtimeSinceStartupAsDouble - _loadStartedAt) * 1000d).ToString(
                    "F1", System.Globalization.CultureInfo.InvariantCulture) + "ms");
        }

        public void MarkFailed(
            CombatSceneBootstrap bootstrap,
            string error)
        {
            if (bootstrap != _preparedBootstrap ||
                State == CombatScenePreloadState.Activated)
            {
                return;
            }

            _error = string.IsNullOrWhiteSpace(error)
                ? "隐藏战斗构建失败。"
                : error;
            BeginFailureCleanup();
        }

        /// <summary>Finishes a revoked partial build without admitting a ready
        /// combat. The existing discard queue owns unload and any latest plan.</summary>
        public void MarkCancelled(CombatSceneBootstrap bootstrap)
        {
            if (bootstrap != _preparedBootstrap ||
                State != CombatScenePreloadState.Loading) return;
            BeginDiscardCleanup();
        }

        public bool TryCreateLoadHandle(out ISceneLoadHandle handle)
        {
            handle = null;
            if (State == CombatScenePreloadState.Cleaning)
            {
                // 转场不应与已有 Additive Combat 的卸载并行。取消悬而未决的
                // Hover 重定向，清理完成后再由句柄启动普通单场景加载。
                using (PlanMissMarker.Auto())
                using (FallbackCleanupMarker.Auto())
                {
                    BeginFallbackCleanup();
                    handle = new CleanupThenCombatSceneLoadHandle(this);
                }
                return true;
            }

            if (State != CombatScenePreloadState.Loading &&
                State != CombatScenePreloadState.Ready)
            {
                return false;
            }

            // 预载计划必须与玩家实际点击的活动遭遇一致。地图上常有多个
            // 可用节点，玩家点击的未必是预载的那一个；不匹配时丢弃过期
            // 预载并回退普通加载，而不是让转场失败。
            LevelRunSession session = LevelRunSessionStore.Current;
            if (session != null && session.ActiveEncounter.HasValue)
            {
                LevelEncounterLaunch launch = session.ActiveEncounter.Value;
                if (!string.Equals(_plan.RunId, session.RunId, StringComparison.Ordinal) ||
                    !string.Equals(_plan.FloorContentId, session.Profile.contentId, StringComparison.Ordinal) ||
                    !_plan.Matches(launch.NodeContentId, launch.ScenarioId) ||
                    _discardRequested ||
                    _hasPendingPlan)
                {
                    Debug.Log(
                        $"[CombatScenePreloader] 点击遭遇 {launch.ScenarioId} " +
                        $"与当前稳定预载 {_plan.ScenarioId} 不匹配，" +
                        "先清理旧场景再回退普通加载。");
                    using (PlanMissMarker.Auto())
                    using (FallbackCleanupMarker.Auto())
                    {
                        BeginFallbackCleanup();
                        handle = new CleanupThenCombatSceneLoadHandle(this);
                    }
                    return true;
                }

                using (PlanMatchMarker.Auto())
                {
                    // 事件型采样：表示活动遭遇与稳定预载计划完全命中。
                }
            }

            handle = new PreloadedCombatSceneLoadHandle(
                this,
                _plan.NodeContentId,
                _plan.ScenarioId);
            return true;
        }

        public bool TryActivatePrepared(out string error)
        {
            using (ActivationMarker.Auto())
            {
                error = null;
                if (State != CombatScenePreloadState.Ready ||
                    _preparedBootstrap == null ||
                    !_combatScene.IsValid() ||
                    !_combatScene.isLoaded)
                {
                    error = "隐藏战斗尚未准备完成。";
                    return false;
                }

                LevelRunSession session = LevelRunSessionStore.Current;
                if (session == null || !session.ActiveEncounter.HasValue)
                {
                    error = "激活隐藏战斗时没有活动遭遇。";
                    return false;
                }

                LevelEncounterLaunch launch = session.ActiveEncounter.Value;
                if (!string.Equals(_plan.RunId, session.RunId, StringComparison.Ordinal) ||
                    !string.Equals(_plan.FloorContentId, session.Profile.contentId, StringComparison.Ordinal) ||
                    !_plan.Matches(launch.NodeContentId, launch.ScenarioId))
                {
                    error = "活动遭遇与后台准备的战斗不匹配。";
                    return false;
                }

                Scene sourceScene = SceneManager.GetActiveScene();
                if (!SceneManager.SetActiveScene(_combatScene))
                {
                    error = "无法把预载 Combat 设置为活动场景。";
                    return false;
                }

                GameObject[] sourceRoots = sourceScene.IsValid()
                    ? sourceScene.GetRootGameObjects()
                    : Array.Empty<GameObject>();
                for (int i = 0; i < sourceRoots.Length; i++)
                    sourceRoots[i].SetActive(false);

                string requestId = SceneContext.Get<string>(
                    SceneContextKeys.TransitionRequestId,
                    null);
                if (!_preparedBootstrap.ActivatePrepared(
                        requestId,
                        out error))
                {
                    for (int i = 0; i < sourceRoots.Length; i++)
                        sourceRoots[i].SetActive(true);
                    if (sourceScene.IsValid() && sourceScene.isLoaded)
                        SceneManager.SetActiveScene(sourceScene);
                    return false;
                }

                State = CombatScenePreloadState.Activated;
                // 不卸载源地图场景——仅停用其根对象，返回时直接重新激活，
                // 避免从零加载地图导致的卡顿。
                // 源场景的根对象已在上方 SetActive(false) 隐藏。

                return true;
            }
        }

        private IEnumerator LoadCombatScene()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                SceneNames.Combat,
                LoadSceneMode.Additive);
            if (operation == null)
            {
                _error = "无法开始后台加载 Combat 场景。";
                _loadRoutine = null;
                CompleteCleanup(endsFailed: !_discardRequested);
                yield break;
            }

            while (!operation.isDone)
                yield return null;

            if (!_combatScene.IsValid())
                _combatScene = SceneManager.GetSceneByName(SceneNames.Combat);

            float deadline = Time.realtimeSinceStartup + ReadyTimeoutSeconds;
            while (State == CombatScenePreloadState.Loading &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (State == CombatScenePreloadState.Loading)
            {
                _error = "隐藏战斗构建超时。";
                BeginFailureCleanup();
            }

            _loadRoutine = null;
        }

        private void BeginFailureCleanup()
        {
            if (State == CombatScenePreloadState.Cleaning)
            {
                return;
            }

            bool endsFailed = !_discardRequested;
            _cleanupEndsFailed = endsFailed;
            if (_combatScene.IsValid() && _combatScene.isLoaded)
            {
                State = CombatScenePreloadState.Cleaning;
                StartCoroutine(CleanupScene());
            }
            else
            {
                CompleteCleanup(endsFailed);
            }

            Debug.LogWarning(
                $"[CombatScenePreloader] 后台准备失败：{_error}");
        }

        private void BeginFallbackCleanup()
        {
            _pendingPlan = default;
            _hasPendingPlan = false;
            _discardRequested = true;
            _cleanupEndsFailed = false;

            if (State == CombatScenePreloadState.Ready)
                BeginDiscardCleanup();
            // Loading 必须等不可取消的 LoadSceneAsync 和隐藏战斗构建结束；
            // MarkReady/MarkFailed 会接续清理。Cleaning 已经在卸载中。
        }

        private void BeginDiscardCleanup()
        {
            if (State == CombatScenePreloadState.Cleaning)
                return;

            _discardRequested = true;
            _cleanupEndsFailed = false;
            if (_combatScene.IsValid() && _combatScene.isLoaded)
            {
                State = CombatScenePreloadState.Cleaning;
                StartCoroutine(CleanupScene());
            }
            else
            {
                CompleteCleanup(endsFailed: false);
            }
        }

        private IEnumerator CleanupScene()
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(_combatScene);
            while (unload != null && !unload.isDone)
                yield return null;
            CompleteCleanup(_cleanupEndsFailed);
        }

        private void CompleteCleanup(bool endsFailed)
        {
            CombatScenePreloadPlan completedPlan = _plan;
            string completedError = _error;
            bool startPending = _hasPendingPlan;
            CombatScenePreloadPlan pendingPlan = _pendingPlan;

            _preparedBootstrap = null;
            _combatScene = default;
            _loadRoutine = null;
            _pendingPlan = default;
            _hasPendingPlan = false;
            _discardRequested = false;
            _cleanupEndsFailed = false;

            if (startPending)
            {
                State = CombatScenePreloadState.Idle;
                _plan = default;
                _error = null;
                StartPlan(pendingPlan);
                return;
            }

            if (endsFailed)
            {
                _plan = completedPlan;
                _error = completedError;
                State = CombatScenePreloadState.Failed;
            }
            else
            {
                _plan = default;
                _error = null;
                State = CombatScenePreloadState.Idle;
            }
        }

        /// <summary>战斗返回后重置预载器状态，使其能为下一场战斗预载。</summary>
        public void ResetForReturn()
        {
            if (_loadRoutine != null)
            {
                StopCoroutine(_loadRoutine);
                _loadRoutine = null;
            }
            State = CombatScenePreloadState.Idle;
            _preparedBootstrap = null;
            _combatScene = default;
            _error = null;
            _plan = default;
            _pendingPlan = default;
            _hasPendingPlan = false;
            _discardRequested = false;
            _cleanupEndsFailed = false;
        }

        private void OnDestroy()
        {
            if (State == CombatScenePreloadState.Activated)
                return;

            if (_combatScene.IsValid() && _combatScene.isLoaded)
                SceneManager.UnloadSceneAsync(_combatScene);
        }
    }

    /// <summary>
    /// 过期 Additive Combat 的清理屏障。清理结束前不会启动普通加载，避免
    /// Unity 同时卸载旧 Combat 并构建第二份 Combat 场景。
    /// </summary>
    public sealed class CleanupThenCombatSceneLoadHandle : ISceneLoadHandle
    {
        private static readonly ProfilerMarker FallbackLoadStartMarker =
            new ProfilerMarker("Wanjie.CombatPreload.FallbackLoadStart");
        private readonly CombatScenePreloader _preloader;
        private UnitySceneLoadHandle _fallback;

        public CleanupThenCombatSceneLoadHandle(
            CombatScenePreloader preloader)
        {
            _preloader = preloader;
        }

        public float Progress
        {
            get
            {
                EnsureFallbackStarted();
                return _fallback?.Progress ?? 0f;
            }
        }

        public bool IsReadyForActivation
        {
            get
            {
                EnsureFallbackStarted();
                return _fallback != null &&
                       _fallback.IsReadyForActivation;
            }
        }

        public bool IsDone
        {
            get
            {
                EnsureFallbackStarted();
                return _fallback != null && _fallback.IsDone;
            }
        }

        public string Error
        {
            get
            {
                EnsureFallbackStarted();
                return _fallback?.Error;
            }
        }

        public void Activate()
        {
            EnsureFallbackStarted();
            _fallback?.Activate();
        }

        private void EnsureFallbackStarted()
        {
            if (_fallback != null)
                return;
            if (_preloader != null && _preloader.IsBusy)
                return;

            using (FallbackLoadStartMarker.Auto())
            {
                _fallback = new UnitySceneLoadHandle(SceneNames.Combat);
                _fallback.Start();
            }
        }
    }

    public sealed class PreloadedCombatSceneLoadHandle : ISceneLoadHandle
    {
        private readonly CombatScenePreloader _preloader;
        private readonly string _nodeContentId;
        private readonly string _scenarioId;
        private bool _activated;
        private bool _done;
        private string _activationError;

        public PreloadedCombatSceneLoadHandle(
            CombatScenePreloader preloader,
            string nodeContentId,
            string scenarioId)
        {
            _preloader = preloader;
            _nodeContentId = nodeContentId;
            _scenarioId = scenarioId;
        }

        public float Progress => IsReadyForActivation ? 1f : 0.9f;

        public bool IsReadyForActivation =>
            _preloader != null &&
            _preloader.IsReadyFor(_nodeContentId, _scenarioId);

        public bool IsDone => _done;

        public string Error
        {
            get
            {
                // Activate() 之后结果已经落定。
                if (_activated)
                    return _activationError;
                if (!string.IsNullOrEmpty(_activationError))
                    return _activationError;
                if (_preloader == null)
                    return "后台战斗预载器已销毁。";
                if (_preloader.HasFailedFor(
                        _nodeContentId,
                        _scenarioId))
                    return _preloader.Error;
                // 遮幕先行后，转场依赖这个句柄等待预载收敛。预载既不在进行、
                // 也未就绪，说明计划已被取消或替换；此时必须失败回退，
                // 不能在遮幕后面空等到 DestinationReady 超时。
                if (!_preloader.IsBusy &&
                    !_preloader.IsReadyFor(_nodeContentId, _scenarioId))
                    return "后台战斗准备已被取消。";
                return null;
            }
        }

        public void Activate()
        {
            if (_activated)
                return;
            _activated = true;

            if (_preloader == null ||
                !_preloader.TryActivatePrepared(out _activationError))
            {
                if (string.IsNullOrWhiteSpace(_activationError))
                    _activationError = "无法激活后台准备的战斗。";
            }

            _done = true;
        }
    }
}
