using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.UI.Presentations;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>Owns the shared invitation modal and automatic result lifecycle.
    /// Dungeon alone owns frozen candidates, the free grant and recovery proof.
    /// There is no user cancel, wallet operation or combat activation here.</summary>
    public sealed class DungeonRunSpiritInvitationCoordinator : MonoBehaviour, IDungeonMapCardFacility
    {
        public LevelInteractionEffectKind EffectKind => LevelInteractionEffectKind.CrawlerInvitation;
        private enum Phase { Closed, Choosing, SelectionClosing, Result }
        private Phase _phase;
        private DungeonRunSession _run;
        private Func<IReadOnlyList<CardConfig>> _getCards;
        private Func<InvitationPreparedContext> _prepare;
        private Action _persist;
        private Func<bool> _canPersist;
        private Func<string, bool> _canAdmitCard;
        private InvitationOffer _offer;
        private InvitationReceipt _receipt;
        private Canvas _canvas;
        private RewardChoiceOverlay _choices;
        private SpiritInvitationRevealOverlay _reveal;
        private IDisposable _flowLease;
        private readonly List<CardConfig> _ownedCards = new List<CardConfig>();
        private Action<LevelInteractionReceipt> _onCommitted;
        private Action _onInterrupted, _onClosed;
        private string _nodeId;
        private bool _actionPending, _notified;
        private int _generation;

        public bool IsActive => _phase != Phase.Closed;
        public bool CanBegin => isActiveAndEnabled && _run != null && _getCards != null &&
            _prepare != null && _persist != null && _canPersist != null && _canAdmitCard != null && _canPersist();

        public void Bind(DungeonRunSession run, Func<IReadOnlyList<CardConfig>> getCards,
            Func<InvitationPreparedContext> prepare, Action persist, Func<bool> canPersist,
            Func<string, bool> canAdmitCard)
        {
            CloseSilently(); _run = run; _getCards = getCards; _prepare = prepare;
            _persist = persist; _canPersist = canPersist;
            _canAdmitCard = canAdmitCard;
        }

        public bool TryBegin(string nodeId, Action<LevelInteractionReceipt> onCommitted,
            Action onCancelled, out string error)
        {
            error = null;
            if (IsActive || !CanBegin) { error = "请灵需要当前可保存的探索，或已有选择正在处理。"; return false; }
            var node = _run.CurrentFloor.FindNode(nodeId);
            bool legacy = node?.completionEffects?.Count == 1 &&
                node.completionEffects[0]?.kind == LevelInteractionEffectKind.GunForHire;
            if (node?.completionEffects?.Count != 1 || (node.completionEffects[0]?.kind != EffectKind && !legacy))
            { error = "节点没有独立的请灵合同。"; return false; }
            if (legacy && !_run.Invitations.TryCanAdoptLegacy(nodeId, out error)) return false;
            _generation++; _nodeId = nodeId; _phase = Phase.Choosing;
            _onCommitted = onCommitted; _onInterrupted = onCancelled;
            _flowLease = SceneTransitionRuntime.ExternalModalBarrier?.Hold("modal.map.spirit-invitation");
            Reopen(); return true;
        }

        private void Reopen()
        {
            if (!IsActive) return;
            try
            {
                RequirePersistence();
                if (!_run.Invitations.TryInspect(_nodeId, out _offer, out var error))
                {
                    if (_run.Invitations.HasPendingChoice || _run.CurrentFloor.GetNodeState(_nodeId) == LevelNodeViewState.Completed)
                        throw new InvalidOperationException(error);
                    var context = _prepare();
                    if (!_run.Invitations.CanGenerate(context))
                    {
                        // A different event can consume the last lawful choice
                        // after source publication. This is no longer a choice
                        // to abandon, and must not trap the player in retry.
                        Debug.Log("[SpiritInvitation] 当前没有可应邀的英灵，返回地图。");
                        ReturnToMap(); return;
                    }
                    if (!_run.Invitations.TryOpen(_nodeId, context, out _offer, out error, _persist))
                        throw new InvalidOperationException(error);
                }
                if (_offer.AlreadyCompleted) { _receipt = _offer.Receipt; NotifyCommitted(); return; }
                ReleaseCards();
                _ownedCards.AddRange(InvitationCardProjection.InstantiateCandidates(_offer, _getCards()));
                var items = _ownedCards.Select(card => new RewardFeedbackItem {
                    Kind = RewardFeedbackKind.Card, Card = card, DisplayName = card.cardName,
                    Title = "请灵", Quantity = 1, DecisionHint = "加入本局牌组 · 下一场起手获得"
                }).ToArray();
                Choices().Show(EnsureCanvas(), "请灵 · 选择一位英灵同行", items, null,
                    index => Choose(index), RewardCardTooltipContext.FromRun(_run, _getCards()));
                GlobalButtonAudioRuntime.BindHierarchyNow(_choices.Root);
                _phase = Phase.Choosing; _actionPending = false;
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private void Choose(int index)
        {
            if (_phase != Phase.Choosing || _actionPending || index < 0 || index >= _offer.Candidates.Count) return;
            _actionPending = true;
            try
            {
                RequirePersistence();
                if (!_canAdmitCard(_offer.Candidates[index].Author.ContentId))
                    throw new InvalidOperationException("该召唤牌正在等待人工验收，请验收后重试。");
                if (!_run.Invitations.TryChooseAndComplete(_nodeId, _offer.Candidates[index].Author.ContentId,
                        _getCards(), out _receipt, out var error, _persist))
                    throw new InvalidOperationException(error);
                _phase = Phase.SelectionClosing;
                int generation = _generation;
                if (!_choices.PlaySelectionAndClose(index, () => {
                    if (generation == _generation && IsActive) NotifyCommitted(); })) NotifyCommitted();
            }
            catch (Exception exception)
            {
                if (_run.Invitations.TryInspect(_nodeId, out var saved, out _) && saved.AlreadyCompleted)
                { _receipt = saved.Receipt; NotifyCommitted(); }
                else ShowProblem(exception.Message);
            }
        }

        private void NotifyCommitted()
        {
            if (_notified || _receipt == null) return;
            _notified = true; _phase = Phase.SelectionClosing; _actionPending = false;
            _choices?.Close(); ReleaseCards();
            _onCommitted?.Invoke(_receipt.InteractionReceipt);
        }

        public void PlayCommitted(Action onClosed)
        {
            _onClosed = onClosed;
            if (!IsActive || _receipt == null) { Finish(); return; }
            try
            {
                if (!_run.TryGetCurrentCards(_getCards(), out var current, out var error))
                    throw new InvalidOperationException(error);
                _ownedCards.AddRange(current);
                var card = current.Single(value => value.runtimeInstanceId == _receipt.GrantedRuntimeInstanceId);
                if (!CharacterPresentationCatalog.TryGetHeroArrivalForSubject(_receipt.CrawlerContentId, out var actor))
                    throw new InvalidOperationException("缺少所选英灵的正式显形素材：" + _receipt.CrawlerContentId);
                _reveal ??= gameObject.AddComponent<SpiritInvitationRevealOverlay>();
                _phase = Phase.Result;
                _reveal.Show(EnsureCanvas(), actor, card, Finish);
            }
            catch (Exception exception)
            { Debug.LogWarning("[SpiritInvitation] 请灵已保存，跳过失败的显形反馈：" + exception.Message); Finish(); }
        }

        private void ShowProblem(string error)
        {
            _actionPending = false; _phase = Phase.Choosing;
            _choices?.Close(); ReleaseCards();
            Choices().Show(EnsureCanvas(), "请灵暂未完成", new[] {
                new RewardFeedbackItem { Kind = RewardFeedbackKind.Unavailable, DisplayName = "请重试", Description = error }
            }, new[] { new RewardUtilityAction(RewardUtilityActionKind.Refresh, "重试", Reopen),
                new RewardUtilityAction(RewardUtilityActionKind.Abandon, "返回地图", ReturnToMap) }, _ => Reopen());
            GlobalButtonAudioRuntime.BindHierarchyNow(_choices.Root);
        }

        private void ReturnToMap()
        { var interrupted = _onInterrupted; CloseSilently(); interrupted?.Invoke(); }

        private void RequirePersistence()
        { if (!CanBegin) throw new InvalidOperationException("当前探索暂不可保存，请稍后重试。"); }

        private void Finish()
        { var callback = _onClosed; CloseSilently(); callback?.Invoke(); }

        public void CloseSilently()
        {
            _generation++; _phase = Phase.Closed; _actionPending = _notified = false;
            _choices?.Close(); _reveal?.Close(); ReleaseCards();
            _flowLease?.Dispose(); _flowLease = null;
            _offer = null; _receipt = null; _nodeId = null;
            _onCommitted = null; _onInterrupted = _onClosed = null;
            // Never abandon or cancel the Dungeon draft when its view is lost.
        }

        private void LateUpdate()
        {
            if (!IsActive) return;
            if (_phase == Phase.Result) { if (_reveal == null || !_reveal.IsShown) Finish(); return; }
            if (_choices != null && _choices.IsShown && _choices.Root.activeInHierarchy) return;
            if (_phase == Phase.SelectionClosing) { NotifyCommitted(); return; }
            var interrupted = _onInterrupted; CloseSilently(); interrupted?.Invoke();
        }

        private RewardChoiceOverlay Choices() => _choices ??= gameObject.AddComponent<RewardChoiceOverlay>();
        private void ReleaseCards()
        { foreach (var card in _ownedCards) if (card != null) Destroy(card); _ownedCards.Clear(); }
        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            var go = new GameObject("SpiritInvitationCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false); _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.overrideSorting = true; _canvas.sortingOrder = 32080;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return _canvas;
        }
        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
