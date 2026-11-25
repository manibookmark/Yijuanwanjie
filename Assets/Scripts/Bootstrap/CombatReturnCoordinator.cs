using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    public interface ICombatReturnNavigator
    {
        bool TryReturnToLevelMap(out string error);
    }

    public interface ICombatReturnPresentationGate
    {
        bool IsBusy { get; }
    }

    /// <summary>
    /// 等待终结卡牌的出牌与实际攻击完成，再允许合拢返图。
    /// 死亡碎屑、余粒子及常驻 AudioManager 的尾音不阻塞返回。
    /// </summary>
    public sealed class CombatReturnPresentationGate :
        ICombatReturnPresentationGate
    {
        private readonly MonoBehaviour _owner;
        private readonly CombatVisualBridge _visualBridge;
        private CombatSceneController _sceneController;

        public CombatReturnPresentationGate(
            MonoBehaviour owner,
            CombatVisualBridge visualBridge)
        {
            _owner = owner;
            _visualBridge = visualBridge;
        }

        public void SetSceneController(CombatSceneController sceneController)
        {
            _sceneController = sceneController;
        }

        public bool IsBusy
        {
            get
            {
                if (_owner == null)
                    return false;

                if (_sceneController != null &&
                    _sceneController.IsCardPresentationActive)
                {
                    return true;
                }

                if (_visualBridge != null &&
                    _visualBridge.HasPendingImpacts)
                {
                    return true;
                }

                return false;
            }
        }
    }

    /// <summary>无转场遮幕地直接切回 LevelMap。</summary>
    public sealed class DirectCombatReturnNavigator : ICombatReturnNavigator
    {
        public bool TryReturnToLevelMap(out string error)
        {
            SceneContext.Clear();
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                SceneNames.LevelMap,
                LoadSceneMode.Single);
            if (operation == null)
            {
                error = "无法直接加载 LevelMap 场景。";
                return false;
            }

            error = null;
            return true;
        }
    }

    /// <summary>
    /// 战斗返回协调器。订阅 OnCombatEnded，等待终结攻击与外部窗口完成后启动合拢，
    /// 提交遭遇结果并切回 LevelMap。尾效与尾音仍可和返图并行。
    /// 无活动遭遇时不导航（独立战斗）。
    /// </summary>
    public sealed class CombatReturnCoordinator : MonoBehaviour
    {
        private CombatController _combat;
        private CombatVisualBridge _visualBridge;
        private FlowBarrier _externalFlowBarrier;
        private ICombatReturnNavigator _returnNavigator;
        private ICombatReturnPresentationGate _presentationGate;
        private bool _resultSubmitted;
        private bool _navigationPending;
        private CombatOutcome? _pendingOutcome;
        private int _earliestEvaluationFrame;
        private bool _returnCoverStarted;
        private Action _runStateChanged;
        private float _nextSubmissionRetryAt;
        private string _lastSubmissionError;

        /// <summary>是否已绑定。</summary>
        public bool IsBound => _combat != null;

        /// <summary>补充绑定 UI 卡牌演出状态；由场景装配在控制器创建后调用。</summary>
        public void SetSceneController(CombatSceneController sceneController)
        {
            if (_presentationGate is CombatReturnPresentationGate runtimeGate)
                runtimeGate.SetSceneController(sceneController);
        }

        /// <summary>绑定战斗控制器、视觉桥和外部窗口屏障。</summary>
        public void Bind(
            CombatController combat,
            CombatVisualBridge visualBridge,
            FlowBarrier externalFlowBarrier = null,
            ICombatReturnNavigator returnNavigator = null,
            ICombatReturnPresentationGate presentationGate = null,
            Action runStateChanged = null)
        {
            _combat = combat;
            _visualBridge = visualBridge;
            _externalFlowBarrier = externalFlowBarrier
                ?? SceneTransitionRuntime.ExternalModalBarrier;
            _returnNavigator = returnNavigator
                ?? new DirectCombatReturnNavigator();
            _presentationGate = presentationGate
                ?? new CombatReturnPresentationGate(this, _visualBridge);
            _runStateChanged = runStateChanged;

            if (_combat != null)
                _combat.OnCombatEnded += OnCombatEnded;

            if (_visualBridge != null)
                _visualBridge.PresentationCompleted += OnPresentationCompleted;
        }

        private void OnCombatEnded(CombatEndedEvent e)
        {
            LevelRunSession session = LevelRunSessionStore.Current;
            if (session == null || !session.ActiveEncounter.HasValue)
            {
                // 无活动遭遇（独立战斗）— 不导航
                return;
            }

            // Defer evaluation by one frame so same-frame subscribers can Hold
            _pendingOutcome = e.Outcome;
            _earliestEvaluationFrame = Time.frameCount + 1;

            // Core may finish before the final projectile arrives. Update owns
            // admission after same-frame impact/reward subscribers have settled.
        }

        private void OnPresentationCompleted()
        {
            if (_pendingOutcome.HasValue && !_resultSubmitted)
            {
                _earliestEvaluationFrame = Time.frameCount + 1;
            }
        }

        private void Update()
        {
            if (!_pendingOutcome.HasValue || _resultSubmitted)
                return;

            // Wait at least one frame after event for other subscribers
            if (Time.frameCount < _earliestEvaluationFrame)
                return;
            if (Time.unscaledTime < _nextSubmissionRetryAt)
                return;

            // 等待外部弹窗（升级、奖励等）关闭
            if (_externalFlowBarrier != null && !_externalFlowBarrier.IsClear)
                return;

            // Do not hide or unload the target before its recorded attack arrives.
            // The runtime gate excludes cosmetic tails and one-shot audio.
            if (_presentationGate != null && _presentationGate.IsBusy)
                return;

            EnsureReturnCoverStarted();

            // 实际攻击已完成；余粒子和尾音仍可与地图返回并行。
            SubmitResult(_pendingOutcome.Value);
        }

        private void EnsureReturnCoverStarted()
        {
            if (_returnCoverStarted || !SceneTransitionRuntime.IsAvailable)
                return;
            _returnCoverStarted =
                SceneTransitionRuntime.TryBeginReturnCover(out _);
        }

        private void SubmitResult(CombatOutcome outcome)
        {
            if (_resultSubmitted) return;

            LevelRunSession session = LevelRunSessionStore.Current;
            if (session == null || !session.ActiveEncounter.HasValue)
                return;
            DungeonRunSession run = DungeonRunSessionStore.Current;
            if (run == null) return;
            if (outcome != CombatOutcome.Victory && outcome != CombatOutcome.Defeat)
            {
                Debug.LogWarning(
                    $"[CombatReturnCoordinator] 不支持的战斗结果：{outcome}。");
                return;
            }

            string beforeAcceptance;
            try { beforeAcceptance = run.Serialize(); }
            catch (Exception exception)
            {
                ReportSubmissionFailure("无法建立提交回滚快照：" + exception.Message);
                return;
            }
            try
            {
                string error;
                bool accepted = outcome == CombatOutcome.Victory
                    ? run.TryCompleteVictory(_combat, out _, out error)
                    : run.TryCompleteDefeat(_combat, out _, out error);
                if (!accepted) throw new InvalidOperationException(error ?? "无法接受战斗结果。");
                if (outcome == CombatOutcome.Defeat && !run.TryConcludeDefeat(out _, out error))
                    throw new InvalidOperationException(error ?? "无法冻结战败结果。");
                // The injected production callback must throw if strict account/Run
                // saving fails. A swallowed write is not an accepted result.
                _runStateChanged?.Invoke();
            }
            catch (Exception exception)
            {
                bool restored = DungeonRunSessionStore.TryRestoreCombatSubmission(beforeAcceptance, out string restoreError);
                ReportSubmissionFailure(
                    "战斗结果提交或保存失败：" + exception.Message +
                    (restored ? string.Empty : "；提交前 Run 恢复失败：" + restoreError));
                return;
            }

            // Only durable success clears the abort checkpoint and opens navigation.
            DungeonRunSessionStore.CommitCombatRollbackCheckpoint();
            _nextSubmissionRetryAt = 0f;
            _lastSubmissionError = null;
            _resultSubmitted = true;
            _navigationPending = true;
        }

        private void ReportSubmissionFailure(string error)
        {
            // Keep the pending result retryable without writing to disk or
            // repeating the same diagnostic every frame while a save is blocked.
            _nextSubmissionRetryAt = Time.unscaledTime + 1f;
            if (string.Equals(_lastSubmissionError, error, StringComparison.Ordinal))
                return;
            _lastSubmissionError = error;
            Debug.LogError("[CombatReturnCoordinator] " + error);
        }

        private void LateUpdate()
        {
            if (_navigationPending)
            {
                _navigationPending = false;
                BeginDirectReturn();
            }
        }

        private void BeginDirectReturn()
        {
            if (SceneTransitionRuntime.IsAvailable)
            {
                string requestId = System.Guid.NewGuid().ToString("N");
                var request = new SceneTransitionRequest(
                    requestId,
                    SceneNames.LevelMap,
                    new SceneContextBuilder(),
                    5f);

                if (SceneTransitionRuntime.TryBegin(request, out string beginError))
                {
                    // 立即标记 Ready，状态机会缓存并在场景激活后揭幕
                    SceneTransitionRuntime.TryMarkDestinationReady(
                        requestId, null, out _);
                    Debug.Log(
                        "[CombatReturnCoordinator] 已发起撕纸返回转场。");
                    return;
                }

                Debug.LogWarning(
                    $"[CombatReturnCoordinator] 转场返回失败：{beginError}，回退直接加载。");
            }

            if (_returnNavigator == null)
            {
                Debug.LogWarning(
                    "[CombatReturnCoordinator] 未配置返回地图导航器。");
                return;
            }

            if (!_returnNavigator.TryReturnToLevelMap(out string error))
            {
                Debug.LogWarning(
                    $"[CombatReturnCoordinator] 直接返回地图失败：{error}");
                return;
            }

            Debug.Log(
                "[CombatReturnCoordinator] 终结攻击已完成，直接返回地图。");
        }

        private void OnDestroy()
        {
            if (_combat != null)
                _combat.OnCombatEnded -= OnCombatEnded;
            if (_visualBridge != null)
                _visualBridge.PresentationCompleted -= OnPresentationCompleted;
        }
    }
}
