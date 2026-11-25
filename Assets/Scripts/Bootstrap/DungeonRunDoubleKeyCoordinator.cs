using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns only the pre-payment two-selection UI and its temporary card views.
    /// Dungeon validates/removes both instances and checkpoints atomically; the
    /// existing interaction coordinator owns the subsequent seal disposition.
    /// </summary>
    public sealed class DungeonRunDoubleKeyCoordinator : MonoBehaviour
    {
        private DungeonRunSession _run;
        private Func<DungeonRunInteractionCatalog> _catalog;
        private Action _persist;
        private Canvas _canvas;
        private string _nodeId;
        private Action _paid;
        private Action _cancelled;
        private RewardChoiceOverlay _choices;
        private readonly List<CardConfig> _cards = new List<CardConfig>();
        private DungeonRunDoubleKeyPaymentOffer _offer;
        private int _firstId, _secondId, _generation;
        private bool _active, _submitting;

        public void Begin(DungeonRunSession run, string nodeId,
            Func<DungeonRunInteractionCatalog> catalog, Action persist,
            Canvas canvas, Action paid, Action cancelled)
        {
            CloseSilently();
            _run = run; _nodeId = nodeId; _catalog = catalog; _persist = persist;
            _canvas = canvas; _paid = paid; _cancelled = cancelled; _active = true;
            ResetSelection();
        }

        private void ResetSelection()
        {
            try
            {
                ReleaseCards();
                var catalog = _catalog();
                if (!_run.DoubleKeyPayments.TryInspect(_nodeId, catalog.Cards, out _offer, out string error))
                    throw new InvalidOperationException(error);
                if (_offer.IsPaid) { FinishPaid(); return; }
                if (!_run.TryGetCurrentCards(catalog.Cards, out var cards, out error))
                    throw new InvalidOperationException(error);
                _cards.AddRange(cards);
                _firstId = _secondId = 0;
                ShowCandidates(false, 0);
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private void ShowCandidates(bool second, int page)
        {
            if (!_active || _submitting) return;
            var candidates = second ? _offer.SecondCandidates : _offer.FirstCandidates;
            const int pageSize = 3;
            int lastPage = Math.Max(0, (candidates.Count - 1) / pageSize);
            page = Math.Max(0, Math.Min(page, lastPage));
            var visible = candidates.Skip(page * pageSize).Take(pageSize).ToList();
            var items = visible.Select(c => KeyCard(c.RuntimeInstanceId)).ToList();
            if (items.Count == 0)
                items.Add(new RewardFeedbackItem { Kind = RewardFeedbackKind.Unavailable,
                    DisplayName = "缺少合法钥牌", Description = "需要两种本色的持久卡牌；阳牌和灵印触发色不能代替。尚未支付，可以离开。" });
            var actions = new List<RewardUtilityAction>();
            int currentPage = page;
            if (page > 0) actions.Add(Action(RewardUtilityActionKind.Exclude, "上一页", () => ShowCandidates(second, currentPage - 1)));
            if (page < lastPage) actions.Add(Action(RewardUtilityActionKind.Refresh, "下一页", () => ShowCandidates(second, currentPage + 1)));
            if (second) actions.Add(Action(RewardUtilityActionKind.Refresh, "重选第一张", ResetSelection));
            actions.Add(Action(RewardUtilityActionKind.Abandon, "离开（尚未支付）", Cancel));
            string color = ColorName(second ? _offer.SecondColor : _offer.FirstColor);
            Show((second ? "第二把钥" : "第一把钥") + "：选择一张" + color + "色卡（确认后将连同附印移除）",
                items, actions, index =>
                {
                    if (index < 0 || index >= visible.Count) return;
                    if (second) { _secondId = visible[index].RuntimeInstanceId; ShowConfirmation(); }
                    else { _firstId = visible[index].RuntimeInstanceId; ShowCandidates(true, 0); }
                });
        }

        private void ShowConfirmation()
        {
            Show("确认献祭这两张实例及附印；支付后必须完成钤印或化玉",
                new[] { KeyCard(_firstId), KeyCard(_secondId) },
                new[] {
                    Action(RewardUtilityActionKind.Exclude, "确认移除两张", Pay),
                    Action(RewardUtilityActionKind.Refresh, "重新选择", ResetSelection),
                    Action(RewardUtilityActionKind.Abandon, "离开（尚未支付）", Cancel)
                }, _ => { });
        }

        private void Pay()
        {
            if (!_active || _submitting) return;
            _submitting = true;
            try
            {
                if (_persist == null) throw new InvalidOperationException("缺少原子存档回调，不能支付钥牌。");
                if (!_run.DoubleKeyPayments.TryPay(_run.CurrentFloorContentId, _nodeId,
                        _firstId, _secondId, _catalog().Cards, out _, out string error, _persist))
                    throw new InvalidOperationException(error);
                FinishPaid();
            }
            catch (Exception exception) { _submitting = false; ShowProblem(exception.Message); }
        }

        private RewardFeedbackItem KeyCard(int instanceId)
        {
            CardConfig card = _cards.FirstOrDefault(c => c.runtimeInstanceId == instanceId);
            if (card == null) throw new InvalidOperationException("钥牌视图已失效，请重新选择。");
            var item = RewardFeedbackItem.FromCard("key/" + instanceId, card);
            item.Title = "将移除此实例及附印";
            return item;
        }

        private void ShowProblem(string message)
        {
            if (!_active) return;
            Show("双钥尚未完成", new[] { new RewardFeedbackItem {
                    Kind = RewardFeedbackKind.Unavailable, DisplayName = "请重试", Description = message } },
                new[] { Action(RewardUtilityActionKind.Refresh, "重新检查", ResetSelection),
                    Action(RewardUtilityActionKind.Abandon, "返回", Cancel) }, _ => { });
        }

        private void Show(string title, IReadOnlyList<RewardFeedbackItem> items,
            IReadOnlyList<RewardUtilityAction> actions, Action<int> select)
        {
            int generation = ++_generation;
            _choices ??= gameObject.AddComponent<RewardChoiceOverlay>();
            // Utility callbacks are wrapped here, after this page's generation exists.
            var guarded = actions.Select(a => new RewardUtilityAction(a.Kind, a.Label,
                () => { if (_active && !_submitting && generation == _generation) a.Execute?.Invoke(); })).ToList();
            _choices.Show(_canvas, title, items, guarded,
                i => { if (_active && !_submitting && generation == _generation) select(i); },
                items.Any(item => item.Card != null) ? RewardCardTooltipContext.FromRun(_run, _catalog().Cards) : null);
            GlobalButtonAudioRuntime.BindHierarchyNow(_choices.Root);
        }

        private static RewardUtilityAction Action(RewardUtilityActionKind kind, string label, Action callback)
            => new RewardUtilityAction(kind, label, callback);
        private static string ColorName(CardColor color) => color switch {
            CardColor.Mu => "木", CardColor.Huo => "火", CardColor.Tu => "土",
            CardColor.Shui => "水", CardColor.Yin => "阴", _ => color.ToString() };
        private void FinishPaid() { Action callback = _paid; CloseSilently(); callback?.Invoke(); }
        private void Cancel()
        {
            if (!_active || _submitting) return;
            if (!_run.DoubleKeyPayments.RequiresPayment(_nodeId)) { FinishPaid(); return; }
            Action callback = _cancelled; CloseSilently(); callback?.Invoke();
        }
        public void CloseSilently()
        {
            _generation++; _active = _submitting = false;
            _choices?.Close(); ReleaseCards(); _paid = _cancelled = null;
            _firstId = _secondId = 0;
        }
        private void ReleaseCards()
        { foreach (CardConfig card in _cards) if (card != null) Destroy(card); _cards.Clear(); }
        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
