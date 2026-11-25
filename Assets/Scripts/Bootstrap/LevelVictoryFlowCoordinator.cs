using System;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Effects.Map;
using WanjieScroll.Meta;
using WanjieScroll.Progression;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 胜利返回流程协调器。回图后提交奖励并编排净化、下一节点或 W7 终局流程。
    /// 奖励在任何获得动画播放前提交并保存。
    /// </summary>
    public sealed class LevelVictoryFlowCoordinator : MonoBehaviour
    {
        private LevelRunSession _session;
        private MapPresentation _presentation;
        private LevelRewardGrantService _rewardService;
        private string _transitionRequestId;
        private bool _processing;
        private CharacterPresentationCoordinator _characterPresenter;
        private DungeonRunSession _dungeonRun;
        private SealChoiceVictoryRewardCoordinator _sealChoiceCoordinator;
        private DungeonRunConclusionCoordinator _conclusionCoordinator;

        /// <summary>普通节点胜利演出完成且返回结果已确认。</summary>
        public event Action VictoryPresentationCompleted;

        /// <summary>绑定依赖。</summary>
        public void Bind(
            LevelRunSession session,
            MapPresentation presentation,
            LevelRewardGrantService rewardService,
            string transitionRequestId,
            CharacterPresentationCoordinator characterPresenter,
            DungeonRunSession dungeonRun = null,
            SealChoiceVictoryRewardCoordinator sealChoiceCoordinator = null,
            DungeonRunConclusionCoordinator conclusionCoordinator = null)
        {
            _session = session;
            _presentation = presentation;
            _rewardService = rewardService;
            _transitionRequestId = transitionRequestId;
            _characterPresenter = characterPresenter ??
                throw new ArgumentNullException(nameof(characterPresenter));
            _dungeonRun = dungeonRun;
            _sealChoiceCoordinator = sealChoiceCoordinator;
            _conclusionCoordinator = conclusionCoordinator;
        }

        /// <summary>更新场景过渡请求标识，用于在绑定后重新指定过渡请求 ID。</summary>
        public void UpdateTransitionRequestId(string transitionRequestId)
        {
            _transitionRequestId = transitionRequestId;
        }

        /// <summary>开始处理胜利返回流程。</summary>
        public void ProcessVictoryReturn()
        {
            if (_processing) return;
            if (_session == null) return;
            if (_conclusionCoordinator != null && _conclusionCoordinator.IsShowing) return;

            if (!_session.TryGetPendingReturn(out LevelEncounterReturn result))
            {
                // The settlement acknowledges the combat return before any
                // presentation. A surviving frozen Run still needs Results.
                if (_dungeonRun != null &&
                    _dungeonRun.TryGetConclusion(out DungeonRunResultSnapshot frozen) &&
                    frozen.Status == DungeonRunResultStatus.Victory)
                {
                    _processing = true;
                    _presentation.SetInteractionEnabled(false);
                    RunAfterDestinationReveal(() => StartCoroutine(RecoverFrozenFinale()));
                }
                return;
            }

            // Only handle Victory
            if (result.Outcome != CombatOutcome.Victory)
                return;

            _processing = true;
            string nodeId = result.NodeContentId;
            var node = _session.FindNode(nodeId);
            bool isFinale = node != null && node.nodeType == LevelNodeType.Finale;
            _presentation.SetInteractionEnabled(false);
            Debug.Log(
                $"[LevelVictoryFlow] ProcessVictoryReturn: node={nodeId}, isFinale={isFinale}");

            if (_session.TryGetPendingVictorySealReward(out _))
            {
                if (_sealChoiceCoordinator == null)
                {
                    Debug.LogError(
                        "[LevelVictoryFlow] 缺少胜利灵印奖励协调器。");
                    _processing = false;
                    _presentation.SetInteractionEnabled(true);
                    MarkDestinationReady();
                    return;
                }
                RunAfterDestinationReveal(() =>
                {
                    if (_sealChoiceCoordinator.TryProcessPending(
                            () =>
                            {
                                if (isFinale)
                                {
                                    StartCoroutine(
                                        AfterReveal(nodeId, true, node));
                                }
                                else
                                {
                                    CompleteOrdinaryVictory(
                                        nodeId,
                                        node,
                                        markDestinationReady: false);
                                }
                            },
                            out string choiceError))
                        return;

                    Debug.LogError(
                        "[LevelVictoryFlow] 无法处理胜利灵印奖励：" +
                        (choiceError ?? "没有待处理奖励。"));
                    _processing = false;
                    _presentation.SetInteractionEnabled(true);
                });
                return;
            }

            if (!isFinale)
            {
                CompleteOrdinaryVictory(
                    nodeId, node, markDestinationReady: true);
            }
            else
            {
                // 揭幕后播放净化和英灵显化，再进入 Results。
                RunAfterDestinationReveal(
                    () => StartCoroutine(AfterReveal(nodeId, isFinale, node)));
            }
        }

        private System.Collections.IEnumerator RecoverFrozenFinale()
        {
            DungeonRunResultSnapshot result = null;
            IReadOnlyList<LevelRewardReceipt> receipts = null;
            string error = null;
            if (_rewardService == null ||
                !_rewardService.TryRecoverSettledVictory(
                    _dungeonRun, out result, out receipts, out error))
            {
                Debug.LogError("[LevelVictoryFlow] 无法恢复已提交的终局结算：" +
                    (error ?? "缺少局外奖励服务。"));
                _processing = false;
                _presentation.SetInteractionEnabled(true);
                yield break;
            }
            // The map already restores completed node states. Resume the
            // frozen unlock receipts and Results without another purification.
            yield return ShowFinaleSequence(receipts, result);
        }

        /// <summary>提交节点奖励，不播放获得动画。</summary>
        private void CommitRewards(LevelNode node)
        {
            IReadOnlyList<LevelRewardGrant> automatic =
                BuildAutomaticRewards(node);
            if (_rewardService == null || automatic.Count == 0)
                return;

            try
            {
                _rewardService.Apply(automatic);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[LevelVictoryFlow] 奖励提交失败：{e.Message}。");
            }
        }

        private void CompleteOrdinaryVictory(
            string nodeId,
            LevelNode node,
            bool markDestinationReady)
        {
            CommitRewards(node);
            bool acknowledged = _session.TryAcknowledgeReturn(nodeId);
            if (!acknowledged)
            {
                Debug.LogError(
                    $"[LevelVictoryFlow] 无法确认胜利返回：{nodeId}。");
                _processing = false;
                _presentation.SetInteractionEnabled(true);
                if (markDestinationReady)
                    MarkDestinationReady();
                return;
            }
            _processing = false;
            VictoryPresentationCompleted?.Invoke();
            _presentation.SetInteractionEnabled(true);
            if (markDestinationReady)
                MarkDestinationReady();
        }

        private void RunAfterDestinationReveal(Action continuation)
        {
            Action guardedContinuation = () =>
            {
                if (this == null) return;
                if (!isActiveAndEnabled)
                {
                    _processing = false;
                    return;
                }
                continuation?.Invoke();
            };
            if (!string.IsNullOrEmpty(_transitionRequestId) &&
                SceneTransitionRuntime.IsAvailable &&
                SceneTransitionRuntime.TryMarkDestinationReady(
                    _transitionRequestId, guardedContinuation, out _))
                return;
            guardedContinuation();
        }

        private void MarkDestinationReady()
        {
            if (!string.IsNullOrEmpty(_transitionRequestId) &&
                SceneTransitionRuntime.IsAvailable)
            {
                SceneTransitionRuntime.TryMarkDestinationReady(
                    _transitionRequestId, null, out _);
            }
        }

        private static IReadOnlyList<LevelRewardGrant>
            BuildAutomaticRewards(LevelNode node)
            => LevelRewardGrantRules.GetAutomaticGrants(node);

        private System.Collections.IEnumerator AfterReveal(
            string nodeId,
            bool isFinale,
            LevelNode node)
        {
            // The complete non-UI transaction commits before the existing
            // purification and character presentation sequence begins.
            IReadOnlyList<LevelRewardReceipt> receipts = null;
            DungeonRunResultSnapshot settledResult = null;
            if (isFinale)
            {
                string settlementError = null;
                if (_rewardService == null || _dungeonRun == null ||
                    !_rewardService.TrySettleDungeon(_dungeonRun, out settledResult, out receipts, out settlementError))
                {
                    Debug.LogError("[LevelVictoryFlow] 无法提交完整终局结算：" +
                        (settlementError ?? "缺少 Dungeon Run 或局外奖励服务。"));
                    _processing = false;
                    _presentation.SetInteractionEnabled(true);
                    yield break;
                }
            }
            else if (_rewardService != null)
            {
                try
                {
                    receipts = _rewardService.Apply(BuildAutomaticRewards(node));
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"[LevelVictoryFlow] 奖励提交失败：{e.Message}。" +
                        "不播放获得动画，不确认返回。");
                    _processing = false;
                    _presentation.SetInteractionEnabled(true);
                    yield break;
                }
            }

            // Play purification
            bool purificationDone = false;
            bool purificationStarted = _presentation.TryPlayVictoryPurification(
                nodeId, () => { purificationDone = true; });
            if (!purificationStarted)
            {
                // Map not built or group missing — do not hang forever
                Debug.LogWarning(
                    $"[LevelVictoryFlow] 净化未能启动（节点 {nodeId} 不可用），跳过净化。");
                purificationDone = true;
            }

            // Wait for purification to complete
            while (!purificationDone)
                yield return null;

            if (!isFinale)
            {
                // Normal node: return to overview, highlight next, acknowledge
                string nextAvailable = FindNextAvailableNode(nodeId);
                _presentation.ReturnToOverview(nextAvailable, () =>
                {
                    bool acknowledged =
                        _session.TryAcknowledgeReturn(nodeId);
                    _processing = false;
                    if (acknowledged)
                        VictoryPresentationCompleted?.Invoke();
                    _presentation.SetInteractionEnabled(true);
                });
            }
            else
            {
                // Finale: show configured crawler presentation, then Results.
                yield return ShowFinaleSequence(receipts, settledResult);
            }
        }

        private System.Collections.IEnumerator ShowFinaleSequence(
            IReadOnlyList<LevelRewardReceipt> receipts,
            DungeonRunResultSnapshot settledResult)
        {
            bool presentedAny = false;
            if (receipts != null)
            {
                foreach (var receipt in receipts)
                {
                    if (receipt.Kind != LevelRewardKind.CrawlerUnlock ||
                        !receipt.NewlyGranted)
                        continue;

                    bool closed = false;
                    if (!_characterPresenter.TryPlay(
                            receipt.PresentationId,
                            () =>
                            {
                                // Character completion restores its host input;
                                // a settled Run must stay blocked until Results.
                                _presentation.SetInteractionEnabled(false);
                                closed = true;
                            }))
                    {
                        Debug.LogError(
                            "[LevelVictoryFlow] 无法播放英灵演出：" +
                            $"{receipt.PresentationId}（{receipt.TargetContentId}）。");
                        continue;
                    }

                    presentedAny = true;
                    while (!closed)
                        yield return null;
                }
            }

            if (!presentedAny)
            {
                Debug.Log(
                    "[LevelVictoryFlow] 终局没有新的英灵解锁演出，将进入结算页。");
            }

            _processing = false;
            string conclusionError = null;
            if (_conclusionCoordinator == null ||
                !_conclusionCoordinator.TryShowVictoryResults(
                    settledResult.Rewards, out conclusionError))
            {
                Debug.LogError(
                    "[LevelVictoryFlow] 无法显示终局结算：" +
                    (conclusionError ?? "未配置结算协调器。"));
                _presentation.SetInteractionEnabled(true);
            }
        }

        private string FindNextAvailableNode(string clearedNodeId)
        {
            foreach (var node in _session.Nodes)
            {
                if (node.contentId == clearedNodeId)
                    continue;
                if (_session.GetNodeState(node.contentId) ==
                    LevelNodeViewState.Available)
                    return node.contentId;
            }
            return null;
        }

    }
}
