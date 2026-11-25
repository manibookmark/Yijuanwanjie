using System.Collections;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Transitions;
using WanjieScroll.UI.Views;
using Object = UnityEngine.Object;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 常驻转场运行时装配。创建 DontDestroyOnLoad 根对象，
    /// 注入 Effects 遮幕、Core 控制器和 Unity 场景加载后端。
    /// 2026-10-18规模审查：本owner统一持有转场请求、失败恢复租约和加载策略。
    /// 已激活目的场景的失败不能交回被卸载的源场景；恢复仅取消构建、调用Dungeon
    /// 的既有回滚合同并发起全新地图请求，不保存第二份Run或实现地图/战斗规则。
    /// 2026-10-21规模复核：地图重载的身份、准备阶段与失败通知下沉到Core单一owner；
    /// 本宿主仍只持有Unity协程、加载策略和适配器，不新增结算或Run状态职责。
    /// </summary>
    public sealed class SceneTransitionRuntimeBootstrap : MonoBehaviour
    {
        public static bool IsCoverFullyShown => _instance != null &&
            _instance._curtainView != null && _instance._curtainView.IsCovered;
        private static SceneTransitionRuntimeBootstrap _instance;

        private PaperCloudTransitionView _curtainView;
        private SceneTransitionController _controller;
        private UnitySceneLoadBackend _loadBackend;
        private FlowBarrier _interactionBarrier;
        private FlowBarrier _externalModalBarrier;
        private Coroutine _tickCoroutine;
        // The Core owner holds request identity, pending/started state and its
        // at-most-once failure notification; this host only performs scene work.
        private readonly MapReloadRequestLifecycle _mapReload = new MapReloadRequestLifecycle();
        private Coroutine _failedCombatRecovery;
        private bool _failedCombatMapReload;

        public event Action<SceneTransitionResult> TransitionCompleted;

        public static bool IsInstantiated => _instance != null;

        /// <summary>
        /// Holds input while preparing the selected encounter. Chapter resource
        /// loading has its own entrance cover; in-chapter actions show no loader.
        /// </summary>
        public IDisposable HoldPreparation() =>
            _interactionBarrier.Hold("scene-loading-preparation");

        /// <summary>
        /// The Core controller reserves input and request identity before any
        /// encounter checkpoint work. The resident curtain owns all mist state.
        /// No scene is loaded or Run changed by this visual preparation command.
        /// </summary>
        public bool TryBeginBattlePreparation(string requestId, Sprite background,
            Color tint, out string error)
        {
            if (_mapReload.IsBusy)
            {
                error = "已有地图切换正在进行。";
                return false;
            }
            if (!_controller.TryBeginPreparation(requestId, out error)) return false;
            try
            {
                _curtainView.BeginBattlePreparation(background, tint);
                return true;
            }
            catch (Exception exception)
            {
                _controller.TryCancelPreparation(requestId);
                Debug.LogException(exception);
                error = "入战表现暂未准备好。";
                return false;
            }
        }

        public bool OwnsBattlePreparation(string requestId) =>
            _controller.Phase == SceneTransitionPhase.Preparing &&
            _controller.ActiveRequestId == requestId;

        public bool IsBattlePreparationFeedbackComplete(string requestId) =>
            OwnsBattlePreparation(requestId) && _curtainView.IsPreparationFeedbackComplete;

        public void CancelBattlePreparation(string requestId) =>
            _controller.TryCancelPreparation(requestId);

        /// <summary>
        /// Reloads LevelMap through the same async load/Ready handshake as combat.
        /// The optional context preparation runs only after feedback has rendered.
        /// </summary>
        public bool TryReloadMap(Action prepare, out string error, Action<string> onFailed = null)
        {
            if (!isActiveAndEnabled)
            {
                error = "场景切换运行时尚未就绪。";
                return false;
            }
            if (!_mapReload.TrySchedule(_controller.Phase == SceneTransitionPhase.Idle,
                    onFailed, out var request, out error))
                return false;
            try
            {
                _curtainView?.SetCoverCaption("正在前往下一层");
                if (StartCoroutine(ReloadMapAfterFeedback(request, prepare,
                        FindObjectOfType<LevelMapEntryCoordinator>())) == null)
                    throw new InvalidOperationException("无法启动地图切换。");
            }
            catch (Exception exception)
            {
                // No accepted request escaped to the caller. Reject without a
                // callback, just like the synchronous busy check above.
                _mapReload.TryDiscard(request);
                error = exception.Message;
                return false;
            }
            error = null;
            return true;
        }

        private IEnumerator ReloadMapAfterFeedback(MapReloadRequestLifecycle.Request request, Action prepare,
            LevelMapEntryCoordinator sourceEntry)
        {
            string failure = null;
            using (HoldPreparation())
            {
                // Leave the IMGUI/input stack before changing scene ownership.
                yield return null;
                try
                {
                    if (!_mapReload.IsCurrent(request)) yield break;
                    // Another navigation can win during the render handoff.
                    // Do not clear a quick-play Run before checking ownership again.
                    if (!_mapReload.TryPrepareTransition(request,
                            _controller.Phase == SceneTransitionPhase.Idle, prepare, out failure))
                    {
                        if (sourceEntry != null)
                            sourceEntry.RestoreInputAfterRejectedMapReload();
                        Debug.LogWarning("[SceneTransitionRuntime] 地图重载未完成：" + failure);
                    }
                    else
                    {
                        var transition = new SceneTransitionRequest(request.RequestId,
                            SceneNames.LevelMap, new SceneContextBuilder(), 20f);
                        if (!SceneTransitionRuntime.TryBegin(transition, out string beginError))
                            throw new InvalidOperationException(beginError);
                    }
                }
                catch (Exception exception)
                {
                    failure = exception.Message;
                    Debug.LogException(exception);
                }
            }
            // Release the preparation lease and the old request before a
            // failure callback can schedule its replacement.
            if (failure != null) FailMapReload(request, failure);
        }

        private void FailMapReload(MapReloadRequestLifecycle.Request request, string error)
        {
            try { _mapReload.TryFail(request, error); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        /// <summary>Only reloads owned by this runtime consume map-build Ready.</summary>
        public void NotifyMapBuilt()
        {
            string requestId = _mapReload.TransitionRequestId;
            if (string.IsNullOrEmpty(requestId)) return;
            // A victory/result flow may already own the reveal callback before
            // the map's first-frame notification arrives. This null callback is
            // an acknowledgement, not a second reveal or callback replacement.
            if (_controller.ActiveRequestId == requestId &&
                _controller.Phase == SceneTransitionPhase.Revealing)
                return;
            if (!SceneTransitionRuntime.TryMarkDestinationReady(
                    requestId, null, out string error))
                Debug.LogWarning("[SceneTransitionRuntime] 地图就绪握手失败：" + error);
        }

        private void OnTransitionCompleted(SceneTransitionResult result)
        {
            if (result.DestinationScene == SceneNames.LevelMap &&
                string.Equals(result.RequestId, _mapReload.TransitionRequestId, StringComparison.Ordinal))
            {
                // Clear the old recovery mode before its callback can accept a
                // new request. The Core owner ignores unrelated/late results.
                _failedCombatMapReload = false;
            }
            try { _mapReload.TryComplete(result); }
            catch (Exception exception) { Debug.LogException(exception); }
            Scene activeScene = SceneManager.GetActiveScene();
            if (!result.IsSuccess && result.DestinationScene == SceneNames.Combat &&
                activeScene.name == SceneNames.Combat && _failedCombatRecovery == null && !_mapReload.IsBusy &&
                IsFailedCombatRecoveryCurrent(result.RequestId,
                    DungeonRunSessionStore.Current, LevelRunSessionStore.Current, activeScene))
                _failedCombatRecovery = StartCoroutine(RecoverFailedCombatEntry(
                    result.RequestId, DungeonRunSessionStore.Current,
                    LevelRunSessionStore.Current, activeScene));
            TransitionCompleted?.Invoke(result);
        }

        private bool IsFailedCombatRecoveryCurrent(string requestId,
            DungeonRunSession run, LevelRunSession floor, Scene combatScene) =>
            _controller.Phase == SceneTransitionPhase.Idle &&
            string.Equals(SceneContext.Get<string>(SceneContextKeys.TransitionRequestId, null),
                requestId, StringComparison.Ordinal) &&
            ReferenceEquals(run, DungeonRunSessionStore.Current) &&
            ReferenceEquals(floor, LevelRunSessionStore.Current) &&
            combatScene.IsValid() && combatScene.isLoaded &&
            SceneManager.GetActiveScene() == combatScene;

        private IEnumerator RecoverFailedCombatEntry(string failedRequestId,
            DungeonRunSession run, LevelRunSession floor, Scene combatScene)
        {
            if (!_mapReload.TrySchedule(_controller.Phase == SceneTransitionPhase.Idle,
                    null, out var reload, out string scheduleError))
            {
                Debug.LogError("[SceneTransitionRuntime] 无法安排入战失败恢复：" + scheduleError);
                _failedCombatRecovery = null;
                yield break;
            }
            using (HoldPreparation())
            {
                try
                {
                    bool cancelled = false;
                    try
                    {
                        // The strict Ready timeout keeps its opaque mist. Other
                        // failures are covered again before another render can run.
                        if (!_curtainView.IsCovered) _curtainView.SnapCovered();
                        foreach (GameObject root in combatScene.GetRootGameObjects())
                            foreach (CombatSceneBootstrap bootstrap in
                                root.GetComponentsInChildren<CombatSceneBootstrap>(true))
                                bootstrap.ReleaseResidentEncounter();
                        cancelled = true;
                    }
                    catch (Exception exception) { Debug.LogException(exception); }
                    if (!cancelled) yield break;

                    // Let deferred encounter destruction finish. A newer Run,
                    // navigation or transition revokes this recovery here.
                    yield return null;
                    if (!IsFailedCombatRecoveryCurrent(failedRequestId, run, floor, combatScene))
                        yield break;
                    try
                    {
                        DungeonRunSession sourceRun = run;
                        LevelRunSession sourceFloor = floor;
                        bool restored = true;
                        if (DungeonRunSessionStore.HasCombatRollbackCheckpoint)
                        {
                            if (!DungeonRunSessionStore.TryRollbackCombat(out run, out string rollbackError))
                            {
                                Debug.LogError("[SceneTransitionRuntime] 入战失败且战前检查点恢复失败，保留遮幕与检查点：" + rollbackError);
                                restored = false;
                            }
                            else
                                floor = run.CurrentFloor;
                        }
                        else
                            floor?.RecoverInterruptedEncounter();

                        if (restored)
                        {
                            // Rollback replaces aggregate references. Old hidden map
                            // coordinators cannot be reactivated against that new Run.
                            FloorCombatSceneOwner.Current?.ReleaseFloorScope(sourceRun, sourceFloor);
                            if (!_mapReload.TryPrepareTransition(reload,
                                    _controller.Phase == SceneTransitionPhase.Idle, null, out string prepareError))
                            {
                                FailMapReload(reload, prepareError);
                                yield break;
                            }
                            _failedCombatMapReload = true;
                            var request = new SceneTransitionRequest(reload.RequestId,
                                SceneNames.LevelMap, new SceneContextBuilder(), 20f);
                            if (!SceneTransitionRuntime.TryBegin(request, out string beginError))
                            {
                                _failedCombatMapReload = false;
                                FailMapReload(reload, beginError);
                                Debug.LogError("[SceneTransitionRuntime] 战前状态已恢复，返回地图请求被拒绝：" + beginError);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        if (_mapReload.IsCurrent(reload)) _failedCombatMapReload = false;
                        FailMapReload(reload, exception.Message);
                        Debug.LogException(exception);
                    }
                }
                finally
                {
                    // An unstarted recovery may have lost its scene/Run. Never
                    // clear a started request or a replacement accepted later.
                    if (_mapReload.IsCurrent(reload) && _mapReload.TransitionRequestId == null)
                        _mapReload.TryDiscard(reload);
                    _failedCombatRecovery = null;
                }
            }
        }

        /// <summary>
        /// 为下一次进入战斗设置当前楼层地图背景。遮幕直接持有 Sprite 资源，
        /// 不执行屏幕截图、纹理复制或 GPU 读回。
        /// </summary>
        public void SetMapCover(Sprite mapBackground, Color tint)
        {
            _curtainView?.SetCoverSprite(mapBackground, tint);
        }

        /// <summary>
        /// 设置遮幕合上期间显示的文案。撕纸遮幕画的是地图背景图本身，
        /// 没有文案时玩家会误以为仍停在地图上。
        /// </summary>
        public void SetCoverCaption(string caption)
        {
            _curtainView?.SetCoverCaption(caption);
        }

        /// <summary>
        /// 幂等创建常驻转场运行时。返回实例。
        /// </summary>
        public static SceneTransitionRuntimeBootstrap Ensure(string profileContentId = null)
        {
            if (_instance != null) return _instance;

            var go = new GameObject("[SceneTransitionRuntime]");
            _instance = go.AddComponent<SceneTransitionRuntimeBootstrap>();
            DontDestroyOnLoad(go);
            _instance.Initialize(profileContentId);
            return _instance;
        }

        private void Initialize(string profileContentId)
        {
            // Load profile
            SceneTransitionProfile profile = null;
            if (!string.IsNullOrWhiteSpace(profileContentId))
            {
                try
                {
                    profile = SceneTransitionProfileCatalog.Get(profileContentId);
                }
                catch (System.Exception e)
                {
                    Debug.LogError(
                        $"[SceneTransitionRuntime] 无法加载转场配置 {profileContentId}：{e.Message}");
                }
            }
            if (profile == null)
            {
                try
                {
                    profile = SceneTransitionProfileCatalog.Get(
                        "transition.paper-cloud.standard.v1");
                }
                catch
                {
                    profile = ScriptableObject.CreateInstance<SceneTransitionProfile>();
                }
            }

            // Create curtain view
            _curtainView = gameObject.AddComponent<PaperCloudTransitionView>();
            _curtainView.Initialize(profile);

            // Create barriers
            _interactionBarrier = new FlowBarrier();
            _externalModalBarrier = new FlowBarrier();

            // Create load backend
            _loadBackend = new UnitySceneLoadBackend(() => _failedCombatMapReload &&
                _mapReload.TransitionRequestId != null && string.Equals(_mapReload.TransitionRequestId,
                    SceneContext.Get<string>(SceneContextKeys.TransitionRequestId, null),
                    StringComparison.Ordinal));

            // Create controller
            _controller = new SceneTransitionController(
                _curtainView,
                _loadBackend,
                _interactionBarrier);
            _controller.Completed += OnTransitionCompleted;

            // Register
            SceneTransitionRuntime.Register(
                _controller,
                _interactionBarrier,
                _externalModalBarrier);

            // Start ticking
            _tickCoroutine = StartCoroutine(TickLoop());

            Debug.Log("[SceneTransitionRuntime] 转场运行时已初始化。");
        }

        private IEnumerator TickLoop()
        {
            while (true)
            {
                _controller.Tick(Time.unscaledDeltaTime);
                yield return null;
            }
        }

        private void OnDestroy()
        {
            if (_tickCoroutine != null)
                StopCoroutine(_tickCoroutine);
            if (_controller != null)
            {
                _controller.Completed -= OnTransitionCompleted;
                _controller.Stop();
            }
            SceneTransitionRuntime.Unregister(_controller);
            if (_instance == this)
                _instance = null;
        }
    }

    /// <summary>
    /// Unity SceneManager 异步加载后端。实现 ISceneLoadBackend。
    /// </summary>
    public sealed class UnitySceneLoadBackend : ISceneLoadBackend
    {
        private readonly Func<bool> _requiresFreshMapReload;

        public UnitySceneLoadBackend(Func<bool> requiresFreshMapReload = null)
            => _requiresFreshMapReload = requiresFreshMapReload;

        public ISceneLoadHandle BeginLoad(string sceneName)
        {
            if (sceneName == SceneNames.Combat)
            {
                FloorCombatSceneOwner resident = FloorCombatSceneOwner.Current;
                if (resident != null && resident.TryCreateLoadHandle(out ISceneLoadHandle residentHandle))
                    return residentHandle;
                CombatScenePreloader preloader =
                    Object.FindObjectOfType<CombatScenePreloader>();
                if (preloader != null &&
                    preloader.TryCreateLoadHandle(
                        out ISceneLoadHandle preloadedHandle))
                {
                    return preloadedHandle;
                }
            }

            // 返回地图时，若 LevelMap 仍在内存中（仅被隐藏），
            // 直接重新激活而非从零加载，消除场景重建卡顿。
            if (sceneName == SceneNames.LevelMap && _requiresFreshMapReload?.Invoke() != true)
            {
                Scene existing = SceneManager.GetSceneByName(SceneNames.LevelMap);
                if (existing.IsValid() && existing.isLoaded)
                {
                    Scene active = SceneManager.GetActiveScene();
                    if (active.name != SceneNames.LevelMap)
                    {
                        return new HiddenSceneReactivateHandle(existing);
                    }
                }
            }

            var handle = new UnitySceneLoadHandle(sceneName);
            handle.Start();
            return handle;
        }
    }

    /// <summary>Unity 异步场景加载句柄。</summary>
    public sealed class UnitySceneLoadHandle : ISceneLoadHandle
    {
        private readonly string _sceneName;
        private AsyncOperation _operation;
        private bool _activated;
        private bool _done;

        public float Progress => _operation?.progress ?? 0f;
        public bool IsReadyForActivation =>
            _operation != null && _operation.progress >= 0.9f;
        public bool IsDone => _done;
        public string Error { get; private set; }
        public bool IsActivated => _activated;

        public UnitySceneLoadHandle(string sceneName)
        {
            _sceneName = sceneName;
        }

        public void Start()
        {
            _operation = SceneManager.LoadSceneAsync(_sceneName);
            if (_operation == null)
            {
                Error = $"无法加载场景：{_sceneName}";
                _done = true;
                return;
            }
            _operation.allowSceneActivation = false;
        }

        public void Activate()
        {
            if (_operation == null) return;
            _activated = true;
            _operation.allowSceneActivation = true;
            _operation.completed += _ => _done = true;
        }
    }

    /// <summary>
    /// 隐藏场景重新激活句柄。用于 LevelMap 仍在内存中时跳过完整加载，
    /// 将待卸载场景的全部根停用后，才恢复地图根：输入、相机与音频监听器
    /// 在同一调用内交接，卸载等待期间不留两套活动场景基础设施。
    /// 常驻遮幕和 TickLoop 不属于这些场景根，不受交接影响。
    /// </summary>
    public sealed class HiddenSceneReactivateHandle : ISceneLoadHandle
    {
        private readonly Scene _hiddenScene;
        private readonly System.Func<Scene, bool> _tryRetainSource;
        private bool _activated;
        private bool _done;

        public float Progress => IsReadyForActivation ? 1f : 0.9f;
        public bool IsReadyForActivation => !_activated && Error == null;
        public bool IsDone => _done;
        public string Error { get; private set; }

        public HiddenSceneReactivateHandle(Scene hiddenScene)
            : this(hiddenScene, source => FloorCombatSceneOwner.Current != null &&
                FloorCombatSceneOwner.Current.TryRetainForReturn(source)) { }

        public HiddenSceneReactivateHandle(Scene hiddenScene,
            System.Func<Scene, bool> tryRetainSource)
        {
            _hiddenScene = hiddenScene;
            _tryRetainSource = tryRetainSource;
            if (!_hiddenScene.IsValid() || !_hiddenScene.isLoaded)
            {
                Error = "待恢复的隐藏场景尚未加载或已经卸载。";
                _done = true;
            }
        }

        public void Activate()
        {
            if (_activated || Error != null) return;
            _activated = true;

            // 场景可能在句柄创建后被其他导航卸载；不要先关掉当前场景。
            if (!_hiddenScene.IsValid() || !_hiddenScene.isLoaded)
            {
                Error = "待恢复的隐藏场景已经卸载。";
                _done = true;
                return;
            }

            // 记住当前活动场景（战斗），稍后卸载
            Scene combatScene = SceneManager.GetActiveScene();
            bool retained = _tryRetainSource?.Invoke(combatScene) == true;

            // 只禁用 Listener 会留下活动 EventSystem 和相机。源场景已完成
            // 战斗提交且即将卸载，停用全部根统一结束其生命周期；不要永久
            // 改写 Listener.enabled，地图 Listener 保持原有配置即可恢复。
            if (combatScene.IsValid() && combatScene.isLoaded &&
                combatScene != _hiddenScene && !retained)
            {
                GameObject[] combatRoots = combatScene.GetRootGameObjects();
                for (int i = 0; i < combatRoots.Length; i++)
                    combatRoots[i].SetActive(false);
            }

            // 重新激活隐藏地图的根对象
            GameObject[] roots = _hiddenScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                roots[i].SetActive(true);

            // 设为活动场景
            SceneManager.SetActiveScene(_hiddenScene);

            // 卸载战斗场景
            if (combatScene.IsValid() &&
                combatScene.isLoaded &&
                combatScene != _hiddenScene && !retained)
            {
                AsyncOperation unload =
                    SceneManager.UnloadSceneAsync(combatScene);
                if (unload != null)
                    unload.completed += _ => _done = true;
                else
                    _done = true;
            }
            else
            {
                _done = true;
            }
        }
    }
}
