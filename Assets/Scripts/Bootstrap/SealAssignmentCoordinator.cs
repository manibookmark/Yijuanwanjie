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
    public enum SealDispositionSelectionKind
    {
        Card,
        CashOut
    }

    public readonly struct SealDispositionSelection
    {
        public SealDispositionSelectionKind Kind { get; }
        public int CardRuntimeInstanceId { get; }
        public bool IsCashOut =>
            Kind == SealDispositionSelectionKind.CashOut;

        private SealDispositionSelection(
            SealDispositionSelectionKind kind,
            int cardRuntimeInstanceId)
        {
            Kind = kind;
            CardRuntimeInstanceId = Math.Max(0, cardRuntimeInstanceId);
        }

        public static SealDispositionSelection Card(int runtimeInstanceId)
            => new SealDispositionSelection(
                SealDispositionSelectionKind.Card,
                runtimeInstanceId);

        public static SealDispositionSelection CashOut()
            => new SealDispositionSelection(
                SealDispositionSelectionKind.CashOut,
                0);
    }

    /// <summary>
    /// 地图 GrantSeal 的“拾取/化玉”处置与宿主选择表现协调器。它只把玩家的
    /// 类型化意图交回地图流程，不写入 Run、经济账本或节点完成状态。
    /// </summary>
    public sealed class SealAssignmentCoordinator : MonoBehaviour
    {
        private SealInteractionEffectHandler _handler;
        private Action _playStampAudio;
        private Action _playEvolutionAudio;
        private Canvas _canvas;
        private SealAssignmentOverlay _overlay;
        private RewardFeedbackPresenter _revealOverlay;
        private SealAssignmentViewModel _lastPreviewViewModel;
        private int _lastPreviewSelectedIndex = -1;
        private Func<bool> _getMapInteractionEnabled;
        private Action<bool> _setMapInteractionEnabled;
        private bool _mapInteractionLeaseHeld;
        private bool _mapInteractionWasEnabled;

        public bool IsActive =>
            (_revealOverlay != null && _revealOverlay.IsShown) ||
            (_overlay != null && _overlay.IsShown);

        /// <summary>
        /// Inspector 调试入口只复用最近一次合法候选快照；重播不会调用规则提交，
        /// 因而不会修改 Run 或节点状态。
        /// </summary>
        public bool CanReplayLastSealPreview =>
            Application.isPlaying && !IsActive &&
            _lastPreviewViewModel != null &&
            _lastPreviewSelectedIndex >= 0 &&
            _lastPreviewSelectedIndex < _lastPreviewViewModel.Candidates.Count;

        public void Bind(
            SealInteractionEffectHandler handler,
            Action playStampAudio = null,
            Func<bool> getMapInteractionEnabled = null,
            Action<bool> setMapInteractionEnabled = null,
            Action playEvolutionAudio = null)
        {
            _handler = handler ??
                throw new ArgumentNullException(nameof(handler));
            BindPresentationAudio(playStampAudio, playEvolutionAudio);
            _getMapInteractionEnabled = getMapInteractionEnabled;
            _setMapInteractionEnabled = setMapInteractionEnabled;
        }

        /// <summary>地图与战斗共用落印音效接线；纯表现来源无需创建地图规则处理器。</summary>
        public void BindPresentationAudio(Action playStampAudio, Action playEvolutionAudio = null)
        {
            _playStampAudio = playStampAudio;
            _playEvolutionAudio = playEvolutionAudio;
        }

        public bool TryBegin(
            LevelInteractionEffect effect,
            Action<SealDispositionSelection> onSelected,
            out string error)
        {
            error = null;
            if (_handler == null)
            {
                error = "钤印选择协调器尚未绑定处理器。";
                return false;
            }
            if (!_handler.TryGetCandidateSnapshot(
                    effect,
                    out var seal,
                    out var candidates,
                    out error))
                return false;
            if (candidates.Count == 0 && !_handler.CanCashOut)
            {
                error = $"灵印 {seal.contentId} 没有合法宿主，且未配置化玉入口。";
                return false;
            }

            SealAssignmentViewModel viewModel = null;
            if (candidates.Count > 0)
            {
                try
                {
                    viewModel = new SealAssignmentViewModel(seal, candidates);
                }
                catch (Exception exception)
                {
                    error = $"构建钤印视图模型失败：{exception.Message}";
                    return false;
                }
            }

            RememberPreview(viewModel);
            ShowDispositionFlow(seal, viewModel, onSelected);
            return true;
        }

        private void ShowDispositionFlow(
            LingyinConfig seal,
            SealAssignmentViewModel viewModel,
            Action<SealDispositionSelection> onSelected)
        {
            CloseSilently();
            SuspendMapInteraction();
            Canvas canvas = EnsureCanvas();
            _revealOverlay ??=
                RewardFeedbackPresenter.Create(this);

            var actions = new List<RewardUtilityAction>(1);
            if (_handler.CanCashOut)
            {
                actions.Add(new RewardUtilityAction(
                    RewardUtilityActionKind.CashOut,
                    $"化为灵玉 · +{_handler.PreviewCashOut()}",
                    () =>
                    {
                        CloseSilently();
                        onSelected?.Invoke(
                            SealDispositionSelection.CashOut());
                    }));
            }

            Action pickUp = viewModel != null
                ? (Action)(() => ShowFlow(
                    viewModel,
                    showRewardReveal: false,
                    selected =>
                    {
                        _lastPreviewSelectedIndex = viewModel.SelectedIndex;
                        onSelected?.Invoke(
                            SealDispositionSelection.Card(selected));
                    },
                    () => ShowDispositionFlow(
                        seal,
                        viewModel,
                        onSelected)))
                : null;
            RewardFeedbackItem revealItem =
                RewardFeedbackItem.FromSeal(seal);
            if (viewModel == null)
                revealItem.DecisionHint = "当前没有可钤印的卡牌，可化为灵玉。";
            _revealOverlay.ShowDecision(
                canvas,
                revealItem,
                pickUp,
                actions);
        }

        /// <summary>
        /// 复用宿主选择与落印表现。BOSS 处理页已经展示灵印时可跳过重复获得窗；
        /// 返回仍由调用方决定回地图还是回奖励处理页。
        /// </summary>
        public bool TryBeginAssignment(
            LingyinConfig seal,
            IReadOnlyList<CardConfig> candidates,
            Action<int> onConfirmed,
            Action onCancelled,
            bool showRewardReveal,
            out string error)
        {
            error = null;
            if (seal == null || candidates == null || candidates.Count == 0)
            {
                error = "钤印选择缺少灵印或合法宿主。";
                return false;
            }

            SealAssignmentViewModel viewModel;
            try
            {
                viewModel = new SealAssignmentViewModel(seal, candidates);
            }
            catch (Exception exception)
            {
                error = $"构建钤印视图模型失败：{exception.Message}";
                return false;
            }

            RememberPreview(viewModel);
            ShowFlow(
                viewModel,
                showRewardReveal,
                selected =>
                {
                    _lastPreviewSelectedIndex = viewModel.SelectedIndex;
                    onConfirmed?.Invoke(selected);
                },
                onCancelled);
            return true;
        }

        /// <summary>
        /// 重播最近一次真实选择对应的落印、粒子与收牌，不重新展示奖励和
        /// 候选页，也不会把选择交给地图事务。
        /// </summary>
        public bool ReplayLastSealPreview()
        {
            if (!CanReplayLastSealPreview)
                return false;

            SealAssignmentViewModel viewModel = _lastPreviewViewModel;

            if (!viewModel.Select(_lastPreviewSelectedIndex))
                return false;

            CloseSilently();
            SuspendMapInteraction();
            Canvas canvas = EnsureCanvas();
            _overlay ??= gameObject.AddComponent<SealAssignmentOverlay>();
            bool started = _overlay.ShowCommittedPreview(
                canvas,
                viewModel,
                _lastPreviewSelectedIndex,
                _playStampAudio,
                CloseSilently,
                _playEvolutionAudio);
            if (started)
                GlobalButtonAudioRuntime.BindHierarchyNow(_overlay.Root);
            if (!started)
                CloseSilently();
            return started;
        }

        private void ShowFlow(
            SealAssignmentViewModel viewModel,
            bool showRewardReveal,
            Action<int> onConfirmed,
            Action onCancelled)
        {
            CloseSilently();
            SuspendMapInteraction();
            Canvas canvas = EnsureCanvas();
            _overlay ??= gameObject.AddComponent<SealAssignmentOverlay>();
            _revealOverlay ??=
                RewardFeedbackPresenter.Create(this);

            RewardFeedbackItem revealItem =
                RewardFeedbackItem.FromSeal(viewModel.Seal);
            Action showAssignment = () =>
            {
                _overlay.Show(
                    canvas,
                    viewModel,
                    onConfirmed,
                    () =>
                    {
                        CloseSilently();
                        onCancelled?.Invoke();
                    });
                GlobalButtonAudioRuntime.BindHierarchyNow(_overlay.Root);
            };
            if (!showRewardReveal)
            {
                showAssignment();
                return;
            }
            _revealOverlay.Show(
                canvas,
                revealItem,
                showAssignment);
        }

        private void RememberPreview(SealAssignmentViewModel viewModel)
        {
            _lastPreviewViewModel = viewModel;
            _lastPreviewSelectedIndex = -1;
        }

        public void PlayCommitted(Action onClosed)
            => PlayCommitted(onClosed, null);

        /// <summary>
        /// 战斗来源提供真实牌区终点时自动归库；地图来源短暂停留后自动归卷。
        /// </summary>
        public void PlayCommitted(Action onClosed, SealCardReturnTarget? returnTarget)
        {
            if (_overlay == null)
            {
                RestoreMapInteraction();
                onClosed?.Invoke();
                return;
            }
            _overlay.PlayCommitted(
                _playStampAudio,
                () =>
                {
                    RestoreMapInteraction();
                    onClosed?.Invoke();
                },
                _playEvolutionAudio,
                returnTarget);
        }

        /// <summary>
        /// 为跨表现模块编排返回固定灵印锚点的屏幕坐标，不暴露 UI Transform。
        /// </summary>
        public bool TryGetAnimationOriginScreenPoint(out Vector2 screenPoint)
        {
            if (_overlay != null)
                return _overlay.TryGetAnimationOriginScreenPoint(
                    out screenPoint);
            screenPoint = default;
            return false;
        }

        public void CloseSilently()
        {
            _revealOverlay?.CloseSilently();
            _overlay?.CloseSilently();
            RestoreMapInteraction();
        }

        private void SuspendMapInteraction()
        {
            if (_mapInteractionLeaseHeld ||
                _setMapInteractionEnabled == null)
                return;
            _mapInteractionWasEnabled =
                _getMapInteractionEnabled?.Invoke() ?? false;
            _setMapInteractionEnabled(false);
            _mapInteractionLeaseHeld = true;
        }

        private void RestoreMapInteraction()
        {
            if (!_mapInteractionLeaseHeld)
                return;
            _mapInteractionLeaseHeld = false;
            _setMapInteractionEnabled?.Invoke(
                _mapInteractionWasEnabled);
        }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null)
                return _canvas;
            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystem = new GameObject(
                    "SealAssignmentEventSystem",
                    typeof(EventSystem),
                    typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform, false);
            }
            var canvasObject = new GameObject(
                "SealAssignmentCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32100;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return _canvas;
        }

        private void OnDestroy()
        {
            CloseSilently();
            _playStampAudio = null;
            _playEvolutionAudio = null;
            _getMapInteractionEnabled = null;
            _setMapInteractionEnabled = null;
            _lastPreviewViewModel = null;
        }
    }
}
