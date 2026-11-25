using System;
using System.Collections;

namespace WanjieScroll.Core
{
    /// <summary>场景转场阶段。</summary>
    public enum SceneTransitionPhase
    {
        Idle,
        Covering,
        Loading,
        Activating,
        WaitingForDestinationReady,
        Revealing,
        Failed,
        // Appended to preserve the existing phase values.
        Preparing
    }

    /// <summary>场景转场结果。</summary>
    public readonly struct SceneTransitionResult
    {
        public string RequestId { get; }
        public bool IsSuccess { get; }
        public string Error { get; }
        public string DestinationScene { get; }

        public SceneTransitionResult(string requestId, bool isSuccess, string error,
            string destinationScene = null)
        {
            RequestId = requestId;
            IsSuccess = isSuccess;
            Error = error;
            DestinationScene = destinationScene;
        }
    }

    /// <summary>场景转场请求。</summary>
    public readonly struct SceneTransitionRequest
    {
        public string RequestId { get; }
        public string DestinationScene { get; }
        public SceneContextBuilder Context { get; }
        public float DestinationReadyTimeoutSeconds { get; }

        public SceneTransitionRequest(
            string requestId,
            string destinationScene,
            SceneContextBuilder context,
            float destinationReadyTimeoutSeconds)
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            DestinationScene = destinationScene ?? throw new ArgumentNullException(nameof(destinationScene));
            Context = context;
            DestinationReadyTimeoutSeconds = destinationReadyTimeoutSeconds;
        }
    }

    /// <summary>场景上下文键常量。</summary>
    public static class SceneContextKeys
    {
        public const string TransitionRequestId = "sceneTransitionRequestId";
        public const string EncounterNodeContentId = "encounterNodeContentId";
        public const string EncounterRunId = "encounterRunId";
        public const string EncounterScenarioId = "encounterScenarioId";
    }

    /// <summary>
    /// 遮幕抽象。Core 定义接口，Effects 实现。
    /// 方法是非阻塞的：Cover()/Reveal() 启动动画后立即返回，
    /// 调用方通过 IsCovered/IsRevealed 轮询动画是否完成。
    /// </summary>
    public interface ISceneTransitionCurtain
    {
        /// <summary>遮幕是否已完成覆盖。</summary>
        bool IsCovered { get; }

        /// <summary>遮幕是否已完成揭开（回到不可见状态）。</summary>
        bool IsRevealed { get; }

        /// <summary>启动覆盖动画。非阻塞，通过 IsCovered 轮询完成。
        /// 若遮幕已处于 Covering 或 Covered 状态则忽略调用。</summary>
        void Cover();

        /// <summary>启动带合拢动画的覆盖（面板从两侧滑入）。非阻塞。</summary>
        void CoverAnimated();

        /// <summary>启动揭开动画。非阻塞，通过 IsRevealed 轮询完成。</summary>
        void Reveal();

        /// <summary>瞬间覆盖，无动画。用于失败恢复或初始化。</summary>
        void SnapCovered();

        /// <summary>瞬间揭开，无动画。用于失败恢复。</summary>
        void SnapRevealed();
    }

    /// <summary>场景加载后端抽象。</summary>
    public interface ISceneLoadBackend
    {
        ISceneLoadHandle BeginLoad(string sceneName);
    }

    /// <summary>场景加载句柄。</summary>
    public interface ISceneLoadHandle
    {
        float Progress { get; }
        bool IsReadyForActivation { get; }
        bool IsDone { get; }
        string Error { get; }
        void Activate();
    }
}
