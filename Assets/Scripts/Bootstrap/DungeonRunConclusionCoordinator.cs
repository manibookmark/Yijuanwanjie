using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Overlays;

namespace WanjieScroll.Bootstrap
{
    public delegate bool DungeonRunRetryHandler(out string error);
    public delegate bool DungeonRunContinueHandler(out string error);

    /// <summary>
    /// 整局结束流程的唯一协调器：冻结并保存权威快照、显示共用 Results，随后按
    /// 类型化玩家意图重开或离场。它不解析 Run 内部数据，也不构建具体 UI。
    /// 重试准备与保存检查点归 DungeonRunResultPreparation；本类只衔接既有
    /// 结果页输入租约和场景完成/失败通知，不持有另一份 Run 准备状态。
    /// </summary>
    public sealed class DungeonRunConclusionCoordinator : MonoBehaviour
    {
        private DungeonRunSession _run;
        private RunSummaryPanel _panel;
        private FlowDestinationRouter _router;
        private Action _persistActiveRun;
        private Action<bool> _setMapInteractionEnabled;
        private DungeonRunResultSnapshot _activeResult;
        private bool _handlingAction;
        private DungeonRunRetryHandler _tryRestart;
        private DungeonRunContinueHandler _tryContinue;
        private DungeonRunResultPreparation _navigationPreparation;
        private string _navigationHint;

        public bool IsShowing => _activeResult != null &&
                                 _panel != null &&
                                 _panel.IsOpen;

        public void Bind(
            DungeonRunSession run,
            RunSummaryPanel panel,
            FlowDestinationRouter router,
            Action persistActiveRun,
            Action<bool> setMapInteractionEnabled,
            DungeonRunRetryHandler tryRestart = null,
            DungeonRunContinueHandler tryContinue = null,
            string navigationHint = null)
        {
            if (_panel != null)
                _panel.ActionRequested -= OnActionRequested;
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _persistActiveRun = persistActiveRun;
            _setMapInteractionEnabled = setMapInteractionEnabled;
            _tryRestart = tryRestart;
            _tryContinue = tryContinue;
            _navigationPreparation = new DungeonRunResultPreparation(run);
            _navigationHint = navigationHint;
            _panel.ActionRequested += OnActionRequested;
        }

        /// <summary>冻结并显示终层胜利结果；重复进入恢复同一快照。</summary>
        public bool TryShowVictory(
            IReadOnlyList<LevelRewardReceipt> rewards,
            out string error)
        {
            if (!_run.TryConcludeVictory(
                    rewards,
                    out DungeonRunResultSnapshot snapshot,
                    out error))
                return false;
            return TryShow(snapshot, out error);
        }

        /// <summary>冻结并显示已经按总体解锁规则展开的终层胜利结果。</summary>
        public bool TryShowVictoryResults(
            IReadOnlyList<DungeonRunRewardResult> rewards,
            out string error)
        {
            if (!_run.TryConcludeVictoryResults(
                    rewards,
                    out DungeonRunResultSnapshot snapshot,
                    out error))
            {
                return false;
            }
            return TryShow(snapshot, out error);
        }

        /// <summary>显示战斗场景已冻结的失败结果；旧存档可在此补冻结。</summary>
        public bool TryShowDefeat(out string error)
        {
            if (!_run.TryGetConclusion(out DungeonRunResultSnapshot snapshot))
            {
                if (!_run.TryConcludeDefeat(out snapshot, out error))
                    return false;
            }
            if (snapshot.Status != DungeonRunResultStatus.Defeat)
            {
                error = $"待显示结果不是战败：{snapshot.Status}。";
                return false;
            }
            return TryShow(snapshot, out error);
        }

        private bool TryShow(
            DungeonRunResultSnapshot snapshot,
            out string error)
        {
            if (_panel == null)
            {
                error = "结算面板尚未绑定。";
                return false;
            }
            try
            {
                _persistActiveRun?.Invoke();
                _activeResult = snapshot;
                _handlingAction = false;
                _setMapInteractionEnabled?.Invoke(false);
                // 永久解锁已在快照中发放，本页以文字清单与详情承接回执。
                _panel.Show(snapshot,
                    _tryContinue != null && !DemoReleasePolicy.IsDemoFinale(snapshot.DungeonContentId),
                    _navigationHint);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                _activeResult = null;
                _setMapInteractionEnabled?.Invoke(true);
                error = $"显示 Run 结算失败：{exception.Message}";
                return false;
            }
        }

        private void OnActionRequested(DungeonRunResultAction action)
        {
            if (_handlingAction || _activeResult == null)
                return;
            _handlingAction = true;
            if (action == DungeonRunResultAction.ContinueDungeon)
            {
                HandleContinue();
                return;
            }
            if (action == DungeonRunResultAction.RetryDungeon)
            {
                HandleRetry();
                return;
            }
            if (action == DungeonRunResultAction.ReturnToHub)
            {
                HandleReturnToHub();
                return;
            }
            RecoverFromActionFailure($"不支持的结算操作：{action}。");
        }

        private void HandleContinue()
        {
            if (_activeResult.Status != DungeonRunResultStatus.Victory || _tryContinue == null ||
                DemoReleasePolicy.IsDemoFinale(_activeResult.DungeonContentId))
            {
                RecoverFromActionFailure("当前结算不可进入下一关。");
                return;
            }
            BeginMapNavigation(DungeonRunResultAction.ContinueDungeon,
                (out string error) => _tryContinue(out error), null);
        }

        private void HandleRetry()
        {
            if (_activeResult.Status != DungeonRunResultStatus.Defeat)
            {
                RecoverFromActionFailure("只有战败结算可以重新挑战。");
                return;
            }
            BeginMapNavigation(DungeonRunResultAction.RetryDungeon,
                (out string error) => _tryRestart != null
                    ? _tryRestart(out error)
                    : DungeonRunSessionStore.TryRestartAfterDefeat(out _, out error),
                _persistActiveRun);
        }

        // The scene request owns loading; Dungeon owns the prepared Run/checkpoint.
        // Results remains open with its existing click lease until source unload.
        // A rejected/failed request restores that same page, and retry reuses the
        // prepared Run instead of creating or granting another exploration.
        private void BeginMapNavigation(DungeonRunResultAction action,
            DungeonRunNavigationPrepareHandler prepare, Action persistPrepared)
        {
            string playerMessage = action == DungeonRunResultAction.ContinueDungeon
                ? "下一关暂时无法进入，请返回神域查看解锁状态。"
                : "暂时无法重新挑战，请返回神域后再试。";
            try
            {
                bool accepted = SceneTransitionRuntimeBootstrap.Ensure().TryReloadMap(
                    () =>
                    {
                        if (this == null || !isActiveAndEnabled || _activeResult == null)
                            throw new OperationCanceledException("结算页面已关闭。");
                        Action persist = persistPrepared == null ? null : (Action)(() =>
                        {
                            playerMessage = "进度未能保存，请稍后重试。";
                            persistPrepared();
                        });
                        if (!_navigationPreparation.TryPrepare(action, prepare,
                                persist, out string preparationError))
                        {
                            playerMessage = preparationError;
                            throw new InvalidOperationException(preparationError);
                        }
                        playerMessage = "场景加载失败，请重试或返回神域。";
                        WanjieScroll.Core.SceneContext.Clear();
                    },
                    out string error,
                    failure =>
                    {
                        if (this != null && isActiveAndEnabled)
                            RecoverFromActionFailure(failure, playerMessage);
                    });
                if (!accepted)
                    RecoverFromActionFailure(error, "正在切换场景，请稍后重试。");
            }
            catch (Exception exception)
            {
                RecoverFromActionFailure(exception.Message, playerMessage);
            }
        }

        private void HandleReturnToHub()
        {
            try
            {
                if (!_router.TryRoute(FlowDestinationIds.MetaHub, out string error))
                {
                    RecoverFromActionFailure(error, "暂时无法返回神域，请稍后重试。");
                    return;
                }
                _panel.Close();
                _activeResult = null;
            }
            catch (Exception exception)
            {
                RecoverFromActionFailure($"返回神域失败：{exception.Message}",
                    "暂时无法返回神域，请稍后重试。");
            }
        }

        private void RecoverFromActionFailure(string error, string playerMessage = null)
        {
            Debug.LogError("[DungeonRunConclusion] " +
                           (error ?? "未知结算操作失败。"));
            _handlingAction = false;
            _panel?.RecoverFromActionFailure(playerMessage);
        }

        private void OnDestroy()
        {
            if (_panel != null)
                _panel.ActionRequested -= OnActionRequested;
        }
    }
}
