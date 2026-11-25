using System;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 场景转场控制器。管理遮幕覆盖、异步加载、激活门、Ready 握手和失败恢复。
    /// Core 只依赖抽象接口，不引用 Effects/UI/Bootstrap。
    /// 状态机通过 <see cref="Tick"/> 驱动，可由 MonoBehaviour Update 或测试直接调用。
    /// </summary>
    public sealed class SceneTransitionController
    {
        private readonly ISceneTransitionCurtain _curtain;
        private readonly ISceneLoadBackend _loadBackend;
        private readonly FlowBarrier _interactionBarrier;

        private SceneTransitionPhase _phase = SceneTransitionPhase.Idle;
        private string _activeRequestId;
        private ISceneLoadHandle _activeHandle;
        private IDisposable _interactionLease;
        private Action _pendingRevealCallback;
        private float _phaseTimer;
        private SceneTransitionRequest _activeRequest;
        private bool _requiresDestinationReady;

        // Ready 提前到达缓存
        private string _cachedReadyRequestId;
        private Action _cachedReadyCallback;

        /// <summary>当前转场阶段。</summary>
        public SceneTransitionPhase Phase => _phase;

        /// <summary>当前活跃请求 ID（无请求时为 null）。</summary>
        public string ActiveRequestId => _activeRequestId;
        public string DestinationScene => _activeRequest.DestinationScene;

        /// <summary>转场完成（成功或失败）时触发。</summary>
        public event Action<SceneTransitionResult> Completed;

        public SceneTransitionController(
            ISceneTransitionCurtain curtain,
            ISceneLoadBackend loadBackend,
            FlowBarrier interactionBarrier)
        {
            _curtain = curtain ?? throw new ArgumentNullException(nameof(curtain));
            _loadBackend = loadBackend ?? throw new ArgumentNullException(nameof(loadBackend));
            _interactionBarrier = interactionBarrier ?? throw new ArgumentNullException(nameof(interactionBarrier));
        }

        /// <summary>
        /// 预启动合拢动画（返回转场用）。仅在 Idle 时允许调用；
        /// 后续 TryBegin 中的 Cover() 会因遮幕已在 Covering/Covered 而跳过。
        /// </summary>
        public bool TryBeginAnimatedCover(out string error)
        {
            if (_phase != SceneTransitionPhase.Idle)
            {
                error = $"转场进行中（{_phase}），不能预启动合拢动画。";
                return false;
            }

            _curtain.CoverAnimated();
            error = null;
            return true;
        }

        /// <summary>
        /// Reserves this request before its domain checkpoint is constructed.
        /// Presentation may respond immediately; no context, scene load or
        /// destination activation is allowed until TryBegin commits the same ID.
        /// </summary>
        public bool TryBeginPreparation(string requestId, out string error)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                error = "RequestId 不能为空。";
                return false;
            }
            if (_phase != SceneTransitionPhase.Idle && _phase != SceneTransitionPhase.Failed)
            {
                error = $"转场进行中（{_phase}），不能开始入场准备。";
                return false;
            }
            _activeRequestId = requestId;
            _phase = SceneTransitionPhase.Preparing;
            _phaseTimer = 0f;
            _interactionLease = _interactionBarrier.Hold("scene-preparation");
            error = null;
            return true;
        }

        /// <summary>Only the pending request may cancel its pre-load preparation.</summary>
        public bool TryCancelPreparation(string requestId)
        {
            if (_phase != SceneTransitionPhase.Preparing || requestId != _activeRequestId)
                return false;
            _curtain.SnapRevealed();
            CompleteTransition(false, "入场准备已取消。");
            return true;
        }

        /// <summary>
        /// 开始一次转场。返回 false 时 error 包含拒绝原因。
        /// </summary>
        public bool TryBegin(SceneTransitionRequest request, out string error)
        {
            bool prepared = _phase == SceneTransitionPhase.Preparing &&
                request.RequestId == _activeRequestId;
            if (_phase != SceneTransitionPhase.Idle && _phase != SceneTransitionPhase.Failed && !prepared)
            {
                error = $"转场进行中（{_phase}），不能开始新请求。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                error = "RequestId 不能为空。";
                return false;
            }

            error = null;
            _activeRequestId = request.RequestId;
            _activeRequest = request;
            _requiresDestinationReady = prepared;
            _phase = SceneTransitionPhase.Covering;
            _phaseTimer = 0f;

            // Clear any cached Ready from a previous transition
            _cachedReadyRequestId = null;
            _cachedReadyCallback = null;

            // Apply context before loading
            request.Context?.Apply();
            SceneContext.Set(SceneContextKeys.TransitionRequestId, request.RequestId);

            // Hold interaction lock
            _interactionLease ??= _interactionBarrier.Hold("scene-transition");

            // Start cover animation (non-blocking)
            _curtain.Cover();
            return true;
        }

        /// <summary>
        /// 目标场景声明 Ready。requestId 必须匹配当前活跃请求。
        /// 如果当前尚未到达 WaitingForDestinationReady 阶段，会缓存该请求，
        /// 等到达该阶段后自动处理。
        /// </summary>
        public bool TryMarkDestinationReady(
            string requestId,
            Action onRevealCompleted,
            out string error)
        {
            if (requestId != _activeRequestId)
            {
                error = $"RequestId 不匹配：期望 {_activeRequestId}，收到 {requestId}。";
                return false;
            }

            error = null;

            if (_phase == SceneTransitionPhase.WaitingForDestinationReady)
            {
                // Already waiting — proceed to reveal
                _pendingRevealCallback = onRevealCompleted;
                _phase = SceneTransitionPhase.Revealing;
                _phaseTimer = 0f;
                _curtain.Reveal();
            }
            else if (_phase == SceneTransitionPhase.Covering ||
                     _phase == SceneTransitionPhase.Loading ||
                     _phase == SceneTransitionPhase.Activating)
            {
                // Arrived early — cache for later
                _cachedReadyRequestId = requestId;
                _cachedReadyCallback = onRevealCompleted;
            }
            else
            {
                error = $"当前阶段 {_phase} 不接受 Ready。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 推进状态机。由 MonoBehaviour Update 或测试调用。
        /// </summary>
        public void Tick(float deltaTime)
        {
            switch (_phase)
            {
                case SceneTransitionPhase.Preparing:
                    // No load starts while visual feedback and domain admission
                    // are pending. A lost caller cannot retain input forever.
                    _phaseTimer += deltaTime;
                    if (_phaseTimer > 10f)
                        FailTransition("入场准备超时。");
                    break;

                case SceneTransitionPhase.Covering:
                    // Start loading immediately — parallel with cover animation
                    if (_activeHandle == null)
                    {
                        _activeHandle = _loadBackend.BeginLoad(
                            _activeRequest.DestinationScene);
                    }

                    // Check for early load errors
                    if (!string.IsNullOrEmpty(_activeHandle.Error))
                    {
                        FailTransition(_activeHandle.Error);
                        break;
                    }

                    // Wait for cover to complete
                    if (!_curtain.IsCovered)
                    {
                        _phaseTimer += deltaTime;
                        if (_phaseTimer > 10f)
                            FailTransition("遮幕覆盖超时。");
                        break;
                    }

                    // Cover complete — check if load is also ready
                    if (_activeHandle.IsReadyForActivation)
                    {
                        _phase = SceneTransitionPhase.Activating;
                        _activeHandle.Activate();
                        _phaseTimer = 0f;
                    }
                    else
                    {
                        // Cover done but load not ready — wait in Loading
                        _phase = SceneTransitionPhase.Loading;
                        _phaseTimer = 0f;
                    }
                    break;

                case SceneTransitionPhase.Loading:
                    if (!string.IsNullOrEmpty(_activeHandle.Error))
                    {
                        FailTransition(_activeHandle.Error);
                    }
                    else if (_activeHandle.IsReadyForActivation)
                    {
                        _phase = SceneTransitionPhase.Activating;
                        _activeHandle.Activate();
                        _phaseTimer = 0f;
                    }
                    else
                    {
                        _phaseTimer += deltaTime;
                        if (_phaseTimer > 30f)
                            FailTransition("场景加载超时。");
                    }
                    break;

                case SceneTransitionPhase.Activating:
                    if (!string.IsNullOrEmpty(_activeHandle.Error))
                    {
                        FailTransition(_activeHandle.Error);
                    }
                    else if (_activeHandle.IsDone)
                    {
                        _phase = SceneTransitionPhase.WaitingForDestinationReady;
                        _phaseTimer = 0f;

                        // Check if Ready was cached during loading
                        if (_cachedReadyRequestId == _activeRequestId)
                        {
                            _pendingRevealCallback = _cachedReadyCallback;
                            _cachedReadyRequestId = null;
                            _cachedReadyCallback = null;
                            _phase = SceneTransitionPhase.Revealing;
                            _curtain.Reveal();
                        }
                    }
                    else
                    {
                        _phaseTimer += deltaTime;
                        if (_phaseTimer > 10f)
                            FailTransition("场景激活超时。");
                    }
                    break;

                case SceneTransitionPhase.WaitingForDestinationReady:
                    _phaseTimer += deltaTime;
                    if (_phaseTimer > _activeRequest.DestinationReadyTimeoutSeconds)
                    {
                        if (_requiresDestinationReady)
                        {
                            // The battle mist must not expose a partial scene or
                            // report success without its real entry callback.
                            FailTransition("目标战斗准备超时。", keepCovered: true);
                            break;
                        }
                        Debug.LogError(
                            "[SceneTransition] 目标场景 Ready 超时，" +
                            "执行 failsafe 揭幕。");
                        _phase = SceneTransitionPhase.Revealing;
                        _phaseTimer = 0f;
                        _curtain.Reveal();
                    }
                    break;

                case SceneTransitionPhase.Revealing:
                    // Wait for curtain reveal animation to complete
                    if (_curtain.IsRevealed)
                    {
                        _pendingRevealCallback?.Invoke();
                        _pendingRevealCallback = null;
                        CompleteTransition(true, null);
                    }
                    else
                    {
                        _phaseTimer += deltaTime;
                        if (_phaseTimer > 10f)
                        {
                            // Failsafe: force complete even if reveal didn't finish
                            Debug.LogWarning(
                                "[SceneTransition] 遮幕揭开超时，强制完成。");
                            _pendingRevealCallback?.Invoke();
                            _pendingRevealCallback = null;
                            CompleteTransition(true, null);
                        }
                    }
                    break;
            }
        }

        private void CompleteTransition(bool success, string error)
        {
            var result = new SceneTransitionResult(_activeRequestId, success, error,
                _activeRequest.DestinationScene);

            _interactionLease?.Dispose();
            _interactionLease = null;
            _pendingRevealCallback = null;
            _activeRequestId = null;
            _activeHandle = null;
            _activeRequest = default;
            _requiresDestinationReady = false;
            _phase = SceneTransitionPhase.Idle;
            _phaseTimer = 0f;
            _cachedReadyRequestId = null;
            _cachedReadyCallback = null;

            Completed?.Invoke(result);
        }

        private void FailTransition(string error, bool keepCovered = false)
        {
            Debug.LogError($"[SceneTransition] 转场失败：{error}");

            // Reveal source scene (snap to covered then reveal)
            if (keepCovered) _curtain.SnapCovered();
            else _curtain.SnapRevealed();

            var result = new SceneTransitionResult(_activeRequestId, false, error,
                _activeRequest.DestinationScene);

            _interactionLease?.Dispose();
            _interactionLease = null;
            _pendingRevealCallback = null;
            _activeRequestId = null;
            _activeHandle = null;
            _activeRequest = default;
            _requiresDestinationReady = false;
            _phase = SceneTransitionPhase.Idle;
            _phaseTimer = 0f;
            _cachedReadyRequestId = null;
            _cachedReadyCallback = null;

            Completed?.Invoke(result);
        }

        /// <summary>停止并重置（测试/清理用）。</summary>
        public void Stop()
        {
            _curtain.SnapRevealed();
            _interactionLease?.Dispose();
            _interactionLease = null;
            _pendingRevealCallback = null;
            _phase = SceneTransitionPhase.Idle;
            _activeRequestId = null;
            _activeHandle = null;
            _activeRequest = default;
            _requiresDestinationReady = false;
            _phaseTimer = 0f;
            _cachedReadyRequestId = null;
            _cachedReadyCallback = null;
        }
    }
}
