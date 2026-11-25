using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the Offering panel/input lifecycle. Dungeon owns frozen instances,
    /// cancellation, rewards and durable completion. Closing a view preserves
    /// the saved draft; explicit cancellation first saves the owner command.
    /// Existing choice/reveal surfaces are reused; no new effect asset is owned.
    /// Historical type name is retained; Chicken and Mana share this same view
    /// lifecycle, while the typed Dungeon commands choose the reward contract.
    /// </summary>
    public sealed class DungeonRunChickenOfferingCoordinator : MonoBehaviour
    {
        private DungeonRunSession _run;
        private IChickenOfferingContext _context;
        private Func<IReadOnlyList<CardConfig>> _getCards;
        private Action _persist;
        private Func<bool> _canPersist;
        private Canvas _canvas;
        private RewardChoiceOverlay _choices;
        private RewardFeedbackPresenter _reveal;
        private readonly List<CardConfig> _ownedCards = new List<CardConfig>();
        private IDisposable _flowLease;
        private string _nodeId;
        private Action<LevelInteractionReceipt> _onCommitted;
        private Action _onCancelled;
        private LevelInteractionReceipt _receipt;
        private CardOfferingKind _kind;
        private bool _active, _actionPending;
        private int _generation;
        private enum WatchedView { None, Choices, Reveal }
        private WatchedView _watchedView;
        private Action _onViewLost;
        public bool IsActive => _active;
        public bool CanBegin => _run != null && _context != null && _getCards != null &&
            _persist != null && _canPersist != null && _canPersist();

        public void Bind(DungeonRunSession run, IChickenOfferingContext context,
            Func<IReadOnlyList<CardConfig>> getCards, Action persist, Func<bool> canPersist)
        {
            CloseSilently();
            _run = run; _context = context; _getCards = getCards;
            _persist = persist; _canPersist = canPersist;
        }

        public static bool Handles(LevelNode node)
            => node?.completionEffects?.Any(effect => effect != null &&
                (effect.kind == LevelInteractionEffectKind.ChickenOffering || effect.kind == LevelInteractionEffectKind.ManaOffering)) == true;

        public bool TryBegin(string nodeId, Action<LevelInteractionReceipt> onCommitted,
            Action onCancelled, out string error)
        {
            error = null;
            if (_active || !CanBegin)
            { error = "供奉需要正式存档，或已有选择正在处理。"; return false; }
            LevelNode node = _run.CurrentFloor.FindNode(nodeId);
            if (!Handles(node)) { error = "节点没有类型化供奉合同。"; return false; }
            _kind = node.completionEffects.Any(effect => effect?.kind == LevelInteractionEffectKind.ManaOffering)
                ? CardOfferingKind.Mana : CardOfferingKind.Chicken;
            if (_kind == CardOfferingKind.Mana && !(_context is IManaOfferingContext))
            { error = "灵气供奉缺少真实资格上下文。"; return false; }
            _active = true; _nodeId = nodeId;
            _onCommitted = onCommitted; _onCancelled = onCancelled; _generation++;
            _flowLease = SceneTransitionRuntime.ExternalModalBarrier?.Hold("modal.map.card-offering");
            Reopen();
            return true;
        }

        private void Reopen()
        {
            if (!_active) return;
            _actionPending = false;
            try
            {
                if (!CanBegin) throw new InvalidOperationException("正式存档当前不可用。");
                DungeonRunChickenOfferingOffer offer;
                string error;
                bool opened = _kind == CardOfferingKind.Mana
                    ? _run.CardOfferings.TryOpenMana(_nodeId, (IManaOfferingContext)_context, out offer, out error, _persist)
                    : _run.CardOfferings.TryOpen(_nodeId, _context, out offer, out error, _persist);
                if (!opened)
                { ShowProblem(error); return; }
                _kind = offer.Kind;
                if (offer.HasReceipt) { _receipt = offer.Receipt; _onCommitted?.Invoke(_receipt); return; }
                ClosePanels(); ReleaseCards();
                var actions = new[] { new RewardUtilityAction(RewardUtilityActionKind.Abandon, "返回地图", Cancel) };
                if (offer.Candidates.Count == 0)
                {
                    _reveal ??= RewardFeedbackPresenter.Create(this);
                    _reveal.ShowDecision(EnsureCanvas(), new RewardFeedbackItem {
                        Kind = RewardFeedbackKind.Unavailable, Title = "供奉台", DisplayName = "没有可献出的卡牌",
                        Description = "当前牌组没有符合供奉条件的卡牌，可以返回地图。" }, null, actions);
                    WatchView(WatchedView.Reveal, CloseSilently);
                    return;
                }
                if (!_run.TryGetCurrentCards(_getCards(), out var current, out error))
                    throw new InvalidOperationException(error);
                _ownedCards.AddRange(current);
                var ids = new List<int>(); var items = new List<RewardFeedbackItem>();
                foreach (var candidate in offer.Candidates)
                {
                    var card = current.Single(item => item.runtimeInstanceId == candidate.RuntimeInstanceId);
                    ids.Add(candidate.RuntimeInstanceId);
                    items.Add(new RewardFeedbackItem { Kind = RewardFeedbackKind.Card, Title = "献出此牌",
                        DisplayName = card.cardName,
                        Description = _kind == CardOfferingKind.Mana
                            ? "从本局卡组移除此牌，领取灵气：当前灵气与基础灵气各加1，实际结果受上限约束。"
                            : $"从本局卡组移除此牌，获得 3 份食物，每份恢复 {ChickenOfferingContracts.HealingPerFood(_context.ExperimentalMedicineEnabled)} 点生命。",
                        Card = card, Quantity = 1 });
                }
                _choices ??= gameObject.AddComponent<RewardChoiceOverlay>();
                _choices.Show(EnsureCanvas(), "献出一张卡牌", items, actions,
                    index => { if (index >= 0 && index < ids.Count) Commit(ids[index], index); });
                GlobalButtonAudioRuntime.BindHierarchyNow(_choices.Root);
                WatchView(WatchedView.Choices, CloseSilently);
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private void Commit(int instanceId, int index)
        {
            if (!_active || _actionPending) return;
            _actionPending = true; int generation = _generation;
            try
            {
                if (!CanBegin) throw new InvalidOperationException("正式存档当前不可用。");
                string error;
                bool committed = _kind == CardOfferingKind.Mana
                    ? _run.CardOfferings.TrySubmitManaAndComplete(_nodeId, instanceId, (IManaOfferingContext)_context,
                        out _receipt, out error, _persist)
                    : _run.CardOfferings.TrySubmitAndComplete(_nodeId, instanceId, _context,
                        out _receipt, out error, _persist);
                if (!committed) { ShowProblem(error); return; }
            }
            catch (Exception exception) { ShowProblem(exception.Message); return; }
            bool feedbackResolved = false;
            Action resolved = () => {
                if (feedbackResolved || !_active || generation != _generation) return;
                feedbackResolved = true;
                ClosePanels(); ReleaseCards(); _actionPending = false; _onCommitted?.Invoke(_receipt);
            };
            WatchView(WatchedView.Choices, resolved);
            try
            {
                if (_choices != null && _choices.IsShown && _choices.PlaySelectionAndClose(index, resolved)) return;
                resolved();
            }
            catch (Exception exception)
            { Debug.LogWarning("[ChickenOffering] 供奉已保存，继续完成反馈：" + exception.Message); resolved(); }
        }

        private void Cancel()
        {
            if (!_active || _actionPending) return;
            try
            {
                if (!CanBegin) throw new InvalidOperationException("正式存档当前不可用。");
                if (!_run.CardOfferings.TryCancel(_nodeId, out string error, _persist))
                { ShowProblem(error); return; }
            }
            catch (Exception exception) { ShowProblem(exception.Message); return; }
            Action cancelled = _onCancelled; CloseSilently(); cancelled?.Invoke();
        }

        public void PlayCommitted(Action onClosed)
        {
            bool finished = false;
            Action finish = () => {
                if (finished) return;
                finished = true; CloseSilently(); onClosed?.Invoke();
            };
            if (!_active) { finish(); return; }
            try
            {
                ClosePanels(); ReleaseCards();
                _reveal ??= RewardFeedbackPresenter.Create(this);
                RewardFeedbackItem item = RewardFeedbackItem.FromInteractionReceipt(_receipt);
                bool manaOffering = _receipt.Kind == LevelInteractionEffectKind.ManaOffering;
                item.Title = "供奉完成";
                item.DisplayName = manaOffering ? "灵气已领取" : "获得 3 份食物";
                item.Description = manaOffering
                    ? $"当前灵气 {_receipt.ValueBefore} → {_receipt.ValueAfter}；基础灵气变化已结算。所选牌已从本局卡组移除。"
                    : _receipt.Amount > 0 ? $"恢复了 {_receipt.Amount} 点生命。" : "生命已满，食物已计入累计。";
                _reveal.Show(EnsureCanvas(), item, finish);
                WatchView(WatchedView.Reveal, finish);
            }
            catch (Exception exception)
            { Debug.LogWarning("[ChickenOffering] 供奉已保存，结果反馈暂不可用：" + exception.Message); finish(); }
        }

        private void ShowProblem(string error)
        {
            if (!_active) return;
            _actionPending = false; ClosePanels(); ReleaseCards();
            _reveal ??= RewardFeedbackPresenter.Create(this);
            _reveal.ShowDecision(EnsureCanvas(), new RewardFeedbackItem { Kind = RewardFeedbackKind.Unavailable,
                Title = "供奉暂未完成", DisplayName = "请重试或返回地图", Description = error }, null, new[] {
                new RewardUtilityAction(RewardUtilityActionKind.Refresh, "重试", Reopen),
                new RewardUtilityAction(RewardUtilityActionKind.Abandon, "返回地图", Cancel) });
            WatchView(WatchedView.Reveal, CloseSilently);
        }

        public void CloseSilently()
        {
            _generation++; _active = _actionPending = false; _nodeId = null;
            _onCommitted = null; _onCancelled = null; _receipt = default;
            ClosePanels(); ReleaseCards(); _flowLease?.Dispose(); _flowLease = null;
        }
        private void ClosePanels()
        {
            _watchedView = WatchedView.None; _onViewLost = null;
            _choices?.Close(); _reveal?.CloseSilently();
        }
        private void WatchView(WatchedView view, Action onLost)
        { _watchedView = view; _onViewLost = onLost; }
        private void LateUpdate()
        {
            if (!_active || _watchedView == WatchedView.None) return;
            bool visible = _watchedView == WatchedView.Choices
                ? _choices != null && _choices.isActiveAndEnabled && _choices.IsShown && _choices.Root.activeInHierarchy
                : _reveal != null && _reveal.isActiveAndEnabled && _reveal.IsShown && _reveal.Root.activeInHierarchy;
            if (visible) return;
            Action lost = _onViewLost;
            _watchedView = WatchedView.None; _onViewLost = null;
            lost?.Invoke();
        }
        private void ReleaseCards()
        { foreach (var card in _ownedCards) if (card != null) Destroy(card); _ownedCards.Clear(); }
        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            var go = new GameObject("ChickenOfferingCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false); _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.overrideSorting = true; _canvas.sortingOrder = 32080;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; return _canvas;
        }
        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
