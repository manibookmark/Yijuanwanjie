using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Owns one map rubbing modal lifecycle. Dungeon owns candidate
    /// evidence, copy construction, cancellation and the durable grant; UI owns
    /// card transforms and ink/return animation. Scene loss retains pending work.</summary>
    public sealed class DungeonRunDuplicateOfferingCoordinator : MonoBehaviour, IDungeonMapCardFacility
    {
        private DungeonRunSession _run;
        private Func<IReadOnlyList<CardConfig>> _getCards;
        private Func<DuplicateOfferingEligibility> _getEligibility;
        private Action _persist;
        private Func<bool> _canPersist;
        private Canvas _canvas;
        private DuplicateOfferingOverlay _overlay;
        private RewardChoiceOverlay _problem;
        private readonly List<CardConfig> _ownedCards = new List<CardConfig>();
        private IDisposable _lease;
        private string _nodeId;
        private Action<LevelInteractionReceipt> _onCommitted;
        private Action _onCancelled;
        private DuplicateOfferingReceipt _receipt;
        private bool _active, _pending, _notified, _playingFeedback;

        public LevelInteractionEffectKind EffectKind => LevelInteractionEffectKind.DuplicateOffering;
        public bool IsActive => _active;
        public bool CanBegin => isActiveAndEnabled && _run != null && _getCards != null &&
            _getEligibility != null && _persist != null && _canPersist != null && _canPersist();

        public void Bind(DungeonRunSession run, Func<IReadOnlyList<CardConfig>> getCards,
            Func<DuplicateOfferingEligibility> getEligibility, Action persist, Func<bool> canPersist)
        {
            CloseSilently(); _run = run; _getCards = getCards; _getEligibility = getEligibility;
            _persist = persist; _canPersist = canPersist;
        }

        public static bool Handles(LevelNode node) => node?.completionEffects?.Count == 1 &&
            node.completionEffects[0]?.kind == LevelInteractionEffectKind.DuplicateOffering;

        public bool TryBegin(string nodeId, Action<LevelInteractionReceipt> onCommitted,
            Action onCancelled, out string error)
        {
            error = null;
            if (_active || !CanBegin) { error = "拓印处需要当前可保存的探索。"; return false; }
            if (!Handles(_run.CurrentFloor.FindNode(nodeId))) { error = "节点没有独立的拓印合同。"; return false; }
            _active = true; _nodeId = nodeId; _onCommitted = onCommitted; _onCancelled = onCancelled;
            _lease = SceneTransitionRuntime.ExternalModalBarrier?.Hold("modal.map.duplicate-offering");
            Reopen(); return true;
        }

        private void Reopen()
        {
            if (!_active) return;
            try
            {
                RequirePersistence(); ClosePanels(); ReleaseCards(); _pending = false;
                bool inspected = _run.DuplicateOfferings.TryInspect(_nodeId, out var offer, out var error);
                // Only an active pending visit is frozen. An explicit 暂别 leaves
                // a dormant journal and reopening refreshes the actual deck.
                if (!inspected || offer.IsDormant)
                {
                    if (_run.DuplicateOfferings.HasPendingChoice ||
                        _run.CurrentFloor.GetNodeState(_nodeId) == LevelNodeViewState.Completed)
                        throw new InvalidOperationException(error);
                    if (!DungeonRunDuplicateOfferingSource.TryPrepare(_run, _nodeId, _getCards(), _getEligibility(), out var context, out error) ||
                        !_run.DuplicateOfferings.TryOpen(_nodeId, context, out offer, out error, _persist))
                        throw new InvalidOperationException(error);
                }
                if (offer.AlreadyCompleted) { _receipt = offer.Receipt; NotifyCommitted(); return; }
                if (!offer.HasPendingChoice) throw new InvalidOperationException("拓印没有可恢复的待选记录。");
                if (!_run.TryGetCurrentCards(_getCards(), out var current, out error)) throw new InvalidOperationException(error);
                _ownedCards.AddRange(current);
                var originals = new List<CardConfig>(); var previews = new List<CardConfig>();
                foreach (var candidate in offer.Candidates)
                {
                    if (!candidate.IsAvailable) throw new InvalidOperationException("待选卡牌已发生变化，请返回地图后重访拓印处。");
                    var original = current.Single(value => value.runtimeInstanceId == candidate.RuntimeInstanceId);
                    var preview = DungeonRunDuplicateOfferingSource.BuildPreview(original, _getCards(), offer.LimitBreakPolicy);
                    _ownedCards.Add(preview); originals.Add(original); previews.Add(preview);
                }
                Overlay().Show(EnsureCanvas(), originals, previews, Submit, Cancel);
                GlobalButtonAudioRuntime.BindHierarchyNow(_overlay.Root);
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private void Submit(int instanceId)
        {
            if (!_active || _pending) return; _pending = true;
            try
            {
                RequirePersistence();
                if (!DungeonRunDuplicateOfferingSource.TrySubmit(_run, _nodeId, instanceId, _getCards(), _persist, out _receipt, out var error))
                    throw new InvalidOperationException(error);
                NotifyCommitted();
            }
            catch (Exception exception)
            {
                if (_run.DuplicateOfferings.TryInspect(_nodeId, out var saved, out _) && saved.AlreadyCompleted)
                { _receipt = saved.Receipt; NotifyCommitted(); }
                else ShowProblem(exception.Message);
            }
        }

        private void NotifyCommitted()
        {
            if (_notified || _receipt == null) return;
            _notified = true; _pending = true; ClosePanels(); ReleaseCards();
            _onCommitted?.Invoke(_receipt.InteractionReceipt);
        }

        public void PlayCommitted(Action onClosed)
        {
            if (!_active || _receipt == null) { CloseSilently(); onClosed?.Invoke(); return; }
            try
            {
                if (!_run.TryGetCurrentCards(_getCards(), out var current, out var error)) throw new InvalidOperationException(error);
                _ownedCards.AddRange(current);
                var result = current.Single(value => value.runtimeInstanceId == _receipt.CreatedRuntimeInstanceId);
                _playingFeedback = true;
                Overlay().PlayCommitted(EnsureCanvas(), result, () => { CloseSilently(); onClosed?.Invoke(); });
            }
            catch (Exception exception)
            { Debug.LogWarning("[DuplicateOffering] 拓印已保存，跳过不可用的结果演出：" + exception.Message); CloseSilently(); onClosed?.Invoke(); }
        }

        private void Cancel()
        {
            if (!_active || _receipt != null) return;
            try
            {
                if (_run.DuplicateOfferings.TryInspect(_nodeId, out var saved, out _) && saved.AlreadyCompleted)
                { _receipt = saved.Receipt; NotifyCommitted(); return; }
                var floor = _run.CurrentFloor;
                if (_run.DuplicateOfferings.HasPendingChoice || !string.IsNullOrEmpty(floor.ActiveInteractionNodeId))
                {
                    RequirePersistence();
                    if (!_run.DuplicateOfferings.TryLeave(_nodeId, out var error, _persist)) throw new InvalidOperationException(error);
                }
                Action cancelled = _onCancelled; CloseSilently(); cancelled?.Invoke();
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private void ShowProblem(string error)
        {
            if (!_active) return; _pending = false; ClosePanels(); ReleaseCards();
            _problem ??= gameObject.AddComponent<RewardChoiceOverlay>();
            _problem.Show(EnsureCanvas(), "拓印暂未完成", new[] {
                new RewardFeedbackItem { Kind = RewardFeedbackKind.Unavailable, DisplayName = "请重试或暂别", Description = error }
            }, new[] { new RewardUtilityAction(RewardUtilityActionKind.Refresh, "重试", Reopen),
                new RewardUtilityAction(RewardUtilityActionKind.Abandon, "返回地图", InterruptView) }, _ => Reopen());
            GlobalButtonAudioRuntime.BindHierarchyNow(_problem.Root);
        }

        private void RequirePersistence()
        { if (!CanBegin) throw new InvalidOperationException("当前地图的正式存档暂不可用。"); }
        private DuplicateOfferingOverlay Overlay() => _overlay ??= gameObject.AddComponent<DuplicateOfferingOverlay>();
        private void ClosePanels() { _overlay?.CloseSilently(); _problem?.Close(); }
        private void ReleaseCards()
        { foreach (var card in _ownedCards) if (card != null) Destroy(card); _ownedCards.Clear(); }

        public void CloseSilently()
        {
            _active = _pending = _notified = _playingFeedback = false; _nodeId = null; _receipt = null;
            _onCommitted = null; _onCancelled = null; ClosePanels(); ReleaseCards();
            _lease?.Dispose(); _lease = null;
        }

        private void InterruptView()
        { var cancelled = _onCancelled; CloseSilently(); cancelled?.Invoke(); }

        private void LateUpdate()
        {
            if (!_active) return;
            if (_overlay != null && _overlay.IsShown && _overlay.Root.activeInHierarchy ||
                _problem != null && _problem.IsShown && _problem.Root.activeInHierarchy) return;
            if (_receipt == null) { InterruptView(); return; }
            // There may be a frame between durable selection and the host's
            // PlayCommitted call. Preserve that handoff; only a missing result
            // view ends already-started feedback. No gameplay is replayed.
            if (!_notified) NotifyCommitted();
            else if (_playingFeedback) CloseSilently();
        }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            var go = new GameObject("DuplicateOfferingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false); _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.overrideSorting = true; _canvas.sortingOrder = 32080;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; return _canvas;
        }
        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
