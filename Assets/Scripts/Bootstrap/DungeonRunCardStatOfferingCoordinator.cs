using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI;
using WanjieScroll.UI.Rewards;
using WanjieScroll.UI.Views;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns one CardStat modal/input lifecycle; Dungeon alone owns candidates,
    /// payment, actual stat deltas, cash, cancellation and durable completion.
    /// Uses only the shared RewardChoiceOverlay. Completed feedback closes
    /// automatically; view loss cannot grant again or pretend a draft completed.
    /// Field/method threshold review: all mutable fields belong to this one modal
    /// state machine, not a second gameplay or persistence owner. Reassess before
    /// adding another event kind; this coordinator never selects its own reward.
    /// </summary>
    public sealed class DungeonRunCardStatOfferingCoordinator : MonoBehaviour
    {
        private const int PageSize = 3;
        private enum Phase { None, Choosing, SelectionClosing, Result, ResultClosing }
        private DungeonRunSession _run;
        private Func<IReadOnlyList<CardConfig>> _getCards;
        private IDungeonRunFlatCashOutWallet _wallet;
        private Action _persist;
        private Func<bool> _canPersist;
        private Canvas _canvas;
        private RewardChoiceOverlay _choices;
        private readonly List<CardConfig> _ownedCards = new List<CardConfig>();
        private IDisposable _flowLease;
        private string _nodeId;
        private Action<LevelInteractionReceipt> _onCommitted;
        private Action _onCancelled, _onClosed;
        private CardStatOfferingReceipt _receipt;
        private CardStatOfferingOffer _offer;
        private Coroutine _autoClose;
        private Phase _phase;
        private bool _actionPending, _notified;
        private int _page, _generation;

        public bool IsActive => _phase != Phase.None;
        public bool CanBegin => isActiveAndEnabled && _run != null && _getCards != null &&
            _wallet != null && _persist != null && _canPersist != null && _canPersist();

        public void Bind(DungeonRunSession run, Func<IReadOnlyList<CardConfig>> getCards,
            IDungeonRunFlatCashOutWallet wallet, Action persist, Func<bool> canPersist)
        {
            CloseSilently();
            _run = run; _getCards = getCards; _wallet = wallet;
            _persist = persist; _canPersist = canPersist;
        }

        public static bool Handles(LevelNode node) => node?.completionEffects?.Count == 1 &&
            node.completionEffects[0]?.kind == LevelInteractionEffectKind.CardStatOffering;

        public bool TryBegin(string nodeId, Action<LevelInteractionReceipt> onCommitted,
            Action onCancelled, out string error)
        {
            error = null;
            if (IsActive || !CanBegin)
            { error = "属性供奉需要当前可保存的探索，或已有选择正在处理。"; return false; }
            if (!Handles(_run.CurrentFloor.FindNode(nodeId)))
            { error = "节点没有独立的属性供奉合同。"; return false; }
            _phase = Phase.Choosing; _nodeId = nodeId; _page = 0; _generation++;
            _onCommitted = onCommitted; _onCancelled = onCancelled;
            _flowLease = SceneTransitionRuntime.ExternalModalBarrier?.Hold("modal.map.card-stat-offering");
            Reopen();
            return true;
        }

        private void Reopen()
        {
            if (!IsActive) return;
            try
            {
                RequirePersistence();
                if (!_run.CardStatOfferings.TryRetryRecovery(out var error))
                    throw new InvalidOperationException(error);
                // Restored pending/completed journals never consult today's
                // candidate provider, qualification or source random stream.
                if (!_run.CardStatOfferings.TryInspect(_nodeId, out _offer, out error))
                {
                    if (_run.CardStatOfferings.HasPendingChoice ||
                        _run.CurrentFloor.GetNodeState(_nodeId) == LevelNodeViewState.Completed)
                        throw new InvalidOperationException(error);
                    if (!DungeonRunCardStatOfferingSource.TryPrepare(_run, _nodeId, _getCards(), out var context, out error) ||
                        !_run.CardStatOfferings.TryOpen(_nodeId, context, out _offer, out error, _persist))
                        throw new InvalidOperationException(error);
                }
                if (_offer.AlreadyCompleted)
                { _receipt = _offer.Receipt; NotifyCommitted(); return; }
                if (!_offer.HasPendingChoice) throw new InvalidOperationException("属性供奉没有可恢复的待选记录。");
                _actionPending = false;
                ShowPage(_page);
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private void ShowPage(int page)
        {
            ClosePanel(); ReleaseCards();
            _phase = Phase.Choosing;
            var actions = new List<RewardUtilityAction>();
            var items = new List<RewardFeedbackItem>();
            var ids = new List<int>();
            bool empty = _offer.Candidates.Count == 0;
            if (empty)
            {
                items.Add(RewardFeedbackItem.FromJade(_nodeId + ".cash", 200,
                    "无可供奉卡牌，无需献牌。"));
            }
            else
            {
                if (!_run.TryGetCurrentCards(_getCards(), out var current, out var error))
                    throw new InvalidOperationException(error);
                _ownedCards.AddRange(current);
                int last = (_offer.Candidates.Count - 1) / PageSize;
                _page = Math.Max(0, Math.Min(page, last));
                var presentation = CardPresentationProfile.LoadDefault();
                foreach (var candidate in _offer.Candidates.Skip(_page * PageSize).Take(PageSize))
                {
                    var card = current.Single(value => value.runtimeInstanceId == candidate.RuntimeInstanceId);
                    string gains = string.Join("、", candidate.RequestedRewards.Select(value => FormatStat(value.SourceStat, value.RequestedAmount)));
                    ids.Add(candidate.RuntimeInstanceId);
                    items.Add(new RewardFeedbackItem { Kind = RewardFeedbackKind.Card, Title = "献出此牌",
                        DisplayName = card.cardName, Card = card,
                        Artwork = card.cardArt ?? presentation?.GetArtwork(card.contentId), Quantity = 1,
                        DecisionHint = "献出此牌｜本次探索：" + gains,
                        Description = "从本局卡组移除此牌。\n本次探索：" + gains + "。" });
                }
                if (_page > 0) actions.Add(new RewardUtilityAction(RewardUtilityActionKind.Exclude,
                    "上一页", () => ChangePage(_page - 1)));
                if (_page < last) actions.Add(new RewardUtilityAction(RewardUtilityActionKind.Refresh,
                    "下一页", () => ChangePage(_page + 1)));
                actions.Add(new RewardUtilityAction(RewardUtilityActionKind.CashOut,
                    "不献牌 · 领取200灵玉", () => Commit(0, -1)));
            }
            actions.Add(new RewardUtilityAction(RewardUtilityActionKind.Abandon, "返回地图", Cancel));
            string title = empty ? "属性供奉 · 直接领取灵玉" : "献出一张卡牌 · 获得本次探索属性";
            if (!empty && _offer.Candidates.Count > PageSize)
                title += $"（{_page + 1}/{(_offer.Candidates.Count + PageSize - 1) / PageSize}）";
            Overlay().Show(EnsureCanvas(), title, items, actions, index =>
            {
                if (empty) Commit(0, index);
                else if (index >= 0 && index < ids.Count) Commit(ids[index], index);
            });
            GlobalButtonAudioRuntime.BindHierarchyNow(_choices.Root);
        }

        private void ChangePage(int page)
        {
            if (!IsActive || _actionPending) return;
            try { ShowPage(page); }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private void Commit(int instanceId, int selectedIndex)
        {
            if (!IsActive || _actionPending || _phase != Phase.Choosing) return;
            _actionPending = true;
            try
            {
                RequirePersistence();
                bool succeeded = instanceId == 0
                    ? _run.CardStatOfferings.TryCashOutAndComplete(_nodeId, _wallet, out _receipt, out var error, _persist)
                    : _run.CardStatOfferings.TrySubmitAndComplete(_nodeId, instanceId, out _receipt, out error, _persist);
                if (!succeeded) throw new InvalidOperationException(error);
                _phase = Phase.SelectionClosing;
                int generation = _generation;
                Action finish = () => { if (generation == _generation && IsActive) NotifyCommitted(); };
                bool animated = selectedIndex >= 0
                    ? _choices.PlaySelectionAndClose(selectedIndex, finish) : _choices.PlayClose(finish);
                if (!animated) finish();
            }
            catch (Exception exception)
            {
                // A presentation failure after a durable commit cannot return
                // the player to a fresh payment choice or grant the reward again.
                if (_run.CardStatOfferings.TryInspect(_nodeId, out var saved, out _) && saved.AlreadyCompleted)
                { _receipt = saved.Receipt; NotifyCommitted(); }
                else ShowProblem(exception.Message);
            }
        }

        private void NotifyCommitted()
        {
            if (_notified || _receipt == null) return;
            _notified = true; ClosePanel(); ReleaseCards();
            _actionPending = false; _phase = Phase.SelectionClosing;
            _onCommitted?.Invoke(_receipt.InteractionReceipt);
        }

        public void PlayCommitted(Action onClosed)
        {
            _onClosed = onClosed;
            if (!IsActive || _receipt == null) { FinishResult(); return; }
            try
            {
                RewardFeedbackItem item;
                if (_receipt.Disposition == CardStatOfferingDisposition.Cash)
                    item = RewardFeedbackItem.FromJade(_receipt.InteractionReceipt.EffectId,
                        _receipt.InteractionReceipt.Amount, "本次未献出卡牌。");
                else
                {
                    var changes = DungeonRunCardStatOfferingSource.ReadAppliedChanges(_receipt);
                    string gains = string.Join("、", changes
                        .Select(value => FormatStat(value.SourceStat, value.Amount)));
                    Sprite artwork = changes[0].SourceStat switch {
                        2 => RewardArtworkCatalog.ResolveInteractionBuff(LevelInteractionEffectKind.AddBaseArmor),
                        10 => RewardArtworkCatalog.ResolveInteractionBuff(LevelInteractionEffectKind.AddHandLimit),
                        _ => null };
                    item = new RewardFeedbackItem { Kind = RewardFeedbackKind.Stat, Title = "供奉完成",
                        DisplayName = gains, Description = "所选牌已从本局卡组移除，属性在本次探索中生效。", Quantity = 1,
                        Artwork = artwork };
                }
                ClosePanel(); ReleaseCards(); _phase = Phase.Result;
                Overlay().Show(EnsureCanvas(), "供奉完成", new[] { item }, null, _ => CloseResult());
                GlobalButtonAudioRuntime.BindHierarchyNow(_choices.Root);
                _autoClose = StartCoroutine(CloseAfterPause(_generation));
            }
            catch (Exception exception)
            { Debug.LogWarning("[CardStatOffering] 收益已保存，结束失败的结果反馈：" + exception.Message); FinishResult(); }
        }

        private IEnumerator CloseAfterPause(int generation)
        {
            float elapsed = 0f;
            while (elapsed < 1.2f && generation == _generation && _phase == Phase.Result)
            { elapsed += Time.unscaledDeltaTime; yield return null; }
            _autoClose = null;
            if (generation == _generation && _phase == Phase.Result) CloseResult();
        }

        private void CloseResult()
        {
            if (_phase != Phase.Result) return;
            _phase = Phase.ResultClosing;
            int generation = _generation;
            if (_choices == null || !_choices.PlayClose(() => { if (generation == _generation) FinishResult(); }))
                FinishResult();
        }

        private void FinishResult()
        { Action closed = _onClosed; CloseSilently(); closed?.Invoke(); }

        private void Cancel()
        {
            if (!IsActive || _actionPending) return;
            try
            {
                if (_receipt != null) { NotifyCommitted(); return; }
                if (_run.CardStatOfferings.TryInspect(_nodeId, out var saved, out _) && saved.AlreadyCompleted)
                { _receipt = saved.Receipt; NotifyCommitted(); return; }
                // A failed initial preparation may never have opened an owner.
                // No save/cancel command is needed to close this untouched view;
                // importantly a malformed source cannot trap its Return button.
                var floor = _run.CurrentFloor;
                if (!_run.CardStatOfferings.HasPendingChoice &&
                    string.IsNullOrEmpty(floor.ActiveInteractionNodeId) &&
                    floor.GetNodeState(_nodeId) == LevelNodeViewState.Available)
                {
                    Action untouched = _onCancelled; CloseSilently(); untouched?.Invoke(); return;
                }
                RequirePersistence();
                if (!_run.CardStatOfferings.TryLeave(_nodeId, out var error, _persist))
                    throw new InvalidOperationException(error);
                Action cancelled = _onCancelled; CloseSilently(); cancelled?.Invoke();
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private void ShowProblem(string error)
        {
            if (!IsActive) return;
            _actionPending = false; ClosePanel(); ReleaseCards(); _phase = Phase.Choosing;
            Overlay().Show(EnsureCanvas(), "供奉暂未完成", new[] {
                new RewardFeedbackItem { Kind = RewardFeedbackKind.Unavailable, DisplayName = "请重试或返回地图", Description = error }
            }, new[] { new RewardUtilityAction(RewardUtilityActionKind.Refresh, "重试", Reopen),
                new RewardUtilityAction(RewardUtilityActionKind.Abandon, "返回地图", Cancel) }, _ => Reopen());
            GlobalButtonAudioRuntime.BindHierarchyNow(_choices.Root);
        }

        private void RequirePersistence()
        { if (!CanBegin) throw new InvalidOperationException("当前地图的正式存档暂不可用，请稍后重试。"); }

        private static string FormatStat(int sourceStat, int amount)
        {
            // Source IDs identify reward display facts, not project enum ordinals.
            if (sourceStat == 2) return "每回合护甲 +" + amount;
            if (sourceStat == 13) return "还魂 +" + amount + "%";
            StatType stat = sourceStat switch {
                0 => StatType.BengLie, 1 => StatType.Amount, 4 => StatType.FengRui,
                5 => StatType.MingWu, 6 => StatType.LianYu, 7 => StatType.TianYun,
                10 => StatType.ShouPai, 11 => StatType.ChouPai, 17 => StatType.ZhaoSha,
                _ => throw new InvalidOperationException("属性收益没有对应的显示名称。") };
            return PowerUpGlyphConfig.FormatDelta(stat, amount);
        }

        public void CloseSilently()
        {
            _generation++;
            if (_autoClose != null) StopCoroutine(_autoClose);
            _autoClose = null; _phase = Phase.None; _actionPending = _notified = false;
            _onCommitted = null; _onCancelled = _onClosed = null; _nodeId = null; _receipt = null; _offer = null;
            ClosePanel(); ReleaseCards(); _flowLease?.Dispose(); _flowLease = null;
            // No TryLeave here: a scene/domain interruption must preserve the
            // already-saved pending owner for reopening after restore.
        }

        private void ClosePanel() => _choices?.Close();
        private RewardChoiceOverlay Overlay() => _choices ??= gameObject.AddComponent<RewardChoiceOverlay>();
        private void ReleaseCards()
        { foreach (var card in _ownedCards) if (card != null) Destroy(card); _ownedCards.Clear(); }
        private void LateUpdate()
        {
            if (!IsActive || (_choices != null && _choices.IsShown && _choices.isActiveAndEnabled && _choices.Root.activeInHierarchy)) return;
            if (_phase == Phase.Choosing) Cancel();
            else if (_phase == Phase.SelectionClosing) NotifyCommitted();
            else FinishResult();
        }
        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            var go = new GameObject("CardStatOfferingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false); _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.overrideSorting = true; _canvas.sortingOrder = 32080;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; return _canvas;
        }
        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
