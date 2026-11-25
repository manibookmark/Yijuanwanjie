using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns one reward-feedback session: overlay, effects, button audio and
    /// completion/cancellation. Callers supply an already resolved reward and
    /// their canvas; payment, saved offers and progression remain outside.
    /// Each flow owns a child instance, never another coordinator's effects.
    /// </summary>
    public sealed class RewardFeedbackPresenter : MonoBehaviour
    {
        private RewardRevealOverlay _overlay;
        private RewardRevealVfxPresenter _effects;
        private Coroutine _startEffects;
        private Coroutine _autoClose;
        private Action _onClosed;
        private Behaviour _owner;
        private int _generation;
        private bool _active;

        public GameObject Root => _overlay != null ? _overlay.Root : null;
        public bool IsShown => _active && _overlay != null &&
            _overlay.isActiveAndEnabled && _overlay.IsShown &&
            Root != null && Root.activeInHierarchy;

        public static RewardFeedbackPresenter Create(Component owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            var child = new GameObject(owner.GetType().Name + "RewardFeedback");
            child.transform.SetParent(owner.transform, false);
            var presenter = child.AddComponent<RewardFeedbackPresenter>();
            presenter._owner = owner as Behaviour;
            return presenter;
        }

        public void Show(Canvas canvas, RewardFeedbackItem item, Action onClosed)
        {
            int generation = Begin(canvas, item);
            _onClosed = onClosed;
            try
            {
                _overlay.Show(canvas, item, () => Complete(generation));
                ArmPresentation(generation, item);
            }
            catch
            {
                CloseSilently();
                throw;
            }
        }

        public void ShowDecision(Canvas canvas, RewardFeedbackItem item,
            Action onPrimarySelected, IReadOnlyList<RewardUtilityAction> actions)
        {
            int generation = Begin(canvas, item);
            try
            {
                Action primary = onPrimarySelected == null ? null : (Action)(() =>
                {
                    if (!IsCurrent(generation)) return;
                    CloseSilently();
                    onPrimarySelected();
                });
                var guardedActions = new List<RewardUtilityAction>();
                for (int i = 0; actions != null && i < actions.Count; i++)
                {
                    RewardUtilityAction action = actions[i];
                    if (action?.Execute == null) continue;
                    guardedActions.Add(new RewardUtilityAction(action.Kind,
                        action.Label, () =>
                        {
                            if (IsCurrent(generation)) action.Execute();
                        }));
                }
                // Tool actions may need a durable commit before closing.
                // The transaction owner retains that decision.
                _overlay.ShowDecision(canvas, item, primary, guardedActions);
                ArmPresentation(generation, item);
            }
            catch
            {
                CloseSilently();
                throw;
            }
        }

        /// <summary>Shows an already committed result without another confirmation.</summary>
        public void ShowTimed(Canvas canvas, RewardFeedbackItem item, float seconds, Action onClosed)
        {
            Show(canvas, item, onClosed);
            _autoClose = StartCoroutine(CloseAfter(_generation, Mathf.Max(0.1f, seconds)));
        }

        private IEnumerator CloseAfter(int generation, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _autoClose = null;
            if (IsCurrent(generation)) Complete(generation);
        }

        private int Begin(Canvas canvas, RewardFeedbackItem item)
        {
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (_owner != null && !_owner.isActiveAndEnabled)
                throw new InvalidOperationException("Reward feedback requires an active flow owner.");
            CloseSilently();
            // A lost child view can complete one reward while the owning flow
            // continues. An explicit new Show reactivates our own view only.
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            enabled = true;
            _overlay ??= gameObject.AddComponent<RewardRevealOverlay>();
            _overlay.enabled = true;
            _active = true;
            return _generation;
        }

        private void ArmPresentation(int generation, RewardFeedbackItem item)
        {
            GlobalButtonAudioRuntime.BindHierarchyNow(Root);
            if (item.Kind != RewardFeedbackKind.Unavailable)
                _startEffects = StartCoroutine(StartEffectsNextFrame(generation, item));
        }

        private IEnumerator StartEffectsNextFrame(int generation, RewardFeedbackItem item)
        {
            // Previous render stages use Destroy; let them leave the hierarchy
            // before constructing another native particle/camera stage.
            yield return null;
            _startEffects = null;
            if (!IsCurrent(generation) || !IsShown) yield break;
            _effects ??= gameObject.AddComponent<RewardRevealVfxPresenter>();
            try
            {
                _effects.Present(_overlay, item);
            }
            catch (Exception exception)
            {
                _effects.Release();
                Debug.LogWarning("[RewardFeedback] 奖励已提交，特效暂不可用：" + exception.Message);
            }
        }

        private bool IsCurrent(int generation) => _active && generation == _generation;

        private void Complete(int generation)
        {
            if (!IsCurrent(generation)) return;
            Action callback = _onClosed;
            CloseSilently();
            callback?.Invoke();
        }

        public void Close()
        {
            if (_active) Complete(_generation);
            else CloseSilently();
        }

        public void CloseSilently()
        {
            _generation++;
            _active = false;
            _onClosed = null;
            if (_autoClose != null)
            {
                StopCoroutine(_autoClose);
                _autoClose = null;
            }
            if (_startEffects != null)
            {
                StopCoroutine(_startEffects);
                _startEffects = null;
            }
            _effects?.Release();
            _overlay?.CloseSilently();
        }

        private void LateUpdate()
        {
            if (_active && !IsShown) Complete(_generation);
        }

        private void OnDisable()
        {
            // Losing only the view must release a live flow's wait. Whole-flow
            // teardown and explicit cancellation continue to be silent.
            if (_active && _owner != null && _owner.isActiveAndEnabled &&
                _owner.gameObject.activeInHierarchy)
                Complete(_generation);
            else
                CloseSilently();
        }
        private void OnDestroy() => CloseSilently();
    }
}
