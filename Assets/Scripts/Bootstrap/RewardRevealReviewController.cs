#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// 编辑器审核页中的通用奖励获得播放器。调用方提供已经确定的展示快照，
    /// 本组件只复用生产 Overlay 与 Presenter，并在玩家收起获得窗后交回流程。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class RewardRevealReviewController : MonoBehaviour
    {
        private Canvas _canvas;
        private RewardFeedbackPresenter _overlay;
        private RewardFeedbackItem _item;
        private Action _onClosed;
        private Action _onPrimarySelected;
        private IReadOnlyList<RewardUtilityAction> _actions;
        private bool _showDecision;
        private Coroutine _showRoutine;
        private int _generation;

        public bool IsShown { get; private set; }

        public void Initialize(Canvas canvas)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _overlay ??= RewardFeedbackPresenter.Create(this);
        }

        public void Show(RewardFeedbackItem item, Action onClosed)
        {
            if (_canvas == null)
                throw new InvalidOperationException("奖励获得播放器尚未初始化。");
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _onClosed = onClosed;
            _onPrimarySelected = null;
            _actions = null;
            _showDecision = false;
            IsShown = true;
            QueueShow();
        }

        public void ShowDecision(
            RewardFeedbackItem item,
            Action onPrimarySelected,
            IReadOnlyList<RewardUtilityAction> actions)
        {
            if (_canvas == null)
                throw new InvalidOperationException("奖励获得播放器尚未初始化。");
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _onClosed = null;
            _onPrimarySelected = onPrimarySelected;
            _actions = actions;
            _showDecision = true;
            IsShown = true;
            QueueShow();
        }

        private void QueueShow()
        {
            int generation = ++_generation;
            if (_showRoutine != null)
                StopCoroutine(_showRoutine);
            _overlay?.CloseSilently();
            _showRoutine = StartCoroutine(ShowNextFrame(generation));
        }

        private IEnumerator ShowNextFrame(int generation)
        {
            yield return null;
            _showRoutine = null;
            if (!IsShown || generation != _generation || _canvas == null ||
                _overlay == null || _item == null)
            {
                yield break;
            }

            if (_showDecision)
            {
                Action selected = _onPrimarySelected;
                Action wrappedPrimary = selected == null
                    ? null
                    : (Action)(() =>
                        HandleDecisionAction(generation, selected));
                var wrappedActions = new List<RewardUtilityAction>();
                for (int i = 0; _actions != null && i < _actions.Count; i++)
                {
                    RewardUtilityAction action = _actions[i];
                    if (action == null || action.Execute == null)
                        continue;
                    Action execute = action.Execute;
                    wrappedActions.Add(new RewardUtilityAction(
                        action.Kind,
                        action.Label,
                        () => HandleDecisionAction(generation, execute)));
                }
                _overlay.ShowDecision(
                    _canvas,
                    _item,
                    wrappedPrimary,
                    wrappedActions);
            }
            else
            {
                _overlay.Show(
                    _canvas,
                    _item,
                    () => HandleClosed(generation));
            }
        }

        private void HandleClosed(int generation)
        {
            if (!IsShown || generation != _generation)
                return;
            IsShown = false;
            Action callback = _onClosed;
            _onClosed = null;
            callback?.Invoke();
        }

        private void HandleDecisionAction(int generation, Action callback)
        {
            if (!IsShown || generation != _generation)
                return;
            IsShown = false;
            _overlay?.CloseSilently();
            _onPrimarySelected = null;
            _actions = null;
            _item = null;
            _showDecision = false;
            callback?.Invoke();
        }

        public void Close()
        {
            IsShown = false;
            _generation++;
            if (_showRoutine != null)
            {
                StopCoroutine(_showRoutine);
                _showRoutine = null;
            }
            _onClosed = null;
            _onPrimarySelected = null;
            _actions = null;
            _item = null;
            _showDecision = false;
            _overlay?.CloseSilently();
        }

        private void OnDestroy()
        {
            Close();
        }
    }
}
#endif
