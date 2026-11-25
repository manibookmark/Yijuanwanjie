using System;
using UnityEngine;

namespace WanjieScroll.Core
{
    /// <summary>
    /// 跨场景服务注册和最小调用入口。
    /// Bootstrap 注册具体实现后，业务代码通过此静态入口发起转场。
    /// </summary>
    public static class SceneTransitionRuntime
    {
        private static SceneTransitionController _controller;
        private static FlowBarrier _interactionBarrier;
        private static FlowBarrier _externalModalBarrier;

        /// <summary>转场运行时是否可用。</summary>
        public static bool IsAvailable => _controller != null;

        /// <summary>Read-only ownership check for a multi-frame destination build.
        /// A stopped, replaced or already revealed request cannot finish an old build.</summary>
        public static bool IsDestinationBuildCurrent(string requestId, string destinationScene)
        {
            if (_controller == null || string.IsNullOrWhiteSpace(requestId) ||
                _controller.ActiveRequestId != requestId ||
                _controller.DestinationScene != destinationScene) return false;
            SceneTransitionPhase phase = _controller.Phase;
            return phase == SceneTransitionPhase.Covering || phase == SceneTransitionPhase.Loading ||
                phase == SceneTransitionPhase.Activating || phase == SceneTransitionPhase.WaitingForDestinationReady;
        }

        /// <summary>转场交互锁屏障。</summary>
        public static FlowBarrier InteractionBarrier => _interactionBarrier;

        /// <summary>外部窗口（升级、奖励等）屏障。</summary>
        public static FlowBarrier ExternalModalBarrier => _externalModalBarrier;

        /// <summary>注册控制器和屏障。</summary>
        public static void Register(
            SceneTransitionController controller,
            FlowBarrier interactionBarrier,
            FlowBarrier externalModalBarrier)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _interactionBarrier = interactionBarrier ?? throw new ArgumentNullException(nameof(interactionBarrier));
            _externalModalBarrier = externalModalBarrier ?? throw new ArgumentNullException(nameof(externalModalBarrier));
        }

        /// <summary>解除注册。</summary>
        public static void Unregister(SceneTransitionController controller)
        {
            if (_controller != controller) return;
            _controller = null;
            _interactionBarrier = null;
            _externalModalBarrier = null;
        }

        /// <summary>测试用：完全清除注册。</summary>
        public static void UnregisterForTests()
        {
            _controller = null;
            _interactionBarrier = null;
            _externalModalBarrier = null;
        }

        /// <summary>预启动合拢动画（返回转场用）。仅 Idle 时可用。</summary>
        public static bool TryBeginReturnCover(out string error)
        {
            if (_controller == null)
            {
                error = "场景转场运行时未注册。";
                return false;
            }
            return _controller.TryBeginAnimatedCover(out error);
        }

        /// <summary>发起转场。返回 false 时 error 包含拒绝原因。</summary>
        public static bool TryBegin(SceneTransitionRequest request, out string error)
        {
            if (_controller == null)
            {
                error = "场景转场运行时未注册。";
                return false;
            }
            return _controller.TryBegin(request, out error);
        }

        /// <summary>标记目标场景 Ready。</summary>
        public static bool TryMarkDestinationReady(
            string requestId,
            Action onRevealCompleted,
            out string error)
        {
            if (_controller == null)
            {
                error = "场景转场运行时未注册。";
                return false;
            }
            return _controller.TryMarkDestinationReady(requestId, onRevealCompleted, out error);
        }
    }
}
