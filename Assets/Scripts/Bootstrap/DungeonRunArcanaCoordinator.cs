using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WanjieScroll.Core;
using WanjieScroll.Dungeon;
using WanjieScroll.Meta;
using WanjieScroll.UI.Rewards;

namespace WanjieScroll.Bootstrap
{
    /// <summary>
    /// Owns the Arcana panel and input lifecycle. Dungeon owns the frozen choices,
    /// acquisition proof and node completion; successful commands save before any
    /// selection animation. Closing the panel preserves an unresolved draft.
    /// </summary>
    public sealed class DungeonRunArcanaCoordinator : MonoBehaviour
    {
        private DungeonRunSession _run;
        private Func<DungeonRunArcanaCatalog> _getCatalog;
        private Func<IReadOnlyList<ArcanaConfig>> _getConfigs;
        private Action _persist;
        private Canvas _canvas;
        private RewardChoiceOverlay _choices;
        private RewardFeedbackPresenter _reveal;
        private IDisposable _flowLease;
        private string _nodeId;
        private Action<LevelInteractionReceipt> _onCommitted;
        private Action _onCancelled;
        private LevelInteractionReceipt _receipt;
        private bool _active, _actionPending;
        private int _generation;
        public bool IsActive => _active;

        public void Bind(DungeonRunSession run, Func<DungeonRunArcanaCatalog> getCatalog,
            Func<IReadOnlyList<ArcanaConfig>> getConfigs, Action persist)
        {
            CloseSilently();
            _run = run;
            _getCatalog = getCatalog;
            _getConfigs = getConfigs;
            _persist = persist;
        }

        public static bool Handles(LevelNode node)
        {
            if (node?.completionEffects == null) return false;
            foreach (var effect in node.completionEffects)
                if (effect?.kind == LevelInteractionEffectKind.ChooseArcana) return true;
            return false;
        }

        public bool TryBegin(string nodeId, Action<LevelInteractionReceipt> onCommitted,
            Action onCancelled, out string error)
        {
            error = null;
            if (_active || _run == null || _getCatalog == null || _getConfigs == null || _persist == null)
            {
                error = "天机选择尚未准备好，或已有选择正在处理。";
                return false;
            }
            _active = true;
            _nodeId = nodeId;
            _onCommitted = onCommitted;
            _onCancelled = onCancelled;
            _generation++;
            _flowLease = SceneTransitionRuntime.ExternalModalBarrier?.Hold("modal.map.arcana-choice");
            Reopen();
            return true;
        }

        private void Reopen()
        {
            if (!_active) return;
            _actionPending = false;
            try
            {
                if (!_run.Arcanas.TryOpen(_nodeId, _getCatalog(), out var offer,
                        out string error, _persist))
                {
                    ShowProblem(error);
                    return;
                }
                if (offer.HasReceipt)
                {
                    // A previous checkpoint may contain the acquisition receipt
                    // while its map node is still resolving. Complete it without regranting.
                    Commit(offer.Skipped ? null : offer.Receipt.TargetContentId);
                    return;
                }
                ClosePanels();
                var actions = new[]
                {
                    new RewardUtilityAction(RewardUtilityActionKind.Abandon, "跳过", () => Commit(null)),
                    new RewardUtilityAction(RewardUtilityActionKind.Abandon, "返回地图", Cancel)
                };
                if (offer.Candidates.Count == 0)
                {
                    _reveal ??= RewardFeedbackPresenter.Create(this);
                    _reveal.ShowDecision(EnsureCanvas(), new RewardFeedbackItem
                    {
                        Kind = RewardFeedbackKind.Unavailable,
                        Title = "选择天机",
                        DisplayName = "没有可选天机",
                        Description = "当前已解锁且尚未持有的天机为空。可以跳过本次选择。"
                    }, null, actions);
                    return;
                }
                var ids = new List<string>();
                var items = new List<RewardFeedbackItem>();
                foreach (var candidate in offer.Candidates)
                {
                    ids.Add(candidate.ContentId);
                    items.Add(CreateItem(candidate.ContentId, "选择天机"));
                }
                _choices ??= gameObject.AddComponent<RewardChoiceOverlay>();
                _choices.Show(EnsureCanvas(), "选择一项本局天机", items, actions,
                    index => { if (index >= 0 && index < ids.Count) Commit(ids[index], index); });
                GlobalButtonAudioRuntime.BindHierarchyNow(_choices.Root);
            }
            catch (Exception exception) { ShowProblem(exception.Message); }
        }

        private void Commit(string contentId, int selectedIndex = -1)
        {
            if (!_active || _actionPending) return;
            _actionPending = true;
            int generation = _generation;
            LevelInteractionReceipt receipt;
            try
            {
                string error;
                bool committed = contentId == null
                    ? _run.Arcanas.TrySkipAndComplete(_nodeId, out receipt, out error, _persist)
                    : _run.Arcanas.TryChooseAndComplete(_nodeId, contentId, out receipt, out error, _persist);
                if (!committed) { ShowProblem(error); return; }
            }
            catch (Exception exception) { ShowProblem(exception.Message); return; }
            _receipt = receipt;
            Action resolved = () =>
            {
                if (!_active || generation != _generation) return;
                ClosePanels();
                _actionPending = false;
                _onCommitted?.Invoke(_receipt);
            };
            try
            {
                if (_choices != null && _choices.IsShown &&
                    (selectedIndex >= 0 ? _choices.PlaySelectionAndClose(selectedIndex, resolved)
                        : _choices.PlayClose(resolved))) return;
                resolved();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DungeonRunArcana] 天机已保存，跳过失败的选择动画：" + exception.Message);
                resolved();
            }
        }

        /// <summary>Displays an already saved result; never grants or equips anything.</summary>
        public void PlayCommitted(Action onClosed)
        {
            Action finish = () => { CloseSilently(); onClosed?.Invoke(); };
            if (!_active || !_receipt.NewlyApplied || _receipt.Amount == 0) { finish(); return; }
            try
            {
                ClosePanels();
                var item = CreateItem(_receipt.TargetContentId, "获得本局天机");
                item.ReceiptId = _receipt.EffectId;
                _reveal ??= RewardFeedbackPresenter.Create(this);
                _reveal.Show(EnsureCanvas(), item, finish);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DungeonRunArcana] 天机已保存，结果反馈暂不可用：" + exception.Message);
                finish();
            }
        }

        private RewardFeedbackItem CreateItem(string contentId, string title)
        {
            foreach (var config in _getConfigs())
                if (config != null && config.contentId == contentId)
                    return new RewardFeedbackItem
                    {
                        Kind = RewardFeedbackKind.Generic, Title = title,
                        DisplayName = config.arcanaName, Description = config.description,
                        Quantity = 1
                    };
            throw new InvalidOperationException("天机表现内容缺失：" + contentId);
        }

        private void ShowProblem(string message)
        {
            if (!_active) return;
            _actionPending = false;
            ClosePanels();
            _reveal ??= RewardFeedbackPresenter.Create(this);
            _reveal.ShowDecision(EnsureCanvas(), new RewardFeedbackItem
            {
                Kind = RewardFeedbackKind.Unavailable, Title = "暂不可用",
                DisplayName = "天机选择暂未完成",
                Description = string.IsNullOrWhiteSpace(message) ? "请重试或返回地图。" : message
            }, null, new[]
            {
                new RewardUtilityAction(RewardUtilityActionKind.Refresh, "重试", Reopen),
                new RewardUtilityAction(RewardUtilityActionKind.Abandon, "返回地图", Cancel)
            });
        }

        private void Cancel()
        {
            if (!_active || _actionPending) return;
            Action cancelled = _onCancelled;
            CloseSilently();
            cancelled?.Invoke();
        }

        public void CloseSilently()
        {
            _generation++;
            _active = _actionPending = false;
            _nodeId = null;
            _onCommitted = null;
            _onCancelled = null;
            _receipt = default;
            ClosePanels();
            _flowLease?.Dispose();
            _flowLease = null;
        }

        private void ClosePanels() { _choices?.Close(); _reveal?.CloseSilently(); }

        private Canvas EnsureCanvas()
        {
            if (_canvas != null) return _canvas;
            var go = new GameObject("ArcanaChoiceCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 32080;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return _canvas;
        }

        private void OnDisable() => CloseSilently();
        private void OnDestroy() => CloseSilently();
    }
}
