using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 层主固定灵印奖励的“选印 / 钤印 / 化为灵玉”表现编排。规则提交全部
    /// 委托给 Dungeon 事务；本类只在候选页、处理页、宿主页和落印动画之间切换。
    /// </summary>
    public sealed class SealChoiceVictoryRewardCoordinator : MonoBehaviour
    {
        private ISealChoiceVictoryRewardRuntime _runtime;
        private SealInteractionEffectHandler _sealHandler;
        private SealAssignmentCoordinator _assignmentCoordinator;
        private LevelMapJadeRewardLedger _jadeLedger;
        private Action _onCompleted;
        private Canvas _canvas;
        private RewardChoiceOverlay _sealChoiceOverlay;
        private RewardFeedbackPresenter _rewardRevealOverlay;
        private LingyinConfig _activeSeal;
        private List<CardConfig> _activeCandidates;
        private LevelVictorySealRewardOffer _activeOffer;
        private bool _processing;
        private bool _actionPending;

        public bool IsProcessing => _processing;

        internal void Bind(
            ISealChoiceVictoryRewardRuntime runtime,
            SealInteractionEffectHandler sealHandler,
            SealAssignmentCoordinator assignmentCoordinator,
            LevelMapJadeRewardLedger jadeLedger)
        {
            _runtime = runtime ??
                throw new ArgumentNullException(nameof(runtime));
            _sealHandler = sealHandler ??
                throw new ArgumentNullException(nameof(sealHandler));
            _assignmentCoordinator = assignmentCoordinator ??
                throw new ArgumentNullException(
                    nameof(assignmentCoordinator));
            _jadeLedger = jadeLedger ??
                throw new ArgumentNullException(nameof(jadeLedger));
        }

        /// <summary>若有待处理奖励则接管流程；没有时返回 false。</summary>
        public bool TryProcessPending(
            Action onCompleted,
            out string error)
        {
            error = null;
            if (_processing)
            {
                error = "胜利灵印奖励已经在处理中。";
                return false;
            }
            if (_runtime == null ||
                !_runtime.TryGetPending(
                    out LevelVictorySealRewardOffer offer))
                return false;

            _processing = true;
            _actionPending = false;
            _onCompleted = onCompleted;
            _activeOffer = offer;
            bool started = offer.Phase switch
            {
                LevelVictorySealRewardPhase.ChoosingSeal =>
                    TryShowSealChoice(offer, out error),
                LevelVictorySealRewardPhase.AssigningSeal =>
                    TryShowAssignment(offer, out error),
                _ => offer.SealContentIds != null && offer.SealContentIds.Count > 1
                    ? TryShowAssignment(offer, out error)
                    : TryShowDisposition(offer, out error)
            };
            if (started)
                return true;

            _processing = false;
            _onCompleted = null;
            return false;
        }

        /// <summary>
        /// 多印奖励先显示所有由 Dungeon 状态机持有的固定候选；这里不生成
        /// 候选、不写入选择，只把精确 ID 回交给窄运行时接口。
        /// </summary>
        private bool TryShowSealChoice(
            LevelVictorySealRewardOffer offer,
            out string error)
        {
            error = null;
            if (offer.SealContentIds == null || offer.SealContentIds.Count == 0)
            {
                error = "胜利灵印奖励缺少可选候选。";
                return false;
            }

            var items = new List<RewardFeedbackItem>(
                offer.SealContentIds.Count);
            for (int i = 0; i < offer.SealContentIds.Count; i++)
            {
                string sealContentId = offer.SealContentIds[i];
                if (!LingyinCatalog.TryGet(
                        sealContentId,
                        out LingyinConfig seal) || seal == null)
                {
                    error = "找不到胜利灵印内容：" + sealContentId + "。";
                    return false;
                }
                if (!seal.IsValid(out error))
                    return false;
                items.Add(RewardFeedbackItem.FromSeal(seal));
            }

            _activeOffer = offer;
            _actionPending = false;
            var actions = new List<RewardUtilityAction>();
            if (offer.AllowsCashOut)
                actions.Add(new RewardUtilityAction(RewardUtilityActionKind.CashOut,
                    $"化为灵玉 · +{_jadeLedger.PreviewCashOut()}", OnCashOutSelected));
            _sealChoiceOverlay ??=
                gameObject.AddComponent<RewardChoiceOverlay>();
            _sealChoiceOverlay.Show(
                EnsureCanvas(),
                "选择灵印",
                items,
                actions: actions,
                onSelected: index => OnSealSelected(offer, index));
            GlobalButtonAudioRuntime.BindHierarchyNow(_sealChoiceOverlay.Root);
            return true;
        }

        private bool TryShowDisposition(
            LevelVictorySealRewardOffer offer,
            out string error)
        {
            error = null;
            if (offer.IsCashOutOnly)
            {
                _activeOffer = offer;
                _activeSeal = null;
                _activeCandidates = null;
                _actionPending = false;
                _rewardRevealOverlay ??= RewardFeedbackPresenter.Create(this);
                _rewardRevealOverlay.ShowDecision(EnsureCanvas(),
                    RewardFeedbackItem.FromJade(offer.RewardContentId,
                        _jadeLedger.PreviewCashOut(),
                        "当前没有已解锁且可钤印的高阶灵印，可领取灵玉。"),
                    null, new[] { new RewardUtilityAction(RewardUtilityActionKind.CashOut,
                    $"领取灵玉 · +{_jadeLedger.PreviewCashOut()}", OnCashOutSelected) });
                return true;
            }
            if (!_sealHandler.TryGetCandidateSnapshot(
                    offer.SealContentId,
                    out _activeSeal,
                    out _activeCandidates,
                    out error))
                return false;

            _activeOffer = offer;
            _actionPending = false;
            var actions = new List<RewardUtilityAction>();
            if (offer.AllowsCashOut)
            {
                actions.Add(new RewardUtilityAction(
                    RewardUtilityActionKind.CashOut,
                    $"化为灵玉 · +{_jadeLedger.PreviewCashOut()}",
                    OnCashOutSelected));
            }
            if (offer.SealContentIds != null &&
                offer.SealContentIds.Count > 1)
            {
                actions.Add(new RewardUtilityAction(
                    RewardUtilityActionKind.Refresh,
                    "重选灵印",
                    OnReturnToSealChoice));
            }
            RewardFeedbackItem item =
                RewardFeedbackItem.FromSeal(_activeSeal);
            if (_activeCandidates.Count == 0)
            {
                bool canReselect = offer.SealContentIds != null && offer.SealContentIds.Count > 1;
                item.DecisionHint = canReselect
                    ? offer.AllowsCashOut
                        ? "当前没有可钤印的卡牌，可重选灵印或化为灵玉。"
                        : "当前没有可钤印的卡牌，可重选灵印。"
                    : offer.AllowsCashOut
                        ? "当前没有可钤印的卡牌，可化为灵玉。"
                        : "当前没有可钤印的卡牌。";
            }
            _rewardRevealOverlay ??=
                RewardFeedbackPresenter.Create(this);
            _rewardRevealOverlay.ShowDecision(
                EnsureCanvas(),
                item,
                _activeCandidates.Count > 0
                    ? (Action)OnAssignSelected
                    : null,
                actions);
            return true;
        }

        private void OnSealSelected(
            LevelVictorySealRewardOffer shownOffer,
            int selectedIndex)
        {
            if (_actionPending || selectedIndex < 0 ||
                shownOffer.SealContentIds == null ||
                selectedIndex >= shownOffer.SealContentIds.Count)
            {
                return;
            }

            _actionPending = true;
            string sealContentId = shownOffer.SealContentIds[selectedIndex];
            if (!_runtime.TryChooseSeal(
                    shownOffer.RewardContentId,
                    sealContentId,
                    out string error))
            {
                RecoverFromChoice(error);
                return;
            }
            Action show = () =>
            {
                string showError = null;
                if (_runtime.TryGetPending(
                        out LevelVictorySealRewardOffer pending) &&
                    TryShowAssignment(pending, out showError))
                {
                    return;
                }
                RecoverToDisposition(
                    string.IsNullOrWhiteSpace(showError)
                        ? "无法显示已选灵印。"
                        : showError);
            };
            if (_sealChoiceOverlay != null &&
                _sealChoiceOverlay.PlaySelectionAndClose(selectedIndex, show))
            {
                return;
            }
            show();
        }

        private bool TryShowAssignment(
            LevelVictorySealRewardOffer offer,
            out string error)
        {
            error = null;
            // Includes a saved multi-choice checkpoint from the former disposition page.
            if (offer.Phase == LevelVictorySealRewardPhase.ChoosingDisposition)
            {
                if (!_runtime.TryBeginAssignment(offer.RewardContentId, out error))
                    return false;
                if (!_runtime.TryGetPending(out offer))
                {
                    error = "进入钤印后找不到待处理奖励。";
                    return false;
                }
            }
            if (!_sealHandler.TryGetCandidateSnapshot(
                    offer.SealContentId,
                    out _activeSeal,
                    out _activeCandidates,
                    out error))
                return false;
            _activeOffer = offer;
            if (_activeCandidates.Count == 0)
            {
                if (!_runtime.TryReturnToDisposition(
                        offer.RewardContentId, out error))
                    return false;
                if (!_runtime.TryGetPending(out offer))
                {
                    error = "返回处理页后找不到待处理奖励。";
                    return false;
                }
                return TryShowDisposition(offer, out error);
            }
            _actionPending = false;
            return _assignmentCoordinator.TryBeginAssignment(
                _activeSeal,
                _activeCandidates,
                CommitAssignment,
                ReturnToDisposition,
                showRewardReveal: false,
                out error);
        }

        private void OnAssignSelected()
        {
            if (_actionPending)
                return;
            _actionPending = true;
            if (!_runtime.TryBeginAssignment(
                    _activeOffer.RewardContentId,
                    out string error))
            {
                RecoverToDisposition(error);
                return;
            }
            _rewardRevealOverlay?.CloseSilently();
            Action begin = () =>
            {
                string beginError = null;
                if (_runtime.TryGetPending(
                        out LevelVictorySealRewardOffer pending) &&
                    TryShowAssignment(pending, out beginError))
                    return;
                RecoverToDisposition(
                    string.IsNullOrWhiteSpace(beginError)
                        ? "无法恢复待钤印奖励。"
                        : beginError);
            };
            begin();
        }

        private void CommitAssignment(int cardRuntimeInstanceId)
        {
            if (!_runtime.TryAssign(
                    _activeOffer.RewardContentId,
                    cardRuntimeInstanceId,
                    _activeSeal.ToCardSealSpec(),
                    out _,
                    out string error))
            {
                RecoverToDisposition(error);
                return;
            }
            _assignmentCoordinator.PlayCommitted(Finish);
        }

        private void ReturnToDisposition()
        {
            if (_activeOffer.SealContentIds != null && _activeOffer.SealContentIds.Count > 1)
            {
                OnReturnToSealChoice();
                return;
            }
            if (!_runtime.TryReturnToDisposition(
                    _activeOffer.RewardContentId,
                    out string error))
            {
                RecoverToDisposition(error);
                return;
            }
            string showError = null;
            if (_runtime.TryGetPending(out LevelVictorySealRewardOffer offer) &&
                TryShowDisposition(offer, out showError))
                return;
            RecoverToDisposition(showError ?? "无法返回灵印处理页。");
        }

        private void OnReturnToSealChoice()
        {
            if (_actionPending)
                return;
            _actionPending = true;
            _rewardRevealOverlay?.CloseSilently();
            if (!_runtime.TryReturnToSealChoice(
                    _activeOffer.RewardContentId,
                    out string error))
            {
                RecoverToDisposition(error);
                return;
            }
            string showError = null;
            if (_runtime.TryGetPending(
                    out LevelVictorySealRewardOffer pending) &&
                TryShowSealChoice(pending, out showError))
            {
                return;
            }
            RecoverToDisposition(showError ?? "无法返回灵印候选页。");
        }

        private void OnCashOutSelected()
        {
            if (_actionPending)
                return;
            _actionPending = true;
            _rewardRevealOverlay?.CloseSilently();
            _sealChoiceOverlay?.Close();
            if (!_runtime.TryCashOut(
                    _activeOffer.RewardContentId,
                    _jadeLedger.CashOutBaseJade, _jadeLedger,
                    out _,
                    out string error))
            {
                RecoverToDisposition(error);
                return;
            }
            Finish();
        }

        private void RecoverToDisposition(string error)
        {
            Debug.LogError(
                "[SealChoiceVictoryReward] " +
                (string.IsNullOrWhiteSpace(error)
                    ? "胜利灵印处理失败。"
                    : error));
            _assignmentCoordinator?.CloseSilently();
            _sealChoiceOverlay?.Close();
            _rewardRevealOverlay?.CloseSilently();
            _actionPending = false;

            if (_runtime != null &&
                _runtime.TryGetPending(out LevelVictorySealRewardOffer pending))
            {
                bool multiple = pending.SealContentIds != null && pending.SealContentIds.Count > 1;
                if (pending.Phase == LevelVictorySealRewardPhase.AssigningSeal ||
                    (multiple && pending.Phase == LevelVictorySealRewardPhase.ChoosingDisposition))
                {
                    string recoveryError;
                    bool returned = multiple
                        ? _runtime.TryReturnToSealChoice(pending.RewardContentId, out recoveryError)
                        : _runtime.TryReturnToDisposition(pending.RewardContentId, out recoveryError);
                    if (!returned)
                        Debug.LogError("[SealChoiceVictoryReward] 返回奖励页未保存：" + recoveryError);
                    _runtime.TryGetPending(out pending);
                }
                string chooseError = null;
                string showError = null;
                if (pending.Phase ==
                        LevelVictorySealRewardPhase.ChoosingSeal &&
                    TryShowSealChoice(pending, out chooseError))
                {
                    return;
                }
                if (pending.Phase == LevelVictorySealRewardPhase.AssigningSeal)
                {
                    if (TryShowAssignment(pending, out showError)) return;
                }
                else if (TryShowDisposition(pending, out showError))
                    return;
                Debug.LogError(
                    "[SealChoiceVictoryReward] 无法恢复处理页：" +
                    (pending.Phase ==
                        LevelVictorySealRewardPhase.ChoosingSeal
                        ? chooseError
                        : showError));
            }
        }

        private void RecoverFromChoice(string error)
        {
            Debug.LogError(
                "[SealChoiceVictoryReward] " +
                (string.IsNullOrWhiteSpace(error)
                    ? "胜利灵印选择失败。"
                    : error));
            _sealChoiceOverlay?.Close();
            _actionPending = false;
            if (_runtime != null &&
                _runtime.TryGetPending(out LevelVictorySealRewardOffer pending) &&
                pending.Phase ==
                    LevelVictorySealRewardPhase.ChoosingSeal &&
                TryShowSealChoice(pending, out string showError))
            {
                return;
            }
            RecoverToDisposition(error);
        }

        private void Finish()
        {
            if (!_processing)
                return;
            _rewardRevealOverlay?.CloseSilently();
            _sealChoiceOverlay?.Close();
            _processing = false;
            _actionPending = false;
            _activeSeal = null;
            _activeCandidates = null;
            Action completed = _onCompleted;
            _onCompleted = null;
            completed?.Invoke();
        }


        private Canvas EnsureCanvas()
        {
            if (_canvas != null)
                return _canvas;
            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystem = new GameObject(
                    "SealChoiceRewardEventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform, false);
            }
            var canvasObject = new GameObject(
                "SealChoiceRewardCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32090;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return _canvas;
        }

        private void OnDestroy()
        {
            _sealChoiceOverlay?.Close();
            _rewardRevealOverlay?.CloseSilently();
            _assignmentCoordinator?.CloseSilently();
            _onCompleted = null;
        }
    }
}
